# Web 7.0 Pando — Society TDA Debug Guide

Covers launching a Society TDA, completing the Society registration handshake with the
Federation TDA, registering Citizens, and querying Society state.

**Prerequisite:** Complete `FEDERATIONDEBUG.md` first — the Federation TDA must be
initialised (E.0) and the Society registered with the Federation (E.2) before starting
this guide.

---

## Overview

The Society TDA is the middle tier.  It receives Citizens, manages wallets, holds the
Merkle audit log, and resolves DID Documents for its members.  Multiple Societies can
exist under one Federation.

Key protocols handled by a Society TDA:

| Protocol URI | Handler |
|---|---|
| `did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/register-society-result` | `Invoke-Web7RegisterSocietyResult` |
| `did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/register-citizen` | `ConvertFrom-Web7OnboardRequest` |
| `did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/society-query` | `Invoke-Web7SocietyQuery` |
| `did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/member-query` | `Invoke-Web7MemberQuery` |
| `did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/overdraft-query` | `Invoke-Web7OverdraftQuery` |
| `did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/did-method-register` | `Invoke-Web7DidMethodRegister` |
| `did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/did-methods-query` | `Invoke-Web7DidMethodsQuery` |
| `did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/citizen-did-add` | `Invoke-Web7CitizenDidAdd` |

---

## Prerequisites

- PowerShell 7 (`pwsh.exe`):

  ```powershell
  $PSVersionTable.PSVersion   # Major must be 7
  ```

- Solution built:

  ```powershell
  Set-Location C:/SVRN7/repos/SVRN7
  dotnet build src/Svrn7.TDA/Svrn7.TDA.csproj
  ```

- Federation TDA running on port 8441 and initialised (`FEDERATIONDEBUG.md` complete).

---

## Working Directory

All commands assume:

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
```

---

## Scenario A — Launch the Society TDA

### A.1 — Start the Society TDA

```powershell
dotnet .\Svrn7.TDA.dll --port 8442 --name Bindloss
```

Expected startup banner (first run):

```
────────────────────────────────────────────────────────────────────────────────
  SVRN7 Trusted Digital Assistant (TDA)  v0.8.0
  Web 7.0 Foundation — https://svrn7.net
────────────────────────────────────────────────────────────────────────────────
  TDA Name    : Bindloss
  First run   : yes — Wanderer identity created
  Role        : Wanderer
  Agent DID   : did:drn:wanderer.svrn7.net/agent/1.0/<base58-pubkey>
  Listen port : 8442
────────────────────────────────────────────────────────────────────────────────
  Federation  : (not yet initialised)
  Societies   : (not yet initialised)
────────────────────────────────────────────────────────────────────────────────
```

Note the `Agent DID` line — this is the Society TDA's Wanderer identity.

### A.2 — Load the send helper (separate PowerShell terminal)

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
Import-Module .\lobes\Svrn7.Federation.0.8.0\Svrn7.Federation.0.8.0.psm1
```

---

## E.2r — Society Registration Result

After `FEDERATIONDEBUG.md §E.2` sends `register-society` to the Federation TDA,
the Federation delivers `register-society-result` to this TDA on port 8442.

`Invoke-Web7RegisterSocietyResult` runs automatically.  Expected log on this TDA:

```
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: routing ... (type=.../Svrn7.Federation.0.8.0/register-society-result)
          → Invoke-Web7RegisterSocietyResult [Svrn7.Federation]
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      [PS Info] Invoke-Web7RegisterSocietyResult: registered with did:drn:foundation.svrn7.net
```

On receipt the Society TDA:
1. Stores `societyDidDocument` in its local DID registry
2. Stores `federationDidDocument` in its local DID registry
3. Sets `parentTdaDid` = `federationDid` and `parentTdaEndpointUrl` = `federationEndpointUrl`
   (persisted to `agent-identity.json`)

Verify the parent wiring was persisted:

```powershell
Get-Content 8442/mem/agent-identity.json | ConvertFrom-Json |
    Select-Object did, parentTdaDid, parentTdaEndpointUrl
```

Expected:

```
did                  parentTdaDid                   parentTdaEndpointUrl
---                  ------------                   --------------------
did:drn:wanderer...  did:drn:foundation.svrn7.net   http://localhost:8441/didcomm
```

---

## Scenario B — Register a Citizen via DIDComm

Citizen registration is driven by `Svrn7.Onboarding.0.8.0/register-citizen`.
The Society TDA routes the message to `ConvertFrom-Web7OnboardRequest`, which calls
`Register-Svrn7CitizenInSociety` and delivers `Svrn7.Onboarding.0.8.0/receipt` to
the Citizen TDA.

### B.1 — Generate Citizen key material (client-side)

Run once and save the output.  The private key must be stored securely by the Citizen's
own TDA — the Society stores only the public key.

```powershell
$kp  = New-Svrn7KeyPair
$did = New-Svrn7Did -KeyPair $kp -MethodName "bindloss"

Write-Host "Citizen DID : $($did.Did)"
Write-Host "Public key  : $($kp.PublicKeyHex)"
Write-Host "Private key : $($kp.PrivateKeyHex)   <-- store this securely, never share"
```

Example output (values will differ):

```
Citizen DID : did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
Public key  : 0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798
Private key : <32-byte hex — keep secret>
```

### B.2 — Send the onboarding request

`serviceEndpointUrl` is the Citizen TDA's DIDComm endpoint — the Society uses it to
deliver the `receipt` reply.

```powershell
$citizenDid   = $did.Did
$publicKeyHex = $kp.PublicKeyHex

$body = @{
    citizenDid         = $citizenDid
    publicKeyHex       = $publicKeyHex
    displayName        = "mwherman"
    serviceEndpointUrl = "http://localhost:8443/didcomm"   # Citizen TDA endpoint
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/register-citizen"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Send-LocalDIDCommMessage -Port 8442 -Body $msg
```

Expected: `Status: Accepted`

### B.4 — Verify registration in the TDA log

With `LogLevel.Debug` or finer:

```
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: routing ... (type=.../Svrn7.Onboarding.0.8.0/register-citizen)
          → ConvertFrom-Web7OnboardRequest [Svrn7.Onboarding]
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      [PS Info] Citizen did:bindloss:3J98... registered. Endowment: 1000000000 grana.
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: outbound delivered to http://localhost:8443/didcomm (202).
```

The last line confirms the `receipt` was delivered to the Citizen TDA.

### B.5 — Verify via module query

```powershell
# Membership check
$body = @{ did = $citizenDid } | ConvertTo-Json -Compress
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/member-query"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json
Send-LocalDIDCommMessage -Port 8442 -Body $msg
```

Expected reply body:

```json
{ "societyDid": "did:drn:bindloss.svrn7.net", "did": "did:bindloss:3J98...", "isMember": true }
```

### B.6 — Duplicate registration (expected error)

Sending the same `register-citizen` twice (same `citizenDid`) logs:

```
[PS Info] Onboarding LOBE: error for ... — CitizenAlreadyRegisteredException
```

`202 Accepted` is still returned at the HTTP layer.  Use a new key pair and DID to
register a second Citizen.

---

## E.4 — Register Citizen (full message format reference)

The same message as B.2 above.  For completeness, the Society TDA log shows:

```
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: routing ... (type=.../Svrn7.Onboarding.0.8.0/register-citizen)
          → ConvertFrom-Web7OnboardRequest [Svrn7.Onboarding]
```

The `receipt` reply body (`Svrn7.Onboarding.0.8.0/receipt`) delivered to Citizen TDA:

```json
{
  "success":            true,
  "citizenDid":         "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy",
  "citizenDidDocument": { "Did": "did:bindloss:3J98...", "ServiceEndpoints": [...] },
  "societyDid":         "did:drn:bindloss.svrn7.net",
  "societyDidDocument": { "Did": "did:drn:bindloss.svrn7.net", ... },
  "societyEndpointUrl": "http://localhost:8442/didcomm",
  "endowmentGrana":     1000000000,
  "endowmentVcId":      "...",
  "registeredAt":       "2026-..."
}
```

---

## E.5 — Query the Society record

```powershell
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/society-query"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = "{}"
} | ConvertTo-Json
Send-LocalDIDCommMessage -Port 8442 -Body $msg
```

Reply body:

```json
{
  "societyDid":    "did:drn:bindloss.svrn7.net",
  "federationDid": "did:drn:foundation.svrn7.net",
  "currentEpoch":  0,
  "queriedAt":     "2026-..."
}
```

---

## E.6 — Test membership for a specific DID

```powershell
$body = @{ did = $citizenDid } | ConvertTo-Json -Compress
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/member-query"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json
Send-LocalDIDCommMessage -Port 8442 -Body $msg
```

Reply: `{ "societyDid": "did:drn:bindloss.svrn7.net", "did": "did:bindloss:3J98...", "isMember": true }`

---

## E.7 — List all members

Send `member-query` with an empty body:

```powershell
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/member-query"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = "{}"
} | ConvertTo-Json
Send-LocalDIDCommMessage -Port 8442 -Body $msg
```

Reply: `{ "societyDid": "did:drn:bindloss.svrn7.net", "memberCount": 1, "memberDids": ["did:bindloss:3J98..."] }`

---

## E.8 — Query overdraft status

```powershell
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/overdraft-query"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = "{}"
} | ConvertTo-Json
Send-LocalDIDCommMessage -Port 8442 -Body $msg
```

Reply body (after first citizen registration, overdraft drawn):

```json
{
  "societyDid":            "did:drn:bindloss.svrn7.net",
  "status":                "Overdrawn",
  "totalOverdrawnGrana":   1000000000000,
  "overdraftCeilingGrana": 10000000000000,
  "lifetimeDrawsGrana":    1000000000000,
  "drawCount":             1
}
```

---

## E.9 — Register a secondary DID method

```powershell
$body = @{ methodName = "bindlossgov" } | ConvertTo-Json -Compress
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/did-method-register"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json
Send-LocalDIDCommMessage -Port 8442 -Body $msg
```

Reply: `{ "societyDid": "did:drn:bindloss.svrn7.net", "methodName": "bindlossgov", "status": "Active", "success": true }`

---

## E.10 — List DID methods

```powershell
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/did-methods-query"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = "{}"
} | ConvertTo-Json
Send-LocalDIDCommMessage -Port 8442 -Body $msg
```

Reply body:

```json
{
  "societyDid": "did:drn:bindloss.svrn7.net",
  "methods": [
    { "methodName": "bindloss",    "isPrimary": true,  "status": "Active" },
    { "methodName": "bindlossgov", "isPrimary": false, "status": "Active" }
  ]
}
```

---

## E.11 — Add a secondary DID for "mwherman"

```powershell
$body = @{
    citizenPrimaryDid = $citizenDid
    methodName        = "bindlossgov"
} | ConvertTo-Json -Compress
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Society.0.8.0/citizen-did-add"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json
Send-LocalDIDCommMessage -Port 8442 -Body $msg
```

Reply: `{ "citizenPrimaryDid": "did:bindloss:3J98...", "secondaryDid": "did:bindlossgov:3J98...", "methodName": "bindlossgov", "success": true }`

---

## Scenario D — Pure PowerShell Cmdlet Workflow (no TDA running)

The PS modules call the same C# `ISvrn7SocietyDriver` stack the TDA uses internally.
Useful for scripted provisioning, admin tasks, and debugging without HTTP/2.

> **LiteDB lock:** If the Society TDA is running against the same `DbPath`, the PS
> module throws `LiteException` immediately.  Stop the TDA first, or use a separate
> `DbPath` (isolated — no shared state with the running TDA).

### D.1 — Import modules and initialise the drivers

```powershell
# Import order matters: Federation before Society
Import-Module .\lobes\Svrn7.Federation.0.8.0\Svrn7.Federation.0.8.0.psm1 -Force
Import-Module .\lobes\Svrn7.Society.0.8.0\Svrn7.Society.0.8.0.psm1    -Force

Initialize-Svrn7FederationDriver -DbPath "./data-ps" -DidMethodName "drn" -Verbose

Connect-Svrn7Society `
    -SocietyDid     "did:drn:bindloss.svrn7.net" `
    -FederationDid  "did:drn:foundation.svrn7.net" `
    -DidMethodNames @("bindloss") `
    -DbPath         "./data-ps"
```

Expected verbose output:

```
VERBOSE: Loaded: Svrn7.Core.dll
...
VERBOSE: Svrn7.Federation ready. DbRoot: ./data-ps  Method: drn
VERBOSE: Svrn7.Society connected: did:drn:bindloss.svrn7.net
```

### D.2 — Verify the Society record

```powershell
Get-Svrn7OwnSociety | Select-Object SocietyDid, CurrentEpoch
Get-Svrn7OverdraftStatus
```

### D.3 — Generate citizen key material

```powershell
$kp  = New-Svrn7KeyPair
$did = New-Svrn7Did -KeyPair $kp -MethodName "bindloss"
"Citizen DID : $($did.Did)"
"Public key  : $($kp.PublicKeyHex)"
```

### D.4 — Register the citizen

```powershell
$reg = Register-Svrn7CitizenInSociety -DidDocument $did -KeyPair $kp
$reg | Format-List
```

Expected:

```
CitizenDid     : did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
SocietyDid     : did:drn:bindloss.svrn7.net
EndowmentSvrn7 : 1000.000000
EndowmentGrana : 1000000000
Success        : True
```

### D.5 — Verify membership and overdraft

```powershell
Test-Svrn7SocietyMember -Did $did.Did
Get-Svrn7SocietyMembers | Select-Object MemberCount, MemberDids
Get-Svrn7OverdraftRecord | Format-List
```

### D.6 — Add a secondary DID method

```powershell
Initialize-Svrn7SocietyDidMethod -MethodName "bindlossgov"
Add-Svrn7CitizenDid -CitizenPrimaryDid $did.Did -MethodName "bindlossgov"
```

---

## Resetting the Society TDA

```powershell
# Stop the TDA first, then:
Remove-Item -Recurse -Force 8442\mem -ErrorAction SilentlyContinue

# Or use --reset at startup:
dotnet .\Svrn7.TDA.dll --port 8442 --name Bindloss --reset
```

After a reset, repeat `FEDERATIONDEBUG.md §E.2` to re-register the Society with the
Federation before starting this guide from §E.2r.

### Full teardown (remove Society LiteDB files only)

```powershell
Remove-Svrn7Databases -Confirm:$false
```

Uses default paths (`svrn7.db`, `svrn7-dids.db`, etc.) relative to the working directory.
See `Remove-Svrn7Databases -WhatIf` to preview which files will be deleted.

---

## Available Protocol URIs — Society TDA

| `type` URI | Handler | Direction |
|---|---|---|
| `.../Svrn7.Federation.0.8.0/register-society-result` | `Invoke-Web7RegisterSocietyResult` | inbound |
| `.../Svrn7.Federation.0.8.0/society-list-result` | `Invoke-Web7SocietyListResult` | inbound |
| `.../Svrn7.Onboarding.0.8.0/register-citizen` | `ConvertFrom-Web7OnboardRequest` | inbound |
| `.../Svrn7.Society.0.8.0/society-query` | `Invoke-Web7SocietyQuery` | inbound |
| `.../Svrn7.Society.0.8.0/member-query` | `Invoke-Web7MemberQuery` | inbound |
| `.../Svrn7.Society.0.8.0/overdraft-query` | `Invoke-Web7OverdraftQuery` | inbound |
| `.../Svrn7.Society.0.8.0/did-method-register` | `Invoke-Web7DidMethodRegister` | inbound |
| `.../Svrn7.Society.0.8.0/did-methods-query` | `Invoke-Web7DidMethodsQuery` | inbound |
| `.../Svrn7.Society.0.8.0/citizen-did-add` | `Invoke-Web7CitizenDidAdd` | inbound |
| `.../Svrn7.Society.0.8.0/transfer-request` | `Invoke-Svrn7IncomingTransfer` | inbound |
| `.../Svrn7.Society.0.8.0/transfer-order` | `Invoke-Svrn7IncomingTransfer` | inbound |
| `.../Svrn7.Society.0.8.0/transfer-order-receipt` | `Confirm-Svrn7Settlement` | inbound |

Full URI prefix: `did:drn:svrn7.net/protocols/`

---

## Error Reference

| Symptom | Cause | Fix |
|---|---|---|
| `400 Bad Request` | Missing `type` field | Add `"type"` key to the message root |
| `202` but `CitizenAlreadyRegisteredException` | Citizen DID already registered | Use a new key pair and DID |
| `202` but `SocietyEndowmentDepletedException` | Society overdraft ceiling reached | Check `overdraft-query`; await Federation top-up |
| No `register-society-result` received | Federation could not reach port 8442 | Start this TDA before sending E.2 |
| `agent-identity.json` missing `parentTdaDid` | `register-society-result` not received yet | Confirm Federation TDA delivered the result |
| `202` but `unknown message type application/didcomm-encrypted+json` | Encrypted JWE sent — not yet decrypted | Use plaintext messages for dev/test |
