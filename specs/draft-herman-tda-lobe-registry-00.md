# TDA LOBE Registry and Descriptor Format
# draft-herman-tda-lobe-registry-00
# Author: M. Herman, Web 7.0 Foundation
# Published: April 2026

Internet-Draft: draft-herman-tda-lobe-registry-00
Published:      April 2026
Expires:        October 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Web 7.0 Foundation Governance Council
Related:        draft-herman-parchment-programming-00
                draft-herman-web7-society-architecture-00
                draft-herman-didcomm-svrn7-transfer-00
                draft-herman-drn-resource-addressing-00

---

## Abstract

This document specifies the LOBE (Logic-Oriented Behaviour Extension) descriptor format
and runtime registry for Web 7.0 Trusted Digital Assistants (TDAs). A LOBE is a
PowerShell Module that implements one or more DIDComm protocol message handlers within
a TDA's PowerShell Runspace Pool. Each LOBE ships a JSON descriptor file
(`{module-name}.lobe.json`) that declares its identity, DIDComm protocol registrations,
exported cmdlets with typed input/output schemas, AI legibility metadata, and inter-LOBE
dependencies. The TDA LOBE Registry is an in-process registry populated at startup from
descriptor files and updated at runtime via a filesystem watcher. The DIDComm Message
Switchboard uses the registry to route inbound messages to the correct LOBE cmdlet
without hardcoded protocol knowledge. This enables independent developers to extend a
TDA's capabilities by dropping a new LOBE module and descriptor into the TDA's LOBE
directory without restarting the TDA host process.

---

## 1. Introduction

The Web 7.0 Trusted Digital Assistant (TDA) is a sovereign, DID-native, DIDComm-native
runtime agent for citizens and Societies within a VTC7 (Verifiable Trust Circle)
federation [WEB70-ARCH]. A TDA receives inbound DIDComm V2 messages on a single
HTTP/2 + mTLS endpoint (`POST /didcomm`), processes them through a PowerShell Runspace
Pool, and delivers outbound DIDComm messages to peer TDAs via HttpClient.

The extensibility mechanism for TDA message handling is the LOBE — a PowerShell Module
(`.psm1`) that implements the entry-point cmdlets for one or more DIDComm protocol
message types. LOBEs are designed to be independently authored, versioned, and deployed.
A third-party LOBE developer writes a PowerShell module that handles their protocol, ships
it with a descriptor file, and drops both into the TDA's LOBE directory. The TDA discovers
the new LOBE, registers its protocols, and begins routing matching messages to it — without
any change to the TDA host process and without requiring a restart.

This document specifies:

1. The LOBE descriptor format — the JSON schema for `.lobe.json` files.
2. The LOBE registry — the in-process `ConcurrentDictionary` that maps `@type` URI
   patterns to LOBE cmdlet registrations.
3. The dynamic LOBE loading mechanism — `FileSystemWatcher` + `LobeManager`.
4. The Switchboard routing protocol — how inbound messages are matched to registered LOBEs.
5. The third-party LOBE developer contract — what a LOBE author must implement.
6. The AI legibility framework — how LOBE descriptors support AI-aided pipeline
   composition in future TDA epochs.
7. The standard SVRN7 LOBEs — the nine built-in LOBEs shipped with the SVRN7 TDA Host.

### 1.1 LOBE Temporality

A LOBE is a PowerShell Module. There is no architectural distinction between LOBEs that
are outside a Runspace and those that are inside a Runspace [DRAFT-PPML, Rule AI-1].
The only distinction is temporal:

- **Eager LOBEs** are declared in the `"eager"` array of `lobes.config.json` and imported
  into the `InitialSessionState` at TDA startup. They are available in every runspace
  with no per-invocation import cost.

- **JIT LOBEs** are declared in the `"jit"` array of `lobes.config.json` and imported
  via `Import-Module` on first use. Subsequent calls in the same runspace do not
  re-import.

The `lobes.config.json` file is a lightweight bootstrap manifest that lists module paths.
The `.lobe.json` descriptor file is the rich per-LOBE capability declaration.

### 1.2 Relationship to the PPML Specification

The LOBE descriptor format is an application-specific extension to the Parchment
Programming Modeling Language (PPML) derivation rules [DRAFT-PPML]. Each LOBE is a
PPML artefact derived from a LOBE element instance in the DSA 0.24 diagram. The
descriptor schema satisfies the PPML Tractability Invariant: every field traces to a
named diagram element or a documented requirement.

### 1.3 Relationship to MCP

The cmdlet definition structure in the LOBE descriptor deliberately mirrors the Model
Context Protocol (MCP) Tool definition [MCP-2025-11-25]. The `cmdlets[].name`,
`cmdlets[].title`, `cmdlets[].description`, `cmdlets[].inputSchema`,
`cmdlets[].outputSchema`, and `cmdlets[].annotations` fields correspond directly to MCP
Tool fields. This alignment enables a future epoch feature: the TDA could expose its
registered LOBEs as MCP tools via a `tools/list` interface, making the LOBE descriptor
the MCP tool definition with no translation needed. The `protocols` array — which maps
DIDComm `@type` URI patterns to entry-point cmdlets — has no MCP equivalent and is the
TDA-specific addition.

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

---

## 3. Terminology

- **LOBE**: Logic-Oriented Behaviour Extension. A PowerShell Module (`.psm1`) that
  implements one or more DIDComm protocol message handlers for a TDA. Derived from the
  LOBE element type in the PPML Legend (DSA 0.24).

- **LOBE descriptor**: A JSON file (`{module-name}.lobe.json`) that declares a LOBE's
  identity, DIDComm protocol registrations, exported cmdlet schemas, dependencies, and
  AI legibility metadata.

- **LOBE registry**: The in-process `ConcurrentDictionary<string, LobeProtocolRegistration>`
  maintained by the `DIDCommMessageSwitchboard`. Maps `@type` URI patterns to
  `LobeProtocolRegistration` records.

- **Entry-point cmdlet**: The PowerShell cmdlet invoked by the Switchboard for a
  specific `@type` URI. Declared in `protocols[].entrypoint` in the descriptor.

- **Eager LOBE**: A LOBE imported into the `InitialSessionState` at TDA startup.
  Available in every runspace with no import cost.

- **JIT LOBE**: A LOBE imported via `Import-Module` on first use within a runspace.

- **DIDComm Message Switchboard**: The TDA component that reads from the inbox, resolves
  registered LOBEs, imports JIT LOBEs on demand, and invokes entry-point cmdlet pipelines.

- **LobeManager**: The TDA component that owns the `InitialSessionState`, reads
  `lobes.config.json` and `.lobe.json` descriptor files, manages the `FileSystemWatcher`,
  and resolves LOBE dependency graphs.

- **Option A special case**: The Switchboard idempotency check for SVRN7 transfer
  protocols (`transfer/1.0/order`) that is implemented directly in the Switchboard
  rather than in the LOBE, because transfer idempotency is a correctness invariant.

- **Pass-by-reference**: The TDA pattern in which the Switchboard passes an inbox message
  DID URL (not the payload) to LOBE cmdlet pipelines. Cmdlets resolve the message payload
  via `$SVRN7.GetMessageAsync($messageDid)`.

---

## 4. File Naming Convention

Each LOBE MUST ship three files with names derived from the same base name:

```
{module-name}.psm1        — PowerShell module implementation
{module-name}.psd1        — PowerShell module manifest
{module-name}.lobe.json   — TDA LOBE descriptor (this specification)
```

The `{module-name}` MUST be consistent across all three files. The `.lobe.json` file
MUST be placed in the same directory as the `.psm1` and `.psd1` files. The TDA LOBE
directory is configured via `TdaOptions.LobesConfigPath` and defaults to `./lobes/`.

Examples:
```
Svrn7.Email.psm1
Svrn7.Email.psd1
Svrn7.Email.lobe.json
```

Third-party example:
```
MyOrg.Health.psm1
MyOrg.Health.psd1
MyOrg.Health.lobe.json
```

---

## 5. LOBE Descriptor Schema

### 5.1 Complete Schema Reference

```json
{
  "lobe": {
    "id":           "string  — reverse-domain unique ID (e.g., svrn7.email, myorg.health)",
    "name":         "string  — PowerShell module name (e.g., Svrn7.Email)",
    "title":        "string  — human-readable display name (e.g., 'SVRN7 Email LOBE')",
    "description":  "string  — full description for human and AI developers",
    "version":      "string  — semantic version (MAJOR.MINOR.PATCH, e.g., 0.8.0)",
    "author":       "string  — author name",
    "organization": "string  — organisation name",
    "website":      "string  — documentation or project URI",
    "license":      "string  — SPDX license identifier (e.g., MIT, Apache-2.0)",
    "epochRequired":"integer — minimum TDA epoch (0=Epoch 0+, 1=EcosystemUtility+)",
    "module":       "string  — .psm1 filename, resolved relative to descriptor"
  },

  "protocols": [
    {
      "uri":          "string  — DIDComm @type URI (or prefix if match='prefix')",
      "title":        "string  — human-readable name for this protocol message type",
      "description":  "string  — message body format, semantics, and expected fields",
      "direction":    "string  — 'inbound' | 'outbound' | 'bidirectional'",
      "match":        "string  — 'prefix' (default) | 'exact'",
      "entrypoint":   "string  — PS cmdlet name invoked by the Switchboard",
      "epochRequired":"integer — minimum epoch for this specific URI"
    }
  ],

  "cmdlets": [
    {
      "name":          "string  — PS cmdlet name (unique within LOBE)",
      "title":         "string  — human-readable display name (mirrors MCP Tool.title)",
      "description":   "string  — full description: purpose, inputs, outputs, behaviour",
      "epochRequired": "integer — minimum epoch for this cmdlet",
      "annotations": {
        "idempotent":       "boolean|null — safe to call multiple times with same input",
        "modifiesState":    "boolean|null — true if writes to Data Storage or inbox",
        "requiresEpoch":    "integer      — minimum epoch (may differ from LOBE epoch)",
        "destructive":      "boolean      — true for irreversible actions (e.g., transfer)",
        "pipelinePosition": "string|null  — 'source'|'transform'|'sink'"
      },
      "inputSchema":      "object|null — JSON Schema 2020-12 (optional in Epoch 0)",
      "outputSchema":     "object|null — JSON Schema 2020-12 (optional in Epoch 0)",
      "pipelineExample":  "string|null — illustrative PowerShell pipeline",
      "example":          "string|null — example invocation"
    }
  ],

  "dependencies": {
    "lobes":    ["array of LOBE module names required before this LOBE imports"],
    "packages": ["array of NuGet or PS Gallery package IDs (future use)"]
  },

  "ai": {
    "_note":            "string — Epoch 0 design intent note (see Section 11)",
    "summary":          "string — one-paragraph AI-optimised capability summary",
    "useCases":         ["array of concrete use cases as complete sentences"],
    "compositionHints": ["array of pipeline composition hints as complete sentences"],
    "limitations":      ["array of known limitations as complete sentences"]
  }
}
```

### 5.2 Required Fields

The following fields are REQUIRED in a conformant LOBE descriptor:

- `lobe.id`, `lobe.name`, `lobe.title`, `lobe.description`
- `lobe.version`, `lobe.author`, `lobe.organization`
- `lobe.epochRequired`, `lobe.module`
- `protocols[].uri`, `protocols[].entrypoint` (for each protocol entry)
- `cmdlets[].name`, `cmdlets[].title`, `cmdlets[].description`
- `cmdlets[].annotations` (with all five annotation fields present)
- `ai._note`, `ai.summary`

All other fields are RECOMMENDED or OPTIONAL.

### 5.3 `lobe.id` Format

The `lobe.id` MUST be a reverse-domain-style string in lowercase, using only
alphanumeric characters, hyphens, and dots. It MUST be globally unique across all
LOBEs registered in a given TDA deployment. SVRN7 standard LOBEs use the `svrn7.`
prefix. Third-party LOBE developers SHOULD use a prefix derived from their
organisation's domain.

```
svrn7.email             — SVRN7 foundation LOBE
svrn7.calendar          — SVRN7 foundation LOBE
myorg.health            — third-party health domain LOBE
io.example.legal        — third-party legal LOBE
```

### 5.4 `lobe.epochRequired` and Epoch Gating

The `lobe.epochRequired` field declares the minimum TDA epoch in which the LOBE may
be loaded. The TDA Host defines three epochs:

| Value | Epoch Name         | Description                                      |
|-------|--------------------|--------------------------------------------------|
| 0     | Endowment          | Citizen registration, same-Society transfers     |
| 1     | EcosystemUtility   | Cross-Society transfers, extended protocols      |
| 2     | MarketIssuance     | Trading, market-rate issuance (future)           |

The Switchboard checks the current epoch against `protocols[].epochRequired` before
routing. Messages matching a protocol URI whose `epochRequired` exceeds the current epoch
are marked Failed without invoking the entry-point cmdlet.

---

## 6. Protocol URI Matching

### 6.1 `match: "prefix"` (default)

When `match` is `"prefix"`, any inbound DIDComm `@type` value that begins with the
registered `uri` string is routed to the registered `entrypoint`. A single descriptor
entry with a protocol prefix covers all message subtypes in a protocol family:

```json
{
  "uri":       "did:drn:svrn7.net/protocols/email/1.0/",
  "match":     "prefix",
  "entrypoint":"Receive-TdaEmail"
}
```

This routes `email/1.0/message`, `email/1.0/receipt`, and any future `email/1.0/*`
subtypes to `Receive-TdaEmail`.

### 6.2 `match: "exact"`

When `match` is `"exact"`, only an exact string match routes to the entry. Use when
different subtypes within a protocol family require different entry-point cmdlets:

```json
{ "uri": "did:drn:svrn7.net/protocols/calendar/1.0/invite",
  "match": "exact", "entrypoint": "Receive-TdaMeetingRequest" },
{ "uri": "did:drn:svrn7.net/protocols/calendar/1.0/",
  "match": "prefix", "entrypoint": "Import-TdaCalendarEvent" }
```

### 6.3 Match Priority

When both an exact and a prefix match apply to the same `@type`, the Switchboard MUST
prefer the exact match. Prefix matches MUST be checked in order of decreasing specificity
(longest prefix first).

### 6.4 Conflict Resolution

If two registered LOBEs declare overlapping protocol URI registrations, the Switchboard
MUST log a warning and retain the first-registered entry. LOBE developers MUST NOT
register URIs in namespaces they do not control.

---

## 7. Option A: SVRN7 Transfer Protocol Special Case

The SVRN7 transfer protocols include an idempotency requirement that is implemented as
a Switchboard special case rather than in the LOBE:

- `did:drn:svrn7.net/protocols/transfer/1.0/order` — cross-Society TransferOrder
  MUST be checked against `IProcessedOrderStore` before routing. If a receipt already
  exists, the stored receipt is returned without re-invoking the LOBE cmdlet.

This is Option A of two design alternatives:

**Option A (implemented):** Idempotency is a Switchboard-level responsibility for
monetary transfer protocols. Rationale: cross-Society transfer idempotency is a
correctness invariant — a property that, if violated, produces an incorrect state in the
monetary ledger. Delegating it to individual LOBE implementations would require every
payment-adjacent LOBE to re-implement the same check, creating a fragile contract.

**Option B (not chosen):** Idempotency is the LOBE's responsibility. Rejected because
it creates a developer obligation that is easy to overlook and catastrophic to violate.

The Svrn7.Society.lobe.json descriptor DOES declare `transfer/1.0/order` in its
`protocols` array, and its `entrypoint` is correct. The Switchboard performs the
idempotency check BEFORE routing; the LOBE entry-point cmdlet does not perform it.

---

## 8. LOBE Registry and Dynamic Loading

### 8.1 Startup Sequence

At TDA Host startup, the `LobeManager` performs the following sequence:

1. Read `lobes.config.json` to obtain the eager and JIT LOBE module paths.
2. For each module path, locate the corresponding `.lobe.json` descriptor (same
   directory, same base name).
3. Parse each descriptor into a `LobeDescriptor` object.
4. Call `Switchboard.TryRegisterProtocol()` for each `protocols[]` entry in every
   descriptor. Epoch-gated protocols are registered but flagged with their
   `epochRequired` value.
5. Build the `InitialSessionState` with eager LOBE modules imported.
6. Inject `$SVRN7` (Svrn7RunspaceContext) and `$SVRN7_JIT_LOBES` session variables.
7. Open the `RunspacePool`.
8. Start the `FileSystemWatcher` on the LOBE directory (see Section 8.2).

### 8.2 FileSystemWatcher — Hot-Drop Loading

The `LobeManager` MUST watch the LOBE directory for new `*.lobe.json` files using
`System.IO.FileSystemWatcher`. When a new descriptor file is detected:

1. Parse the descriptor.
2. Validate required fields. If validation fails, log an error and skip.
3. Resolve `dependencies.lobes`: for each listed dependency, verify it is already
   registered. If not, attempt to load the dependency descriptor first.
4. Call `Switchboard.TryRegisterProtocol()` for each `protocols[]` entry.
5. Add the module path to the JIT registry (do NOT import the module yet — import
   happens on first message).

This sequence enables the following zero-downtime hot-drop workflow for third-party
LOBE deployment:

```
1. Drop {ModuleName}.psm1, {ModuleName}.psd1, {ModuleName}.lobe.json
   into the TDA's LOBE directory.
2. FileSystemWatcher detects {ModuleName}.lobe.json within milliseconds.
3. Protocol URIs registered in the Switchboard immediately.
4. Next inbound message of a matching @type triggers JIT import and dispatch.
5. TDA continues processing without restart.
```

### 8.3 LobeManager.EnsureLoadedAsync

The Switchboard calls `LobeManager.EnsureLoadedAsync(moduleName)` before the first
dispatch to a JIT LOBE. This method is idempotent: if the module is already loaded in
the target runspace (tracked via a per-runspace `HashSet<string>`), it returns
immediately. On the first call, it executes `Import-Module {path}` within the runspace
pool context.

Because PowerShell's `Import-Module` imports a module into the calling runspace (not
into the shared `InitialSessionState`, which is frozen once the pool opens), a JIT LOBE
is available in the runspace that imported it. If multiple runspaces are open, each must
import independently on first use. The import cost is incurred once per runspace.

### 8.4 Dependency Resolution

The `dependencies.lobes` array declares LOBE module names that MUST be imported before
the declaring LOBE. `LobeManager` performs a topological sort of the dependency graph
before building the `InitialSessionState`. Circular dependencies MUST be detected and
reported as a startup error.

---

## 9. Switchboard Dispatch Protocol

### 9.1 Dispatch Flow

For each inbound DIDComm message dequeued from `IInboxStore`, the Switchboard performs
the following dispatch protocol:

```
1. Read msg.Id (TDA resource DID URL), msg.MessageType (@type URI).

2. EPOCH GATE:
   Lookup registration by @type URI (Section 6).
   If registration.EpochRequired > CurrentEpoch:
     Mark Failed ("epoch {n} required for {type}").
     Return.

3. OPTION A CHECK (transfer/1.0/order only):
   If @type == "did:drn:svrn7.net/protocols/transfer/1.0/order":
     Check IProcessedOrderStore.GetReceiptAsync(msg.Id).
     If receipt found: Mark Processed. Return.

4. REGISTRY LOOKUP:
   Lookup registration by @type URI (exact match first, then prefix).
   If not found: Mark Failed ("no LOBE registered for {type}"). Return.

5. JIT IMPORT:
   Call LobeManager.EnsureLoadedAsync(registration.ModuleName).

6. PIPELINE INVOCATION:
   Open PS pipeline from RunspacePool.
   Execute: Get-TdaMessage -Did $msg.Id | {registration.Entrypoint} | Send-TdaMessage
   (Pass-by-reference: msg.Id is the DID URL, not the payload.)

7. OUTBOUND DELIVERY:
   For each OutboundMessage returned by the pipeline:
     Enqueue to outbound queue.
     Deliver via HttpClient.PostAsync to PeerEndpoint.

8. MARK PROCESSED:
   Call IInboxStore.MarkProcessedAsync(msg.Id).

9. ON ERROR (after Polly retry exhaustion):
   Call IInboxStore.MarkFailedAsync(msg.Id, error, retry: true, maxAttempts: 3).
```

### 9.2 Pass-by-Reference Pattern

The Switchboard MUST pass `msg.Id` (the TDA resource DID URL of the inbox message) to
the LOBE entry-point cmdlet, not the message payload. This is the pass-by-reference
pattern mandated by DSA 0.24:

```powershell
Get-TdaMessage -Did "did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1c8e9d7f012345678" |
    Receive-TdaEmail |
    Send-TdaMessage
```

The `Get-TdaMessage` cmdlet (defined in `Agent1-Coordinator.ps1`) resolves the message
from `IMemoryCache` (hot path) or `IInboxStore` (cold path) via
`$SVRN7.GetMessageAsync($messageDid)`.

---

## 10. Third-Party LOBE Developer Contract

### 10.1 Required Implementation

A third-party LOBE developer MUST:

1. **Provide a `.lobe.json` descriptor** conforming to this specification alongside
   the `.psm1` and `.psd1` files.

2. **Implement each `entrypoint` cmdlet** with the following minimum contract:
   - Accept `-MessageDid [string]` as a mandatory parameter (the TDA resource DID URL).
   - Optionally accept pipeline input via `ValueFromPipeline` or
     `ValueFromPipelineByPropertyName`.
   - Return an `OutboundMessage` hashtable `{ PeerEndpoint, PackedMessage, MessageType }`
     for messages that require a DIDComm reply, or `$null` for fire-and-forget handling.
   - Do NOT throw uncaught exceptions — catch all errors, log with `Write-Error`, and
     return `$null`. The Switchboard will mark the message Failed on exception.

3. **Never hold citizen private keys**. LOBEs receive a MessageDid reference. They
   resolve the message payload, but the TDA's `$SVRN7.Driver` object — not the LOBE —
   is responsible for cryptographic operations.

4. **Declare `idempotent: true` only if genuinely safe**. The Switchboard may retry
   failed messages up to `maxAttempts` times. A LOBE that declares `idempotent: true`
   but is not actually idempotent may produce duplicate state.

### 10.2 Recommended Practices

A third-party LOBE developer SHOULD:

1. Provide `inputSchema` and `outputSchema` on every cmdlet, even though optional in
   Epoch 0. This enables future AI pipeline composition without descriptor changes.

2. Declare `dependencies.lobes` for every LOBE whose cmdlets are called internally.
   This allows `LobeManager` to ensure correct import order.

3. Set all `ai.*` fields to meaningful values. Descriptive `ai.compositionHints` and
   `ai.limitations` are the primary way an AI discovers whether a LOBE is appropriate
   for a given pipeline task.

4. Use a namespaced `lobe.id` prefix derived from the developer's domain (e.g.,
   `io.myorg.health`) to avoid conflicts with other LOBEs.

5. Version LOBEs using semantic versioning and include a version suffix in the `lobe.id`
   when introducing breaking changes (e.g., `io.myorg.health-v2`).

6. Test the LOBE in isolation using `$SVRN7 = [MockSvrn7RunspaceContext]::new()` before
   integration testing within a live TDA.

### 10.3 Namespace Restrictions

Third-party LOBE developers MUST NOT register DIDComm `@type` URI patterns in the
`did:drn:svrn7.net/protocols/` namespace. This namespace is reserved for SVRN7 standard
protocols. Third-party protocols MUST use URIs in a namespace the developer controls.

---

## 11. AI Legibility Framework

### 11.1 Purpose (Epoch 0)

The `ai` block in each LOBE descriptor is informational in Epoch 0. Its purpose is to
make LOBEs discoverable, understandable, and composable by:

1. **Human developers** reading the descriptor to understand a LOBE's capabilities
   before writing a pipeline script.
2. **AI developers** (Claude or similar) being asked to suggest or construct a pipeline
   that uses one or more LOBEs.

### 11.2 The `_note` Field

Every LOBE descriptor MUST include an `ai._note` field with the following text (verbatim
or equivalent in meaning):

> "Epoch 0: these fields are informational for human and AI developers.
> inputSchema and outputSchema on each cmdlet are optional in Epoch 0.
> In a future epoch, an AI pipeline composer will consume this descriptor
> directly — possibly via an MCP-compatible tools/list interface — to discover,
> understand, and dynamically compose LOBE pipelines. The LOBE descriptor will
> become the MCP tool definition with no translation needed."

This field serves as a forward-compatibility marker. Its presence ensures that future
tooling can identify descriptors that were authored with the MCP-alignment design intent.

### 11.3 `compositionHints` Guidelines

`ai.compositionHints` MUST be written as complete, actionable sentences that could be
read verbatim by an AI developer constructing a pipeline. They SHOULD describe:

- Which cmdlet to use for a specific task.
- Which cmdlets chain naturally (piping `Receive-TdaEmail` output to `Send-TdaEmail`).
- Which cmdlets should NOT be chained (fire-and-forget cmdlets that return `$null`).
- Important precedence or ordering constraints.
- When to prefer one cmdlet over another.

Example:
```
"Chain Receive-TdaEmail after Get-TdaMessage for any pipeline handling email/1.0/* types."
"SenderDid in the returned record is authoritative — not the RFC 5322 From header."
"Receive-TdaEmail is idempotent: processing the same MessageDid twice is safe."
```

### 11.4 `inputSchema` and `outputSchema` Guidelines

Although optional in Epoch 0, LOBE authors are STRONGLY RECOMMENDED to provide JSON
Schema 2020-12 objects for `inputSchema` and `outputSchema` on every cmdlet. The schema
SHOULD accurately describe:

- **`inputSchema`**: Every named parameter the cmdlet accepts, with types, constraints
  (`minimum`, `maximum`, `maxLength`, `enum`), and `description` per field.
- **`outputSchema`**: The structure of the hashtable or object the cmdlet returns, with
  `required` fields listed and all properties described.

When these schemas are present, a future-epoch AI pipeline composer can:

1. Verify that the output schema of cmdlet A is compatible with the input schema of
   cmdlet B before chaining them in a pipeline.
2. Generate correct parameter values for cmdlet invocations.
3. Identify which cmdlets in a LOBE are pipeline sources (no `$MessageDid` input
   required) vs. pipeline transforms vs. sinks.

### 11.5 Future Epoch MCP Compatibility

When AI-aided pipeline construction arrives in a future epoch, the LOBE descriptor's
`cmdlets` array with `inputSchema`/`outputSchema` will be exactly what an AI needs to
reason about composability — the same way MCP tools enable AI reasoning about which
tools to chain. The Epoch 0 groundwork of putting proper schemas in the descriptor now
means the future-epoch AI pipeline composer can consume LOBE descriptors directly,
possibly via an MCP-compatible `tools/list` interface that wraps the LOBE registry. The
TDA could expose its registered LOBEs as MCP tools to an AI client — the LOBE descriptor
becomes the MCP tool definition with no translation needed.

The MCP alignment table:

| LOBE descriptor field          | MCP Tool field     | Notes                           |
|--------------------------------|--------------------|---------------------------------|
| `cmdlets[].name`               | `name`             | Direct equivalent               |
| `cmdlets[].title`              | `title`            | Direct equivalent               |
| `cmdlets[].description`        | `description`      | Direct equivalent               |
| `cmdlets[].inputSchema`        | `inputSchema`      | JSON Schema 2020-12             |
| `cmdlets[].outputSchema`       | `outputSchema`     | JSON Schema 2020-12             |
| `cmdlets[].annotations.idempotent`    | `idempotentHint`   | Direct equivalent        |
| `cmdlets[].annotations.modifiesState` | `readOnlyHint`     | Inverted                 |
| `cmdlets[].annotations.destructive`   | `destructiveHint`  | Direct equivalent        |
| `cmdlets[].annotations.pipelinePosition` | —              | TDA-specific             |
| `protocols[].uri`              | —                  | TDA-specific (no MCP parallel)  |

---

## 12. Standard SVRN7 LOBEs

The following nine LOBEs are shipped with the SVRN7 TDA Host v0.8.0.

### 12.1 Eager LOBEs (InitialSessionState)

| LOBE              | Module                 | Purpose                                          |
|-------------------|------------------------|--------------------------------------------------|
| Svrn7.Common      | Svrn7.Common.psm1      | Shared helpers. No protocol handlers.            |
| Svrn7.Federation  | Svrn7.Federation.psm1  | DID generation, key pairs, base registry.        |
| Svrn7.Society     | Svrn7.Society.psm1     | Citizen registration, transfers, membership.     |

### 12.2 JIT LOBEs (on-demand import)

| LOBE                   | Module                      | Protocols Handled                          |
|------------------------|-----------------------------|--------------------------------------------|
| Svrn7.Email            | Svrn7.Email.psm1            | email/1.0/*                                |
| Svrn7.Calendar         | Svrn7.Calendar.psm1         | calendar/1.0/*                             |
| Svrn7.Presence         | Svrn7.Presence.psm1         | presence/1.0/*                             |
| Svrn7.Notifications    | Svrn7.Notifications.psm1    | notification/1.0/*                         |
| Svrn7.Onboarding       | Svrn7.Onboarding.psm1       | onboard/1.0/*                              |
| Svrn7.Invoicing        | Svrn7.Invoicing.psm1        | invoice/1.0/*                              |

### 12.3 Option A Protocols (Switchboard special case)

The following protocols are declared in `Svrn7.Society.lobe.json` but receive special
handling in the Switchboard before being routed to the registered cmdlet:

| Protocol URI                                        | Special Case                        |
|-----------------------------------------------------|-------------------------------------|
| `did:drn:svrn7.net/protocols/transfer/1.0/order`   | Idempotency check via IProcessedOrderStore |

### 12.4 Standard Protocol URI Registry

| @type URI prefix                                         | LOBE              | Entrypoint                   | Epoch |
|----------------------------------------------------------|-------------------|------------------------------|-------|
| `did:drn:svrn7.net/protocols/transfer/1.0/request`      | Svrn7.Society     | Invoke-Svrn7IncomingTransfer | 0     |
| `did:drn:svrn7.net/protocols/transfer/1.0/order`        | Svrn7.Society     | Invoke-Svrn7IncomingTransfer | 1     |
| `did:drn:svrn7.net/protocols/transfer/1.0/order-receipt`| Svrn7.Society     | Confirm-Svrn7Settlement      | 1     |
| `did:drn:svrn7.net/protocols/onboard/1.0/`              | Svrn7.Onboarding  | ConvertFrom-TdaOnboardRequest| 0     |
| `did:drn:svrn7.net/protocols/email/1.0/`                | Svrn7.Email       | Receive-TdaEmail             | 0     |
| `did:drn:svrn7.net/protocols/calendar/1.0/invite`       | Svrn7.Calendar    | Receive-TdaMeetingRequest    | 0     |
| `did:drn:svrn7.net/protocols/calendar/1.0/`             | Svrn7.Calendar    | Import-TdaCalendarEvent      | 0     |
| `did:drn:svrn7.net/protocols/presence/1.0/subscribe`    | Svrn7.Presence    | Add-TdaPresenceSubscription  | 0     |
| `did:drn:svrn7.net/protocols/presence/1.0/`             | Svrn7.Presence    | Update-TdaPresence           | 0     |
| `did:drn:svrn7.net/protocols/notification/1.0/`         | Svrn7.Notifications| Invoke-TdaNotification      | 0     |
| `did:drn:svrn7.net/protocols/invoice/1.0/`              | Svrn7.Invoicing   | ConvertFrom-TdaInvoiceRequest| 0     |
| `did:drn:svrn7.net/protocols/did/1.0/resolve-request`   | Svrn7.Society     | Resolve-Svrn7Did             | 0     |

---

## 13. Example: Third-Party Health LOBE

The following is a complete illustrative example of a third-party LOBE descriptor for a
hypothetical health domain extension developed by an independent third party.

### 13.1 `MyOrg.Health.lobe.json`

```json
{
  "lobe": {
    "id":           "io.myorg.health",
    "name":         "MyOrg.Health",
    "title":        "MyOrg Health Domain LOBE",
    "description":  "Handles prescription and referral messaging for health-domain Society TDAs. Implements the MyOrg Health DIDComm protocol suite. Requires a health-domain Society deployment with the HealthCredentialSchema registered in the Schema Registry.",
    "version":      "1.0.0",
    "author":       "Jane Developer",
    "organization": "My Organisation",
    "website":      "https://developer.myorg.io/health-lobe",
    "license":      "Apache-2.0",
    "epochRequired": 0,
    "module":       "MyOrg.Health.psm1"
  },
  "protocols": [
    {
      "uri":          "https://health.myorg.io/protocols/prescription/1.0/request",
      "title":        "Prescription Request",
      "description":  "Citizen requests a prescription. Body: { patientDid, prescriberId, medication, dosage, duration }",
      "direction":    "inbound",
      "match":        "exact",
      "entrypoint":   "Receive-HealthPrescriptionRequest",
      "epochRequired": 0
    },
    {
      "uri":          "https://health.myorg.io/protocols/referral/1.0/",
      "title":        "Referral Messages",
      "description":  "All referral protocol messages. Body format varies by subtype.",
      "direction":    "inbound",
      "match":        "prefix",
      "entrypoint":   "Receive-HealthReferral",
      "epochRequired": 0
    }
  ],
  "cmdlets": [
    {
      "name":         "Receive-HealthPrescriptionRequest",
      "title":        "Receive Prescription Request",
      "description":  "Processes an inbound prescription request. Validates patient membership, checks prescriber credentials, and stores the prescription record. Returns $null (fire-and-forget — prescriber is notified asynchronously).",
      "epochRequired": 0,
      "annotations": {
        "idempotent":       true,
        "modifiesState":    true,
        "requiresEpoch":    0,
        "destructive":      false,
        "pipelinePosition": "source"
      },
      "inputSchema": {
        "type": "object",
        "required": ["MessageDid"],
        "properties": {
          "MessageDid": {
            "type": "string",
            "description": "TDA resource DID URL of the inbox message.",
            "pattern": "^did:drn:[^/]+/inbox/msg/[0-9a-f]{24}$"
          }
        }
      },
      "outputSchema": null,
      "pipelineExample": "Get-TdaMessage -Did $MessageDid | Receive-HealthPrescriptionRequest"
    }
  ],
  "dependencies": {
    "lobes":    ["Svrn7.Society"],
    "packages": []
  },
  "ai": {
    "_note": "Epoch 0: these fields are informational for human and AI developers. inputSchema and outputSchema on each cmdlet are optional in Epoch 0. In a future epoch, an AI pipeline composer will consume this descriptor directly — possibly via an MCP-compatible tools/list interface — to discover, understand, and dynamically compose LOBE pipelines. The LOBE descriptor will become the MCP tool definition with no translation needed.",
    "summary":          "Handles medical prescription and referral messaging for health-domain Society TDAs. Requires a health-domain Society deployment.",
    "useCases":         [
      "Processing electronic prescription requests from citizen patients",
      "Routing specialist referrals between health provider TDAs"
    ],
    "compositionHints": [
      "Receive-HealthPrescriptionRequest is fire-and-forget — it returns $null, do not pipe its output",
      "This LOBE depends on Svrn7.Society — call Test-Svrn7SocietyMember before processing any request"
    ],
    "limitations":      [
      "Epoch 0: no HL7 FHIR integration. Prescription data is plain JSON, not FHIR-encoded.",
      "Requires Svrn7.Society LOBE to be loaded (declared in dependencies.lobes)"
    ]
  }
}
```

### 13.2 Deployment Steps

```
1. Obtain: MyOrg.Health.psm1, MyOrg.Health.psd1, MyOrg.Health.lobe.json
2. Drop all three files into the TDA's LOBE directory (e.g., ./lobes/)
3. FileSystemWatcher detects MyOrg.Health.lobe.json
4. LobeManager verifies Svrn7.Society is loaded (dependency satisfied)
5. Switchboard registers:
     exact:  https://health.myorg.io/protocols/prescription/1.0/request
             → Receive-HealthPrescriptionRequest
     prefix: https://health.myorg.io/protocols/referral/1.0/
             → Receive-HealthReferral
6. Any incoming TDA message with @type matching these URIs is now routed
   to MyOrg.Health — no TDA restart required.
```

---


---

## 13a. Dead-Letter Outbox

Failed outbound DIDComm messages — those for which HttpClient delivery fails after
Polly retry exhaustion — MUST be persisted to the `IOutboxStore` dead-letter outbox
rather than silently discarded.

The outbox is backed by `svrn7-inbox.db` (the same LiteDB file as the inbox, shared
via `InboxLiteContext`). Records are stored in an `Outbox` collection:

```
Id:           TDA resource DID URL (did:drn:{networkId}/inbox/outbox/{objectId})
PeerEndpoint: target TDA HTTP/2 endpoint URL
PackedMessage:packed DIDComm message payload
MessageType:  DIDComm @type URI (or "outbound" for Switchboard-generated messages)
FailedAt:     timestamp of final delivery failure
AttemptCount: number of delivery attempts made
LastError:    error message from final attempt
IsRetried:    false until operator marks as retried
```

`IOutboxStore` interface (Svrn7.Core):
```csharp
Task EnqueueAsync(OutboxRecord record, CancellationToken ct);
Task<IReadOnlyList<OutboxRecord>> GetPendingAsync(CancellationToken ct);
Task MarkRetriedAsync(string id, CancellationToken ct);
```

Operator tooling SHOULD call `GetPendingAsync()` to retrieve unretried records and
`MarkRetriedAsync()` after manual retry or resolution. Automated retry is deferred
to a future epoch.

### 13a.1 MCP Alignment Update

Since the initial publication of this draft, the cmdlet definition structure has been
formally aligned with the MCP Tool definition [MCP-2025-11-25]:

| LOBE descriptor field                  | MCP Tool field      | Notes                          |
|----------------------------------------|---------------------|--------------------------------|
| `cmdlets[].name`                       | `name`              | Direct equivalent              |
| `cmdlets[].title`                      | `title`             | Direct equivalent              |
| `cmdlets[].description`                | `description`       | Direct equivalent              |
| `cmdlets[].inputSchema`                | `inputSchema`       | JSON Schema 2020-12            |
| `cmdlets[].outputSchema`               | `outputSchema`      | JSON Schema 2020-12            |
| `cmdlets[].annotations.idempotent`     | `idempotentHint`    | Direct equivalent              |
| `cmdlets[].annotations.modifiesState`  | `readOnlyHint`      | Inverted                       |
| `cmdlets[].annotations.destructive`    | `destructiveHint`   | Direct equivalent              |
| `cmdlets[].annotations.pipelinePosition` | —               | TDA-specific                   |
| `cmdlets[].annotations.requiresEpoch`  | —                   | TDA-specific                   |
| `protocols[].uri` + `match`            | —                   | TDA-specific DIDComm routing   |

The `ai._note` field on every standard LOBE descriptor states the future-epoch intent:

> "When AI-aided pipeline construction arrives in a future epoch, the LOBE descriptor's
> cmdlets array with JSON Schema inputSchema/outputSchema will be exactly what an AI
> needs to reason about composability — the same way MCP tools enable AI reasoning about
> which tools to chain. The TDA could expose its registered LOBEs as MCP tools to an AI
> client — the LOBE descriptor becomes the MCP tool definition with no translation
> needed."

## 14. Security Considerations

### 14.1 LOBE Descriptor Trust

LOBE descriptors are NOT cryptographically signed in Epoch 0. A TDA operator MUST
verify the provenance of third-party LOBE files through out-of-band means before
placing them in the LOBE directory. The TDA LOBE directory MUST be protected from
unauthorised write access by filesystem permissions.

In a future epoch, LOBE descriptors SHOULD be signed by the LOBE author's DID and
verified by the TDA before registration.

### 14.2 Protocol Namespace Hijacking

A malicious LOBE could register a URI in the `did:drn:svrn7.net/protocols/` namespace,
intercepting SVRN7 standard protocol messages. The Switchboard MUST reject any
registration attempt whose URI begins with a reserved namespace prefix unless the
registering LOBE is a standard SVRN7 LOBE (identified by `lobe.id` prefix `svrn7.`).

### 14.3 RunspacePool Isolation

PowerShell runspaces in the pool do not share session state between runspaces. A LOBE
running in one runspace cannot access the `$SVRN7` session variable of another runspace
directly. All shared state access MUST go through `$SVRN7.Driver`, `$SVRN7.Inbox`,
`$SVRN7.Cache`, or `$SVRN7.ProcessedOrders`, which are thread-safe.

### 14.4 Citizen Private Key Protection

LOBEs MUST NOT accept, store, or transmit citizen private keys. The Society never
holds citizen private keys. The only cryptographic material a LOBE handles is the
citizen's public key hex (`publicKeyHex`) received in an onboarding request body.

### 14.5 Idempotency Declarations

A LOBE that incorrectly declares `annotations.idempotent: true` when the cmdlet is
not actually idempotent may produce duplicate database records, duplicate transfer
credits, or duplicate VC issuances on retry. LOBE authors MUST verify idempotency
before making this declaration.

---

## 15. IANA Considerations

This document has no IANA actions.

---

## 16. References

### Normative

- [RFC2119]   Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174]   Leiba, B. Ambiguity of Uppercase vs Lowercase in RFC 2119. May 2017.
- [RFC8259]   Bray, T. The JSON Data Interchange Format. December 2017.

### Informative

- [DRAFT-PPML]   Herman, M. Parchment Programming Modeling Language (PPML).
                 draft-herman-parchment-programming-00. Web 7.0 Foundation, 2026.
- [WEB70-ARCH]   Herman, M. Web 7.0 Society Architecture.
                 draft-herman-web7-society-architecture-00. Web 7.0 Foundation, 2026.
- [DRAFT-DID-DRN] Herman, M. Decentralized Resource Name (DRN) DID Method.
                  draft-herman-did-w3c-drn-00. Web 7.0 Foundation, 2026.
- [DRAFT-DIDCOMM] Herman, M. SOVRONA (SVRN7) DIDComm Transfer Protocol.
                  draft-herman-didcomm-svrn7-transfer-00. Web 7.0 Foundation, 2026.
- [DRAFT-RESOURCE] Herman, M. Web 7.0 TDA Resource Addressing using DID URL Paths.
                   draft-herman-drn-resource-addressing-00. Web 7.0 Foundation, 2026.
- [MCP-2025-11-25] Model Context Protocol Specification, Version 2025-11-25.
                   https://modelcontextprotocol.io/specification/2025-11-25.
                   Anthropic / Agentic AI Foundation (Linux Foundation), 2025.

---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
URI:   https://hyperonomy.com/about/
