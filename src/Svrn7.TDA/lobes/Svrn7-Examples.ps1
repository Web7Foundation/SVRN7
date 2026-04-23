#Requires -Version 7.2
<#
.SYNOPSIS
    Svrn7 PowerShell Modules — Complete Usage Examples
    Covers all cmdlets in Svrn7.Federation and Svrn7.Society.

.DESCRIPTION
    Run after placing compiled Svrn7 assemblies in ./lobes/bin/ and importing both modules:

        Import-Module ./lobes/Svrn7.Federation.psd1
        Import-Module ./lobes/Svrn7.Society.psd1

    Examples track the Quick-Start sections of the SVRN7 Architecture Whitepaper
    (§15.1 Federation: Register and Transfer; §15.2 Society: Register Citizen and DID Method).

MODULE SPLIT
    Svrn7.Federation — ISvrn7Driver cmdlets (35 functions)
        Initialize-Svrn7Federation       New-Svrn7KeyPair              New-Svrn7Ed25519KeyPair
        Invoke-Svrn7SignSecp256k1         Test-Svrn7SignatureSecp256k1  New-Svrn7Did
        Resolve-Svrn7CitizenPrimaryDid   Register-Svrn7Citizen         Get-Svrn7Citizen
        Test-Svrn7CitizenActive          Get-Svrn7CitizenDids          Register-Svrn7Society
        Get-Svrn7Society                 Test-Svrn7SocietyActive       Disable-Svrn7Society
        Register-Svrn7DidMethod          Unregister-Svrn7DidMethod     Get-Svrn7DidMethodStatus
        Get-Svrn7DidMethods              Get-Svrn7Balance              Invoke-Svrn7Transfer
        Invoke-Svrn7BatchTransfer        Get-Svrn7Federation           Update-Svrn7FederationSupply
        Get-Svrn7CurrentEpoch            Resolve-Svrn7Did              Test-Svrn7DidActive
        Get-Svrn7VcsBySubject            Get-Svrn7VcById               Revoke-Svrn7Vc
        Get-Svrn7MerkleRoot              Get-Svrn7MerkleLogSize        Get-Svrn7MerkleTreeHead
        Invoke-Svrn7SignMerkleTreeHead   Invoke-Svrn7GdprErasure

    Svrn7.Society — ISvrn7SocietyDriver cmdlets (15 functions)
        Connect-Svrn7Society             Get-Svrn7OwnSociety           Register-Svrn7CitizenInSociety
        Add-Svrn7CitizenDid             Invoke-Svrn7IncomingTransfer  Invoke-Svrn7ExternalTransfer
        Invoke-Svrn7FederationTransfer  Get-Svrn7OverdraftStatus       Get-Svrn7OverdraftRecord
        Get-Svrn7SocietyMembers         Test-Svrn7SocietyMember       Register-Svrn7SocietyDidMethod
        Unregister-Svrn7SocietyDidMethod Get-Svrn7SocietyDidMethods   Find-Svrn7VcsBySubject
#>

Set-StrictMode -Version Latest
$moduleRoot = Split-Path $PSCommandPath -Parent
Import-Module (Join-Path $moduleRoot 'Svrn7.Federation.psd1') -Force -Verbose:$false
Import-Module (Join-Path $moduleRoot 'Svrn7.Society.psd1')    -Force -Verbose:$false

function Write-Header([string]$text) {
    Write-Host "`n$('─' * 60)" -ForegroundColor Cyan
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host "$('─' * 60)" -ForegroundColor Cyan
}

###############################################################################
# FEDERATION MODULE EXAMPLES
###############################################################################
Write-Header 'SVRN7.FEDERATION — ISvrn7Driver examples'

# ── Initialise ─────────────────────────────────────────────────────────────
Write-Host "`n[1] Initialize-Svrn7Federation" -ForegroundColor Yellow
Initialize-Svrn7Federation -Verbose

# ── Key generation ─────────────────────────────────────────────────────────
Write-Host "`n[2] New-Svrn7KeyPair" -ForegroundColor Yellow
$federationKp = New-Svrn7KeyPair
Write-Host "PublicKeyHex : $($federationKp.PublicKeyHex)"
Write-Host "Algorithm    : $($federationKp.Algorithm)"

Write-Host "`n[3] New-Svrn7Ed25519KeyPair (for DIDComm)" -ForegroundColor Yellow
$societyEdKp = New-Svrn7Ed25519KeyPair
Write-Host "Ed25519 PublicKeyHex: $($societyEdKp.PublicKeyHex)"

# ── DID construction ───────────────────────────────────────────────────────
Write-Host "`n[4] New-Svrn7Did" -ForegroundColor Yellow
$federationKp2 = New-Svrn7KeyPair
$societyKp     = New-Svrn7KeyPair
$citizenKp     = New-Svrn7KeyPair

$societyDid  = (New-Svrn7Did -KeyPair $societyKp  -MethodName 'sovronia').Did
$citizenDid  = (New-Svrn7Did -KeyPair $citizenKp  -MethodName 'sovronia').Did
$federationDid = (New-Svrn7Did -KeyPair $federationKp2 -MethodName 'drn').Did

Write-Host "FederationDid : $federationDid"
Write-Host "SocietyDid    : $societyDid"
Write-Host "CitizenDid    : $citizenDid"

# ── Society registration (Whitepaper §15.1) ────────────────────────────────
Write-Host "`n[5] Register-Svrn7Society  (Whitepaper §15.1)" -ForegroundColor Yellow
$soc = Register-Svrn7Society `
    -Did        $societyDid `
    -KeyPair    $societyKp `
    -Name       'Sovronia Digital Nation' `
    -MethodName 'sovronia'
Write-Host "Registered: $($soc.SocietyName) [$($soc.MethodName)]"

# ── Citizen registration at Federation level ───────────────────────────────
Write-Host "`n[6] Register-Svrn7Citizen  (Federation level — no endowment)" -ForegroundColor Yellow
Register-Svrn7Citizen -Did $citizenDid -KeyPair $citizenKp | Out-Null
Write-Host "Citizen registered: $citizenDid"

# ── Balance query (Whitepaper §15.1) ──────────────────────────────────────
Write-Host "`n[7] Get-Svrn7Balance  (Whitepaper §15.1)" -ForegroundColor Yellow
$bal = Get-Svrn7Balance -Did $citizenDid
Write-Host "Citizen balance : $($bal.Display)"

# Pipeline — multiple DIDs as a table
Write-Host "`nBalances via pipeline:"
@($citizenDid, $societyDid) | Get-Svrn7Balance | Format-Table Did, Display -AutoSize

# ── Signature verification ─────────────────────────────────────────────────
Write-Host "`n[8] Invoke-Svrn7SignSecp256k1 / Test-Svrn7SignatureSecp256k1" -ForegroundColor Yellow
$payload = [System.Text.Encoding]::UTF8.GetBytes('hello-svrn7')
$sig     = Invoke-Svrn7SignSecp256k1 -Payload $payload -PrivateKeyBytes $citizenKp.PrivateKeyBytes
$valid   = Test-Svrn7SignatureSecp256k1 -Payload $payload -Signature $sig -PublicKeyHex $citizenKp.PublicKeyHex
Write-Host "Signature valid: $valid"

# ── Transfer (Whitepaper §15.1) ────────────────────────────────────────────
Write-Host "`n[9] Invoke-Svrn7Transfer  (Whitepaper §15.1)" -ForegroundColor Yellow
$tx = Invoke-Svrn7Transfer `
    -PayerDid     $citizenDid `
    -PayerKeyPair $citizenKp `
    -PayeeDid     $societyDid `
    -AmountSvrn7  100 `
    -Memo         'Monthly society dues'
Write-Host "TransferId   : $($tx.TransferId.Substring(0,16))..."
Write-Host "Amount       : $($tx.AmountSvrn7) SVRN7"
Write-Host "Memo         : $($tx.Memo)"

# ── DID method registration (Federation-level) ────────────────────────────
Write-Host "`n[10] Register-Svrn7DidMethod  (Federation-level)" -ForegroundColor Yellow
$dm = Register-Svrn7DidMethod -SocietyDid $societyDid -MethodName 'sovroniamed'
Write-Host "Method '$($dm.MethodName)' status: $($dm.Status)"

# ── DID method status ─────────────────────────────────────────────────────
Write-Host "`n[11] Get-Svrn7DidMethodStatus" -ForegroundColor Yellow
Write-Host "sovronia    : $(Get-Svrn7DidMethodStatus -MethodName 'sovronia')"
Write-Host "sovroniamed : $(Get-Svrn7DidMethodStatus -MethodName 'sovroniamed')"
Write-Host "notregistered: $(Get-Svrn7DidMethodStatus -MethodName 'notregistered')"

# ── Current epoch ─────────────────────────────────────────────────────────
Write-Host "`n[12] Get-Svrn7CurrentEpoch" -ForegroundColor Yellow
$epoch = Get-Svrn7CurrentEpoch
Write-Host "Current Epoch: $epoch  (0=Endowment, 1=EcosystemUtility, 2=Market)"

# ── DID resolution ────────────────────────────────────────────────────────
Write-Host "`n[13] Resolve-Svrn7Did / Test-Svrn7DidActive" -ForegroundColor Yellow
$didRes = Resolve-Svrn7Did -Did $citizenDid
Write-Host "DID Document id  : $($didRes.Document?.Id)"
Write-Host "DID Active       : $(Test-Svrn7DidActive -Did $citizenDid)"

# ── VC query ──────────────────────────────────────────────────────────────
Write-Host "`n[14] Get-Svrn7VcsBySubject" -ForegroundColor Yellow
$vcs = Get-Svrn7VcsBySubject -SubjectDid $citizenDid
Write-Host "VCs for citizen: $($vcs.Count)"

# ── Merkle log ────────────────────────────────────────────────────────────
Write-Host "`n[15] Merkle log cmdlets" -ForegroundColor Yellow
Write-Host "Log size  : $(Get-Svrn7MerkleLogSize)"
Write-Host "Root hash : $((Get-Svrn7MerkleRoot).Substring(0,16))..."
$head = Invoke-Svrn7SignMerkleTreeHead
Write-Host "STH signed. TreeSize: $($head.TreeSize)  Timestamp: $($head.Timestamp)"

# ── Federation record ─────────────────────────────────────────────────────
Write-Host "`n[16] Get-Svrn7Federation" -ForegroundColor Yellow
$fed = Get-Svrn7Federation
if ($fed) {
    Write-Host "TotalSupplyGrana : $($fed.TotalSupplyGrana)"
} else { Write-Host "(No federation record — genesis not yet written)" }

###############################################################################
# SOCIETY MODULE EXAMPLES
###############################################################################
Write-Header 'SVRN7.SOCIETY — ISvrn7SocietyDriver examples'

# ── Connect Society driver ─────────────────────────────────────────────────
Write-Host "`n[17] Connect-Svrn7Society" -ForegroundColor Yellow
Connect-Svrn7Society `
    -SocietyDid                     $societyDid `
    -FederationDid                  $federationDid `
    -MessagingKeyPair               $societyEdKp `
    -FederationMessagingPublicKeyHex $societyEdKp.PublicKeyHex `
    -DidMethodNames                 @('sovronia','sovroniamed')
Write-Host "Connected to: $societyDid"

# ── Own society record ────────────────────────────────────────────────────
Write-Host "`n[18] Get-Svrn7OwnSociety" -ForegroundColor Yellow
$ownSoc = Get-Svrn7OwnSociety
Write-Host "Society name: $($ownSoc?.SocietyName ?? '(pending — not yet written to Society db)')"

# ── Register citizen in Society with endowment (Whitepaper §15.2) ─────────
Write-Host "`n[19] Register-Svrn7CitizenInSociety  (Whitepaper §15.2)" -ForegroundColor Yellow
$citizen2Kp  = New-Svrn7KeyPair
$citizen2Did = (New-Svrn7Did -KeyPair $citizen2Kp -MethodName 'sovronia').Did
$c2reg = Register-Svrn7CitizenInSociety -CitizenDid $citizen2Did -KeyPair $citizen2Kp
Write-Host "Citizen2 registered: $($c2reg.CitizenDid)"
Write-Host "Endowment           : $($c2reg.EndowmentSvrn7) SVRN7"

# ── Overdraft status ──────────────────────────────────────────────────────
Write-Host "`n[20] Get-Svrn7OverdraftStatus / Get-Svrn7OverdraftRecord" -ForegroundColor Yellow
Write-Host "Overdraft status: $(Get-Svrn7OverdraftStatus)"
$rec = Get-Svrn7OverdraftRecord
if ($rec) {
    Write-Host "Lifetime draws  : $($rec.LifetimeDrawsGrana) grana"
}

# ── Society membership ────────────────────────────────────────────────────
Write-Host "`n[21] Get-Svrn7SocietyMembers / Test-Svrn7SocietyMember" -ForegroundColor Yellow
$members = Get-Svrn7SocietyMembers
Write-Host "Member count       : $($members.Count)"
Write-Host "Citizen2 is member : $(Test-Svrn7SocietyMember -Did $citizen2Did)"
Write-Host "Random DID member  : $(Test-Svrn7SocietyMember -Did 'did:sovronia:nonexistent')"

# ── Self-service DID method (Whitepaper §15.2) ────────────────────────────
Write-Host "`n[22] Register-Svrn7SocietyDidMethod  (Whitepaper §15.2)" -ForegroundColor Yellow
$newMethod = Register-Svrn7SocietyDidMethod -MethodName 'sovroniahealth'
Write-Host "Registered: $($newMethod.MethodName)  Status: $($newMethod.Status)"

# ── List all Society DID methods ──────────────────────────────────────────
Write-Host "`n[23] Get-Svrn7SocietyDidMethods" -ForegroundColor Yellow
Get-Svrn7SocietyDidMethods | Format-Table MethodName, Status, IsPrimary -AutoSize

# ── Add secondary DID to citizen (Whitepaper §15.2) ──────────────────────
Write-Host "`n[24] Add-Svrn7CitizenDid  (Whitepaper §15.2)" -ForegroundColor Yellow
$secondDid = Add-Svrn7CitizenDid -CitizenPrimaryDid $citizen2Did -MethodName 'sovroniahealth'
Write-Host "Primary DID    : $($secondDid.CitizenPrimaryDid)"
Write-Host "Secondary DID  : $($secondDid.SecondaryDid)"

# ── Verify primary DID resolution ─────────────────────────────────────────
Write-Host "`n[25] Resolve-Svrn7CitizenPrimaryDid (from secondary)" -ForegroundColor Yellow
$resolved = Resolve-Svrn7CitizenPrimaryDid -Did $secondDid.SecondaryDid
Write-Host "Resolved to primary: $resolved"

# ── Cross-Society VC resolution ───────────────────────────────────────────
Write-Host "`n[26] Find-Svrn7VcsBySubject (cross-Society fan-out)" -ForegroundColor Yellow
$vcResult = Find-Svrn7VcsBySubject -SubjectDid $citizen2Did -TimeoutSeconds 5
Write-Host "Records found       : $($vcResult.Records.Count)"
Write-Host "Responded Societies : $($vcResult.RespondedSocieties.Count)"
Write-Host "Timed out Societies : $($vcResult.TimedOutSocieties.Count)"
Write-Host "Result complete     : $($vcResult.IsComplete)"

# ── Deregister a DID method ───────────────────────────────────────────────
Write-Host "`n[27] Unregister-Svrn7SocietyDidMethod" -ForegroundColor Yellow
Unregister-Svrn7SocietyDidMethod -MethodName 'sovroniahealth' | Out-Null
Write-Host "Method 'sovroniahealth' deregistered (now Dormant for 30 days)"

###############################################################################
# SUMMARY
###############################################################################
Write-Header 'ALL EXAMPLES COMPLETE'
Write-Host "`nFinal balances:" -ForegroundColor White
@($citizenDid, $citizen2Did, $societyDid) |
    Get-Svrn7Balance |
    Format-Table Did, Svrn7, Display -AutoSize
