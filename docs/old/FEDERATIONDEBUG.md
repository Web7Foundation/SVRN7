# Web 7.0 Pando — Federation TDA Debug Guide

Covers launching a Federation TDA and performing the full Federation bootstrap:
initialising the federation record, querying it, and registering the first Society.

Run this guide first.  `SOCIETYDEBUG.md` requires the Federation to be initialised
before it starts.

---

## Overview

The Federation TDA is the root tier of the Web 7.0 Pando hierarchy.  It holds the
shared ledger, registers Societies, and serves `society-list` responses to Wanderers
seeking to join a Society.  One Federation TDA exists per network deployment.

Key protocols handled by a Federation TDA:

| Protocol URI | Handler |
|---|---|
| `did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/initialize-federation` | `Invoke-Web7FederationInit` |
| `did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/federation-query` | `Invoke-Web7FederationQuery` |
| `did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/register-society` | `Invoke-Web7RegisterSociety` |
| `did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/society-list` | `Invoke-Web7SocietyList` |

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

---

## Working Directory

All commands assume:

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
```

Run this once at the start of every session.

---

## Step 1 — Launch the Federation TDA

```powershell
dotnet .\Svrn7.TDA.dll --port 8441 --name Federation
```

Expected startup banner (first run):

```
────────────────────────────────────────────────────────────────────────────────
  SVRN7 Trusted Digital Assistant (TDA)  v0.8.0
  Web 7.0 Foundation — https://svrn7.net
────────────────────────────────────────────────────────────────────────────────
  TDA Name    : Federation
  First run   : yes — Wanderer identity created
  Role        : Wanderer
  Agent DID   : did:drn:wanderer.svrn7.net/agent/1.0/<genesis-hash>
  Listen port : 8441
────────────────────────────────────────────────────────────────────────────────
  Federation  : (not yet initialised — see §E.0 to initialise)
  Societies   : (not yet initialised)
────────────────────────────────────────────────────────────────────────────────
```

---

## Step 2 — Load the send helper (separate PowerShell terminal)

Open a second PowerShell 7 terminal and set the working directory:

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
```

Import the LOBE that provides `Send-LocalDIDCommMessage`:

```powershell
Import-Module .\lobes\Svrn7.Federation.0.8.0\Svrn7.Federation.0.8.0.psm1
```

> `Invoke-RestMethod -HttpVersion 2.0` does not work with cleartext HTTP/2 (h2c):
> PowerShell uses `HttpVersionPolicy.RequestVersionOrLower`, which falls back to
> HTTP/1.1 — rejected by the server.  `Send-LocalDIDCommMessage` enforces HTTP/2 via
> `RequestVersionExact`.

---

## E.0 — Initialise the Federation

Sent once, before any Societies are registered.  Idempotent — safe to repeat.

### E.0.1 — Generate the federation governance key pair

This is a **one-time operation**.  The private key must be stored in a key vault
or HSM and never placed in config files.  The public key is recorded permanently
in the federation record.

```powershell
$federationKp = New-Svrn7KeyPair

Write-Host "Public key  : $($federationKp.PublicKeyHex)"
Write-Host "Private key : $($federationKp.PrivateKeyHex)   <-- store securely, never share"
```

Example output (your values will differ):

```
Public key  : 0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798
Private key : 18e14a7b5a...  <-- store securely, never share
```

### E.0.2 — Send initialize-federation

```powershell
$body = @{
    federationDid        = "did:drn:foundation.svrn7.net"
    federationName       = "Web 7.0 SOVRON Foundation"
    publicKeyHex         = $federationKp.PublicKeyHex
    primaryDidMethodName = "drn"
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/initialize-federation"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:foundation.svrn7.net")
    body = $body
} | ConvertTo-Json

Send-LocalDIDCommMessage -Port 8441 -Body $msg
```

Expected TDA log:

```
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: routing ... (type=.../Svrn7.Federation.0.8.0/initialize-federation)
          → Invoke-Web7FederationInit [Svrn7.Federation]
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      [PS Info] Federation initialised: did:drn:foundation.svrn7.net (Web 7.0 SOVRON Foundation)
```

Reply body (`initialize-federation-result`) delivered to the sender:

```json
{
  "federationDid":        "did:drn:foundation.svrn7.net",
  "federationName":       "Web 7.0 SOVRON Foundation",
  "primaryDidMethodName": "drn",
  "totalSupplyGrana":     1000000000000000000,
  "alreadyInitialised":   false,
  "initialisedAt":        "2026-..."
}
```

---

## E.1 — Query the Federation record

Verifies the federation was initialised correctly.  Also works before initialisation
— returns `found: false`.

```powershell
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/federation-query"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:foundation.svrn7.net")
    body = "{}"
} | ConvertTo-Json

Send-LocalDIDCommMessage -Port 8441 -Body $msg
```

Expected TDA log:

```
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: routing ... (type=.../Svrn7.Federation.0.8.0/federation-query)
          → Invoke-Web7FederationQuery [Svrn7.Federation]
```

Reply body (`federation-query-result`):

```json
{
  "found":                    true,
  "federationDid":            "did:drn:foundation.svrn7.net",
  "federationName":           "Web 7.0 SOVRON Foundation",
  "primaryDidMethodName":     "drn",
  "totalSupplyGrana":         1000000000000000000,
  "endowmentPerSocietyGrana": 0,
  "currentEpoch":             0,
  "isActive":                 true
}
```

---

## E.2 — Register the first Society

The Society TDA (or a bootstrap script) sends `register-society` to the Federation
TDA.  `serviceEndpointUrl` is required so the Federation can create the Society's
DID Document and deliver the `register-society-result` reply.

`Invoke-Web7RegisterSociety` handles the request on the Federation TDA.

```powershell
$societyKeyPair = New-Svrn7KeyPair

$body = @{
    societyDid            = "did:drn:bindloss.svrn7.net"
    publicKeyHex          = $societyKeyPair.PublicKeyHex
    societyName           = "Bindloss Alberta"
    primaryDidMethodName  = "bindloss"
    serviceEndpointUrl    = "http://localhost:8442/didcomm"   # Society TDA endpoint
    drawAmountGrana       = 1000000000000     # 1 SVRN7
    overdraftCeilingGrana = 10000000000000    # 10 SVRN7
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/register-society"
    from = "did:drn:bindloss.svrn7.net"
    to   = @("did:drn:foundation.svrn7.net")
    body = $body
} | ConvertTo-Json

Send-LocalDIDCommMessage -Port 8441 -Body $msg
```

Expected TDA log (Federation TDA):

```
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: routing ... (type=.../Svrn7.Federation.0.8.0/register-society)
          → Invoke-Web7RegisterSociety [Svrn7.Federation]
warn: ...
      RegisterSocietyAsync: FoundationPrivateKey not configured — VTC credential
      skipped for did:drn:bindloss.svrn7.net (development mode)
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      [PS Info] Invoke-Web7RegisterSociety: registered 'did:drn:bindloss.svrn7.net'
```

The Federation TDA delivers `register-society-result` to the Society TDA at
`http://localhost:8442/didcomm`.  See `SOCIETYDEBUG.md §E.2r` for what the
Society TDA does on receipt.

Reply body (`register-society-result`):

```json
{
  "societyDid":            "did:drn:bindloss.svrn7.net",
  "societyName":           "Bindloss Alberta",
  "primaryDidMethodName":  "bindloss",
  "societyDidDocument":    { "Did": "did:drn:bindloss.svrn7.net", ... },
  "federationDid":         "did:drn:foundation.svrn7.net",
  "federationEndpointUrl": "http://localhost:8441/didcomm",
  "federationDidDocument": { "Did": "did:drn:foundation.svrn7.net", ... },
  "drawAmountGrana":       1000000000000,
  "overdraftCeilingGrana": 10000000000000,
  "success":               true
}
```

---

## Resetting the Federation TDA

**Stop the TDA before deleting any database file** — LiteDB holds an exclusive
write lock for the lifetime of the process.

```powershell
# Delete all Federation TDA data (port 8441)
Remove-Item -Recurse -Force 8441\mem -ErrorAction SilentlyContinue

# Or use --reset at startup (equivalent):
dotnet .\Svrn7.TDA.dll --port 8441 --name Federation --reset
```

---

## Available Protocol URIs — Federation TDA

| `type` URI | Handler | Direction |
|---|---|---|
| `.../Svrn7.Federation.0.8.0/initialize-federation` | `Invoke-Web7FederationInit` | inbound |
| `.../Svrn7.Federation.0.8.0/federation-query` | `Invoke-Web7FederationQuery` | inbound |
| `.../Svrn7.Federation.0.8.0/register-society` | `Invoke-Web7RegisterSociety` | inbound |
| `.../Svrn7.Federation.0.8.0/society-list` | `Invoke-Web7SocietyList` | inbound |

Full URI prefix: `did:drn:svrn7.net/protocols/`

---

## Response Codes

| Code | Meaning |
|---|---|
| `202 Accepted` | Message unpacked and enqueued successfully |
| `400 Bad Request` | Empty body, invalid JSON, or missing `type` field |
| `415 Unsupported Media Type` | Content-Type is not `application/didcomm-encrypted+json` or `application/didcomm-plain+json` |
| `403 Forbidden` | Plaintext message with `@type` not in `PlaintextDiscoveryProtocols` |

---

## Log Level

Set in `appsettings.json`:

```json
"Svrn7.TDA.DIDCommMessageSwitchboard": "Debug"
```

Or in `Program.cs` `ConfigureLogging`:

```csharp
logging.SetMinimumLevel(LogLevel.Trace);   // verbose
logging.SetMinimumLevel(LogLevel.Information); // normal
```

---

## Tracing Cmdlet Execution

At `LogLevel.Information`, the Switchboard logs the cmdlet name and LOBE on dispatch:

```
Switchboard: routing {Did} (type={Type}) → {EP} [{LOBE}]
```

At `LogLevel.Trace`, it additionally logs cmdlet start, completion, and all PowerShell
streams forwarded to the .NET logger:

```
[Trace] PS invoke: Invoke-Web7FederationInit -MessageDid did:tda:...
[Info]    [PS Info] Federation initialised: ...
[Trace] PS complete: Invoke-Web7FederationInit → 1 result(s).
```

---

## Error Reference

| Symptom | Cause | Fix |
|---|---|---|
| `400 Bad Request` | Missing `type` field at root | Add `"type"` key to the message root |
| `202` but no routing log | `type` URI does not match any LOBE protocol | Check `.lobe.json` URIs; use exact match |
| `[PS Info] already initialised` | `initialize-federation` sent twice | Idempotent — safe to ignore |
| Society TDA shows no `register-society-result` log | Federation could not reach `serviceEndpointUrl` | Confirm Society TDA is running on port 8442 |
