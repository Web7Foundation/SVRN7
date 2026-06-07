# SVRN7 TDA — Backlog

---

## TDA-005 — TDA-to-TDA transport without HTTPS/TLS (FYI / Future Design)

**Area:** `KestrelListenerService`, `TdaOptions`, deployment

**Summary:** TDAs will normally communicate over cleartext HTTP/2 (h2c), not
HTTPS/TLS.  TLS termination, if required, will be handled by the network
infrastructure layer (load balancer, reverse proxy, service mesh) rather than
by the TDA process itself.

**Implications:**
- `TdaOptions.RequireMutualTls` default should eventually change to `false`.
- `TdaOptions.TlsCertificatePath` / `TlsCertificatePassword` become optional
  infrastructure concerns, not TDA concerns.
- The Kestrel listener should default to h2c (cleartext HTTP/2) without requiring
  a certificate to be configured.
- `AcceptSelfSignedPeerCertificates` becomes irrelevant in the h2c path.
- The outbound `HttpClient` ("didcomm") already uses `RequestVersionExact` for
  HTTP/2 and works over h2c today — no change needed there.

**Current behaviour:** When no TLS certificate is configured, `KestrelListenerService`
logs a warning and runs in cleartext HTTP/2 mode:

```
warn: Svrn7.TDA.KestrelListenerService[0]
      KestrelListenerService: TLS certificate not configured.
      Running in cleartext HTTP/2 (development mode only).
```

This warning message itself will need updating once h2c becomes the intended
production mode rather than a development fallback.

**No code change required now** — tracked here for design continuity.

---

## TDA-004 — Per-instance LOBE directory (LOBE marketplace / registry)

**Area:** `LobeManager`, `TdaOptions`, deployment, `Program.cs`

**Summary:** Today all TDA instances share a single `lobes/` folder copied at build
time.  This is sufficient while LOBEs are static and bundled with the binary.

Once a LOBE marketplace or registry exists, each TDA instance will need its own
isolated lobes folder so that LOBEs can be downloaded, installed, updated, or removed
per-instance without affecting other running TDAs.

**Current behaviour (Epoch 0):**
- LOBEs are copied to `bin/.../lobes/` at build time — the build has no knowledge of
  the runtime port.
- All instances share that folder; per-instance isolation is achieved only for
  databases (`{port}/mem/`).
- A specific instance can point at a different LOBE set today by overriding
  `Tda:LobesConfigPath` at launch time.

**What will be needed:**
- Default `LobesConfigPath` changed to `{port}/lobes/lobes.config.json` so each
  instance has its own LOBE directory.
- A LOBE installer / package manager that downloads `.lobe.json` + `.psm1` pairs
  from a registry and places them in `{port}/lobes/`.
- `LobeManager` hot-reload (TDA-001) becomes essential — marketplace installs should
  not require a TDA restart.
- Possibly a signed/verified download chain so only trusted LOBEs are installed.

**No code change required now** — tracked here for design continuity.

---

## ~~TDA-003~~ — Wanderer-first additive role architecture ✓ *implemented*

**Area:** `Program.cs`, `TdaOptions`, `Svrn7.Core/Models.cs`, deployment

**Summary:** Every TDA starts as a **Wanderer** (`Svrn7Role.Wanderer`). Role is
additive — promoting a TDA creates an *additional* DID alongside the primary
Wanderer DID. The Wanderer DID is always the primary identity.

- **Wanderer** — base role; every TDA. Auto-bootstrapped on first run.
- **Federation** — Wanderer + `Initialize-Svrn7Federation` (no params). One per deployment.
- **Society** — Wanderer + Society DID registered with a Federation TDA via DIDComm.
- **Citizen** — Wanderer + Citizen DID registered with a Society TDA via DIDComm.

The `--role` and `--did` CLI arguments have been removed. `--port` is the only
required startup parameter. Role detection is DB-driven: `GetFederationAsync()`,
`GetOwnSocietyAsync()`, or equivalent. Each TDA is isolated via its port-scoped
`{port}/mem/` data directory. `Svrn7Name` is stored in the DIDDocument and
auto-generated as `"TDA-{port}"` at first-run Wanderer bootstrap.

---

## ~~TDA-002~~ — Federation/society query via DIDComm ✓ *implemented*

Protocol `federation/1.0/society-list` → handler `Invoke-Web7SocietyList` in
`Svrn7.Federation` LOBE.  Returns count, activeCount, and a societies array to
the `replyEndpoint`.  See LOBEDEBUG.md §4.5 for the send pattern.

---

## TDA-001 — Hot-reload for JIT LOBEs

**Area:** `LobeManager`, `IsolatedRunspaceFactory`

**Summary:** Updated or newly registered JIT LOBEs currently require a TDA restart to
take effect. `lobes.config.json` is read once at startup; JIT modules are cached in
runspaces for their lifetime.

**What is needed:**

- `FileSystemWatcher` on the lobes directory to detect `.psm1` changes and new
  `lobes.config.json` entries
- `LobeManager` dirty-flag mechanism per LOBE
- `IsolatedRunspaceFactory` drains and recreates runspaces that have a dirty LOBE loaded,
  on the next dispatch cycle

**Out of scope:** Eager LOBEs (loaded into the Initial Session State) always require a
TDA restart regardless of this feature.

---

## TDA-001b — Eager LOBE re-verification cost per dispatch (FYI / Design Note)

**Area:** `LobeManager.EnsureLoadedAsync`, `DIDCommMessageSwitchboard.InvokeCmdletPipelineAsync`

**Summary:** Eager LOBEs are reimported via `Import-Module` on every message dispatch,
even though they are already present in the runspace via the `InitialSessionState` (ISS).
Observed cost: ~43ms per dispatch (e.g. `Svrn7.Federation` on a `society-list` message).

**Why it happens:** `InvokeCmdletPipelineAsync` calls `EnsureLoadedAsync` for all
non-`.ps1` LOBEs unconditionally. The code comment says eager LOBEs are skipped — the
comment is wrong; the skip is not implemented.

**Why the current behaviour is intentional:** `Runspace.Open()` silently swallows ISS
load failures. Calling `Import-Module` for eager LOBEs on every dispatch acts as a
health check — if the ISS load failed silently, `EnsureLoadedAsync` detects it and
throws a clear `InvalidOperationException` rather than a cryptic "command not found"
error downstream.

**Trade-off:**
- Skipping → saves ~43ms per dispatch, but loses silent ISS failure recovery.
- Keeping → ~43ms overhead per dispatch per eager LOBE, but ISS failures surface
  immediately with a clear error message.

**Decision: keep current behaviour** during early development while ISS load
reliability is still being established.

**Future option:** Call `EnsureLoadedAsync` for eager LOBEs only when the runspace
probe (`IsolatedPipeline.ProbeRunspace()`) detects the module is missing — pay the
health-check cost only on actual failure, zero cost on the happy path.

---

## TDA-001a — JIT LOBE reimport cost per dispatch (FYI / Design Note)

**Area:** `LobeManager.EnsureLoadedAsync`, `IsolatedRunspaceFactory`, `DIDCommMessageSwitchboard`

**Summary:** JIT LOBEs are reimported via `Import-Module` on every message dispatch.
Because each dispatch opens a fresh `Runspace` from the shared `InitialSessionState`
(ISS), JIT LOBEs are never present in the new runspace — `EnsureLoadedAsync` always
runs `Import-Module` for them.

**Current behaviour:**
- Eager LOBEs: baked into the ISS at startup via `iss.ImportPSModule()`. `Import-Module`
  in `EnsureLoadedAsync` is idempotent (module already present) — near-zero cost per dispatch.
- JIT LOBEs: not in the ISS. `Import-Module` runs from disk on every dispatch — pays the
  full module load cost each time.

**Design trade-off (intentional):** Per-invocation runspace isolation is the priority.
A crash or runaway cmdlet in one runspace cannot affect any other concurrent dispatch.
The JIT reimport cost is the accepted price for that guarantee.

**If JIT latency becomes a problem:**
The fix is to dynamically add a JIT LOBE to the ISS template the first time it is
needed (requires rebuilding the ISS or maintaining a secondary ISS per LOBE set).
This is closely related to TDA-001 (hot-reload) — the same ISS rebuild mechanism
would eliminate the per-dispatch import cost for frequently-used JIT LOBEs.
