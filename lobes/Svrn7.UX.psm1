#Requires -Version 7.0
<#
.SYNOPSIS
    SVRN7 UX LOBE — Platform-specific user interface adapter for TDA interactions.

.DESCRIPTION
    The UX LOBE is the boundary between the TDA's internal DIDComm ecosystem and
    the citizen-facing user interface layer. It translates TDA events into UI
    notifications, renders wallet balance updates, and relays citizen-initiated
    actions into the appropriate DIDComm pipelines.

    In the DSA 0.24 PPML diagram, the UX LOBE sits outside the TDA Host (element
    type: LOBE) adjacent to the DEVICE element. It is the only LOBE that communicates
    with a DEVICE; all other LOBEs communicate exclusively TDA-to-TDA via DIDComm.

    Derived from: UX LOBE (LOBE element type) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    Protocol URIs handled (outbound — TDA → UX layer):
        did:drn:svrn7.net/protocols/ux/1.0/balance-update     — wallet balance changed
        did:drn:svrn7.net/protocols/ux/1.0/notification       — surface an alert to the citizen
        did:drn:svrn7.net/protocols/ux/1.0/registration-complete — onboarding confirmed

    Protocol URIs handled (inbound — UX layer → TDA):
        did:drn:svrn7.net/protocols/ux/1.0/transfer-intent     — citizen initiated a transfer
        did:drn:svrn7.net/protocols/ux/1.0/registration-intent — citizen initiated onboarding

    Epoch 0: platform-specific rendering is stubbed. Implementors replace
    the Render-* functions with platform-appropriate UI calls (CLI, web, mobile).
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Render-TdaBalanceUpdate ───────────────────────────────────────────────────

function Render-TdaBalanceUpdate {
    <#
    .SYNOPSIS
        Renders a wallet balance update to the citizen-facing UX device.

    .DESCRIPTION
        Processes an inbound ux/1.0/balance-update message from the TDA and
        surfaces the new balance to the citizen via the platform-specific UI layer.

        Derived from: UX LOBE — DSA 0.24 Epoch 0 (PPML).
        Protocol: did:drn:svrn7.net/protocols/ux/1.0/balance-update

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — { CitizenDid, BalanceGrana, BalanceSvrn7, UpdatedAt }
        or $null if message could not be processed.

    .EXAMPLE
        Render-TdaBalanceUpdate -MessageDid "did:drn:alpha.svrn7.net/inbox/msg/5f43a2..."
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { Write-Warning "UX LOBE: $MessageDid not found."; return $null }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop

        $record = @{
            CitizenDid    = $body.citizenDid
            BalanceGrana  = $body.balanceGrana
            BalanceSvrn7  = [decimal]$body.balanceGrana / 1000000M
            UpdatedAt     = [datetimeoffset]::UtcNow.ToString('o')
        }

        # Platform-specific rendering — replace with actual UI call.
        # Epoch 0: writes to console. Production: push notification, web socket, etc.
        Write-Host ("UX: Balance update for {0} — {1:F6} SRC ({2} grana)" -f
            $record.CitizenDid,
            $record.BalanceSvrn7,
            $record.BalanceGrana) -ForegroundColor Cyan

        return $record
    }
}

# ── Render-TdaNotification ────────────────────────────────────────────────────

function Render-TdaNotification {
    <#
    .SYNOPSIS
        Surfaces a TDA alert notification to the citizen UX device.

    .DESCRIPTION
        Translates a notification/1.0/alert or ux/1.0/notification message into
        a platform-specific UI notification. The UX LOBE is the adapter boundary;
        the rendering implementation is platform-specific.

        Protocol: did:drn:svrn7.net/protocols/ux/1.0/notification

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — { AlertType, Severity, Message, ResourceDid, RenderedAt }

    .EXAMPLE
        Render-TdaNotification -MessageDid "did:drn:alpha.svrn7.net/inbox/msg/5f43a2..."
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { Write-Warning "UX LOBE: $MessageDid not found."; return $null }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop

        $record = @{
            AlertType   = $body.alertType
            Severity    = $body.severity
            Message     = $body.message
            ResourceDid = $body.resourceDid
            RenderedAt  = [datetimeoffset]::UtcNow.ToString('o')
        }

        # Platform-specific rendering — severity maps to UI treatment.
        $colour = switch ($body.severity) {
            'Critical' { 'Red' }
            'Warning'  { 'Yellow' }
            default    { 'White' }
        }
        Write-Host ("UX [{0}]: {1} — {2}" -f
            $body.severity, $body.alertType, $body.message) -ForegroundColor $colour

        return $record
    }
}

# ── Render-TdaRegistrationComplete ───────────────────────────────────────────

function Render-TdaRegistrationComplete {
    <#
    .SYNOPSIS
        Renders a successful citizen registration confirmation to the UX device.

    .DESCRIPTION
        Called when onboard/1.0/receipt arrives with success=true.
        Surfaces the citizen's new DID, Society membership, and endowment amount.

        Protocol: did:drn:svrn7.net/protocols/ux/1.0/registration-complete

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — { CitizenDid, SocietyDid, EndowmentGrana, EndowmentSvrn7, RegisteredAt }

    .EXAMPLE
        Render-TdaRegistrationComplete -MessageDid "did:drn:alpha.svrn7.net/inbox/msg/..."
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { Write-Warning "UX LOBE: $MessageDid not found."; return $null }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop

        if (-not $body.success) {
            Write-Warning ("UX LOBE: registration failed — {0}" -f $body.error)
            return $null
        }

        $svrn7 = [decimal]$body.endowmentGrana / 1000000M
        $record = @{
            CitizenDid     = $body.citizenDid
            SocietyDid     = $body.societyDid
            EndowmentGrana = $body.endowmentGrana
            EndowmentSvrn7 = $svrn7
            RegisteredAt   = [datetimeoffset]::UtcNow.ToString('o')
        }

        Write-Host "UX: Welcome to $($body.societyDid)!" -ForegroundColor Green
        Write-Host "    DID:       $($body.citizenDid)" -ForegroundColor Green
        Write-Host ("    Endowment: {0:F6} SRC ({1} grana)" -f $svrn7, $body.endowmentGrana) -ForegroundColor Green

        return $record
    }
}

# ── New-TdaTransferIntent ─────────────────────────────────────────────────────

function New-TdaTransferIntent {
    <#
    .SYNOPSIS
        Packages a citizen-initiated transfer into a DIDComm transfer/1.0/request.

    .DESCRIPTION
        Called by the UX layer when a citizen submits a transfer via the UI.
        Constructs the outbound DIDComm message for the Switchboard to deliver
        to the Society TDA for execution.

        Protocol: did:drn:svrn7.net/protocols/ux/1.0/transfer-intent (inbound UX)
                  → did:drn:svrn7.net/protocols/transfer/1.0/request (outbound DIDComm)

    .PARAMETER PayerDid
        Payer citizen DID.

    .PARAMETER PayeeDid
        Payee citizen DID.

    .PARAMETER AmountGrana
        Amount in grana (integer).

    .PARAMETER Memo
        Optional memo (max 256 chars).

    .OUTPUTS
        Hashtable — OutboundMessage for the Switchboard.

    .EXAMPLE
        New-TdaTransferIntent -PayerDid $from -PayeeDid $to -AmountGrana 500000
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory)] [string] $PayerDid,
        [Parameter(Mandatory)] [string] $PayeeDid,
        [Parameter(Mandatory)] [long]   $AmountGrana,
        [string] $Memo = ''
    )

    process {
        if ($AmountGrana -le 0) {
            throw "UX LOBE: AmountGrana must be greater than zero."
        }

        $payload = @{
            type        = 'did:drn:svrn7.net/protocols/transfer/1.0/request'
            from        = $PayerDid
            to          = $PayeeDid
            body        = @{
                payerDid    = $PayerDid
                payeeDid    = $PayeeDid
                amountGrana = $AmountGrana
                memo        = $Memo
                nonce       = [guid]::NewGuid().ToString('N')
                timestamp   = [datetimeoffset]::UtcNow.ToString('o')
            }
        } | ConvertTo-Json -Depth 5 -Compress

        $societyDid   = $SVRN7.Driver.GetSocietyDidAsync().GetAwaiter().GetResult()
        $societyDoc   = $SVRN7.Driver.ResolveDidDocumentAsync($societyDid).GetAwaiter().GetResult()
        $peerEndpoint = ($societyDoc.Service |
            Where-Object { $_.type -eq 'DIDComm' } |
            Select-Object -First 1).serviceEndpoint

        return @{
            PeerEndpoint  = $peerEndpoint
            PackedMessage = $payload
            MessageType   = 'did:drn:svrn7.net/protocols/transfer/1.0/request'
        }
    }
}

Export-ModuleMember -Function @(
    'Render-TdaBalanceUpdate',
    'Render-TdaNotification',
    'Render-TdaRegistrationComplete',
    'New-TdaTransferIntent'
)
