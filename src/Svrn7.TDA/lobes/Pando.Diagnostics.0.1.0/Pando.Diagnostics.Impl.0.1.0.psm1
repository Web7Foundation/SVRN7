#Requires -Version 7.2
#Requires -PSEdition Core
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-TDADate {
    <#
    .SYNOPSIS
        Returns the TDA server's current date and time as a DateTimeOffset (UTC).
    .OUTPUTS
        [datetimeoffset] — current UTC date and time at the moment of the call.
    .EXAMPLE
        $now = Get-TDADate
        Write-Host "Server time: $($now.ToString('o'))"
    #>
    [CmdletBinding()]
    [OutputType([datetimeoffset])]
    param()
    process {
        return [datetimeoffset]::UtcNow
    }
}

Export-ModuleMember -Function @('Get-TDADate')
