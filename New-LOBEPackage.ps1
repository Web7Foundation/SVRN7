#Requires -Version 7.0
<#
.SYNOPSIS
    Creates a NuGet package (.nupkg) for a single LOBE module.

.DESCRIPTION
    Reads metadata from <LobeName>.lobe.json, assembles a .nupkg (zip) directly
    using System.IO.Compression — no nuget.exe required.
    All LOBE files (*.psd1, *.psm1, *.lobe.json) are placed under tools/<PackageId>/.

.PARAMETER Path
    Path to the LOBE folder (e.g. .\src\Svrn7.TDA\lobes\Svrn7.Email).

.PARAMETER OutputDirectory
    Directory for the generated .nupkg. Defaults to the current directory.

.PARAMETER Version
    Package version override. If omitted, uses lobe.version from the .lobe.json.

.OUTPUTS
    [string] Full path to the generated .nupkg file.

.EXAMPLE
    New-LOBEPackage -Path .\src\Svrn7.TDA\lobes\Svrn7.Email

.EXAMPLE
    New-LOBEPackage -Path .\src\Svrn7.TDA\lobes\Svrn7.Email -OutputDirectory .\dist -Version 0.9.0

.EXAMPLE
    Get-ChildItem .\src\Svrn7.TDA\lobes -Directory |
        ForEach-Object { New-LOBEPackage -Path $_.FullName -OutputDirectory .\dist }
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $Path,

    [string] $OutputDirectory = '.',

    [string] $Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression

$lobeDir = (Resolve-Path $Path).Path

# ── Locate and parse the .lobe.json ──────────────────────────────────────────
$lobeJsonFile = Get-ChildItem $lobeDir -Filter '*.lobe.json' | Select-Object -First 1
if (-not $lobeJsonFile) {
    throw "No *.lobe.json found in '$lobeDir'."
}
$lobe = (Get-Content $lobeJsonFile.FullName -Raw | ConvertFrom-Json).lobe

$packageId  = $lobe.name
$packageVer = if ($Version) { $Version } else { $lobe.version }
$year       = (Get-Date).Year
$copyright  = "Copyright (c) $year $($lobe.author) (Alberta, Canada). $($lobe.license) License."

# ── Gather LOBE files ─────────────────────────────────────────────────────────
$lobeFiles = Get-ChildItem $lobeDir -File
if (-not $lobeFiles) {
    throw "No files found in '$lobeDir'."
}

# ── Build nuspec content ──────────────────────────────────────────────────────
$nuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
  <metadata>
    <id>$packageId</id>
    <version>$packageVer</version>
    <authors>$($lobe.author)</authors>
    <license type="expression">$($lobe.license)</license>
    <projectUrl>$($lobe.website)</projectUrl>
    <description>$($lobe.description)</description>
    <copyright>$copyright</copyright>
    <tags>SVRN7 Web70 DIDComm TDA LOBE ParchmentProgramming $packageId</tags>
  </metadata>
</package>
"@

# ── Build [Content_Types].xml from actual file extensions ─────────────────────
$extensions = @('rels', 'nuspec') + ($lobeFiles | ForEach-Object { $_.Extension.TrimStart('.') }) |
    Select-Object -Unique | Where-Object { $_ }

$typeEntries = $extensions | ForEach-Object {
    $ct = switch ($_) {
        'rels'  { 'application/vnd.openxmlformats-package.relationships+xml' }
        default { 'application/octet-stream' }
    }
    "  <Default Extension=`"$_`" ContentType=`"$ct`" />"
}

$contentTypesXml = @"
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
$($typeEntries -join "`n")
</Types>
"@

$relsXml = @"
<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/$packageId.nuspec" Id="R1" />
</Relationships>
"@

# ── Write .nupkg ──────────────────────────────────────────────────────────────
if (-not (Test-Path $OutputDirectory)) {
    $null = New-Item -ItemType Directory -Path $OutputDirectory -Force
}
$outDir    = (Resolve-Path $OutputDirectory).Path
$nupkgPath = Join-Path $outDir "$packageId.$packageVer.nupkg"

if ($PSCmdlet.ShouldProcess("$packageId $packageVer", 'New-LOBEPackage')) {
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)

    $zipStream = [System.IO.File]::Open($nupkgPath, [System.IO.FileMode]::Create)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $zipStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false   # leaveOpen: false — archive owns the stream
        )
        try {
            # helper: write a UTF-8 text entry
            $writeText = {
                param([string]$name, [string]$text)
                $entry  = $archive.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
                $stream = $entry.Open()
                $bytes  = $utf8NoBom.GetBytes($text)
                $stream.Write($bytes, 0, $bytes.Length)
                $stream.Dispose()
            }

            # helper: write a binary file entry
            $writeFile = {
                param([string]$name, [string]$filePath)
                $entry  = $archive.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
                $dest   = $entry.Open()
                $src    = [System.IO.File]::OpenRead($filePath)
                $src.CopyTo($dest)
                $src.Dispose()
                $dest.Dispose()
            }

            & $writeText '[Content_Types].xml' $contentTypesXml
            & $writeText '_rels/.rels'         $relsXml
            & $writeText "$packageId.nuspec"   $nuspecContent

            foreach ($file in $lobeFiles) {
                & $writeFile "tools/$packageId/$($file.Name)" $file.FullName
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    catch {
        # Remove partial output on failure
        if (Test-Path $nupkgPath) { Remove-Item $nupkgPath -Force }
        throw
    }
    finally {
        $zipStream.Dispose()
    }

    Write-Host "Created: $nupkgPath" -ForegroundColor Green
    $nupkgPath
}
