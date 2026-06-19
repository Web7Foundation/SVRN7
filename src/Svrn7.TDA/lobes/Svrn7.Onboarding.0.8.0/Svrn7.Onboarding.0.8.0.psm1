#Requires -Version 7.0
<#
.SYNOPSIS
    SVRN7 Onboarding LOBE — citizen registration via DIDComm onboard protocol.

.DESCRIPTION
    Implements the did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/* DIDComm protocol.
    Wraps Register-Svrn7CitizenInSociety (Svrn7.Society.0.8.0.psm1) as a DIDComm-driven
    pipeline. Handles endowment, overdraft draw, and receipt credential issuance.

    Derived from: Agent 2 — Onboarding (PowerShell Runspace) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    Protocol URIs:
        did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/register-citizen — inbound registration request
        did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/receipt — outbound registration receipt

    Pipeline:
        Dequeue-Svrn7Message | ConvertFrom-Web7OnboardRequest |
        Register-Svrn7CitizenInSociety | New-Web7OnboardReceipt | Enqueue-Svrn7Message
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── ConvertFrom-Web7OnboardRequest ─────────────────────────────────────────────

function ConvertFrom-Web7OnboardRequest {
    <#
    .SYNOPSIS
        Extracts the citizen public key from an Svrn7.Onboarding/0.8.0/register-citizen message
        and derives the citizen DID.

    .DESCRIPTION
        Resolves the inbox message DID URL and deserialises the onboarding request body.
        The citizen DID is derived server-side:
            did:drn:{societyName}.svrn7.net/citizen/1.0/{blake3(publicKeyHex)}
        where societyName is extracted from the Society TDA's own DID ($SVRN7.LocalDid).

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — { MessageDid, DidDocument, DisplayName, RequestedAt }

    .EXAMPLE
        Dequeue-Svrn7Message -Did $msgDid | ConvertFrom-Web7OnboardRequest
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) {
            Write-Warning "Onboarding LOBE: message $MessageDid not found."
            return $null
        }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop

        Assert-BodyFields $body @('publicKeyHex') 'Onboarding LOBE: Svrn7.Onboarding/0.8.0/register-citizen'

        $publicKeyHex = $body.publicKeyHex
        $svcUrl       = Get-BodyField $body 'serviceEndpointUrl' ''

        # Derive citizen DID from genesis public key and the Society's own name
        $pubBytes    = [System.Convert]::FromHexString($publicKeyHex)
        $genesisHash = [Svrn7.Crypto.CryptoService]::new().Blake3Hex($pubBytes)
        $methodSpec  = ($SVRN7.LocalDid -split ':', 3)[2]   # 'federation.svrn7.net/bindloss/1.0/<hash>'
        $societyName = ($methodSpec -split '/')[1]
        $citizenDid  = "did:drn:$societyName.svrn7.net/citizen/1.0/$genesisHash"

        $didDocument = $SVRN7.Driver.CreateDidDocument(
            $citizenDid,
            $publicKeyHex,
            'drn',
            $(if ($svcUrl) { $svcUrl } else { $null })
        )

        return @{
            MessageDid   = $MessageDid
            DidDocument  = $didDocument
            DisplayName  = Get-BodyField $body 'displayName' ''
            RequestedAt  = [datetimeoffset]::UtcNow.ToString('o')
        }
    }
}

# ── New-Web7OnboardReceipt ─────────────────────────────────────────────────────

function New-Web7OnboardReceipt {
    <#
    .SYNOPSIS
        Builds an Svrn7.Onboarding/0.8.0/receipt OutboundMessage after successful registration.

    .DESCRIPTION
        Accepts the registration result hashtable from Register-Svrn7CitizenInSociety
        (pipeline input) and constructs a DIDComm receipt for the requesting TDA.

    .PARAMETER RegistrationResult
        Registration result hashtable from Register-Svrn7CitizenInSociety.
        Expected fields: CitizenDid, EndowmentGrana, EndowmentVcId, SocietyDid.

    .OUTPUTS
        OutboundMessage — packed DIDComm message ready for Switchboard delivery.

    .EXAMPLE
        ConvertFrom-Web7OnboardRequest | Register-Svrn7CitizenInSociety | New-Web7OnboardReceipt
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [hashtable] $RegistrationResult
    )

    process {
        $citizenDocJson = $SVRN7.GetDidDocumentJson($RegistrationResult.CitizenDid)
        $societyDocJson = $SVRN7.GetDidDocumentJson($SVRN7.LocalDid)

        $payload = @{
            from               = $SVRN7.LocalDid
            to                 = $RegistrationResult.CitizenDid
            success            = $true
            citizenDid         = $RegistrationResult.CitizenDid
            citizenDidDocument = if ($citizenDocJson) { $citizenDocJson | ConvertFrom-Json } else { $null }
            societyDid         = $RegistrationResult.SocietyDid
            societyDidDocument = if ($societyDocJson) { $societyDocJson | ConvertFrom-Json } else { $null }
            societyEndpointUrl = $SVRN7.ServiceEndpointUrl
            endowmentGrana     = $RegistrationResult.EndowmentGrana
            endowmentVcId      = $RegistrationResult.EndowmentVcId
            registeredAt       = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Depth 15 -Compress

        $endpoint = Resolve-SocietySenderEndpoint -Did $RegistrationResult.CitizenDid
        if (-not $endpoint) {
            Write-Warning "New-Web7OnboardReceipt: no DIDComm service endpoint for '$($RegistrationResult.CitizenDid)' — reply skipped."
            return
        }

        Write-Verbose "Onboarding LOBE: receipt for $($RegistrationResult.CitizenDid) — $($RegistrationResult.EndowmentGrana) grana"

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/receipt'
            from = $SVRN7.LocalDid
            to   = @($RegistrationResult.CitizenDid)
            body = $payload
        } | ConvertTo-Json -Compress

        [Svrn7.TDA.OutboundMessage]::new($endpoint, $envelope)
    }
}

# ── Invoke-Web7OnboardReceipt ──────────────────────────────────────────────────

function Invoke-Web7OnboardReceipt {
    <#
    .SYNOPSIS
        Handles Svrn7.Onboarding.0.8.0/receipt on the Citizen TDA.

    .DESCRIPTION
        Stores the Citizen's own DID Document and the Society's DID Document in the
        local registry, then wires the Society as the Citizen's parent TDA (persisted
        to agent-identity.json via $SVRN7.SetParentTda).

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.
    #>
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )
    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) {
            Write-Warning "Invoke-Web7OnboardReceipt: message $MessageDid not found."
            return
        }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        Assert-BodyFields $body @('citizenDid','societyDid','societyEndpointUrl','citizenDidDocument','societyDidDocument') 'Invoke-Web7OnboardReceipt'

        if (-not $body.success) {
            Write-Warning "Invoke-Web7OnboardReceipt: registration failed — $($body.error)"
            return
        }

        # Store Citizen's own DID Document (created by Society during registration)
        $SVRN7.StoreReceivedDidDocumentAsync(
            ($body.citizenDidDocument | ConvertTo-Json -Depth 15 -Compress)
        ).GetAwaiter().GetResult()

        # Store Society's DID Document (enables future DID resolution without network)
        $SVRN7.StoreReceivedDidDocumentAsync(
            ($body.societyDidDocument | ConvertTo-Json -Depth 15 -Compress)
        ).GetAwaiter().GetResult()

        # Wire parent TDA — updates memory and persists to agent-identity.json
        $SVRN7.SetParentTda($body.societyDid, $body.societyEndpointUrl)

        Write-Information "Invoke-Web7OnboardReceipt: registered with $($body.societyDid) at $($body.societyEndpointUrl)"
    }
}

# ── Send-Web7OnboardError ──────────────────────────────────────────────────────

function Send-Web7OnboardError {
    <#
    .SYNOPSIS
        Sends an Svrn7.Onboarding/0.8.0/receipt with success=false on registration failure.

    .PARAMETER CitizenDid
        The requesting citizen's DID.

    .PARAMETER ErrorMessage
        Human-readable error description.

    .OUTPUTS
        OutboundMessage — packed DIDComm message ready for Switchboard delivery.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $CitizenDid,
        [Parameter(Mandatory)] [string] $ErrorMessage
    )

    process {
        $payload = @{
            from          = $SVRN7.LocalDid
            to            = $CitizenDid
            success       = $false
            error         = $ErrorMessage
            registeredAt  = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Compress

        $endpoint = Resolve-SocietySenderEndpoint -Did $CitizenDid
        if (-not $endpoint) {
            Write-Warning "Send-Web7OnboardError: no DIDComm service endpoint for '$CitizenDid' — reply skipped."
            return
        }

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/receipt'
            from = $SVRN7.LocalDid
            to   = @($CitizenDid)
            body = $payload
        } | ConvertTo-Json -Compress

        [Svrn7.TDA.OutboundMessage]::new($endpoint, $envelope)
    }
}

Export-ModuleMember -Function @(
    'ConvertFrom-Web7OnboardRequest',
    'Invoke-Web7OnboardReceipt',
    'New-Web7OnboardReceipt',
    'Send-Web7OnboardError'
)
