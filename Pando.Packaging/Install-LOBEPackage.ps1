function Install-LOBEPackage {
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

    # ── Resolve the .nupkg file ───────────────────────────────────────────────
    $nupkgPath = $null

    if ($PSCmdlet.ParameterSetName -eq 'File') {
        $nupkgPath = (Resolve-Path $Path).Path
    }
    else {
        $sourceDir      = (Resolve-Path $Source).Path
        $isGlobalCache  = $sourceDir -like '*\.nuget\packages*'

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
            if (-not $chosen) {
                throw "Version '$Version' of '$PackageId' not found in global cache."
            }
            $nupkgPath = Get-ChildItem $chosen.FullName -Filter '*.nupkg' |
                Select-Object -First 1 -ExpandProperty FullName
        }
        else {
            $pattern    = if ($Version) { "$PackageId.$Version.nupkg" } else { "$PackageId.*.nupkg" }
            $candidates = @(Get-ChildItem $sourceDir -Filter $pattern | Sort-Object Name -Descending)
            if ($candidates.Count -eq 0) {
                throw "No package matching '$pattern' found in '$sourceDir'."
            }
            $nupkgPath = $candidates[0].FullName
            if ($candidates.Count -gt 1) {
                Write-Verbose "Multiple versions found; selecting latest: $([System.IO.Path]::GetFileName($nupkgPath))"
            }
        }
    }

    if (-not $nupkgPath -or -not (Test-Path $nupkgPath)) {
        throw "Package file not found: '$nupkgPath'."
    }

    Write-Verbose "Source package: $nupkgPath"

    # ── Open package and locate tools/ entries ────────────────────────────────
    $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkgPath)
    try {
        $toolsEntries = @($zip.Entries | Where-Object { $_.FullName -match '^tools/[^/]+/.+' })

        if ($toolsEntries.Count -eq 0) {
            throw "No files found under tools/ in '$nupkgPath'. Not a LOBE package?"
        }

        $lobeName = ($toolsEntries[0].FullName -split '/')[1]

        # ── Resolve destination ───────────────────────────────────────────────
        if (-not (Test-Path $LobesDirectory)) {
            $null = New-Item -ItemType Directory -Path $LobesDirectory -Force
        }
        $lobesDir    = (Resolve-Path $LobesDirectory).Path
        $lobeDestDir = Join-Path $lobesDir $lobeName

        if (Test-Path $lobeDestDir) {
            if (-not $Force) {
                throw "'$lobeDestDir' already exists. Use -Force to overwrite."
            }
            if ($PSCmdlet.ShouldProcess($lobeDestDir, 'Remove existing LOBE')) {
                Remove-Item $lobeDestDir -Recurse -Force
            }
        }

        # ── Extract files ─────────────────────────────────────────────────────
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

            # ── Update lobes.config.json ──────────────────────────────────────
            if ($LoadMode) {
                $configPath = Join-Path $lobesDir 'lobes.config.json'
                if (-not (Test-Path $configPath)) {
                    throw "lobes.config.json not found at '$configPath'. Cannot register LOBE."
                }

                $lobeJsonFile = Get-ChildItem $lobeDestDir -Filter '*.lobe.json' | Select-Object -First 1
                if (-not $lobeJsonFile) {
                    throw "No .lobe.json found in '$lobeDestDir'. Cannot determine module entry point."
                }
                $lobeModule  = (Get-Content $lobeJsonFile.FullName -Raw | ConvertFrom-Json).lobe.module
                $configEntry = "$lobeName/$lobeModule"

                $config      = Get-Content $configPath -Raw | ConvertFrom-Json
                $sectionKey  = $LoadMode.ToLower()
                $otherKey    = if ($sectionKey -eq 'eager') { 'jit' } else { 'eager' }
                $sectionList = @($config.$sectionKey)
                $otherList   = @($config.$otherKey)

                if ($sectionList -contains $configEntry) {
                    Write-Verbose "'$configEntry' already in $sectionKey — no change."
                } else {
                    if ($otherList -contains $configEntry) {
                        Write-Warning "'$configEntry' is currently in '$otherKey'. Moving to '$sectionKey'."
                        $config.$otherKey = @($otherList | Where-Object { $_ -ne $configEntry })
                    }
                    $config.$sectionKey = @($sectionList) + $configEntry

                    if ($PSCmdlet.ShouldProcess($configPath, "Register '$configEntry' as $LoadMode")) {
                        $config | ConvertTo-Json -Depth 5 | Set-Content $configPath -Encoding UTF8
                        Write-Host "Registered: $configEntry as $LoadMode in lobes.config.json" -ForegroundColor Green
                    }
                }
            }

            $lobeDestDir
        }
    }
    finally {
        $zip.Dispose()
    }
}
