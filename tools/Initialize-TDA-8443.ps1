#Requires -Version 7.2
<#
.SYNOPSIS
    Starts a local SVRN7 testnet with four Wanderer TDA instances.

.DESCRIPTION
    Launches four TDA processes in separate windows. Every TDA starts as a
    Wanderer — role is additive and established after startup via cmdlets.

        Wanderer1   --port 8441
        Wanderer2   --port 8442
        Wanderer3   --port 8443
        Wanderer4   --port 8444

    On first run each TDA auto-generates a Wanderer identity (secp256k1 key pair,
    DID, DIDDocument) and persists it to <BinDir>/{port}/mem/agent-identity.json.

    Each TDA stores its databases under:
        <BinDir>/{port}/mem/

    Press Ctrl+C in this window to stop all four processes.

.PARAMETER BinDir
    Path to the TDA output directory containing Svrn7.TDA.dll.
    Default: src\Svrn7.TDA\bin\Debug\net8.0 relative to the repo root.
#>
param(
    [string] $BinDir = (Join-Path $PSScriptRoot '..\src\Svrn7.TDA\bin\Debug\net8.0')
)

$dll = Join-Path $BinDir 'Svrn7.TDA.dll'
if (-not (Test-Path $dll)) {
    Write-Error "Svrn7.TDA.dll not found at '$dll'. Build the solution first."
    exit 1
}

$nodes = @(
    @{ Name = 'Wanderer3'; Port = 8443 }
)
# Each node's Name is passed as --name to the TDA so it becomes the Svrn7Name in
# the Wanderer DIDDocument on first run.

$processes = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

foreach ($node in $nodes) {
    $dbDir = Join-Path $BinDir "$($node.Port)\mem"
    [System.IO.Directory]::CreateDirectory($dbDir) | Out-Null

    $psi = [System.Diagnostics.ProcessStartInfo]@{
        FileName               = 'dotnet'
        Arguments              = "`"$dll`" --port $($node.Port) --name $($node.Name)"
        WorkingDirectory       = $BinDir
        UseShellExecute        = $true
        CreateNoWindow         = $false
    }

    # On Windows, open each TDA in its own titled console window
    if ($IsWindows) {
        $psi.FileName        = 'cmd.exe'
        $psi.Arguments       = "/k title $($node.Name) [Wanderer] :$($node.Port) && dotnet `"$dll`" --port $($node.Port) --name $($node.Name) --federationdomain svrn7.net" #" --reset"
        $psi.UseShellExecute = $true
    }

    $cmd = "dotnet `"$dll`" --port $($node.Port) --name $($node.Name)"
    Write-Host "Launching: $cmd"
    $proc = [System.Diagnostics.Process]::Start($psi)
    $processes.Add($proc)
    Write-Host "Started   $($node.Name)  role=Wanderer  port=$($node.Port)  pid=$($proc.Id)"
}

