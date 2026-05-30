# Pando.Diagnostics LOBE — Debug & Testing Guide

This guide covers building, running, and testing the `Pando.Diagnostics` example LOBE
end-to-end against a live TDA.  It is self-contained from the point of starting the TDA
in debug mode.  See `docs/DEBUG.md` for general TDA background and additional scenarios.

---

## Prerequisites

- PowerShell 7 (`pwsh.exe`) is required.  The VS Developer PowerShell defaults to
  Windows PowerShell 5.1, which is missing required .NET types.

  Verify before running any command:
  ```powershell
  $PSVersionTable.PSVersion   # Major must be 7
  ```

  To open a PowerShell 7 terminal from VS 2022/2026:
  **Tools → Options → Environment → Terminal → Add** — set shell to
  `C:\Program Files\PowerShell\7\pwsh.exe`.

---

## Step 1 — Build

From the repo root in PowerShell 7:

```powershell
Set-Location C:/SVRN7/repos/SVRN7
dotnet build src/Svrn7.TDA/Svrn7.TDA.csproj
```

Verify the three LOBE files were copied to the output:

```powershell
Set-Location src/Svrn7.TDA/bin/Debug/net8.0
Get-ChildItem lobes/Pando.Diagnostics
```

Expected:

```
Pando.Diagnostics.Impl.psm1
Pando.Diagnostics.lobe.json
Pando.Diagnostics.psm1
```

Verify `Pando.Diagnostics` is in the JIT list:

```powershell
Get-Content lobes/lobes.config.json | Select-String "Pando"
```

Expected:

```
    "Pando.Diagnostics/Pando.Diagnostics.psm1"
```

---

## Step 2 — Start the TDA

In the TDA output folder (`src/Svrn7.TDA/bin/Debug/net8.0`):

```powershell
dotnet .\Svrn7.TDA.dll
```

Expected startup log:

```
info: DIDCommMessageSwitchboard[0]
      DIDCommMessageSwitchboard: drain loop started.
info: KestrelListenerService[0]
      TDA Kestrel listener started on port 8443 (h2c).
```

`Pando.Diagnostics` is a JIT LOBE — it is **not** loaded at startup.  It is imported
into the runspace the first time a `diagnostics/1.0/date-query` message arrives.

---

## Step 3 — Load the send helper

> **Session note:** This step only needs to be done once per PowerShell session.
> If your PowerShell terminal is still open from a previous run and the module is
> already imported, skip this step and go straight to Step 5.

In a separate PowerShell 7 terminal, navigate to the TDA output folder and import the
Federation module to get `Send-DIDCommMessage`:

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
Import-Module .\lobes\Svrn7.Federation\Svrn7.Federation.psm1
```

---

## Step 4 — Bootstrap (first run only)

> **Persistence note:** The LiteDB databases survive TDA restarts — you only need to
> run this step once, or again after a full reset (`Remove-Item mem\svrn7.db ...`).
> The federation and society records created here persist until the databases are
> explicitly deleted.  The only thing that does **not** persist between sessions is
> the `Import-Module` call in your PowerShell send terminal — repeat Step 3 if you
> started a new PowerShell session.  If the terminal is still open, skip it.

Skip this step if the TDA database already has a federation and society record.

Open a **second** PowerShell 7 terminal, navigate to the output folder, and run the
bootstrap sequence.  (The TDA must be running in the first terminal throughout.)

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
Import-Module .\lobes\Svrn7.Federation\Svrn7.Federation.psm1
```

### 4.1 — Generate the federation key pair (one-time)

```powershell
$federationKp = New-Svrn7KeyPair
Write-Host "Public key : $($federationKp.PublicKeyHex)"
```

### 4.2 — Initialise the federation

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
    type = "did:drn:svrn7.net/protocols/federation/1.0/init"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log: `Federation initialised: did:drn:foundation.svrn7.net`

### 4.3 — Register the society

```powershell
$societyKp = New-Svrn7KeyPair

$body = @{
    societyDid           = "did:drn:bindloss.svrn7.net"
    publicKeyHex         = $societyKp.PublicKeyHex
    societyName          = "Bindloss Alberta"
    primaryDidMethodName = "bindloss"
    drawAmountGrana      = 1000000000000
    overdraftCeilingGrana= 10000000000000
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/federation/1.0/register-society"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log: `Society registered: did:drn:bindloss.svrn7.net (Bindloss Alberta)`

### 4.4 — List all societies in the federation

While the TDA is running, send a `federation/1.0/society-list` message.
Include `replyEndpoint` so the result is delivered back to the local TDA.

```powershell
Import-Module .\lobes\Svrn7.Federation\Svrn7.Federation.psm1

$body = @{
    replyEndpoint = "http://localhost:8443"
} | ConvertTo-Json -Compress

$body = "{}"

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/federation/1.0/society-list"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log:

```
info:  Switchboard: routing ... (type=.../federation/1.0/society-list) → Invoke-Web7SocietyList [Svrn7.Federation]
info:    [PS Info] Invoke-Web7SocietyList: 1 society/societies, replying to http://localhost:8443
info:    Switchboard: outbound delivered to http://localhost:8443/didcomm (202).
```

Expected `society-list-result` reply body:

```json
{
  "count": 1,
  "activeCount": 1,
  "societies": [
    {
      "societyDid":           "did:drn:bindloss.svrn7.net",
      "societyName":          "Bindloss Alberta",
      "primaryDidMethodName": "bindloss",
      "isActive":             true,
      "registeredAt":         "2026-05-30T..."
    }
  ],
  "queriedAt": "2026-05-30T..."
}
```

---

## Step 5 — Send a date-query message (no reply)

The simplest test — no `replyEndpoint`, so the handler runs and logs the server time
but does not attempt outbound delivery.

```powershell
$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/diagnostics/1.0/date-query"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = "{}"
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected response: `Status: Accepted`

Expected TDA log:

```
info:  Switchboard: routing did:drn:bindloss.svrn7.net/inbox/msg/<id>
           (type=did:drn:svrn7.net/protocols/diagnostics/1.0/date-query)
           → Invoke-PandoDiagnosticsDateQuery [Pando.Diagnostics]
info:    [PS Info] Pando.Diagnostics: serverUtc=2026-05-30T... epoch=0
warn:    [PS Warning] Invoke-PandoDiagnosticsDateQuery: no reply endpoint — result not delivered.
```

The `[PS Warning]` line confirms the handler ran successfully — it is expected when no
`replyEndpoint` is provided and the sender DID has no DID Document registered.

---

## Step 6 — Send a date-query message (with reply endpoint)

Include `replyEndpoint` pointing at the local TDA to exercise the full reply path.
The outbound delivery will be attempted and logged before any delivery failure.

```powershell
$body = @{
    replyEndpoint = "http://localhost:8443"
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/diagnostics/1.0/date-query"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log additions:

```
info:    [PS Info] Pando.Diagnostics: serverUtc=2026-05-30T... epoch=0
info:    Switchboard: outbound delivered to http://localhost:8443/didcomm (202).
```

Expected `date-result` reply body:

```json
{
  "serverUtc":       "2026-05-30T...",
  "serverUtcOffset": "+00:00",
  "currentEpoch":    0,
  "respondedAt":     "2026-05-30T..."
}
```

---

## Step 7 — Verify Get-TDADate standalone (no TDA required)

`Pando.Diagnostics.Impl.psm1` can be imported and tested in isolation — no TDA,
no assemblies, no database needed.

```powershell
Import-Module .\lobes\Pando.Diagnostics\Pando.Diagnostics.Impl.psm1

$now = Get-TDADate
Write-Host "Server time : $($now.ToString('o'))"
Write-Host "Type        : $($now.GetType().FullName)"
```

Expected:

```
Server time : 2026-05-30T...+00:00
Type        : System.DateTimeOffset
```

---

## Step 8 — Reset between test runs

Stop the TDA, delete the inbox database to clear accumulated test messages, then restart:

```powershell
# (Stop the TDA first — Ctrl+C in the TDA terminal)
Remove-Item -Path "mem\svrn7-inbox.db", "mem\svrn7-inbox-log.db" -ErrorAction SilentlyContinue
dotnet .\Svrn7.TDA.dll
```

For a full reset (clears federation and society records too):

```powershell
Remove-Item -Path "mem\*.db" -ErrorAction SilentlyContinue
dotnet .\Svrn7.TDA.dll
```

After a full reset, repeat Step 4 before testing the LOBE.

---

## Common Error Conditions

| Symptom | Cause | Fix |
|---|---|---|
| `No LOBE registered for @type .../diagnostics/1.0/date-query` | `Pando.Diagnostics` missing from `lobes.config.json` or files not in output | Verify Step 1 |
| `The term 'Get-TDADate' is not recognized` | `Pando.Diagnostics.Impl.psm1` not found at `$PSScriptRoot` | Verify all three files are in `lobes/Pando.Diagnostics/` in the output folder |
| `Invoke-PandoDiagnosticsDateQuery: message '...' not found.` | Message expired from cache before handler ran | Retry; check inbox store |
| `[PS Warning] no reply endpoint — result not delivered.` | Expected when `replyEndpoint` absent and sender DID has no DID Document | Normal for Step 5 (no-reply variant) |
| `serverUtc` and `respondedAt` are identical | Normal — both calls run within microseconds | Not an error |
