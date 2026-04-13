#Requires -Version 7.2
#Requires -PSEdition Core
<#
.SYNOPSIS
    Svrn7.Federation — PowerShell cmdlets wrapping ISvrn7Driver.

.DESCRIPTION
    Complete script module exposing every operation of the SOVRONA (SVRN7) Federation-level
    driver as idiomatic PowerShell cmdlets.  All cmdlets have full comment-based help,
    typed [PSCustomObject] output, pipeline support, and -WhatIf/-Confirm on mutations.

    SETUP
        1. Compile the Svrn7 .NET 8 solution and place assemblies in a bin/ folder
           adjacent to this file, or set $env:SVRN7_BIN_PATH.
        2. Import-Module ./lobes/Svrn7.Federation.psd1
        3. Initialize-Svrn7Federation
        4. $kp = New-Svrn7KeyPair

    RELATED
        Svrn7.Society — ISvrn7SocietyDriver cmdlets. Requires Svrn7.Federation first.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Svrn7.Common.psm1')

###############################################################################
#region INITIALISATION
###############################################################################
function Initialize-Svrn7Federation {
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
    Initialize-Svrn7Federation
.EXAMPLE
    Initialize-Svrn7Federation -DbPath 'C:\data\svrn7' -DidMethodName 'drn' -Verbose
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
    directly into New-Svrn7Did, Register-Svrn7Society, Register-Svrn7Citizen, and
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
    Assert-FederationDriver
    $kp = $Script:FederationDriver.GenerateSecp256k1KeyPair()
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
    Assert-FederationDriver
    $kp = $Script:FederationDriver.GenerateEd25519KeyPair()
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
    Constructs a W3C DID string from a SVRN7 key pair.
.DESCRIPTION
    Base58btc-encodes the public key bytes and prepends the method prefix:
        did:{MethodName}:{Base58btc(publicKeyBytes)}
    Accepts pipeline input from New-Svrn7KeyPair.
.PARAMETER KeyPair
    [Svrn7.KeyPair] from New-Svrn7KeyPair.
.PARAMETER MethodName
    DID method name. Must match [a-z0-9]+. Default: 'drn'.
.EXAMPLE
    $kp  = New-Svrn7KeyPair
    $did = New-Svrn7Did -KeyPair $kp
    $did.Did   # did:drn:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
.EXAMPLE
    New-Svrn7KeyPair | New-Svrn7Did -MethodName 'sovronia'
.OUTPUTS
    [PSCustomObject] Svrn7.Did
        Did          [string]
        MethodName   [string]
        PublicKeyHex [string]
.NOTES
    ISvrn7Driver method: Base58EncodeAsync(byte[])
#>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, ValueFromPipeline)] [PSCustomObject] $KeyPair,
        [Parameter()] [ValidatePattern('^[a-z0-9]+$')] [string] $MethodName = 'drn'
    )
    process {
        Assert-FederationDriver
        if (-not $KeyPair.PublicKeyHex) {
            throw [System.ArgumentException]::new('KeyPair.PublicKeyHex is empty. Use New-Svrn7KeyPair.')
        }
        $bytes = [System.Convert]::FromHexString($KeyPair.PublicKeyHex)
        $id    = $Script:FederationDriver.Base58EncodeAsync($bytes).GetAwaiter().GetResult()
        [PSCustomObject]@{
            PSTypeName = $Script:TypeDid; Did = "did:${MethodName}:${id}"
            MethodName = $MethodName; PublicKeyHex = $KeyPair.PublicKeyHex
        }
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
function Register-Svrn7Citizen {
<#
.SYNOPSIS
    Registers a citizen at the Federation level (no Society endowment).
.DESCRIPTION
    Calls ISvrn7Driver.RegisterCitizenAsync(). Creates a citizen record and
    DID Document but does NOT transfer the 1,000 SVRN7 endowment. For
    Society-scoped registration with endowment, use Register-Svrn7CitizenInSociety
    from Svrn7.Society.
.PARAMETER Did
    DID string. Obtain from New-Svrn7Did.
.PARAMETER KeyPair
    secp256k1 [Svrn7.KeyPair] for the citizen.
.EXAMPLE
    $kp  = New-Svrn7KeyPair
    $did = (New-Svrn7Did -KeyPair $kp).Did
    Register-Svrn7Citizen -Did $did -KeyPair $kp
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
        [Parameter(Mandatory)] [string]        $Did,
        [Parameter(Mandatory)] [PSCustomObject] $KeyPair
    )
    Assert-FederationDriver
    if ($PSCmdlet.ShouldProcess($Did, 'Register citizen')) {
        $r = $Script:FederationDriver.RegisterCitizenAsync(
            [Svrn7.Core.Models.RegisterCitizenRequest]@{
                Did = $Did; PublicKeyHex = $KeyPair.PublicKeyHex
                PrivateKeyBytes = $KeyPair.PrivateKeyBytes
            }).GetAwaiter().GetResult()
        Resolve-OperationResult $r 'RegisterCitizen' | Out-Null
        [PSCustomObject]@{ PSTypeName = $Script:TypeCitizenReg; CitizenDid = $Did; Success = $true }
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
function Register-Svrn7Society {
<#
.SYNOPSIS
    Registers a new digital Society with the Federation.
.DESCRIPTION
    Calls ISvrn7Driver.RegisterSocietyAsync() to create a Society record, DID Document,
    and wallet. The primary DID method name is IMMUTABLE after registration. It must:
      - Match [a-z0-9]+ (W3C DID Core §8.1 — no hyphens or underscores)
      - Not already be Active or Dormant in the Federation registry
.PARAMETER Did
    Society DID string from New-Svrn7Did (use -MethodName matching the primary method).
.PARAMETER KeyPair
    secp256k1 [Svrn7.KeyPair] for the Society.
.PARAMETER Name
    Human-readable Society name (e.g. 'Sovronia Digital Nation').
.PARAMETER MethodName
    Primary DID method name. Must match [a-z0-9]+. Immutable after registration.
.PARAMETER DrawAmountGrana
    Overdraft draw increment in grana. Default: 1,000,000,000,000 (1,000 SVRN7).
.PARAMETER OverdraftCeilingGrana
    Maximum outstanding overdraft in grana. Default: 10,000,000,000,000 (10,000 SVRN7).
.EXAMPLE
    $kp     = New-Svrn7KeyPair
    $didObj = New-Svrn7Did -KeyPair $kp -MethodName 'sovronia'
    Register-Svrn7Society -Did $didObj.Did -KeyPair $kp `
        -Name 'Sovronia Digital Nation' -MethodName 'sovronia'
.EXAMPLE
    $params = @{ Did=$didObj.Did; KeyPair=$kp; Name='Sovronia'; MethodName='sovronia' }
    Register-Svrn7Society @params
.OUTPUTS
    [PSCustomObject] Svrn7.SocietyRegistration
        SocietyDid [string]; SocietyName [string]; MethodName [string]
        DrawAmountGrana [long]; OverdraftCeilingGrana [long]; Success [bool]
.NOTES
    ISvrn7Driver method: RegisterSocietyAsync(RegisterSocietyRequest)
    Spec: draft-herman-web7-society-architecture-00 §4.2
#>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)] [string]        $Did,
        [Parameter(Mandatory)] [PSCustomObject] $KeyPair,
        [Parameter(Mandatory)] [string]        $Name,
        [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9]+$')] [string] $MethodName,
        [Parameter()] [ValidateRange(1L,[long]::MaxValue)] [long] $DrawAmountGrana       = 1_000_000_000_000L,
        [Parameter()] [ValidateRange(1L,[long]::MaxValue)] [long] $OverdraftCeilingGrana = 10_000_000_000_000L
    )
    Assert-FederationDriver
    if ($PSCmdlet.ShouldProcess($Did, "Register Society '$Name' (method: $MethodName)")) {
        $r = $Script:FederationDriver.RegisterSocietyAsync(
            [Svrn7.Core.Models.RegisterSocietyRequest]@{
                Did=$Did; PublicKeyHex=$KeyPair.PublicKeyHex; PrivateKeyBytes=$KeyPair.PrivateKeyBytes
                SocietyName=$Name; PrimaryDidMethodName=$MethodName
                DrawAmountGrana=$DrawAmountGrana; OverdraftCeilingGrana=$OverdraftCeilingGrana
            }).GetAwaiter().GetResult()
        Resolve-OperationResult $r 'RegisterSociety' | Out-Null
        [PSCustomObject]@{
            PSTypeName=$Script:TypeSocietyReg; SocietyDid=$Did; SocietyName=$Name
            MethodName=$MethodName; DrawAmountGrana=$DrawAmountGrana
            OverdraftCeilingGrana=$OverdraftCeilingGrana; Success=$true
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
#region DID METHOD GOVERNANCE
###############################################################################
function Register-Svrn7DidMethod {
<#
.SYNOPSIS
    Registers an additional DID method name for a Society (self-service).
.DESCRIPTION
    Calls ISvrn7Driver.RegisterAdditionalDidMethodAsync(). No Foundation signature
    required. The method name must match [a-z0-9]+ and not be Active or Dormant.
    The primary method name (set at Society registration) is immutable.
.PARAMETER SocietyDid
    DID of the Society registering the new method name.
.PARAMETER MethodName
    Additional DID method name. Must match [a-z0-9]+. Accepts pipeline input.
.EXAMPLE
    Register-Svrn7DidMethod -SocietyDid 'did:sovronia:abc...' -MethodName 'sovroniamed'
.EXAMPLE
    'sovroniamed','sovroniaedu' | Register-Svrn7DidMethod -SocietyDid $soc
.OUTPUTS
    [PSCustomObject] Svrn7.DidMethodRegistration
        SocietyDid [string]; MethodName [string]; Status 'Active'; Success [bool]
.NOTES
    ISvrn7Driver method: RegisterAdditionalDidMethodAsync(string, string)
    Spec: draft-herman-did-method-governance-00 §6.2
#>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)] [string] $SocietyDid,
        [Parameter(Mandatory, ValueFromPipeline)] [ValidatePattern('^[a-z0-9]+$')] [string] $MethodName
    )
    process {
        Assert-FederationDriver
        if ($PSCmdlet.ShouldProcess($SocietyDid, "Register DID method '$MethodName'")) {
            $r = $Script:FederationDriver.RegisterAdditionalDidMethodAsync($SocietyDid, $MethodName).GetAwaiter().GetResult()
            Resolve-OperationResult $r 'RegisterDidMethod' | Out-Null
            [PSCustomObject]@{ PSTypeName=$Script:TypeDidMethodReg; SocietyDid=$SocietyDid; MethodName=$MethodName; Status='Active'; Success=$true }
        }
    }
}

function Unregister-Svrn7DidMethod {
<#
.SYNOPSIS
    Deregisters an additional DID method name from a Society.
.DESCRIPTION
    Calls ISvrn7Driver.DeregisterDidMethodAsync(). The name enters dormancy (default 30 days).
    The primary method name cannot be deregistered (throws PrimaryDidMethodException).
    Existing DIDs under the name remain valid and resolvable.
.PARAMETER SocietyDid
    DID of the owning Society.
.PARAMETER MethodName
    DID method name to deregister. Must not be the primary.
.EXAMPLE
    Unregister-Svrn7DidMethod -SocietyDid 'did:sovronia:abc...' -MethodName 'sovroniaedu'
.OUTPUTS
    [PSCustomObject] Svrn7.DidMethodDeregistration
        SocietyDid [string]; MethodName [string]; Status 'Dormant'; Success [bool]
.NOTES
    ISvrn7Driver method: DeregisterDidMethodAsync(string, string)
    Spec: draft-herman-did-method-governance-00 §7
#>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact='Medium')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)] [string] $SocietyDid,
        [Parameter(Mandatory)] [string] $MethodName
    )
    Assert-FederationDriver
    if ($PSCmdlet.ShouldProcess($SocietyDid, "Deregister DID method '$MethodName'")) {
        $r = $Script:FederationDriver.DeregisterDidMethodAsync($SocietyDid, $MethodName).GetAwaiter().GetResult()
        Resolve-OperationResult $r 'UnregisterDidMethod' | Out-Null
        [PSCustomObject]@{ PSTypeName=$Script:TypeDidMethodDereg; SocietyDid=$SocietyDid; MethodName=$MethodName; Status='Dormant'; Success=$true }
    }
}

function Get-Svrn7DidMethodStatus {
<#
.SYNOPSIS
    Returns the status of a DID method name (Active, Dormant, or Available).
.PARAMETER MethodName
    The DID method name to query. Accepts pipeline input.
.EXAMPLE
    Get-Svrn7DidMethodStatus -MethodName 'sovronia'
.OUTPUTS
    [Svrn7.Core.Models.DidMethodStatus] enum value.
.NOTES
    ISvrn7Driver method: GetDidMethodStatusAsync(string)
#>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline)] [string] $MethodName)
    process { Assert-FederationDriver; $Script:FederationDriver.GetDidMethodStatusAsync($MethodName).GetAwaiter().GetResult() }
}

function Get-Svrn7DidMethods {
<#
.SYNOPSIS
    Lists DID method name records in the Federation registry.
.PARAMETER SocietyDid
    Optional filter by owning Society DID.
.PARAMETER Status
    Optional filter by status ('Active' or 'Dormant').
.EXAMPLE
    Get-Svrn7DidMethods
.EXAMPLE
    Get-Svrn7DidMethods -SocietyDid 'did:sovronia:abc...' -Status Active
.OUTPUTS
    [IReadOnlyList[Svrn7.Core.Models.SocietyDidMethodRecord]]
.NOTES
    ISvrn7Driver method: GetAllDidMethodsAsync(string?, DidMethodStatus?)
#>
    [CmdletBinding()]
    param(
        [string] $SocietyDid = '',
        [ValidateSet('Active','Dormant')] [string] $Status = ''
    )
    Assert-FederationDriver
    $s = if ($SocietyDid) { $SocietyDid } else { $null }
    $v = if ($Status) { [Svrn7.Core.Models.DidMethodStatus]$Status } else { $null }
    $Script:FederationDriver.GetAllDidMethodsAsync($s, $v).GetAwaiter().GetResult()
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
        -AmountGrana 500_000_000 -Memo 'Monthly dues'
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
    $grana = if ($PSCmdlet.ParameterSetName -eq 'BySvrn7') { [long][Math]::Round($AmountSvrn7 * 1_000_000) } else { $AmountGrana }
    $svrn7 = [decimal]$grana / 1_000_000M
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
        @{ PayerDid=$d1; PayerKeyPair=$kp; PayeeDid=$d2; AmountGrana=100_000_000L },
        @{ PayerDid=$d1; PayerKeyPair=$kp; PayeeDid=$d3; AmountGrana=50_000_000L  }
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
        -NewTotalSupplyGrana  2_000_000_000_000_000L `
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
    'Get-Svrn7DidMethodStatus'
    'Get-Svrn7DidMethods'
    'Get-Svrn7Federation'
    'Get-Svrn7MerkleLogSize'
    'Get-Svrn7MerkleRoot'
    'Get-Svrn7MerkleTreeHead'
    'Get-Svrn7Society'
    'Get-Svrn7VcById'
    'Get-Svrn7VcsBySubject'
    'Initialize-Svrn7Federation'
    'Invoke-Svrn7BatchTransfer'
    'Invoke-Svrn7GdprErasure'
    'Invoke-Svrn7SignMerkleTreeHead'
    'Invoke-Svrn7SignSecp256k1'
    'Invoke-Svrn7Transfer'
    'New-Svrn7Did'
    'New-Svrn7Ed25519KeyPair'
    'New-Svrn7KeyPair'
    'Register-Svrn7Citizen'
    'Register-Svrn7DidMethod'
    'Register-Svrn7Society'
    'Resolve-Svrn7CitizenPrimaryDid'
    'Resolve-Svrn7Did'
    'Revoke-Svrn7Vc'
    'Test-Svrn7CitizenActive'
    'Test-Svrn7DidActive'
    'Test-Svrn7SignatureSecp256k1'
    'Test-Svrn7SocietyActive'
    'Unregister-Svrn7DidMethod'
    'Update-Svrn7FederationSupply'
)
