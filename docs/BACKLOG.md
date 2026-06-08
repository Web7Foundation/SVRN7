# SVRN7 TDA — Backlog

---

## TDA-007 — Rationalize protocol URI naming and versioning around LOBE names

**Area:** all `.lobe.json` descriptors, agent scripts, DIDComm integration guide, BACKLOG TDA-006

**Summary:** Protocol URI path segments and version numbers should be derived
from LOBE names — not independently invented.  The LOBE name is the source of
truth.  This is a prerequisite for TDA-006 (on-demand LOBE download), where an
unknown protocol URI must resolve to a deterministic NuGet package ID without
a round-trip to a registry index.

---

### Naming convention (to be enforced)

**Rule:** `did:drn:svrn7.net/protocols/{segment}/{ver}/{action}`

Where `{segment}` is the LOBE name suffix — the part after the first `.` —
lowercased.  Examples: `Svrn7.Email` → `email`; `Pando.Diagnostics` →
`diagnostics`; `Svrn7.Onboarding` → `onboarding`.

**Derivation algorithm (bidirectional):**

| Direction | Algorithm |
|---|---|
| LOBE name → segment | strip namespace prefix (`Svrn7.` / `Pando.`), lowercase |
| segment → LOBE name | title-case, prepend `Svrn7.` (or `Pando.` for known Pando LOBEs) |

This makes TDA-006 step 1 algorithmic: given an unknown URI
`did:drn:svrn7.net/protocols/onboarding/1.0/register-citizen`, derive
`Svrn7.Onboarding` as the NuGet package ID without a registry lookup.

---

### Current inconsistencies — must be fixed

Mapping produced by scanning all `.lobe.json` files as of 2026-06-08:

| LOBE | Current segment(s) | Required segment | Action |
|---|---|---|---|
| `Svrn7.Invoicing` | `invoice` | `invoicing` | Rename all URIs |
| `Svrn7.Notifications` | `notification` | `notifications` | Rename all URIs |
| `Svrn7.Onboarding` | `onboard` | `onboarding` | Rename all URIs |
| `Svrn7.Identity` | `did`, `vc` | `identity` | Rename all URIs; consolidate two segments into one |
| `Svrn7.Society` | `society`, `transfer` | `society` | Move `transfer/*` URIs to a new `Svrn7.Transfer` LOBE |

LOBEs already conforming: `Svrn7.Calendar`, `Svrn7.Email`, `Svrn7.Federation`,
`Svrn7.Presence`, `Svrn7.Society` (partial), `Svrn7.UX`, `Pando.Diagnostics`.
`Svrn7.Common` has no protocols — conforms by default.

---

### One segment per LOBE rule

A LOBE must own exactly one protocol path segment.  Multiple segments in one
LOBE (`did` + `vc` in `Svrn7.Identity`; `society` + `transfer` in
`Svrn7.Society`) break the derivation algorithm and make on-demand download
ambiguous.

**Required splits:**
- `Svrn7.Identity`: consolidate `did/` and `vc/` under `identity/`, OR split
  into `Svrn7.DID` and `Svrn7.VC` (separate LOBEs, separate packages).
- `Svrn7.Society`: extract `transfer/` into a new `Svrn7.Transfer` LOBE.

---

### Version numbering convention

Protocol URI version (`1.0`) and LOBE package version (`0.8.0`) are
**independent axes**:

| Axis | Format | Bumped when |
|---|---|---|
| Protocol URI version | `{major}.{minor}` only | Breaking change to the message body schema |
| LOBE package version | Full SemVer `{major}.{minor}.{patch}` | Any implementation change |

**Rule:** a patch or minor LOBE release must not change the protocol URI
version.  A protocol version bump (`1.0` → `2.0`) always implies a new URI and
may require a new handler registration alongside the old one during a migration
window (see TDA versioning backlog for side-by-side handling).

Current state: all protocols at `1.0`, all LOBEs at `0.x.0` — consistent for
Epoch 0.  No changes needed now beyond enforcing the rule going forward.

---

### Scope of change

URI renames are a **breaking change** for any sender that has hardcoded the
current URI strings.  The agent scripts in `lobes/Agent*.ps1` and the
integration tests must be updated in the same commit as the `.lobe.json`
descriptor changes.  A compatibility prefix registration (old URI → same
handler) can be added during a transition window if needed.

**No code change required in `LobeManager` or `DIDCommMessageSwitchboard`** —
the registry is URI-keyed and is indifferent to the naming convention.

**Dependencies:** must be completed before TDA-006 to enable the algorithmic
package-ID derivation.

---

## TDA-006 — On-demand LOBE download when an unknown message type arrives

**Area:** `DIDCommMessageSwitchboard`, `LobeManager`, `TdaOptions`, LOBE registry/marketplace

**Summary:** When a TDA receives a DIDComm message whose `@type` URI has no
registered handler, the Switchboard calls `MarkFailedAsync(retry: false)` —
the message is dead-lettered immediately.  A future capability would allow the
TDA to intercept that path, automatically resolve, download, and install the
required LOBE from a registry, then re-enqueue the message — making the LOBE
set self-healing and removing the need for pre-deployment configuration of
every message type a TDA will ever encounter.

**What would be required:**

1. **LOBE registry / index** — Once TDA-007 naming is in place, the NuGet
   package ID is derivable algorithmically from the protocol URI (strip
   `did:drn:svrn7.net/protocols/`, take the first segment, title-case,
   prepend `Svrn7.`).  The registry is still needed for two things: the feed
   URL (`https://packages.svrn7.net/v3/index.json`) and the latest compatible
   version for the current TDA epoch.  `TdaOptions.LobeRegistryUrl` holds the
   registry base URL.

2. **Switchboard — "no handler" intercept** — `DIDCommMessageSwitchboard` must
   intercept the `reg is null` branch before calling `MarkFailedAsync` and call
   into `LobeManager.TryResolveAndInstallAsync(messageType)`.  On success the
   message is re-enqueued to the inbox for a second dispatch attempt; on failure
   (download error, timeout, policy rejection) it falls through to
   `MarkFailedAsync(retry: false)` as today.

3. **LobeManager — `TryResolveAndInstallAsync`** — New method:
   - Query the registry index for a package ID matching the protocol URI prefix.
   - Call `dotnet nuget download` (or use `HttpClient` directly against the
     NuGet v3 API) to fetch the `.nupkg` to a temp path.
   - Validate the package (reuse `Test-LOBEPackage` logic or a C# equivalent).
   - Extract to the lobes directory (reuse `Install-LOBEPackage` extraction
     logic).
   - The FileSystemWatcher picks up the new `.lobe.json` and calls
     `RegisterFromDescriptor` — this is already implemented.
   - Return success/failure to the Switchboard.

4. **Trust / signature verification** — Downloaded LOBEs should be signed and
   the signature verified before installation.  The signing key for each
   package should be pinned in `TdaOptions` or fetched from the registry
   alongside the package.  Without this, on-demand download is a remote code
   execution surface.

5. **Policy gate** — `TdaOptions.AutoInstallLobes` (bool, default `false`).
   Auto-download should be opt-in.  When disabled, the "no handler" path
   continues to drop with a warning.  When enabled, auto-install is gated by
   an allowed-list (`TdaOptions.AllowedLobeAuthors` or a signed registry
   manifest).

6. **Retry queue** — The message that triggered the download cannot be
   re-processed until the LOBE is installed and `RegisterFromDescriptor` has
   run.  A simple approach: re-enqueue the raw message bytes to the TDA inbox
   after a configurable delay (`TdaOptions.AutoInstallRetryDelayMs`).  Requires
   the inbox to tolerate duplicate delivery (idempotent handlers).

7. **Per-instance lobes directory** — This feature is only safe if each TDA
   instance has its own lobes directory (see TDA-004).  Downloading a LOBE into
   a shared directory while another instance is running can cause partial reads.

**Dependencies:** TDA-007 (naming rationalization, required for algorithmic
package-ID derivation), TDA-004 (per-instance lobes dir), LOBE registry design
(not yet started).

**No code change required now** — tracked here for design continuity.

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
