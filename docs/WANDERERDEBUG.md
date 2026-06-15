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
Start-Process cmd.exe -ArgumentList '/k title W5 [Wanderer]:8445 && dotnet ".\Svrn7.TDA.dll" --port 8445 --name W5'
Start-Process cmd.exe -ArgumentList '/k title W6 [Wanderer]:8446 && dotnet ".\Svrn7.TDA.dll" --port 8446 --name W6'
```

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
  Agent DID   : did:drn:wanderer.testnet.svrn7.net/agent/1.0/<guid-W5>
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

W5 and W6 each generate a unique GUID-based DID on first run.  Read both from their
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
W5 DID: did:drn:wanderer.testnet.svrn7.net/agent/1.0/<guid-W5>
W6 DID: did:drn:wanderer.testnet.svrn7.net/agent/1.0/<guid-W6>
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

Post the message directly to W6's endpoint.  Include `replyEndpoint` pointing at W5 so
W6 knows where to deliver the `Issue-TOD` reply.

```powershell
$body = @{ replyEndpoint = 'http://localhost:8445/didcomm' } | ConvertTo-Json -Compress

$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/Pando.Diagnostics.0.1.0/Query-TOD'
    from = $w5Did
    to   = @($w6Did)
    body = $body
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
          from='did:drn:wanderer.testnet.svrn7.net/agent/1.0/<guid-W6>'
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
| No `Issue-TOD` delivered to W5 | `replyEndpoint` missing or wrong port | Confirm body includes `"replyEndpoint":"http://localhost:8445/didcomm"` |
| W5 log shows no `Issue-TOD` routing line | W5 Kestrel not yet listening | Ensure W5 started and shows `KestrelListenerService started on port 8445` |
| `agent-identity.json not found` | W5/W6 not yet started, or `Set-Location` is wrong | Verify the TDA output dir is the CWD and the TDA ran at least once |
| W6 logs `no reply endpoint — result not delivered` | `replyEndpoint` key absent from body | Use `-Compress` on the inner `ConvertTo-Json` so the key is present |
