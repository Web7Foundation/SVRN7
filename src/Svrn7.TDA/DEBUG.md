# Svrn7.TDA — Debug & Testing Guide

![](../../docs/images/Web%207.0%20DSA-SocietyArch%200.26.png)

![](../../docs/images/Web%207.0%20DSA-TDA%200.25.png)

## Overview

- Single inbound endpoint: `POST http://localhost:8443/didcomm`
- Protocol: **HTTP/2 cleartext (h2c)** — the server only speaks HTTP/2; HTTP/1.1 requests are rejected
- No TLS cert configured → cleartext development mode (see `Program.cs` and `KestrelListenerService.cs`)
- `UnpackAsync` has a **plaintext branch**: if the JSON body has a `"type"` property at the root, it passes through without decryption — no keys needed for dev testing. It also extracts the `"id"` field as `DIDCommUnpackedMessage.Id` (the DIDComm wire id), which is stored in `InboxMessage.WireId`.
- **Encrypted messages are not yet decrypted**: `UnpackAsync` does not implement JWE decryption — `recipientPrivateKey` is accepted but currently ignored. Encrypted inbound messages will be stored with `MessageType = "application/didcomm-encrypted+json"` and immediately dead-lettered by the Switchboard (`MarkFailedAsync`, no retry). Only plaintext messages are routed end-to-end in the current implementation.
- A valid message returns **202 Accepted** and is enqueued; the Switchboard routes it asynchronously

---

## Log Level

Set in `Program.cs` `ConfigureLogging`:

```csharp
logging.SetMinimumLevel(LogLevel.Trace);   // verbose
logging.SetMinimumLevel(LogLevel.Information); // normal
```

---

## PowerShell Testing

### Step 1 — Connectivity check

```powershell
Test-NetConnection localhost -Port 8443
```

---

### Step 2 — Send a plaintext DIDComm message (PowerShell 7.3+)

`-HttpVersion 2.0` is required — the server rejects HTTP/1.1.

```powershell
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/transfer/1.0/request"
    from = "did:test:sender"
    to   = @("did:drn:alpha.svrn7.net")
    body = "{}"
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
```

Expected: `202 Accepted`

---

### Step 3 — PowerShell 7.2 or earlier (no `-HttpVersion`): use `HttpClient` directly

```powershell
$client = [System.Net.Http.HttpClient]::new()
$client.DefaultRequestVersion = [System.Version]::new(2, 0)
$client.DefaultVersionPolicy  = [System.Net.Http.HttpVersionPolicy]::RequestVersionExact

$body = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/transfer/1.0/request"
    from = "did:test:sender"
    to   = @("did:drn:alpha.svrn7.net")
    body = "{}"
} | ConvertTo-Json

$content = [System.Net.Http.StringContent]::new(
    $body,
    [System.Text.Encoding]::UTF8,
    "application/didcomm-plain+json"
)

$response = $client.PostAsync("http://localhost:8443/didcomm", $content).GetAwaiter().GetResult()
"Status: $($response.StatusCode)"
$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
```

---

## Available Protocol URIs

| `type` URI | LOBE Handler |
|---|---|
| `did:drn:svrn7.net/protocols/transfer/1.0/request` | `Invoke-Svrn7IncomingTransfer` |
| `did:drn:svrn7.net/protocols/transfer/1.0/order` | `Invoke-Svrn7IncomingTransfer` |
| `did:drn:svrn7.net/protocols/transfer/1.0/order-receipt` | `Confirm-Svrn7Settlement` |
| `did:drn:svrn7.net/protocols/onboard/1.0/request` | `Register-Svrn7CitizenInSociety` |

Any other `type` value is enqueued (202) but the Switchboard will log an unroutable message — visible at `LogLevel.Trace`.

---

## Response Codes

| Code | Meaning |
|---|---|
| `202 Accepted` | Message unpacked and enqueued successfully |
| `400 Bad Request` | Empty body, invalid JSON, or DIDComm unpack failed |

---

## Tracing PowerShell Cmdlet Execution

### What is logged and where

At `LogLevel.Information`, the Switchboard logs the cmdlet name and LOBE when it dispatches a message:

```
Switchboard: routing {Did} (type={Type}) → {EP} [{LOBE}]
```

At `LogLevel.Trace`, `InvokeCmdletPipelineAsync` in `DIDCommMessageSwitchboard.cs` additionally logs
cmdlet start, completion, and all PowerShell streams forwarded to the .NET logger:

```
[Trace] PS invoke: Invoke-Svrn7IncomingTransfer -MessageDid did:tda:...
[Trace]   [PS Verbose] ...   ← Write-Verbose inside the .psm1
[Debug]   [PS Debug] ...     ← Write-Debug
[Info]    [PS Info] ...      ← Write-Information / Write-Host
[Warn]    [PS Warning] ...   ← Write-Warning
[Trace] PS complete: Invoke-Svrn7IncomingTransfer → 1 result(s).
```

PS stream forwarding happens after `ps.Invoke()` returns — output is in order but not
streaming line-by-line during execution. For live output from long-running cmdlets, wire
`ps.Streams.Verbose.DataAdded` events in `InvokeCmdletPipelineAsync`.

---

## Scenario A — Create first Society "bindloss"

The Society is not created via a DIDComm message. It is initialised at TDA startup via
configuration. Follow these steps to bring up a fresh TDA for the Bindloss Society.

### A.1 — Configure `appsettings.json`

Create or edit `appsettings.json` beside the `Svrn7.TDA` binary:

```json
{
  "Svrn7": {
    "SocietyDid":  "did:drn:bindloss.svrn7.net",
    "FederationDid": "did:drn:foundation.svrn7.net",
    "DbPath":      "./data/svrn7.db",
    "DidsDbPath":  "./data/svrn7-dids.db",
    "VcsDbPath":   "./data/svrn7-vcs.db",
    "InboxDbPath": "./data/svrn7-inbox.db",
    "SchemasDbPath": "./data/svrn7-schemas.db"
  },
  "Tda": {
    "SocietyDid":  "did:drn:bindloss.svrn7.net",
    "ListenPort":  8443,
    "RequireMutualTls": false,
    "AcceptSelfSigned": true,
    "LobesConfigPath": "./lobes/lobes.config.json"
  }
}
```

Or set environment variables instead (`.NET` colon → `__` in env):

```powershell
$env:Svrn7__SocietyDid   = "did:drn:bindloss.svrn7.net"
$env:Svrn7__FederationDid = "did:drn:foundation.svrn7.net"
$env:Tda__SocietyDid     = "did:drn:bindloss.svrn7.net"
$env:Tda__ListenPort     = "8443"
$env:Tda__RequireMutualTls    = "false"
$env:Tda__AcceptSelfSigned    = "true"
```

### A.2 — First-run startup

```powershell
dotnet run --project src/Svrn7.TDA
```

On first run the TDA:
1. Opens (or creates) the five LiteDB files under `./data/`.
2. Registers the `Svrn7SocietyOptions` with `SocietyDid = did:drn:bindloss.svrn7.net`.
3. Loads eager LOBEs: `Svrn7.Common`, `Svrn7.Federation`, `Svrn7.Society`, `Svrn7.UX`.
4. Starts the Switchboard drain loop and Kestrel on port 8443.

Expected console lines (LogLevel.Information):

```
info: DIDCommMessageSwitchboard[0]
      DIDCommMessageSwitchboard: drain loop started.
info: KestrelListenerService[0]
      TDA Kestrel listener started on port 8443 (h2c).
```

### A.3 — Verify the Society is running (PowerShell module)

In a separate PowerShell 7 session, import the Society module and query the running instance:

```powershell
Import-Module ./src/Svrn7.TDA/lobes/Svrn7.Federation.psm1
Import-Module ./src/Svrn7.TDA/lobes/Svrn7.Society.psm1

Initialize-Svrn7Federation

Connect-Svrn7Society `
    -SocietyDid     "did:drn:bindloss.svrn7.net" `
    -FederationDid  "did:drn:foundation.svrn7.net" `
    -DidMethodNames @("bindloss") `
    -DbPath         "./data"

Get-Svrn7OwnSociety | Select-Object SocietyDid, CurrentEpoch
```

Expected output:

```
SocietyDid                   CurrentEpoch
----------                   ------------
did:drn:bindloss.svrn7.net   0
```

### A.4 — Check overdraft baseline

```powershell
Get-Svrn7OverdraftStatus
```

Expected (fresh Society, no registrations yet):

```
SocietyDid                   Status
----------                   ------
did:drn:bindloss.svrn7.net   Clean
```

---

## Scenario B — Register first citizen "mwherman" via DIDComm

Citizen registration is driven by the `onboard/1.0/request` DIDComm protocol.
The Switchboard routes the inbound message to Agent 2 (Onboarding LOBE), which calls
`Register-Svrn7CitizenInSociety` and returns an `onboard/1.0/receipt`.

### B.1 — Generate key material for "mwherman"

Run once and save the output. The private key must be stored securely by the citizen's
own TDA — the Society stores only the public key.

```powershell
# Import the module (loads assemblies; no database opened)
Import-Module ./src/Svrn7.TDA/lobes/Svrn7.Federation.psm1

# Generate secp256k1 signing key pair — no driver or database needed
$kp  = New-Svrn7KeyPair

# Derive citizen DID under the "bindloss" method
$did = New-Svrn7Did -KeyPair $kp -MethodName "bindloss"

Write-Host "Citizen DID : $($did.Did)"
Write-Host "Public key  : $($kp.PublicKeyHex)"
Write-Host "Private key : $($kp.PrivateKeyHex)   <-- store this securely, never share"
```

Example output (your values will differ — keys are randomly generated):

```
Citizen DID : did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
Public key  : 0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798
Private key : <32-byte hex — keep secret>
```

### B.2 — Send the onboarding request via DIDComm (PowerShell 7.3+)

Replace `$citizenDid` and `$publicKeyHex` with the values from step B.1.

The `body` field is a JSON-string (stringified) — the plaintext unpack branch passes it
through to `PackedPayload` which `ConvertFrom-Web7OnboardRequest` parses with
`ConvertFrom-Json`.

```powershell
$citizenDid   = "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy"
$publicKeyHex = "0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798"

$body = @{
    citizenDid   = $citizenDid
    publicKeyHex = $publicKeyHex
    displayName  = "mwherman"
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/onboard/1.0/request"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
```

Expected: `202 Accepted`

### B.3 — PowerShell 7.2 or earlier (no `-HttpVersion`): use `HttpClient` directly

```powershell
$citizenDid   = "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy"
$publicKeyHex = "0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798"

$body = @{
    citizenDid   = $citizenDid
    publicKeyHex = $publicKeyHex
    displayName  = "mwherman"
} | ConvertTo-Json -Compress

$msgJson = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/onboard/1.0/request"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

$client = [System.Net.Http.HttpClient]::new()
$client.DefaultRequestVersion = [System.Version]::new(2, 0)
$client.DefaultVersionPolicy  = [System.Net.Http.HttpVersionPolicy]::RequestVersionExact

$content = [System.Net.Http.StringContent]::new(
    $msgJson,
    [System.Text.Encoding]::UTF8,
    "application/didcomm-plain+json"
)

$response = $client.PostAsync("http://localhost:8443/didcomm", $content).GetAwaiter().GetResult()
"Status: $($response.StatusCode)"
$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
```

### B.4 — Verify registration in the TDA log

With `LogLevel.Trace`, look for the Agent 2 pipeline output:

```
[Info]  Switchboard: routing did:drn:bindloss.svrn7.net/inbox/msg/<id>
        (type=did:drn:svrn7.net/protocols/onboard/1.0/request) → ConvertFrom-Web7OnboardRequest [Svrn7.Onboarding]
[Trace] PS invoke: Agent2-Onboarding.ps1 -MessageDid did:drn:...
[Trace]   [PS Verbose] Agent 2 / Onboarding: processing did:drn:...
[Trace]   [PS Verbose] Agent 2 / Onboarding: registering citizen did:bindloss:3J98...
[Info]    [PS Info] Agent 2 / Onboarding: citizen did:bindloss:3J98... registered. Endowment: 1000000000 grana.
[Trace] PS complete: Agent2-Onboarding.ps1 → 1 result(s).
```

### B.5 — Verify registration via PowerShell module

```powershell
# Re-use the Connect-Svrn7Society session from Scenario A.3

Test-Svrn7SocietyMember -Did "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy"
```

Expected:

```
Did        : did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
SocietyDid : did:drn:bindloss.svrn7.net
IsMember   : True
```

```powershell
Get-Svrn7SocietyMembers | Select-Object MemberCount, MemberDids
```

Expected after first registration:

```
MemberCount MemberDids
----------- ----------
          1 {did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy}
```

### B.6 — Error: duplicate registration

Sending the same `onboard/1.0/request` a second time (same `citizenDid`) results in
a `202 Accepted` at the HTTP layer (the Switchboard always enqueues), but Agent 2 will
log an error and return an `onboard/1.0/receipt` with `success: false`:

```
[Error] Agent 2 / Onboarding: failed for did:drn:.../inbox/msg/<id> — CitizenAlreadyRegisteredException
```

This is expected. `Register-Svrn7CitizenInSociety` is not idempotent.

---

## Scenario D — Pure PowerShell cmdlet workflow (no TDA running)

The PS modules call the same C# ISvrn7SocietyDriver / ISvrn7Driver stack that the TDA
uses internally. Nothing touches LiteDB directly — all operations go through the driver.
This path is useful for scripted provisioning, one-off admin tasks, and debugging without
having to send DIDComm messages over HTTP/2.

> **Can the TDA be running at the same time?**
>
> It depends on `DbPath`:
>
> | Scenario D `DbPath` | TDA `DbPath` | Result |
> |---|---|---|
> | Same as TDA (e.g. `./data`) | `./data` | **Fails.** LiteDB holds an exclusive write lock for the lifetime of the process. `Connect-Svrn7Society` throws `LiteException` immediately. |
> | Different (e.g. `./data-ps`) | `./data` | **Works, but isolated.** Two completely separate databases — no shared state. Registrations made in D are invisible to the running TDA. |
>
> Practical rules:
> - **Standalone provisioning:** run Scenario D with the TDA stopped, then start the TDA
>   pointing at the same `DbPath`. The TDA will pick up the pre-populated data.
> - **Inspecting a live TDA's data via PS:** stop the TDA first, then open its `DbPath`
>   with the PS module.
> - **Scenario D is not a live admin console** for a running TDA — use DIDComm messages
>   (Scenario B) for that.

### D.1 — Prerequisites

Build the solution so the compiled assemblies exist:

```powershell
dotnet build src/Svrn7.TDA/Svrn7.TDA.csproj
```

Set the assembly search path so `Initialize-Svrn7Assemblies` can find the DLLs.
The TDA build output folder contains all transitive assembly references:

```powershell
$env:SVRN7_BIN_PATH = Resolve-Path "src/Svrn7.TDA/bin/Debug/net8.0"
```

### D.2 — Import modules and initialise the drivers

```powershell
# Import order matters: Federation must be imported before Society
# (Society dot-sources Svrn7.Common.psm1 through its own copy, but the
#  assembly loader flag is set by Initialize-Svrn7Federation)
Import-Module ./src/Svrn7.TDA/lobes/Svrn7.Federation.psm1 -Force
Import-Module ./src/Svrn7.TDA/lobes/Svrn7.Society.psm1    -Force

# Load the Svrn7 assemblies and create the ISvrn7Driver singleton
Initialize-Svrn7Federation -DbPath "./data-ps" -DidMethodName "drn" -Verbose

# Create the ISvrn7SocietyDriver singleton (wraps the Federation driver)
Connect-Svrn7Society `
    -SocietyDid     "did:drn:bindloss.svrn7.net" `
    -FederationDid  "did:drn:foundation.svrn7.net" `
    -DidMethodNames @("bindloss") `
    -DbPath         "./data-ps"
```

Expected verbose output:

```
VERBOSE: Loaded: Svrn7.Core.dll
VERBOSE: Loaded: Svrn7.Crypto.dll
...
VERBOSE: Svrn7.Federation ready. DbRoot: ./data-ps  Method: drn
VERBOSE: Svrn7.Society connected: did:drn:bindloss.svrn7.net
```

### D.3 — Verify the Society record

```powershell
Get-Svrn7OwnSociety | Select-Object SocietyDid, CurrentEpoch
```

Expected:

```
SocietyDid                   CurrentEpoch
----------                   ------------
did:drn:bindloss.svrn7.net   0
```

```powershell
Get-Svrn7OverdraftStatus
```

Expected (no citizens registered yet):

```
SocietyDid                   Status
----------                   ------
did:drn:bindloss.svrn7.net   Clean
```

### D.4 — Generate key material for citizen "mwherman"

Run once. Save `$kp.PrivateKeyHex` — the Society stores only the public key.

```powershell
$kp  = New-Svrn7KeyPair
$did = New-Svrn7Did -KeyPair $kp -MethodName "bindloss"

"Citizen DID : $($did.Did)"
"Public key  : $($kp.PublicKeyHex)"
"Private key : $($kp.PrivateKeyHex)   <-- keep secret"
```

### D.5 — Register the citizen

`Register-Svrn7CitizenInSociety` calls
`ISvrn7SocietyDriver.RegisterCitizenInSocietyAsync()`, which:

1. Creates a `CitizenRecord` and `SocietyMembershipRecord` in LiteDB (via the C# driver).
2. Creates the citizen's wallet.
3. Transfers exactly 1,000 SVRN7 (1,000,000,000 grana) from the Society wallet as the endowment.
4. Issues a `Svrn7EndowmentCredential` VC.
5. Appends a `CitizenRegistration` entry to the Merkle audit log.

```powershell
$reg = Register-Svrn7CitizenInSociety -CitizenDid $did.Did -KeyPair $kp
$reg | Format-List
```

Expected:

```
CitizenDid     : did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
SocietyDid     : did:drn:bindloss.svrn7.net
EndowmentSvrn7 : 1000.000000
EndowmentGrana : 1000000000
MethodName     :
Success        : True
```

### D.6 — Verify membership and overdraft

```powershell
# Membership check
Test-Svrn7SocietyMember -Did $did.Did
```

Expected:

```
Did        : did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
SocietyDid : did:drn:bindloss.svrn7.net
IsMember   : True
```

```powershell
# Full member list
Get-Svrn7SocietyMembers | Select-Object MemberCount, MemberDids
```

Expected:

```
MemberCount MemberDids
----------- ----------
          1 {did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy}
```

```powershell
# Overdraft record — shows one draw event if Society wallet was initially empty
Get-Svrn7OverdraftRecord | Format-List
```

Expected (if the Society wallet had sufficient balance — no overdraft draw triggered):

```
SocietyDid            : did:drn:bindloss.svrn7.net
Status                : Clean
TotalOverdrawnGrana   : 0
LifetimeDrawsGrana    : 0
DrawCount             : 0
```

Expected (if the Society wallet was empty — overdraft draw was triggered automatically):

```
SocietyDid            : did:drn:bindloss.svrn7.net
Status                : Overdrawn
TotalOverdrawnGrana   : 1000000000000
LifetimeDrawsGrana    : 1000000000000
DrawCount             : 1
```

### D.7 — Register a second citizen (smoke test for bulk path)

```powershell
$kp2  = New-Svrn7KeyPair
$did2 = New-Svrn7Did -KeyPair $kp2 -MethodName "bindloss"
Register-Svrn7CitizenInSociety -CitizenDid $did2.Did -KeyPair $kp2 | Select-Object CitizenDid, Success
```

```powershell
# Confirm both are now members
Get-Svrn7SocietyMembers | Select-Object MemberCount
```

### D.8 — Add a secondary DID method and issue a secondary DID for "mwherman"

```powershell
# Register a second DID method name under this Society
Register-Svrn7SocietyDidMethod -MethodName "bindlossgov"

# Issue a secondary DID for mwherman under the new method
Add-Svrn7CitizenDid -CitizenPrimaryDid $did.Did -MethodName "bindlossgov"
```

Expected:

```
CitizenPrimaryDid : did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
SecondaryDid      : did:bindlossgov:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
MethodName        : bindlossgov
Success           : True
```

Note: both DIDs share the same identifier component (the base58-encoded public key) — the
method prefix is the only difference. Both resolve to the same `CitizenRecord`.

```powershell
# Confirm the secondary DID is also recognised as a member
Test-Svrn7SocietyMember -Did "did:bindlossgov:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy"
```

### D.9 — List all DID methods for this Society

```powershell
Get-Svrn7SocietyDidMethods | Format-Table MethodName, IsPrimary, Status -AutoSize
```

Expected:

```
MethodName   IsPrimary Status
----------   --------- ------
bindloss     True      Active
bindlossgov  False     Active
```

---

## Scenario E — Bootstrap federation, society, and citizen via DIDComm (TDA running)

This scenario starts from a completely empty database and walks through the full
bootstrap sequence using only DIDComm messages to the running TDA:

| Step | Protocol | Handler |
|------|----------|---------|
| E.0 | `federation/1.0/init` | `Invoke-Web7FederationInit` |
| E.1 | `federation/1.0/federation-query` | `Invoke-Web7FederationQuery` |
| E.2 | `federation/1.0/register-society` | `Invoke-Web7RegisterSociety` |
| E.3 | *(client-side key generation)* | — |
| E.4 | `onboard/1.0/request` | `ConvertFrom-Web7OnboardRequest` |
| E.5–E.11 | `society/1.0/*` query/admin | see below |

> **How replies work:** the Switchboard executes the handler cmdlet, which resolves
> the sender's DIDComm endpoint from their DID document and returns an
> `OutboundMessage`. The Switchboard delivers it asynchronously via HTTP/2 to the
> sender's TDA. In dev/test the sender TDA may not be running — the outbound delivery
> will fail and be dead-lettered, but the operation itself succeeds and is visible in
> the TDA log.

Key material and DID generation are always client-side (E.2 and E.3 use the same key
generation as Scenario B steps B.1–B.2).

### E.0 — Initialise the Federation

Sent once, before any societies are registered. Idempotent — safe to repeat.

#### E.0.1 — Generate the federation governance key pair

This is a **one-time operation**. The private key must be stored in a key vault or HSM and
never placed in config files. The public key is recorded permanently in the federation record.

`New-Svrn7KeyPair` requires only the Svrn7 assemblies — no driver or database connection.
The TDA may be running or stopped when this step is executed.

```powershell
# Import the module (loads assemblies; no database opened)
Import-Module .\Svrn7.Federation.psm1

$federationKp = New-Svrn7KeyPair

Write-Host "Public key  : $($federationKp.PublicKeyHex)"
Write-Host "Private key : $($federationKp.PrivateKeyHex)   <-- store securely, never share"
```

Example output (your values will differ):

```
Public key  : 0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798
Private key : 18e14a7b5a...  <-- store securely, never share
```

#### E.0.2 — Send the federation/1.0/init DIDComm message

Replace `$federationKp.PublicKeyHex` with the value saved in E.0.1 on subsequent runs.

```powershell
$body = @{
    federationDid        = "did:drn:foundation.svrn7.net"
    federationName       = "SOVRONA Web 7.0 Foundation"
    publicKeyHex         = $federationKp.PublicKeyHex
    primaryDidMethodName = "drn"
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/federation/1.0/init"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
```

Expected TDA log:

```
[Info]  Switchboard: routing ... (type=did:drn:svrn7.net/protocols/federation/1.0/init)
        → Invoke-Web7FederationInit [Svrn7.Federation]
[Info]  Federation initialised: did:drn:foundation.svrn7.net (SOVRONA Web 7.0 Foundation), supply 1000000000000000000 grana
```

Reply body (`federation/1.0/init-result`):

```json
{
  "federationDid":        "did:drn:foundation.svrn7.net",
  "federationName":       "SOVRONA Web 7.0 Foundation",
  "primaryDidMethodName": "drn",
  "totalSupplyGrana":     1000000000000000000,
  "alreadyInitialised":   false,
  "initialisedAt":        "2026-04-15T..."
}
```

### E.1 — Query the Federation record

Verify the federation was initialised correctly. Also works before initialisation — returns `found: false`.

```powershell
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/federation/1.0/federation-query"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = "{}"
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
```

Expected TDA log:

```
[Info]  Switchboard: routing ... (type=did:drn:svrn7.net/protocols/federation/1.0/federation-query)
        → Invoke-Web7FederationQuery [Svrn7.Federation]
[Trace]   [PS Verbose] Invoke-Web7FederationQuery: replying to did:drn:foundation.svrn7.net
```

Reply body (`federation/1.0/federation-query-result`):

```json
{
  "found":                    true,
  "federationDid":            "did:drn:foundation.svrn7.net",
  "federationName":           "SOVRONA Web 7.0 Foundation",
  "primaryDidMethodName":     "drn",
  "totalSupplyGrana":         1000000000000000000,
  "endowmentPerSocietyGrana": 0,
  "currentEpoch":             0,
  "isActive":                 true,
  "createdAt":                "2026-04-15T...",
  "queriedAt":                "2026-04-15T..."
}
```

### E.2 — Register the "bindloss" Society

```powershell
# Reuse the society key pair generated in Scenario B (or generate a new one)
$societyKeyPair = New-Svrn7KeyPair

$body = @{
    societyDid           = "did:drn:bindloss.svrn7.net"
    publicKeyHex         = $societyKeyPair.PublicKeyHex
    societyName          = "Bindloss Alberta"
    primaryDidMethodName = "bindloss"
    drawAmountGrana      = 1000000000000     # 1 SVRN7
    overdraftCeilingGrana= 10000000000000    # 10 SVRN7
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/federation/1.0/register-society"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
```

Expected TDA log:

```
[Info]  Switchboard: routing ... (type=did:drn:svrn7.net/protocols/federation/1.0/register-society)
        → Invoke-Web7RegisterSociety [Svrn7.Federation]
[Warn]  RegisterSocietyAsync: FoundationPrivateKey not configured — VTC credential skipped for did:drn:bindloss.svrn7.net (development mode)
[Info]  Society registered: did:drn:bindloss.svrn7.net (Bindloss Alberta) method=bindloss
```

Reply body (`federation/1.0/register-society-result`):

```json
{
  "societyDid":            "did:drn:bindloss.svrn7.net",
  "societyName":           "Bindloss Alberta",
  "primaryDidMethodName":  "bindloss",
  "drawAmountGrana":       1000000000000,
  "overdraftCeilingGrana": 10000000000000,
  "success":               true,
  "registeredAt":          "2026-04-15T..."
}
```

### E.3 — Generate citizen key material (client-side)

This step is identical to Scenario B step B.1 — key generation never goes through the TDA.

```powershell
$citizenKeyPair = New-Svrn7KeyPair
$citizenDid     = New-Svrn7Did -KeyPair $citizenKeyPair -MethodName "bindloss"
# e.g. did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
```

### E.4 — Register citizen "mwherman" via onboard/1.0/request

```powershell
$body = @{
    citizenDid    = $citizenDid
    citizenName   = "mwherman"
    publicKeyHex  = $citizenKeyPair.PublicKeyHex
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/onboard/1.0/request"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
```

Expected TDA log:

```
[Info]  Switchboard: routing ... (type=did:drn:svrn7.net/protocols/onboard/1.0/request)
        → ConvertFrom-Web7OnboardRequest [Svrn7.Onboarding]
[Info]  Citizen registered: did:bindloss:3J98...
```

---

The steps below mirror Scenario D — the same operations performed via direct cmdlet calls,
now sent as DIDComm messages to the running TDA.

### E.5 — Query the Society record

```powershell
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/society/1.0/society-query"
    from = "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy"   # sender DID
    to   = @("did:drn:bindloss.svrn7.net")
    body = "{}"
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
```

Expected TDA log (LogLevel.Trace):

```
[Info]  Switchboard: routing ... (type=did:drn:svrn7.net/protocols/society/1.0/society-query)
        → Invoke-Web7SocietyQuery [Svrn7.Society]
[Trace]   [PS Verbose] Invoke-Web7SocietyQuery: replying to did:bindloss:3J98...
```

Reply body (delivered to the sender's TDA):

```json
{
  "societyDid":    "did:drn:bindloss.svrn7.net",
  "federationDid": "did:drn:foundation.svrn7.net",
  "currentEpoch":  0,
  "queriedAt":     "2026-04-15T..."
}
```

### E.6 — Test membership for a specific DID

```powershell
$citizenDid = "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy"

$body = @{ did = $citizenDid } | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/society/1.0/member-query"
    from = $citizenDid
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
```

Reply body:

```json
{ "societyDid": "did:drn:bindloss.svrn7.net", "did": "did:bindloss:3J98...", "isMember": true }
```

### E.7 — List all members

Send the same `member-query` with an empty body (`"{}"`):

```powershell
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/society/1.0/member-query"
    from = "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy"
    to   = @("did:drn:bindloss.svrn7.net")
    body = "{}"
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
```

Reply body:

```json
{ "societyDid": "did:drn:bindloss.svrn7.net", "memberCount": 1, "memberDids": ["did:bindloss:3J98..."] }
```

### E.8 — Query overdraft status

```powershell
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/society/1.0/overdraft-query"
    from = "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy"
    to   = @("did:drn:bindloss.svrn7.net")
    body = "{}"
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
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

### E.9 — Register a secondary DID method

```powershell
$body = @{ methodName = "bindlossgov" } | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/society/1.0/did-method-register"
    from = "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy"
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
```

Reply body:

```json
{ "societyDid": "did:drn:bindloss.svrn7.net", "methodName": "bindlossgov", "status": "Active", "success": true }
```

### E.10 — List DID methods

```powershell
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/society/1.0/did-methods-query"
    from = "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy"
    to   = @("did:drn:bindloss.svrn7.net")
    body = "{}"
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
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

### E.11 — Add a secondary DID for "mwherman"

```powershell
$body = @{
    citizenPrimaryDid = "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy"
    methodName        = "bindlossgov"
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/society/1.0/citizen-did-add"
    from = "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy"
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8443/didcomm" `
    -Body $msg `
    -ContentType "application/didcomm-plain+json" `
    -HttpVersion 2.0
```

Reply body:

```json
{
  "citizenPrimaryDid": "did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy",
  "secondaryDid":      "did:bindlossgov:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy",
  "methodName":        "bindlossgov",
  "success":           true
}
```

---

## Scenario C — Common error conditions

| Symptom | Cause | Fix |
|---|---|---|
| `400 Bad Request` on POST /didcomm | Body is empty, not valid JSON, or missing `type` field at root | Add `"type"` key to the message root |
| `202` but log shows `No LOBE registered for @type` | `type` URI does not match any registered protocol | Check `lobes.config.json` and the `.lobe.json` protocol URIs |
| `202` but log shows `CitizenAlreadyRegisteredException` | Citizen DID already registered | Expected — use a new key pair and DID |
| `202` but log shows `SocietyEndowmentDepletedException` | Society overdraft ceiling reached | Check `Get-Svrn7OverdraftStatus`; await Federation top-up |
| Agent 2 log: `No DIDComm service endpoint for <DID>` | Citizen DID document has no `DIDComm` service entry | Register the citizen's DID document before sending the receipt |
| Switchboard epoch gate log warning | `type` URI requires a higher epoch than `CurrentEpoch` | Only transfer/order URIs are epoch-gated (require Epoch 1+) |
| `202` but log shows `unknown message type 'application/didcomm-encrypted+json'` | Encrypted JWE was sent — `UnpackAsync` does not decrypt JWE; stores raw ciphertext with wrong type | Use plaintext messages for dev/test (`typ = "application/didcomm-plain+json"`, no `protected_` wrapper) |

---

## Scenario F — Test teardown: Remove all LiteDB databases

`Remove-Svrn7Databases` deletes all five LiteDB files and their companion journal files.
**Stop the TDA host before running this** — LiteDB holds an exclusive file lock while the process is running.

### F.1 — Interactive teardown (prompts for confirmation)

```powershell
# Defaults match the Svrn7Options/SocietyOptions path defaults.
# PowerShell will display a High-impact confirmation prompt.
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

Expected output (one row per candidate path):

```
Path                      Existed Removed
----                      ------- -------
data/svrn7.db             True    True
data/svrn7.db-log         False   False
data/svrn7-dids.db        True    True
data/svrn7-dids.db-log    False   False
data/svrn7-vcs.db         True    True
data/svrn7-vcs.db-log     False   False
data/svrn7-inbox.db       True    True
data/svrn7-inbox.db-log   False   False
data/svrn7-schemas.db     True    True
data/svrn7-schemas.db-log False   False
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

# 2. Start TDA host (separate process or background job)
Start-Process dotnet -ArgumentList 'run --project src/Svrn7.TDA' -NoNewWindow

# 3. Run test scenarios A/B/D/E ...

# 4. Tear down
Remove-Svrn7Databases -Confirm:$false
```

### Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-Svrn7DbPath` | `data/svrn7.db` | Main wallet / UTXO / Merkle log database |
| `-DidsDbPath` | `data/svrn7-dids.db` | DID Document registry |
| `-VcsDbPath` | `data/svrn7-vcs.db` | Verifiable Credential registry |
| `-InboxDbPath` | `data/svrn7-inbox.db` | DIDComm inbox, outbox, processed orders |
| `-SchemasDbPath` | `data/svrn7-schemas.db` | JSON Schema 2020-12 registry |

Each path also generates a `{path}-log` candidate (LiteDB 5 journal file). If present it is removed automatically.
