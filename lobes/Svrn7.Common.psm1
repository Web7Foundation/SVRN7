#Requires -Version 7.2
#Requires -PSEdition Core
#
# Svrn7.Common.psm1
# Private shared helpers — dot-sourced by Svrn7.Federation.psm1 and Svrn7.Society.psm1.
# Not imported directly by end users.
#

Set-StrictMode -Version Latest

# ── Module-scope singletons ────────────────────────────────────────────────────
# Declared here so both modules share the same variable scope when dot-sourced.

$Script:FederationDriver = $null   # ISvrn7Driver
$Script:SocietyDriver    = $null   # ISvrn7SocietyDriver
$Script:AssembliesLoaded = $false

# ── PSTypeName constants ───────────────────────────────────────────────────────

$Script:TypeKeyPair         = 'Svrn7.KeyPair'
$Script:TypeDid             = 'Svrn7.Did'
$Script:TypeBalance         = 'Svrn7.Balance'
$Script:TypeTransfer        = 'Svrn7.TransferResult'
$Script:TypeBatchItem       = 'Svrn7.BatchTransferResult'
$Script:TypeSocietyReg      = 'Svrn7.SocietyRegistration'
$Script:TypeCitizenReg      = 'Svrn7.CitizenRegistration'
$Script:TypeDidMethodReg    = 'Svrn7.DidMethodRegistration'
$Script:TypeDidMethodDereg  = 'Svrn7.DidMethodDeregistration'
$Script:TypeCitizenDid      = 'Svrn7.CitizenDid'
$Script:TypeOverdraftStatus = 'Svrn7.OverdraftStatus'
$Script:TypeOverdraftRecord = 'Svrn7.OverdraftRecord'
$Script:TypeVcQueryResult   = 'Svrn7.CrossSocietyVcQueryResult'
$Script:TypeGdprErasure     = 'Svrn7.GdprErasure'
$Script:TypeMerkleHead      = 'Svrn7.MerkleTreeHead'
$Script:TypeFederation      = 'Svrn7.FederationRecord'

# ── Assembly loader ────────────────────────────────────────────────────────────

function Initialize-Svrn7Assemblies {
    [CmdletBinding()]
    param([string]$ModuleRoot = $PSScriptRoot)

    if ($Script:AssembliesLoaded) { return }

    $binPath = if ($env:SVRN7_BIN_PATH) { $env:SVRN7_BIN_PATH }
               else { Join-Path $ModuleRoot 'bin' }

    if (-not (Test-Path $binPath)) {
        throw [System.IO.DirectoryNotFoundException]::new(
            "Svrn7 assembly folder not found: '$binPath'.`n" +
            "Set `$env:SVRN7_BIN_PATH or place DLLs in: '$binPath'.")
    }

    foreach ($dll in @(
        'Svrn7.Core.dll','Svrn7.Crypto.dll','Svrn7.Store.dll',
        'Svrn7.Ledger.dll','Svrn7.Identity.dll','Svrn7.DIDComm.dll',
        'Svrn7.Federation.dll','Svrn7.Society.dll')) {
        $p = Join-Path $binPath $dll
        if (-not (Test-Path $p)) {
            throw [System.IO.FileNotFoundException]::new("Required assembly not found: '$p'")
        }
        Add-Type -Path $p -ErrorAction Stop
        Write-Verbose "Loaded: $dll"
    }
    $Script:AssembliesLoaded = $true
}

# ── Driver guards ──────────────────────────────────────────────────────────────

function Assert-FederationDriver {
    if ($null -eq $Script:FederationDriver) {
        throw [System.InvalidOperationException]::new(
            'Svrn7.Federation driver not initialised. ' +
            'Call Initialize-Svrn7Federation before using Federation cmdlets.')
    }
}

function Assert-SocietyDriver {
    if ($null -eq $Script:SocietyDriver) {
        throw [System.InvalidOperationException]::new(
            'Svrn7.Society driver not initialised. ' +
            'Call Connect-Svrn7Society before using Society cmdlets.')
    }
}

# ── OperationResult unwrapper ─────────────────────────────────────────────────

function Resolve-OperationResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)] [string] $Operation
    )
    if (-not $Result.Success) {
        $msg = if ($Result.ErrorMessage) { $Result.ErrorMessage } else { 'Operation failed.' }
        $ex  = [System.InvalidOperationException]::new("${Operation}: $msg")
        throw [System.Management.Automation.ErrorRecord]::new(
            $ex, "${Operation}Failed",
            [System.Management.Automation.ErrorCategory]::InvalidResult, $Result)
    }
    return $Result
}

# ── Canonical transfer JSON builder ───────────────────────────────────────────

function Build-CanonicalTransferJson {
    # Field order is normative per draft-herman-svrn7-monetary-protocol-00 §5.2
    param(
        [string] $PayerDid,
        [string] $PayeeDid,
        [long]   $AmountGrana,
        [string] $Nonce,
        [string] $Timestamp,
        [string] $Memo
    )
    $d = [System.Collections.Specialized.OrderedDictionary]::new()
    $d['PayerDid']    = $PayerDid
    $d['PayeeDid']    = $PayeeDid
    $d['AmountGrana'] = $AmountGrana
    $d['Nonce']       = $Nonce
    $d['Timestamp']   = $Timestamp
    $d['Memo']        = if ($Memo) { $Memo } else { $null }
    [System.Text.Json.JsonSerializer]::Serialize(
        [hashtable]$d,
        [System.Text.Json.JsonSerializerOptions]@{ WriteIndented = $false })
}
