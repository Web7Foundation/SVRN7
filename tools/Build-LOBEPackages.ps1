#Requires -Version 7.0
param(
    [Parameter(Mandatory)] [string] $LobesDir,
    [Parameter(Mandatory)] [string] $OutDir
)

Import-Module (Join-Path $PSScriptRoot 'Pando.Packaging.psm1') -Force

if (-not (Test-Path $OutDir)) {
    $null = New-Item -ItemType Directory -Path $OutDir -Force
}

$lobeDirs = Get-ChildItem -Path $LobesDir -Directory |
    Where-Object { (Get-ChildItem $_.FullName -Filter '*.lobe.json' -File).Count -gt 0 }

$count = 0
foreach ($dir in $lobeDirs) {
    $nupkg = New-LOBEPackage -Path $dir.FullName -OutputDirectory $OutDir
    Write-Host "  Packaged: $([System.IO.Path]::GetFileName($nupkg))"
    $count++
}
Write-Host "Build-LOBEPackages: $count package(s) -> $OutDir"
