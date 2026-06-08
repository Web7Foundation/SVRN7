#Requires -Version 7.0
<#
.SYNOPSIS
    SVRN7 Onboarding LOBE — citizen registration via DIDComm onboard protocol.

.DESCRIPTION
    Implements the did:drn:svrn7.net/protocols/Svrn7.Onboarding/0.8/* DIDComm protocol.
    Wraps Register-Svrn7CitizenInSociety (Svrn7.Society.psm1) as a DIDComm-driven
    pipeline. Handles endowment, overdraft draw, and receipt credential issuance.

    Derived from: Agent 2 — Onboarding (PowerShell Runspace) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    Protocol URIs:
        did:drn:svrn7.net/protocols/Svrn7.Onboarding/0.8/register-citizen — inbound registration request
        did:drn:svrn7.net/protocols/Svrn7.Onboarding/0.8/receipt — outbound registration receipt

    Pipeline:
        Get-Web7Message | ConvertFrom-Web7OnboardRequest |
        Register-Svrn7CitizenInSociety | New-Web7OnboardReceipt | Send-Web7Message
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── ConvertFrom-Web7OnboardRequest ─────────────────────────────────────────────

function ConvertFrom-Web7OnboardRequest {
    <#
    .SYNOPSIS
        Extracts the citizen DID and public key from an Svrn7.Onboarding/0.8/register-citizen message.

    .DESCRIPTION
        Resolves the inbox message DID URL and deserialises the onboarding request body.

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — { MessageDid, CitizenDid, PublicKeyHex, RequestedAt }

    .EXAMPLE
        Get-Web7Message -Did $msgDid | ConvertFrom-Web7OnboardRequest
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

        Assert-BodyFields $body @('citizenDid') 'Onboarding LOBE: Svrn7.Onboarding/0.8/register-citizen'

        $citizenDid   = $body.citizenDid
        $publicKeyHex = Get-BodyField $body 'publicKeyHex' ''
        $svcUrl       = Get-BodyField $body 'serviceEndpointUrl' ''
        $methodName   = ($citizenDid -split ':')[1]

        $didDocument = $SVRN7.Driver.CreateDidDocument(
            $citizenDid,
            $publicKeyHex,
            $methodName,
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
        Builds an Svrn7.Onboarding/0.8/receipt OutboundMessage after successful registration.

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
        $mySocietyDid = $SVRN7.Driver.SocietyDid

        $payload = @{
            from            = $mySocietyDid
            to              = $RegistrationResult.CitizenDid
            success         = $true
            citizenDid      = $RegistrationResult.CitizenDid
            societyDid      = $RegistrationResult.SocietyDid
            endowmentGrana  = $RegistrationResult.EndowmentGrana
            endowmentVcId   = $RegistrationResult.EndowmentVcId
            registeredAt    = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Compress

        $endpoint = Resolve-SocietySenderEndpoint -Did $RegistrationResult.CitizenDid
        if (-not $endpoint) {
            Write-Warning "New-Web7OnboardReceipt: no DIDComm service endpoint for '$($RegistrationResult.CitizenDid)' — reply skipped."
            return
        }

        Write-Verbose "Onboarding LOBE: receipt for $($RegistrationResult.CitizenDid) — $($RegistrationResult.EndowmentGrana) grana"

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Onboarding/0.8/receipt'
            from = $mySocietyDid
            to   = @($RegistrationResult.CitizenDid)
            body = $payload
        } | ConvertTo-Json -Compress

        [Svrn7.TDA.OutboundMessage]::new($endpoint, $envelope)
    }
}

# ── Send-Web7OnboardError ──────────────────────────────────────────────────────

function Send-Web7OnboardError {
    <#
    .SYNOPSIS
        Sends an Svrn7.Onboarding/0.8/receipt with success=false on registration failure.

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
        $mySocietyDid = $SVRN7.Driver.SocietyDid

        $payload = @{
            from          = $mySocietyDid
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
            type = 'did:drn:svrn7.net/protocols/Svrn7.Onboarding/0.8/receipt'
            from = $mySocietyDid
            to   = @($CitizenDid)
            body = $payload
        } | ConvertTo-Json -Compress

        [Svrn7.TDA.OutboundMessage]::new($endpoint, $envelope)
    }
}

Export-ModuleMember -Function @(
    'ConvertFrom-Web7OnboardRequest',
    'New-Web7OnboardReceipt',
    'Send-Web7OnboardError'
)
