function Publish-LOBEPackage {
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

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "'dotnet' not found on PATH. Install the .NET SDK to use Publish-LOBEPackage."
    }

    # ── Resolve the .nupkg file ───────────────────────────────────────────────
    $nupkgPath = $null

    if ($PSCmdlet.ParameterSetName -eq 'File') {
        $nupkgPath = (Resolve-Path $Path).Path
    }
    else {
        $distDir    = (Resolve-Path $DistDirectory).Path
        $pattern    = if ($Version) { "$PackageId.$Version.nupkg" } else { "$PackageId.*.nupkg" }
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

    # ── Build dotnet nuget push arguments ─────────────────────────────────────
    $pushArgs = @('nuget', 'push', $nupkgPath, '--source', $Source)

    if ($ApiKey)        { $pushArgs += '--api-key', $ApiKey }
    if ($SkipDuplicate) { $pushArgs += '--skip-duplicate' }

    # ── Push ──────────────────────────────────────────────────────────────────
    if ($PSCmdlet.ShouldProcess($packageName, "dotnet nuget push -> $Source")) {
        $output   = & dotnet @pushArgs 2>&1
        $exitCode = $LASTEXITCODE

        $output | ForEach-Object { Write-Verbose $_ }

        if ($exitCode -ne 0) {
            $output | Where-Object { $_ -notmatch '^\s*$' } |
                ForEach-Object { Write-Host $_ -ForegroundColor Red }
            throw "dotnet nuget push failed (exit $exitCode) for '$packageName'."
        }

        Write-Host "Published: $packageName -> $Source" -ForegroundColor Green
    }
}
