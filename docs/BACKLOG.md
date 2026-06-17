# SVRN7 TDA — Backlog

---

## TDA-007 — Rationalize protocol URI naming and versioning around LOBE names ✓ DONE

**Area:** all `.lobe.json` descriptors, agent scripts, DIDComm integration guide, BACKLOG TDA-006

**Summary:** Protocol URI path segments and version numbers must be derived
directly from LOBE names and LOBE versions — no independent invention.  The
LOBE name is the source of truth.  This also makes TDA-006 (on-demand LOBE
download) trivial: the NuGet package ID is read directly from the URI with no
transformation.

---

### Naming and version convention (to be enforced)

**Rule:** `did:drn:svrn7.net/protocols/{LobeName}.{lobe.version}/{action}`

- `{LobeName}` — the full LOBE name exactly as it appears in `lobe.name`
  (e.g. `Svrn7.Email`, `Pando.Diagnostics`).  Case-preserved.
- `{lobe.version}` — the full three-part version from `lobe.version` (e.g. `0.8.0`).
- `{action}` — the message action name (e.g. `message`, `register-citizen`).

**Examples under the new convention:**

```
Svrn7.Email 0.8.0      did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/message
Svrn7.Email 0.8.0      did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/receipt
Svrn7.Federation 0.8.0 did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/register-society
Svrn7.Onboarding 0.8.0 did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/register-citizen
Svrn7.Invoicing 0.8.0  did:drn:svrn7.net/protocols/Svrn7.Invoicing.0.8.0/request
Pando.Diagnostics 0.1.0 did:drn:svrn7.net/protocols/Pando.Diagnostics.0.1.0/date-query
```

**Derivation (bidirectional — no algorithm needed):**

| Direction | Rule |
|---|---|
| URI → LOBE name | second path segment after `/protocols/` is the LOBE name verbatim |
| URI → version constraint | third path segment is `lobe.version` verbatim |
| LOBE name → URI segment | use `lobe.name` from `.lobe.json` verbatim |
| LOBE version → URI version | use `lobe.version` from `.lobe.json` verbatim |

This makes TDA-006 trivial: given
`did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/register-citizen`, the
NuGet package ID is `Svrn7.Onboarding` and the minimum version is `0.8.*`
— read directly from the URI, no registry lookup for the package name.

---

### Current state — all URIs must be updated

Every existing protocol URI must be renamed.  Current URIs use ad-hoc lowercase
suffixes (`email`, `federation`, `onboard`, `invoice`) and a hardcoded `1.0`
version.  The full before/after for every LOBE:

| LOBE | Current URI segment / version | New segment / version |
|---|---|---|
| `Svrn7.Calendar` | `calendar/1.0` | `Svrn7.Calendar/0.8` |
| `Svrn7.Email` | `email/1.0` | `Svrn7.Email/0.8` |
| `Svrn7.Federation` | `federation/1.0` | `Svrn7.Federation/0.8` |
| `Svrn7.Identity` | `did/1.0`, `vc/1.0` | `Svrn7.Identity/0.8` (see split note) |
| `Svrn7.Invoicing` | `invoice/1.0` | `Svrn7.Invoicing/0.8` |
| `Svrn7.Notifications` | `notification/1.0` | `Svrn7.Notifications/0.8` |
| `Svrn7.Onboarding` | `onboard/1.0` | `Svrn7.Onboarding/0.8` |
| `Svrn7.Presence` | `presence/1.0` | `Svrn7.Presence/0.8` |
| `Svrn7.Society` | `society/1.0`, `transfer/1.0` | `Svrn7.Society/0.8` (see split note) |
| `Svrn7.UX` | `ux/1.0` | `Svrn7.UX/0.8` |
| `Pando.Diagnostics` | `diagnostics/1.0` | `Pando.Diagnostics/0.1` |

`Svrn7.Common` has no protocols — no change needed.

---

### Required LOBE splits

Two LOBEs currently own protocols that belong in separate LOBEs.  A LOBE must
own exactly one URI segment (its own name):

- **`Svrn7.Identity`** owns `did/1.0/*` and `Svrn7.Identity/0.8.0/vc-*`.  These are unrelated
  concerns.  Options: (A) consolidate all under `Svrn7.Identity/0.8.0/*` and
  rename actions accordingly; (B) split into `Svrn7.DID` and `Svrn7.VC`
  (separate LOBEs, separate packages).  Decision required.

- **`Svrn7.Society`** owns `transfer/1.0/*` in addition to `society/1.0/*`.
  Transfer protocols must move to a new `Svrn7.Transfer` LOBE and be renamed
  `did:drn:svrn7.net/protocols/Svrn7.Transfer/0.8/*`.

---

### Version bump rules

| Change type | Protocol URI version | LOBE package version |
|---|---|---|
| Patch fix (no message format change) | `0.8.0` → `0.8.1` | `0.8.0` → `0.8.1` |
| New optional field added | `0.8.0` → `0.9.0` | `0.8.0` → `0.9.0` |
| Breaking field rename / removal | `0.8.0` → `0.9.0` or `1.0.0` | `0.8.0` → `0.9.0` or `1.0.0` |

A protocol version bump always requires a new URI.  Old and new URIs may be
registered simultaneously during a migration window (see versioning backlog).

---

### Scope of change

This is a **breaking change** across all `.lobe.json` files, all agent scripts
(`lobes/Agent*.ps1`), all integration test message fixtures, and any external
sender that has hardcoded the current URI strings.  All must be updated in a
single coordinated commit.

**No code change required in `LobeManager` or `DIDCommMessageSwitchboard`** —
the registry is URI-keyed and is indifferent to the naming convention.

**Dependencies:** must be completed before TDA-006 to make package-ID
derivation trivial.

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
   package ID is read directly from the URI: the second path segment after
   `/protocols/` is the package ID verbatim (e.g.
   `did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/message` → package
   `Svrn7.Email`).  The registry is still needed for one thing: the NuGet feed
   URL (`https://packages.svrn7.net/v3/index.json`).  The minimum version
   constraint is also read directly from the URI (`0.8` → `>= 0.8.0`).

   **The registry is another TDA with a specific Role.**  It is not a
   traditional HTTP service — it is a TDA whose Role includes serving LOBE
   package metadata and feed URLs via DIDComm.  `TdaOptions.LobeRegistryDid`
   holds the DID of the registry TDA (e.g. `did:drn:registry.svrn7.net`).
   The feed URL is fetched from the registry TDA via a DIDComm request rather
   than being hardcoded.

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

**Implementation gap:** The startup help text and comments in `Program.cs` state that
LOBEs load from `<BaseDir>/{port}/lobes/`, but the actual default in code is:

```csharp
opts.LobesConfigPath = ctx.Configuration["Tda:LobesConfigPath"]
    ?? Path.Combine(AppContext.BaseDirectory, "lobes", "lobes.config.json");
```

This is the shared `lobes/` directory, not port-scoped. The documentation is ahead of
the implementation. The default must be changed to
`Path.Combine(AppContext.BaseDirectory, port.ToString(), "lobes", "lobes.config.json")`
and the build/deployment scripts updated to copy LOBEs into the port-scoped directory
at launch time.

**What will be needed:**
- Default `LobesConfigPath` changed to `{port}/lobes/lobes.config.json` so each
  instance has its own LOBE directory.
- Build or launch scripts updated to seed `{port}/lobes/` from the shared catalog on
  first run (or copy at build time per port).
- A LOBE installer / package manager that downloads `.lobe.json` + `.psm1` pairs
  from a registry and places them in `{port}/lobes/`.
- `LobeManager` hot-reload (TDA-001 ✓) is already in place — marketplace installs
  will not require a TDA restart once TDA-004 is done.
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

## ~~TDA-001~~ — Hot-reload for JIT LOBEs ✓ *implemented*

**Area:** `LobeManager`, `IsolatedRunspaceFactory`

**What was needed and how it was implemented:**

- `FileSystemWatcher` on the lobes directory (`_watcher`) — detects new `*.lobe.json`
  files at runtime and auto-registers them via `RegisterFromDescriptor`. ✓
- `FileSystemWatcher` on `lobes.config.json` (`_configWatcher`) — detects changes to the
  eager LOBE list; warns if new eager entries are added (restart required for those). ✓
- JIT LOBE hot-reload — achieved via `Import-Module -Force` on every dispatch in
  `EnsureLoadedAsync`. A changed `.psm1` is picked up automatically on the next message
  without a TDA restart. No dirty-flag or runspace drain is needed; Force reimport
  is the mechanism. ✓

**Remaining constraint:** Eager LOBEs (baked into the `InitialSessionState` at startup)
still require a TDA restart when their `.psm1` changes. This is by design — the ISS
cannot be rebuilt at runtime.

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

## TDA-001c — Double GetMessageAsync call in LOBE cmdlets (FYI / Minor Performance Note)

**Area:** `DIDCommMessageSwitchboard`, `Svrn7.Email.0.8.0.psm1`, all LOBE cmdlets that call
`$SVRN7.GetMessageAsync()` internally

**Summary:** The Switchboard pipeline calls `Dequeue-Svrn7Message -Did $did` before invoking
the LOBE cmdlet, then passes only the string `$MessageDid` by name. LOBE cmdlets that
need the payload (e.g. `Dequeue-PandoMail`) call `$SVRN7.GetMessageAsync()` again
internally — a second round-trip to the inbox store for the same message.

**Why it is minor:** `GetMessageAsync` caches `InboxMessageView` in `SvrN7RunspaceContext`
with a 24-hour TTL. The second call for the same DID URL is a dictionary lookup — no I/O.
The redundancy is structural but the practical cost is near-zero today.

**Potential fix:** Change LOBE cmdlet signatures to accept `[Parameter(Mandatory,
ValueFromPipeline)] [Svrn7.TDA.InboxMessageView] $Message` and let the pipeline carry
the already-fetched view. The Switchboard currently passes `-MessageDid $didUrl` as a named
parameter for all LOBE cmdlets; that would need to be dropped from the non-.ps1 branch of
`InvokeCmdletPipelineAsync` when changing a cmdlet to this signature. The redundant
`$MessageDid` parameter would need to be kept (optional, unused) or the Switchboard updated.

**Decision:** Deferred — the cache makes this cosmetic at Epoch 0 throughput. Revisit if
profiling shows inbox-store reads appearing under load.

## TDA-008 — Version-less protocol URI fallback ("pick highest installed LOBE version")

**Area:** `LobeManager.TryResolveProtocol`, `LobeProtocolRegistration`

**Summary:** When a DIDComm message arrives with a version-less `@type` URI
(e.g. `did:drn:svrn7.net/protocols/Svrn7.Email/signal-message` instead of
`did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/signal-message`), the
Switchboard currently dead-letters it — no registration matches.  A possible
convenience feature would add a third fallback tier to `TryResolveProtocol`
that strips the version segment from all registered URIs, matches on LOBE name
+ action suffix, and routes to the highest installed version.

**What implementation would require:**
- Add `string LobeVersion` to `LobeProtocolRegistration` (already available
  from `descriptor.Lobe.Version` at registration time — just not carried through).
- Add ~30-40 lines to `TryResolveProtocol`: detect version-less incoming URI,
  collect candidate registrations by `LobeName` + action suffix, pick highest semver.
- Gate behind `TdaOptions.AllowVersionlessFallback` (default: `false`).

**⚠ WARNING — do not enable by default.**

The version segment in a protocol URI is a contract identifier.  Sender and
receiver must agree on the same message schema.  "Pick highest" silently breaks
that agreement:

- A message built against `0.8.0`'s schema arrives version-less.
- `0.9.0` is installed with a renamed or required field.
- The LOBE misparses the body — no routing error, silent data loss or panic
  inside the handler.

This is exactly the failure mode that protocol versioning prevents.
Version-less routing trades correctness for convenience and must never be the
default.  Dead-lettering version-less messages (P-006) is the correct default.

**Acceptable use:** opt-in for development tooling and single-version
deployments where "highest" is always "only".  Never in production with
multiple versions installed.
