#Requires -Version 7.0
<#
.SYNOPSIS
    SVRN7 Identity LOBE — DID Document and VC resolution via DIDComm.

.DESCRIPTION
    Handles all DIDComm-based DID Document resolution requests and Verifiable
    Credential queries. Delegates to ISvrn7SocietyDriver for local resolution
    and to FederationDidDocumentResolver / FederationVcDocumentResolver for
    cross-Society resolution.

    Derived from: Identity LOBE (implied, DSA 0.24 Epoch 0 — PPML).

.NOTES
    Protocol URIs:
        did:drn:svrn7.net/protocols/did/1.0/resolve-request     — inbound DID resolve
        did:drn:svrn7.net/protocols/did/1.0/resolve-response    — outbound DID response
        did:drn:svrn7.net/protocols/vc/1.0/resolve-by-subject-request   — inbound VC query
        did:drn:svrn7.net/protocols/vc/1.0/resolve-by-subject-response  — outbound VC response
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Resolve-Svrn7Did ──────────────────────────────────────────────────────────

function Resolve-Svrn7Did {
    <#
    .SYNOPSIS
        Processes an inbound did/1.0/resolve-request and returns a
        did/1.0/resolve-response OutboundMessage.

    .DESCRIPTION
        Resolves the requested DID via ISvrn7SocietyDriver.ResolveDidDocumentAsync().
        If the DID belongs to this Society it is resolved locally; if cross-Society,
        the FederationDidDocumentResolver performs a DIDComm round-trip.

        Protocol: did:drn:svrn7.net/protocols/did/1.0/resolve-request

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — OutboundMessage with packed did/1.0/resolve-response.

    .EXAMPLE
        Resolve-Svrn7Did -MessageDid "did:drn:alpha.svrn7.net/inbox/msg/5f43a2..."
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { Write-Warning "Identity LOBE: $MessageDid not found."; return $null }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        $requestedDid = $body.did

        Write-Verbose "Identity LOBE: resolving DID $requestedDid"

        $didDoc = $SVRN7.Driver.ResolveDidDocumentAsync($requestedDid).GetAwaiter().GetResult()

        $mySocietyDid = $SVRN7.Driver.GetSocietyDidAsync().GetAwaiter().GetResult()

        $responsePayload = @{
            from        = $mySocietyDid
            to          = $body.from
            requestedDid= $requestedDid
            found       = ($null -ne $didDoc)
            didDocument = $didDoc
            resolvedAt  = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Depth 10 -Compress

        $peerEndpoint = Resolve-TdaEndpoint -Did $body.from

        return @{
            PeerEndpoint  = $peerEndpoint
            PackedMessage = $responsePayload
            MessageType   = 'did:drn:svrn7.net/protocols/did/1.0/resolve-response'
        }
    }
}

# ── Get-Svrn7VcById ───────────────────────────────────────────────────────────

function Get-Svrn7VcById {
    <#
    .SYNOPSIS
        Resolves a VC record by VC ID and returns it to the requesting TDA.

    .DESCRIPTION
        Looks up a Verifiable Credential by its jti (VC ID / UUID) in the
        local VC registry. Returns a vc/1.0/resolve-by-subject-response.

        Protocol: did:drn:svrn7.net/protocols/vc/1.0/resolve-by-subject-request

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — OutboundMessage with VC resolution response.

    .EXAMPLE
        Get-Svrn7VcById -MessageDid "did:drn:alpha.svrn7.net/inbox/msg/5f43a2..."
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { Write-Warning "Identity LOBE: $MessageDid not found."; return $null }

        $body      = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        $subjectDid = $body.subjectDid

        Write-Verbose "Identity LOBE: resolving VCs for subject $subjectDid"

        # Find-Svrn7VcsBySubject is in Svrn7.Society.psm1 (eager)
        $vcs = Find-Svrn7VcsBySubject -SubjectDid $subjectDid

        $mySocietyDid = $SVRN7.Driver.GetSocietyDidAsync().GetAwaiter().GetResult()

        $responsePayload = @{
            from        = $mySocietyDid
            to          = $body.from
            subjectDid  = $subjectDid
            found       = ($vcs.Count -gt 0)
            credentials = $vcs
            resolvedAt  = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Depth 10 -Compress

        $peerEndpoint = Resolve-TdaEndpoint -Did $body.from

        return @{
            PeerEndpoint  = $peerEndpoint
            PackedMessage = $responsePayload
            MessageType   = 'did:drn:svrn7.net/protocols/vc/1.0/resolve-by-subject-response'
        }
    }
}

# ── Resolve-Svrn7CitizenIdentity ──────────────────────────────────────────────

function Resolve-Svrn7CitizenIdentity {
    <#
    .SYNOPSIS
        Performs a full identity resolution for a citizen DID: DID Document +
        all active VCs. Returns a combined identity record.

    .DESCRIPTION
        Convenience function for local identity lookups. Does not produce a
        DIDComm response — used by other LOBEs (e.g., Onboarding) that need
        full citizen identity context before acting.

    .PARAMETER CitizenDid
        The citizen DID to resolve.

    .OUTPUTS
        Hashtable — { CitizenDid, DIDDocument, Credentials[], ResolvedAt }
        or $null if citizen not found.

    .EXAMPLE
        $identity = Resolve-Svrn7CitizenIdentity -CitizenDid "did:drn:alice.alpha.svrn7.net"
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory)] [string] $CitizenDid
    )

    process {
        $didDoc = $SVRN7.Driver.ResolveDidDocumentAsync($CitizenDid).GetAwaiter().GetResult()
        if (-not $didDoc) {
            Write-Verbose "Identity LOBE: DID Document not found for $CitizenDid"
            return $null
        }

        $vcs = Find-Svrn7VcsBySubject -SubjectDid $CitizenDid

        return @{
            CitizenDid   = $CitizenDid
            DIDDocument  = $didDoc
            Credentials  = $vcs
            ResolvedAt   = [datetimeoffset]::UtcNow.ToString('o')
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
    'Resolve-Svrn7Did',
    'Get-Svrn7VcById',
    'Resolve-Svrn7CitizenIdentity'
)
