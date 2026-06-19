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
        Handles inbound did-resolve-request. Tries local first; escalates by role on miss.

    .DESCRIPTION
        Implements the correlated async relay pattern:
          1. Try local registry (always, regardless of role).
          2. If found: reply directly to sender.
          3. If not found: escalate based on $SVRN7.Role —
               Wanderer   → notFound (no parent)
               Citizen    → forward to parent Society ($SVRN7.ParentTdaEndpointUrl)
               Society    → forward to parent Federation ($SVRN7.ParentTdaEndpointUrl)
               Federation → look up owning Society via method registry; forward there
          4. On escalation: store pending correlation entry keyed on originalRequestId.
             When the response arrives, Invoke-Svrn7DidResolveResponse relays it back.

        Protocol: did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-request

        Body: { requestedDid, requestId, originalRequesterDid, originalRequestId }
              originalRequesterDid and originalRequestId are set on the first hop and
              carried unchanged through every relay hop.

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.
    #>
    [CmdletBinding()]
    [OutputType([Svrn7.TDA.OutboundMessage])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { Write-Warning "Identity LOBE: $MessageDid not found."; return $null }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop

        $requestedDid         = Get-BodyField $body 'requestedDid'         ''
        $requestId            = Get-BodyField $body 'requestId'            ''
        $originalRequesterDid = Get-BodyField $body 'originalRequesterDid' ''
        $originalRequestId    = Get-BodyField $body 'originalRequestId'    ''

        # On the originating hop: seed the original fields from this message
        if (-not $originalRequesterDid) { $originalRequesterDid = $msg.FromDid ?? '' }
        if (-not $requestId)            { $requestId            = [Guid]::NewGuid().ToString('N') }
        if (-not $originalRequestId)    { $originalRequestId    = $requestId }

        if (-not $requestedDid) {
            Write-Warning "Resolve-Svrn7Did: $MessageDid missing requestedDid — skipping."
            return $null
        }

        Write-Verbose "Resolve-Svrn7Did: requestedDid='$requestedDid' from='$($msg.FromDid)' role=$($SVRN7.Role)"

        # ── Step 1: Local resolution ────────────────────────────────────────────
        $didDoc = $SVRN7.Driver.ResolveDidAsync($requestedDid).GetAwaiter().GetResult()

        $immediateRequesterEndpoint = Resolve-SocietySenderEndpoint -Did $msg.FromDid
        if (-not $immediateRequesterEndpoint) {
            Write-Warning "Resolve-Svrn7Did: no endpoint for sender '$($msg.FromDid)' — reply skipped."
            return $null
        }

        if ($null -ne $didDoc) {
            Write-Information "Resolve-Svrn7Did: LOCAL HIT '$requestedDid' → '$($msg.FromDid)'"
            return New-DidResolveResponseMessage `
                -Found $true -DidDocument $didDoc `
                -RequestedDid $requestedDid -RecipientDid $msg.FromDid `
                -OriginalRequestId $originalRequestId
        }

        # ── Step 2: Local miss — escalate by role ──────────────────────────────
        $role = $SVRN7.Role

        if ($role -eq [Svrn7.Core.Models.Svrn7Role]::Wanderer) {
            Write-Information "Resolve-Svrn7Did: Wanderer LOCAL MISS '$requestedDid' → notFound"
            return New-DidResolveResponseMessage `
                -Found $false -RequestedDid $requestedDid -RecipientDid $msg.FromDid `
                -OriginalRequestId $originalRequestId `
                -ErrorCode 'notFound'
        }

        if ($role -eq [Svrn7.Core.Models.Svrn7Role]::Federation) {
            $methodName = ($requestedDid -split ':')[1]
            $allMethods = $SVRN7.GetAllDidMethodsAsync().GetAwaiter().GetResult()
            $methodRecord = $allMethods |
                Where-Object { $_.MethodName -eq $methodName } |
                Select-Object -First 1

            if (-not $methodRecord) {
                Write-Information "Resolve-Svrn7Did: Federation LOCAL MISS '$requestedDid' method '$methodName' not registered → notFound"
                return New-DidResolveResponseMessage `
                    -Found $false -RequestedDid $requestedDid -RecipientDid $msg.FromDid `
                    -OriginalRequestId $originalRequestId `
                    -ErrorCode 'methodNotSupported'
            }

            $parentDid      = $methodRecord.SocietyDid
            $parentEndpoint = Resolve-SocietySenderEndpoint -Did $parentDid
            if (-not $parentEndpoint) {
                Write-Warning "Resolve-Svrn7Did: Federation LOCAL MISS '$requestedDid' — no endpoint for target Society '$parentDid'"
                return New-DidResolveResponseMessage `
                    -Found $false -RequestedDid $requestedDid -RecipientDid $msg.FromDid `
                    -OriginalRequestId $originalRequestId `
                    -ErrorCode 'notFound'
            }

            Write-Information "Resolve-Svrn7Did: Federation LOCAL MISS '$requestedDid' → escalating to Society '$parentDid'"
            $SVRN7.AddPendingResolution($originalRequestId, $requestedDid, $msg.FromDid, $immediateRequesterEndpoint)
            return New-DidResolveForwardMessage `
                -RequestedDid $requestedDid -OriginalRequesterDid $originalRequesterDid `
                -OriginalRequestId $originalRequestId -TargetDid $parentDid -TargetEndpoint $parentEndpoint
        }

        # Citizen or Society: escalate to configured parent tier
        $parentEndpoint = $SVRN7.ParentTdaEndpointUrl
        $parentDid      = $SVRN7.ParentTdaDid

        if (-not $parentEndpoint) {
            Write-Warning "Resolve-Svrn7Did: $role LOCAL MISS '$requestedDid' — no parent endpoint configured → notFound"
            return New-DidResolveResponseMessage `
                -Found $false -RequestedDid $requestedDid -RecipientDid $msg.FromDid `
                -OriginalRequestId $originalRequestId `
                -ErrorCode 'notFound'
        }

        Write-Information "Resolve-Svrn7Did: $role LOCAL MISS '$requestedDid' → escalating to '$parentDid'"
        $SVRN7.AddPendingResolution($originalRequestId, $requestedDid, $msg.FromDid, $immediateRequesterEndpoint)
        return New-DidResolveForwardMessage `
            -RequestedDid $requestedDid -OriginalRequesterDid $originalRequesterDid `
            -OriginalRequestId $originalRequestId -TargetDid $parentDid -TargetEndpoint $parentEndpoint
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

        $peerEndpoint = Resolve-SocietySenderEndpoint -Did $body.from
        if (-not $peerEndpoint) {
            Write-Warning "Get-Svrn7VcById: no DIDComm service endpoint for '$($body.from)' — reply skipped."
            return
        }

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/vc-resolve-by-subject-response'
            from = $SVRN7.LocalDid
            to   = @($body.from)
            body = [ordered]@{
                from        = $SVRN7.LocalDid
                to          = $body.from
                subjectDid  = $subjectDid
                found       = ($vcs.Count -gt 0)
                credentials = $vcs
                resolvedAt  = [datetimeoffset]::UtcNow.ToString('o')
            }
        } | ConvertTo-Json -Compress -Depth 5

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

# ── Get-DIDDocument ───────────────────────────────────────────────────────────

function Get-DIDDocument {
    <#
    .SYNOPSIS
        Resolves a DID Document by DID and returns it directly.

    .DESCRIPTION
        Local utility for direct DID Document lookup. Delegates to
        ISvrn7SocietyDriver.ResolveDidAsync(). Returns the DidDocument
        object or $null when not found. Does not produce a DIDComm response.

    .PARAMETER Did
        The DID to resolve (e.g. "did:drn:alpha.svrn7.net/citizen/alice").

    .OUTPUTS
        Svrn7.Core.Models.DidDocument or $null.

    .EXAMPLE
        $doc = Get-DIDDocument -Did "did:drn:alpha.svrn7.net/citizen/alice"
    #>
    [CmdletBinding()]
    [OutputType([object])]
    param(
        [Parameter(Mandatory)]
        [string] $Did
    )

    process {
        Write-Verbose "Get-DIDDocument: resolving '$Did'"

        $doc = $SVRN7.Driver.ResolveDidAsync($Did).GetAwaiter().GetResult()

        if ($null -eq $doc) {
            Write-Verbose "Get-DIDDocument: '$Did' not found."
            return $null
        }

        Write-Information "Get-DIDDocument: resolved '$Did' version=$($doc.Version) status=$($doc.Status) role=$($doc.Role)"
        return $doc
    }
}

# ── Invoke-Svrn7DidResolveResponse ────────────────────────────────────────────

function Invoke-Svrn7DidResolveResponse {
    <#
    .SYNOPSIS
        Handles inbound did-resolve-response. Relays to the pending requester or terminates.
    .DESCRIPTION
        If a pending correlation entry exists for originalRequestId, this TDA is an
        intermediate relay hop — forward the response to the immediate requester.
        If no entry exists, this TDA was the original requester — log and terminate.

        Protocol: did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-response
    .PARAMETER MessageDid
        TDA resource DID URL for the inbox message.
    #>
    [CmdletBinding()]
    [OutputType([Svrn7.TDA.OutboundMessage])]
    param([Parameter(Mandatory, ValueFromPipelineByPropertyName)] [string] $MessageDid)
    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { throw "Invoke-Svrn7DidResolveResponse: message '$MessageDid' not found." }
        $body              = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        $requestedDid      = Get-BodyField $body 'requestedDid'      '(unknown)'
        $found             = Get-BodyField $body 'found'             $false
        $originalRequestId = Get-BodyField $body 'originalRequestId' ''

        Write-Information "Invoke-Svrn7DidResolveResponse: requestedDid='$requestedDid' found=$found originalRequestId='$originalRequestId' from='$($msg.FromDid)'"

        if (-not $originalRequestId) {
            Write-Verbose "Invoke-Svrn7DidResolveResponse: no originalRequestId — terminal."
            return $null
        }

        $pending = $SVRN7.TryCompletePendingResolution($originalRequestId)
        if ($null -eq $pending) {
            # This TDA was the original requester — the response is for us.
            Write-Verbose "Invoke-Svrn7DidResolveResponse: no pending entry for '$originalRequestId' — terminal (this TDA is the requester)."
            return $null
        }

        # Relay the response upstream to the TDA that originally asked us.
        Write-Information "Invoke-Svrn7DidResolveResponse: relaying '$requestedDid' found=$found → '$($pending.ImmediateRequesterDid)'"

        $relayEnvelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-response'
            from = $SVRN7.LocalDid
            to   = @($pending.ImmediateRequesterDid)
            body = $msg.PackedPayload | ConvertFrom-Json
        } | ConvertTo-Json -Compress -Depth 5

        [Svrn7.TDA.OutboundMessage]::new($pending.ImmediateRequesterEndpoint, $relayEnvelope)
    }
}

# ── Private helpers ───────────────────────────────────────────────────────────

function New-DidResolveResponseMessage {
    <#
    .SYNOPSIS
        Builds an OutboundMessage containing a did-resolve-response envelope.
    #>
    [CmdletBinding()]
    [OutputType([Svrn7.TDA.OutboundMessage])]
    param(
        [Parameter(Mandatory)] [string]  $RequestedDid,
        [Parameter(Mandatory)] [string]  $RecipientDid,
        [Parameter(Mandatory)] [string]  $OriginalRequestId,
        [bool]                           $Found        = $false,
        [object]                         $DidDocument  = $null,
        [string]                         $ErrorCode    = ''
    )
    $replyEndpoint = Resolve-SocietySenderEndpoint -Did $RecipientDid
    if (-not $replyEndpoint) {
        Write-Warning "New-DidResolveResponseMessage: cannot resolve endpoint for '$RecipientDid' — response not delivered."
        return $null
    }
    $responseBody = [ordered]@{
        requestedDid      = $RequestedDid
        found             = $Found
        didDocument       = $DidDocument
        resolvedAt        = [datetimeoffset]::UtcNow.ToString('o')
        originalRequestId = $OriginalRequestId
    }
    if ($ErrorCode) { $responseBody['errorCode'] = $ErrorCode }

    $envelope = [ordered]@{
        typ  = 'application/didcomm-plain+json'
        id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
        type = 'did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-response'
        from = $SVRN7.LocalDid
        to   = @($RecipientDid)
        body = $responseBody
    } | ConvertTo-Json -Compress -Depth 5

    [Svrn7.TDA.OutboundMessage]::new($replyEndpoint, $envelope)
}

function New-DidResolveForwardMessage {
    <#
    .SYNOPSIS
        Builds an OutboundMessage containing a forwarded did-resolve-request envelope.
    #>
    [CmdletBinding()]
    [OutputType([Svrn7.TDA.OutboundMessage])]
    param(
        [Parameter(Mandatory)] [string] $RequestedDid,
        [Parameter(Mandatory)] [string] $OriginalRequesterDid,
        [Parameter(Mandatory)] [string] $OriginalRequestId,
        [Parameter(Mandatory)] [string] $TargetDid,
        [Parameter(Mandatory)] [string] $TargetEndpoint
    )
    $envelope = [ordered]@{
        typ  = 'application/didcomm-plain+json'
        id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
        type = 'did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-request'
        from = $SVRN7.LocalDid
        to   = @($TargetDid)
        body = [ordered]@{
            requestedDid         = $RequestedDid
            requestId            = [Guid]::NewGuid().ToString('N')
            originalRequesterDid = $OriginalRequesterDid
            originalRequestId    = $OriginalRequestId
        }
    } | ConvertTo-Json -Compress -Depth 3

    [Svrn7.TDA.OutboundMessage]::new($TargetEndpoint, $envelope)
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
    'Get-DIDDocument',
    'Invoke-Svrn7DidResolveResponse',
    'Invoke-Svrn7VcResolveResponse'
    # New-DidResolveResponseMessage and New-DidResolveForwardMessage are private helpers — not exported.
)
