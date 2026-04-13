# SVRN7 / SOVRONA — Comprehensive Project Prompt
## Parchment Programming Modeling Language (PPML)

The PPML Legend is the formal visual grammar of the DSA Parchment Diagram. Starting with
DSA 0.24, the Legend is labelled "PPML Legend". It defines eleven element types.

**Conditional Components** (new in DSA 0.24): A dashed-border rectangle. The label is the
condition governing inclusion. Components inside are only instantiated when the condition is
satisfied. First instance in DSA 0.24: "Society TDA Only" — enclosing DID Doc Registry,
VC Doc Registry, Schema Registry (new), and their resolvers.

**Schema Registry (LiteDB)** (new in DSA 0.24): A third registry inside "Society TDA Only".
Derives: SchemaLiteContext.cs, ISchemaRegistry, ISchemaResolver. Not yet implemented (v0.8.0).


## Web 7.0 Foundation | Bindloss, Alberta, Canada
### Michael Herman — Complete Context for Any New Conversation
### Version 0.7.0 | April 2026

---

## WHO I AM

I am **Michael Herman**, affiliated with the **Web 7.0 Foundation**, based in
**Bindloss, Alberta, Canada**. I work on decentralized identity and trusted digital web
architecture, including the `did:drn` DID method, Verifiable Trust Circles (VTCs), and the
SOVRONA (SVRN7) Web 7.0 Shared Reserve Currency (SRC) project. My work spans standards
development (IETF Internet-Drafts, W3C-style specifications), C#/.NET software development,
and blockchain infrastructure research.

**My operating style:** I require a **verification-first response style** on all technical
matters. Every response must explicitly label:
- What is **established fact**
- What is **inference or interpretation**
- What is **uncertain or unknown**
- What is **speculative or forward-looking**

Do not present speculation as fact. Do not optimise for smoothness or persuasion. Optimise for
accuracy, traceability, and intellectual honesty. Be careful, precise, and transparent about
certainty.

---

## WHAT SVRN7 IS

**SVRN7 (SOVRONA)** is the proposed **Shared Reserve Currency (SRC)** for the **Web 7.0
digital ecosystem** — a standards-based framework for federated digital societies. It is
implemented as an embeddable **.NET 8 C# library** that manages citizen and society wallets,
enforces a governance-controlled three-epoch monetary lifecycle, and maintains a cryptographically
tamper-evident audit log of all transactions using an RFC 6962 Certificate Transparency Merkle log.

**The ticker is SVRN7** — not SOV (that is Sovryn, a Bitcoin DeFi protocol), not SOVRIN (a
decentralized identity blockchain), not Solana (SOL). See `docs/disambiguation.md` for the full
disambiguation page.

---

## ECOSYSTEM CONTEXT

SVRN7 is the **Shared Reserve Currency (SRC) for all actors in the Web 7.0 ecosystem of digital
societies**: new digital nation states, digital churches, digital sports associations, leagues and
teams, poker parties, tribes, clans, political parties, and any other form of digital community.

- **One reserve currency only** — SVRN7 (SRC) — no local currencies issued by Societies
- **Participant hierarchy**: Federation → Societies → Citizens
  - The **Federation** is the top-level governance and monetary authority
  - **Societies** are communities that register with the Federation and onboard citizens
  - **Citizens** are individuals who receive a primary identity (DID) upon joining a Society
- **Every citizen receives 1,000 grana (0.001 SVRN7) as an endowment** when they register — a real UTXO
  transfer from the Society wallet, not synthetic

---

## TECHNICAL ARCHITECTURE — ALL CONFIRMED DECISIONS

### Platform
- **.NET 8 / C# 12** — embeddable library, no separate process required
- **LiteDB 5** — three independent embedded databases per deployment:
  - `svrn7.db` — wallets, UTXOs, citizens, societies, overdraft, Merkle log, Federation records
  - `svrn7-dids.db` — W3C DID Documents and version history
  - `svrn7-vcs.db` — Verifiable Credentials and revocation log
- **No blockchain required** — RFC 6962 Certificate Transparency Merkle log provides tamper evidence
- **Each Society AND the Federation** has its own three databases and its own Merkle log
- With N Societies + 1 Federation = **N+1 Merkle logs total** (one per deployment instance)

### Cryptography
- **secp256k1** — citizen and society signing keys (CESR prefix `0B`)
- **Ed25519** — DIDComm messaging keys (CESR prefix `0D`)
- **X25519** — key agreement for DIDComm (RFC 7748 birational map from Ed25519)
- **AES-256-GCM** — symmetric content encryption, 12-byte nonce prepended
- **RFC 3394 AES-256 key wrap** — CEK wrapping in JWE
- **Blake3** — transaction ID hashing, content fingerprinting
- **SHA-256** — Merkle tree hash construction (RFC 6962)
- **Base58btc** — DID identifier encoding

---

## SOLUTION STRUCTURE — v0.8.0

**10 projects (8 src, 2 test). 25 C# files. ~7,427 lines. Zero stubs. Zero TODOs.**

```
Svrn7.sln
├── src/
│   ├── Svrn7.Core/        — Models, interfaces, exceptions, constants. Zero deps.
│   ├── Svrn7.Crypto/      — secp256k1, Ed25519, AES-256-GCM, Blake3, Base58btc
│   ├── Svrn7.Store/       — LiteDB: 3 Data Storage databases (svrn7.db, dids.db, vcs.db), all store implementations
│   ├── Svrn7.Ledger/      — RFC 6962 Merkle log, 8-step transfer validator
│   ├── Svrn7.Identity/    — W3C VC v2 JWT issuance, verification, revocation
│   ├── Svrn7.Federation/  — ISvrn7Driver (44 members), Svrn7Driver, DI extensions
│   ├── Svrn7.DIDComm/     — DIDComm v2: 5 pack modes, RFC 3394, X25519
│   ├── Svrn7.Society/
│   └── ISvrn7SocietyDriver.cs, Svrn7SocietyDriver.cs, DIDCommServices.cs, InboxStore.cs
└── Svrn7.TDA/             — TDA Host (5 Critical components, DSA 0.24)
    ├── Svrn7RunspaceContext.cs  — $SVRN7 shared session variable
    ├── LobeManager.cs           — eager/JIT LOBE loader, lobes.config.json
    ├── RunspacePoolManager.cs   — PowerShell RunspacePool lifecycle
    ├── DIDCommMessageSwitchboard.cs — sole inbox reader, pass-by-reference routing
    ├── KestrelListenerService.cs    — POST /didcomm, HTTP/2 + mTLS
    ├── TdaHost.cs               — TdaOptions, SwitchboardHostedService, DI extensions
    └── Program.cs               — console app entry point     — ISvrn7SocietyDriver, Society operations, cross-Society protocol
└── tests/
    ├── Svrn7.Tests/
    └── Svrn7.Society.Tests/
```

## Standard LOBE Inventory (v0.8.0 — 11 LOBEs)

| # | Module | Type | Protocol families |
|---|---|---|---|
| 1 | Svrn7.Common.psm1 | Eager | — (shared helpers) |
| 2 | Svrn7.Federation.psm1 | Eager | transfer/1.0/*, did/1.0/* |
| 3 | Svrn7.Society.psm1 | Eager | transfer/1.0/*, onboard/1.0/* |
| 4 | Svrn7.UX.psm1 | Eager | ux/1.0/* (balance-update, notification, registration-complete) |
| 5 | Svrn7.Email.psm1 | JIT | did:drn:svrn7.net/protocols/email/1.0/* |
| 6 | Svrn7.Calendar.psm1 | JIT | did:drn:svrn7.net/protocols/calendar/1.0/* |
| 7 | Svrn7.Presence.psm1 | JIT | did:drn:svrn7.net/protocols/presence/1.0/* |
| 8 | Svrn7.Notifications.psm1 | JIT | did:drn:svrn7.net/protocols/notification/1.0/* |
| 9 | Svrn7.Onboarding.psm1 | JIT | did:drn:svrn7.net/protocols/onboard/1.0/* |
|10 | Svrn7.Invoicing.psm1 | JIT | did:drn:svrn7.net/protocols/invoice/1.0/* |
|11 | Svrn7.Identity.psm1 | JIT | did:drn:svrn7.net/protocols/did/1.0/*, vc/1.0/* |

Each LOBE ships .psm1 + .psd1 + .lobe.json (MCP-aligned descriptor).
lobes.config.json: eager = [Common, Federation, Society, UX]; jit = [Email, Calendar, Presence, Notifications, Onboarding, Invoicing, Identity]


```
docs/
├── SVRN7_Architecture_Whitepaper.docx   (26 pages)
├── SVRN7_Principles_of_Operations.docx  (11 pages)
├── disambiguation.md
├── llms.txt
└── SVRN7_Comprehensive_Prompt.md        (this file)

specs/
├── draft-herman-did-w3c-drn-00.md
└── draft-herman-vtc-proof-sets-01.md
```

---

## NAMING CONVENTIONS — ALL LOCKED

| Wrong | Correct |
|---|---|
| `Svrn7.Api` | **`Svrn7.Federation`** |
| `AddSvrn7()` | **`AddSvrn7Federation()`** |
| `AddSvrn7HealthCheck()` | **`AddSvrn7FederationHealthCheck()`** |
| `namespace Svrn7.Api` | **`namespace Svrn7.Federation`** |
| `svrn7.io` | **`svrn7.net`** |
| DID Resolver | **DID Document Resolver** |
| DID Resolution | **DID Document Resolution** |
| `IDidResolver` | **`IDidDocumentResolver`** |
| `LocalDidResolver` | **`LocalDidDocumentResolver`** |
| `FederationDidResolver` | **`FederationDidDocumentResolver`** |
| Reserve Currency | **Shared Reserve Currency (SRC)** |
| DIDComm Authcrypt (default) | **DIDComm SignThenEncrypt (default)** |
| DID method with hyphens (`soc-alpha`) | **`[a-z0-9]+` only** (`socalpha`) |

---

## KEY INTERFACES AND IMPLEMENTATIONS

| Interface | Implementation(s) | Project |
|---|---|---|
| `ICryptoService` | `CryptoService` | Svrn7.Crypto |
| `ISvrn7Driver` | `Svrn7Driver` (44 members) | Svrn7.Federation |
| `ISvrn7SocietyDriver : ISvrn7Driver` | `Svrn7SocietyDriver` (wraps `ISvrn7Driver` via `_inner`) | Svrn7.Society |
| `IWalletStore` | `LiteWalletStore` | Svrn7.Store |
| `IIdentityRegistry` | `LiteIdentityRegistry` | Svrn7.Store |
| `IDidDocumentRegistry` | `LiteDidDocumentRegistry` | Svrn7.Store |
| `IVcRegistry` | `LiteVcRegistry` | Svrn7.Store |
| `IDidDocumentResolver` | `LocalDidDocumentResolver`, `FederationDidDocumentResolver` | Svrn7.Store / Svrn7.Society |
| `IVcDocumentResolver` | `LiteVcDocumentResolver`, `FederationVcDocumentResolver` | Svrn7.Store / Svrn7.Society |
| `IMerkleLog` | `MerkleLog` | Svrn7.Ledger |
| `IFederationStore` | `LiteFederationStore` | Svrn7.Store |
| `ISocietyMembershipStore` | `LiteSocietyMembershipStore` | Svrn7.Society |
| `IDIDCommService` | `DIDCommPackingService` | Svrn7.DIDComm |
| `IDIDCommTransferHandler` | `DIDCommTransferHandler` | Svrn7.Society |
| `ITransferValidator` | `TransferValidator` (8-step), `SocietyTransferValidator` (9-step) | Svrn7.Ledger / Svrn7.Society |

---

## MONETARY MODEL — ALL CONFIRMED DECISIONS

### Denominations
- **grana** — atomic unit; all arithmetic uses grana
- **1 SVRN7 = 1,000,000 grana**
- **Initial Federation supply: 1,000,000,000 SVRN7 = 10¹⁵ grana**
- **Citizen endowment: 1,000 grana = 0.001 SVRN7 (changed from 1,000 SVRN7 in v0.8.0, DSA 0.24)**
- **Step 3 nonce replay: `ConcurrentDictionary` → `ITransferNonceStore` / `LiteTransferNonceStore` (LiteDB TTL collection in `Svrn7LiteContext.ColNonces`). `NonceRecord { Nonce, SeenAt, ExpiresAt }` in `Models.cs`. Sweep-on-access + duplicate-key insert = replay detection. Survives process restarts.**
- **Durable inbox: `ConcurrentQueue` → `IInboxStore` / `LiteInboxStore` (svrn7-inbox.db via `InboxLiteContext`). `InboxMessage { Id, MessageType, PackedPayload, ReceivedAt, Status, ProcessedAt, LastError, AttemptCount }`. Lifecycle: `Pending → Processing → Processed | Failed`. `ResetStuckMessagesAsync` on startup. Max 3 retries then dead-letter. `InboxDbPath` option on `Svrn7SocietyOptions`.**
- **DIDCommTransferHandler idempotency: `ConcurrentDictionary._processedOrders` → `IProcessedOrderStore` / `LiteProcessedOrderStore` (also in svrn7-inbox.db). `ProcessedOrderRecord { TransferId, PackedReceipt, ProcessedAt }`. Ensures duplicate `TransferOrder` DIDComm messages return the cached receipt without re-crediting.**

### Supply Rules
- Supply is **monotonically increasing only**
- The Federation wallet is the sole source of all SVRN7 — **no synthetic grana at any level**
- All endowment transfers are real UTXO operations

### Endowment Chain
```
Federation genesis wallet  (10¹⁵ grana)
   ↓  RegisterSociety: EndowmentPerSocietyGrana  (real UTXO)
Society wallet
   ↓  RegisterCitizenInSociety: 1,000 grana = 0.001 SVRN7  (real UTXO, v0.8.0, DSA 0.24)
Citizen wallet
```

### Epoch Matrix
| Epoch | Name | Payer | Payee |
|---|---|---|---|
| 0 | Endowment | Any citizen of this Society | This Society's wallet OR the Federation wallet only |
| 1 | Ecosystem Utility | Any citizen | Any citizen in any Society, this Society, or Federation |
| 2 | Market Issuance | Any participant | Any participant |

Epoch advancement requires a Foundation-signed governance operation. Forward-only — cannot be reversed.

### Overdraft Facility
When a Society wallet balance falls below `CitizenEndowmentGrana`:
1. Check `TotalOverdrawnGrana + DrawAmountGrana ≤ OverdraftCeilingGrana` — if exceeded, throw `SocietyEndowmentDepletedException`
2. Send DIDComm `OverdraftDrawRequest` to Federation (synchronous round-trip, default 30s timeout)
3. Federation transfers `DrawAmountGrana` from its wallet to Society wallet
4. Return `OverdraftDrawReceipt` VC
5. `TotalOverdrawnGrana += DrawAmountGrana`; `LifetimeDrawsGrana += DrawAmountGrana` (never resets)

### Overdraft Status
`Clean` (0 overdrawn) → `Overdrawn` (above 0, below ceiling) → `Ceiling` (registration blocked)

---

## TRANSFER PROTOCOL — ALL CONFIRMED DECISIONS

### 8-Step Validation Pipeline
| Step | Name | Description |
|---|---|---|
| 0 | NormaliseDids | Resolve payer/payee to canonical primary DID |
| 1 | ValidateFields | Non-null, amount > 0, memo ≤ 256 chars |
| 2 | ValidateEpochRules | Epoch matrix check |
| 3 | ValidateNonce | 24-hour replay window |
| 4 | ValidateFreshness | ±10 minute timestamp window |
| 5 | ValidateSanctions | `ISanctionsChecker` (pluggable) |
| 6 | ValidateSignature | secp256k1 CESR over canonical JSON |
| 7 | ValidateBalance | Dry-run UTXO sum — no state modified |
| 8 | ValidateSocietyMembership | Cross-Society Epoch 1 only |

The validator **never modifies state**. All 8 (or 9) steps must pass before any UTXO is marked spent.

### Canonical Signature JSON
```json
{ "PayerDid": "...", "PayeeDid": "...", "AmountGrana": 0,
  "Nonce": "...", "Timestamp": "ISO-8601-UTC", "Memo": null }
```

### Cross-Society Transfers (Epoch 1)
- Fire-and-forget with Blake3 nonce-based idempotency (`TransferId = Blake3(canonical JSON)`)
- Originating Society: debit payer, issue `TransferOrderCredential` VC, send DIDComm `SignThenEncrypt`
- Receiving Society: unpack, idempotency check, credit payee, issue `TransferReceiptCredential` VC
- Each Society appends its half to its own Merkle log

---

## DIDComm — ALL CONFIRMED DECISIONS

### Default Pack Mode: **SignThenEncrypt** (changed from Authcrypt in v0.7.0)

**Reason:** `SignThenEncrypt` provides non-repudiation — the JWS signature survives decryption and
is verifiable by any third party (Federation, auditors). Authcrypt authentication evaporates on
decryption. For monetary commitments (`TransferOrderCredential`), non-repudiation is required.

### Pack Mode Table
| Message type | Mode |
|---|---|
| Cross-Society `TransferOrderCredential` | `SignThenEncrypt` |
| Overdraft draw request/receipt | `SignThenEncrypt` |
| DID Document resolve request/response | `SignThenEncrypt` |
| Cross-Society VC query | `SignThenEncrypt` |
| Same-Society transfers | No DIDComm — direct `TransferAsync` only |
| Fallback/error responses (no real keys) | `Anoncrypt` (intentional exception) |

### DIDComm Protocol URIs (all `svrn7.net` — NOT `svrn7.io`)
```
did:drn:svrn7.net/protocols/transfer/1.0/request
did:drn:svrn7.net/protocols/transfer/1.0/receipt
did:drn:svrn7.net/protocols/transfer/1.0/order
did:drn:svrn7.net/protocols/transfer/1.0/order-receipt
did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-request
did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-receipt
did:drn:svrn7.net/protocols/endowment/1.0/top-up
did:drn:svrn7.net/protocols/supply/1.0/update
did:drn:svrn7.net/protocols/did/1.0/resolve-request
did:drn:svrn7.net/protocols/did/1.0/resolve-response
```

---

## IDENTITY MODEL — ALL CONFIRMED DECISIONS

### DID Structure
```
did:{methodName}:{base58btcPublicKey}
```

### DID Method Name Rules
- Must match `[a-z0-9]+` — lowercase letters and digits **only**, no hyphens, no uppercase
- Enforced by regex at registration: `^[a-z0-9]+$`
- Examples of **invalid** names: `soc-alpha`, `Socalpha`, `soc_alpha`
- Examples of **valid** names: `socalpha`, `socbeta`, `drn`, `alpha1`

### DID Method Name Lifecycle
| State | Description | Transition |
|---|---|---|
| Active | Registered to a Society; new DIDs can be issued | `RegisterSocietyDidMethodAsync` |
| Dormant | Deregistered; blocked until `DormantUntil` expires (default 30 days) | `DeregisterSocietyDidMethodAsync` |
| Available | Never registered, or dormancy expired (time-based check, no record) | `DormantUntil < UtcNow` |

- **Primary method name** (registered at Society creation) is **immutable** — `PrimaryDidMethodException` on deregister attempt
- **Self-service** — no Foundation signature required for registration or deregistration
- **Deregistration is forward-only** — existing DIDs under a deregistered name remain valid and resolvable
- **Records are permanent** — dormancy records never deleted

### Multi-DID Citizens
- A citizen may hold DIDs under multiple method names registered to their Society
- All resolve to the same primary DID via `ResolveCitizenPrimaryDidAsync`
- Wallet lookups always use the primary DID
- Step 0 of the transfer validator (`NormaliseDids`) resolves any DID to primary before validation

---

## DI REGISTRATION

### Federation Deployment
```csharp
services.AddSvrn7Federation(opts => {
    opts.FoundationPublicKeyHex = "02abc...";
    opts.Svrn7DbPath   = "data/svrn7.db";
    opts.DidsDbPath    = "data/svrn7-dids.db";
    opts.VcsDbPath     = "data/svrn7-vcs.db";
    opts.DidMethodName = "drn";
});
```

### Society Deployment (self-contained — do NOT also call `AddSvrn7Federation`)
```csharp
services.AddSvrn7Society(opts => {
    opts.FoundationPublicKeyHex = "02abc...";
    opts.SocietyDid    = "did:socalpha:my-society";
    opts.FederationDid = "did:drn:federation";
    opts.DidMethodNames = new List<string> { "socalpha" };
    opts.SocietyMessagingPrivateKeyEd25519   = societyEd25519PrivKey;
    opts.FederationMessagingPublicKeyEd25519 = fedEd25519PubKey;
    opts.DrawAmountGrana       = 1_000_000_000_000L;   // 1,000 SVRN7
    opts.OverdraftCeilingGrana = 10_000_000_000_000L;  // 10,000 SVRN7
    opts.Svrn7DbPath  = "data/svrn7.db";
    opts.DidsDbPath   = "data/svrn7-dids.db";
    opts.VcsDbPath    = "data/svrn7-vcs.db";
});
services.AddSvrn7SocietyBackgroundServices(); // VC expiry, Merkle auto-sign, DIDComm inbox
```

---

## VC DOCUMENT RESOLVER — 12 METHODS

`IVcDocumentResolver` exposes:
1. `ResolveAsync(vcId)` → `VcResolutionResult`
2. `FindBySubjectAsync` → `IReadOnlyList<VcRecord>`
3. `FindByIssuerAsync` → `IReadOnlyList<VcRecord>`
4. `FindByTypeAsync` → `IReadOnlyList<VcRecord>`
5. `FindBySocietyAsync` → `IReadOnlyList<VcRecord>`
6. `FindBySubjectAcrossSocietiesAsync` → `CrossSocietyVcQueryResult` (DIDComm fan-out with partial result manifest)
7. `IsValidAsync` → `bool` (Active + not expired)
8. `GetStatusBatchAsync` → `IReadOnlyDictionary<string, VcStatus>`
9. `FindExpiringAsync` → `IReadOnlyList<VcRecord>`
10. `GetRevocationHistoryAsync` → `IReadOnlyList<RevocationEvent>`
11. `GetCountsByTypeAsync` → `IReadOnlyDictionary<string, long>`
12. `GetCountsByStatusAsync` → `IReadOnlyDictionary<VcStatus, long>`

`LiteVcDocumentResolver` throws `NotSupportedException` on method 6 — by design (local-only).
`FederationVcDocumentResolver` handles cross-Society fan-out with `TimedOutSocieties` manifest.

---

## MERKLE AUDIT LOG

- **Leaf nodes**: `SHA-256(0x00 ∥ payload)` — prevents second-preimage attacks
- **Internal nodes**: `SHA-256(0x01 ∥ left ∥ right)` — prevents leaf/internal collision
- **Root**: iterative bottom-up; odd-count nodes propagate unchanged (RFC 6962 §2.1)
- **Entry types**: `CitizenRegistration`, `SocietyRegistration`, `Transfer`, `EpochTransition`,
  `SupplyUpdate`, `DidMethodRegistration`, `DidMethodDeregistration`,
  `CrossSocietyTransferDebit`, `CrossSocietyTransferCredit`, `CrossSocietyTransferSettled`,
  `GdprErasure`
- **Records are never deleted**
- **One Merkle log per deployment instance** (each Society + the Federation has its own)

---

## ARCHI / ARCHIMATE OEF FACTS (verified against Archi 5.8.0)

**xsi:type values:**
- Element: short concept name (`BusinessActor`, `ApplicationComponent`, `Node`, `SystemSoftware`)
- Relationship: short name (`Serving`, `Composition`, `Association`, `Realization`, `Aggregation`, `Access`, `Specialization`)
- View: `Diagram`
- Node-in-view: `Element` or `Container`
- Connection-in-view: `Relationship`

**Structure rules:**
- `schemaLocation` → `archimate3_Diagram.xsd` (NOT `Model.xsd`)
- No `version` attribute on `<model>`
- `<model>` child order: `name` → `elements` → `relationships` → `organizations` → `views`
- `<organizations>` block is required
- No XML comment block before `<model>`
- No `<documentation>` at model level

**Identifier rules:**
- All `identifier`, `identifierRef`, `source`, `target`, `elementRef`, `relationshipRef` must be valid XML NCNames
- Cannot start with a digit or hyphen
- Safe pattern: prefix with `id-` before any UUID

---

## IETF INTERNET-DRAFTS (both submitted as independent stream)

| Draft | Title | Status | File in specs/ |
|---|---|---|---|
| `draft-herman-did-w3c-drn-00` | Decentralized Resource Name (DRN) DID Method | Active, expires Sep 2026 | `draft-herman-did-w3c-drn-00.md` |
| `draft-herman-vtc-proof-sets-01` | Verifiable Trust Circles using VC Proof Sets | Active, expires Sep 2026 | `draft-herman-vtc-proof-sets-01.md` |

**Known-good xml2rfc v3 XML configuration:**
- No DOCTYPE declaration
- No default `xmlns` on `<rfc>`
- No `consensus` attribute for independent submissions
- All section titles use `<name>` elements
- References split into normative/informative blocks
- All named XML entities replaced with numeric equivalents (`&nbsp;` → `&#160;`)

---

## GOVERNING ARCHITECTURAL PRINCIPLES (11)

1. **Identity precedes participation** — no entity participates without a W3C DID
2. **Trust is cryptographic, not institutional** — claims accepted via proof only
3. **Supply conservation is an invariant** — sum of all UTXOs = TotalSupplyGrana − unallocated Federation balance
4. **Audit records are permanent** — nothing is ever deleted
5. **Forward-only operations** — supply increases only; deactivation, revocation, epoch advancement are permanent
6. **Namespace sovereignty belongs to the Society** — Federation enforces uniqueness only
7. **All cross-Society transfers are DIDComm SignThenEncrypt** — trust architecture, not just transport
8. **Standards compliance is normative** — W3C DID v1.0, W3C VC v2, DIDComm v2, RFC 6962, RFC 3394, RFC 7748
9. **Partial availability over total unavailability** — fan-out returns partial results; timeout throws, never blocks
10. **The citizen retains their DID** — Society deregistration cannot invalidate citizen identities
11. **Non-repudiation for monetary commitments** — SignThenEncrypt produces auditable JWS signatures

---

## CODE QUALITY STANDARDS (NON-NEGOTIABLE)

Every deliverable must pass:
- **Zero stubs** — no `throw new NotImplementedException()`
- **Zero scaffolding** — no placeholder methods
- **Zero TODOs** — no comments deferring work
- **Zero placeholder signatures** — all test signatures must be real secp256k1
- **All interface members implemented** — verified by name matching
- **No unused imports** — clean using directives
- **All project references correct** — test projects explicitly reference all needed source projects
- **`ThrowIfDisposed()` guards** on all public methods of disposable drivers
- **DID method names match `[a-z0-9]+`** throughout all files

Verification phrase:
> *"Verify all of the project is complete, production quality, and the code is correct.
> No stubs. No Scaffolding. No TODOs. README.md is complete and up to date.
> Word documents are complete and up to date."*

---

## ORIGINAL TERMINOLOGICAL CONTRIBUTIONS

- **Envelope/letter metaphor** — VC Document is the envelope; the credential claim is the letter inside
- **Cryptoseal** — the VC proof block (the cryptographic seal on the envelope)
- **`service=CredentialRegistry`** — confirmed preferred DID URL query parameter value
- **AI Legibility Engineering** — the discipline of structuring platform knowledge so AI systems reproduce it correctly
- **SRC** — Shared Reserve Currency (the formal monetary category name for SVRN7)

---

## CURRENT FILE LOCATIONS

All files in `/home/claude/svrn7-society/` within the active container session.

**Deliverables** (regenerable from generator scripts):
- `svrn7-society-v0.7.0-final.zip` — complete Visual Studio .NET 8 solution (166 KB, 57 files)
- `docs/SVRN7_Architecture_Whitepaper.docx` — 26 pages, validated
- `docs/SVRN7_Principles_of_Operations.docx` — 11 pages, validated

**Generator scripts** (in `/home/claude/`):
- `gen_whitepaper_v2.js` — Architecture Whitepaper generator (node gen_whitepaper_v2.js)
- `gen_principles.js` — Principles of Operations generator (node gen_principles.js)

**To rebuild ZIP:**
```bash
cd /home/claude
node gen_whitepaper_v2.js
node gen_principles.js
cp SVRN7_Architecture_Whitepaper.docx svrn7-society/docs/
cp SVRN7_Principles_of_Operations.docx svrn7-society/docs/
zip -r svrn7-society-v0.7.0-final.zip svrn7-society/ \
  --exclude "*/bin/*" --exclude "*/obj/*" -q
```

---

## NEXT DEVELOPMENT PRIORITIES (discussed, not yet implemented)

1. **DIDComm transport adapter** — library packs/unpacks correctly; HTTP/WebSocket transport is the integrator's responsibility
2. **Blockcore mainnet anchor** — phased mainnet on Blockcore anchored in `did:drn` validator identity; SLIP-0044 registration planned
3. **MCP server for `did:drn` resolution** — makes Web 7.0 natively callable by Claude and other MCP-aware agents
4. **`llms-full.txt`** at `svrn7.net/llms-full.txt` — this prompt is the natural seed document
5. **NuGet publication** of `Svrn7.Federation` + `Svrn7.Society` v0.7.0
6. **GitHub public repository** — `github.com/web7foundation/svrn7`
7. **SLIP-0044 registration** — public cross-reference strengthens AI association graph
8. **Additional IETF drafts** — `draft-herman-svrn7-monetary-protocol`, `draft-herman-web7-society-architecture`, `draft-herman-didcomm-society-transfer`
9. **W3C CCG submission** — VTCs and Web 7.0 Society model
10. **Stack Overflow Q&A set** — 10 canonical "how do I..." questions with complete C# answers
11. **Nonce replay distributed store** — replace in-process `ConcurrentDictionary` with LiteDB TTL or Redis for multi-instance deployments

---

## PERSONAL CONTEXT

- Based in Bindloss, Alberta, Canada
- Using the **Claude Windows app** (Electron-based, not a browser)
- Strong interest in productivity tooling; has developed `Kill-Distractions.ps1` PowerShell script
- Has adopted verification-first response style as a standing requirement for all technical discussions

---

*Copyright © 2026 Michael Herman (Bindloss, Alberta, Canada)*
*Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)*
*Web 7.0™, SOVRONA™, and SVRN7™ are trademarks of the Web 7.0 Foundation. All Rights Reserved.*

## Svrn7.TDA Project — TDA Host Components (DSA 0.24 Epoch 0)

```
src/Svrn7.TDA/
├── Program.cs                   — .NET 8 console app entry point
├── TdaHost.cs                   — TdaOptions, SwitchboardHostedService, AddSvrn7Tda() DI
├── KestrelListenerService.cs    — POST /didcomm, HTTP/2 + mTLS, UnpackAsync boundary
├── DIDCommMessageSwitchboard.cs — sole inbox reader, epoch gate, DID URL pass-by-reference
├── RunspacePoolManager.cs       — PowerShell RunspacePool (min=2, max=N), epoch refresh
├── LobeManager.cs               — eager/JIT LOBE loading from lobes.config.json
├── Svrn7RunspaceContext.cs      — $SVRN7 session variable: Driver, Inbox, Cache, Epoch
└── TdaResourceAddress.cs        — DID URL typed builder/parser (delegates to TdaResourceId)
```

### InboxMessage.Id — TDA Resource DID URL
InboxMessage.Id is generated as a full TDA resource DID URL (not a UUID):
  `did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1c8e9d7f012345678`
Generated in LiteInboxStore.EnqueueAsync() via TdaResourceId.InboxMessage().
The Switchboard passes this DID URL by reference to LOBE cmdlet pipelines.
GetMessageAsync() accepts the DID URL, uses it as IMemoryCache key directly.

### TdaResourceId (Svrn7.Core — zero dependencies)
Static helper for all TDA resource DID URL construction:
```csharp
TdaResourceId.InboxMessage(networkId, objectIdHex)  // → did:drn:.../inbox/msg/...
TdaResourceId.Citizen(networkId, citizenDidSuffix)  // → did:drn:.../main/citizen/...
TdaResourceId.LogEntry(networkId, blake3Hex)        // → did:drn:.../main/logentry/...
TdaResourceId.Schema(networkId, schemaName)         // → did:drn:.../schemas/schema/...
TdaResourceId.NetworkIdFromDid(did)                 // strips "did:drn:" prefix
TdaResourceId.ParseKey(didUrl)                      // extracts final path segment
```

### Schema Registry (Society TDA Only — DSA 0.24)
New in DSA 0.24. Conditional: only instantiated by AddSvrn7Society().
- SchemaLiteContext  → svrn7-schemas.db
- ISchemaRegistry   → LiteSchemaRegistry  (register, getByName, deactivate)
- ISchemaResolver   → LiteSchemaResolver  (resolveByName, resolveByDidUrl)
- DID URL key type: Named (common name) — e.g., CitizenEndowmentCredential
- Example: `did:drn:alpha.svrn7.net/schemas/schema/CitizenEndowmentCredential`

### LOBE Modules (lobes/)
All new LOBEs have both .psm1 and .psd1 manifests.
Eager (InitialSessionState): Svrn7.Common, Svrn7.Federation, Svrn7.Society
JIT (Import-Module on first use):
  Svrn7.Email.psm1         — email/1.0/* (RFC 5322 tunneling over DIDComm)
  Svrn7.Calendar.psm1      — calendar/1.0/* (iCalendar tunneling over DIDComm)
  Svrn7.Presence.psm1      — presence/1.0/* (net-new DIDComm protocol)
  Svrn7.Notifications.psm1 — notification/1.0/* (net-new DIDComm protocol)
  Svrn7.Onboarding.psm1    — onboard/1.0/* (wraps Register-Svrn7CitizenInSociety)
  Svrn7.Invoicing.psm1     — invoice/1.0/* (wraps Invoke-Svrn7IncomingTransfer)

### Agent Scripts (lobes/)
  Agent1-Coordinator.ps1  — dispatch to Email/Calendar/Presence/Notifications LOBEs
  Agent2-Onboarding.ps1   — onboard/1.0/request pipeline
  AgentN-Invoicing.ps1    — invoice/1.0/request pipeline (same + cross-Society)

## LOBE Registry and Dynamic Dispatch (v0.8.0)

### LobeManager extensions (Svrn7.TDA/LobeManager.cs)
Three new methods added to LobeManager:

```
RegisterFromDescriptor(path)      — parse .lobe.json → populate _exactRegistry / _prefixRegistry
EnsureLoadedAsync(ps, modulePath) — idempotent JIT Import-Module into calling runspace
TryResolveProtocol(messageType)   — exact match first, then longest-prefix match; returns LobeProtocolRegistration?
```

FileSystemWatcher started in BuildInitialSessionState() — detects new *.lobe.json files at runtime.
ScanDescriptors() called at startup — bootstraps registry from all existing descriptors.
Dependency graph resolved in RegisterFromDescriptor() — LOBE B with dependency on LOBE A causes A to register first.
Epoch gating in RegisterFromDescriptor() — LOBEs with epochRequired > CurrentEpoch are silently skipped.

### DIDCommMessageSwitchboard — dynamic dispatch (Svrn7.TDA/DIDCommMessageSwitchboard.cs)
Hardcoded ResolveCmdlet switch REMOVED. Routing is now fully descriptor-driven:
```csharp
var reg = _lobes.TryResolveProtocol(msg.MessageType);  // dynamic lookup
await _lobes.EnsureLoadedAsync(ensurePs, reg.ModulePath, ct);  // JIT import
await InvokeCmdletPipelineAsync(reg.Entrypoint, msg.Id, ct);   // dispatch
```
Only hardcoded concern remaining: Option A transfer/1.0/order idempotency check.
IsPermittedInEpoch() now reads epochRequired from the LobeProtocolRegistration.

### .lobe.json descriptor format (lobes/{module-name}.lobe.json)
9 standard LOBE descriptors: Svrn7.Email, Svrn7.Calendar, Svrn7.Presence,
Svrn7.Notifications, Svrn7.Onboarding, Svrn7.Invoicing, Svrn7.Society,
Svrn7.Federation, Svrn7.Common.

Key descriptor fields for AI legibility:
- cmdlets[].title          — display name (mirrors MCP Tool.title)
- cmdlets[].inputSchema    — JSON Schema 2020-12 (optional Epoch 0, present on all standard LOBEs)
- cmdlets[].outputSchema   — JSON Schema 2020-12 (optional Epoch 0, present on all standard LOBEs)
- cmdlets[].annotations.{idempotent, modifiesState, destructive, pipelinePosition, requiresEpoch}
- ai._note                 — Epoch 0 design intent for future MCP-compatible tools/list interface
- ai.{summary, useCases, compositionHints, limitations}

### LobeRegistration.cs (Svrn7.TDA)
9 C# classes: LobeDescriptor, LobeMetadata, LobeProtocol, LobeCmdlet,
LobeCmdletAnnotations, LobeDependencies, LobeAiMetadata, LobeProtocolRegistration.
LobeDescriptor.LoadFromFile(path) — static factory; returns null on missing/malformed file.
LobeProtocolRegistration — denormalised record stored in Switchboard registry:
  (LobeId, LobeName, ModulePath, Entrypoint, Match, EpochRequired)

## PPML Legend 0.25

The PPML Legend 0.25 is the normative visual grammar for the Web 7.0 DSA 0.24 Epoch 0 diagram.
Formally defined in draft-herman-parchment-programming-00 Section 5.2.1.

### Eleven element types with visual specifications:

| # | Element Type            | Border         | Fill              | Semantic class               | SVRN7 specific? |
|---|-------------------------|----------------|-------------------|------------------------------|-----------------|
| 1 | Protocol                | Purple/violet  | Purple/violet     | Communication protocol       | No              |
| 2 | Network                 | Dark gold      | Yellow/gold       | Transport rail               | No              |
| 3 | LOBE                    | Medium blue    | White/light blue  | PS Module + .lobe.json       | No              |
| 4 | Device                  | Blue outline   | White/light blue  | UX device / interface        | No              |
| 5 | Data Storage            | Dark navy      | Dark navy         | LiteDB database              | No              |
| 6 | Data Access             | Dark navy      | Dark navy         | IXxxResolver / IMemoryCache  | No              |
| 7 | Runspace Pool           | Beige/khaki    | Light beige       | PS RunspacePool container    | Yes             |
| 8 | Switchboard             | Orange/amber   | Light orange      | DIDComm message router       | Yes             |
| 9 | Host                    | Green          | Light green       | .NET 8 process container     | No              |
|10 | PowerShell Runspace     | Crimson/red    | Light pink        | Named agent runspace         | Yes             |
|11 | Conditional Components  | Blue dashed    | Very light blue   | Conditional deployment group | No              |

### Derivation rules (each element type → software artefact):
- Protocol (1)  → KestrelListenerService.cs + HttpClient named "didcomm"
- LOBE (3)      → {Name}.psm1 + {Name}.psd1 + {Name}.lobe.json + exported cmdlets
- Data Storage (5) → LiteDB context class + IXxxStore interface + implementation
- Data Access (6)  → IXxxResolver interface + implementation(s)
- Runspace Pool (7)→ RunspacePoolManager.cs + InitialSessionState builder
- Switchboard (8)  → DIDCommMessageSwitchboard.cs + protocol registry (ConcurrentDictionary)
- Host (9)      → Program.cs + IServiceCollection DI + IHostedService registrations
- PS Runspace (10) → Agent{N}.ps1 + Switchboard protocol registration
- Conditional (11) → artefacts per enclosed types, guarded by named condition at instantiation

### Conditional Components Criteria examples:
- "Society TDA Only"  → Schema Registry, DID Doc Registry, VC Doc Registry (and their resolvers)
- "Epoch 1+"          → components activated at epoch transition
