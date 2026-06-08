function Test-LOBEPackage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
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

    # ── 1. ZIP INTEGRITY ──────────────────────────────────────────────────────
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

    # ── 2. OPC REQUIRED ENTRIES ───────────────────────────────────────────────
    Write-Host "`n[2] OPC Required Entries" -ForegroundColor White
    $hasContentTypes = $entries -contains '[Content_Types].xml'
    $hasRels         = $entries -contains '_rels/.rels'
    $nuspecEntries   = @($entries | Where-Object { $_ -match '^[^/]+\.nuspec$' })
    Write-Check "[Content_Types].xml present at root" $hasContentTypes
    Write-Check "_rels/.rels present"                 $hasRels
    Write-Check "Exactly one .nuspec at package root" ($nuspecEntries.Count -eq 1) "found: $($nuspecEntries.Count)"

    # ── 3. [Content_Types].xml ────────────────────────────────────────────────
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

    # ── 4. _rels/.rels ────────────────────────────────────────────────────────
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

    # ── 5. NUSPEC SCHEMA + REQUIRED FIELDS ───────────────────────────────────
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

    # ── 6. PACKAGE ID FORMAT ──────────────────────────────────────────────────
    Write-Host "`n[6] Package ID Format" -ForegroundColor White
    if ($id) {
        Write-Check "Only [A-Za-z0-9._-] characters"    ($id -match '^[A-Za-z0-9._\-]+$') "'$id'"
        Write-Check "Does not start with a dot"          ($id -notmatch '^\.')
        Write-Check "Does not end with a dot"            ($id -notmatch '\.$')
        Write-Check "Length ≤ 100 chars ($($id.Length))" ($id.Length -le 100)
        Write-Check "No whitespace"                      ($id -notmatch '\s')
    }

    # ── 7. VERSION FORMAT ─────────────────────────────────────────────────────
    Write-Host "`n[7] Version Format (SemVer / NuGet)" -ForegroundColor White
    if ($version) {
        Write-Check "Matches NuGet version pattern ('$version')" `
            ($version -match '^\d+\.\d+\.\d+(\.\d+)?(-[A-Za-z0-9\.\-]+)?(\+[A-Za-z0-9\.\-]+)?$')
        $parts = ($version -split '[.\-]')[0..2]
        Write-Check "Major.Minor.Patch are all integers" `
            ($parts.Count -ge 3 -and -not (($parts | ForEach-Object { $_ -as [int] }) -contains $null))
    }

    # ── 8. LICENSE ────────────────────────────────────────────────────────────
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
            Write-Check "type='expression' attribute set"              ($licNode.type -eq 'expression')
            Write-Check "SPDX expression is a known value ('$expr')"  ($validSpdx -contains $expr)
        }
    }

    # ── 9. PROJECT URL ────────────────────────────────────────────────────────
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

    # ── 10. FILE PLACEMENT ────────────────────────────────────────────────────
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

    # ── SUMMARY ───────────────────────────────────────────────────────────────
    Write-Host "`n$('─' * 60)" -ForegroundColor Cyan
    Write-Host "  Results: " -NoNewline
    Write-Host "$($counts.pass) PASS  " -ForegroundColor Green  -NoNewline
    Write-Host "$($counts.warn) WARN  " -ForegroundColor Yellow -NoNewline
    Write-Host "$($counts.fail) FAIL"   -ForegroundColor Red
    Write-Host ('─' * 60) -ForegroundColor Cyan

    ($counts.fail -eq 0)
}
