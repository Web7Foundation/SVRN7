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
        [Svrn7.TDA.OutboundMessage] or $null if no reply endpoint.
    #>
    [CmdletBinding()]
    [OutputType([Svrn7.TDA.OutboundMessage])]
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

        $payload = @{
            serverUtc       = $now.UtcDateTime.ToString('o')
            serverUtcOffset = '+00:00'
            currentEpoch    = $SVRN7.CurrentEpoch
            respondedAt     = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Compress

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Pando.Diagnostics/0.1/date-result'
            from = $SVRN7.Driver.SocietyDid
            to   = @($msg.FromDid)
            body = $payload
        } | ConvertTo-Json -Compress

        [Svrn7.TDA.OutboundMessage]::new($replyEndpoint, $envelope)
    }
}

Export-ModuleMember -Function @('Invoke-PandoDiagnosticsDateQuery')
