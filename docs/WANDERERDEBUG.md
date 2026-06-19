# SVRN7 — Wanderer TDA Debug Guide

Covers launching two Wanderer TDA instances (W5 on port 8445, W6 on port 8446) and
simulating a `Query-TOD` / `Issue-TOD` round-trip between them using the
`Pando.Diagnostics` LOBE.

A Wanderer is the initial role of every TDA. On first run each instance auto-generates
a secp256k1 key pair, derives a GUID-based DID, creates a DID Document, and persists
key material to `{port}/mem/agent-identity.json`. No federation or society registration
is required.

---

## Prerequisites

- PowerShell 7 (`pwsh.exe`).

  ```powershell
  $PSVersionTable.PSVersion   # Major must be 7
  ```

- The solution must be built before starting:

  ```powershell
  Set-Location C:/SVRN7/repos/SVRN7
  dotnet build src/Svrn7.TDA/Svrn7.TDA.csproj
  ```

- Verify `Pando.Diagnostics` is in the JIT list:

  ```powershell
  Set-Location src/Svrn7.TDA/bin/Debug/net8.0
  Get-Content lobes/lobes.config.json | Select-String "Pando"
  ```

  Expected:

  ```
      "Pando.Diagnostics.0.1.0/Pando.Diagnostics.0.1.0.psm1"
  ```

---

## Terminal layout

Three PowerShell 7 terminals are needed throughout this guide.

| Terminal | Purpose |
|----------|---------|
| **A — W5** | Runs the W5 TDA process on port 8445; watch log output here |
| **B — W6** | Runs the W6 TDA process on port 8446; watch log output here |
| **C — Sender** | Sends DIDComm messages; reads identity files |

---

## Step 1 — Start W5 and W6 (Terminals A and B)

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
Start-Process cmd.exe -ArgumentList '/k title W5 [Wanderer]:8445 && dotnet ".\Svrn7.TDA.dll" --port 8445 --name W5 --reset'
Start-Process cmd.exe -ArgumentList '/k title W6 [Wanderer]:8446 && dotnet ".\Svrn7.TDA.dll" --port 8446 --name W6 --reset'
```

> **Production / staging:** Add `--federationdomain svrn7.net` to auto-discover the
> Federation TDA endpoint via drn.directory DNS at startup.  The discovered URL is shown
> in the banner (`Fed Endpoint`) and exposed as `$SVRN7.FederationEndpointUrl` in every
> LOBE runspace.  Omit for standalone dev runs with no live drn.directory DNS record.

W5 has no prior databases — this is a first run.  Expected startup banner:

```
────────────────────────────────────────────────────────────────────────────────
  SVRN7 Trusted Digital Assistant (TDA)  v0.8.0
  Web 7.0 Foundation — https://svrn7.net
────────────────────────────────────────────────────────────────────────────────
  ...
  TDA Name    : W5
  First run   : yes — Wanderer identity created
  Role        : Wanderer
  Agent DID   : did:drn:wanderer.svrn7.net/agent/1.0/<genesis-hash-W5>
  Listen port : 8445
  LOBEs       : 4 eager  8 JIT  (N protocols  N cmdlets)
    Eager     : Svrn7.Common  Svrn7.Federation  Svrn7.Society  Svrn7.UX
    JIT       : ...  Pando.Diagnostics  ...
────────────────────────────────────────────────────────────────────────────────
  Federation  : (not yet initialised ...)
  Societies   : (not yet initialised ...)
────────────────────────────────────────────────────────────────────────────────
```

Note the `Agent DID` line — this is W5's Wanderer identity.  It is also written to:

```
8445/mem/agent-identity.json
```

---

## Step 3 — Read the Wanderer DIDs (Terminal C)

W5 and W6 each generate a unique public-key-derived DID on first run.  Read both from their
identity files:

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0

$w5Did = (Get-Content 8445/mem/agent-identity.json | ConvertFrom-Json).did
$w6Did = (Get-Content 8446/mem/agent-identity.json | ConvertFrom-Json).did

Write-Host "W5 DID: $w5Did"
Write-Host "W6 DID: $w6Did"
```

Expected:

```
W5 DID: did:drn:wanderer.svrn7.net/agent/1.0/<genesis-hash-W5>
W6 DID: did:drn:wanderer.svrn7.net/agent/1.0/<genesis-hash-W6>
```

---

## Step 4 — Import the send helper (Terminal C)

> Do this once per PowerShell session.

```powershell
Import-Module .\lobes\Svrn7.Federation.0.8.0\Svrn7.Federation.0.8.0.psm1
```

This gives you `Send-DIDCommMessage` for the steps below.

---

## Step 5 — Send Query-TOD from W5 to W6 (Terminal C)

Post the message directly to W6's endpoint.  W6 resolves the reply endpoint from W5's
DID Document and delivers the `Issue-TOD` reply automatically.

```powershell
$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/Pando.Diagnostics.0.1.0/Query-TOD'
    from = $w5Did
    to   = @($w6Did)
    body = '{}'
} | ConvertTo-Json

Send-DIDCommMessage -Uri 'http://localhost:8446/didcomm' -Body $msg
```

Expected response from Terminal C:

```
Status: Accepted
```

---

## Step 6 — Verify W6 received and replied (Terminal B)

Watch Terminal B for W6's log.  `Pando.Diagnostics` is a JIT LOBE — `Import-Module -Force`
runs on every dispatch (by design, for hot-update support).

```
dbug: Svrn7.TDA.LobeManager[0]
      LobeManager: EnsureLoadedAsync — JIT '...\Pando.Diagnostics.0.1.0.psm1'.

info: Svrn7.TDA.LobeManager[0]
      LobeManager: importing into isolated runspace (JIT) — ...\Pando.Diagnostics.0.1.0.psm1

info: Svrn7.TDA.LobeManager[0]
      LobeManager: import complete — ...\Pando.Diagnostics.0.1.0.psm1

info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      [PS Info] Pando.Diagnostics: serverUtc=2026-06-15T... epoch=0

info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: outbound delivered to http://localhost:8445/didcomm (202).
```

The last line confirms W6 delivered the `Issue-TOD` reply to W5.

---

## Step 7 — Verify W5 received the reply (Terminal A)

Watch Terminal A for W5's log.  W5 receives the `Issue-TOD` and routes it to
`Invoke-PandoDiagnosticsDateResult`:

```
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: routing ...\inbox\msg\<id>
          (type=did:drn:svrn7.net/protocols/Pando.Diagnostics.0.1.0/Issue-TOD)
          → Invoke-PandoDiagnosticsDateResult [Pando.Diagnostics]

info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      [PS Info] Invoke-PandoDiagnosticsDateResult: serverUtc=2026-06-15T... epoch=0
          from='did:drn:wanderer.svrn7.net/agent/1.0/<genesis-hash-W6>'
```

The `from` field shows W6's DID confirming the reply originated from W6.
`Issue-TOD` is a terminal message — W5 logs the result and sends no further reply.

---

## Step 8 — Send a second Query-TOD

Repeat Step 5.  The import lines **will appear again** — JIT LOBEs run
`Import-Module -Force` on every dispatch by design, so that an updated `.psm1` is
always picked up without a TDA restart (hot-update).  The ~30 ms reimport overhead
is tracked in the backlog as TDA-001a.

```
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: processing 1 inbound message(s).

info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: routing did:drn:solo.svrn7.net/inbox/msg/<id>
          (type=did:drn:svrn7.net/protocols/Pando.Diagnostics.0.1.0/Query-TOD)
          → Invoke-PandoDiagnosticsDateQuery [Pando.Diagnostics]

info: Svrn7.TDA.LobeManager[0]
      LobeManager: importing into isolated runspace (JIT) — ...\Pando.Diagnostics.0.1.0.psm1

info: Svrn7.TDA.LobeManager[0]
      LobeManager: import complete — ...\Pando.Diagnostics.0.1.0.psm1

info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      [PS Info] Pando.Diagnostics: serverUtc=2026-06-15T... epoch=0

info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: outbound delivered to http://localhost:8445/didcomm (202).
```

---

## Steps 10–14 — Register W5 with a Society (Wanderer → Citizen)

This section shows how a Wanderer TDA discovers available Societies from the Federation
and registers with one, becoming a Citizen TDA.  After step 14, W5's `agent-identity.json`
contains its parent Society DID and endpoint, and W5's local DID registry holds both the
Citizen and Society DID Documents.

**Prerequisites:**  A Federation TDA and at least one Society TDA must already be running
and bootstrapped.  Complete DEBUG.md Scenario E steps E.0–E.2 first (Federation init +
Society registration).  Simplest setup — in two new titled terminals:

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0

# Terminal D — Federation TDA on port 8441
Start-Process cmd.exe -ArgumentList '/k title Federation:8441 && dotnet ".\Svrn7.TDA.dll" --port 8441 --name Federation'

# Terminal E — Society TDA on port 8442
Start-Process cmd.exe -ArgumentList '/k title Society:8442 && dotnet ".\Svrn7.TDA.dll" --port 8442 --name Bindloss'
```

Then complete E.0 (initialize-federation) and E.2 (register-society) from DEBUG.md
before continuing here.  W5 on port 8445 must already be running from Step 1.

---

### Step 10 — Discover available Societies (Terminal C)

W5 only knows the Federation's endpoint.  It sends a `society-list` request and receives
back each Society's DID Document — which W5 stores locally so Phase 2 needs no further
network lookup.

> **Production note:** When W5 is started with `--federationdomain svrn7.net`, the
> Federation endpoint URL is discovered at startup and available inside any LOBE handler
> as `$SVRN7.FederationEndpointUrl`.  In standalone PowerShell (Terminal C), use
> `Resolve-FederationEndpoint -FederationDid "svrn7.net"` instead of the hardcoded
> `http://localhost:8441/didcomm` below.

```powershell
# Ensure the send helper is loaded (if not already from Step 4)
Import-Module .\lobes\Svrn7.Federation.0.8.0\Svrn7.Federation.0.8.0.psm1

$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/society-list'
    from = $w5Did
    to   = @('did:drn:foundation.svrn7.net')
    body = '{}'
} | ConvertTo-Json

Send-DIDCommMessage -Uri 'http://localhost:8441/didcomm' -Body $msg
```

Expected log — Terminal D (Federation TDA):

```
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: routing ... (type=.../Svrn7.Federation.0.8.0/society-list)
          → Invoke-Web7SocietyList [Svrn7.Federation]
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      [PS Info] Invoke-Web7SocietyList: 1 society/societies, replying to http://localhost:8445/didcomm
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: outbound delivered to http://localhost:8445/didcomm (202).
```

Expected log — Terminal A (W5):

```
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: routing ... (type=.../Svrn7.Federation.0.8.0/society-list-result)
          → Invoke-Web7SocietyListResult [Svrn7.Federation]
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      [PS Info] Invoke-Web7SocietyListResult: stored 1 society DID Document(s) from 1 result(s)
```

W5's local DID registry now contains the Society's DID Document.  Note the `endpointUrl`
from the result — that is the Society's DIDComm address used in Step 12.

---

### Step 11 — Generate Citizen key material (Terminal C)

The Citizen DID is derived from a secp256k1 key pair — distinct from the Wanderer GUID
DID.  Generate once and save the output.

```powershell
$citizenKp  = New-Svrn7KeyPair
$citizenDid = New-Svrn7Did -KeyPair $citizenKp -MethodName 'bindloss'

Write-Host "Citizen DID : $($citizenDid.Did)"
Write-Host "Public key  : $($citizenKp.PublicKeyHex)"
Write-Host "Private key : $($citizenKp.PrivateKeyHex)   <-- store securely"
```

Example output (values will differ):

```
Citizen DID : did:bindloss:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy
Public key  : 0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798
Private key : <32-byte hex — keep secret>
```

---

### Step 12 — Send register-citizen to the Society (Terminal C)

W5 sends its Citizen DID, public key, and — critically — `serviceEndpointUrl` so the
Society can create W5's DID Document with the correct DIDComm endpoint and deliver the
receipt back to W5.

```powershell
$body = @{
    citizenDid         = $citizenDid.Did
    publicKeyHex       = $citizenKp.PublicKeyHex
    displayName        = 'W5'
    serviceEndpointUrl = 'http://localhost:8445/didcomm'   # W5's endpoint
} | ConvertTo-Json -Compress

$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/register-citizen'
    from = $citizenDid.Did
    to   = @('did:drn:bindloss.svrn7.net')
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Uri 'http://localhost:8442/didcomm' -Body $msg
```

Expected log — Terminal E (Society TDA):

```
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: routing ... (type=.../Svrn7.Onboarding.0.8.0/register-citizen)
          → ConvertFrom-Web7OnboardRequest [Svrn7.Onboarding]
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      [PS Info] Onboarding LOBE: receipt for did:bindloss:3J98... — 1000000000 grana
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: outbound delivered to http://localhost:8445/didcomm (202).
```

---

### Step 13 — Verify W5 received the receipt (Terminal A)

The Society delivers `Svrn7.Onboarding.0.8.0/receipt` to W5.  `Invoke-Web7OnboardReceipt`
runs automatically and stores both DID Documents and wires the parent TDA:

```
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      Switchboard: routing ... (type=.../Svrn7.Onboarding.0.8.0/receipt)
          → Invoke-Web7OnboardReceipt [Svrn7.Onboarding]
info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
      [PS Info] Invoke-Web7OnboardReceipt: registered with did:drn:bindloss.svrn7.net at http://localhost:8442/didcomm
```

---

### Step 14 — Verify agent-identity.json (Terminal C)

Read W5's identity file to confirm the parent TDA wiring was persisted:

```powershell
Get-Content 8445/mem/agent-identity.json | ConvertFrom-Json | Select-Object did, parentTdaDid, parentTdaEndpointUrl
```

Expected:

```
did                  parentTdaDid                   parentTdaEndpointUrl
---                  ------------                   --------------------
did:drn:wanderer...  did:drn:bindloss.svrn7.net     http://localhost:8442/didcomm
```

W5 is now a Citizen TDA.  On the next restart it reads `parentTdaDid` and
`parentTdaEndpointUrl` from `agent-identity.json` automatically — no `appsettings.json`
entries needed.

---

## Step 9 — Reset between runs

Stop both TDAs (Ctrl+C in Terminal A and B), then delete their data directories:

```powershell
Remove-Item -Recurse -Force 8445/mem -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force 8446/mem -ErrorAction SilentlyContinue
```

Restart with `--reset` to let the TDA delete its own data on startup (equivalent):

```powershell
dotnet .\Svrn7.TDA.dll --port 8445 --name W5 --reset
dotnet .\Svrn7.TDA.dll --port 8446 --name W6 --reset
```

`--reset` deletes all files in `{port}/mem/` before startup, forcing a new first-run
Wanderer bootstrap with a fresh GUID-based DID.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `Status: ConnectionRefused` when posting to port 8446 | W6 not running or still starting | Wait for the `KestrelListenerService` started log line |
| No `Issue-TOD` delivered to W5 | W5's DID Document not yet registered on W6 | Ensure W5 has bootstrapped and published its DID Document before sending |
| W5 log shows no `Issue-TOD` routing line | W5 Kestrel not yet listening | Ensure W5 started and shows `KestrelListenerService started on port 8445` |
| `agent-identity.json not found` | W5/W6 not yet started, or `Set-Location` is wrong | Verify the TDA output dir is the CWD and the TDA ran at least once |
| W6 logs `cannot resolve endpoint for sender` | W5's DID not in W6's registry | Confirm federation registration completed before sending the query |
