#Requires -Version 7.0
<#
.SYNOPSIS
    SVRN7 Presence LOBE — net-new DIDComm presence protocol.

.DESCRIPTION
    Implements the did:drn:svrn7.net/protocols/presence/1.0/* DIDComm protocol.
    Publishes and receives TDA availability status across the VTC7 mesh.
    Presence state is held in IMemoryCache (TTL 5 minutes).

    Derived from: Presence LOBE (Agent 1 LOBE) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    Protocol URIs:
        did:drn:svrn7.net/protocols/presence/1.0/status      — publish/receive status
        did:drn:svrn7.net/protocols/presence/1.0/subscribe   — subscribe to peer status
        did:drn:svrn7.net/protocols/presence/1.0/unsubscribe — cancel subscription

    Status values: Available | Busy | Away | Offline
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:PresenceCacheTtl      = [TimeSpan]::FromMinutes(5)
$script:SubscriptionCacheKey  = 'presence:subscriptions'

# ── Update-TdaPresence ────────────────────────────────────────────────────────

function Update-TdaPresence {
    <#
    .SYNOPSIS
        Processes an inbound presence/1.0/status message and updates the IMemoryCache.

    .DESCRIPTION
        Resolves the inbox message, extracts the sender's status and TTL,
        and sets the presence cache entry for the sender's DID.

        Protocol: did:drn:svrn7.net/protocols/presence/1.0/status

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — { Did, Status, Since, CachedUntil }

    .EXAMPLE
        Update-TdaPresence -MessageDid "did:drn:alpha.svrn7.net/inbox/msg/5f43a2..."
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { Write-Warning "Presence LOBE: $MessageDid not found."; return $null }

        $body   = $msg.PackedPayload | ConvertFrom-Json
        $did    = $body.did
        $status = $body.status
        $ttl    = if ($body.ttlSeconds) { [TimeSpan]::FromSeconds($body.ttlSeconds) } else { $script:PresenceCacheTtl }

        $entry = @{
            Did         = $did
            Status      = $status
            Since       = [datetimeoffset]::UtcNow.ToString('o')
            CachedUntil = [datetimeoffset]::UtcNow.Add($ttl).ToString('o')
        }

        $SVRN7.Cache.Set("presence:$did", $entry, $ttl) | Out-Null
        Write-Verbose "Presence LOBE: $did → $status (TTL $($ttl.TotalSeconds)s)"
        return $entry
    }
}

# ── Add-TdaPresenceSubscription ───────────────────────────────────────────────

function Add-TdaPresenceSubscription {
    <#
    .SYNOPSIS
        Processes an inbound presence/1.0/subscribe and registers the subscriber.

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        None. Subscriber is added to the subscription cache.

    .EXAMPLE
        Add-TdaPresenceSubscription -MessageDid "did:drn:alpha.svrn7.net/inbox/msg/5f43a2..."
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg  = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        $body = $msg.PackedPayload | ConvertFrom-Json
        $subscriberDid = $body.from

        $subs = Get-PresenceSubscriptions
        if ($subscriberDid -notin $subs) {
            $subs += $subscriberDid
            $SVRN7.Cache.Set($script:SubscriptionCacheKey, $subs,
                [TimeSpan]::FromHours(24)) | Out-Null
            Write-Verbose "Presence LOBE: $subscriberDid subscribed."
        }
    }
}

# ── Publish-TdaPresence ───────────────────────────────────────────────────────

function Publish-TdaPresence {
    <#
    .SYNOPSIS
        Publishes this TDA's presence status to all subscribed peers.

    .PARAMETER Status
        Availability status: Available | Busy | Away | Offline.

    .PARAMETER TtlSeconds
        How long the status is valid (default: 300).

    .OUTPUTS
        Array of OutboundMessage — one per subscribed peer.

    .EXAMPLE
        Publish-TdaPresence -Status Available
        Publish-TdaPresence -Status Busy -TtlSeconds 3600
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Available', 'Busy', 'Away', 'Offline')]
        [string] $Status,

        [int] $TtlSeconds = 300
    )

    process {
        $mySocietyDid = $SVRN7.Driver.GetSocietyDidAsync().GetAwaiter().GetResult()
        $subs = Get-PresenceSubscriptions

        if ($subs.Count -eq 0) {
            Write-Verbose "Presence LOBE: no subscribers — nothing to publish."
            return
        }

        $payload = @{
            did        = $mySocietyDid
            status     = $Status
            since      = [datetimeoffset]::UtcNow.ToString('o')
            ttlSeconds = $TtlSeconds
        } | ConvertTo-Json -Compress

        foreach ($subscriberDid in $subs) {
            $endpoint = Resolve-TdaEndpoint -Did $subscriberDid
            [PSCustomObject]@{
                PeerEndpoint  = $endpoint
                PackedMessage = $payload
                MessageType   = 'did:drn:svrn7.net/protocols/presence/1.0/status'
            }
        }
    }
}

# ── Get-TdaPresence ───────────────────────────────────────────────────────────

function Get-TdaPresence {
    <#
    .SYNOPSIS
        Returns the cached presence status for a peer TDA DID.

    .PARAMETER Did
        The peer TDA's DID.

    .OUTPUTS
        Hashtable — { Did, Status, Since, CachedUntil } or $null if not cached.

    .EXAMPLE
        Get-TdaPresence -Did "did:drn:beta.svrn7.net"
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Did)

    process {
        $entry = $null
        if ($SVRN7.Cache.TryGetValue("presence:$Did", [ref]$entry)) { return $entry }
        return $null
    }
}

# ── Helpers ───────────────────────────────────────────────────────────────────

function Get-PresenceSubscriptions {
    $subs = $null
    if ($SVRN7.Cache.TryGetValue($script:SubscriptionCacheKey, [ref]$subs)) { return $subs }
    return @()
}

function Resolve-TdaEndpoint {
    param([string] $Did)
    $doc = $SVRN7.Driver.ResolveDidDocumentAsync($Did).GetAwaiter().GetResult()
    $svc = $doc.Service | Where-Object { $_.type -eq 'DIDComm' } | Select-Object -First 1
    if (-not $svc) { throw "No DIDComm service endpoint for $Did" }
    return $svc.serviceEndpoint
}

Export-ModuleMember -Function @(
    'Update-TdaPresence',
    'Add-TdaPresenceSubscription',
    'Publish-TdaPresence',
    'Get-TdaPresence'
)
