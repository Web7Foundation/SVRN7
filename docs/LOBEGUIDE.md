# Designing a New SVRN7 LOBE

A LOBE (Lightweight Operational Behaviour Engine) is a PowerShell module that the TDA
Switchboard loads into an isolated runspace and invokes when a DIDComm message arrives.
Each LOBE owns one or more DIDComm protocol URIs.  The Switchboard routes by `@type`
→ LOBE entrypoint cmdlet → PowerShell pipeline → optional outbound reply.

---

## Step 1 — Define the Protocol URI(s)

Every inbound DIDComm message type the LOBE handles needs a URI.

Convention:

```
did:drn:svrn7.net/protocols/{domain}/{version}/{action}
```

Examples:
- `did:drn:svrn7.net/protocols/Svrn7.Onboarding/0.8/register-citizen`
- `did:drn:svrn7.net/protocols/Svrn7.Invoicing/0.8/request`
- `did:drn:svrn7.net/protocols/payments/1.0/request`

One URI maps to exactly one entrypoint cmdlet.  If the LOBE handles multiple message
types (request + confirmation, for example), define a URI and entrypoint for each.

### Accepted `{action}` names

Action names are lowercase and hyphen-separated.  They describe the message's intent
from the **sender's** perspective.  Use an existing name when it fits; coin a new one
only when none of these apply.

#### Transactional — state-changing operations

| Action | Meaning | Example URI |
|---|---|---|
| `request` | Initiate an operation; expects a receipt or result in return | `Svrn7.Society/0.8/transfer-request` |
| `order` | Issue a formal cross-Society instruction | `Svrn7.Society/0.8/transfer-order` |
| `init` | One-time initialisation of a resource | `federation/1.0/initialize-federation` |
| `register` | Register a new entity in a registry | `federation/1.0/register-society` |
| `add` | Add a sub-entity to an existing resource | `society/1.0/citizen-did-add` |
| `update` | Modify fields of an existing entity | `identity/1.0/did-update` |
| `revoke` | Permanently revoke a credential or permission | `Svrn7.Identity/0.8/vc-revoke` |
| `cancel` | Cancel an in-progress operation | `Svrn7.Invoicing/0.8/cancel` |

#### Confirmations and responses

| Action | Meaning | Example URI |
|---|---|---|
| `receipt` | Acknowledge completion of a transaction | `Svrn7.Society/0.8/transfer-order-receipt` |
| `result` | Return the outcome of a request or query | `federation/1.0/initialize-federation-result` |
| `response` | Reply to an invite or proposal (accept / decline encoded in body) | `calendar/1.0/response` |
| `confirm` | Confirm a pending two-phase action | `transfer/1.0/settlement-confirm` |
| `reject` | Explicitly decline a request | `Svrn7.Invoicing/0.8/reject` |
| `error` | Signal a protocol-level error | `Svrn7.Onboarding/0.8/error` |

#### Queries — read-only, no state change

| Action | Meaning | Example URI |
|---|---|---|
| `query` | Ask for information; expects a `result` in return | `society/1.0/member-query` |
| `resolve-request` | Resolve a specific named resource (DID, VC, schema) | `Svrn7.Identity/0.8/did-resolve-request` |

#### Events and notifications — fire-and-forget, no reply expected

| Action | Meaning | Example URI |
|---|---|---|
| `notify` / `notification` | Push a general notification | `ux/1.0/notification` |
| `alert` | Push an urgent or high-priority notification | `Svrn7.Notifications/0.8/alert` |
| `event` | Announce a domain event | `calendar/1.0/event` |
| `status` | Push current status of a resource | `presence/1.0/status` |
| `balance-update` | Push an updated balance figure | `ux/1.0/balance-update` |

#### Subscription lifecycle

| Action | Meaning | Example URI |
|---|---|---|
| `subscribe` | Subscribe to an ongoing event stream | `presence/1.0/subscribe` |
| `unsubscribe` | Remove a subscription | `presence/1.0/unsubscribe` |
| `invite` | Invite a participant to join something | `calendar/1.0/invite` |
| `accept` | Accept an invitation or proposal | `calendar/1.0/accept` |
| `decline` | Decline an invitation or proposal | `calendar/1.0/decline` |

#### Compound action names

When the domain alone is ambiguous, prefix the action with a noun:

| Pattern | Example |
|---|---|
| `{noun}-{verb}` | `federation-query`, `did-method-register`, `resolve-request` |
| `{verb}-{noun}` | `order-receipt`, `balance-update`, `registration-complete` |

---

## Step 2 — Define the Inbound Message Body

Specify every field the handler will read from `$msg.PackedPayload`.

For each field state:
- **Name** — camelCase, matching the JSON key exactly
- **Type** — string, integer, boolean, array, object
- **Required or optional**
- **Description / constraints** — DID format, max length, valid range, etc.

> **Rule:** Under `Set-StrictMode -Version Latest` (enforced in all LOBEs), reading a
> property that is absent on a `PSCustomObject` throws immediately.  Use
> `Assert-BodyFields` for required fields and `Get-BodyField` for optional ones.
> Both helpers are defined in `Svrn7.Common.psm1`.

---

## Step 3 — Define the Business Logic

Describe what the handler does after parsing the body:

- Which driver method(s) it calls: `$SVRN7.Driver.*` (Society context) or
  `Get-ActiveFederationDriver` (Federation context)
- What validations it applies before calling the driver
- What state it changes (wallet, DID registry, VC registry, Merkle log, etc.)
- Whether the operation is idempotent
- What exceptions it may throw and whether the Switchboard should retry
  (transactional protocols dead-letter on first failure; non-transactional retry up to
  `NonTransactionalMaxAttempts`)

---

## Step 4 — Define the Reply (if any)

Not all LOBEs send a reply.  If this one does, specify:

| Item | Detail |
|---|---|
| Outbound `@type` URI | e.g. `did:drn:svrn7.net/protocols/payments/1.0/receipt` |
| Reply body fields | camelCase JSON field names and types |
| Endpoint resolution | `replyEndpoint` in the body (TDA-to-TDA) **or** DID Document lookup via `Resolve-SocietySenderEndpoint` (peer DID already registered) |

If the sender may be either a script tool or a TDA, support both:

```powershell
$replyEndpoint = if ($body.PSObject.Properties['replyEndpoint']) { $body.replyEndpoint }
                 elseif ($msg.FromDid) { Resolve-SocietySenderEndpoint -Did $msg.FromDid }
if (-not $replyEndpoint) {
    Write-Warning "MyLobe: no reply endpoint — result not delivered."
    return
}
```

---

## Step 5 — Choose Load Mode

| Mode | When to use | Effect |
|---|---|---|
| **Eager** | High-frequency protocols; foundational (Common, Federation, Society) | Module is loaded into the Initial Session State — always available in every runspace, zero import latency |
| **JIT** | Infrequently triggered protocols; large or specialised modules | Module is imported into the dedicated runspace only when a matching message arrives |

Default to **JIT**.  Use eager only if per-message import latency is measurable and
unacceptable for the protocol's SLA.

---

## Step 6 — Name the Cmdlets

PowerShell requires the `{ApprovedVerb}-{Noun}` pattern for all cmdlets.
Non-approved verbs produce a warning on module import.  Run `Get-Verb` in PowerShell
to see the full approved list.

### Noun prefix convention

All public cmdlets in SVRN7 LOBEs prefix the noun with `Pando` (protocol handlers) or
`Svrn7` (driver wrappers):

| Prefix | Use | Examples |
|---|---|---|
| `Pando` | DIDComm message handlers and message builders | `Invoke-PandoPaymentRequest`, `New-PandoPaymentReceipt` |
| `Svrn7` | Driver wrappers, key operations, standalone utilities | `Register-Svrn7CitizenInSociety`, `Get-Svrn7Balance` |

### Approved verbs used in SVRN7 LOBEs

| Verb | Group | LOBE use |
|---|---|---|
| `Invoke` | Lifecycle | Entrypoint for an inbound DIDComm protocol handler |
| `Get` | Common | Retrieve data from the driver, store, or runspace context |
| `New` | Common | Construct an outbound message or in-memory object |
| `Send` | Communications | Enqueue or deliver a DIDComm message |
| `ConvertFrom` | Data | Parse an inbound DIDComm message body into a typed object |
| `Register` | Lifecycle | Register an entity (citizen, society, DID method) |
| `Unregister` | Lifecycle | Remove a registration |
| `Resolve` | Diagnostic | Look up a DID, VC, or service endpoint |
| `Test` | Diagnostic | Boolean check — active, member, valid, etc. |
| `Initialize` | Lifecycle | One-time setup (assemblies, driver singleton) |
| `Assert` | Lifecycle | Validate; throw on failure (e.g. `Assert-BodyFields`) |
| `Build` | Lifecycle | Construct a canonical data structure (e.g. canonical transfer JSON) |
| `Confirm` | Lifecycle | Acknowledge or finalise a pending two-phase operation |
| `Add` | Common | Add a sub-entity to an existing resource |
| `Remove` | Common | Delete or clean up a resource |
| `Set` | Common | Update one or more fields of an existing entity |
| `Update` | Data | Replace the full state of an existing entity |
| `Revoke` | Security | Permanently revoke a credential or permission |
| `Import` | Data | Load external data or assemblies into the runspace |

### Standard entrypoint naming pattern

| Message direction | Verb | Example |
|---|---|---|
| Inbound handler (entry) | `Invoke-Pando{Domain}{Action}` | `Invoke-PandoPaymentRequest` |
| Inbound parser (pipeline source) | `ConvertFrom-Pando{Domain}Request` | `ConvertFrom-PandoInvoiceRequest` |
| Outbound builder (pipeline sink) | `New-Pando{Domain}Receipt` / `New-Pando{Domain}Result` | `New-PandoInvoiceReceipt` |
| Error reply | `Send-Pando{Domain}Error` | `Send-PandoOnboardError` |

---

## Step 7 — Identify Dependencies

List any other LOBEs whose cmdlets this LOBE calls.

- Eager LOBEs (Common, Federation, Society, UX) are always available — no import needed.
- JIT LOBEs must be imported explicitly if called cross-LOBE (uncommon).
- C# assemblies are loaded via `Initialize-Svrn7Assemblies` in Common — no additional
  setup needed in the LOBE itself.

---

## Step 7 — Choose Minimum Epoch

| Value | Meaning |
|---|---|
| `0` | Available from genesis (Endowment epoch) |
| `1` | Requires EcosystemUtility epoch (cross-Society transfers enabled) |
| `2` | Requires Market epoch |

The Switchboard enforces the epoch gate before dispatching to the LOBE.

---

## Step 8 — Create the Files

Given LOBE name `Svrn7.MyLobe`, create:

```
src/Svrn7.TDA/lobes/
└── Svrn7.MyLobe/
    ├── Svrn7.MyLobe.psm1        ← PowerShell module (cmdlets)
    ├── Svrn7.MyLobe.Impl.psm1   ← internal helpers (optional — see below)
    └── Svrn7.MyLobe.lobe.json   ← LOBE descriptor
```

Then register in `src/Svrn7.TDA/lobes/lobes.config.json`:

```json
{
  "eager": [ ... ],
  "jit":   [ ..., "Svrn7.MyLobe/Svrn7.MyLobe.psm1" ]
}
```

### The `.Impl.psm1` pattern

Place internal functions that are specific to a LOBE — and not intended for use by other
LOBEs — in a companion `{LobeName}.Impl.psm1` file in the same folder.  The main
`.psm1` loads it with a `PSScriptRoot`-relative import:

```powershell
Import-Module "$PSScriptRoot/Svrn7.MyLobe.Impl.psm1"
```

**When to use it:**

| Situation | Guidance |
|---|---|
| Wrapping a pre-existing PS module that predates the LOBE | Put the pre-existing module (unmodified) in `{LobeName}.Impl.psm1`; the main `.psm1` is a thin DIDComm adapter only |
| LOBE has private helper functions not exported to other LOBEs | Move them to `{LobeName}.Impl.psm1` to keep the main module focused on the public protocol surface |
| Functions shared across multiple LOBEs | Do **not** use `.Impl.psm1` — place shared helpers in `Svrn7.Common.psm1` instead |

The `.Impl.psm1` file is picked up automatically by the existing `.csproj` glob
(`lobes/**/*.psm1`) and copied to the build output — no project file changes needed.

---

## Step 9 — Write the .psm1

Module header:

```powershell
#Requires -Version 7.2
#Requires -PSEdition Core
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
```

Entrypoint cmdlet skeleton:

```powershell
function Invoke-PandoMyLobeRequest {
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )
    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { throw "Invoke-PandoMyLobeRequest: message '$MessageDid' not found." }

        $body = $msg.PackedPayload | ConvertFrom-Json
        Assert-BodyFields $body @('requiredField1', 'requiredField2') 'Invoke-PandoMyLobeRequest'
        $optionalField = Get-BodyField $body 'optionalField' $null

        # --- business logic ---

        $endpoint = if ($body.PSObject.Properties['replyEndpoint']) { $body.replyEndpoint }
                    elseif ($msg.FromDid) { Resolve-SocietySenderEndpoint -Did $msg.FromDid }
        if (-not $endpoint) {
            Write-Warning "Invoke-PandoMyLobeRequest: no reply endpoint — result not delivered."
            return
        }

        return @{
            PeerEndpoint  = $endpoint
            PackedMessage = (@{ success = $true } | ConvertTo-Json -Compress)
            MessageType   = 'did:drn:svrn7.net/protocols/mylobe/1.0/result'
        }
    }
}

Export-ModuleMember -Function @('Invoke-PandoMyLobeRequest')
```

---

## Step 10 — Write the .lobe.json Descriptor

Minimum required structure:

```json
{
  "lobe": {
    "id":            "svrn7.mylobe",
    "name":          "Svrn7.MyLobe",
    "title":         "Human-readable title",
    "description":   "What this LOBE does.",
    "version":       "0.1.0",
    "author":        "Michael Herman",
    "organization":  "Web 7.0 Foundation",
    "website":       "https://svrn7.net",
    "license":       "MIT",
    "epochRequired": 0,
    "module":        "Svrn7.MyLobe.psm1"
  },
  "protocols": [
    {
      "uri":           "did:drn:svrn7.net/protocols/mylobe/1.0/request",
      "title":         "MyLobe Request",
      "description":   "What this message does. Body: { requiredField1, requiredField2 }",
      "direction":     "inbound",
      "match":         "prefix",
      "entrypoint":    "Invoke-PandoMyLobeRequest",
      "epochRequired": 0
    }
  ],
  "cmdlets": [],
  "dependencies": {
    "lobes":    ["Svrn7.Society"],
    "packages": []
  }
}
```

---

## Checklist

- [ ] Protocol URI(s) defined and unique across all LOBEs
- [ ] Inbound body schema documented (required vs optional fields)
- [ ] Business logic calls driver methods via `$SVRN7.Driver` or `Get-ActiveFederationDriver`
- [ ] All body field access uses `Assert-BodyFields` / `Get-BodyField`
- [ ] Reply endpoint resolved with `replyEndpoint` body field → DID Document fallback → warning
- [ ] `Export-ModuleMember` lists all public cmdlets
- [ ] `.lobe.json` `protocols[].entrypoint` matches the PowerShell function name exactly
- [ ] `lobes.config.json` updated (eager or jit array)
- [ ] Load mode chosen and justified
- [ ] Epoch set correctly

---

## Appendix A — `$SVRN7` Runspace Context API

Every cmdlet running inside a TDA runspace has access to `$SVRN7` (`Svrn7RunspaceContext`).
Do not call `Get-ActiveSocietyDriver` or `Get-ActiveFederationDriver` when inside a TDA
runspace — use `$SVRN7.Driver` directly (the helpers do this automatically in TDA context).

| Member | Type | Description |
|---|---|---|
| `$SVRN7.Driver` | `ISvrn7SocietyDriver` | Full Society + Federation driver. All 44 driver methods are available. See Appendix E. |
| `$SVRN7.CurrentEpoch` | `int` | The current epoch (0, 1, or 2). Refreshed from the driver every 60 seconds. |
| `$SVRN7.GetMessageAsync(messageDid, ct)` | `Task<InboxMessageView?>` | Retrieves the inbox message by its DID URL. Hot path: IMemoryCache (24 h TTL); cold path: IInboxStore. Returns `$null` when not found. |

### Typical handler entry pattern

```powershell
function Invoke-PandoMyLobeRequest {
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )
    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { throw "Invoke-PandoMyLobeRequest: message '$MessageDid' not found." }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        # ...
    }
}
```

---

## Appendix B — `InboxMessageView` Fields

`InboxMessageView` is the read-only projection passed to LOBE cmdlets.
It is returned by `$SVRN7.GetMessageAsync(messageDid)`.

| Property | Type | Description |
|---|---|---|
| `Id` | `string` | TDA resource DID URL — `did:drn:{network}/inbox/msg/{objectId}`. This is the value of `$MessageDid` passed to the entrypoint. |
| `MessageType` | `string` | The `@type` value from the DIDComm envelope (e.g. `did:drn:svrn7.net/protocols/Svrn7.Invoicing/0.8/request`). |
| `PackedPayload` | `string` | The raw JSON string of the DIDComm `body` field. Parse with `ConvertFrom-Json`. |
| `FromDid` | `string?` | The `from` field of the DIDComm envelope. `$null` if the sender omitted it. Check before calling `Resolve-SocietySenderEndpoint`. |
| `AttemptCount` | `int` | Number of processing attempts so far (0 on first delivery). |

---

## Appendix C — `Svrn7.Common.psm1` Helper Reference

These helpers are dot-sourced into `Svrn7.Federation.psm1` and `Svrn7.Society.psm1`
and are therefore available in every eager LOBE runspace.  JIT LOBEs that are not
children of Federation or Society must dot-source `Svrn7.Common.psm1` explicitly.

> Do **not** add `#Requires` to `Svrn7.Common.psm1` — it causes PowerShell 7 to treat
> the dot-source as a module-context load, scoping functions to a private scope instead
> of the caller's.

### Assembly management

| Cmdlet | Description |
|---|---|
| `Initialize-Svrn7Assemblies [-ModuleRoot <path>]` | Loads the eight Svrn7 DLLs into the runspace. Reads `$env:SVRN7_BIN_PATH` first; falls back to a `bin/` sibling of the module root, then to two levels up (TDA debug layout). Idempotent — safe to call multiple times. |

### Driver accessors

| Cmdlet | Description |
|---|---|
| `Get-ActiveSocietyDriver` | Returns `$SVRN7.Driver` in a TDA runspace, or the standalone `$Script:SocietyDriver` in PowerShell module mode. Throws `InvalidOperationException` when neither is initialised. |
| `Get-ActiveFederationDriver` | Same as above for the federation driver (`ISvrn7Driver`). In TDA context `$SVRN7.Driver` implements both interfaces — this returns the same object. |

### Body field helpers (strict-mode safe)

| Cmdlet | Signature | Description |
|---|---|---|
| `Assert-BodyFields` | `($Body, $Required[], $Caller)` | Throws a clear per-field message for any required field that is absent or empty. Use for every required body field. |
| `Get-BodyField` | `($Body, $Field, $Default = $null)` | Returns the field value, or `$Default` when absent. Never touches a missing property — safe under `Set-StrictMode -Version Latest`. |

Probe for boolean flags without calling these helpers:

```powershell
$success = $body.PSObject.Properties['success'] -and $body.success
```

### DIDComm endpoint resolution

| Cmdlet | Description |
|---|---|
| `Resolve-SocietySenderEndpoint -Did <string>` | Resolves the DIDComm `serviceEndpoint` from the sender's DID Document. Returns `$null` when no `DIDComm`-type service entry exists — **never throws**. Callers must handle `$null`. |

Standard three-step reply endpoint pattern:

```powershell
$replyEndpoint = if ($body.PSObject.Properties['replyEndpoint']) { $body.replyEndpoint }
                 else { Resolve-SocietySenderEndpoint -Did $msg.FromDid }
if (-not $replyEndpoint) {
    Write-Warning "Invoke-PandoMyLobeRequest: no reply endpoint — result not delivered."
    return
}
```

### Operation result

| Cmdlet | Description |
|---|---|
| `Resolve-OperationResult -Result <OperationResult> -Operation <string>` | Throws a typed `ErrorRecord` when `Result.Success` is `$false`; returns `$Result` when successful. Wraps driver calls that return `OperationResult`. |

### Canonical transfer JSON

| Cmdlet | Description |
|---|---|
| `Build-CanonicalTransferJson -PayerDid -PayeeDid -AmountGrana -Nonce -Timestamp -Memo` | Serialises a transfer payload with normative field ordering per draft-herman-svrn7-monetary-protocol-00 §5.2. Used as the pre-image for Blake3 nonce generation and the transfer ID. |

### DIDComm HTTP/2 sender

| Cmdlet | Description |
|---|---|
| `Send-DIDCommMessage [-Uri <url>] -Body <json> [-ContentType <mime>]` | Posts a plaintext DIDComm message to a TDA endpoint over h2c (HTTP/2 cleartext). Enforces `HttpVersionPolicy.RequestVersionExact` — `Invoke-RestMethod` cannot be used for h2c. Default URI: `http://localhost:8443/didcomm`. |

### Inbox accessor

| Cmdlet | Description |
|---|---|
| `Get-PandoMessage -Did <string>` | Retrieves an `InboxMessageView` from `$SVRN7`. Throws when not in a TDA runspace or when the message is not found. Used as a pipeline source. |

---

## Appendix D — Outbound Return Contract

A LOBE handler signals outbound delivery by returning a `[Svrn7.TDA.OutboundMessage]` instance.
Returning `$null`, an empty pipeline, or a bare `return` means no reply is sent.

### With reply

```powershell
$payload = @{ ... } | ConvertTo-Json -Compress   # the data to send

$envelope = [ordered]@{
    typ  = 'application/didcomm-plain+json'
    id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
    type = 'did:drn:svrn7.net/protocols/domain/1.0/result'   # outbound @type URI
    from = $SVRN7.Driver.SocietyDid
    to   = @($msg.FromDid)
    body = $payload   # $payload is a JSON string; stored as a string literal in the envelope
} | ConvertTo-Json -Compress

[Svrn7.TDA.OutboundMessage]::new($endpoint, $envelope)
```

| Parameter | Type | Description |
|---|---|---|
| `PeerEndpoint` (1st arg) | `string` | HTTP/2 (h2c) URL of the recipient's TDA endpoint (e.g. `http://peer.svrn7.net:8443`). Resolved via `replyEndpoint` body field or `Resolve-SocietySenderEndpoint`. |
| `PackedMessage` (2nd arg) | `string` | Full DIDComm plaintext envelope (`typ`/`id`/`type`/`from`/`to`/`body`). The Switchboard POSTs this verbatim; the recipient's `KestrelListenerService` routes on the `type` field. |

### No reply

```powershell
# Either of these:
return          # bare return — Switchboard marks message Processed, no outbound delivery
return $null    # explicit null
                # or simply fall off the end of the process block
```

The Switchboard calls `MarkProcessedAsync` on the inbox message in both cases.
A delivery failure (unreachable `PeerEndpoint`) dead-letters the outbound record but
does **not** re-queue the inbound message.

---

## Appendix E — Driver API Surface

`$SVRN7.Driver` implements `ISvrn7SocietyDriver`, which extends `ISvrn7Driver`.
Call all methods synchronously inside the runspace with `.GetAwaiter().GetResult()`.

### `ISvrn7Driver` (Federation-level)

| Method | Return type | Description |
|---|---|---|
| `GetCurrentEpoch()` | `int` | Current epoch (0, 1, or 2). Prefer `$SVRN7.CurrentEpoch` — it caches with a 60 s refresh. |
| `RegisterCitizenAsync(request, ct)` | `OperationResult` | Federation-level citizen registration. |
| `GetCitizenAsync(did, ct)` | `CitizenRecord?` | Look up a citizen by any of their DIDs. |
| `IsCitizenActiveAsync(did, ct)` | `bool` | Active check — does not resolve primary DID. |
| `ResolveCitizenPrimaryDidAsync(anyDid, ct)` | `string?` | Finds the primary DID given any secondary DID. |
| `RegisterSocietyAsync(request, ct)` | `OperationResult` | Registers a new Society at the Federation level. |
| `GetSocietyAsync(did, ct)` | `SocietyRecord?` | Fetch Society metadata. |
| `GetAllSocietiesAsync(ct)` | `IReadOnlyList<SocietyRecord>` | All registered Societies. |
| `IsSocietyActiveAsync(did, ct)` | `bool` | Active status check for a Society. |
| `TransferAsync(request, ct)` | `OperationResult` | Executes a same-Society or Federation wallet transfer. |
| `BatchTransferAsync(requests, ct)` | `IReadOnlyList<OperationResult>` | Atomic batch transfer. |
| `GetBalanceGranaAsync(did, ct)` | `long` | Raw grana balance for any DID (citizen or Society). |
| `GetBalanceSvrn7Async(did, ct)` | `decimal` | Balance in SVRN7 units (grana ÷ 1,000,000). |
| `GetFederationAsync(ct)` | `FederationRecord?` | Federation record (supply, epoch, governance). |
| `InitialiseFederationAsync(federationDid, name, pubKeyHex, methodName, ct)` | `OperationResult` | One-time federation bootstrap. Idempotent. |
| `ResolveDidAsync(did, ct)` | `DidResolutionResult` | Resolves a DID Document from the registry. |
| `CreateDidAsync(document, ct)` | `Task` | Stores a new DID Document. |
| `StoreVcAsync(record, ct)` | `Task` | Stores a Verifiable Credential. |
| `GetVcByIdAsync(vcId, ct)` | `VcRecord?` | Retrieves a VC by its ID. |
| `RevokeVcAsync(vcId, reason, ct)` | `Task` | Permanently revokes a VC. |
| `AppendToLogAsync(entryType, payloadJson, ct)` | `string` | Appends to the Merkle audit log; returns the entry hash. |
| `GetMerkleRootAsync(ct)` | `string` | Current Merkle tree root hash. |
| `GenerateSecp256k1KeyPair()` | `Svrn7KeyPair` | Generates a new secp256k1 key pair (synchronous). |
| `GenerateEd25519KeyPair()` | `Svrn7KeyPair` | Generates a new Ed25519 key pair (synchronous). |
| `SignSecp256k1(payload, privateKeyBytes)` | `string` | Signs bytes; returns CESR-encoded signature. |
| `VerifySecp256k1(payload, cesrSig, pubKeyHex)` | `bool` | Verifies a CESR-encoded secp256k1 signature. |
| `Blake3HexAsync(data, ct)` | `string` | Blake3 hash of `data` as lowercase hex. |
| `ErasePersonAsync(did, controllerSignature, requestTimestamp, ct)` | `OperationResult` | GDPR erasure — pseudonymises all PII fields for the DID. |

### `ISvrn7SocietyDriver` (Society-level additions)

| Method | Return type | Description |
|---|---|---|
| `SocietyDid` | `string` | This Society's own DID (property, not a method). |
| `GetOwnSocietyAsync(ct)` | `SocietyRecord?` | Retrieves this Society's own record. |
| `RegisterCitizenInSocietyAsync(request, ct)` | `OperationResult` | Registers a citizen within this Society; triggers overdraft draw if wallet is low. |
| `AddCitizenDidAsync(primaryDid, methodName, ct)` | `OperationResult` | Issues a secondary DID for an existing citizen under a different method name. |
| `HandleIncomingTransferMessageAsync(packedMsg, ct)` | `string` | Handles a packed DIDComm transfer message end-to-end; returns a packed receipt. |
| `TransferToExternalCitizenAsync(request, targetSocietyDid, ct)` | `OperationResult` | Epoch-1 cross-Society transfer via TransferOrderCredential VC. |
| `TransferToFederationAsync(payerDid, amountGrana, nonce, sig, memo, ct)` | `OperationResult` | Transfer to the Federation wallet (permitted in Epoch 0 and 1). |
| `GetOverdraftStatusAsync(ct)` | `OverdraftStatus` | Current overdraft status (Clean, Overdrawn, Blocked). |
| `GetOverdraftRecordAsync(ct)` | `SocietyOverdraftRecord?` | Detailed overdraft record with lifetime draw totals. |
| `GetMemberCitizenDidsAsync(ct)` | `IReadOnlyList<string>` | All citizen DIDs registered with this Society. |
| `IsMemberAsync(citizenDid, ct)` | `bool` | Checks if a DID belongs to this Society. |
| `RegisterSocietyDidMethodAsync(methodName, ct)` | `OperationResult` | Registers an additional DID method name for this Society. |
| `DeregisterSocietyDidMethodAsync(methodName, ct)` | `OperationResult` | Deregisters a DID method name (enters dormancy period). Primary method cannot be deregistered. |
| `GetSocietyDidMethodsAsync(ct)` | `IReadOnlyList<SocietyDidMethodRecord>` | All Active and Dormant DID method names for this Society. |
| `FindVcsBySubjectAcrossSocietiesAsync(subjectDid, timeout, ct)` | `CrossSocietyVcQueryResult` | Cross-Society VC resolution via DIDComm fan-out. |

---

## Appendix F — PowerShell Stream → .NET Log Mapping

The Switchboard forwards all PS output streams to the .NET `ILogger` after `ps.Invoke()` returns.
Output appears in order but is not streamed line-by-line during execution.

| PowerShell stream | Cmdlet | .NET log level | Log prefix |
|---|---|---|---|
| Verbose | `Write-Verbose` | `Trace` | `[PS Verbose]` |
| Debug | `Write-Debug` | `Debug` | `[PS Debug]` |
| Information | `Write-Information` / `Write-Host` | `Information` | `[PS Info]` |
| Warning | `Write-Warning` | `Warning` | `[PS Warning]` |
| Error | `Write-Error` / `throw` | `Error` | `[PS Error]` |

`throw` also sets `ps.HadErrors = true`, which causes the Switchboard to call
`MarkFailedAsync` and retry or dead-letter depending on whether the protocol is transactional.

### Log level configuration

Set in `Program.cs` `ConfigureLogging`:

```csharp
logging.SetMinimumLevel(LogLevel.Trace);       // verbose — shows all PS streams
logging.SetMinimumLevel(LogLevel.Information); // normal  — hides Verbose and Debug
```

### What appears at each level

| Level | What you see |
|---|---|
| `Information` | Switchboard routing line, `[PS Info]` messages, error/failure events |
| `Debug` | Inbox enqueue/dequeue, `[PS Debug]` messages |
| `Trace` | Full dispatch spans, `ps.Invoke` start/complete, `[PS Verbose]` messages, all OpenTelemetry activity events |

---

## Appendix G — Error Handling Patterns

### `$ErrorActionPreference` and `Set-StrictMode`

Every LOBE `.psm1` must begin with:

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
```

`Set-StrictMode -Version Latest` converts absent-property reads and undefined variable
accesses into terminating errors.  `$ErrorActionPreference = 'Stop'` ensures that
non-terminating errors from cmdlets (e.g. `Write-Error`) also terminate the pipeline.

### Transactional vs non-transactional

| Type | Behaviour on failure | Configuration |
|---|---|---|
| **Transactional** | Dead-lettered immediately — no retry | Add URI to `Svrn7Constants.TransactionalProtocols` in `Svrn7.Core` |
| **Non-transactional** | Retried up to `NonTransactionalMaxAttempts` (default: 3) | Default for all protocols not in `TransactionalProtocols` |

Prefer transactional for any protocol that mutates state (transfers, registrations).
Non-transactional is appropriate for queries and notifications where a duplicate delivery is harmless.

### Throwing from a handler

```powershell
# Clean terminating error — Switchboard catches, logs, and retries/dead-letters
throw "Invoke-PandoMyLobeRequest: required field 'payerDid' was empty."

# Typed exception — use when the catch site needs to distinguish the error
throw [System.InvalidOperationException]::new("Invoke-PandoMyLobeRequest: ...")
```

Do **not** swallow exceptions silently — the Switchboard only calls `MarkProcessedAsync`
when the handler returns without error.  A swallowed exception leaves the inbox message
in `Processing` state; `ResetStuckMessagesAsync` (called on TDA startup) will re-queue it.

### Sending an error reply

For protocols that send receipts, send a failure receipt rather than throwing:

```powershell
if (-not $result.Success) {
    return @{
        PeerEndpoint  = $replyEndpoint
        PackedMessage = (@{ success = $false; error = $result.ErrorMessage } | ConvertTo-Json -Compress)
        MessageType   = 'did:drn:svrn7.net/protocols/domain/1.0/receipt'
    }
}
```

This marks the inbound message Processed (not Failed) and sends a structured error receipt
to the caller — preferred over throwing for user-visible protocol errors.

---

## Appendix H — Registered Protocol URI Registry

All URIs currently registered across SVRN7 LOBEs.  Choose a URI from this list to
understand existing patterns; confirm that any new URI is unique before registering it.

| LOBE | Protocol URI | Direction | Entrypoint |
|---|---|---|---|
| `Svrn7.Federation` | `did:drn:svrn7.net/protocols/Svrn7.Federation/0.8/initialize-federation` | inbound | `Invoke-PandoFederationInit` |
| `Svrn7.Federation` | `did:drn:svrn7.net/protocols/Svrn7.Federation/0.8/federation-query` | inbound | `Invoke-PandoFederationQuery` |
| `Svrn7.Federation` | `did:drn:svrn7.net/protocols/Svrn7.Federation/0.8/register-society` | inbound | `Invoke-PandoRegisterSociety` |
| `Svrn7.Society` | `did:drn:svrn7.net/protocols/Svrn7.Society/0.8/member-query` | inbound | `Invoke-PandoMemberQuery` |
| `Svrn7.Society` | `did:drn:svrn7.net/protocols/Svrn7.Society/0.8/society-query` | inbound | `Invoke-PandoSocietyQuery` |
| `Svrn7.Society` | `did:drn:svrn7.net/protocols/Svrn7.Society/0.8/overdraft-query` | inbound | `Invoke-PandoOverdraftQuery` |
| `Svrn7.Society` | `did:drn:svrn7.net/protocols/Svrn7.Society/0.8/did-method-register` | inbound | `Invoke-PandoDidMethodRegister` |
| `Svrn7.Society` | `did:drn:svrn7.net/protocols/Svrn7.Society/0.8/did-methods-query` | inbound | `Invoke-PandoDidMethodsQuery` |
| `Svrn7.Society` | `did:drn:svrn7.net/protocols/Svrn7.Society/0.8/citizen-did-add` | inbound | `Invoke-PandoCitizenDidAdd` |
| `Svrn7.Society` | `did:drn:svrn7.net/protocols/Svrn7.Society/0.8/transfer-request` | inbound | `Invoke-Svrn7IncomingTransfer` |
| `Svrn7.Society` | `did:drn:svrn7.net/protocols/Svrn7.Society/0.8/transfer-order` | inbound | `Invoke-Svrn7IncomingTransfer` |
| `Svrn7.Society` | `did:drn:svrn7.net/protocols/Svrn7.Society/0.8/transfer-order-receipt` | inbound | `Confirm-Svrn7Settlement` |
| `Svrn7.Onboarding` | `did:drn:svrn7.net/protocols/Svrn7.Onboarding/0.8/register-citizen` | inbound | `ConvertFrom-PandoOnboardRequest` |
| `Svrn7.Onboarding` | `did:drn:svrn7.net/protocols/Svrn7.Onboarding/0.8/receipt` | outbound | — |
| `Svrn7.Invoicing` | `did:drn:svrn7.net/protocols/Svrn7.Invoicing/0.8/request` | inbound | `ConvertFrom-PandoInvoiceRequest` |
| `Svrn7.Invoicing` | `did:drn:svrn7.net/protocols/Svrn7.Invoicing/0.8/receipt` | outbound | — |
| `Svrn7.Identity` | `did:drn:svrn7.net/protocols/Svrn7.Identity/0.8/did-resolve-request` | inbound | *(see lobe.json)* |
| `Svrn7.Calendar` | `did:drn:svrn7.net/protocols/Svrn7.Calendar/0.8/event` | inbound | *(see lobe.json)* |
| `Svrn7.Calendar` | `did:drn:svrn7.net/protocols/Svrn7.Calendar/0.8/invite` | inbound | *(see lobe.json)* |
| `Svrn7.Calendar` | `did:drn:svrn7.net/protocols/Svrn7.Calendar/0.8/response` | inbound | *(see lobe.json)* |
| `Svrn7.Email` | `did:drn:svrn7.net/protocols/Svrn7.Email/0.8/message` | inbound | *(see lobe.json)* |
| `Svrn7.Email` | `did:drn:svrn7.net/protocols/Svrn7.Email/0.8/receipt` | outbound | — |
| `Svrn7.Notifications` | `did:drn:svrn7.net/protocols/Svrn7.Notifications/0.8/alert` | inbound | *(see lobe.json)* |
| `Svrn7.Presence` | `did:drn:svrn7.net/protocols/Svrn7.Presence/0.8/status` | inbound | *(see lobe.json)* |
| `Svrn7.Presence` | `did:drn:svrn7.net/protocols/Svrn7.Presence/0.8/subscribe` | inbound | *(see lobe.json)* |
| `Svrn7.Presence` | `did:drn:svrn7.net/protocols/Svrn7.Presence/0.8/unsubscribe` | inbound | *(see lobe.json)* |
| `Svrn7.UX` | `did:drn:svrn7.net/protocols/Svrn7.UX/0.8/notification` | inbound | *(see lobe.json)* |
| `Svrn7.UX` | `did:drn:svrn7.net/protocols/Svrn7.UX/0.8/balance-update` | inbound | *(see lobe.json)* |
| `Svrn7.UX` | `did:drn:svrn7.net/protocols/Svrn7.UX/0.8/registration-complete` | inbound | *(see lobe.json)* |

---

## Appendix I — Testing a New LOBE

### Unit tests — Pester (no TDA required)

Pester 5 tests can verify that module functions exist, that `Assert-BodyFields` throws on
missing required fields, and that output hashtables have the correct keys — all without a
running TDA or compiled assemblies.

```powershell
# Install once
Install-Module Pester -MinimumVersion 5.0 -Force -SkipPublisherCheck -Scope CurrentUser

# Run from repo root
Import-Module Pester -MinimumVersion 5.0 -Force
Invoke-Pester .\tests\Svrn7.Lobes.Tests.ps1 -Output Detailed
```

### Integration tests — DIDComm messages to a running TDA

The fastest way to exercise a new LOBE end-to-end:

1. Build and start the TDA: `dotnet .\Svrn7.TDA.dll`
2. Import the send helper from a separate PowerShell 7 session:
   ```powershell
   Import-Module .\lobes\Svrn7.Federation\Svrn7.Federation.psm1
   ```
3. Send a plaintext DIDComm message:
   ```powershell
   $body = @{ exampleField = "value" } | ConvertTo-Json -Compress
   $msg  = @{
       typ  = "application/didcomm-plain+json"
       id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
       type = "did:drn:svrn7.net/protocols/mylobe/1.0/request"
       from = "did:drn:foundation.svrn7.net"
       to   = @("did:drn:bindloss.svrn7.net")
       body = $body
   } | ConvertTo-Json
   Send-DIDCommMessage -Body $msg
   ```
4. Expected response: `Status: Accepted`
5. Watch the TDA log for the routing line and any `[PS *]` output from your handler.

### What the log shows

At `LogLevel.Information`:
```
Switchboard: routing did:drn:.../inbox/msg/<id>
    (type=did:drn:svrn7.net/protocols/mylobe/1.0/request) → Invoke-PandoMyLobeRequest [Svrn7.MyLobe]
```

At `LogLevel.Trace`, all PS streams are forwarded (see Appendix F).

### Verifying state changes

After a successful handler run, verify driver-side effects using the PowerShell module directly
(with the TDA stopped — LiteDB exclusive lock):

```powershell
Import-Module .\lobes\Svrn7.Society\Svrn7.Society.psm1
Connect-Svrn7Society -SocietyDid "did:drn:bindloss.svrn7.net" -FederationDid "did:drn:foundation.svrn7.net" -DidMethodNames @("bindloss") -DbPath "."
# ... then call Get-* cmdlets to inspect state
```

### Full test lifecycle

See `docs/DEBUG.md` for the complete Scenario E bootstrap sequence (federation init →
society register → citizen onboard) and Scenario F (database teardown).
