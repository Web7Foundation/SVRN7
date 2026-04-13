#Requires -Version 7.0
<#
.SYNOPSIS
    SVRN7 Onboarding LOBE — citizen registration via DIDComm onboard protocol.

.DESCRIPTION
    Implements the did:drn:svrn7.net/protocols/onboard/1.0/* DIDComm protocol.
    Wraps Register-Svrn7CitizenInSociety (Svrn7.Society.psm1) as a DIDComm-driven
    pipeline. Handles endowment, overdraft draw, and receipt credential issuance.

    Derived from: Agent 2 — Onboarding (PowerShell Runspace) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    Protocol URIs:
        did:drn:svrn7.net/protocols/onboard/1.0/request — inbound registration request
        did:drn:svrn7.net/protocols/onboard/1.0/receipt — outbound registration receipt

    Pipeline:
        Get-TdaMessage | ConvertFrom-TdaOnboardRequest |
        Register-Svrn7CitizenInSociety | New-TdaOnboardReceipt | Send-TdaMessage
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── ConvertFrom-TdaOnboardRequest ─────────────────────────────────────────────

function ConvertFrom-TdaOnboardRequest {
    <#
    .SYNOPSIS
        Extracts the citizen DID and public key from an onboard/1.0/request message.

    .DESCRIPTION
        Resolves the inbox message DID URL and deserialises the onboarding request body.

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — { MessageDid, CitizenDid, PublicKeyHex, RequestedAt }

    .EXAMPLE
        Get-TdaMessage -Did $msgDid | ConvertFrom-TdaOnboardRequest
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

        if (-not $body.citizenDid) {
            throw "Onboarding LOBE: onboard/1.0/request body missing required field 'citizenDid'."
        }

        return @{
            MessageDid   = $MessageDid
            CitizenDid   = $body.citizenDid
            PublicKeyHex = $body.publicKeyHex    # secp256k1 signing key (hex)
            DisplayName  = $body.displayName
            RequestedAt  = [datetimeoffset]::UtcNow.ToString('o')
        }
    }
}

# ── New-TdaOnboardReceipt ─────────────────────────────────────────────────────

function New-TdaOnboardReceipt {
    <#
    .SYNOPSIS
        Builds an onboard/1.0/receipt OutboundMessage after successful registration.

    .DESCRIPTION
        Accepts the registration result hashtable from Register-Svrn7CitizenInSociety
        (pipeline input) and constructs a DIDComm receipt for the requesting TDA.

    .PARAMETER RegistrationResult
        Registration result hashtable from Register-Svrn7CitizenInSociety.
        Expected fields: CitizenDid, EndowmentGrana, EndowmentVcId, SocietyDid.

    .OUTPUTS
        Hashtable — OutboundMessage for the Switchboard.

    .EXAMPLE
        ConvertFrom-TdaOnboardRequest | Register-Svrn7CitizenInSociety | New-TdaOnboardReceipt
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [hashtable] $RegistrationResult
    )

    process {
        $mySocietyDid = $SVRN7.Driver.GetSocietyDidAsync().GetAwaiter().GetResult()

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

        $endpoint = Resolve-TdaEndpoint -Did $RegistrationResult.CitizenDid

        Write-Verbose "Onboarding LOBE: receipt for $($RegistrationResult.CitizenDid) — $($RegistrationResult.EndowmentGrana) grana"

        return @{
            PeerEndpoint  = $endpoint
            PackedMessage = $payload
            MessageType   = 'did:drn:svrn7.net/protocols/onboard/1.0/receipt'
        }
    }
}

# ── Send-TdaOnboardError ──────────────────────────────────────────────────────

function Send-TdaOnboardError {
    <#
    .SYNOPSIS
        Sends an onboard/1.0/receipt with success=false on registration failure.

    .PARAMETER CitizenDid
        The requesting citizen's DID.

    .PARAMETER ErrorMessage
        Human-readable error description.

    .OUTPUTS
        Hashtable — OutboundMessage for the Switchboard.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $CitizenDid,
        [Parameter(Mandatory)] [string] $ErrorMessage
    )

    process {
        $mySocietyDid = $SVRN7.Driver.GetSocietyDidAsync().GetAwaiter().GetResult()

        $payload = @{
            from          = $mySocietyDid
            to            = $CitizenDid
            success       = $false
            error         = $ErrorMessage
            registeredAt  = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Compress

        $endpoint = Resolve-TdaEndpoint -Did $CitizenDid
        return @{
            PeerEndpoint  = $endpoint
            PackedMessage = $payload
            MessageType   = 'did:drn:svrn7.net/protocols/onboard/1.0/receipt'
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
    'ConvertFrom-TdaOnboardRequest',
    'New-TdaOnboardReceipt',
    'Send-TdaOnboardError'
)
