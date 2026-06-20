# Pando.TDA — Debug & Testing Guide

![](../../docs/images/Web%207.0%20DSA-SocietyArch%200.26.png)

![](../../docs/images/Web%207.0%20DSA-TDA%200.25.png)

## Overview

- Single inbound endpoint: `POST http://localhost:{port}/didcomm`
- Protocol: **HTTP/2 cleartext (h2c)** — the server only speaks HTTP/2; HTTP/1.1 requests are rejected
- No TLS cert configured → cleartext development mode (see `Program.cs` and `KestrelListenerService.cs`)
- `UnpackAsync` has a **plaintext branch**: if the JSON body has a `"type"` property at the root, it passes through without decryption — no keys needed for dev testing
- **Encrypted messages are not yet decrypted**: `UnpackAsync` does not implement JWE decryption — encrypted inbound messages are dead-lettered immediately
- A valid message returns **202 Accepted** and is enqueued; the Switchboard routes it asynchronously

---

## Role-specific Debug Guides

Each TDA role has a dedicated guide.  Run them in order:

| Guide | Role | Port (default) | Prerequisite |
|---|---|---|---|
| `FEDERATIONDEBUG.md` | Federation TDA | 8441 | None — run first |
| `SOCIETYDEBUG.md` | Society TDA | 8442 | `FEDERATIONDEBUG.md` complete |
| `CITIZENDEBUG.md` | Citizen TDA | 8443 | `SOCIETYDEBUG.md` complete |
| `WANDERERDEBUG.md` | Wanderer TDA | 8445, 8446 | Standalone — no Federation required |
| `DNSDEBUG.md` | drn.directory DNS | — | TDA built |
| `DIDDEBUG.md` | DID Document types | — | Reference |
| `LOBEDEBUG.md` | LOBE authoring | — | Reference |

---

## PowerShell Requirement — VS 2022 / VS 2026

The send helpers in this guide use `Send-LocalDIDCommMessage` and require **PowerShell 7 (`pwsh.exe`)**.
The VS Developer PowerShell defaults to Windows PowerShell 5.1 (.NET Framework), which is missing
required .NET 5+ types. Configure VS to use PowerShell 7 once:

1. **Tools → Options → Environment → Terminal**
2. Click **Add**
3. Set **Name** `PowerShell 7`, **Shell location** `C:\Program Files\PowerShell\7\pwsh.exe`, **Arguments** `-NoExit`
4. Check **Make default**, click **OK**

Verify you are on PowerShell 7 before running any scenario:
```powershell
$PSVersionTable.PSVersion   # Major should be 7
```

---

## Working Directory

All commands in the role-specific guides assume:

```powershell
Set-Location C:/SVRN7/repos/SVRN7
Set-Location src/Svrn7.TDA/bin/Debug/net8.0
```

---

## Running Automated Tests

### C# unit / integration tests (xUnit)

```powershell
# From repo root
dotnet test .\tests\Svrn7.Tests\Svrn7.Tests.csproj
dotnet test .\tests\Svrn7.TDA.Tests\Svrn7.TDA.Tests.csproj
dotnet test .\tests\Svrn7.Society.Tests\Svrn7.Society.Tests.csproj
```

### PowerShell LOBE tests (Pester)

Tests the LOBE PowerShell layer — function availability after module import,
`Build-CanonicalTransferJson` field ordering, `Send-LocalDIDCommMessage` parameter contract,
and `Initialize-Svrn7Assemblies` path resolution.  No TDA or compiled assemblies required.

Install Pester 5 once if needed:

```powershell
Install-Module Pester -MinimumVersion 5.0 -Force -SkipPublisherCheck -Scope CurrentUser
```

Run from repo root:

```powershell
Import-Module Pester -MinimumVersion 5.0 -Force
Set-Location C:\SVRN7\repos\SVRN7
Invoke-Pester .\tests\Svrn7.Lobes.Tests.ps1 -Output Detailed
```

> `Import-Module Pester -MinimumVersion 5.0 -Force` is required because Windows ships
> Pester 3.4.0 and PowerShell would otherwise load the older version.

---

## Resetting the Environment

All state lives in five LiteDB files under the `{port}\mem\` subfolder.
LiteDB holds an **exclusive write lock** — **stop the TDA before deleting any database file**.

### Full reset

```powershell
# 1. Stop all TDAs (Ctrl+C in each window)
# 2. Delete all mem folders in one shot:
Remove-Item -Recurse -Force "*\mem" -ErrorAction SilentlyContinue
# 3. Restart — databases are recreated on first run
```

Or pass `--reset` at startup to let the TDA delete its own data:

```powershell
dotnet .\Svrn7.TDA.dll --port 8441 --name Federation --reset
```

After a full reset, run the role-specific guides in order: `FEDERATIONDEBUG.md` → `SOCIETYDEBUG.md` → `CITIZENDEBUG.md`.

---

## Available Protocol URIs — All TDA Roles

| `type` URI | Handler | TDA role |
|---|---|---|
| `.../Svrn7.Federation.0.8.0/initialize-federation` | `Invoke-Web7FederationInit` | Federation |
| `.../Svrn7.Federation.0.8.0/federation-query` | `Invoke-Web7FederationQuery` | Federation |
| `.../Svrn7.Federation.0.8.0/society-list` | `Invoke-Web7SocietyList` | Federation |
| `.../Svrn7.Federation.0.8.0/society-list-result` | `Invoke-Web7SocietyListResult` | Citizen / Society |
| `.../Svrn7.Federation.0.8.0/register-society` | `Invoke-Web7RegisterSociety` | Federation |
| `.../Svrn7.Federation.0.8.0/register-society-result` | `Invoke-Web7RegisterSocietyResult` | Society |
| `.../Svrn7.Onboarding.0.8.0/register-citizen` | `ConvertFrom-Web7OnboardRequest` | Society |
| `.../Svrn7.Onboarding.0.8.0/receipt` | `Invoke-Web7OnboardReceipt` | Citizen |
| `.../Svrn7.Society.0.8.0/society-query` | `Invoke-Web7SocietyQuery` | Society |
| `.../Svrn7.Society.0.8.0/member-query` | `Invoke-Web7MemberQuery` | Society |
| `.../Svrn7.Society.0.8.0/overdraft-query` | `Invoke-Web7OverdraftQuery` | Society |
| `.../Svrn7.Society.0.8.0/did-method-register` | `Invoke-Web7DidMethodRegister` | Society |
| `.../Svrn7.Society.0.8.0/did-methods-query` | `Invoke-Web7DidMethodsQuery` | Society |
| `.../Svrn7.Society.0.8.0/citizen-did-add` | `Invoke-Web7CitizenDidAdd` | Society |
| `.../Svrn7.Society.0.8.0/transfer-request` | `Invoke-Svrn7IncomingTransfer` | Society |
| `.../Svrn7.Society.0.8.0/transfer-order` | `Invoke-Svrn7IncomingTransfer` | Society |
| `.../Svrn7.Society.0.8.0/transfer-order-receipt` | `Confirm-Svrn7Settlement` | Society |

Full URI prefix: `did:drn:svrn7.net/protocols/`

---

## Response Codes

| Code | Meaning |
|---|---|
| `202 Accepted` | Message unpacked and enqueued successfully |
| `400 Bad Request` | Empty body, invalid JSON, or DIDComm unpack failed |
| `415 Unsupported Media Type` | Content-Type is not `application/didcomm-encrypted+json` or `application/didcomm-plain+json` |
| `403 Forbidden` | Plaintext message with `@type` not in `PlaintextDiscoveryProtocols` (only `did-resolve-request`/`did-resolve-response` are permitted as plaintext) |

---

## Tracing PowerShell Cmdlet Execution

At `LogLevel.Information`, the Switchboard logs the cmdlet name and LOBE when it dispatches:

```
Switchboard: routing {Did} (type={Type}) → {EP} [{LOBE}]
```

At `LogLevel.Trace`, it additionally logs cmdlet start, completion, and all PowerShell
streams forwarded to the .NET logger:

```
[Trace] PS invoke: Invoke-Svrn7IncomingTransfer -MessageDid did:tda:...
[Trace]   [PS Verbose] ...   ← Write-Verbose inside the .psm1
[Debug]   [PS Debug] ...     ← Write-Debug
[Info]    [PS Info] ...      ← Write-Information / Write-Host
[Warn]    [PS Warning] ...   ← Write-Warning
[Trace] PS complete: Invoke-Svrn7IncomingTransfer → 1 result(s).
```

---

## Log Level

Set in `appsettings.json`:

```json
"Svrn7.TDA.DIDCommMessageSwitchboard": "Debug"
```

Or in `Program.cs` `ConfigureLogging`:

```csharp
logging.SetMinimumLevel(LogLevel.Trace);       // verbose
logging.SetMinimumLevel(LogLevel.Information); // normal
```

---

## Common Error Conditions

| Symptom | Cause | Fix |
|---|---|---|
| `400 Bad Request` on POST /didcomm | Body is empty, not valid JSON, or missing `type` field at root | Add `"type"` key to the message root |
| `202` but log shows `No LOBE registered for @type` | `type` URI does not match any registered protocol | Check `lobes.config.json` and `.lobe.json` protocol URIs |
| `202` but log shows `CitizenAlreadyRegisteredException` | Citizen DID already registered | Expected — use a new key pair and DID |
| `202` but log shows `SocietyEndowmentDepletedException` | Society overdraft ceiling reached | Check overdraft-query; await Federation top-up |
| Agent 2 log: `No DIDComm service endpoint for <DID>` | Citizen DID document has no DIDComm service entry | Register the citizen's DID document before sending the receipt |
| `415 Unsupported Media Type` | Content-Type header not recognized | Use `application/didcomm-encrypted+json` or `application/didcomm-plain+json` |
| `403 Forbidden` on plaintext POST | `@type` is not `did-resolve-request` or `did-resolve-response` | Only DID discovery protocols may be sent as plaintext; all others require SignThenEncrypt |

---

## Scenario F — Test Teardown: Remove All LiteDB Databases

`Remove-Svrn7Databases` deletes all five LiteDB files and their companion journal files.
**Stop the TDA host before running this.**

### F.1 — Interactive teardown

```powershell
Remove-Svrn7Databases
```

### F.2 — Non-interactive teardown (CI / automated test scripts)

```powershell
Remove-Svrn7Databases -Confirm:$false
```

### F.3 — Preview without deleting (`-WhatIf`)

```powershell
Remove-Svrn7Databases -WhatIf
```

Expected output:

```
Path                   Existed Removed
----                   ------- -------
svrn7.db               True    True
svrn7.db-log           False   False
svrn7-dids.db          True    True
...
```

### F.4 — Custom data directory

```powershell
Remove-Svrn7Databases `
    -Svrn7DbPath    tests/data/svrn7.db `
    -DidsDbPath     tests/data/svrn7-dids.db `
    -VcsDbPath      tests/data/svrn7-vcs.db `
    -InboxDbPath    tests/data/svrn7-inbox.db `
    -SchemasDbPath  tests/data/svrn7-schemas.db `
    -Confirm:$false
```

### F.5 — Typical test lifecycle pattern

```powershell
# 1. Tear down previous run
Remove-Svrn7Databases -Confirm:$false

# 2. Start TDA host
Start-Process dotnet -ArgumentList '.\Svrn7.TDA.dll','--port','8441','--name','Federation' -NoNewWindow

# 3. Run test scenarios ...

# 4. Tear down
Remove-Svrn7Databases -Confirm:$false
```

### Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-Svrn7DbPath` | `svrn7.db` | Main wallet / UTXO / Merkle log database |
| `-DidsDbPath` | `svrn7-dids.db` | DID Document registry |
| `-VcsDbPath` | `svrn7-vcs.db` | Verifiable Credential registry |
| `-InboxDbPath` | `svrn7-inbox.db` | DIDComm inbox, outbox, processed orders |
| `-SchemasDbPath` | `svrn7-schemas.db` | JSON Schema 2020-12 registry |
