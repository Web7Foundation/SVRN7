#Requires -Version 7.0
<#
.SYNOPSIS
    Publishes a LOBE NuGet package to a feed.

.DESCRIPTION
    Pushes a .nupkg to a NuGet feed using 'dotnet nuget push'.
    Supports nuget.org, GitHub Packages, BaGet, and local folder feeds.

    Two source modes:
      -Path          — publish a specific .nupkg file directly
      -PackageId     — locate the package in DistDirectory by ID and version

.PARAMETER Path
    Path to a specific .nupkg file to publish.

.PARAMETER PackageId
    LOBE package ID to locate in DistDirectory (e.g. Svrn7.Email).

.PARAMETER Version
    Specific version to publish. If omitted, the latest in DistDirectory is used.

.PARAMETER DistDirectory
    Directory to search for .nupkg files when using -PackageId.
    Defaults to .\dist.

.PARAMETER Source
    NuGet feed URL or local folder path to publish to.
    Examples:
      https://api.nuget.org/v3/index.json          (nuget.org)
      https://nuget.pkg.github.com/{owner}/index.json  (GitHub Packages)
      http://localhost:5000/v3/index.json           (BaGet)
      C:\LocalFeed                                  (local folder)

.PARAMETER ApiKey
    API key for authenticated feeds (nuget.org, GitHub Packages, BaGet).
    Omit for unauthenticated local folder feeds.

.PARAMETER SkipDuplicate
    If the package version already exists on the feed, skip without error.

.OUTPUTS
    None.

.EXAMPLE
    # Publish to nuget.org
    Publish-LOBEPackage -Path .\dist\Svrn7.Email.0.8.0.nupkg `
        -Source https://api.nuget.org/v3/index.json -ApiKey $env:NUGET_API_KEY

.EXAMPLE
    # Publish to GitHub Packages by ID (picks latest from .\dist)
    Publish-LOBEPackage -PackageId Svrn7.Email `
        -Source https://nuget.pkg.github.com/mwherman2000/index.json `
        -ApiKey $env:GITHUB_TOKEN

.EXAMPLE
    # Publish all LOBEs to a local BaGet instance
    Get-ChildItem .\dist -Filter *.nupkg | ForEach-Object {
        Publish-LOBEPackage -Path $_.FullName `
            -Source http://localhost:5000/v3/index.json `
            -ApiKey local -SkipDuplicate
    }

.EXAMPLE
    # Publish to a local folder feed (no API key needed)
    Publish-LOBEPackage -Path .\dist\Svrn7.Email.0.8.0.nupkg -Source C:\LocalFeed
#>
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'File')]
param(
    [Parameter(Mandatory, ParameterSetName = 'File', Position = 0)]
    [string] $Path,

    [Parameter(Mandatory, ParameterSetName = 'ById')]
    [string] $PackageId,

    [Parameter(ParameterSetName = 'ById')]
    [string] $Version,

    [Parameter(ParameterSetName = 'ById')]
    [string] $DistDirectory = '.\dist',

    [Parameter(Mandatory)]
    [string] $Source,

    [string] $ApiKey,

    [switch] $SkipDuplicate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Verify dotnet is available ────────────────────────────────────────────────
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "'dotnet' not found on PATH. Install the .NET SDK to use Publish-LOBEPackage."
}

# ── Resolve the .nupkg file ───────────────────────────────────────────────────
$nupkgPath = $null

if ($PSCmdlet.ParameterSetName -eq 'File') {
    $nupkgPath = (Resolve-Path $Path).Path
}
else {
    $distDir  = (Resolve-Path $DistDirectory).Path
    $pattern  = if ($Version) { "$PackageId.$Version.nupkg" } else { "$PackageId.*.nupkg" }
    $candidates = @(Get-ChildItem $distDir -Filter $pattern | Sort-Object Name -Descending)
    if ($candidates.Count -eq 0) {
        throw "No package matching '$pattern' found in '$distDir'."
    }
    $nupkgPath = $candidates[0].FullName
    if ($candidates.Count -gt 1) {
        Write-Verbose "Multiple versions found; selecting latest: $([System.IO.Path]::GetFileName($nupkgPath))"
    }
}

$packageName = [System.IO.Path]::GetFileName($nupkgPath)
Write-Verbose "Publishing: $packageName"
Write-Verbose "Target feed: $Source"

# ── Build dotnet nuget push arguments ─────────────────────────────────────────
$pushArgs = @(
    'nuget', 'push', $nupkgPath,
    '--source', $Source
)

if ($ApiKey) {
    $pushArgs += '--api-key', $ApiKey
}

if ($SkipDuplicate) {
    $pushArgs += '--skip-duplicate'
}

# ── Push ──────────────────────────────────────────────────────────────────────
if ($PSCmdlet.ShouldProcess($packageName, "dotnet nuget push -> $Source")) {
    $output = & dotnet @pushArgs 2>&1
    $exitCode = $LASTEXITCODE

    $output | ForEach-Object { Write-Verbose $_ }

    if ($exitCode -ne 0) {
        # Surface the dotnet output so the caller can diagnose
        $output | Where-Object { $_ -match 'error|warning|conflict' -or $_ -notmatch '^\s*$' } |
            ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw "dotnet nuget push failed (exit $exitCode) for '$packageName'."
    }

    Write-Host "Published: $packageName -> $Source" -ForegroundColor Green
}
