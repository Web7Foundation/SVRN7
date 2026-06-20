#Requires -Version 7.0

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ── New-LOBEPackage ───────────────────────────────────────────────────────────

function New-LOBEPackage {
<#
.SYNOPSIS
    Creates a NuGet package (.nupkg) for a single LOBE module.
.DESCRIPTION
    Reads metadata from <LobeName>.lobe.json, assembles a .nupkg directly
    using System.IO.Compression — no nuget.exe required.
    All LOBE files are placed under tools/<PackageId>/.
.PARAMETER Path
    Path to the LOBE folder (e.g. .\src\Svrn7.TDA\lobes\Svrn7.Email).
.PARAMETER OutputDirectory
    Directory for the generated .nupkg. Defaults to the current directory.
.PARAMETER Version
    Package version override. If omitted, uses lobe.version from the .lobe.json.
.OUTPUTS
    [string] Full path to the generated .nupkg file.
.EXAMPLE
    New-LOBEPackage -Path .\src\Svrn7.TDA\lobes\Svrn7.Email -OutputDirectory .\dist
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

    $lobeDir = (Resolve-Path $Path).Path

    $lobeJsonFile = Get-ChildItem $lobeDir -Filter '*.lobe.json' | Select-Object -First 1
    if (-not $lobeJsonFile) { throw "No *.lobe.json found in '$lobeDir'." }
    $lobe = (Get-Content $lobeJsonFile.FullName -Raw | ConvertFrom-Json).lobe

    $packageId  = $lobe.name
    $packageVer = if ($Version) { $Version } else { $lobe.version }
    $year       = (Get-Date).Year
    $copyright  = "Copyright (c) $year $($lobe.author) (Alberta, Canada). $($lobe.license) License."

    $lobeFiles = Get-ChildItem $lobeDir -File
    if (-not $lobeFiles) { throw "No files found in '$lobeDir'." }

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

    if (-not (Test-Path $OutputDirectory)) {
        $null = New-Item -ItemType Directory -Path $OutputDirectory -Force
    }
    $outDir    = (Resolve-Path $OutputDirectory).Path
    $nupkgPath = Join-Path $outDir "$packageId.$packageVer.nupkg"

    if ($PSCmdlet.ShouldProcess("$packageId $packageVer", 'New-LOBEPackage')) {
        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        $zipStream = [System.IO.File]::Open($nupkgPath, [System.IO.FileMode]::Create)
        try {
            $archive = [System.IO.Compression.ZipArchive]::new($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
            try {
                $writeText = {
                    param([string]$name, [string]$text)
                    $entry  = $archive.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
                    $stream = $entry.Open()
                    $bytes  = $utf8NoBom.GetBytes($text)
                    $stream.Write($bytes, 0, $bytes.Length)
                    $stream.Dispose()
                }
                $writeFile = {
                    param([string]$name, [string]$filePath)
                    $entry = $archive.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
                    $dest  = $entry.Open()
                    $src   = [System.IO.File]::OpenRead($filePath)
                    $src.CopyTo($dest)
                    $src.Dispose(); $dest.Dispose()
                }
                & $writeText '[Content_Types].xml' $contentTypesXml
                & $writeText '_rels/.rels'         $relsXml
                & $writeText "$packageId.nuspec"   $nuspecContent
                foreach ($file in $lobeFiles) {
                    & $writeFile "tools/$packageId/$($file.Name)" $file.FullName
                }
            }
            finally { $archive.Dispose() }
        }
        catch {
            if (Test-Path $nupkgPath) { Remove-Item $nupkgPath -Force }
            throw
        }
        finally { $zipStream.Dispose() }

        Write-Host "Created: $nupkgPath" -ForegroundColor Green
        $nupkgPath
    }
}

# ── Test-LOBEPackage ──────────────────────────────────────────────────────────

function Test-LOBEPackage {
<#
.SYNOPSIS
    Validates a LOBE .nupkg against the NuGet package specification.
.DESCRIPTION
    Checks OPC structure, [Content_Types].xml, _rels/.rels, nuspec schema,
    package ID format, SemVer version, SPDX license, projectUrl, and
    file placement conventions for LOBE packages.
.PARAMETER Path
    Path to the .nupkg file to validate.
.OUTPUTS
    [bool] $true if all checks pass, $false if any FAIL.
.EXAMPLE
    Test-LOBEPackage -Path .\dist\Svrn7.Email.0.8.0.nupkg
.EXAMPLE
    Get-ChildItem .\dist -Filter *.nupkg | ForEach-Object { Test-LOBEPackage -Path $_.FullName }
#>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [string] $Path
    )

    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'

    $nupkgPath = (Resolve-Path $Path).Path
    $counts    = @{ pass = 0; fail = 0; warn = 0 }

    function Write-Check([string]$label, [bool]$ok, [string]$detail = '', [switch]$isWarn) {
        if ($ok) {
            Write-Host "  [PASS] $label" -ForegroundColor Green
            $counts.pass++
        } elseif ($isWarn) {
            Write-Host "  [WARN] $label$(if ($detail) { " — $detail" })" -ForegroundColor Yellow
            $counts.warn++
        } else {
            Write-Host "  [FAIL] $label$(if ($detail) { " — $detail" })" -ForegroundColor Red
            $counts.fail++
        }
    }

    function Read-Entry([string]$name) {
        $e = $zip.GetEntry($name); if (-not $e) { return $null }
        $r = [System.IO.StreamReader]::new($e.Open(), [System.Text.Encoding]::UTF8)
        $t = $r.ReadToEnd(); $r.Dispose(); $t
    }

    function Get-Field([string]$f) {
        $nuspecXml.SelectSingleNode("//n:metadata/n:$f", $nsMgr).'#text'
    }

    Write-Host "`nNuGet Package Validation: $nupkgPath" -ForegroundColor Cyan
    Write-Host ('─' * 60) -ForegroundColor Cyan

    Write-Host "`n[1] ZIP Integrity" -ForegroundColor White
    try {
        $zip     = [System.IO.Compression.ZipFile]::OpenRead($nupkgPath)
        $entries = @($zip.Entries | ForEach-Object { $_.FullName })
        Write-Check "Zip file opens without error"                $true
        Write-Check "Archive is non-empty ($($entries.Count) entries)" ($entries.Count -gt 0)
    } catch {
        Write-Check "Zip file opens without error" $false "$_"
        return $false
    }

    Write-Host "`n[2] OPC Required Entries" -ForegroundColor White
    $hasContentTypes = $entries -contains '[Content_Types].xml'
    $hasRels         = $entries -contains '_rels/.rels'
    $nuspecEntries   = @($entries | Where-Object { $_ -match '^[^/]+\.nuspec$' })
    Write-Check "[Content_Types].xml present at root" $hasContentTypes
    Write-Check "_rels/.rels present"                 $hasRels
    Write-Check "Exactly one .nuspec at package root" ($nuspecEntries.Count -eq 1) "found: $($nuspecEntries.Count)"

    Write-Host "`n[3] [Content_Types].xml" -ForegroundColor White
    if ($hasContentTypes) {
        try {
            $ctXml  = [xml](Read-Entry '[Content_Types].xml')
            $ctNs   = 'http://schemas.openxmlformats.org/package/2006/content-types'
            $ctExts = @($ctXml.Types.Default | ForEach-Object { $_.Extension })
            Write-Check "Parses as valid XML"         $true
            Write-Check "Correct OPC namespace"       ($ctXml.DocumentElement.NamespaceURI -eq $ctNs) $ctXml.DocumentElement.NamespaceURI
            Write-Check "'rels' extension declared"   ($ctExts -contains 'rels')
            Write-Check "'nuspec' extension declared" ($ctExts -contains 'nuspec')
            $pkgExts = @($entries |
                Where-Object { $_ -notmatch '^\[' -and $_ -notmatch '^_rels' } |
                ForEach-Object { [System.IO.Path]::GetExtension($_).TrimStart('.') } |
                Where-Object { $_ } | Select-Object -Unique)
            foreach ($ext in $pkgExts) {
                Write-Check "Extension '$ext' covered in Content_Types" ($ctExts -contains $ext)
            }
        } catch { Write-Check "Parses as valid XML" $false "$_" }
    }

    Write-Host "`n[4] _rels/.rels Relationships" -ForegroundColor White
    if ($hasRels) {
        try {
            $relsXml = [xml](Read-Entry '_rels/.rels')
            $relsNs  = 'http://schemas.openxmlformats.org/package/2006/relationships'
            Write-Check "Parses as valid XML"                 $true
            Write-Check "Correct OPC relationships namespace" ($relsXml.DocumentElement.NamespaceURI -eq $relsNs)
            $manifestRel = $relsXml.Relationships.Relationship |
                Where-Object { $_.Type -eq 'http://schemas.microsoft.com/packaging/2010/07/manifest' }
            Write-Check "Manifest relationship present" ($null -ne $manifestRel)
            if ($manifestRel -and $nuspecEntries.Count -eq 1) {
                $expectedTarget = "/$($nuspecEntries[0])"
                Write-Check "Manifest target matches nuspec ('$($manifestRel.Target)')" `
                    ($manifestRel.Target -eq $expectedTarget) "expected '$expectedTarget'"
            }
            Write-Check "Relationship Id is non-empty" ($manifestRel.Id -and $manifestRel.Id.Trim())
        } catch { Write-Check "Parses as valid XML" $false "$_" }
    }

    Write-Host "`n[5] Nuspec — Schema and Required Fields" -ForegroundColor White
    $id = $version = $nuspecXml = $nsMgr = $null
    if ($nuspecEntries.Count -eq 1) {
        try {
            $nuspecXml = [xml](Read-Entry $nuspecEntries[0])
            Write-Check "Parses as valid XML" $true
            $nuspecNs = 'http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd'
            Write-Check "Correct nuspec namespace (2012/06)" `
                ($nuspecXml.DocumentElement.NamespaceURI -eq $nuspecNs) `
                $nuspecXml.DocumentElement.NamespaceURI
            $nsMgr = [System.Xml.XmlNamespaceManager]::new($nuspecXml.NameTable)
            $nsMgr.AddNamespace('n', $nuspecNs)
            $id          = Get-Field 'id'
            $version     = Get-Field 'version'
            $authors     = Get-Field 'authors'
            $description = Get-Field 'description'
            Write-Check "<id> present and non-empty"          ($id          -and $id.Trim())
            Write-Check "<version> present and non-empty"     ($version     -and $version.Trim())
            Write-Check "<authors> present and non-empty"     ($authors     -and $authors.Trim())
            Write-Check "<description> present and non-empty" ($description -and $description.Trim())
        } catch { Write-Check "Parses as valid XML" $false "$_" }
    }

    Write-Host "`n[6] Package ID Format" -ForegroundColor White
    if ($id) {
        Write-Check "Only [A-Za-z0-9._-] characters"    ($id -match '^[A-Za-z0-9._\-]+$') "'$id'"
        Write-Check "Does not start with a dot"          ($id -notmatch '^\.')
        Write-Check "Does not end with a dot"            ($id -notmatch '\.$')
        Write-Check "Length ≤ 100 chars ($($id.Length))" ($id.Length -le 100)
        Write-Check "No whitespace"                      ($id -notmatch '\s')
    }

    Write-Host "`n[7] Version Format (SemVer / NuGet)" -ForegroundColor White
    if ($version) {
        Write-Check "Matches NuGet version pattern ('$version')" `
            ($version -match '^\d+\.\d+\.\d+(\.\d+)?(-[A-Za-z0-9\.\-]+)?(\+[A-Za-z0-9\.\-]+)?$')
        $parts = ($version -split '[.\-]')[0..2]
        Write-Check "Major.Minor.Patch are all integers" `
            ($parts.Count -ge 3 -and -not (($parts | ForEach-Object { $_ -as [int] }) -contains $null))
    }

    Write-Host "`n[8] License" -ForegroundColor White
    if ($nuspecXml -and $nsMgr) {
        $licNode    = $nuspecXml.SelectSingleNode('//n:metadata/n:license',    $nsMgr)
        $licUrlNode = $nuspecXml.SelectSingleNode('//n:metadata/n:licenseUrl', $nsMgr)
        Write-Check "License declared (<license> or <licenseUrl>)" ($null -ne $licNode -or $null -ne $licUrlNode)
        if ($licUrlNode -and -not $licNode) {
            Write-Check "<licenseUrl> deprecated; prefer <license type='expression'>" $false -isWarn
        }
        if ($licNode) {
            $validSpdx = @('MIT','Apache-2.0','GPL-2.0-only','GPL-3.0-only','BSD-2-Clause','BSD-3-Clause','ISC','MPL-2.0','LGPL-2.1-only','LGPL-3.0-only')
            $expr = $licNode.'#text'
            Write-Check "type='expression' attribute set"             ($licNode.type -eq 'expression')
            Write-Check "SPDX expression is a known value ('$expr')" ($validSpdx -contains $expr)
        }
    }

    Write-Host "`n[9] projectUrl" -ForegroundColor White
    if ($nuspecXml -and $nsMgr) {
        $projUrl = Get-Field 'projectUrl'
        if ($projUrl) {
            try {
                $uri = [System.Uri]$projUrl
                Write-Check "Valid absolute URI ('$projUrl')" $uri.IsAbsoluteUri
                Write-Check "Uses https scheme"               ($uri.Scheme -eq 'https')
            } catch { Write-Check "Valid URI" $false $projUrl }
        } else {
            Write-Check "projectUrl present" $false -isWarn
        }
    }

    Write-Host "`n[10] File Placement" -ForegroundColor White
    $toolsFiles  = @($entries | Where-Object { $_ -match '^tools/' })
    $libFiles    = @($entries | Where-Object { $_ -match '^lib/' })
    $lobeNames   = @($toolsFiles | ForEach-Object { [System.IO.Path]::GetFileName($_) } | Where-Object { $_ })
    $toolFolders = @($toolsFiles | ForEach-Object { ($_ -split '/')[1] } | Select-Object -Unique)
    Write-Check "LOBE files in tools/ ($($toolsFiles.Count) files)"            ($toolsFiles.Count -gt 0)
    Write-Check "No files in lib/ (correct — PS module, not a .NET assembly)"  ($libFiles.Count   -eq 0)
    Write-Check "All tools/ files under single named subfolder ('$($toolFolders -join ',')')" ($toolFolders.Count -eq 1)
    Write-Check ".psd1 manifest present (recommended)" `
        ([bool]($lobeNames | Where-Object { $_ -like '*.psd1' })) `
        -isWarn:(-not ($lobeNames | Where-Object { $_ -like '*.psd1' }))
    Write-Check ".psm1 implementation present"  ([bool]($lobeNames | Where-Object { $_ -like '*.psm1'      }))
    Write-Check ".lobe.json descriptor present" ([bool]($lobeNames | Where-Object { $_ -like '*.lobe.json' }))

    $zip.Dispose()

    Write-Host "`n$('─' * 60)" -ForegroundColor Cyan
    Write-Host "  Results: " -NoNewline
    Write-Host "$($counts.pass) PASS  " -ForegroundColor Green  -NoNewline
    Write-Host "$($counts.warn) WARN  " -ForegroundColor Yellow -NoNewline
    Write-Host "$($counts.fail) FAIL"   -ForegroundColor Red
    Write-Host ('─' * 60) -ForegroundColor Cyan

    ($counts.fail -eq 0)
}

# ── Install-LOBEPackage ───────────────────────────────────────────────────────

function Install-LOBEPackage {
<#
.SYNOPSIS
    Installs a LOBE NuGet package into a TDA lobes directory.
.DESCRIPTION
    Extracts tools/<LobeName>/* from a .nupkg into LobesDirectory/<LobeName>/.
    JIT LOBEs are auto-discovered by the TDA from their .lobe.json — no config registration needed.
    Use -LoadMode Eager only to pre-load a LOBE at TDA startup (requires restart).
.PARAMETER Path
    Path to a specific .nupkg file to install.
.PARAMETER PackageId
    LOBE package ID to locate in Source.
.PARAMETER Source
    Local directory or global NuGet cache root. Defaults to ~\.nuget\packages.
.PARAMETER Version
    Specific version to install. Defaults to latest found in Source.
.PARAMETER LobesDirectory
    Destination parent directory. Defaults to .\lobes.
.PARAMETER LoadMode
    Eager: adds the LOBE to lobes.config.json eager list (TDA restart required).
    JIT: files are dropped only; auto-discovered by the TDA FileSystemWatcher.
.PARAMETER Force
    Overwrite an existing LOBE installation.
.OUTPUTS
    [string] Path to the installed LOBE directory.
.EXAMPLE
    Install-LOBEPackage -Path .\dist\Svrn7.Email.0.8.0.nupkg -LobesDirectory .\src\Svrn7.TDA\lobes
.EXAMPLE
    Install-LOBEPackage -Path .\dist\Svrn7.Common.0.8.0.nupkg -LobesDirectory .\src\Svrn7.TDA\lobes -LoadMode Eager
.EXAMPLE
    Install-LOBEPackage -PackageId Svrn7.Email -Source .\dist -LobesDirectory .\src\Svrn7.TDA\lobes
#>
    [CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'File')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'File', Position = 0)]
        [string] $Path,

        [Parameter(Mandatory, ParameterSetName = 'Feed')]
        [string] $PackageId,

        [Parameter(ParameterSetName = 'Feed')]
        [string] $Source = (Join-Path $env:USERPROFILE '.nuget\packages'),

        [Parameter(ParameterSetName = 'Feed')]
        [string] $Version,

        [string] $LobesDirectory = '.\lobes',

        [ValidateSet('Eager', 'JIT')]
        [string] $LoadMode,

        [switch] $Force
    )

    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'

    $nupkgPath = $null

    if ($PSCmdlet.ParameterSetName -eq 'File') {
        $nupkgPath = (Resolve-Path $Path).Path
    }
    else {
        $sourceDir     = (Resolve-Path $Source).Path
        $isGlobalCache = $sourceDir -like '*\.nuget\packages*'

        if ($isGlobalCache) {
            $pkgCacheDir = Join-Path $sourceDir $PackageId.ToLower()
            if (-not (Test-Path $pkgCacheDir)) {
                throw "Package '$PackageId' not found in global cache at '$pkgCacheDir'."
            }
            $versionDirs = Get-ChildItem $pkgCacheDir -Directory | Sort-Object Name -Descending
            $chosen = if ($Version) {
                $versionDirs | Where-Object { $_.Name -eq $Version } | Select-Object -First 1
            } else {
                $versionDirs | Select-Object -First 1
            }
            if (-not $chosen) { throw "Version '$Version' of '$PackageId' not found in global cache." }
            $nupkgPath = Get-ChildItem $chosen.FullName -Filter '*.nupkg' |
                Select-Object -First 1 -ExpandProperty FullName
        }
        else {
            $pattern    = if ($Version) { "$PackageId.$Version.nupkg" } else { "$PackageId.*.nupkg" }
            $candidates = @(Get-ChildItem $sourceDir -Filter $pattern | Sort-Object Name -Descending)
            if ($candidates.Count -eq 0) { throw "No package matching '$pattern' found in '$sourceDir'." }
            $nupkgPath = $candidates[0].FullName
            if ($candidates.Count -gt 1) {
                Write-Verbose "Multiple versions found; selecting latest: $([System.IO.Path]::GetFileName($nupkgPath))"
            }
        }
    }

    if (-not $nupkgPath -or -not (Test-Path $nupkgPath)) { throw "Package file not found: '$nupkgPath'." }
    Write-Verbose "Source package: $nupkgPath"

    $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkgPath)
    try {
        $toolsEntries = @($zip.Entries | Where-Object { $_.FullName -match '^tools/[^/]+/.+' })
        if ($toolsEntries.Count -eq 0) { throw "No files under tools/ in '$nupkgPath'. Not a LOBE package?" }

        $lobeName = ($toolsEntries[0].FullName -split '/')[1]

        if (-not (Test-Path $LobesDirectory)) { $null = New-Item -ItemType Directory -Path $LobesDirectory -Force }
        $lobesDir    = (Resolve-Path $LobesDirectory).Path
        $lobeDestDir = Join-Path $lobesDir $lobeName

        if (Test-Path $lobeDestDir) {
            if (-not $Force) { throw "'$lobeDestDir' already exists. Use -Force to overwrite." }
            if ($PSCmdlet.ShouldProcess($lobeDestDir, 'Remove existing LOBE')) {
                Remove-Item $lobeDestDir -Recurse -Force
            }
        }

        if ($PSCmdlet.ShouldProcess($lobeDestDir, "Install LOBE '$lobeName'")) {
            $null = New-Item -ItemType Directory -Path $lobeDestDir -Force

            foreach ($entry in $toolsEntries) {
                $fileName = [System.IO.Path]::GetFileName($entry.FullName)
                $destFile = Join-Path $lobeDestDir $fileName
                $src  = $entry.Open()
                $dest = [System.IO.File]::Create($destFile)
                try   { $src.CopyTo($dest) }
                finally { $src.Dispose(); $dest.Dispose() }
                Write-Verbose "  Extracted: $fileName"
            }

            Write-Host "Installed: $lobeName -> $lobeDestDir" -ForegroundColor Green

            if ($LoadMode -eq 'Eager') {
                $configPath = Join-Path $lobesDir 'lobes.config.json'
                if (-not (Test-Path $configPath)) { throw "lobes.config.json not found at '$configPath'." }

                $lobeJsonFile = Get-ChildItem $lobeDestDir -Filter '*.lobe.json' | Select-Object -First 1
                if (-not $lobeJsonFile) { throw "No .lobe.json in '$lobeDestDir'. Cannot determine entry point." }
                $lobeModule  = (Get-Content $lobeJsonFile.FullName -Raw | ConvertFrom-Json).lobe.module
                $configEntry = "$lobeName/$lobeModule"

                $config    = Get-Content $configPath -Raw | ConvertFrom-Json
                $eagerList = @($config.eager)

                if ($eagerList -contains $configEntry) {
                    Write-Verbose "'$configEntry' already in eager list — no change."
                } else {
                    $config.eager = @($eagerList) + $configEntry
                    if ($PSCmdlet.ShouldProcess($configPath, "Register '$configEntry' as Eager")) {
                        $config | ConvertTo-Json -Depth 5 | Set-Content $configPath -Encoding UTF8
                        Write-Host "Registered: $configEntry as Eager in lobes.config.json" -ForegroundColor Green
                        Write-Warning "TDA restart required to apply eager loading for '$lobeName'."
                    }
                }
            } elseif ($LoadMode -eq 'JIT') {
                Write-Verbose "LoadMode=JIT: '$lobeName' auto-discovered by TDA FileSystemWatcher — no config change needed."
            }

            $lobeDestDir
        }
    }
    finally { $zip.Dispose() }
}

# ── Publish-LOBEPackage ───────────────────────────────────────────────────────

function Publish-LOBEPackage {
<#
.SYNOPSIS
    Publishes a LOBE NuGet package to a feed.
.DESCRIPTION
    Pushes a .nupkg to a NuGet feed using 'dotnet nuget push'.
    Supports nuget.org, GitHub Packages, BaGet, and local folder feeds.
.PARAMETER Path
    Path to a specific .nupkg file to publish.
.PARAMETER PackageId
    LOBE package ID to locate in DistDirectory.
.PARAMETER Version
    Specific version to publish. Defaults to latest in DistDirectory.
.PARAMETER DistDirectory
    Directory to search for .nupkg files. Defaults to .\dist.
.PARAMETER Source
    NuGet feed URL or local folder path.
.PARAMETER ApiKey
    API key for authenticated feeds. Omit for local folder feeds.
.PARAMETER SkipDuplicate
    Skip without error if the version already exists on the feed.
.EXAMPLE
    Publish-LOBEPackage -Path .\dist\Svrn7.Email.0.8.0.nupkg `
        -Source https://api.nuget.org/v3/index.json -ApiKey $env:NUGET_API_KEY
.EXAMPLE
    Get-ChildItem .\dist -Filter *.nupkg | ForEach-Object {
        Publish-LOBEPackage -Path $_.FullName -Source http://localhost:5000/v3/index.json -ApiKey local -SkipDuplicate
    }
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

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "'dotnet' not found on PATH. Install the .NET SDK to use Publish-LOBEPackage."
    }

    $nupkgPath = $null

    if ($PSCmdlet.ParameterSetName -eq 'File') {
        $nupkgPath = (Resolve-Path $Path).Path
    }
    else {
        $distDir    = (Resolve-Path $DistDirectory).Path
        $pattern    = if ($Version) { "$PackageId.$Version.nupkg" } else { "$PackageId.*.nupkg" }
        $candidates = @(Get-ChildItem $distDir -Filter $pattern | Sort-Object Name -Descending)
        if ($candidates.Count -eq 0) { throw "No package matching '$pattern' found in '$distDir'." }
        $nupkgPath = $candidates[0].FullName
        if ($candidates.Count -gt 1) {
            Write-Verbose "Multiple versions found; selecting latest: $([System.IO.Path]::GetFileName($nupkgPath))"
        }
    }

    $packageName = [System.IO.Path]::GetFileName($nupkgPath)
    Write-Verbose "Publishing: $packageName"
    Write-Verbose "Target feed: $Source"

    $pushArgs = @('nuget', 'push', $nupkgPath, '--source', $Source)
    if ($ApiKey)        { $pushArgs += '--api-key', $ApiKey }
    if ($SkipDuplicate) { $pushArgs += '--skip-duplicate' }

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
