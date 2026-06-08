# Pando.Packaging — JIT LOBE Lifecycle Debug Guide

This guide walks the complete lifecycle of a JIT LOBE: packaging from source,
validating, installing into a running TDA, verifying hot-load via the
FileSystemWatcher, triggering the first JIT import, and verifying hot-update.

`Pando.Diagnostics` is used as the concrete example throughout.  It is the
simplest LOBE in the repo — one protocol, one cmdlet, no dependencies.

See `docs/LOBEDEBUG.md` for the federation/society bootstrap sequence that must
have been completed at least once before sending messages.

---

## Prerequisites

- PowerShell 7 (`pwsh.exe`).

  ```powershell
  $PSVersionTable.PSVersion   # Major must be 7
  ```

- The Pando.Packaging module is in `tools/Pando.Packaging.psm1` — it is **not**
  a LOBE and does not need to be built.  It is imported directly from source.

- A TDA database that already has a federation and society record.
  If starting fresh, complete Steps 1–4 of `docs/LOBEDEBUG.md` first.

---

## Terminal layout

Two PowerShell 7 terminals are needed throughout this guide.

| Terminal | Purpose |
|---|---|
| **A — TDA** | Runs the TDA process; watch log output here |
| **B — tooling** | Runs packaging cmdlets and sends test messages |

---

## Step 1 — Build (Terminal A)

```powershell
Set-Location C:/SVRN7/repos/SVRN7
dotnet build src/Svrn7.TDA/Svrn7.TDA.csproj
```

---

## Step 2 — Remove the LOBE from the build output (Terminal A)

This simulates a TDA instance that does not yet have `Pando.Diagnostics`
installed.  The TDA starts and runs normally without it.

```powershell
$outDir  = 'src/Svrn7.TDA/bin/Debug/net8.0'
$lobeOut = "$outDir/lobes/Pando.Diagnostics"

if (Test-Path $lobeOut) {
    Remove-Item $lobeOut -Recurse -Force
    Write-Host "Removed $lobeOut"
} else {
    Write-Host "Already absent"
}
```

Verify it is gone:

```powershell
Test-Path $lobeOut   # expected: False
```

---

## Step 3 — Start the TDA (Terminal A)

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
dotnet Svrn7.TDA.dll --port 8443 --name MyTDA
```

In the startup banner, confirm `Pando.Diagnostics` is **absent** from the JIT
count:

```
  LOBEs       : 4 eager  7 JIT  (N protocols  N cmdlets)
    Eager     : Svrn7.Common  Svrn7.Federation  Svrn7.Society  Svrn7.UX
    JIT       : Svrn7.Email  Svrn7.Calendar  ...
```

`Pando.Diagnostics` should **not** appear in the JIT line.

---

## Step 4 — Load the Pando.Packaging module (Terminal B)

> Do this once per PowerShell session.

```powershell
Set-Location C:/SVRN7/repos/SVRN7
Import-Module .\tools\Pando.Packaging.psm1
```

Verify all four cmdlets are available:

```powershell
Get-Command -Module Pando.Packaging
```

Expected:

```
CommandType  Name                   Version  Source
-----------  ----                   -------  ------
Function     Install-LOBEPackage    0.0      Pando.Packaging
Function     New-LOBEPackage        0.0      Pando.Packaging
Function     Publish-LOBEPackage    0.0      Pando.Packaging
Function     Test-LOBEPackage       0.0      Pando.Packaging
```

---

## Step 5 — Package (Terminal B)

```powershell
$lobeSrc = '.\src\Svrn7.TDA\lobes\Pando.Diagnostics'
$distDir = '.\dist'

$nupkg = New-LOBEPackage -Path $lobeSrc -OutputDirectory $distDir
```

Expected output:

```
Created: C:\SVRN7\repos\SVRN7\dist\Pando.Diagnostics.0.1.0.nupkg
```

Confirm the path was captured:

```powershell
$nupkg   # should print the full path
```

---

## Step 6 — Validate (Terminal B)

```powershell
$nupkg | Test-LOBEPackage
```

Expected: `42 PASS  0 WARN  0 FAIL` (or with one WARN if `.psd1` is absent —
that is expected for `Pando.Diagnostics`).

```
──────────────────────────────────────────────────────────
  Results:  41 PASS  1 WARN  0 FAIL
──────────────────────────────────────────────────────────
```

A non-zero FAIL count means the package is malformed.  Do not proceed — fix
the source files and repackage.

---

## Step 7 — Verify the protocol is NOT yet registered (Terminal B)

Send a `date-query` message to the running TDA.  Because `Pando.Diagnostics`
is not installed, the Switchboard will log "no handler".

```powershell
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
Import-Module .\lobes\Svrn7.Federation\Svrn7.Federation.psm1

$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/diagnostics/1.0/date-query'
    from = 'did:drn:solo.svrn7.net'
    to   = @('did:drn:solo.svrn7.net')
    body = '{}'
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log (Terminal A):

```
warn: DIDCommMessageSwitchboard[0]
      DIDCommMessageSwitchboard: no handler for
      'did:drn:svrn7.net/protocols/diagnostics/1.0/date-query' — message dropped.
```

This confirms the LOBE is absent.

---

## Step 8 — Install (Terminal B)

Install the `.nupkg` into the running TDA's lobes directory.  No `-LoadMode`
is needed — JIT LOBEs are auto-discovered.

```powershell
Set-Location C:/SVRN7/repos/SVRN7

$lobesDir = '.\src\Svrn7.TDA\bin\Debug\net8.0\lobes'

Install-LOBEPackage -Path $nupkg -LobesDirectory $lobesDir
```

Expected output:

```
Installed: Pando.Diagnostics -> ...\lobes\Pando.Diagnostics
```

Confirm the files are on disk:

```powershell
Get-ChildItem $lobesDir\Pando.Diagnostics
```

Expected:

```
Pando.Diagnostics.Impl.psm1
Pando.Diagnostics.lobe.json
Pando.Diagnostics.psm1
```

---

## Step 9 — Verify hot-load: TDA detects the new LOBE (Terminal A)

Within ~200 ms of Step 8 completing, the TDA FileSystemWatcher fires.
Watch for these log lines in Terminal A:

```
info: Svrn7.TDA.LobeManager[0]
      LobeManager: descriptor change —
      ...\lobes\Pando.Diagnostics\Pando.Diagnostics.lobe.json.
      Re-registering protocols.

info: Svrn7.TDA.LobeManager[0]
      LobeManager: LOBE 'Pando.Diagnostics' v0.1.0 — 2 protocol(s) registered.
```

**If these lines do not appear within 2–3 seconds:**
- Check that the TDA is still running (it may have crashed — look for exceptions).
- Verify the `.lobe.json` file is actually on disk (`Test-Path` in Terminal B).
- Check `TdaOptions.LobesConfigPath` in `appsettings.json` — the lobes dir must
  match where the package was installed.

---

## Step 10 — Trigger JIT import: send a date-query (Terminal B)

```powershell
$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/diagnostics/1.0/date-query'
    from = 'did:drn:solo.svrn7.net'
    to   = @('did:drn:solo.svrn7.net')
    body = '{}'
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log (Terminal A):

```
dbg: Svrn7.TDA.LobeManager[0]
     LobeManager: EnsureLoadedAsync — JIT '...\Pando.Diagnostics.psm1'.

info: Svrn7.TDA.LobeManager[0]
     LobeManager: importing into isolated runspace (JIT) — ...\Pando.Diagnostics.psm1

info: Svrn7.TDA.LobeManager[0]
     LobeManager: import complete — ...\Pando.Diagnostics.psm1

info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
     Invoke-PandoDiagnosticsDateQuery: replying ...
```

The JIT import succeeded.  The LOBE cmdlets are now live in the runspace.

---

## Step 11 — Hot-update: edit the .psm1 and resend (Terminal B)

The TDA uses `Import-Module -Force` for every JIT message.  Any change to the
`.psm1` is picked up on the very next message dispatch — no TDA restart needed.

**11.1 — Edit the installed .psm1**

Open `...\lobes\Pando.Diagnostics\Pando.Diagnostics.psm1` in any editor and
add a visible marker to the `Invoke-PandoDiagnosticsDateQuery` function body:

```powershell
# Add this line at the top of the function:
Write-Host "HOT-UPDATE v2 — Invoke-PandoDiagnosticsDateQuery" -ForegroundColor Magenta
```

Save the file.

**11.2 — Resend the message**

```powershell
$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/diagnostics/1.0/date-query'
    from = 'did:drn:solo.svrn7.net'
    to   = @('did:drn:solo.svrn7.net')
    body = '{}'
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

**11.3 — Verify (Terminal A)**

The TDA log shows the reimport:

```
info: Svrn7.TDA.LobeManager[0]
      LobeManager: importing into isolated runspace (JIT) — ...\Pando.Diagnostics.psm1
```

And the magenta `Write-Host` output appears in the TDA terminal, confirming the
updated `.psm1` was loaded for this dispatch.

> **Note:** `Import-Module -Force` is called on every JIT message, not only
> when the file changes.  The reimport log line will appear for every dispatch.
> This is by design — it guarantees hot-update without requiring a file watcher
> on `.psm1` files.  See `docs/BACKLOG.md` (TDA-001a) if reimport overhead
> becomes a concern at higher throughput.

---

## Step 12 — Publish to a local folder feed (optional)

To test `Publish-LOBEPackage` without a remote server, use a local directory
as the NuGet feed source.

```powershell
$feedDir = '.\local-feed'
New-Item -ItemType Directory -Path $feedDir -Force | Out-Null

Publish-LOBEPackage -Path $nupkg -Source $feedDir
```

Expected:

```
Published: Pando.Diagnostics.0.1.0.nupkg -> .\local-feed
```

Verify the package is in the feed:

```powershell
Get-ChildItem $feedDir
# Pando.Diagnostics.0.1.0.nupkg
```

Round-trip: install from the local feed:

```powershell
Install-LOBEPackage `
    -PackageId      Pando.Diagnostics `
    -Source         $feedDir `
    -LobesDirectory $lobesDir `
    -Force
```

---

## Troubleshooting

### `No *.lobe.json found in '...'`

`New-LOBEPackage -Path` must point to the **LOBE folder**, not the repo root or
the `lobes/` parent.  The folder must contain exactly one `*.lobe.json` file.

```powershell
Get-ChildItem .\src\Svrn7.TDA\lobes\Pando.Diagnostics -Filter '*.lobe.json'
# Must return: Pando.Diagnostics.lobe.json
```

### `Test-LOBEPackage` reports FAIL

Read the FAIL line for the specific check.  Common causes:

| FAIL message | Cause |
|---|---|
| `Exactly one .nuspec at package root` | Package was built incorrectly — re-run `New-LOBEPackage` |
| `All tools/ files under single named subfolder` | Two or more LOBEs were accidentally included |
| `.psm1 implementation present` | The `.psm1` was not found by `Get-ChildItem $lobeDir -File` |
| `SPDX expression is a known value` | `lobe.license` in `.lobe.json` is not a recognised SPDX identifier |

WARN on `.psd1 manifest present` is expected for LOBEs that intentionally omit
a manifest (`Pando.Diagnostics`, `Svrn7.Common`).

### FSW does not fire after `Install-LOBEPackage`

1. Confirm the TDA is still running — an unhandled exception may have stopped it.
2. Confirm the install path matches the TDA's lobes directory.
   The TDA logs the base directory at startup:
   ```
   LobeManager: FileSystemWatcher started — watching 'C:\...\lobes' for *.lobe.json.
   ```
   The `Install-LOBEPackage -LobesDirectory` value must resolve to the same path.
3. On Windows, the FSW has a small buffer — if many files change simultaneously
   the event may be lost.  Install one LOBE at a time.

### `no handler for '...'` after install

The FSW fired but `RegisterFromDescriptor` found no valid protocols.

Check the TDA log for a parse warning:

```
warn: Svrn7.TDA.LobeManager[0]
      LobeManager: could not parse descriptor — ...\Pando.Diagnostics.lobe.json.
```

Validate the `.lobe.json` is well-formed JSON and the `protocols` array is
non-empty.  The `uri`, `match`, and `entrypoint` fields are required per entry.

### JIT import fails (`Import-Module failed`)

```
InvalidOperationException: LobeManager: Import-Module failed for '...\Pando.Diagnostics.psm1':
  The term 'Invoke-PandoDiagnosticsDateQuery' is not recognized ...
```

The `.psm1` has a syntax error or the entrypoint function name does not match
`lobe.json`.  Open the `.psm1` directly and run it in a standalone `pwsh`
session to surface parse errors:

```powershell
pwsh -NoProfile -Command "Import-Module '.\lobes\Pando.Diagnostics\Pando.Diagnostics.psm1' -Force"
```

### Hot-update: old code still runs

This should not happen — `Import-Module -Force` runs on every JIT message
dispatch.  If old code runs after a `.psm1` edit:

1. Confirm the edit was saved to the **installed** copy in the `lobes/` output
   dir, not just the source copy in `src/Svrn7.TDA/lobes/`.
2. Check the TDA log for `importing into isolated runspace (JIT)` — if that
   line is absent the LOBE may have been resolved as eager, which uses
   `-Force false`.  Verify `Pando.Diagnostics` is not in the `eager` list in
   `lobes.config.json`.
