#Requires -Version 7.2
<#
.SYNOPSIS
    Starts a local SVRN7 testnet with three TDA instances.

.DESCRIPTION
    Launches four TDA processes in separate windows:

        Federation1   --role Federation  --port 8441  --did did:drn:federation1.testnet.svrn7.net
        Society2      --role Society     --port 8442  --did did:drn:society2.testnet.svrn7.net
        Citizen3      --role Citizen     --port 8443  --did did:drn:citizen3.testnet.svrn7.net
        Wanderer4     --role Wanderer    --port 8444  --did did:drn:wanderer4.testnet.svrn7.net/agent/1.0/<guid>

    Each TDA stores its databases under:
        <BinDir>/{port}/mem/

    Press Ctrl+C in this window to stop all four processes.

.PARAMETER BinDir
    Path to the TDA output directory containing Svrn7.TDA.dll.
    Default: src\Svrn7.TDA\bin\Debug\net8.0 relative to the repo root.
#>
param(
    [string] $BinDir = (Join-Path $PSScriptRoot 'src\Svrn7.TDA\bin\Debug\net8.0')
)

$dll = Join-Path $BinDir 'Svrn7.TDA.dll'
if (-not (Test-Path $dll)) {
    Write-Error "Svrn7.TDA.dll not found at '$dll'. Build the solution first."
    exit 1
}

$wandererGuid = [Guid]::NewGuid().ToString('N')

$nodes = @(
    @{ Name = 'Federation1'; Role = 'Federation'; Port = 8441; Did = 'did:drn:federation1.testnet.svrn7.net' }
    @{ Name = 'Society2';    Role = 'Society';    Port = 8442; Did = 'did:drn:society2.testnet.svrn7.net'    }
    @{ Name = 'Citizen3';    Role = 'Citizen';    Port = 8443; Did = 'did:drn:citizen3.testnet.svrn7.net'    }
    @{ Name = 'Wanderer4';   Role = 'Wanderer';   Port = 8444; Did = "did:drn:wanderer4.testnet.svrn7.net/agent/1.0/$wandererGuid" }
)

$processes = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

foreach ($node in $nodes) {
    $dbDir = Join-Path $BinDir "$($node.Port)\mem"
    [System.IO.Directory]::CreateDirectory($dbDir) | Out-Null

    $psi = [System.Diagnostics.ProcessStartInfo]@{
        FileName               = 'dotnet'
        Arguments              = "`"$dll`" --role $($node.Role) --port $($node.Port) --did $($node.Did)"
        WorkingDirectory       = $BinDir
        UseShellExecute        = $true
        CreateNoWindow         = $false
    }

    # On Windows, open each TDA in its own titled console window
    if ($IsWindows) {
        $psi.FileName        = 'cmd.exe'
        $psi.Arguments       = "/k title $($node.Name) [$($node.Role)] :$($node.Port) && dotnet `"$dll`" --role $($node.Role) --port $($node.Port) --did $($node.Did)"
        $psi.UseShellExecute = $true
    }

    $proc = [System.Diagnostics.Process]::Start($psi)
    $processes.Add($proc)
    Write-Host "Started $($node.Name)  role=$($node.Role)  port=$($node.Port)  pid=$($proc.Id)"
}

Write-Host ""
Write-Host "Testnet running. Press Ctrl+C to stop all TDAs."
Write-Host ""

try {
    # Wait until user interrupts
    while ($true) { Start-Sleep -Seconds 5 }
}
finally {
    Write-Host ""
    Write-Host "Stopping testnet..."
    foreach ($proc in $processes) {
        if (-not $proc.HasExited) {
            $proc.Kill($true)   # kill process tree
            Write-Host "  Stopped pid $($proc.Id)"
        }
    }
}
