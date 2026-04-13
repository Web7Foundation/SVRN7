#Requires -Version 7.0
<#
.SYNOPSIS
    SVRN7 Notifications LOBE — net-new DIDComm notification protocol.

.DESCRIPTION
    Implements the did:drn:svrn7.net/protocols/notification/1.0/* DIDComm protocol.
    Dispatches alerts to the UX LOBE when internal TDA events fire.
    Fired by internal events, not by inbound DIDComm messages.

    Derived from: Notifications LOBE (Agent 1 LOBE) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    Protocol URIs:
        did:drn:svrn7.net/protocols/notification/1.0/alert — outbound alert to citizen UX

    Trigger events (Epoch 0):
        BalanceChange        — citizen SVRN7 balance changed
        VcExpiry             — VC within 7 days of expiry
        InboxDepth           — inbox pending count exceeds threshold
        OverdraftCeiling     — society wallet below CitizenEndowmentGrana threshold
        TransferComplete     — a transfer settled successfully
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:InboxDepthThreshold   = 100   # pending messages before alerting
$script:VcExpiryWarningDays   = 7

# ── Invoke-TdaNotification ────────────────────────────────────────────────────

function Invoke-TdaNotification {
    <#
    .SYNOPSIS
        Processes an inbound notification/1.0/alert message.

    .DESCRIPTION
        Resolves the inbox message and logs the alert. Inbound notifications
        are rare (peer TDAs alerting this TDA). Most notifications flow outbound.

        Protocol: did:drn:svrn7.net/protocols/notification/1.0/alert

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — the alert record.

    .EXAMPLE
        Invoke-TdaNotification -MessageDid "did:drn:alpha.svrn7.net/inbox/msg/5f43a2..."
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg  = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { Write-Warning "Notifications LOBE: $MessageDid not found."; return $null }

        $body = $msg.PackedPayload | ConvertFrom-Json
        Write-Verbose "Notifications LOBE: alert received — type=$($body.alertType) severity=$($body.severity)"

        return @{
            MessageDid  = $MessageDid
            AlertType   = $body.alertType
            Severity    = $body.severity
            Message     = $body.message
            ResourceDid = $body.resourceDid
            ReceivedAt  = [datetimeoffset]::UtcNow.ToString('o')
        }
    }
}

# ── Send-TdaAlert ─────────────────────────────────────────────────────────────

function Send-TdaAlert {
    <#
    .SYNOPSIS
        Dispatches a notification/1.0/alert to a citizen's UX endpoint.

    .PARAMETER RecipientDid
        The citizen or society DID to notify.

    .PARAMETER AlertType
        One of: BalanceChange | VcExpiry | InboxDepth | OverdraftCeiling | TransferComplete | Custom

    .PARAMETER Severity
        One of: Info | Warning | Critical

    .PARAMETER Message
        Human-readable alert message.

    .PARAMETER ResourceDid
        Optional DID URL of the resource that triggered the alert.

    .OUTPUTS
        Hashtable — OutboundMessage for the Switchboard.

    .EXAMPLE
        Send-TdaAlert -RecipientDid "did:drn:alice.alpha.svrn7.net" `
                      -AlertType BalanceChange -Severity Info `
                      -Message "Your balance changed by 500 grana."
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RecipientDid,
        [Parameter(Mandatory)]
        [ValidateSet('BalanceChange','VcExpiry','InboxDepth',
                     'OverdraftCeiling','TransferComplete','Custom')]
        [string] $AlertType,
        [Parameter(Mandatory)]
        [ValidateSet('Info','Warning','Critical')]
        [string] $Severity,
        [Parameter(Mandatory)] [string] $Message,
        [string] $ResourceDid
    )

    process {
        $mySocietyDid = $SVRN7.Driver.GetSocietyDidAsync().GetAwaiter().GetResult()

        $payload = @{
            from        = $mySocietyDid
            to          = $RecipientDid
            alertType   = $AlertType
            severity    = $Severity
            message     = $Message
            resourceDid = $ResourceDid
            issuedAt    = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Compress

        $endpoint = Resolve-TdaEndpoint -Did $RecipientDid
        return @{
            PeerEndpoint  = $endpoint
            PackedMessage = $payload
            MessageType   = 'did:drn:svrn7.net/protocols/notification/1.0/alert'
        }
    }
}

# ── Test-TdaInboxDepth ────────────────────────────────────────────────────────

function Test-TdaInboxDepth {
    <#
    .SYNOPSIS
        Checks if the inbox pending count exceeds the threshold and fires an alert if so.

    .DESCRIPTION
        Called periodically by the Switchboard sweep. If the pending count exceeds
        $script:InboxDepthThreshold, sends an alert to the Society DID.

    .OUTPUTS
        None (alert dispatched internally via Send-TdaAlert if threshold exceeded).

    .EXAMPLE
        Test-TdaInboxDepth
    #>
    [CmdletBinding()]
    param()

    process {
        $counts  = $SVRN7.Inbox.GetStatusCountsAsync().GetAwaiter().GetResult()
        $pending = if ($counts.ContainsKey(0)) { $counts[0] } else { 0 }  # 0 = Pending

        if ($pending -gt $script:InboxDepthThreshold) {
            $societyDid = $SVRN7.Driver.GetSocietyDidAsync().GetAwaiter().GetResult()
            Send-TdaAlert -RecipientDid $societyDid `
                          -AlertType InboxDepth `
                          -Severity Warning `
                          -Message "Inbox depth is $pending messages (threshold: $($script:InboxDepthThreshold))."
        }
    }
}

# ── Helpers ───────────────────────────────────────────────────────────────────

function Resolve-TdaEndpoint {
    param([string] $Did)
    $doc = $SVRN7.Driver.ResolveDidDocumentAsync($Did).GetAwaiter().GetResult()
    $svc = $doc.Service | Where-Object { $_.type -eq 'DIDComm' } | Select-Object -First 1
    if (-not $svc) { throw "No DIDComm service endpoint for $Did" }
    return $svc.serviceEndpoint
}

Export-ModuleMember -Function @(
    'Invoke-TdaNotification',
    'Send-TdaAlert',
    'Test-TdaInboxDepth'
)
