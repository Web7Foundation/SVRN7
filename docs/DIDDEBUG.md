# SVRN7 — DID & DIDDocument Debug Guide

Covers all DID and DIDDocument operations: key generation, DID creation, document inspection,
method registration, secondary DIDs, resolution, and DIDComm-based resolution via the
Identity LOBE.

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

The scenarios below split across two modules. Import only what the scenario requires:

```powershell
# Federation-level DID operations (no TDA required for key gen and DID construction)
Import-Module .\lobes\Svrn7.Federation\Svrn7.Federation.psm1

# Society-level DID operations (requires TDA running)
Import-Module .\lobes\Svrn7.Society\Svrn7.Society.psm1
```

---

## Scenario D1 — Key generation and DID construction

`New-Svrn7KeyPair` requires only the Svrn7 assemblies — no driver or database needed.
`New-Svrn7Did` requires the Federation driver to be initialised (`Initialize-Svrn7Federation`)
because it calls `Base58EncodeAsync` and `CreateDidDocument` on the driver.

### D1.1 — Generate a key pair (no driver required)

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

### D1.2 — Create a DID (TDA must be running)

`New-Svrn7Did` requires the Federation driver, which is only fully operational inside
the TDA's hosted runspace. `Initialize-Svrn7Federation` cannot be called standalone
from the PS CLI because the DI container depends on ASP.NET Core assemblies that are
only available via the `dotnet` host, not in a plain PowerShell session.

The correct workflow: start the TDA, then interact via DIDComm messages (see Scenario D3b)
or use the TDA's running runspace context directly.

```powershell
# TDA must be started first in a separate terminal:
#   dotnet .\Svrn7.TDA.dll
#
# Then send a DIDComm message to trigger DID creation server-side,
# or access the running TDA's PowerShell runspace directly.
$didDoc = New-Svrn7Did -KeyPair $kp   # only valid inside a TDA runspace
$didDoc
```

Expected output (selected fields):

```
Did              : did:drn:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
MethodName       : drn
Controller       : did:drn:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
Version          : 1
Status           : Active
ServiceEndpoints : {}
```

### D1.3 — Create a DID with a specific method name

```powershell
$didDoc = New-Svrn7Did -KeyPair $kp -MethodName 'sovronia'
$didDoc.Did   # did:sovronia:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
```

### D1.4 — Create a DID with a TDA service endpoint

Pass `-ServiceEndpointUrl` to embed the TDA's DIDComm endpoint directly in the document.
Other TDAs discover it by resolving the DID — no out-of-band configuration needed.

```powershell
$didDoc = New-Svrn7Did -KeyPair $kp -MethodName 'sovronia' `
                       -ServiceEndpointUrl 'https://sovronia.svrn7.net:8443/didcomm'
$didDoc.ServiceEndpoints | Format-List
```

Expected:

```
Id              : did:sovronia:3J98...#didcomm-1
Type            : DIDCommMessaging
ServiceEndpoint : https://sovronia.svrn7.net:8443/didcomm
```

The service endpoint also appears in `DocumentJson`:

```powershell
$didDoc.DocumentJson | ConvertFrom-Json | Select-Object -ExpandProperty service
```

```json
[{ "id": "did:sovronia:3J98...#didcomm-1", "type": "DIDCommMessaging",
   "serviceEndpoint": "https://sovronia.svrn7.net:8443/didcomm" }]
```

### D1.5 — Pipeline form

```powershell
$didDoc = New-Svrn7KeyPair | New-Svrn7Did -MethodName 'bindloss' `
                                          -ServiceEndpointUrl 'https://bindloss.svrn7.net:8443/didcomm'
```

---

## Scenario D2 — Inspect all W3C DIDDocument fields

`New-Svrn7Did` returns a `Svrn7.Core.Models.DidDocument` with all W3C DID Core 1.0
properties as first-class typed fields.

### D2.1 — Verification methods

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

### D2.2 — Verification relationships

```powershell
$didDoc.Authentication       # ['did:drn:3J98...#key-1']
$didDoc.AssertionMethod      # ['did:drn:3J98...#key-1']
$didDoc.KeyAgreement         # [] — empty at creation
$didDoc.CapabilityInvocation # [] — empty at creation
$didDoc.CapabilityDelegation # [] — empty at creation
```

`Authentication` and `AssertionMethod` are both populated with `#key-1` at construction.
The remaining relationship lists start empty and are populated when the DID owner adds
specialised keys.

### D2.3 — Service endpoints

Preferred: pass `-ServiceEndpointUrl` to `New-Svrn7Did` (see D1.4) — the endpoint is
embedded at construction time and included in `DocumentJson`.

```powershell
# With -ServiceEndpointUrl
$didDoc.ServiceEndpoints | Format-List
# Id, Type, ServiceEndpoint populated

# Without -ServiceEndpointUrl
$didDoc.ServiceEndpoints     # {} — empty
```

To add an endpoint manually after construction (before registration):

```powershell
$didDoc.ServiceEndpoints.Add([Svrn7.Core.Models.DidServiceEndpoint]@{
    Id              = "$($didDoc.Did)#didcomm-1"
    Type            = 'DIDCommMessaging'
    ServiceEndpoint = 'https://alpha.svrn7.net:8443/didcomm'
})
```

Note: manually added endpoints are **not** reflected in `DocumentJson` — use
`-ServiceEndpointUrl` on `New-Svrn7Did` when the document will be persisted.

### D2.4 — Controller and AlsoKnownAs

```powershell
$didDoc.Controller    # 'did:drn:3J98...' — same as Did at construction
$didDoc.AlsoKnownAs   # [] — empty at construction
```

### D2.5 — Proof (Data Integrity)

```powershell
$didDoc.Proof   # $null at construction — populated after signing
```

### D2.6 — Canonical W3C JSON

`DocumentJson` holds the serialized W3C DID Document that is stored in the registry
and returned in DID resolution responses:

```powershell
$didDoc.DocumentJson | ConvertFrom-Json | ConvertTo-Json -Depth 5
```

Expected (without service endpoint):

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
  "assertionMethod": ["did:drn:3J98...#key-1"]
}
```

With `-ServiceEndpointUrl`, a `service` array is appended:

```json
{
  ...,
  "service": [
    {
      "id": "did:drn:3J98...#didcomm-1",
      "type": "DIDCommMessaging",
      "serviceEndpoint": "https://alpha.svrn7.net:8443/didcomm"
    }
  ]
}
```

### D2.7 — Registry metadata

```powershell
$didDoc.Id            # LiteDB document PK (Guid, not the DID)
$didDoc.Version       # 1
$didDoc.Status        # Active
$didDoc.CreatedAt
$didDoc.UpdatedAt
$didDoc.DeactivatedAt # $null while Active
```

---

## Scenario D3 — DID resolution (TDA running)

These steps require the Federation driver to be initialised. Run after the TDA has
performed its bootstrap sequence (federation init → society register → citizen onboard).

### D3.1 — Resolve a DID to its full DIDDocument

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
$result.Document.DocumentJson | ConvertFrom-Json | ConvertTo-Json -Depth 5
```

### D3.2 — Resolve a DID that does not exist

```powershell
$result = Resolve-Svrn7Did -Did 'did:drn:doesnotexist'
$result.Found      # $false
$result.Document   # $null
$result.ErrorCode  # 'notFound'
```

### D3.3 — Test whether a DID is Active

```powershell
Test-Svrn7DidActive -Did $didDoc.Did   # $true
```

### D3.4 — Resolve a citizen's primary DID from any DID

A citizen may hold multiple DIDs across method names. `Resolve-Svrn7CitizenPrimaryDid`
normalises any of them back to the primary:

```powershell
# Given a secondary DID did:sovroniamed:3J98... returns did:sovronia:3J98...
Resolve-Svrn7CitizenPrimaryDid -Did 'did:sovroniamed:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy'
```

Returns `$null` if the DID is not registered.

---

## Scenario D3b — Registering Federation, Society, or Citizen (TDA running)

All three Register cmdlets accept `-DidDocument` directly. The DID, method name, public
key, and service endpoint all come from the document — no redundant parameters. All three
persist the `DidDocument` to `svrn7-dids.db` via `LiteDidDocumentRegistry`.

### D3b.0 — Register the Federation (one-time, idempotent)

Must be done once before registering any Societies. Safe to repeat — on a second call
`AlreadyInitialised=$true` is returned and the existing `DidDocument` is left unchanged.

```powershell
$kp     = New-Svrn7KeyPair
$didDoc = New-Svrn7Did -KeyPair $kp -MethodName 'drn' `
                       -ServiceEndpointUrl 'https://foundation.svrn7.net:8443/didcomm'

Register-Svrn7Federation -DidDocument $didDoc -KeyPair $kp -Name 'Web 7.0 Foundation'
```

Expected output:

```
FederationDid      : did:drn:3J98...
FederationName     : Web 7.0 Foundation
MethodName         : drn
AlreadyInitialised : False
Success            : True
```

Verify the DIDDocument was persisted:

```powershell
Resolve-Svrn7Did -Did $didDoc.Did | Select-Object -ExpandProperty Document |
    Format-List Did, MethodName, Status
$result.Document.ServiceEndpoints | Format-List   # DIDCommMessaging endpoint present
```

### D3b.1 — Register a Society

```powershell
$kp     = New-Svrn7KeyPair
$didDoc = New-Svrn7Did -KeyPair $kp -MethodName 'sovronia' `
                       -ServiceEndpointUrl 'https://sovronia.svrn7.net:8443/didcomm'

Register-Svrn7Society -DidDocument $didDoc -KeyPair $kp -Name 'Sovronia Digital Nation'
```

Expected output:

```
SocietyDid            : did:sovronia:3J98...
SocietyName           : Sovronia Digital Nation
MethodName            : sovronia
DrawAmountGrana       : 1000000000000
OverdraftCeilingGrana : 10000000000000
Success               : True
```

The persisted `DidDocument` in the registry will contain the `DIDCommMessaging` service
endpoint, so any other TDA can discover the URL by resolving the Society DID.

### D3b.2 — Register a Citizen in a Society

```powershell
$kp     = New-Svrn7KeyPair
$didDoc = New-Svrn7Did -KeyPair $kp -MethodName 'sovronia' `
                       -ServiceEndpointUrl 'https://citizen1.svrn7.net:8443/didcomm'

Register-Svrn7CitizenInSociety -DidDocument $didDoc -KeyPair $kp
```

Expected output:

```
CitizenDid     : did:sovronia:3J98...
SocietyDid     : did:sovronia:alpha.svrn7.net
EndowmentSvrn7 : 1000.000000
EndowmentGrana : 1000000000
MethodName     :
Success        : True
```

### D3b.3 — Register a Citizen with a preferred secondary method

```powershell
Register-Svrn7CitizenInSociety -DidDocument $didDoc -KeyPair $kp `
    -PreferredMethodName 'sovroniamed'
```

The citizen's primary DID uses the Society's default method; the secondary DID is issued
under `sovroniamed`.

### D3b.4 — Federation-level citizen registration (no endowment)

Use `Register-Svrn7Citizen` when registering directly at Federation level without the
Society endowment transfer:

```powershell
$kp     = New-Svrn7KeyPair
$didDoc = New-Svrn7Did -KeyPair $kp -MethodName 'drn' `
                       -ServiceEndpointUrl 'https://fed-citizen.svrn7.net:8443/didcomm'

Register-Svrn7Citizen -DidDocument $didDoc -KeyPair $kp
```

---

## Scenario D4 — DID method management (Federation LOBE)

DID method names are registered per-Society and gate which method-name prefixes a
Society may issue. Requires TDA running with an initialised federation.

### D4.1 — Check the status of a method name

```powershell
Get-Svrn7DidMethodStatus -MethodName 'sovronia'
# Active | Dormant | (not returned = Available)
```

### D4.2 — List all registered method names

```powershell
Get-Svrn7DidMethods | Format-Table MethodName, Status, SocietyDid -AutoSize
```

Filter by Society:

```powershell
Get-Svrn7DidMethods -SocietyDid 'did:drn:bindloss.svrn7.net' -Status Active
```

### D4.3 — Register an additional method name (Federation LOBE)

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

### D4.4 — Deregister a method name

The primary method name cannot be deregistered. The name enters a 30-day dormancy period
before it can be re-registered by any Society.

```powershell
Unregister-Svrn7DidMethod -SocietyDid 'did:drn:bindloss.svrn7.net' -MethodName 'bindlossedu'
```

Expected output:

```
SocietyDid : did:drn:bindloss.svrn7.net
MethodName : bindlossedu
Status     : Dormant
Success    : True
```

Verify the dormancy:

```powershell
Get-Svrn7DidMethodStatus -MethodName 'bindlossedu'   # Dormant
```

---

## Scenario D5 — DID method management (Society LOBE)

The Society LOBE exposes self-service variants of the same operations.  
`-SocietyDid` is inferred from the loaded Society driver — no parameter needed.

### D5.1 — Register an additional method name

```powershell
Register-Svrn7SocietyDidMethod -MethodName 'sovroniamed'
```

### D5.2 — Register multiple method names via pipeline

```powershell
'sovroniaedu','sovroniahealth' | Register-Svrn7SocietyDidMethod
```

### D5.3 — List method names for this Society

```powershell
Get-Svrn7SocietyDidMethods | Format-Table MethodName, Status, IsPrimary -AutoSize
```

### D5.4 — Deregister a method name

```powershell
Unregister-Svrn7SocietyDidMethod -MethodName 'sovroniaedu'
```

---

## Scenario D6 — Secondary DIDs for citizens (Society LOBE)

A citizen's secondary DID uses the same base58-encoded public key as the primary,
but under a different method name. Both DIDs resolve to the same `CitizenRecord`.

### D6.1 — Issue a secondary DID

The method name must be Active and registered to this Society before issuance.

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

### D6.2 — List all DIDs for a citizen

```powershell
Get-Svrn7CitizenDids -PrimaryDid $citizenPrimaryDid | Format-Table Did, IsPrimary, MethodName
```

### D6.3 — Resolve a secondary DID back to primary

```powershell
$secondaryDid = 'did:sovroniamed:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy'
Resolve-Svrn7CitizenPrimaryDid -Did $secondaryDid
# did:sovronia:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
```

---

## Scenario D7 — DIDComm-based DID resolution (Identity LOBE, TDA running)

The Identity LOBE handles DID resolution requests arriving as DIDComm messages.

Protocol URIs:

| Direction | URI |
|-----------|-----|
| Request  | `did:drn:svrn7.net/protocols/did/1.0/resolve-request` |
| Response | `did:drn:svrn7.net/protocols/did/1.0/resolve-response` |

### D7.1 — Send a DID resolve request

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
[Info]  Invoke-Svrn7DidResolveResponse: requestedDid='did:sovronia:abc123...' found=True from='did:drn:3J98...'
```

### D7.2 — Response payload (delivered to requester's TDA)

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

### D7.3 — Resolve a DID that is not found

Send the same message with a DID that is not registered. The response will have:

```json
{ "found": false, "didDocument": null, "requestedDid": "did:drn:doesnotexist" }
```

---

## DIDDocument persistence reference

All three Register cmdlets persist the `DidDocument` as part of the same registration
transaction. The chain is identical for all three:

```
Register-Svrn7Federation / Register-Svrn7Society / Register-Svrn7CitizenInSociety
  → ISvrn7Driver.InitialiseFederationAsync / RegisterSocietyAsync / RegisterCitizenAsync
    → LiteDidDocumentRegistry.CreateAsync
      → svrn7-dids.db  (DidRegistryLiteContext, Documents + History collections)
```

| Cmdlet | Driver method | Persists DidDocument |
|--------|--------------|:---:|
| `Register-Svrn7Federation` | `InitialiseFederationAsync(DidDocument, name)` | Yes |
| `Register-Svrn7Society` | `RegisterSocietyAsync(RegisterSocietyRequest)` | Yes |
| `Register-Svrn7Citizen` | `RegisterCitizenAsync(RegisterCitizenRequest)` | Yes |
| `Register-Svrn7CitizenInSociety` | `RegisterCitizenInSocietyAsync` → `RegisterCitizenAsync` | Yes |

The `History` collection in `svrn7-dids.db` keeps every version — resolve a specific
version with `Resolve-Svrn7Did` after updating, or query history directly via
`ISvrn7Driver.GetDidHistoryAsync`.

---

## Quick-reference: all DID cmdlets

| Cmdlet | Key parameters | LOBE | Requires TDA |
|--------|---------------|------|:---:|
| `New-Svrn7KeyPair` | — | Federation | No |
| `New-Svrn7Did` | `-KeyPair` `-MethodName` `-ServiceEndpointUrl` | Federation | Yes — TDA runspace only |
| `Register-Svrn7Federation` | `-DidDocument` `-KeyPair` `-Name` | Federation | Yes |
| `Register-Svrn7Citizen` | `-DidDocument` `-KeyPair` | Federation | Yes |
| `Register-Svrn7Society` | `-DidDocument` `-KeyPair` `-Name` | Federation | Yes |
| `Resolve-Svrn7Did` | `-Did` | Federation | Yes |
| `Test-Svrn7DidActive` | `-Did` | Federation | Yes |
| `Resolve-Svrn7CitizenPrimaryDid` | `-Did` | Federation | Yes |
| `Get-Svrn7CitizenDids` | `-PrimaryDid` | Federation | Yes |
| `Register-Svrn7DidMethod` | `-SocietyDid` `-MethodName` | Federation | Yes |
| `Unregister-Svrn7DidMethod` | `-SocietyDid` `-MethodName` | Federation | Yes |
| `Get-Svrn7DidMethodStatus` | `-MethodName` | Federation | Yes |
| `Get-Svrn7DidMethods` | `[-SocietyDid]` `[-Status]` | Federation | Yes |
| `Register-Svrn7CitizenInSociety` | `-DidDocument` `-KeyPair` `[-PreferredMethodName]` | Society | Yes |
| `Register-Svrn7SocietyDidMethod` | `-MethodName` | Society | Yes |
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
