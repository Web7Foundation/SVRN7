# SVRN7 — DID & DIDDocument Debug Guide

Covers all DID and DIDDocument operations: key generation, registration, document
inspection, method management, secondary DIDs, resolution, and DIDComm-based resolution.

---

## Prerequisites

### PowerShell 7

All commands require **PowerShell 7 (`pwsh.exe`)**. Verify before running anything:

```powershell
$PSVersionTable.PSVersion   # Major must be 7
```

### Working directory

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
```

### Module imports

```powershell
Import-Module .\lobes\Svrn7.Federation\Svrn7.Federation.psm1
Import-Module .\lobes\Svrn7.Society\Svrn7.Society.psm1
```

### TDA requirement

`New-Svrn7KeyPair` works from the PS CLI with no TDA. All other DID cmdlets require the
Federation driver, which can only be initialised inside the `dotnet` host. The workflow is:

1. Generate key pairs from the PS CLI (Scenario D1)
2. Start the TDA: `dotnet .\Svrn7.TDA.dll`
3. Register via the TDA runspace (Scenario D2)

---

## Scenario D1 — Key generation (PS CLI, no TDA required)

### D1.1 — Generate a key pair

```powershell
$kp = New-Svrn7KeyPair
$kp
```

Expected output:

```
Algorithm    : Secp256k1
PublicKeyHex : 02a1b2c3d4...
PrivateKeyHex: 8f7e6d5c4b...
```

The `$kp` object is used as input to `New-Svrn7Did` and all Register cmdlets inside
the TDA runspace.

---

## Scenario D2 — Registering Federation, Society, or Citizen (TDA running)

Registration is triggered by sending DIDComm messages to the running TDA. Key pairs are
generated from the PS CLI (D1.1); the public key hex is included in the message body.
The TDA calls `New-Svrn7Did` internally and persists the `DidDocument` to `svrn7-dids.db`.

Include `serviceEndpointUrl` in the message body to embed the TDA's DIDComm endpoint
in the stored `DidDocument` so other TDAs can discover it via resolution.

### D2.0 — Register the Federation (one-time, idempotent)

```powershell
$kp = New-Svrn7KeyPair   # D1.1 — from PS CLI

$body = @{
    federationDid        = 'did:drn:foundation.svrn7.net'
    federationName       = 'Web 7.0 Foundation'
    publicKeyHex         = $kp.PublicKeyHex
    primaryDidMethodName = 'drn'
    serviceEndpointUrl   = 'https://foundation.svrn7.net:8443/didcomm'
} | ConvertTo-Json -Compress

$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/federation/1.0/initialize-federation'
    from = 'did:drn:foundation.svrn7.net'
    to   = @('did:drn:bindloss.svrn7.net')
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log:

```
[Info]  Switchboard: routing ... (type=did:drn:svrn7.net/protocols/federation/1.0/init)
        → Invoke-Web7FederationInit [Svrn7.Federation]
[Info]  Federation initialised: did:drn:foundation.svrn7.net (Web 7.0 Foundation)
```

### D2.1 — Register a Society

```powershell
$kp = New-Svrn7KeyPair   # D1.1 — from PS CLI

$body = @{
    societyDid           = 'did:drn:bindloss.svrn7.net'
    publicKeyHex         = $kp.PublicKeyHex
    societyName          = 'Bindloss Alberta'
    primaryDidMethodName = 'bindloss'
    serviceEndpointUrl   = 'https://bindloss.svrn7.net:8443/didcomm'
    drawAmountGrana      = 1000000000000
    overdraftCeilingGrana= 10000000000000
} | ConvertTo-Json -Compress

$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/federation/1.0/register-society'
    from = 'did:drn:foundation.svrn7.net'
    to   = @('did:drn:bindloss.svrn7.net')
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log:

```
[Info]  Society registered: did:drn:bindloss.svrn7.net (Bindloss Alberta) method=bindloss
```

### D2.2 — Register a Citizen

```powershell
$kp         = New-Svrn7KeyPair   # D1.1 — from PS CLI
$citizenDid = 'did:bindloss:mwherman001'   # client-assigned DID string

$body = @{
    citizenDid         = $citizenDid
    publicKeyHex       = $kp.PublicKeyHex
    displayName        = 'mwherman'
    serviceEndpointUrl = 'https://mwherman.svrn7.net:8443/didcomm'
} | ConvertTo-Json -Compress

$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/onboard/1.0/register-citizen'
    from = $citizenDid
    to   = @('did:drn:bindloss.svrn7.net')
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log:

```
[Info]  Switchboard: routing ... (type=did:drn:svrn7.net/protocols/onboard/1.0/register-citizen)
        → ConvertFrom-Web7OnboardRequest [Svrn7.Onboarding]
[Info]  Citizen registered: did:bindloss:mwherman001
```

---

## Scenario D3 — Inspect all W3C DIDDocument fields

After registration the `DidDocument` stored in `svrn7-dids.db` contains all W3C DID
Core 1.0 properties as first-class typed fields.

### D3.1 — Verification methods

```powershell
$didDoc.VerificationMethod | Format-List
```

Expected:

```
Id                : did:drn:3J98...#key-1
Type              : EcdsaSecp256k1VerificationKey2019
Controller        : did:drn:3J98...
PublicKeyHex      : 02a1b2c3d4...
PublicKeyMultibase:
```

### D3.2 — Verification relationships

```powershell
$didDoc.Authentication       # ['did:drn:3J98...#key-1']
$didDoc.AssertionMethod      # ['did:drn:3J98...#key-1']
$didDoc.KeyAgreement         # [] — empty at creation
$didDoc.CapabilityInvocation # [] — empty at creation
$didDoc.CapabilityDelegation # [] — empty at creation
```

`Authentication` and `AssertionMethod` are both populated with `#key-1` at construction.

### D3.3 — Service endpoints

```powershell
$didDoc.ServiceEndpoints | Format-List
```

Expected (when `-ServiceEndpointUrl` was passed to `New-Svrn7Did`):

```
Id              : did:drn:3J98...#didcomm-1
Type            : DIDCommMessaging
ServiceEndpoint : https://foundation.svrn7.net:8443/didcomm
```

### D3.4 — Controller and AlsoKnownAs

```powershell
$didDoc.Controller    # 'did:drn:3J98...' — same as Did at construction
$didDoc.AlsoKnownAs   # [] — empty at construction
```

### D3.5 — Proof (Data Integrity)

```powershell
$didDoc.Proof   # $null at construction — populated after signing
```

### D3.6 — Canonical W3C JSON

```powershell
$didDoc.DocumentJson | ConvertFrom-Json | ConvertTo-Json -Depth 5
```

Expected (with service endpoint):

```json
{
  "context": ["https://www.w3.org/ns/did/v1"],
  "id": "did:drn:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy",
  "verificationMethod": [
    {
      "id": "did:drn:3J98...#key-1",
      "type": "EcdsaSecp256k1VerificationKey2019",
      "controller": "did:drn:3J98...",
      "publicKeyHex": "02a1b2c3..."
    }
  ],
  "authentication": ["did:drn:3J98...#key-1"],
  "assertionMethod": ["did:drn:3J98...#key-1"],
  "service": [
    {
      "id": "did:drn:3J98...#didcomm-1",
      "type": "DIDCommMessaging",
      "serviceEndpoint": "https://foundation.svrn7.net:8443/didcomm"
    }
  ]
}
```

### D3.7 — Registry metadata

```powershell
$didDoc.Id            # LiteDB document PK (Guid, not the DID)
$didDoc.Version       # 1
$didDoc.Status        # Active
$didDoc.CreatedAt
$didDoc.UpdatedAt
$didDoc.DeactivatedAt # $null while Active
```

---

## Scenario D4 — DID resolution (TDA running)

### D4.1 — Resolve a DID to its full DIDDocument

```powershell
$result = Resolve-Svrn7Did -Did $didDoc.Did
$result.Found        # $true
$result.Document     # DidDocument object
$result.ErrorCode    # $null on success
$result.ResolvedAt
```

Inspect the resolved document:

```powershell
$result.Document | Format-List Did, MethodName, Status, Version
$result.Document.VerificationMethod | Format-List
$result.Document.ServiceEndpoints | Format-List
```

### D4.2 — Resolve a DID that does not exist

```powershell
$result = Resolve-Svrn7Did -Did 'did:drn:doesnotexist'
$result.Found      # $false
$result.Document   # $null
$result.ErrorCode  # 'notFound'
```

### D4.3 — Test whether a DID is Active

```powershell
Test-Svrn7DidActive -Did $didDoc.Did   # $true
```

### D4.4 — Resolve a citizen's primary DID from any DID

```powershell
# Given a secondary DID did:sovroniamed:3J98... returns did:sovronia:3J98...
Resolve-Svrn7CitizenPrimaryDid -Did 'did:sovroniamed:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy'
```

Returns `$null` if the DID is not registered.

---

## Scenario D5 — DID method management (Federation LOBE)

### D5.1 — Check the status of a method name

```powershell
Get-Svrn7DidMethodStatus -MethodName 'sovronia'
# Active | Dormant | (not returned = Available)
```

### D5.2 — List all registered method names

```powershell
Get-Svrn7DidMethods | Format-Table MethodName, Status, SocietyDid -AutoSize
```

Filter by Society:

```powershell
Get-Svrn7DidMethods -SocietyDid 'did:drn:bindloss.svrn7.net' -Status Active
```

### D5.3 — Register an additional method name

```powershell
Register-Svrn7DidMethod -SocietyDid 'did:drn:bindloss.svrn7.net' -MethodName 'bindlossedu'
```

Expected output:

```
SocietyDid : did:drn:bindloss.svrn7.net
MethodName : bindlossedu
Status     : Active
Success    : True
```

Pipeline form:

```powershell
'bindlossedu','bindlosshealth' | Register-Svrn7DidMethod -SocietyDid 'did:drn:bindloss.svrn7.net'
```

### D5.4 — Deregister a method name

The primary method name cannot be deregistered. The name enters a 30-day dormancy period.

```powershell
Unregister-Svrn7DidMethod -SocietyDid 'did:drn:bindloss.svrn7.net' -MethodName 'bindlossedu'
Get-Svrn7DidMethodStatus -MethodName 'bindlossedu'   # Dormant
```

---

## Scenario D6 — DID method management (Society LOBE)

`-SocietyDid` is inferred from the loaded Society driver — no parameter needed.

### D6.1 — Register an additional method name

```powershell
Initialize-Svrn7SocietyDidMethod -MethodName 'sovroniamed'
'sovroniaedu','sovroniahealth' | Initialize-Svrn7SocietyDidMethod
```

### D6.2 — List method names for this Society

```powershell
Get-Svrn7SocietyDidMethods | Format-Table MethodName, Status, IsPrimary -AutoSize
```

### D6.3 — Deregister a method name

```powershell
Unregister-Svrn7SocietyDidMethod -MethodName 'sovroniaedu'
```

---

## Scenario D7 — Secondary DIDs for citizens (Society LOBE)

A citizen's secondary DID uses the same base58-encoded public key as the primary, but
under a different method name. Both DIDs resolve to the same `CitizenRecord`.

### D7.1 — Issue a secondary DID

```powershell
$citizenPrimaryDid = 'did:sovronia:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy'
Add-Svrn7CitizenDid -CitizenPrimaryDid $citizenPrimaryDid -MethodName 'sovroniamed'
```

Expected output:

```
CitizenPrimaryDid : did:sovronia:3J98...
SecondaryDid      : did:sovroniamed:3J98...
MethodName        : sovroniamed
Success           : True
```

### D7.2 — List all DIDs for a citizen

```powershell
Get-Svrn7CitizenDids -PrimaryDid $citizenPrimaryDid | Format-Table Did, IsPrimary, MethodName
```

### D7.3 — Resolve a secondary DID back to primary

```powershell
Resolve-Svrn7CitizenPrimaryDid -Did 'did:sovroniamed:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy'
# did:sovronia:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
```

---

## Scenario D8 — DIDComm-based DID resolution (Identity LOBE, TDA running)

| Direction | Protocol URI |
|-----------|-------------|
| Request  | `did:drn:svrn7.net/protocols/did/1.0/resolve-request` |
| Response | `did:drn:svrn7.net/protocols/did/1.0/resolve-response` |

### D8.1 — Send a DID resolve request

```powershell
$requesterDid = 'did:drn:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy'
$targetDid    = 'did:sovronia:abc123...'

$body = @{ did = $targetDid; from = $requesterDid } | ConvertTo-Json -Compress

$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/did/1.0/resolve-request'
    from = $requesterDid
    to   = @('did:drn:bindloss.svrn7.net')
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log:

```
[Info]  Switchboard: routing ... (type=did:drn:svrn7.net/protocols/did/1.0/resolve-request)
        → Resolve-Svrn7Did [Svrn7.Identity]
[Info]  Invoke-Svrn7DidResolveResponse: requestedDid='did:sovronia:abc123...' found=True
```

### D8.2 — Response payload

```json
{
  "from":         "did:drn:bindloss.svrn7.net",
  "to":           "did:drn:3J98...",
  "requestedDid": "did:sovronia:abc123...",
  "found":        true,
  "didDocument":  { ... },
  "resolvedAt":   "2026-06-05T..."
}
```

### D8.3 — Resolve a DID that is not found

```json
{ "found": false, "didDocument": null, "requestedDid": "did:drn:doesnotexist" }
```

---

## DIDDocument persistence reference

```
Initialize-Svrn7Federation / Initialize-Svrn7Society / Register-Svrn7Citizen
  → ISvrn7Driver.InitialiseFederationAsync / RegisterSocietyAsync / RegisterCitizenAsync
    → LiteDidDocumentRegistry.CreateAsync
      → svrn7-dids.db  (Documents + History collections)
```

| Cmdlet | Driver method | Persists DidDocument |
|--------|--------------|:---:|
| `Initialize-Svrn7Federation` | `InitialiseFederationAsync(DidDocument, name)` | Yes |
| `Initialize-Svrn7Society` | `RegisterSocietyAsync(RegisterSocietyRequest)` | Yes |
| `Register-Svrn7Citizen` | `RegisterCitizenAsync(RegisterCitizenRequest)` | Yes |
| `Register-Svrn7Citizen` | `RegisterCitizenInSocietyAsync` → `RegisterCitizenAsync` | Yes |

---

## Quick-reference: all DID cmdlets

| Cmdlet | Key parameters | LOBE | Requires TDA |
|--------|---------------|------|:---:|
| `New-Svrn7KeyPair` | — | Federation | No |
| `New-Svrn7Did` | `-KeyPair` `-MethodName` `-ServiceEndpointUrl` | Federation | Yes — TDA runspace only |
| `Initialize-Svrn7Federation` | `-DidDocument` `-KeyPair` `-Name` | Federation | Yes |
| `Initialize-Svrn7Citizen` | `-DidDocument` `-KeyPair` | Federation | Yes |
| `Register-Svrn7Citizen` | `-DidDocument` `-KeyPair` `[-PreferredMethodName]` | Society | Yes |
| `Initialize-Svrn7Society` | `-DidDocument` `-KeyPair` `-Name` | Federation | Yes |
| `Resolve-Svrn7Did` | `-Did` | Federation | Yes |
| `Test-Svrn7DidActive` | `-Did` | Federation | Yes |
| `Resolve-Svrn7CitizenPrimaryDid` | `-Did` | Federation | Yes |
| `Get-Svrn7CitizenDids` | `-PrimaryDid` | Federation | Yes |
| `Register-Svrn7DidMethod` | `-SocietyDid` `-MethodName` | Federation | Yes |
| `Unregister-Svrn7DidMethod` | `-SocietyDid` `-MethodName` | Federation | Yes |
| `Get-Svrn7DidMethodStatus` | `-MethodName` | Federation | Yes |
| `Get-Svrn7DidMethods` | `[-SocietyDid]` `[-Status]` | Federation | Yes |
| `Register-Svrn7Citizen` | `-DidDocument` `-KeyPair` `[-PreferredMethodName]` | Society | Yes |
| `Initialize-Svrn7SocietyDidMethod` | `-MethodName` | Society | Yes |
| `Unregister-Svrn7SocietyDidMethod` | `-MethodName` | Society | Yes |
| `Get-Svrn7SocietyDidMethods` | — | Society | Yes |
| `Add-Svrn7CitizenDid` | `-CitizenPrimaryDid` `-MethodName` | Society | Yes |
| `Resolve-Svrn7Did` (DIDComm handler) | `-MessageDid` | Identity | Yes |
| `Invoke-Svrn7DidResolveResponse` | `-MessageDid` | Identity | Yes |

---

## DIDDocument field reference

| Field | W3C DID Core | Type | Notes |
|-------|-------------|------|-------|
| `Did` | `id` | `string` | Required |
| `Controller` | `controller` | `string?` | Defaults to `Did` at construction |
| `AlsoKnownAs` | `alsoKnownAs` | `List<string>` | Empty at construction |
| `VerificationMethod` | `verificationMethod` | `List<DidVerificationMethod>` | `#key-1` added at construction |
| `Authentication` | `authentication` | `List<string>` | `[#key-1]` at construction |
| `AssertionMethod` | `assertionMethod` | `List<string>` | `[#key-1]` at construction |
| `KeyAgreement` | `keyAgreement` | `List<string>` | Empty at construction |
| `CapabilityInvocation` | `capabilityInvocation` | `List<string>` | Empty at construction |
| `CapabilityDelegation` | `capabilityDelegation` | `List<string>` | Empty at construction |
| `ServiceEndpoints` | `service` | `List<DidServiceEndpoint>` | Populated when `-ServiceEndpointUrl` is passed to `New-Svrn7Did` |
| `Proof` | Data Integrity | `DidProof?` | `$null` until signed |
| `DocumentJson` | — | `string` | Canonical W3C JSON; registry-stored |
| `MethodName` | — | `string` | SVRN7 registry key |
| `Version` | — | `int` | Monotonically increasing |
| `Status` | — | `DidStatus` | `Active` \| `Suspended` \| `Deactivated` |
| `CreatedAt` | — | `DateTimeOffset` | Registry metadata |
| `UpdatedAt` | — | `DateTimeOffset` | Registry metadata |
| `DeactivatedAt` | — | `DateTimeOffset?` | Set on deactivation |
