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
        did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-request     — inbound DID resolve
        did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-response    — outbound DID response
        did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/vc-resolve-by-subject-request   — inbound VC query
        did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/vc-resolve-by-subject-response  — outbound VC response
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Resolve-Svrn7Did ──────────────────────────────────────────────────────────

function Resolve-Svrn7Did {
    <#
    .SYNOPSIS
        Processes an inbound Svrn7.Identity/0.8.0/did-resolve-request and returns a
        Svrn7.Identity/0.8.0/did-resolve-response OutboundMessage.

    .DESCRIPTION
        Resolves the requested DID via ISvrn7SocietyDriver.ResolveDidAsync().
        If the DID belongs to this Society it is resolved locally; if cross-Society,
        the FederationDidDocumentResolver performs a DIDComm round-trip.

        Protocol: did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-request

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — OutboundMessage with packed Svrn7.Identity/0.8.0/did-resolve-response.

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

        $didDoc = $SVRN7.Driver.ResolveDidAsync($requestedDid).GetAwaiter().GetResult()

        $mySocietyDid = $SVRN7.Driver.SocietyDid

        $responsePayload = @{
            from        = $mySocietyDid
            to          = $body.from
            requestedDid= $requestedDid
            found       = ($null -ne $didDoc)
            didDocument = $didDoc
            resolvedAt  = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Depth 10 -Compress

        $peerEndpoint = Resolve-SocietySenderEndpoint -Did $body.from
        if (-not $peerEndpoint) {
            Write-Warning "Resolve-Svrn7Did: no DIDComm service endpoint for '$($body.from)' — reply skipped."
            return
        }

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-response'
            from = $SVRN7.Driver.SocietyDid
            to   = @($body.from)
            body = $responsePayload
        } | ConvertTo-Json -Compress

        Write-Information "Resolve-Svrn7Did: requestedDid='$requestedDid' found=$($null -ne $didDoc) replying to '$($body.from)'"

        [Svrn7.TDA.OutboundMessage]::new($peerEndpoint, $envelope)
    }
}

# ── Get-Svrn7VcById ───────────────────────────────────────────────────────────

function Get-Svrn7VcById {
    <#
    .SYNOPSIS
        Resolves a VC record by VC ID and returns it to the requesting TDA.

    .DESCRIPTION
        Looks up a Verifiable Credential by its jti (VC ID / UUID) in the
        local VC registry. Returns a Svrn7.Identity/0.8.0/vc-resolve-by-subject-response.

        Protocol: did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/vc-resolve-by-subject-request

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

        # Find-Svrn7VcsBySubject is in Svrn7.Society.0.8.0.psm1 (eager)
        $vcs = Find-Svrn7VcsBySubject -SubjectDid $subjectDid

        $mySocietyDid = $SVRN7.Driver.SocietyDid

        $responsePayload = @{
            from        = $mySocietyDid
            to          = $body.from
            subjectDid  = $subjectDid
            found       = ($vcs.Count -gt 0)
            credentials = $vcs
            resolvedAt  = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Depth 10 -Compress

        $peerEndpoint = Resolve-SocietySenderEndpoint -Did $body.from
        if (-not $peerEndpoint) {
            Write-Warning "Get-Svrn7VcById: no DIDComm service endpoint for '$($body.from)' — reply skipped."
            return
        }

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/vc-resolve-by-subject-response'
            from = $SVRN7.Driver.SocietyDid
            to   = @($body.from)
            body = $responsePayload
        } | ConvertTo-Json -Compress

        Write-Information "Get-Svrn7VcById: subjectDid='$subjectDid' found=$($vcs.Count -gt 0) replying to '$($body.from)'"

        [Svrn7.TDA.OutboundMessage]::new($peerEndpoint, $envelope)
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
        $identity = Resolve-Svrn7CitizenIdentity -CitizenDid "did:drn:alpha.svrn7.net/citizen/alice"
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory)] [string] $CitizenDid
    )

    process {
        $didDoc = $SVRN7.Driver.ResolveDidAsync($CitizenDid).GetAwaiter().GetResult()
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

function Invoke-Svrn7DidResolveResponse {
    <#
    .SYNOPSIS
        Handles Svrn7.Identity/0.8.0/did-resolve-response — receives the DID resolution reply.
    .DESCRIPTION
        Inbound reply to a previously-sent Svrn7.Identity/0.8.0/did-resolve-request.
        Body: { from, to, requestedDid, found, didDocument, resolvedAt }
        Logs the resolution outcome. No reply is sent — response messages are terminal.
    .PARAMETER MessageDid
        TDA resource DID URL for the inbox message.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipelineByPropertyName)] [string] $MessageDid)
    process {
        $msg          = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { throw "Invoke-Svrn7DidResolveResponse: message '$MessageDid' not found." }
        $body         = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        $requestedDid = Get-BodyField $body 'requestedDid' '(unknown)'
        $found        = Get-BodyField $body 'found'        $false
        Write-Information "Invoke-Svrn7DidResolveResponse: requestedDid='$requestedDid' found=$found from='$($msg.FromDid)'"
        # Terminal reply — no outbound message returned.
    }
}

function Invoke-Svrn7VcResolveResponse {
    <#
    .SYNOPSIS
        Handles Svrn7.Identity/0.8.0/vc-resolve-by-subject-response — receives the VC resolution reply.
    .DESCRIPTION
        Inbound reply to a previously-sent Svrn7.Identity/0.8.0/vc-resolve-by-subject-request.
        Body: { from, to, subjectDid, found, credentials[], resolvedAt }
        Logs the resolution outcome. No reply is sent — response messages are terminal.
    .PARAMETER MessageDid
        TDA resource DID URL for the inbox message.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipelineByPropertyName)] [string] $MessageDid)
    process {
        $msg        = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { throw "Invoke-Svrn7VcResolveResponse: message '$MessageDid' not found." }
        $body       = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        $subjectDid = Get-BodyField $body 'subjectDid' '(unknown)'
        $found      = Get-BodyField $body 'found'      $false
        Write-Information "Invoke-Svrn7VcResolveResponse: subjectDid='$subjectDid' found=$found from='$($msg.FromDid)'"
        # Terminal reply — no outbound message returned.
    }
}

Export-ModuleMember -Function @(
    'Resolve-Svrn7Did',
    'Get-Svrn7VcById',
    'Resolve-Svrn7CitizenIdentity',
    'Invoke-Svrn7DidResolveResponse',
    'Invoke-Svrn7VcResolveResponse'
)
