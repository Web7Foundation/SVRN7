#Requires -Version 7.2
#Requires -PSEdition Core
<#
.SYNOPSIS
    Pester 5 tests for the Svrn7.Common, Svrn7.Federation, and Svrn7.Society LOBEs.

.DESCRIPTION
    These tests cover the PowerShell LOBE layer independently of the C# test suite.
    They deliberately avoid loading the Svrn7 .NET assemblies (no Initialize-Svrn7FederationDriver
    call), so they run without a compiled solution and without a live TDA.

    Requires Pester 5+. Install if needed:
        Install-Module Pester -MinimumVersion 5.0 -Force -SkipPublisherCheck

    Run with:
        Invoke-Pester .\tests\Svrn7.Lobes.Tests.ps1 -Output Detailed
#>

BeforeAll {
    $LobesDir      = Join-Path $PSScriptRoot '..\src\Svrn7.TDA\lobes'
    $CommonPsm1    = Join-Path $LobesDir 'Svrn7.Common\Svrn7.Common.psm1'
    $FederationPsm1 = Join-Path $LobesDir 'Svrn7.Federation\Svrn7.Federation.psm1'
    $SocietyPsm1   = Join-Path $LobesDir 'Svrn7.Society\Svrn7.Society.psm1'

    # Load Common helpers directly into this script's scope via the scriptblock pattern.
    # This is the same mechanism Federation and Society use — avoids .psm1 extension scoping.
    . ([scriptblock]::Create([System.IO.File]::ReadAllText($CommonPsm1)))
}

# ── Send-DIDCommMessage: exported by Federation ───────────────────────────────

Describe 'Send-DIDCommMessage exported by Svrn7.Federation' {
    BeforeAll {
        # Import without $SVRN7_LOBES_DIR so Common is loaded into Federation's scope.
        $LobesDir = Join-Path $PSScriptRoot '..\src\Svrn7.TDA\lobes'
        Import-Module (Join-Path $LobesDir 'Svrn7.Federation\Svrn7.Federation.psm1') -Force -WarningAction SilentlyContinue
    }

    AfterAll {
        Remove-Module Svrn7.Federation -ErrorAction SilentlyContinue
    }

    It 'Send-DIDCommMessage is callable after importing Svrn7.Federation' {
        Get-Command Send-DIDCommMessage -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
    }

    It 'Send-DIDCommMessage -Body is mandatory' {
        $cmd = Get-Command Send-DIDCommMessage
        $bodyAttr = $cmd.Parameters['Body'].Attributes |
            Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] } |
            Select-Object -First 1
        $bodyAttr.Mandatory | Should -BeTrue
    }

    It 'Send-DIDCommMessage -Uri has default http://localhost:8443/didcomm' {
        # Inspect default via script text — parameter defaults live in the AST.
        # Note: [regex]::Escape(...) must be wrapped in parens; Pester treats a bare
        # method-call argument as a positional -Because string.
        $cmd = Get-Command Send-DIDCommMessage
        $scriptText = $cmd.ScriptBlock.ToString()
        $scriptText | Should -Match ([regex]::Escape('http://localhost:8443/didcomm'))
    }

    It 'Send-DIDCommMessage -ContentType has default application/didcomm-plain+json' {
        $cmd = Get-Command Send-DIDCommMessage
        $scriptText = $cmd.ScriptBlock.ToString()
        $scriptText | Should -Match ([regex]::Escape('application/didcomm-plain+json'))
    }
}

# ── Send-DIDCommMessage: exported by Svrn7.Society ────────────────────────────

Describe 'Send-DIDCommMessage exported by Svrn7.Society' {
    BeforeAll {
        $LobesDir = Join-Path $PSScriptRoot '..\src\Svrn7.TDA\lobes'
        # Society loads Common in standalone mode too — import without TDA context.
        Import-Module (Join-Path $LobesDir 'Svrn7.Society\Svrn7.Society.psm1') -Force -WarningAction SilentlyContinue
    }

    AfterAll {
        Remove-Module Svrn7.Society -ErrorAction SilentlyContinue
    }

    It 'Send-DIDCommMessage is callable after importing Svrn7.Society' {
        Get-Command Send-DIDCommMessage -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
    }
}

# ── Build-CanonicalTransferJson ───────────────────────────────────────────────
# Common.psm1 is dot-sourced directly in BeforeAll above.

Describe 'Build-CanonicalTransferJson' {
    It 'produces fields in normative order (§5.2)' {
        $json = Build-CanonicalTransferJson `
            -PayerDid    'did:drn:payer' `
            -PayeeDid    'did:drn:payee' `
            -AmountGrana 500 `
            -Nonce       'abc123' `
            -Timestamp   '2025-01-01T00:00:00Z' `
            -Memo        $null

        $doc   = [System.Text.Json.JsonDocument]::Parse($json)
        $names = $doc.RootElement.EnumerateObject() | ForEach-Object { $_.Name }
        $names | Should -Be @('PayerDid', 'PayeeDid', 'AmountGrana', 'Nonce', 'Timestamp', 'Memo')
    }

    It 'serializes a non-null Memo correctly' {
        $json = Build-CanonicalTransferJson `
            -PayerDid    'did:drn:p' `
            -PayeeDid    'did:drn:q' `
            -AmountGrana 1 `
            -Nonce       'n' `
            -Timestamp   't' `
            -Memo        'payment for invoice 42'

        $doc  = [System.Text.Json.JsonDocument]::Parse($json)
        $memo = $doc.RootElement.GetProperty('Memo').GetString()
        $memo | Should -Be 'payment for invoice 42'
    }

    It 'serializes a null Memo as JSON null (not absent)' {
        $json = Build-CanonicalTransferJson `
            -PayerDid    'did:drn:p' `
            -PayeeDid    'did:drn:q' `
            -AmountGrana 1 `
            -Nonce       'n' `
            -Timestamp   't' `
            -Memo        $null

        $doc      = [System.Text.Json.JsonDocument]::Parse($json)
        $memoKind = $doc.RootElement.GetProperty('Memo').ValueKind
        $memoKind | Should -Be ([System.Text.Json.JsonValueKind]::Null)
    }

    It 'produces compact JSON with no line breaks' {
        $json = Build-CanonicalTransferJson `
            -PayerDid    'did:drn:p' `
            -PayeeDid    'did:drn:q' `
            -AmountGrana 1 `
            -Nonce       'n' `
            -Timestamp   't' `
            -Memo        $null

        $json | Should -Not -Match "`n"
        $json | Should -Not -Match "`r"
    }

    It 'encodes AmountGrana as a JSON number, not a string' {
        $json = Build-CanonicalTransferJson `
            -PayerDid    'did:drn:p' `
            -PayeeDid    'did:drn:q' `
            -AmountGrana 12345 `
            -Nonce       'n' `
            -Timestamp   't' `
            -Memo        $null

        $doc    = [System.Text.Json.JsonDocument]::Parse($json)
        $kind   = $doc.RootElement.GetProperty('AmountGrana').ValueKind
        $value  = $doc.RootElement.GetProperty('AmountGrana').GetInt64()
        $kind   | Should -Be ([System.Text.Json.JsonValueKind]::Number)
        $value  | Should -Be 12345
    }
}

# ── Initialize-Svrn7Assemblies path resolution ────────────────────────────────

Describe 'Initialize-Svrn7Assemblies path resolution' {
    BeforeAll {
        # Remove any SVRN7_BIN_PATH override so the fallback logic runs.
        $Saved = $env:SVRN7_BIN_PATH
        Remove-Item Env:\SVRN7_BIN_PATH -ErrorAction SilentlyContinue
    }

    AfterAll {
        if ($null -ne $Saved) { $env:SVRN7_BIN_PATH = $Saved }
    }

    It 'throws DirectoryNotFoundException when $env:SVRN7_BIN_PATH points to nonexistent path' {
        $env:SVRN7_BIN_PATH = 'C:\does\not\exist\svrn7'
        { Initialize-Svrn7Assemblies -ModuleRoot (Get-Location).Path } |
            Should -Throw -ExceptionType ([System.IO.DirectoryNotFoundException])
        Remove-Item Env:\SVRN7_BIN_PATH -ErrorAction SilentlyContinue
    }

    It 'falls back to two-levels-up when ModuleRoot has no bin/ subfolder' {
        # Pass a completely nonexistent module root — no directories are created.
        # The function computes binPath as GetFullPath('ModuleRoot/../..').
        # Because neither bin/ nor the two-levels-up path exist, it throws
        # DirectoryNotFoundException naming the fallback (not the bin/) path.
        $moduleDir        = Join-Path $env:TEMP "svrn7-ne-$([System.Guid]::NewGuid().ToString('N'))\lobes\Svrn7.Module"
        $expectedFallback = [System.IO.Path]::GetFullPath((Join-Path $moduleDir '../..'))

        { Initialize-Svrn7Assemblies -ModuleRoot $moduleDir } |
            Should -Throw -ExceptionType ([System.IO.DirectoryNotFoundException])

        try { Initialize-Svrn7Assemblies -ModuleRoot $moduleDir } catch {
            $_.Exception.Message | Should -Match ([regex]::Escape($expectedFallback))
        }
    }

    It 'uses $env:SVRN7_BIN_PATH when set, even if the path does not exist' {
        $env:SVRN7_BIN_PATH = 'C:\svrn7-imaginary-bin-path-does-not-exist'
        try {
            { Initialize-Svrn7Assemblies -ModuleRoot (Get-Location).Path } |
                Should -Throw -ExceptionType ([System.IO.DirectoryNotFoundException])
            try { Initialize-Svrn7Assemblies -ModuleRoot (Get-Location).Path } catch {
                $_.Exception.Message | Should -Match ([regex]::Escape('C:\svrn7-imaginary-bin-path-does-not-exist'))
            }
        } finally {
            Remove-Item Env:\SVRN7_BIN_PATH -ErrorAction SilentlyContinue
        }
    }
}
