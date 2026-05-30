#Requires -Version 7.2
#Requires -PSEdition Core
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module "$PSScriptRoot/Pando.Diagnostics.Impl.psm1"

# ── Invoke-PandoDiagnosticsDateQuery ──────────────────────────────────────────

function Invoke-PandoDiagnosticsDateQuery {
    <#
    .SYNOPSIS
        DIDComm adapter for diagnostics/1.0/date-query.
        Delegates to the pre-existing Get-TDADate cmdlet and returns the result
        as a diagnostics/1.0/date-result reply.
    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.
    .OUTPUTS
        Hashtable — { PeerEndpoint, PackedMessage, MessageType } or $null if no reply endpoint.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )
    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { throw "Invoke-PandoDiagnosticsDateQuery: message '$MessageDid' not found." }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop

        $now = Get-TDADate

        Write-Information "Pando.Diagnostics: serverUtc=$($now.ToString('o')) epoch=$($SVRN7.CurrentEpoch)"

        $replyEndpoint = if ($body.PSObject.Properties['replyEndpoint']) { $body.replyEndpoint }
                         else { Resolve-SocietySenderEndpoint -Did $msg.FromDid }
        if (-not $replyEndpoint) {
            Write-Warning "Invoke-PandoDiagnosticsDateQuery: no reply endpoint — result not delivered."
            return
        }

        return @{
            PeerEndpoint  = $replyEndpoint
            PackedMessage = (@{
                serverUtc       = $now.UtcDateTime.ToString('o')
                serverUtcOffset = '+00:00'
                currentEpoch    = $SVRN7.CurrentEpoch
                respondedAt     = [datetimeoffset]::UtcNow.ToString('o')
            } | ConvertTo-Json -Compress)
            MessageType   = 'did:drn:svrn7.net/protocols/diagnostics/1.0/date-result'
        }
    }
}

Export-ModuleMember -Function @('Invoke-PandoDiagnosticsDateQuery')
