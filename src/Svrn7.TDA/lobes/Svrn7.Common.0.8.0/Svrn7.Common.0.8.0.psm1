#
# Svrn7.Common.0.8.0.psm1
# Private shared helpers — dot-sourced by Svrn7.Federation.0.8.0.psm1 and Svrn7.Society.0.8.0.psm1.
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
        $drv = Get-ActiveSocietyDriver
        $res = $drv.ResolveDidAsync($Did).GetAwaiter().GetResult()
        if (-not $res -or -not $res.Document -or -not $res.Document.ServiceEndpoints) {
            return $null
        }
        $svc = $res.Document.ServiceEndpoints |
               Where-Object { $_.Type -eq 'DIDCommMessaging' } |
               Select-Object -First 1
        if (-not $svc) { return $null }
        return $svc.ServiceEndpoint
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

# ── drn.directory federation endpoint discovery ───────────────────────────────

function Resolve-FederationEndpoint {
    <#
    .SYNOPSIS
        Resolves the DIDComm service endpoint for a Federation TDA via drn.directory DNS.
    .DESCRIPTION
        Queries the drn.directory DNS zone for the Federation TDA's DIDComm endpoint URL.
        This is the only use of DNS in the Web 7.0 Pando architecture and the canonical
        bootstrap mechanism for locating a Federation before any DIDComm state exists.

        Spec: draft-herman-did-w3c-drn-00 Section 5b.

        Accepted input forms:
          did:drn:federation.svrn7.net/agent/1.0/{key}   (full Federation DID)
          federation.svrn7.net                            (DID method-specific id)
          svrn7.net                                       (bare domain)

        In TDA runspace context, DnsClient.dll is already loaded by the host process.
        In standalone PowerShell mode, DnsClient.dll is loaded from the same bin
        directory used by Initialize-Svrn7Assemblies (set SVRN7_BIN_PATH if needed).
    .PARAMETER FederationDid
        Federation DID, method-specific identifier, or bare domain.
    .OUTPUTS
        [string] — DIDComm endpoint URL, or $null when no drn.directory record exists.
    .EXAMPLE
        Resolve-FederationEndpoint -FederationDid "did:drn:federation.svrn7.net/agent/1.0/abc"
    .EXAMPLE
        Resolve-FederationEndpoint "svrn7.net"
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory, Position = 0)]
        [ValidateNotNullOrEmpty()]
        [string] $FederationDid
    )
    process {
        # Build drn.directory query label from DID, method-specific id, or bare domain
        $input = $FederationDid
        if ($input -match '^did:drn:(.+)$') { $input = $Matches[1] }  # strip did:drn:
        $input = ($input -split '/')[0]                                # strip /agent/...
        if ($input -notmatch '^federation\.') { $input = "federation.$input" }
        $queryLabel = "$input.drn.directory"

        Write-Verbose "Resolve-FederationEndpoint: querying '$queryLabel'"

        # Load DnsClient.dll if not yet available in this runspace
        $loaded = try { [DnsClient.LookupClient] | Out-Null; $true } catch { $false }
        if (-not $loaded) {
            $binPath = if ($env:SVRN7_BIN_PATH) {
                           $env:SVRN7_BIN_PATH
                       } else {
                           $localBin = Join-Path $PSScriptRoot 'bin'
                           if (Test-Path $localBin) { $localBin }
                           else { [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')) }
                       }
            $dll = Join-Path $binPath 'DnsClient.dll'
            if (-not (Test-Path $dll)) {
                throw "Resolve-FederationEndpoint: DnsClient.dll not found at '$dll'. " +
                      "Ensure the TDA project references the DnsClient NuGet package, " +
                      "or set `$env:SVRN7_BIN_PATH to the folder containing DnsClient.dll."
            }
            Add-Type -Path $dll -ErrorAction Stop
        }

        $client = [DnsClient.LookupClient]::new()
        $result = $client.QueryAsync($queryLabel, [DnsClient.QueryType]::TXT).GetAwaiter().GetResult()
        $endpoint = $result.Answers |
            Where-Object { $_ -is [DnsClient.Protocol.TxtRecord] } |
            ForEach-Object { $_.Text } |
            Where-Object { $_ } |
            Select-Object -First 1

        if (-not $endpoint) {
            Write-Verbose "Resolve-FederationEndpoint: no TXT record found for '$queryLabel'"
        }
        $endpoint
    }
}

# ── DIDComm local WebSocket sender ───────────────────────────────────────────

function Send-LocalDIDCommMessage {
    <#
    .SYNOPSIS
        Sends a DIDComm plaintext envelope to a local TDA via WebSocket (/didcomm-notify).
    .DESCRIPTION
        Connects to ws://localhost:{Port}/didcomm-notify using RFC 8441 (WebSocket over HTTP/2),
        sends a single DIDComm plaintext JSON frame, then closes the connection gracefully.
        The TDA's receive loop unpacks and enqueues the message through the same pipeline as
        POST /didcomm, routing it to the appropriate LOBE via the Switchboard.

        Use for localhost-only communication: PS debug scripts, local tooling, automated tests.
        For TDA-to-TDA traffic, the Switchboard handles packing and delivery automatically.

        POST /didcomm enforces application/didcomm-encrypted+json (SignThenEncrypt) and will
        reject plaintext messages. This cmdlet targets the WebSocket path, which accepts
        plaintext from localhost-only connections.
    .PARAMETER Port
        TDA listen port. Defaults to 8443.
    .PARAMETER Body
        DIDComm plaintext JSON envelope to send.
    .OUTPUTS
        [string] — confirmation line with byte count sent.
    .EXAMPLE
        Send-LocalDIDCommMessage -Body ($msg | ConvertTo-Json)
    .EXAMPLE
        Send-LocalDIDCommMessage -Port 8441 -Body ($msg | ConvertTo-Json)
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [int]    $Port = 8443,
        [Parameter(Mandatory)]
        [string] $Body
    )
    process {
        # h2c (cleartext HTTP/2) support required for ws:// URIs in dev/test environments.
        [System.AppContext]::SetSwitch('System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport', $true)

        $uri = "ws://localhost:$Port/didcomm-notify"
        $ws  = [System.Net.WebSockets.ClientWebSocket]::new()
        try {
            $ws.Options.HttpVersion       = [System.Version]::new(2, 0)
            $ws.Options.HttpVersionPolicy = [System.Net.Http.HttpVersionPolicy]::RequestVersionOrHigher
            $ws.ConnectAsync([Uri]::new($uri), [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
            $ws.SendAsync(
                [System.ArraySegment[byte]]::new($bytes),
                [System.Net.WebSockets.WebSocketMessageType]::Text,
                $true,
                [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
            $ws.CloseAsync(
                [System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,
                '',
                [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
            "Sent to $uri ($($bytes.Length) bytes)"
        } finally {
            $ws.Dispose()
        }
    }
}
