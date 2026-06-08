#
# Svrn7.Common.psm1
# Private shared helpers — dot-sourced by Svrn7.Federation.psm1 and Svrn7.Society.psm1.
# Not imported directly by end users.
# Note: #Requires is intentionally absent. This file is dot-sourced by parent modules
# (Federation, Society) that already enforce #Requires -Version 7.2 -PSEdition Core.
# Adding #Requires to a dot-sourced .psm1 causes PowerShell 7 to treat the dot-source
# as a module-context load, scoping functions to a private scope instead of the caller's.
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

    $binPath = if ($env:SVRN7_BIN_PATH) {
                   $env:SVRN7_BIN_PATH
               } else {
                   # Production layout: bin/ folder adjacent to the module (.psm1).
                   $localBin = Join-Path $ModuleRoot 'bin'
                   if (Test-Path $localBin) { $localBin }
                   else {
                       # Debug/TDA output layout: net8.0/lobes/<Module>/ → DLLs are in net8.0/ (2 levels up).
                       [System.IO.Path]::GetFullPath((Join-Path $ModuleRoot '../..'))
                   }
               }

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
    if ($null -eq $SVRN7 -and $null -eq $Script:FederationDriver) {
        throw [System.InvalidOperationException]::new(
            'Svrn7.Federation driver not initialised. ' +
            'Call Initialize-Svrn7FederationDriver before using Federation cmdlets.')
    }
}

function Assert-SocietyDriver {
    if ($null -eq $SVRN7 -and $null -eq $Script:SocietyDriver) {
        throw [System.InvalidOperationException]::new(
            'Svrn7.Society driver not initialised. ' +
            'Call Connect-Svrn7Society before using Society cmdlets.')
    }
}

# ── TDA inbox message accessor ───────────────────────────────────────────────

function Dequeue-Svrn7Message {
    <#
    .SYNOPSIS
        Retrieves an InboxMessageView from the TDA inbox by its DID URL.
        Requires a TDA runspace ($SVRN7 must be set).
    .PARAMETER Did
        The inbox message DID URL (e.g. did:drn:alpha.svrn7.net/inbox/msg/<id>).
    #>
    [CmdletBinding()]
    [OutputType([object])]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Did
    )
    process {
        if ($null -eq $SVRN7) {
            throw [System.InvalidOperationException]::new(
                'Dequeue-Svrn7Message requires a TDA runspace ($SVRN7 not set).')
        }
        $view = $SVRN7.GetMessageAsync($Did).GetAwaiter().GetResult()
        if (-not $view) {
            throw [System.InvalidOperationException]::new(
                "Dequeue-Svrn7Message: no inbox message found for '$Did'.")
        }
        $view
    }
}

# ── TDA-aware driver accessors ────────────────────────────────────────────────
# In TDA runspace context $SVRN7 is set and $Script:SocietyDriver / $Script:FederationDriver
# are null. These helpers return the appropriate driver for whichever context is active.

function Get-ActiveSocietyDriver {
    if ($null -ne $SVRN7)          { return $SVRN7.Driver }
    if ($Script:SocietyDriver)     { return $Script:SocietyDriver }
    throw [System.InvalidOperationException]::new(
        'No active society driver. In standalone mode call Connect-Svrn7Society; ' +
        'in TDA context ensure the runspace is initialised.')
}

function Get-ActiveFederationDriver {
    $drv = if ($null -ne $SVRN7) { $SVRN7.Driver } else { $Script:FederationDriver }
    if (-not $drv) {
        throw [System.InvalidOperationException]::new(
            'No active federation driver. In standalone mode call Initialize-Svrn7FederationDriver; ' +
            'in TDA context ensure the runspace is initialised.')
    }
    $fed = $drv.GetFederationAsync().GetAwaiter().GetResult()
    if (-not $fed) {
        throw [System.InvalidOperationException]::new(
            'This TDA has not been initialized as a Federation. ' +
            'Call Initialize-Svrn7Federation first.')
    }
    return $drv
}

# ── Strict-mode-safe JSON body helpers ───────────────────────────────────────────
#
# ConvertFrom-Json returns a PSCustomObject. Under Set-StrictMode -Version Latest,
# reading any property that is not present on the object throws immediately — before
# any -not check or ternary can fire. Use these two helpers throughout LOBE handlers
# instead of bare $body.fieldName access.

function Assert-BodyFields {
    <#
    .SYNOPSIS
        Validates that every named field is present and non-empty on a ConvertFrom-Json body.
        Throws a clear per-field message rather than the opaque strict-mode property error.
    .PARAMETER Body
        The PSCustomObject returned by ConvertFrom-Json.
    .PARAMETER Required
        Array of field names that must be present and non-empty.
    .PARAMETER Caller
        Cmdlet name used as a prefix in the error message.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [pscustomobject] $Body,
        [Parameter(Mandatory)] [string[]]       $Required,
        [Parameter(Mandatory)] [string]         $Caller
    )
    foreach ($f in $Required) {
        if (-not $Body.PSObject.Properties[$f] -or -not $Body.$f) {
            throw "${Caller}: body missing required field '$f'."
        }
    }
}

function Get-BodyField {
    <#
    .SYNOPSIS
        Returns the value of an optional JSON body field, or $Default when absent.
        Safe under Set-StrictMode -Version Latest — never reads a missing property.
    .PARAMETER Body
        The PSCustomObject returned by ConvertFrom-Json.
    .PARAMETER Field
        Name of the optional field.
    .PARAMETER Default
        Value to return when the field is absent. Default: $null.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [pscustomobject] $Body,
        [Parameter(Mandatory)] [string]         $Field,
                                                $Default = $null
    )
    if ($Body.PSObject.Properties[$Field]) { return $Body.$Field }
    return $Default
}

# ── DIDComm endpoint resolver (shared by Society and Federation LOBE handlers) ──

function Resolve-SocietySenderEndpoint {
    <#
    .SYNOPSIS
        Resolves the DIDComm service endpoint for a sender DID.
        Returns $null when no DIDComm service entry exists — callers decide whether
        to throw, skip the reply, or fall back to a default endpoint.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Did)
    process {
        $drv = Get-ActiveFederationDriver
        $res = $drv.ResolveDidAsync($Did).GetAwaiter().GetResult()
        if (-not $res -or -not $res.Document -or -not $res.Document.Service) {
            return $null
        }
        $svc = $res.Document.Service |
               Where-Object { $_.type -eq 'DIDComm' } |
               Select-Object -First 1
        if (-not $svc) { return $null }
        return $svc.serviceEndpoint
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
        $d,
        [System.Text.Json.JsonSerializerOptions]@{ WriteIndented = $false })
}

# ── DIDComm HTTP/2 sender ─────────────────────────────────────────────────────

function Send-DIDCommMessage {
    <#
    .SYNOPSIS
        Posts a DIDComm message to a TDA endpoint over cleartext HTTP/2 (h2c).
    .DESCRIPTION
        Invoke-RestMethod cannot be used for h2c: PowerShell sets HttpVersionPolicy.RequestVersionOrLower
        which falls back to HTTP/1.1 framing — rejected by the TDA server. This function uses
        HttpClient with RequestVersionExact so HTTP/2 is enforced end-to-end.
    .PARAMETER Uri
        Target DIDComm endpoint. Defaults to http://localhost:8443/didcomm.
    .PARAMETER Body
        The raw JSON string to POST. Must be a valid DIDComm message envelope.
    .PARAMETER ContentType
        MIME content-type header. Defaults to application/didcomm-plain+json.
    .OUTPUTS
        [string] — status line followed by response body, both as plain strings.
    .EXAMPLE
        Send-DIDCommMessage -Body ($msg | ConvertTo-Json)
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [string] $Uri         = 'http://localhost:8443/didcomm',
        [Parameter(Mandatory)]
        [string] $Body,
        [string] $ContentType = 'application/didcomm-plain+json'
    )
    process {
        $client = [System.Net.Http.HttpClient]::new()
        try {
            $client.DefaultRequestVersion = [System.Version]::new(2, 0)
            $client.DefaultVersionPolicy  = [System.Net.Http.HttpVersionPolicy]::RequestVersionExact
            $content  = [System.Net.Http.StringContent]::new(
                $Body, [System.Text.Encoding]::UTF8, $ContentType)
            $response = $client.PostAsync($Uri, $content).GetAwaiter().GetResult()
            "Status: $($response.StatusCode)"
            $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        } finally {
            $client.Dispose()
        }
    }
}
