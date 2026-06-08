# Svrn7.Email LOBE — Debug & Testing Guide

This guide covers building, running, and testing the `Svrn7.Email` LOBE end-to-end
against a live TDA.  It is self-contained from the point of starting the TDA in debug
mode.  See `docs/LOBEDEBUG.md` for the federation/society bootstrap sequence and
general TDA background.

---

## Prerequisites

- PowerShell 7 (`pwsh.exe`) is required.

  ```powershell
  $PSVersionTable.PSVersion   # Major must be 7
  ```

- The TDA database must already have a federation and society record.
  If starting fresh, complete Steps 1–4 of `docs/LOBEDEBUG.md` first.

---

## Step 1 — Build

From the repo root in PowerShell 7:

```powershell
Set-Location C:/SVRN7/repos/SVRN7
dotnet build src/Svrn7.TDA/Svrn7.TDA.csproj
```

Verify the Email LOBE files are in the output:

```powershell
Set-Location src/Svrn7.TDA/bin/Debug/net8.0
Get-ChildItem lobes/Svrn7.Email
```

Expected:

```
Svrn7.Email.lobe.json
Svrn7.Email.0.8.0.psm1
```

Verify `Svrn7.Email` is in the JIT list:

```powershell
Get-Content lobes/lobes.config.json | Select-String "Email"
```

Expected:

```
    "Svrn7.Email/Svrn7.Email.0.8.0.psm1"
```

---

## Step 2 — Start the TDA

In the TDA output folder (`src/Svrn7.TDA/bin/Debug/net8.0`):

```powershell
dotnet .\Svrn7.TDA.dll
```

`Svrn7.Email` is a **JIT LOBE** — it is not imported at startup.  It is loaded into
the runspace the first time an `email/1.0/*` message arrives.  Expect a one-time
import log line on first delivery.

---

## Step 3 — Load the send helper

In a separate PowerShell 7 terminal:

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
Import-Module .\lobes\Svrn7.Federation\Svrn7.Federation.0.8.0.psm1
```

This gives you `Send-DIDCommMessage` for all subsequent steps.

---

## Step 4 — Send a test email (no reply)

The inbound body carries two fields: `from` (authoritative sender DID) and
`rfc5322Body` (complete RFC 5322 message text).  The sender's DID must appear
in both the outer DIDComm envelope (`from`) and the inner body (`from`) — the
handler uses the body field as the canonical sender identity.

```powershell
$rfc5322 = @"
From: Web 7.0 Foundation <did:drn:foundation.svrn7.net>
To: Bindloss Alberta <did:drn:bindloss.svrn7.net>
Subject: Hello from the Foundation
Date: $(([datetime]::UtcNow).ToString('ddd, dd MMM yyyy HH:mm:ss')) +0000
MIME-Version: 1.0
Content-Type: text/plain; charset=utf-8

This is a test email sent via DIDComm.
No SMTP server was involved.
"@

$body = @{
    from        = "did:drn:foundation.svrn7.net"
    to          = "did:drn:bindloss.svrn7.net"
    rfc5322Body = $rfc5322
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/message"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected response: `Status: Accepted`

Expected TDA log (timestamps vary):

```
20:49:13.432 info: Svrn7.TDA.DIDCommMessageSwitchboard[0] Switchboard: routing did:drn:alpha.svrn7.net/inbox/msg/<id> (type=did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/message) → Dequeue-PandoEmail [Svrn7.Email]
20:49:13.441 dbug: Svrn7.TDA.LobeManager[0] LobeManager: EnsureLoadedAsync - JIT 'C:\...\lobes\Svrn7.Email\Svrn7.Email.0.8.0.psm1'.
20:49:13.512 info: Svrn7.TDA.LobeManager[0] LobeManager: import complete - ...\Svrn7.Email.0.8.0.psm1
20:49:13.518 dbug: Svrn7.TDA.DIDCommMessageSwitchboard[0]   [PS Verbose] Email LOBE: stored email from did:drn:foundation.svrn7.net — 'Hello from the Foundation'
20:49:13.519 dbug: Svrn7.Society.LiteInboxStore[0] Inbox: message did:drn:alpha.svrn7.net/inbox/msg/<id> marked Processed
```

The `[PS Verbose]` line confirms the handler parsed the RFC 5322 subject correctly.
The import log only appears once — subsequent emails reuse the loaded runspace.

---

## Step 5 — Send a second email (JIT already loaded)

Send the same message again to confirm the import delay is gone:

```powershell
$body = @{
    from        = "did:drn:foundation.svrn7.net"
    to          = "did:drn:bindloss.svrn7.net"
    rfc5322Body = "From: did:drn:foundation.svrn7.net`r`nTo: did:drn:bindloss.svrn7.net`r`nSubject: Second message`r`nDate: $(([datetime]::UtcNow).ToString('ddd, dd MMM yyyy HH:mm:ss')) +0000`r`nMIME-Version: 1.0`r`nContent-Type: text/plain; charset=utf-8`r`n`r`nSecond test."
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/message"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

The `LobeManager: EnsureLoadedAsync` and import lines will not appear this time.

---

## Step 6 — Send an email delivery receipt

The `email/1.0/receipt` protocol is also handled by `Dequeue-PandoEmail`.  A receipt
body conventionally carries `originalMessageId` and `deliveredAt`:

```powershell
$body = @{
    from              = "did:drn:foundation.svrn7.net"
    to                = "did:drn:bindloss.svrn7.net"
    originalMessageId = "did:drn:svrn7.net/didcomm/msg/abc123"
    deliveredAt       = [datetimeoffset]::UtcNow.ToString('o')
    rfc5322Body       = "From: did:drn:foundation.svrn7.net`r`nTo: did:drn:bindloss.svrn7.net`r`nSubject: Delivery receipt`r`nDate: $(([datetime]::UtcNow).ToString('ddd, dd MMM yyyy HH:mm:ss')) +0000`r`nMIME-Version: 1.0`r`nContent-Type: text/plain; charset=utf-8`r`n`r`nYour message was delivered."
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/receipt"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log:

```
20:49:14.101 info: Svrn7.TDA.DIDCommMessageSwitchboard[0] Switchboard: routing ... (type=did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/receipt) → Dequeue-PandoEmail [Svrn7.Email]
20:49:14.104 dbug: ...   [PS Verbose] Email LOBE: stored email from did:drn:foundation.svrn7.net — 'Delivery receipt'
```

Both `email/1.0/message` and `email/1.0/receipt` route to `Dequeue-PandoEmail` —
the handler stores whichever arrives and returns the record for pipeline chaining.

---

## Step 7 — Inspect the email record

`Dequeue-PandoEmail` returns a hashtable with these fields:

| Field | Value |
|---|---|
| `MessageDid` | TDA resource DID URL — `did:drn:alpha.svrn7.net/inbox/msg/{objectId}` |
| `MessageId` | LiteDB ObjectId hex of the inbox record |
| `SenderDid` | `from` field of the DIDComm **body** (application-level sender, not envelope) |
| `ReceivedAt` | ISO 8601 UTC timestamp |
| `Rfc5322Body` | Complete RFC 5322 text as a single string |
| `Subject` | Extracted `Subject:` header value, or `$null` if absent |
| `FromHeader` | Extracted `From:` header value, or `$null` |
| `ToHeader` | Extracted `To:` header value, or `$null` |

**Sender identity note:** `SenderDid` is extracted from the DIDComm message body's
`from` field — not from the RFC 5322 `From:` header and not from the DIDComm
envelope's `from` field.  The RFC 5322 header is treated as display metadata only.

---

## Step 8 — Missing `rfc5322Body` field (error path)

Send a malformed message with no `rfc5322Body` to verify the handler degrades gracefully:

```powershell
$body = @{
    from = "did:drn:foundation.svrn7.net"
    to   = "did:drn:bindloss.svrn7.net"
} | ConvertTo-Json -Compress

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/message"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = $body
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log:

```
warn: ...   [PS Warning] Email LOBE: message did:drn:alpha.svrn7.net/inbox/msg/<id> has no rfc5322Body field.
```

The message is marked `Processed` (not failed) — the handler returns `$null` rather
than throwing, so the Switchboard considers it a clean completion.

---

## Step 9 — Reset between test runs

Stop the TDA, clear the inbox database, then restart:

```powershell
# (Stop the TDA first — Ctrl+C in the TDA terminal)
Remove-Item -Path "mem\svrn7-inbox.db", "mem\svrn7-inbox.db-log" -ErrorAction SilentlyContinue
dotnet .\Svrn7.TDA.dll
```

For a full reset (clears all records):

```powershell
Remove-Item -Path "mem\*.db" -ErrorAction SilentlyContinue
dotnet .\Svrn7.TDA.dll
```

After a full reset, repeat the bootstrap in `docs/LOBEDEBUG.md` Step 4 before
re-running these steps.

---

## Common Error Conditions

| Symptom | Cause | Fix |
|---|---|---|
| `No LOBE registered for @type .../email/1.0/message` | `Svrn7.Email` missing from `lobes.config.json` or `.psm1` not in output | Verify Step 1 |
| `[PS Warning] has no rfc5322Body field` | Body missing `rfc5322Body` key | Include `rfc5322Body` in the DIDComm body JSON (see Step 4) |
| `[PS Warning] message ... not found` | Message expired from cache before handler ran | Retry; increase `MaxMessageAgeSeconds` in `TdaOptions` for testing |
| Subject shows `$null` in verbose log | RFC 5322 `Subject:` header absent or misspelled | Verify header name casing (`Subject:` not `subject:`) |
| First message is slow (~200 ms extra) | JIT LOBE import delay — normal first-run behaviour | Expected; subsequent messages are fast |
