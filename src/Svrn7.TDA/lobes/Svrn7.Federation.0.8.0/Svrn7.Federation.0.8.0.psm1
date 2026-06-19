#Requires -Version 7.2
#Requires -PSEdition Core
<#
.SYNOPSIS
    Svrn7.Federation — PowerShell cmdlets wrapping ISvrn7Driver.

.DESCRIPTION
    Complete script module exposing every operation of the SOVRON (SVRN7) Federation-level
    driver as idiomatic PowerShell cmdlets.  All cmdlets have full comment-based help,
    typed [PSCustomObject] output, pipeline support, and -WhatIf/-Confirm on mutations.

    SETUP
        1. Compile the Svrn7 .NET 8 solution and place assemblies in a bin/ folder
           adjacent to this file, or set $env:SVRN7_BIN_PATH.
        2. Import-Module ./lobes/Svrn7.Federation.psd1
        3. Initialize-Svrn7FederationDriver
        4. $kp = New-Svrn7KeyPair

    RELATED
        Svrn7.Society — ISvrn7SocietyDriver cmdlets. Requires Svrn7.Federation first.
#>
# Pre-initialise all $Script: singletons and type constants that Svrn7.Common.0.8.0.psm1
# would normally inject via dot-source. This ensures Federation.psm1 loads cleanly
# under Set-StrictMode regardless of whether the dot-source below succeeds.
# The dot-source overwrites these with identical values when it runs.
$Script:FederationDriver    = $null
$Script:SocietyDriver       = $null
$Script:AssembliesLoaded    = $false
$Script:TypeKeyPair         = 'Svrn7.KeyPair'
$Script:TypeDid             = 'Svrn7.Did'
$Script:TypeBalance         = 'Svrn7.Balance'
$Script:TypeTransfer        = 'Svrn7.TransferResult'
$Script:TypeBatchItem       = 'Svrn7.BatchTransferResult'
$Script:TypeSocietyReg      = 'Svrn7.SocietyRegistration'
$Script:TypeCitizenReg      = 'Svrn7.CitizenRegistration'
$Script:TypeCitizenDid      = 'Svrn7.CitizenDid'
$Script:TypeOverdraftStatus = 'Svrn7.OverdraftStatus'
$Script:TypeOverdraftRecord = 'Svrn7.OverdraftRecord'
$Script:TypeVcQueryResult   = 'Svrn7.CrossSocietyVcQueryResult'
$Script:TypeGdprErasure     = 'Svrn7.GdprErasure'
$Script:TypeMerkleHead      = 'Svrn7.MerkleTreeHead'
$Script:TypeFederation      = 'Svrn7.FederationRecord'
$Script:TypeFederationReg   = 'Svrn7.FederationRegistration'

# In TDA mode ($SVRN7_LOBES_DIR set), Common is already an eager ISS module — skip.
# In standalone mode (no ISS), load Common helpers into this module's scope.
# NOTE: We read Common.psm1 as text and invoke it as a [scriptblock] rather than
# using the dot-source operator (. file.psm1). PowerShell 7 applies module-context
# scoping to dot-sourced .psm1 files, which hides the defined functions from the
# calling module's scope. Invoking a scriptblock is always plain-script execution.
if (-not $SVRN7_LOBES_DIR) {
    $_commonPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'Svrn7.Common.0.8.0\Svrn7.Common.0.8.0.psm1'
    if (Test-Path -LiteralPath $_commonPath) {
        . ([scriptblock]::Create([System.IO.File]::ReadAllText($_commonPath)))
    } else {
        Write-Warning "Svrn7.Federation: Svrn7.Common.0.8.0.psm1 not found at '$_commonPath' — standalone cmdlets will fail."
    }
}
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

###############################################################################
#region INITIALISATION
###############################################################################
function Initialize-Svrn7FederationDriver {
<#
.SYNOPSIS
    Loads Svrn7 assemblies and creates the ISvrn7Driver singleton.
.DESCRIPTION
    Must be called once before any other Svrn7.Federation cmdlet. Subsequent calls are
    no-ops unless -Force is specified.

    The driver uses three LiteDB embedded databases (svrn7.db, svrn7-dids.db, svrn7-vcs.db)
    created in the system temp directory under svrn7-ps/ by default. Override with -DbPath
    or $env:SVRN7_DB_PATH.
.PARAMETER DbPath
    Directory for LiteDB files. Overrides $env:SVRN7_DB_PATH.
.PARAMETER BinPath
    Directory containing compiled Svrn7 assemblies. Overrides $env:SVRN7_BIN_PATH.
.PARAMETER DidMethodName
    DID method name for this Federation. Must match [a-z0-9]+. Default: 'drn'.
.PARAMETER Force
    Disposes and recreates the driver even if one already exists.
.EXAMPLE
    Initialize-Svrn7FederationDriver
.EXAMPLE
    Initialize-Svrn7FederationDriver -DbPath 'C:\data\svrn7' -DidMethodName 'drn' -Verbose
.OUTPUTS
    None. Driver stored as a module-level singleton.
.NOTES
    The driver is disposed automatically when the module is removed.
#>
    [CmdletBinding()]
    param(
        [string] $DbPath        = '',
        [string] $BinPath       = '',
        [ValidatePattern('^[a-z0-9]+$')]
        [string] $DidMethodName = 'drn',
        [switch] $Force
    )
    if ($Script:FederationDriver -and -not $Force) {
        Write-Verbose 'Svrn7.Federation already initialised. Use -Force to reinitialise.'; return
    }
    if ($Script:FederationDriver -and $Force) {
        $Script:FederationDriver.DisposeAsync().GetAwaiter().GetResult()
        $Script:FederationDriver = $null
    }
    if ($BinPath) { $env:SVRN7_BIN_PATH = $BinPath }
    Initialize-Svrn7Assemblies -ModuleRoot $PSScriptRoot `
        -Verbose:($VerbosePreference -ne 'SilentlyContinue')

    $dbRoot = if ($DbPath) { $DbPath }
              elseif ($env:SVRN7_DB_PATH) { $env:SVRN7_DB_PATH }
              else { Join-Path ([System.IO.Path]::GetTempPath()) 'svrn7-ps' }
    [System.IO.Directory]::CreateDirectory($dbRoot) | Out-Null

    $svc = [Microsoft.Extensions.DependencyInjection.ServiceCollection]::new()
    $svc.AddSvrn7Federation([Action[Svrn7.Federation.Svrn7Options]] {
        param($o)
        $o.Svrn7DbPath   = Join-Path $dbRoot 'svrn7.db'
        $o.DidsDbPath    = Join-Path $dbRoot 'svrn7-dids.db'
        $o.VcsDbPath     = Join-Path $dbRoot 'svrn7-vcs.db'
        $o.DidMethodName = $DidMethodName
    }) | Out-Null
    $Script:FederationDriver = $svc.BuildServiceProvider()
                                   .GetRequiredService([Svrn7.Federation.ISvrn7Driver])
    Write-Verbose "Svrn7.Federation ready. DbRoot: $dbRoot  Method: $DidMethodName"
}
#endregion

###############################################################################
#region CRYPTOGRAPHY
###############################################################################
function New-Svrn7KeyPair {
<#
.SYNOPSIS
    Generates a secp256k1 key pair for signing SVRN7 transfer requests.
.DESCRIPTION
    Calls ISvrn7Driver.GenerateSecp256k1KeyPair(). The returned object can be piped
    directly into New-Svrn7Did, Initialize-Svrn7Society, Register-Svrn7CitizenInSociety, and
    Invoke-Svrn7Transfer. Handle PrivateKeyBytes with care.
.EXAMPLE
    $kp = New-Svrn7KeyPair
    $kp.PublicKeyHex
.EXAMPLE
    New-Svrn7KeyPair | New-Svrn7Did -MethodName 'sovronia'
.OUTPUTS
    [PSCustomObject] Svrn7.KeyPair
        PublicKeyHex    [string]   33-byte compressed secp256k1 public key (hex).
        PrivateKeyBytes [byte[]]   32-byte raw private key.
        PrivateKeyHex   [string]   Hex of the private key.
        Algorithm       [string]   'Secp256k1'.
.NOTES
    ISvrn7Driver method: GenerateSecp256k1KeyPair()
#>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()
    # Key generation is pure crypto — no database connection required.
    # Use the driver if already initialised; otherwise instantiate CryptoService directly.
    $kp = if ($Script:FederationDriver) {
        $Script:FederationDriver.GenerateSecp256k1KeyPair()
    } else {
        Initialize-Svrn7Assemblies -ModuleRoot $PSScriptRoot
        [Svrn7.Crypto.CryptoService]::new().GenerateSecp256k1KeyPair()
    }
    [PSCustomObject]@{
        PSTypeName      = $Script:TypeKeyPair
        PublicKeyHex    = $kp.PublicKeyHex
        PrivateKeyBytes = $kp.PrivateKeyBytes
        PrivateKeyHex   = [System.Convert]::ToHexString($kp.PrivateKeyBytes).ToLower()
        Algorithm       = 'Secp256k1'
    }
}

function New-Svrn7Ed25519KeyPair {
<#
.SYNOPSIS
    Generates an Ed25519 key pair for DIDComm messaging.
.DESCRIPTION
    Calls ISvrn7Driver.GenerateEd25519KeyPair(). Ed25519 keys are used for DIDComm
    SignThenEncrypt signing and are distinct from secp256k1 transfer-signing keys.
    The Svrn7.Society module derives the X25519 key automatically (RFC 7748).
.EXAMPLE
    $edKp = New-Svrn7Ed25519KeyPair
.OUTPUTS
    [PSCustomObject] Svrn7.KeyPair
        PublicKeyHex    [string]   32-byte Ed25519 public key (hex).
        PrivateKeyBytes [byte[]]   64-byte Ed25519 private key.
        PrivateKeyHex   [string]   Hex of the private key.
        Algorithm       [string]   'Ed25519'.
.NOTES
    ISvrn7Driver method: GenerateEd25519KeyPair()
#>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()
    # Key generation is pure crypto — no database connection required.
    $kp = if ($Script:FederationDriver) {
        $Script:FederationDriver.GenerateEd25519KeyPair()
    } else {
        Initialize-Svrn7Assemblies -ModuleRoot $PSScriptRoot
        [Svrn7.Crypto.CryptoService]::new().GenerateEd25519KeyPair()
    }
    [PSCustomObject]@{
        PSTypeName      = $Script:TypeKeyPair
        PublicKeyHex    = $kp.PublicKeyHex
        PrivateKeyBytes = $kp.PrivateKeyBytes
        PrivateKeyHex   = [System.Convert]::ToHexString($kp.PrivateKeyBytes).ToLower()
        Algorithm       = 'Ed25519'
    }
}

function Invoke-Svrn7SignSecp256k1 {
<#
.SYNOPSIS
    Signs a byte payload with a secp256k1 private key (CESR-encoded output).
.DESCRIPTION
    Calls ISvrn7Driver.SignSecp256k1(payload, privateKeyBytes). Returns a CESR compact
    signature ('0B' + base64url-nopad). Invoke-Svrn7Transfer calls this automatically;
    use directly for governance operation signing.
.PARAMETER Payload
    Raw bytes to sign.
.PARAMETER PrivateKeyBytes
    32-byte secp256k1 private key from New-Svrn7KeyPair.PrivateKeyBytes.
.EXAMPLE
    $sig = Invoke-Svrn7SignSecp256k1 `
               -Payload         ([Text.Encoding]::UTF8.GetBytes('hello')) `
               -PrivateKeyBytes $kp.PrivateKeyBytes
.OUTPUTS
    [string]  CESR-encoded secp256k1 signature.
.NOTES
    ISvrn7Driver method: SignSecp256k1(byte[], byte[])
#>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [byte[]] $Payload,
        [Parameter(Mandatory)] [byte[]] $PrivateKeyBytes
    )
    Assert-FederationDriver
    $Script:FederationDriver.SignSecp256k1($Payload, $PrivateKeyBytes)
}

function Test-Svrn7SignatureSecp256k1 {
<#
.SYNOPSIS
    Verifies a CESR secp256k1 signature against a payload and public key.
.PARAMETER Payload
    The raw bytes that were originally signed.
.PARAMETER Signature
    CESR-encoded signature string from Invoke-Svrn7SignSecp256k1.
.PARAMETER PublicKeyHex
    33-byte compressed secp256k1 public key hex.
.EXAMPLE
    Test-Svrn7SignatureSecp256k1 `
        -Payload      ([Text.Encoding]::UTF8.GetBytes('hello')) `
        -Signature    $sig `
        -PublicKeyHex $kp.PublicKeyHex
.OUTPUTS
    [bool]
.NOTES
    ISvrn7Driver method: VerifySecp256k1(byte[], string, string)
#>
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)] [byte[]] $Payload,
        [Parameter(Mandatory)] [string] $Signature,
        [Parameter(Mandatory)] [string] $PublicKeyHex
    )
    Assert-FederationDriver
    $Script:FederationDriver.VerifySecp256k1($Payload, $Signature, $PublicKeyHex)
}
#endregion

###############################################################################
#region DID CONSTRUCTION
###############################################################################
function New-Svrn7Did {
<#
.SYNOPSIS
    Constructs a W3C DID Document from a SVRN7 key pair using a role-based did:drn path.
.DESCRIPTION
    Derives a stable, self-certifying DID:
        Blake3(genesis_secp256k1_compressed_pubkey_bytes) → 64-char hex genesis-hash

    DID format by role:
        Wanderer   — did:drn:wanderer.svrn7.net/agent/1.0/{genesis-hash}
        Citizen    — did:drn:{societyName}.svrn7.net/citizen/1.0/{genesis-hash}
        Society    — did:drn:federation.svrn7.net/{societyName}/1.0/{genesis-hash}
        Federation — did:drn:federation.svrn7.net/federation/1.0/{genesis-hash}

    The genesis-hash is stable across key rotations — rotations update verificationMethod
    in the DID Document, not the DID itself. Accepts pipeline input from New-Svrn7KeyPair.
.PARAMETER KeyPair
    [Svrn7.KeyPair] from New-Svrn7KeyPair.
.PARAMETER Role
    Svrn7Role: Wanderer, Citizen, Society, or Federation. Mandatory.
.PARAMETER SocietyName
    Society name (lowercase, e.g. 'bindloss'). Required for Citizen and Society roles.
    Ignored for Wanderer and Federation.
.PARAMETER ServiceEndpointUrl
    Optional DIDComm service endpoint URL (e.g. 'http://localhost:8443/didcomm').
    Added as a DIDCommMessaging service endpoint in the document.
.PARAMETER Svrn7Name
    Optional human-readable name for this TDA (e.g. 'Web 7.0 Foundation').
.EXAMPLE
    $kp  = New-Svrn7KeyPair
    $did = New-Svrn7Did -KeyPair $kp -Role Wanderer
    $did.Did   # did:drn:wanderer.svrn7.net/agent/1.0/<genesis-hash>
.EXAMPLE
    New-Svrn7KeyPair | New-Svrn7Did -Role Citizen -SocietyName 'bindloss' `
        -ServiceEndpointUrl 'http://localhost:8443/didcomm'
.OUTPUTS
    [Svrn7.Core.Models.DidDocument]
        Did              [string]
        MethodName       [string]  always 'drn'
        Svrn7Name        [string]  optional human-readable TDA name
        DocumentJson     [string]  W3C DID Document JSON
        ServiceEndpoints [List]    contains one entry when -ServiceEndpointUrl is given
.NOTES
    Uses Svrn7.Crypto.CryptoService.Blake3Hex and Svrn7.Federation.Svrn7Driver.BuildMinimalDidDocument.
    Pure in-memory operation — no driver or database required.
#>
    [CmdletBinding()]
    [OutputType([Svrn7.Core.Models.DidDocument])]
    param(
        [Parameter(Mandatory, ValueFromPipeline)] [PSCustomObject] $KeyPair,
        [Parameter(Mandatory)] [Svrn7.Core.Models.Svrn7Role] $Role,
        [Parameter()] [string] $SocietyName = '',
        [Parameter()] [string] $ServiceEndpointUrl = '',
        [Parameter()] [string] $Svrn7Name = ''
    )
    process {
        if (-not $KeyPair.PublicKeyHex) {
            throw [System.ArgumentException]::new('KeyPair.PublicKeyHex is empty. Use New-Svrn7KeyPair.')
        }
        if (-not $Script:FederationDriver) {
            Initialize-Svrn7Assemblies -ModuleRoot $PSScriptRoot
        }
        $bytes       = [System.Convert]::FromHexString($KeyPair.PublicKeyHex)
        $genesisHash = [Svrn7.Crypto.CryptoService]::new().Blake3Hex($bytes)
        $did = switch ($Role) {
            ([Svrn7.Core.Models.Svrn7Role]::Wanderer)   { "did:drn:wanderer.svrn7.net/agent/1.0/$genesisHash" }
            ([Svrn7.Core.Models.Svrn7Role]::Citizen)    {
                if (-not $SocietyName) { throw [System.ArgumentException]::new('-SocietyName is required for Citizen role.') }
                "did:drn:$SocietyName.svrn7.net/citizen/1.0/$genesisHash"
            }
            ([Svrn7.Core.Models.Svrn7Role]::Society)    {
                if (-not $SocietyName) { throw [System.ArgumentException]::new('-SocietyName is required for Society role.') }
                "did:drn:federation.svrn7.net/$SocietyName/1.0/$genesisHash"
            }
            ([Svrn7.Core.Models.Svrn7Role]::Federation) { "did:drn:federation.svrn7.net/federation/1.0/$genesisHash" }
            default { throw [System.ArgumentException]::new("Unsupported role: $Role") }
        }
        $svcUrl  = if ($ServiceEndpointUrl) { $ServiceEndpointUrl } else { $null }
        $nameVal = if ($Svrn7Name) { $Svrn7Name } else { $null }
        [Svrn7.Federation.Svrn7Driver]::BuildMinimalDidDocument($did, $KeyPair.PublicKeyHex, 'drn', $svcUrl, $Role, $nameVal)
    }
}

function Resolve-Svrn7CitizenPrimaryDid {
<#
.SYNOPSIS
    Resolves any citizen DID (primary or additional) to the citizen's primary DID.
.DESCRIPTION
    Calls ISvrn7Driver.ResolveCitizenPrimaryDidAsync(). Returns $null if not found.
    Mirrors Step 0 of the 8-step transfer validation pipeline
    (draft-herman-svrn7-monetary-protocol-00 §6).
.PARAMETER Did
    Any citizen DID. Accepts pipeline input.
.EXAMPLE
    Resolve-Svrn7CitizenPrimaryDid -Did 'did:sovroniamed:abc123...'
    # did:sovronia:abc123...
.OUTPUTS
    [string] or $null.
.NOTES
    ISvrn7Driver method: ResolveCitizenPrimaryDidAsync(string)
#>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory, ValueFromPipeline)] [string] $Did)
    process {
        Assert-FederationDriver
        $Script:FederationDriver.ResolveCitizenPrimaryDidAsync($Did).GetAwaiter().GetResult()
    }
}
#endregion

###############################################################################
#region CITIZEN LIFECYCLE
###############################################################################
function Initialize-Svrn7Citizen {
<#
.SYNOPSIS
    Creates a new citizen record and persists their DIDDocument to the local database.
.DESCRIPTION
    Calls ISvrn7Driver.RegisterCitizenAsync(). Creates the CitizenRecord, wallet, and
    DIDDocument in the local svrn7-dids.db. Does NOT transfer the 1,000 SVRN7 endowment
    and does NOT associate the citizen with any Society.
    To register the citizen with a Society (with endowment), use Register-Svrn7CitizenInSociety
    from Svrn7.Society.
.PARAMETER DidDocument
    [Svrn7.Core.Models.DidDocument] from New-Svrn7Did. Persisted to svrn7-dids.db.
.PARAMETER KeyPair
    secp256k1 [Svrn7.KeyPair] — provides PrivateKeyBytes for local key storage.
.EXAMPLE
    $kp     = New-Svrn7KeyPair
    $didDoc = New-Svrn7Did -KeyPair $kp -ServiceEndpointUrl 'https://alpha.svrn7.net:8443/didcomm'
    Initialize-Svrn7Citizen -DidDocument $didDoc -KeyPair $kp
.OUTPUTS
    [PSCustomObject] Svrn7.CitizenRegistration
        CitizenDid [string]
        Success    [bool]
.NOTES
    ISvrn7Driver method: RegisterCitizenAsync(RegisterCitizenRequest)
#>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)] [Svrn7.Core.Models.DidDocument] $DidDocument,
        [Parameter(Mandatory)] [PSCustomObject]                 $KeyPair
    )
    Assert-FederationDriver -RequiredRole 'Society'
    if ($PSCmdlet.ShouldProcess($DidDocument.Did, 'Initialize citizen')) {
        $r = $Script:FederationDriver.RegisterCitizenAsync(
            [Svrn7.Core.Models.RegisterCitizenRequest]@{
                DidDocument     = $DidDocument
                PrivateKeyBytes = $KeyPair.PrivateKeyBytes
            }).GetAwaiter().GetResult()
        Resolve-OperationResult $r 'InitializeCitizen' | Out-Null
        [PSCustomObject]@{ PSTypeName = $Script:TypeCitizenReg; CitizenDid = $DidDocument.Did; Success = $true }
    }
}

function Get-Svrn7Citizen {
<#
.SYNOPSIS
    Retrieves a citizen record by DID.
.PARAMETER Did
    Citizen DID. Accepts pipeline input.
.EXAMPLE
    Get-Svrn7Citizen -Did 'did:drn:3J98t1...'
.EXAMPLE
    'did:drn:abc...','did:drn:def...' | Get-Svrn7Citizen
.OUTPUTS
    [Svrn7.Core.Models.CitizenRecord] or $null.
.NOTES
    ISvrn7Driver method: GetCitizenAsync(string)
#>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)] [string] $Did)
    process { Assert-FederationDriver; $Script:FederationDriver.GetCitizenAsync($Did).GetAwaiter().GetResult() }
}

function Test-Svrn7CitizenActive {
<#
.SYNOPSIS
    Returns $true if the DID belongs to an active citizen.
.PARAMETER Did
    Citizen DID. Accepts pipeline input.
.EXAMPLE
    Test-Svrn7CitizenActive -Did 'did:drn:3J98t1...'
.OUTPUTS
    [bool]
.NOTES
    ISvrn7Driver method: IsCitizenActiveAsync(string)
#>
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)] [string] $Did)
    process { Assert-FederationDriver; $Script:FederationDriver.IsCitizenActiveAsync($Did).GetAwaiter().GetResult() }
}

function Get-Svrn7CitizenDids {
<#
.SYNOPSIS
    Returns all DID records (primary and additional) for a citizen.
.DESCRIPTION
    Calls ISvrn7Driver.GetAllDidsForCitizenAsync(). Includes the primary DID and any
    additional DIDs issued via Add-Svrn7CitizenDid in Svrn7.Society.
.PARAMETER PrimaryDid
    Primary DID of the citizen.
.EXAMPLE
    Get-Svrn7CitizenDids -PrimaryDid 'did:sovronia:abc123...'
.OUTPUTS
    [IReadOnlyList[Svrn7.Core.Models.CitizenDidRecord]]
.NOTES
    ISvrn7Driver method: GetAllDidsForCitizenAsync(string)
#>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)] [string] $PrimaryDid)
    process { Assert-FederationDriver; $Script:FederationDriver.GetAllDidsForCitizenAsync($PrimaryDid).GetAwaiter().GetResult() }
}
#endregion

###############################################################################
#region SOCIETY LIFECYCLE
###############################################################################
function Initialize-Svrn7Society {
<#
.SYNOPSIS
    Creates a Society record, wallet, and DIDDocument in the local database.
.DESCRIPTION
    Calls ISvrn7Driver.InitializeSocietyAsync(). Creates the SocietyRecord, wallet,
    and persists the DIDDocument locally. Does NOT register the Society with the
    Federation or issue a VTC credential.
    Call Register-Svrn7Society after this to complete Federation registration.
.PARAMETER DidDocument
    [Svrn7.Core.Models.DidDocument] from New-Svrn7Did -Role Society -SocietyName '...'.
.PARAMETER KeyPair
    secp256k1 [Svrn7.KeyPair] — provides PrivateKeyBytes for local key storage.
.PARAMETER Name
    Human-readable Society name (e.g. 'Sovronia Digital Nation').
.EXAMPLE
    $kp     = New-Svrn7KeyPair
    $didDoc = New-Svrn7Did -KeyPair $kp -Role Society -SocietyName 'sovronia' `
                           -ServiceEndpointUrl 'http://sovronia.svrn7.net:8443/didcomm'
    Initialize-Svrn7Society -DidDocument $didDoc -KeyPair $kp -Name 'Sovronia Digital Nation'
    Register-Svrn7Society   -SocietyDid $didDoc.Did
.OUTPUTS
    [PSCustomObject] Svrn7.SocietyRegistration
        SocietyDid [string]; SocietyName [string]; Success [bool]
.NOTES
    ISvrn7Driver method: InitializeSocietyAsync(RegisterSocietyRequest)
    Spec: draft-herman-web7-society-architecture-00 §4.2
#>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)] [Svrn7.Core.Models.DidDocument] $DidDocument,
        [Parameter(Mandatory)] [PSCustomObject]                 $KeyPair,
        [Parameter(Mandatory)] [string]                         $Name
    )
    Assert-FederationDriver
    if ($PSCmdlet.ShouldProcess($DidDocument.Did, "Initialize Society '$Name'")) {
        $r = $Script:FederationDriver.InitializeSocietyAsync(
            [Svrn7.Core.Models.RegisterSocietyRequest]@{
                DidDocument     = $DidDocument
                PrivateKeyBytes = $KeyPair.PrivateKeyBytes
                SocietyName     = $Name
                DrawAmountGrana       = 0L
                OverdraftCeilingGrana = 0L
            }).GetAwaiter().GetResult()
        Resolve-OperationResult $r 'InitializeSociety' | Out-Null
        [PSCustomObject]@{
            PSTypeName  = $Script:TypeSocietyReg
            SocietyDid  = $DidDocument.Did
            SocietyName = $Name
            Success     = $true
        }
    }
}

function Register-Svrn7Society {
<#
.SYNOPSIS
    Registers an already-initialized Society with the Federation.
.DESCRIPTION
    Calls ISvrn7Driver.RegisterSocietyInFederationAsync(). Issues a Svrn7VtcCredential
    and appends a SocietyRegistration entry to the Merkle log.
    The Society must already be initialized via Initialize-Svrn7Society.
.PARAMETER SocietyDid
    DID of the Society to register. Must have been initialized first.
.EXAMPLE
    Register-Svrn7Society -SocietyDid 'did:drn:federation.svrn7.net/bindloss/1.0/<genesis-hash>'
.OUTPUTS
    [PSCustomObject] Svrn7.SocietyRegistration
        SocietyDid [string]; SocietyName [string]; Success [bool]
.NOTES
    ISvrn7Driver method: RegisterSocietyInFederationAsync(string)
    Spec: draft-herman-web7-society-architecture-00 §4.2
#>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)] [string] $SocietyDid
    )
    process {
        Assert-FederationDriver
        if ($PSCmdlet.ShouldProcess($SocietyDid, 'Register Society in Federation')) {
            $r = $Script:FederationDriver.RegisterSocietyInFederationAsync($SocietyDid).GetAwaiter().GetResult()
            Resolve-OperationResult $r 'RegisterSociety' | Out-Null
            $soc = $Script:FederationDriver.GetSocietyAsync($SocietyDid).GetAwaiter().GetResult()
            [PSCustomObject]@{
                PSTypeName  = $Script:TypeSocietyReg
                SocietyDid  = $SocietyDid
                SocietyName = $soc?.SocietyName
                Success     = $true
            }
        }
    }
}

function Get-Svrn7Society {
<#
.SYNOPSIS
    Retrieves a Society record by DID.
.PARAMETER Did
    Society DID. Accepts pipeline input.
.EXAMPLE
    Get-Svrn7Society -Did 'did:sovronia:abc123...'
.OUTPUTS
    [Svrn7.Core.Models.SocietyRecord] or $null.
.NOTES
    ISvrn7Driver method: GetSocietyAsync(string)
#>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)] [string] $Did)
    process { Assert-FederationDriver; $Script:FederationDriver.GetSocietyAsync($Did).GetAwaiter().GetResult() }
}


function Test-Svrn7SocietyActive {
<#
.SYNOPSIS
    Returns $true if the DID belongs to an active Society.
.PARAMETER Did
    Society DID. Accepts pipeline input.
.EXAMPLE
    Test-Svrn7SocietyActive -Did 'did:sovronia:abc123...'
.OUTPUTS
    [bool]
.NOTES
    ISvrn7Driver method: IsSocietyActiveAsync(string)
#>
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)] [string] $Did)
    process { Assert-FederationDriver; $Script:FederationDriver.IsSocietyActiveAsync($Did).GetAwaiter().GetResult() }
}

function Disable-Svrn7Society {
<#
.SYNOPSIS
    Permanently deactivates a Society (irreversible).
.DESCRIPTION
    Calls ISvrn7Driver.DeactivateSocietyAsync(). Existing citizen DIDs remain valid
    per Governing Architectural Principle 10 (citizen retains their DID).
.PARAMETER Did
    Society DID to deactivate.
.EXAMPLE
    Disable-Svrn7Society -Did 'did:sovronia:abc123...' -Confirm
.OUTPUTS
    None. Throws on failure.
.NOTES
    ISvrn7Driver method: DeactivateSocietyAsync(string). IRREVERSIBLE.
#>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]
    param([Parameter(Mandatory)] [string] $Did)
    Assert-FederationDriver
    if ($PSCmdlet.ShouldProcess($Did, 'Deactivate Society (IRREVERSIBLE)')) {
        $Script:FederationDriver.DeactivateSocietyAsync($Did).GetAwaiter().GetResult()
    }
}
#endregion


###############################################################################
#region BALANCE
###############################################################################
function Get-Svrn7Balance {
<#
.SYNOPSIS
    Queries the SVRN7 and grana balance of a DID.
.DESCRIPTION
    Calls ISvrn7Driver.GetBalanceResultAsync(). A newly registered citizen always
    has exactly 1,000.000000 SVRN7 (1,000,000,000 grana) from the endowment transfer.
.PARAMETER Did
    DID to query. Accepts pipeline input from New-Svrn7Did.
.EXAMPLE
    Get-Svrn7Balance -Did 'did:drn:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy'
.EXAMPLE
    New-Svrn7KeyPair | New-Svrn7Did | Get-Svrn7Balance
.EXAMPLE
    $dids | Get-Svrn7Balance | Format-Table Did, Display -AutoSize
.OUTPUTS
    [PSCustomObject] Svrn7.Balance
        Did [string]; Grana [long]; Svrn7 [decimal]; Display [string] '1,000.000000 SVRN7'
.NOTES
    ISvrn7Driver method: GetBalanceResultAsync(string)
#>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param([Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)] [string] $Did)
    process {
        Assert-FederationDriver
        $b = $Script:FederationDriver.GetBalanceResultAsync($Did).GetAwaiter().GetResult()
        [PSCustomObject]@{
            PSTypeName=$Script:TypeBalance; Did=$Did; Grana=$b.Grana
            Svrn7=$b.Svrn7; Display=('{0:N6} SVRN7' -f $b.Svrn7)
        }
    }
}
#endregion

###############################################################################
#region TRANSFERS
###############################################################################
function Invoke-Svrn7Transfer {
<#
.SYNOPSIS
    Signs and submits a SVRN7 transfer request.
.DESCRIPTION
    Builds the canonical JSON per draft-herman-svrn7-monetary-protocol-00 §5.2:
        { PayerDid, PayeeDid, AmountGrana, Nonce, Timestamp, Memo }
    Signs the UTF-8 bytes with the payer's secp256k1 key (CESR '0B'), then calls
    ISvrn7Driver.TransferAsync(). Field order is enforced automatically. A UUID nonce
    is generated if -Nonce is omitted.
.PARAMETER PayerDid
    DID of the payer. Must be Active.
.PARAMETER PayerKeyPair
    secp256k1 [Svrn7.KeyPair] for the payer.
.PARAMETER PayeeDid
    DID of the payee. Must be Active and permitted by the current Epoch.
.PARAMETER AmountSvrn7
    Amount in SVRN7. Mutually exclusive with -AmountGrana.
.PARAMETER AmountGrana
    Amount in grana. Mutually exclusive with -AmountSvrn7.
.PARAMETER Memo
    Optional memo (max 256 characters).
.PARAMETER Nonce
    Optional idempotency nonce. Auto-generated UUID if omitted.
.EXAMPLE
    Invoke-Svrn7Transfer `
        -PayerDid $citizenDid -PayerKeyPair $kp `
        -PayeeDid $societyDid -AmountSvrn7 100
.EXAMPLE
    Invoke-Svrn7Transfer -PayerDid $d1 -PayerKeyPair $kp -PayeeDid $d2 `
        -AmountGrana 500000000 -Memo 'Monthly dues'
.EXAMPLE
    Invoke-Svrn7Transfer -PayerDid $d1 -PayerKeyPair $kp `
        -PayeeDid $d2 -AmountSvrn7 50 -WhatIf
.OUTPUTS
    [PSCustomObject] Svrn7.TransferResult
        TransferId [string]; PayerDid [string]; PayeeDid [string]
        AmountGrana [long]; AmountSvrn7 [decimal]; Nonce [string]
        Timestamp [string]; Memo [string]; Success [bool]
.NOTES
    ISvrn7Driver method: TransferAsync(TransferRequest)
    Spec: draft-herman-svrn7-monetary-protocol-00 §§5-6
#>
    [CmdletBinding(SupportsShouldProcess, DefaultParameterSetName='BySvrn7')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)] [string]        $PayerDid,
        [Parameter(Mandatory)] [PSCustomObject] $PayerKeyPair,
        [Parameter(Mandatory)] [string]        $PayeeDid,
        [Parameter(Mandatory, ParameterSetName='BySvrn7')]
        [ValidateRange(0.000001,1e15)] [double] $AmountSvrn7,
        [Parameter(Mandatory, ParameterSetName='ByGrana')]
        [ValidateRange(1L,[long]::MaxValue)] [long] $AmountGrana,
        [Parameter()] [ValidateLength(0,256)] [string] $Memo  = '',
        [Parameter()]                         [string] $Nonce = ''
    )
    Assert-FederationDriver
    $grana = if ($PSCmdlet.ParameterSetName -eq 'BySvrn7') { [long][Math]::Round($AmountSvrn7 * 1000000) } else { $AmountGrana }
    $svrn7 = [decimal]$grana / 1000000
    $nonce = if ($Nonce) { $Nonce } else { [Guid]::NewGuid().ToString('N') }
    $ts    = [DateTimeOffset]::UtcNow.ToString('O')
    $memo  = if ($Memo) { $Memo } else { $null }
    $json  = Build-CanonicalTransferJson $PayerDid $PayeeDid $grana $nonce $ts $memo
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $sig   = $Script:FederationDriver.SignSecp256k1($bytes, $PayerKeyPair.PrivateKeyBytes)
    Write-Verbose "Canonical: $json"
    if ($PSCmdlet.ShouldProcess($PayerDid, "Transfer $svrn7 SVRN7 to $PayeeDid")) {
        $r = $Script:FederationDriver.TransferAsync([Svrn7.Core.Models.TransferRequest]@{
            PayerDid=$PayerDid; PayeeDid=$PayeeDid; AmountGrana=$grana; Nonce=$nonce
            Timestamp=[DateTimeOffset]::Parse($ts); Signature=$sig; Memo=$memo
        }).GetAwaiter().GetResult()
        Resolve-OperationResult $r 'Transfer' | Out-Null
        $txId = $Script:FederationDriver.Blake3HexAsync($bytes).GetAwaiter().GetResult()
        [PSCustomObject]@{
            PSTypeName=$Script:TypeTransfer; TransferId=$txId
            PayerDid=$PayerDid; PayeeDid=$PayeeDid; AmountGrana=$grana; AmountSvrn7=$svrn7
            Nonce=$nonce; Timestamp=$ts; Memo=$Memo; Success=$true
        }
    }
}

function Invoke-Svrn7BatchTransfer {
<#
.SYNOPSIS
    Signs and submits multiple transfer requests in one batch call.
.DESCRIPTION
    Accepts an array of transfer descriptors (each with PayerDid, PayerKeyPair,
    PayeeDid, AmountGrana; optional Memo, Nonce), signs each canonically, and
    calls ISvrn7Driver.BatchTransferAsync(). Returns one result per input.
.PARAMETER Transfers
    Array of hashtables or PSCustomObjects with keys:
        PayerDid [string] Required; PayerKeyPair [Svrn7.KeyPair] Required
        PayeeDid [string] Required; AmountGrana [long] Required
        Memo [string] Optional; Nonce [string] Optional
.EXAMPLE
    $batch = @(
        @{ PayerDid=$d1; PayerKeyPair=$kp; PayeeDid=$d2; AmountGrana=100000000L },
        @{ PayerDid=$d1; PayerKeyPair=$kp; PayeeDid=$d3; AmountGrana=50000000L  }
    )
    Invoke-Svrn7BatchTransfer -Transfers $batch
.OUTPUTS
    [PSCustomObject[]] Svrn7.BatchTransferResult — one per input.
.NOTES
    ISvrn7Driver method: BatchTransferAsync(IEnumerable<TransferRequest>)
#>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject[]])]
    param([Parameter(Mandatory, ValueFromPipeline)] [object[]] $Transfers)
    process {
        Assert-FederationDriver
        $reqs = [System.Collections.Generic.List[Svrn7.Core.Models.TransferRequest]]::new()
        $meta = [System.Collections.Generic.List[hashtable]]::new()
        foreach ($t in $Transfers) {
            $n    = if ($t.Nonce) { $t.Nonce } else { [Guid]::NewGuid().ToString('N') }
            $ts   = [DateTimeOffset]::UtcNow.ToString('O')
            $m    = if ($t.Memo) { [string]$t.Memo } else { $null }
            $g    = [long]$t.AmountGrana
            $json = Build-CanonicalTransferJson $t.PayerDid $t.PayeeDid $g $n $ts $m
            $b    = [System.Text.Encoding]::UTF8.GetBytes($json)
            $sig  = $Script:FederationDriver.SignSecp256k1($b, $t.PayerKeyPair.PrivateKeyBytes)
            $reqs.Add([Svrn7.Core.Models.TransferRequest]@{
                PayerDid=$t.PayerDid; PayeeDid=$t.PayeeDid; AmountGrana=$g
                Nonce=$n; Timestamp=[DateTimeOffset]::Parse($ts); Signature=$sig; Memo=$m })
            $meta.Add(@{ P=$t.PayerDid; Q=$t.PayeeDid; G=$g })
        }
        if ($PSCmdlet.ShouldProcess("$($reqs.Count) transfers", 'BatchTransfer')) {
            $results = $Script:FederationDriver.BatchTransferAsync($reqs).GetAwaiter().GetResult()
            $i = 0
            foreach ($r in $results) {
                $mm = $meta[$i++]
                [PSCustomObject]@{
                    PSTypeName=$Script:TypeBatchItem; PayerDid=$mm.P; PayeeDid=$mm.Q
                    AmountGrana=$mm.G; Success=$r.Success; ErrorMessage=$r.ErrorMessage
                }
            }
        }
    }
}
#endregion

###############################################################################
#region FEDERATION / EPOCH
###############################################################################
function Initialize-Svrn7Federation {
<#
.SYNOPSIS
    Promotes this Wanderer TDA to a Federation. No parameters required — all
    values are derived from the Wanderer DIDDocument already in the local registry.
    Idempotent — returns AlreadyInitialised=$true if the Federation is already set up.
.DESCRIPTION
    Every TDA starts as a Wanderer with a primary Wanderer DID. Calling this cmdlet
    adds a second, Federation-role DID to the same TDA. Both DIDs remain in the local
    registry; the Wanderer DID is the primary identity, the Federation DID is the
    authoritative handle for society/citizen registration.

    The following values are taken directly from the Wanderer DIDDocument:
      - Svrn7Name            → FederationRecord.FederationName
      - MethodName         → Federation DID method
      - ServiceEndpoint    → Federation DIDDocument DIDCommMessaging service entry

    Internally:
      1. Asserts the Wanderer DIDDocument is present in the registry.
      2. Generates a new secp256k1 key pair and derives the Federation DID
         (did:{wanderer.MethodName}:{Base58(pubKey)}).
      3. Creates a DIDDocument with Role=Federation, same Svrn7Name and service endpoint.
      4. Calls ISvrn7Driver.InitialiseFederationAsync() to persist the record
         and seed the Federation wallet.
.EXAMPLE
    Initialize-Svrn7Federation
.OUTPUTS
    [PSCustomObject] Svrn7.FederationRegistration
        FederationDid      [string]   new Federation DID
        FederationName     [string]   Svrn7Name from the Wanderer DIDDocument
        MethodName         [string]   DID method (from Wanderer DIDDocument)
        WandererDid        [string]   the pre-existing Wanderer primary DID
        PublicKeyHex       [string]   Federation secp256k1 public key
        PrivateKeyHex      [string]   Federation secp256k1 private key — STORE SECURELY
        AlreadyInitialised [bool]
        Success            [bool]
.NOTES
    ISvrn7Driver methods: GetFederationAsync, DidRegistry.QueryAsync,
    GenerateSecp256k1KeyPair, Base58EncodeAsync, CreateDidDocument,
    InitialiseFederationAsync
#>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param()
    Assert-FederationDriver
    $drv = if ($null -ne $SVRN7) { $SVRN7.Driver } else { $Script:FederationDriver }

    # Idempotency guard — already a Federation
    $existing = $drv.GetFederationAsync().GetAwaiter().GetResult()
    if ($existing) {
        return [PSCustomObject]@{
            PSTypeName         = $Script:TypeFederationReg
            FederationDid      = $existing.Did
            FederationName     = $existing.FederationName
            MethodName         = $existing.PrimaryDidMethodName
            WandererDid        = $null
            PublicKeyHex       = $null
            PrivateKeyHex      = $null
            AlreadyInitialised = $true
            Success            = $true
        }
    }

    # Wanderer guard — must have been bootstrapped at startup
    $allDids     = $drv.DidRegistry.QueryAsync().GetAwaiter().GetResult()
    $wandererDoc = $allDids |
                   Where-Object { $_.Role -eq [Svrn7.Core.Models.Svrn7Role]::Wanderer } |
                   Select-Object -First 1
    if (-not $wandererDoc) {
        throw [System.InvalidOperationException]::new(
            'Initialize-Svrn7Federation: No Wanderer DIDDocument found in the local registry. ' +
            'Start this TDA at least once on its assigned port so its Wanderer identity is bootstrapped.')
    }

    $wandererDid = $wandererDoc.Did
    $methodName  = $wandererDoc.MethodName
    $tdaName     = $wandererDoc.Svrn7Name
    $svcUrl      = if ($wandererDoc.ServiceEndpoints.Count -gt 0) {
                       $wandererDoc.ServiceEndpoints[0].ServiceEndpoint
                   } else { $null }

    if (-not $PSCmdlet.ShouldProcess($wandererDid, "Promote Wanderer to Federation '$tdaName' (method: $methodName)")) {
        return
    }

    # Generate Federation key pair and DID
    $kp     = $drv.GenerateSecp256k1KeyPair()
    $bytes  = [System.Convert]::FromHexString($kp.PublicKeyHex)
    $id     = $drv.Base58EncodeAsync($bytes).GetAwaiter().GetResult()
    $fedDid = "did:${methodName}:${id}"

    $fedDoc = $drv.CreateDidDocument(
        $fedDid, $kp.PublicKeyHex, $methodName, $svcUrl,
        [Svrn7.Core.Models.Svrn7Role]::Federation, $tdaName)

    $r = $drv.InitialiseFederationAsync($fedDoc, $tdaName).GetAwaiter().GetResult()
    Resolve-OperationResult $r 'InitializeFederation' | Out-Null

    # Capture private key before zeroing
    $privHex = [System.Convert]::ToHexString($kp.PrivateKeyBytes).ToLowerInvariant()
    $pubHex  = $kp.PublicKeyHex
    $kp.ZeroPrivateKey()

    [PSCustomObject]@{
        PSTypeName         = $Script:TypeFederationReg
        FederationDid      = $fedDid
        FederationName     = $tdaName
        MethodName         = $methodName
        WandererDid        = $wandererDid
        PublicKeyHex       = $pubHex
        PrivateKeyHex      = $privHex
        AlreadyInitialised = $false
        Success            = $true
    }
}

function Get-Svrn7Federation {
<#
.SYNOPSIS
    Retrieves the Federation record (TotalSupplyGrana, epoch, wallet balance).
.EXAMPLE
    Get-Svrn7Federation | Select-Object TotalSupplyGrana, CurrentEpoch
.OUTPUTS
    [Svrn7.Core.Models.FederationRecord] or $null.
.NOTES
    ISvrn7Driver method: GetFederationAsync()
#>
    [CmdletBinding()] param()
    Assert-FederationDriver
    $Script:FederationDriver.GetFederationAsync().GetAwaiter().GetResult()
}

function Update-Svrn7FederationSupply {
<#
.SYNOPSIS
    Increases the Federation total supply (Foundation signature required).
.DESCRIPTION
    Calls ISvrn7Driver.UpdateFederationSupplyAsync(). The new total must strictly exceed
    the current TotalSupplyGrana — supply is monotonically increasing
    (draft-herman-svrn7-monetary-protocol-00 §9).
.PARAMETER NewTotalSupplyGrana
    New total supply in grana. Must exceed current total.
.PARAMETER FoundationSignature
    CESR secp256k1 signature from the Foundation governance key.
.PARAMETER GovernanceRef
    URI referencing the governance decision authorising this update.
.EXAMPLE
    Update-Svrn7FederationSupply `
        -NewTotalSupplyGrana  2000000000000000L `
        -FoundationSignature  $sig `
        -GovernanceRef        'https://gov.sovronia.net/2026-001'
.OUTPUTS
    [Svrn7.Core.Models.FederationRecord] — updated record.
.NOTES
    ISvrn7Driver method: UpdateFederationSupplyAsync(long, string, string)
#>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]
    param(
        [Parameter(Mandatory)] [long]   $NewTotalSupplyGrana,
        [Parameter(Mandatory)] [string] $FoundationSignature,
        [Parameter(Mandatory)] [string] $GovernanceRef
    )
    Assert-FederationDriver
    if ($PSCmdlet.ShouldProcess('Federation', "Update supply to $NewTotalSupplyGrana grana")) {
        $r = $Script:FederationDriver.UpdateFederationSupplyAsync($NewTotalSupplyGrana, $FoundationSignature, $GovernanceRef).GetAwaiter().GetResult()
        Resolve-OperationResult $r 'UpdateFederationSupply' | Out-Null
        $Script:FederationDriver.GetFederationAsync().GetAwaiter().GetResult()
    }
}

function Get-Svrn7CurrentEpoch {
<#
.SYNOPSIS
    Returns the current monetary epoch (0=Endowment, 1=EcosystemUtility, 2=Market).
.EXAMPLE
    Get-Svrn7CurrentEpoch
.OUTPUTS
    [int]
.NOTES
    ISvrn7Driver method: GetCurrentEpoch()
    Spec: draft-herman-web7-epoch-governance-00
#>
    [CmdletBinding()] [OutputType([int])] param()
    Assert-FederationDriver
    $Script:FederationDriver.GetCurrentEpoch()
}
#endregion

###############################################################################
#region DID DOCUMENT REGISTRY
###############################################################################
function Resolve-Svrn7Did {
<#
.SYNOPSIS
    Resolves a DID and returns the DID Document resolution result.
.PARAMETER Did
    DID to resolve. Accepts pipeline input.
.EXAMPLE
    Resolve-Svrn7Did -Did 'did:drn:3J98t1...'
.OUTPUTS
    [Svrn7.Core.Models.DidResolutionResult]
.NOTES
    ISvrn7Driver method: ResolveDidAsync(string)
#>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline)] [string] $Did)
    process { Assert-FederationDriver; $Script:FederationDriver.ResolveDidAsync($Did).GetAwaiter().GetResult() }
}

function Test-Svrn7DidActive {
<#
.SYNOPSIS
    Returns $true if the DID Document is in Active status.
.PARAMETER Did
    DID to test. Accepts pipeline input.
.EXAMPLE
    Test-Svrn7DidActive -Did 'did:drn:3J98t1...'
.OUTPUTS
    [bool]
.NOTES
    ISvrn7Driver method: IsDidActiveAsync(string)
#>
    [CmdletBinding()] [OutputType([bool])]
    param([Parameter(Mandatory, ValueFromPipeline)] [string] $Did)
    process { Assert-FederationDriver; $Script:FederationDriver.IsDidActiveAsync($Did).GetAwaiter().GetResult() }
}
#endregion

###############################################################################
#region VC REGISTRY
###############################################################################
function Get-Svrn7VcsBySubject {
<#
.SYNOPSIS
    Returns all Verifiable Credentials issued to a subject DID.
.PARAMETER SubjectDid
    Subject DID to query. Accepts pipeline input.
.EXAMPLE
    Get-Svrn7VcsBySubject -SubjectDid 'did:sovronia:citizen001...'
.OUTPUTS
    [IReadOnlyList[Svrn7.Core.Models.VcRecord]]
.NOTES
    ISvrn7Driver method: GetVcsBySubjectAsync(string)
#>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)] [string] $SubjectDid)
    process { Assert-FederationDriver; $Script:FederationDriver.GetVcsBySubjectAsync($SubjectDid).GetAwaiter().GetResult() }
}

function Get-Svrn7VcById {
<#
.SYNOPSIS
    Returns a Verifiable Credential by its VC ID.
.PARAMETER VcId
    UUID identifier of the VC (e.g. 'urn:uuid:a1b2c3d4-...').
.EXAMPLE
    Get-Svrn7VcById -VcId 'urn:uuid:a1b2c3d4-...'
.OUTPUTS
    [Svrn7.Core.Models.VcRecord] or $null.
.NOTES
    ISvrn7Driver method: GetVcByIdAsync(string)
#>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $VcId)
    Assert-FederationDriver
    $Script:FederationDriver.GetVcByIdAsync($VcId).GetAwaiter().GetResult()
}

function Revoke-Svrn7Vc {
<#
.SYNOPSIS
    Permanently revokes a Verifiable Credential.
.DESCRIPTION
    Calls ISvrn7Driver.RevokeVcAsync(). Revocation is permanent — the record is
    retained with Status = Revoked.
.PARAMETER VcId
    UUID identifier of the VC to revoke.
.PARAMETER Reason
    Human-readable revocation reason.
.EXAMPLE
    Revoke-Svrn7Vc -VcId 'urn:uuid:a1b2c3d4-...' -Reason 'Citizen request'
.OUTPUTS
    None. Throws on failure.
.NOTES
    ISvrn7Driver method: RevokeVcAsync(string, string)
#>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact='Medium')]
    param(
        [Parameter(Mandatory)] [string] $VcId,
        [Parameter(Mandatory)] [string] $Reason
    )
    Assert-FederationDriver
    if ($PSCmdlet.ShouldProcess($VcId, "Revoke VC (reason: $Reason)")) {
        $Script:FederationDriver.RevokeVcAsync($VcId, $Reason).GetAwaiter().GetResult()
    }
}
#endregion

###############################################################################
#region MERKLE AUDIT LOG
###############################################################################
function Get-Svrn7MerkleRoot {
<#
.SYNOPSIS
    Returns the current Merkle root hash of the audit log.
.EXAMPLE
    Get-Svrn7MerkleRoot
.OUTPUTS
    [string] Hex-encoded 32-byte SHA-256 Merkle root.
.NOTES
    ISvrn7Driver method: GetMerkleRootAsync()
    Spec: draft-herman-web7-merkle-audit-log-00 §4
#>
    [CmdletBinding()] [OutputType([string])] param()
    Assert-FederationDriver; $Script:FederationDriver.GetMerkleRootAsync().GetAwaiter().GetResult()
}

function Get-Svrn7MerkleLogSize {
<#
.SYNOPSIS
    Returns the number of entries in the Merkle audit log.
.EXAMPLE
    Get-Svrn7MerkleLogSize
.OUTPUTS
    [long]
.NOTES
    ISvrn7Driver method: GetLogSizeAsync()
#>
    [CmdletBinding()] [OutputType([long])] param()
    Assert-FederationDriver; $Script:FederationDriver.GetLogSizeAsync().GetAwaiter().GetResult()
}

function Get-Svrn7MerkleTreeHead {
<#
.SYNOPSIS
    Returns the most recently signed Merkle Signed Tree Head (STH).
.DESCRIPTION
    The STH contains RootHash, TreeSize, Timestamp, and Foundation governance signature.
    Returns $null if no STH has been signed yet.
.EXAMPLE
    $head = Get-Svrn7MerkleTreeHead
    $head.RootHash
.OUTPUTS
    [Svrn7.Core.Models.TreeHead] or $null.
.NOTES
    ISvrn7Driver method: GetLatestTreeHeadAsync()
    Spec: draft-herman-web7-merkle-audit-log-00 §6
#>
    [CmdletBinding()] param()
    Assert-FederationDriver; $Script:FederationDriver.GetLatestTreeHeadAsync().GetAwaiter().GetResult()
}

function Invoke-Svrn7SignMerkleTreeHead {
<#
.SYNOPSIS
    Signs the current Merkle root and records a new Signed Tree Head.
.DESCRIPTION
    Calls ISvrn7Driver.SignMerkleTreeHeadAsync(). Must be called at least every 24 hours
    — an older STH causes the health check to report Degraded.
.EXAMPLE
    $head = Invoke-Svrn7SignMerkleTreeHead
    $head.RootHash; $head.Timestamp
.OUTPUTS
    [Svrn7.Core.Models.TreeHead]
.NOTES
    ISvrn7Driver method: SignMerkleTreeHeadAsync()
    Spec: draft-herman-web7-merkle-audit-log-00 §6.4
#>
    [CmdletBinding(SupportsShouldProcess)] param()
    Assert-FederationDriver
    if ($PSCmdlet.ShouldProcess('Merkle log', 'Sign tree head')) {
        $Script:FederationDriver.SignMerkleTreeHeadAsync().GetAwaiter().GetResult()
    }
}
#endregion

###############################################################################
#region GDPR ERASURE
###############################################################################
function Invoke-Svrn7GdprErasure {
<#
.SYNOPSIS
    Performs a GDPR Article 17 erasure for a citizen (Foundation signature required).
.DESCRIPTION
    Calls ISvrn7Driver.ErasePersonAsync(). Permanently:
      1. Deactivates the citizen's DID Document.
      2. Revokes all Active VCs issued to the citizen.
      3. Overwrites the stored private key with random bytes ('BURNED:...').
      4. Appends a GdprErasure entry to the Merkle audit log.
    UTXO records and Merkle entries are retained for audit integrity.

    FoundationSignature must be a CESR secp256k1 signature over:
        ERASE:{citizenDid}:{requestTimestamp:ISO-8601-UTC}
    Produce offline with the Foundation private key.
.PARAMETER Did
    Primary DID of the citizen to erase.
.PARAMETER FoundationSignature
    CESR secp256k1 signature from the Foundation governance key.
.PARAMETER RequestTimestamp
    UTC timestamp of the Foundation authorisation (±10 min of server time).
.EXAMPLE
    Invoke-Svrn7GdprErasure `
        -Did                'did:sovronia:abc123...' `
        -FoundationSignature $sig `
        -RequestTimestamp   ([DateTimeOffset]::UtcNow)
.OUTPUTS
    [PSCustomObject] Svrn7.GdprErasure
        Did [string]; ErasedAt [string]; Success [bool]
.NOTES
    ISvrn7Driver method: ErasePersonAsync(string, string, DateTimeOffset)
    Spec: draft-herman-svrn7-gdpr-erasure-00 §6. IRREVERSIBLE.
#>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)] [string]        $Did,
        [Parameter(Mandatory)] [string]        $FoundationSignature,
        [Parameter(Mandatory)] [DateTimeOffset] $RequestTimestamp
    )
    Assert-FederationDriver
    if ($PSCmdlet.ShouldProcess($Did, 'GDPR Article 17 erasure (IRREVERSIBLE)')) {
        $r = $Script:FederationDriver.ErasePersonAsync($Did, $FoundationSignature, $RequestTimestamp).GetAwaiter().GetResult()
        Resolve-OperationResult $r 'GdprErasure' | Out-Null
        [PSCustomObject]@{ PSTypeName=$Script:TypeGdprErasure; Did=$Did; ErasedAt=[DateTimeOffset]::UtcNow.ToString('O'); Success=$true }
    }
}
#endregion

###############################################################################
#region TDA DIDCOMM HANDLERS
###############################################################################

function Invoke-Web7FederationQuery {
    <#
    .SYNOPSIS
        Handles federation/1.0/federation-query — returns the current Federation record.
    .DESCRIPTION
        No body fields required. Replies with federation/1.0/federation-query-result
        containing the Federation DID, name, supply, epoch, and active status.
    .PARAMETER MessageDid
        TDA resource DID URL for the inbox message.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )
    process {
        $drv = Get-ActiveFederationDriver
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { throw "Invoke-Web7FederationQuery: message '$MessageDid' not found." }
        if (-not $msg.FromDid) { throw "Invoke-Web7FederationQuery: FromDid not set — cannot route reply." }

        $fed = $drv.GetFederationAsync().GetAwaiter().GetResult()

        $payload = if ($fed) {
            @{
                found                = $true
                federationDid        = $fed.Did
                federationName       = $fed.FederationName
                primaryDidMethodName = $fed.PrimaryDidMethodName
                totalSupplyGrana     = $fed.TotalSupplyGrana
                endowmentPerSocietyGrana = $fed.EndowmentPerSocietyGrana
                currentEpoch         = $drv.GetCurrentEpoch()
                isActive             = $fed.IsActive
                createdAt            = $fed.CreatedAt.ToString('o')
                queriedAt            = [datetimeoffset]::UtcNow.ToString('o')
            }
        } else {
            @{
                found     = $false
                queriedAt = [datetimeoffset]::UtcNow.ToString('o')
            }
        }

        $json     = $payload | ConvertTo-Json -Compress
        $endpoint = Resolve-SocietySenderEndpoint -Did $msg.FromDid
        if (-not $endpoint) {
            Write-Warning "Invoke-Web7FederationQuery: no DIDComm service endpoint for '$($msg.FromDid)' — reply skipped."
            return
        }
        Write-Information "Invoke-Web7FederationQuery: replying to $($msg.FromDid)"

        [Svrn7.TDA.OutboundMessage]::new($endpoint, $json)
    }
}

function Invoke-Web7SocietyList {
    <#
    .SYNOPSIS
        Handles federation/1.0/society-list — returns all registered societies.
    .DESCRIPTION
        No body fields required.  Replies to the sender's DID Document endpoint
        with a federation/1.0/society-list-result containing an array of society objects.
    .PARAMETER MessageDid
        TDA resource DID URL for the inbox message.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )
    process {
        $drv = Get-ActiveFederationDriver
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { throw "Invoke-Web7SocietyList: message '$MessageDid' not found." }
        if (-not $msg.FromDid) { throw "Invoke-Web7SocietyList: FromDid not set — cannot route reply." }

        $body      = $msg.PackedPayload | ConvertFrom-Json
        $societies   = @($drv.GetAllSocietiesAsync().GetAwaiter().GetResult())
        $activeSocs  = @($societies | Where-Object { $_.IsActive })

        $societyList = @($societies | ForEach-Object {
            $docJson = $SVRN7.GetDidDocumentJson($_.Did)
            @{
                societyDid           = $_.Did
                societyName          = $_.SocietyName
                primaryDidMethodName = $_.PrimaryDidMethodName
                endpointUrl          = Resolve-SocietySenderEndpoint -Did $_.Did
                didDocument          = if ($docJson) { $docJson | ConvertFrom-Json } else { $null }
                isActive             = $_.IsActive
                registeredAt         = $_.RegisteredAt.ToString('o')
            }
        })

        $payload = @{
            count       = $societies.Count
            activeCount = $activeSocs.Count
            societies   = $societyList
            queriedAt   = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Depth 15 -Compress

        $endpoint = Resolve-SocietySenderEndpoint -Did $msg.FromDid

        if (-not $endpoint) {
            Write-Warning "Invoke-Web7SocietyList: cannot resolve endpoint for sender '$($msg.FromDid)' — result not delivered."
            return
        }
        Write-Information "Invoke-Web7SocietyList: $($societies.Count) society/societies, replying to $endpoint"

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/society-list-result'
            from = $SVRN7.LocalDid
            to   = @($msg.FromDid)
            body = $payload
        } | ConvertTo-Json -Compress

        [Svrn7.TDA.OutboundMessage]::new($endpoint, $envelope)
    }
}

function Invoke-Web7FederationInit {
    <#
    .SYNOPSIS
        Handles federation/1.0/initialize-federation — initialises the Federation record (idempotent).
    .DESCRIPTION
        Body: { "federationDid":  "<DID>",
                "federationName": "<name>",
                "publicKeyHex":   "<hex>" }

        Replies to the sender's DID Document endpoint with
        federation/1.0/initialize-federation-result.  If the sender's endpoint
        cannot be resolved the init still succeeds and a warning is written to
        the PS stream; no error is raised.
    .PARAMETER MessageDid
        TDA resource DID URL for the inbox message.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )
    process {
        $drv = Get-ActiveFederationDriver
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { throw "Invoke-Web7FederationInit: message '$MessageDid' not found." }

        $body = $msg.PackedPayload | ConvertFrom-Json
        Assert-BodyFields $body @('federationDid','federationName','publicKeyHex') 'Invoke-Web7FederationInit'

        $svcUrl = if ($body.PSObject.Properties['serviceEndpointUrl'] -and $body.serviceEndpointUrl) { $body.serviceEndpointUrl } else { $null }
        $fedDoc  = $drv.CreateDidDocument($body.federationDid, $body.publicKeyHex, 'drn', $svcUrl)

        $result = $drv.InitialiseFederationAsync(
            $fedDoc,
            $body.federationName
        ).GetAwaiter().GetResult()

        Resolve-OperationResult $result 'FederationInit' | Out-Null

        $fed = $drv.GetFederationAsync().GetAwaiter().GetResult()

        $alreadyInit = ($result.Payload -and $result.Payload.alreadyInitialised -eq $true)
        $payload = @{
            federationDid      = $fed.Did
            federationName     = $fed.FederationName
            totalSupplyGrana   = $fed.TotalSupplyGrana
            alreadyInitialised = $alreadyInit
            initialisedAt        = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Compress

        $endpoint = if ($msg.FromDid) { Resolve-SocietySenderEndpoint -Did $msg.FromDid }

        if (-not $endpoint) {
            Write-Warning "Invoke-Web7FederationInit: cannot resolve endpoint for sender '$($msg.FromDid)' — init succeeded but no initialize-federation-result will be sent."
            return
        }

        Write-Information "Invoke-Web7FederationInit: federation '$($fed.Did)' initialised, replying to $endpoint"

        [Svrn7.TDA.OutboundMessage]::new($endpoint, $payload)
    }
}

function Invoke-Web7RegisterSociety {
    <#
    .SYNOPSIS
        Handles federation/1.0/register-society — registers a new Society.
    .DESCRIPTION
        Body: { "publicKeyHex": "<hex>", "societyName": "<name>",
                "drawAmountGrana": <long>, "overdraftCeilingGrana": <long> }
        societyDid is derived server-side: did:drn:federation.svrn7.net/{societyName}/1.0/{blake3(pubkey)}.
        Replies with federation/1.0/register-society-result.
    .PARAMETER MessageDid
        TDA resource DID URL for the inbox message.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )
    process {
        $drv = Get-ActiveFederationDriver
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { throw "Invoke-Web7RegisterSociety: message '$MessageDid' not found." }
        if (-not $msg.FromDid) { throw "Invoke-Web7RegisterSociety: FromDid not set — cannot route reply." }

        $body = $msg.PackedPayload | ConvertFrom-Json
        Assert-BodyFields $body @('publicKeyHex','societyName') 'Invoke-Web7RegisterSociety'

        $pubBytes    = [System.Convert]::FromHexString($body.publicKeyHex)
        $genesisHash = [Svrn7.Crypto.CryptoService]::new().Blake3Hex($pubBytes)
        $societyDid  = "did:drn:federation.svrn7.net/$($body.societyName)/1.0/$genesisHash"
        $svcUrl      = if ($body.PSObject.Properties['serviceEndpointUrl'] -and $body.serviceEndpointUrl) { $body.serviceEndpointUrl } else { $null }
        $didDoc      = $drv.CreateDidDocument($societyDid, $body.publicKeyHex, 'drn', $svcUrl)

        $request = [Svrn7.Core.Models.RegisterSocietyRequest]::new()
        $request.DidDocument          = $didDoc
        $request.PrivateKeyBytes      = [byte[]]@()
        $request.SocietyName          = $body.societyName
        $request.DrawAmountGrana      = if ($body.PSObject.Properties['drawAmountGrana'])       { [long]$body.drawAmountGrana }       else { 0L }
        $request.OverdraftCeilingGrana= if ($body.PSObject.Properties['overdraftCeilingGrana']) { [long]$body.overdraftCeilingGrana } else { 0L }

        $result = $drv.RegisterSocietyAsync($request).GetAwaiter().GetResult()
        Resolve-OperationResult $result 'RegisterSociety' | Out-Null

        $societyDocJson    = $SVRN7.GetDidDocumentJson($societyDid)
        $federationDocJson = $SVRN7.GetDidDocumentJson($SVRN7.LocalDid)

        $payload = @{
            societyDid            = $societyDid
            societyName           = $body.societyName
            societyDidDocument    = if ($societyDocJson)    { $societyDocJson    | ConvertFrom-Json } else { $null }
            federationDid         = $SVRN7.LocalDid
            federationEndpointUrl = $SVRN7.ServiceEndpointUrl
            federationDidDocument = if ($federationDocJson) { $federationDocJson | ConvertFrom-Json } else { $null }
            drawAmountGrana       = $request.DrawAmountGrana
            overdraftCeilingGrana = $request.OverdraftCeilingGrana
            success               = $true
            registeredAt          = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Depth 15 -Compress

        $endpoint = Resolve-SocietySenderEndpoint -Did $msg.FromDid
        if (-not $endpoint) {
            Write-Warning "Invoke-Web7RegisterSociety: no DIDComm service endpoint for '$($msg.FromDid)' — reply skipped."
            return
        }
        Write-Information "Invoke-Web7RegisterSociety: registered '$($body.societyDid)', replying to $($msg.FromDid)"

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/register-society-result'
            from = $SVRN7.LocalDid
            to   = @($msg.FromDid)
            body = $payload
        } | ConvertTo-Json -Compress

        [Svrn7.TDA.OutboundMessage]::new($endpoint, $envelope)
    }
}

function Invoke-Web7RegisterSocietyResult {
    <#
    .SYNOPSIS
        Handles Svrn7.Federation.0.8.0/register-society-result on the Society TDA.

    .DESCRIPTION
        Stores the Society's own DID Document and the Federation's DID Document in the
        local registry, then wires the Federation as the Society's parent TDA (persisted
        to agent-identity.json via $SVRN7.SetParentTda).

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.
    #>
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )
    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) {
            Write-Warning "Invoke-Web7RegisterSocietyResult: message $MessageDid not found."
            return
        }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        Assert-BodyFields $body @('societyDid','federationDid','federationEndpointUrl','societyDidDocument','federationDidDocument','success') 'Invoke-Web7RegisterSocietyResult'

        if (-not $body.success) {
            Write-Warning "Invoke-Web7RegisterSocietyResult: registration failed — parent TDA not updated."
            return
        }

        # Store Society's own DID Document (created by Federation during registration)
        $SVRN7.StoreReceivedDidDocumentAsync(
            ($body.societyDidDocument | ConvertTo-Json -Depth 15 -Compress)
        ).GetAwaiter().GetResult()

        # Store Federation's DID Document (enables future DID resolution without network)
        $SVRN7.StoreReceivedDidDocumentAsync(
            ($body.federationDidDocument | ConvertTo-Json -Depth 15 -Compress)
        ).GetAwaiter().GetResult()

        # Wire parent TDA — updates memory and persists to agent-identity.json
        $SVRN7.SetParentTda($body.federationDid, $body.federationEndpointUrl)

        Write-Information "Invoke-Web7RegisterSocietyResult: registered with $($body.federationDid) at $($body.federationEndpointUrl)"
    }
}

function Invoke-Web7SocietyListResult {
    <#
    .SYNOPSIS
        Handles Svrn7.Federation.0.8.0/society-list-result — stores each society's
        DID Document in the local registry.

    .DESCRIPTION
        Called on any TDA that sent a society-list request. Stores received Society DID
        Documents locally so subsequent register-citizen can be sent without a DID resolve
        round-trip. The societyEndpointUrl in each entry is used by the Citizen to
        route the register-citizen request.

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.
    #>
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )
    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) {
            Write-Warning "Invoke-Web7SocietyListResult: message $MessageDid not found."
            return
        }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop

        $stored = 0
        foreach ($society in $body.societies) {
            if ($society.PSObject.Properties['didDocument'] -and $society.didDocument) {
                $SVRN7.StoreReceivedDidDocumentAsync(
                    ($society.didDocument | ConvertTo-Json -Depth 15 -Compress)
                ).GetAwaiter().GetResult()
                $stored++
            }
        }

        Write-Information "Invoke-Web7SocietyListResult: stored $stored society DID Document(s) from $($body.count) result(s)"
    }
}

#endregion

###############################################################################
#region TEST UTILITIES
###############################################################################

function Remove-Svrn7Databases {
    <#
    .SYNOPSIS
        Deletes all SVRN7 LiteDB database files and their companion log files.

    .DESCRIPTION
        Removes the five LiteDB files used by a SVRN7 Society deployment:

            svrn7.db        — wallets, UTXOs, citizens, societies, Merkle log
            svrn7-dids.db   — DID Documents and verification methods
            svrn7-vcs.db    — Verifiable Credentials
            svrn7-inbox.db  — DIDComm inbox, outbox, processed orders
            svrn7-schemas.db— JSON Schema 2020-12 registry

        LiteDB also writes a companion journal file alongside each database
        (e.g. svrn7.db-log). Those files are removed as well when present.

        THIS CMDLET IS DESTRUCTIVE AND IRREVERSIBLE. It is intended for test
        teardown only. Always run against a test data directory, never against
        a production deployment.

        Confirmation is required. Pass -Confirm:$false to suppress the prompt
        in automated test scripts. Use -WhatIf to preview without deleting.

    .PARAMETER Svrn7DbPath
        Path to svrn7.db. Default: data/svrn7.db

    .PARAMETER DidsDbPath
        Path to svrn7-dids.db. Default: data/svrn7-dids.db

    .PARAMETER VcsDbPath
        Path to svrn7-vcs.db. Default: data/svrn7-vcs.db

    .PARAMETER InboxDbPath
        Path to svrn7-inbox.db. Default: data/svrn7-inbox.db

    .PARAMETER SchemasDbPath
        Path to svrn7-schemas.db. Default: data/svrn7-schemas.db

    .OUTPUTS
        PSCustomObject[] — one entry per candidate file with Path and Removed fields.

    .EXAMPLE
        # Interactive confirmation prompt:
        Remove-Svrn7Databases

    .EXAMPLE
        # Non-interactive test teardown:
        Remove-Svrn7Databases -Confirm:$false

    .EXAMPLE
        # Preview without deleting:
        Remove-Svrn7Databases -WhatIf

    .EXAMPLE
        # Custom data directory:
        Remove-Svrn7Databases -Svrn7DbPath tests/data/svrn7.db `
                              -DidsDbPath   tests/data/svrn7-dids.db `
                              -VcsDbPath    tests/data/svrn7-vcs.db `
                              -InboxDbPath  tests/data/svrn7-inbox.db `
                              -SchemasDbPath tests/data/svrn7-schemas.db `
                              -Confirm:$false
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    [OutputType([PSCustomObject[]])]
    param(
        [string] $Svrn7DbPath   = 'data/svrn7.db',
        [string] $DidsDbPath    = 'data/svrn7-dids.db',
        [string] $VcsDbPath     = 'data/svrn7-vcs.db',
        [string] $InboxDbPath   = 'data/svrn7-inbox.db',
        [string] $SchemasDbPath = 'data/svrn7-schemas.db'
    )

    $results = [System.Collections.Generic.List[PSCustomObject]]::new()

    # Collect every candidate path: the main .db file and its LiteDB journal companion.
    $candidates = foreach ($dbPath in @($Svrn7DbPath, $DidsDbPath, $VcsDbPath,
                                         $InboxDbPath, $SchemasDbPath)) {
        $dbPath
        "$dbPath-log"   # LiteDB 5 journal file written alongside the main database
    }

    foreach ($path in $candidates) {
        $exists = Test-Path -LiteralPath $path -PathType Leaf

        if ($exists) {
            if ($PSCmdlet.ShouldProcess($path, 'Delete LiteDB database file')) {
                Remove-Item -LiteralPath $path -Force
                $removed = $true
                Write-Verbose "Removed: $path"
            } else {
                $removed = $false   # -WhatIf path
            }
        } else {
            $removed = $false
            Write-Verbose "Not found (skipped): $path"
        }

        $results.Add([PSCustomObject]@{
            Path    = $path
            Existed = $exists
            Removed = $removed
        })
    }

    $results.ToArray()
}

#endregion

###############################################################################
#region MODULE CLEANUP
###############################################################################
$ExecutionContext.SessionState.Module.OnRemove = {
    if ($Script:FederationDriver) {
        try { $Script:FederationDriver.DisposeAsync().GetAwaiter().GetResult() } catch {}
        $Script:FederationDriver = $null
    }
}
#endregion

Export-ModuleMember -Function @(
    'Disable-Svrn7Society'
    'Get-Svrn7Balance'
    'Get-Svrn7Citizen'
    'Get-Svrn7CitizenDids'
    'Get-Svrn7CurrentEpoch'
    'Get-Svrn7Federation'
    'Get-Svrn7MerkleLogSize'
    'Get-Svrn7MerkleRoot'
    'Get-Svrn7MerkleTreeHead'
    'Get-Svrn7Society'
    'Get-Svrn7VcById'
    'Get-Svrn7VcsBySubject'
    'Initialize-Svrn7FederationDriver'
    'Remove-Svrn7Databases'
    'Invoke-Svrn7BatchTransfer'
    'Invoke-Web7FederationQuery'
    'Invoke-Web7SocietyList'
    'Invoke-Web7SocietyListResult'
    'Invoke-Web7FederationInit'
    'Invoke-Web7RegisterSociety'
    'Invoke-Web7RegisterSocietyResult'
    'Invoke-Svrn7GdprErasure'
    'Invoke-Svrn7SignMerkleTreeHead'
    'Invoke-Svrn7SignSecp256k1'
    'Invoke-Svrn7Transfer'
    'New-Svrn7Did'
    'New-Svrn7Ed25519KeyPair'
    'New-Svrn7KeyPair'
    'Initialize-Svrn7Citizen'
    'Initialize-Svrn7Federation'
    'Initialize-Svrn7Society'
    'Register-Svrn7Society'
    'Resolve-Svrn7CitizenPrimaryDid'
    'Resolve-Svrn7Did'
    'Revoke-Svrn7Vc'
    'Test-Svrn7CitizenActive'
    'Test-Svrn7DidActive'
    'Test-Svrn7SignatureSecp256k1'
    'Test-Svrn7SocietyActive'
    'Update-Svrn7FederationSupply'
    'Send-LocalDIDCommMessage'
)
