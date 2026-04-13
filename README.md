# Web 7.0 Decentralized System Architecture (DSA)
## Citizen/Society Trusted Digital Assistant (TDA) — v0.8.0

> **Epoch 0 — Endowment Phase** | .NET 8 | DIDComm V2 | PowerShell LOBEs | W3C DID + VC | LiteDB | PPML

[![License: MIT](https://img.shields.io/badge/Code-MIT-yellow.svg)](https://opensource.org/licenses/MIT) [![License: CC BY-SA 4.0](https://img.shields.io/badge/Docs-CC--BY--SA--4.0-lightgrey.svg)](https://creativecommons.org/licenses/by-sa/4.0/)

The Web 7.0 Decentralized System Architecture (DSA) is a sovereign, DID-native, DIDComm-native
runtime for digital participation. Every participant in the Web 7.0 ecosystem operates a
**Trusted Digital Assistant (TDA)** — a personal or institutional software agent that holds
identity, manages value, communicates exclusively over end-to-end encrypted DIDComm channels,
and participates in **Verifiable Trust Circles (VTC7)** — federated peer meshes in which
identity and trust are cryptographic properties, not institutional ones.

This repository is the Epoch 0 (Endowment Phase) reference implementation of the Web 7.0 DSA,
specified by the DSA 0.24 diagram using the Parchment Programming Modeling Language (PPML).
It includes the TDA Host runtime, all eleven standard LOBE modules, the SOVRONA (SVRN7)
Shared Reserve Currency (SRC) library, and fifteen IETF Internet-Drafts.

---

## Table of Contents

1. [What is the Web 7.0 DSA?](#1-what-is-the-web-70-dsa)
2. [The Trusted Digital Assistant (TDA)](#2-the-trusted-digital-assistant-tda)
3. [Parchment Programming and Consistent Code Generation](#3-parchment-programming-and-consistent-code-generation)
4. [Architecture — DSA 0.24 Epoch 0](#4-architecture--dsa-024-epoch-0)
5. [TDA Host Runtime](#5-tda-host-runtime)
6. [LOBE Registry](#6-lobe-registry)
7. [DIDComm V2 Integration](#7-didcomm-v2-integration)
8. [SOVRONA (SVRN7) Shared Reserve Currency](#8-sovrona-svrn7-shared-reserve-currency)
9. [Identity Model](#9-identity-model)
10. [Verifiable Credentials](#10-verifiable-credentials)
11. [Merkle Audit Log](#11-merkle-audit-log)
12. [GDPR Compliance](#12-gdpr-compliance)
13. [Getting Started — TDA Host](#13-getting-started--tda-host)
14. [Getting Started — Federation Library](#14-getting-started--federation-library)
15. [Getting Started — Society Library](#15-getting-started--society-library)
16. [Configuration Reference](#16-configuration-reference)
17. [DIDComm Protocol URIs](#17-didcomm-protocol-uris)
18. [Exception Reference](#18-exception-reference)
19. [Solution Structure](#19-solution-structure)
20. [Testing](#20-testing)
21. [Naming Conventions](#21-naming-conventions)
22. [NuGet Dependencies](#22-nuget-dependencies)
23. [Roadmap](#23-roadmap)

---

## 1. What is the Web 7.0 DSA?

The Web 7.0 Decentralized System Architecture is a design framework and reference
implementation for sovereign digital participation. Its governing premise is that **identity
precedes participation** — every action in the system is taken by a DID holder, every
entitlement is a Verifiable Credential, and trust between parties is established by
cryptographic proof, not by institutional authority.

The DSA has five structural layers:

```
+--------------------------------------------------------------+
|  VTC7 Mesh  — Verifiable Trust Circles                       |
|  Federated peer TDAs; DIDComm-native; no central broker      |
+--------------------------------------------------------------+
|  TDA  — Trusted Digital Assistant                            |
|  Sovereign agent runtime; LOBEs; Switchboard; Runspace Pool  |
+--------------------------------------------------------------+
|  DIDComm V2  — Transport                                     |
|  SignThenEncrypt; HTTP/2 + mTLS; did:drn Locator DID URLs    |
+--------------------------------------------------------------+
|  W3C DID + VC  — Identity and Trust                          |
|  did:drn method; VTC7 proof sets; IETF-specified             |
+--------------------------------------------------------------+
|  SVRN7 SRC  — Value Layer                                    |
|  Shared Reserve Currency; UTXO; RFC 6962 Merkle log          |
+--------------------------------------------------------------+
```

The DSA is not a blockchain. There is no shared ledger, no consensus protocol, no mining.
Trust is a property of cryptographic identity and standards-based credential exchange
between sovereign agents.

---

## 2. The Trusted Digital Assistant (TDA)

A TDA is a sovereign runtime — a .NET 8 console application (Generic Host + Kestrel HTTP/2
+ mTLS) that acts on behalf of a citizen or a Society. It has exactly one inbound surface:

```
POST /didcomm   (HTTP/2 + mTLS, DIDComm V2 SignThenEncrypt)
```

All TDA-to-TDA communication is DIDComm. No SMTP, no CalDAV, no gRPC, no public REST API.
The TDA is the boundary of trust: only packed, authenticated DIDComm messages enter or leave.

Internally, the TDA is structured around the PPML Legend 0.25 element types:

| PPML Element      | TDA Component                           | Artefact                            |
|-------------------|-----------------------------------------|-------------------------------------|
| Host              | TDA process (Program.cs)                | .NET 8 Generic Host + DI            |
| Runspace Pool     | RunspacePoolManager                     | PS RunspacePool + InitialSessionState |
| PowerShell Runspace | Agent scripts (Agent1, Agent2, AgentN) | .ps1 + Switchboard routing          |
| Switchboard       | DIDCommMessageSwitchboard               | ConcurrentDictionary protocol registry |
| LOBE              | PowerShell modules (.psm1)              | .psm1 + .psd1 + .lobe.json          |
| Data Storage      | LiteDB databases                        | LiteDB context class + IXxxStore    |
| Data Access       | Resolvers / caches                      | IXxxResolver + IMemoryCache         |
| Protocol          | Kestrel listener + HttpClient           | KestrelListenerService.cs           |
| Network           | Internet/LAN/P2P                        | Transport configuration             |

Every component is traceable to a diagram element in DSA 0.24 via a derivation trace comment.

---

## 3. Parchment Programming and Consistent Code Generation

This repository is specified and built using **Parchment Programming** (PPML — Parchment
Programming Modeling Language), a diagram-first methodology in which the DSA 0.24 Epoch 0
architecture diagram is the primary specification and all code is derived from it. PPML has
nine core principles (PP-1 through PP-9).

Every source file carries a derivation trace:
```csharp
// Derived from: "DIDComm Message Switchboard" — element type Switchboard — DSA 0.24 Epoch 0 (PPML)
```

**PP-9 Consistent Code Generation** — the most relevant principle for AI-assisted
development — states that two independent AI generators given the same conformant diagram
MUST produce functionally equivalent artefacts. This enables **session independence**: the
diagram alone, without chat history, is sufficient to regenerate any artefact correctly.

For the full treatment of PPML implications for software development — including the
specification artefact inversion, deterministic AI code generation, explicit architectural
change governance, testability traceability, documentation staleness detection, and
scalability with AI capability — see:

- **`draft-herman-parchment-programming-00`** Section 8.6 — *Implications for Software Development* (normative)
- **`SVRN7_Architecture_Whitepaper.docx`** Section 2a — *Parchment Programming and Consistent Code Generation*
- **`Web7_TDA_Design_v024_Consolidated.docx`** Section 11b — *PPML Implications for this Codebase*
- **`draft-herman-svrn7-ai-legibility-00`** Section 13a — *PPML and AI Legibility* (AI-specific implications)

---

## 4. Architecture — DSA 0.24 Epoch 0

### Deployment Topology

```
+-------------------------------------------------------------+
|  Web 7.0 Federation   (ISvrn7Driver)                        |
|  . Federation wallet — sole source of all SVRN7 SRC         |
|  . Global DID method name registry                          |
|  . Supply governance (monotonically increasing)             |
+------------+--------------------+-------------+------------+
             |  DIDComm V2        |             |
      +-------+------+    +-------+------+  +---+----------+
      |  Society A   |    |  Society B   |  |  Society N   |
      |  Citizen TDAs|    |  Citizen TDAs|  |  Citizen TDAs|
      +--------------+    +--------------+  +--------------+
```

Each participant — Federation, Society, and Citizen — operates a TDA. A Society TDA manages
the monetary layer and registry services. A Citizen TDA manages personal identity,
communications, and value exchange.

### Solution Structure

```
Web7-DSA.sln
+-- src/
|   +-- Svrn7.Core/        Models, interfaces, exceptions, TdaResourceId
|   +-- Svrn7.Crypto/      secp256k1, Ed25519, AES-256-GCM, Blake3
|   +-- Svrn7.Store/       LiteDB: 4 database contexts + store implementations
|   +-- Svrn7.Ledger/      RFC 6962 Merkle log, 8-step transfer validator
|   +-- Svrn7.Identity/    W3C VC v2 JWT issuance, verification, revocation
|   +-- Svrn7.DIDComm/     DIDComm V2: 5 pack modes, RFC 3394, X25519
|   +-- Svrn7.Federation/  ISvrn7Driver (44+ members), DI extensions
|   +-- Svrn7.Society/     ISvrn7SocietyDriver, InboxStore, SchemaRegistry
|   +-- Svrn7.TDA/         TDA Host: Kestrel, Switchboard, LobeManager, RunspacePool
+-- lobes/                 11 LOBE modules (.psm1 + .psd1 + .lobe.json) + 3 agent scripts
+-- specs/                 15 IETF Internet-Drafts
+-- docs/                  Design documents, whitepaper, principles of operations
+-- tests/
    +-- Svrn7.Tests/            94 federation tests
    +-- Svrn7.Society.Tests/    11 society tests
    +-- Svrn7.TDA.Tests/        62 TDA + LOBE registry tests
```

### NuGet Package Hierarchy

```
Svrn7.TDA        (deployable runtime — not a NuGet package)
Svrn7.Society
    +-- Svrn7.Federation
         +-- Svrn7.DIDComm
         +-- Svrn7.Identity
         +-- Svrn7.Ledger
         +-- Svrn7.Store
         +-- Svrn7.Crypto
         +-- Svrn7.Core      (zero dependencies)
```

---

## 5. TDA Host Runtime

Derived from: "Citizen/Society TDA (Host)" — element type Host — DSA 0.24 Epoch 0 (PPML).

**Inbound**: `POST /didcomm` (Kestrel HTTP/2 + mTLS)
→ `KestrelListenerService.UnpackAsync()`
→ `LiteInboxStore.EnqueueAsync()` — persists to `svrn7-inbox.db`
→ `DIDCommMessageSwitchboard` — routes by `@type` Locator DID URL
→ LOBE cmdlet pipeline (PowerShell Runspace)

**Outbound**: LOBE returns `OutboundMessage { PeerEndpoint, PackedMessage, MessageType }`
→ `DIDCommMessageSwitchboard.EnqueueOutbound()`
→ `HttpClient` HTTP/2 POST to peer TDA endpoint

### Message Identity — Pass-by-Reference

Every inbound message is assigned a TDA resource DID URL at ingestion:

```
did:drn:{networkId}/inbox/msg/{objectId}
```

Example: `did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1c8e9d7f012345678`

The Switchboard passes this DID URL — not the payload — to the LOBE cmdlet pipeline.
LOBEs call `$SVRN7.GetMessageAsync($MessageDid)` to resolve the payload on demand.
This is the pass-by-reference constraint derived from the Data Access arrow in DSA 0.24.

### Dead-Letter Outbox

Failed outbound messages (after Polly retry exhaustion) are persisted to `IOutboxStore`
(`LiteOutboxStore` in `svrn7-inbox.db`) for operator inspection and replay.

---

## 6. LOBE Registry

LOBEs (Loadable Object Brain Extensions) are PowerShell modules — the cognitive capability
layer of the TDA. Every LOBE ships three files:

```
{Name}.psm1        PowerShell module
{Name}.psd1        PowerShell manifest
{Name}.lobe.json   LOBE descriptor
```

### Standard LOBE Inventory (v0.8.0)

| #  | Module                    | Loading | Protocol families        | Role                        |
|----|---------------------------|---------|--------------------------|-----------------------------|
|  1 | `Svrn7.Common`            | Eager   | —                        | Shared helpers              |
|  2 | `Svrn7.Federation`        | Eager   | transfer/1.0/*, did/1.0/*| DID management, key pairs   |
|  3 | `Svrn7.Society`           | Eager   | transfer/1.0/*, onboard/*| Monetary + identity ops     |
|  4 | `Svrn7.UX`                | Eager   | ux/1.0/*                 | UX adapter, balance updates |
|  5 | `Svrn7.Email`             | JIT     | email/1.0/*              | RFC 5322 over DIDComm       |
|  6 | `Svrn7.Calendar`          | JIT     | calendar/1.0/*           | iCalendar over DIDComm      |
|  7 | `Svrn7.Presence`          | JIT     | presence/1.0/*           | TDA availability status     |
|  8 | `Svrn7.Notifications`     | JIT     | notification/1.0/*       | Typed alert dispatch        |
|  9 | `Svrn7.Onboarding`        | JIT     | onboard/1.0/*            | Citizen registration        |
| 10 | `Svrn7.Invoicing`         | JIT     | invoice/1.0/*            | Invoice-to-payment          |
| 11 | `Svrn7.Identity`          | JIT     | did/1.0/*, vc/1.0/*      | DID Document + VC resolution|

**Eager**: pre-loaded into `InitialSessionState` at TDA startup.
**JIT**: imported on first inbound message of a matching `@type` via `LobeManager.EnsureLoadedAsync()`.

### LOBE Descriptor Format

Each `.lobe.json` declares:
- Protocol URI registrations (`match: "exact"` or `"prefix"`) for Switchboard routing
- MCP-aligned cmdlet definitions with `inputSchema`/`outputSchema` (JSON Schema 2020-12)
- Behavioural `annotations` (`idempotent`, `modifiesState`, `destructive`, `pipelinePosition`)
- `dependencies.lobes` for dependency graph resolution
- `ai` block (`summary`, `useCases`, `compositionHints`, `limitations`)

In a future epoch, the TDA will expose LOBEs as MCP tools via `tools/list` — the descriptor
becomes the MCP tool definition with no translation needed.

### Dynamic Registration

`LobeManager` scans all `*.lobe.json` files at startup and watches for new files via
`FileSystemWatcher`. Third-party LOBEs can be hot-loaded without TDA restart.

### Pipeline Semantics

```powershell
# Example: citizen onboarding pipeline
Get-TdaMessage -Did $MessageDid |
    ConvertFrom-TdaOnboardRequest |
    Register-Svrn7CitizenInSociety |
    New-TdaOnboardReceipt |
    Send-TdaMessage
```

---

## 7. DIDComm V2 Integration

All TDA-to-TDA communication is DIDComm V2, **SignThenEncrypt** default:

| Mode             | Algorithm                          | Use              |
|------------------|------------------------------------|------------------|
| `Plaintext`      | None                               | Testing only     |
| `Anoncrypt`      | ECDH-ES+A256KW / AES-256-GCM       | Sender anonymous |
| `Authcrypt`      | ECDH-1PU+A256KW / AES-256-GCM      | Authenticated    |
| `SignOnly`       | EdDSA (Ed25519) JWS                | Attestation      |
| `SignThenEncrypt`| JWS inside Anoncrypt JWE           | **Default**      |

### Protocol URI Scheme

All `@type` URIs are **Locator DID URLs** — not `https://` URIs:

```
did:drn:svrn7.net / protocols / transfer / 1.0 / request
+-----------------+ +----------------------------------+
Identity DID        DID URL path (Locator)
(protocol namespace)(specific protocol definition)
```

The SVRN7 ecosystem is intentionally self-contained. Cross-ecosystem interoperability with
non-SVRN7 DIDComm agents is not a goal.

---

## 8. SOVRONA (SVRN7) Shared Reserve Currency

SVRN7 is the value layer of the Web 7.0 DSA — a Shared Reserve Currency (SRC) embedded
within the TDA and governed by a three-epoch monetary lifecycle.

### Units

| Unit    | Value          | Note                           |
|---------|----------------|--------------------------------|
| `grana` | 1              | Smallest unit. All math: long. |
| `SVRN7` | 1,000,000 grana| Display denomination           |

### Epoch Matrix

| Epoch | Name              | Permitted Transfers                              |
|-------|-------------------|--------------------------------------------------|
| 0     | Endowment         | Citizen to own Society or Federation only        |
| 1     | Ecosystem Utility | Any citizen to any citizen across any Society    |
| 2     | Market Issuance   | Open-market rules (future)                       |

### Supply and Endowment Chain

```
Federation wallet  (1,000,000,000 SVRN7 at genesis)
    |
    +-- RegisterSocietyAsync()       --> Society wallet  (EndowmentPerSocietyGrana)
            |
            +-- RegisterCitizenAsync() --> Citizen wallet (1,000 SVRN7)
```

Supply conservation is an invariant: total circulating supply always equals
`FederationRecord.TotalSupplyGrana` minus the Federation wallet balance. No synthetic grana
are ever created.

### 8-Step Transfer Validator

| Step | Name                    | Description                                  |
|------|-------------------------|----------------------------------------------|
| 0    | NormaliseDids           | Resolve any DID to primary DID (Society only)|
| 1    | ValidateFields          | Non-null, amount > 0, memo <= 256 chars      |
| 2    | ValidateEpochRules      | Epoch matrix enforcement                     |
| 3    | ValidateNonce           | 24-hour replay window                        |
| 4    | ValidateFreshness       | +/-10 minute timestamp window                |
| 5    | ValidateSanctions       | ISanctionsChecker                            |
| 6    | ValidateSignature       | secp256k1 CESR over canonical JSON           |
| 7    | ValidateBalance         | Dry-run UTXO sum                             |
| 8    | ValidateSocietyMembership | Cross-Society Epoch 1: payee citizenship   |

### Four-Database Design

| Database        | Default file          | Contents                               |
|-----------------|-----------------------|----------------------------------------|
| `svrn7.db`      | `data/svrn7.db`       | Wallets, UTXOs, citizens, Merkle log   |
| `svrn7-dids.db` | `data/svrn7-dids.db`  | DID Documents, version history         |
| `svrn7-vcs.db`  | `data/svrn7-vcs.db`   | Verifiable Credentials, revocations    |
| `svrn7-inbox.db`| `data/svrn7-inbox.db` | Inbox queue, Schema Registry, outbox   |

All paths accept `:memory:` for zero-disk testing.

---

## 9. Identity Model

### Hierarchy

```
Federation (1)
    +-- Societies (N)  -- each with 1..N DID method names
         +-- Citizens (M per Society)  -- each with 1..N DIDs
```

Every participant has exactly one primary DID — the wallet key, immutable.

### Identity DID vs Locator DID URL

Formalised in `draft-herman-did-w3c-drn-00` Section 5a (W3C DID Core Section 3.2):

| Form               | Delimiter | Example                                      | DID Document? |
|--------------------|-----------|----------------------------------------------|---------------|
| Identity DID       | `:`       | `did:drn:alice.alpha.svrn7.net`              | Yes           |
| Locator DID URL    | `/`       | `did:drn:alpha.svrn7.net/inbox/msg/5f43a2...`| No            |

Identity DIDs identify subjects. Locator DID URLs address resources. The `:` vs `/` choice
reflects W3C DID Core structural semantics, made explicit as a design principle.

### DID Method Name Lifecycle

```
Never existed --> Active --> Dormant (deregistered) --> Available --> Active (re-registered)
```

Primary method name cannot be deregistered. Existing DIDs under a deregistered method
remain resolvable — deregistration only prevents new issuance.

---

## 10. Verifiable Credentials

### Credential Types

| Type                                | Issuer     | Subject  | Validity   |
|-------------------------------------|------------|----------|------------|
| `Svrn7EndowmentCredential`          | Society    | Citizen  | 5 years    |
| `Svrn7SocietyRegistrationCredential`| Federation | Society  | Indefinite |
| `Svrn7EpochCredential`              | Federation | Federation| Per epoch |
| `TransferOrderCredential`           | Orig. Society | Payee | 24 hours  |
| `TransferReceiptCredential`         | Recv. Society | Payer  | 24 hours  |

### Lifecycle

```
Active --> Suspended --> Active  (ReinstateVcAsync)
Active --> Revoked              (permanent)
Active --> Expired              (auto-detected on read)
```

Cross-Society VC resolution (`FindBySubjectAcrossSocietiesAsync`) performs a DIDComm
fan-out to all known Societies, returning partial results when some time out.

---

## 11. Merkle Audit Log

All significant state changes are appended to an RFC 6962 SHA-256 Merkle log:

```
Leaf:     SHA-256(0x00 || data)
Internal: SHA-256(0x01 || left || right)
```

Entry types include: citizen/society registration, supply updates, epoch transitions,
transfers (debit/credit/settlement), DID method registration/deregistration, VC revocation,
GDPR erasure. UTXO records and tree heads are retained permanently — deletion is impossible.

---

## 12. GDPR Compliance

`ErasePersonAsync(did, controllerSignature, requestTimestamp)`:

1. Validates controller signature
2. Burns `EncryptedPrivateKeyBase64` to CSPRNG bytes — private key permanently lost
3. Nullifies all PII fields on `CitizenRecord`
4. Deactivates all DID Documents for the citizen
5. Revokes all VCs where citizen is subject
6. Appends `GdprErasure` to Merkle log (non-repudiable proof of erasure)
7. UTXO records retained — required for supply conservation audit

---

## 13. Getting Started — TDA Host

The TDA Host (`Svrn7.TDA`) is a deployable .NET 8 console app, not a NuGet package.

```bash
cd src/Svrn7.TDA
dotnet run
```

`appsettings.json`:

```json
{
  "Tda": {
    "SocietyDid":      "did:drn:alpha.svrn7.net",
    "NetworkId":       "alpha.svrn7.net",
    "LobesConfigPath": "lobes/lobes.config.json",
    "LobeDirectory":   "lobes/",
    "InboxDbPath":     "data/svrn7-inbox.db",
    "HttpPort":        8080,
    "CertificatePath": "certs/tda.pfx"
  }
}
```

---

## 14. Getting Started — Federation Library

```xml
<PackageReference Include="Svrn7.Federation" Version="0.8.0" />
```

```csharp
builder.Services.AddSvrn7Federation(opts =>
{
    opts.FoundationPublicKeyHex  = Environment.GetEnvironmentVariable("SVRN7_FOUNDATION_KEY")!;
    opts.Svrn7DbPath  = "data/svrn7.db";
    opts.DidsDbPath   = "data/svrn7-dids.db";
    opts.VcsDbPath    = "data/svrn7-vcs.db";
    opts.DidMethodName           = "web7";
    opts.DidMethodDormancyPeriod = TimeSpan.FromDays(30);
});

// Genesis (run once)
var driver  = app.Services.GetRequiredService<ISvrn7Driver>();
var keyPair = driver.GenerateSecp256k1KeyPair();
// Store keyPair.PrivateKeyBytes in HSM -- never in config

await driver.InitialiseFederationAsync(new InitialiseFederationRequest
{
    Did                      = "did:web7:foundation",
    PublicKeyHex             = keyPair.PublicKeyHex,
    FederationName           = "Web 7.0 Foundation",
    PrimaryDidMethodName     = "web7",
    TotalSupplyGrana         = Svrn7Constants.FederationInitialSupplyGrana,
    EndowmentPerSocietyGrana = 1_000_000 * Svrn7Constants.GranaPerSvrn7,
});
```

---

## 15. Getting Started — Society Library

```xml
<PackageReference Include="Svrn7.Society" Version="0.8.0" />
```

```csharp
builder.Services.AddSvrn7Society(opts =>
{
    opts.SocietyDid    = "did:socalpha:my-society";
    opts.FederationDid = "did:web7:foundation";
    opts.DrawAmountGrana         = 100_000 * Svrn7Constants.GranaPerSvrn7;
    opts.OverdraftCeilingGrana   = 1_000_000 * Svrn7Constants.GranaPerSvrn7;
    opts.SocietyMessagingPrivateKeyEd25519   = societyEd25519PrivKey;
    opts.FederationMessagingPublicKeyEd25519 = federationEd25519PubKey;
    opts.FederationEndpoint = "https://federation.svrn7.net/didcomm";
});

// Register a citizen
var driver     = app.Services.GetRequiredService<ISvrn7SocietyDriver>();
var citizenKey = driver.GenerateSecp256k1KeyPair();

await driver.RegisterCitizenInSocietyAsync(new RegisterCitizenInSocietyRequest
{
    Did             = "did:socalpha:citizen-alice",
    PublicKeyHex    = citizenKey.PublicKeyHex,
    PrivateKeyBytes = citizenKey.PrivateKeyBytes,
    SocietyDid      = "did:socalpha:my-society",
});
// Alice's wallet now contains 1,000 SVRN7 (CitizenEndowmentGrana)
```

---

## 16. Configuration Reference

### TdaOptions

| Property          | Default                     | Description                        |
|-------------------|-----------------------------|------------------------------------|
| `SocietyDid`      | *(required)*                | This TDA's Society DID             |
| `NetworkId`       | *(required)*                | Network identifier                 |
| `LobesConfigPath` | `lobes/lobes.config.json`   | LOBE loading manifest path         |
| `LobeDirectory`   | `lobes/`                    | Watched for new .lobe.json files   |
| `InboxDbPath`     | `data/svrn7-inbox.db`       | LiteDB inbox + schema + outbox     |
| `HttpPort`        | `8080`                      | Kestrel listen port                |

### Svrn7Options (Federation / Society)

| Property                         | Default        | Description                           |
|----------------------------------|----------------|---------------------------------------|
| `FoundationPublicKeyHex`         | *(required)*   | Foundation governance secp256k1 key   |
| `Svrn7DbPath`                    | `data/svrn7.db`| Main LiteDB                           |
| `DidsDbPath`                     | `data/svrn7-dids.db` | DID Document LiteDB             |
| `VcsDbPath`                      | `data/svrn7-vcs.db`  | VC LiteDB                       |
| `DidMethodName`                  | `drn`          | Primary DID method name               |
| `DidMethodDormancyPeriod`        | `30 days`      | Dormancy after deregistration         |
| `BackgroundSweepInterval`        | `1 hour`       | VC expiry + Merkle sign interval      |

---

## 17. DIDComm Protocol URIs

All SVRN7 `@type` URIs follow: `did:drn:svrn7.net/protocols/{family}/{version}/{type}`

**Core constants** (`Svrn7Constants.Protocols.*`):

| Constant             | URI                                                                    |
|----------------------|------------------------------------------------------------------------|
| `TransferRequest`    | `did:drn:svrn7.net/protocols/transfer/1.0/request`                    |
| `TransferReceipt`    | `did:drn:svrn7.net/protocols/transfer/1.0/receipt`                    |
| `TransferOrder`      | `did:drn:svrn7.net/protocols/transfer/1.0/order`                      |
| `TransferOrderReceipt`| `did:drn:svrn7.net/protocols/transfer/1.0/order-receipt`             |
| `OverdraftDrawRequest`| `did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-request`   |
| `OverdraftDrawReceipt`| `did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-receipt`   |
| `EndowmentTopUp`     | `did:drn:svrn7.net/protocols/endowment/1.0/top-up`                    |
| `SupplyUpdate`       | `did:drn:svrn7.net/protocols/supply/1.0/update`                       |
| `DidResolveRequest`  | `did:drn:svrn7.net/protocols/did/1.0/resolve-request`                 |
| `DidResolveResponse` | `did:drn:svrn7.net/protocols/did/1.0/resolve-response`                |
| `OnboardRequest`     | `did:drn:svrn7.net/protocols/onboard/1.0/request`                     |
| `OnboardReceipt`     | `did:drn:svrn7.net/protocols/onboard/1.0/receipt`                     |
| `InvoiceRequest`     | `did:drn:svrn7.net/protocols/invoice/1.0/request`                     |
| `InvoiceReceipt`     | `did:drn:svrn7.net/protocols/invoice/1.0/receipt`                     |

**LOBE protocol families** (declared in `.lobe.json` descriptors):

| Family          | URI prefix                                      | LOBE                   |
|-----------------|-------------------------------------------------|------------------------|
| Email           | `did:drn:svrn7.net/protocols/email/1.0/`        | `Svrn7.Email`          |
| Calendar        | `did:drn:svrn7.net/protocols/calendar/1.0/`     | `Svrn7.Calendar`       |
| Presence        | `did:drn:svrn7.net/protocols/presence/1.0/`     | `Svrn7.Presence`       |
| Notification    | `did:drn:svrn7.net/protocols/notification/1.0/` | `Svrn7.Notifications`  |
| UX              | `did:drn:svrn7.net/protocols/ux/1.0/`           | `Svrn7.UX`             |
| DID resolution  | `did:drn:svrn7.net/protocols/did/1.0/`          | `Svrn7.Identity`       |
| VC resolution   | `did:drn:svrn7.net/protocols/vc/1.0/`           | `Svrn7.Identity`       |

---

## 18. Exception Reference

| Exception                          | Thrown When                                           |
|------------------------------------|-------------------------------------------------------|
| `InsufficientBalanceException`     | UTXO sum insufficient for transfer                    |
| `EpochViolationException`          | Transfer violates current epoch rules                 |
| `InvalidDidException`              | DID malformed, unresolvable, or deactivated           |
| `NonceReplayException`             | Nonce reused within 24-hour window                    |
| `StaleTransferException`           | Timestamp outside +/-10 minute window                 |
| `SanctionedPartyException`         | Payer or payee on sanctions list                      |
| `SignatureVerificationException`   | secp256k1 or Ed25519 signature invalid                |
| `NotFoundException`                | Entity not found                                      |
| `DoubleSpendException`             | UTXO already spent                                    |
| `InvalidCredentialException`       | VC invalid, expired, or revoked                       |
| `ConfigurationException`           | Options missing or invalid                            |
| `MerkleIntegrityException`         | Merkle log integrity failure                          |
| `SocietyEndowmentDepletedException`| Overdraft ceiling reached                             |
| `FederationUnavailableException`   | DIDComm round-trip to Federation timed out            |
| `DuplicateDidMethodException`      | Method name already Active under another Society      |
| `DormantDidMethodException`        | Method name within dormancy period                    |
| `DeregisteredDidMethodException`   | Issuing DID under deregistered method                 |
| `PrimaryDidMethodException`        | Attempting to deregister primary method               |
| `UnresolvableDidException`         | DID method has no registered resolver                 |

---

## 19. Solution Structure (Detailed)

```
src/Svrn7.Core/
    Svrn7Constants.cs     Protocol constants, TdaResourceId DID URL builder, epoch values
    Models.cs             All record types: Wallet, Utxo, CitizenRecord, InboxMessage, ...
    Exceptions.cs         19 typed domain exceptions
    Interfaces.cs         All C# interfaces

src/Svrn7.TDA/
    Program.cs                    Entry point -- Generic Host startup
    TdaHost.cs                    DI container configuration
    KestrelListenerService.cs     POST /didcomm -- unpack -> persist -> enqueue
    DIDCommMessageSwitchboard.cs  Descriptor-driven routing + Option A transfer idempotency
    LobeManager.cs                RegisterFromDescriptor, EnsureLoadedAsync, FileSystemWatcher
    LobeRegistration.cs           C# model for .lobe.json (MCP-aligned)
    RunspacePoolManager.cs        PS RunspacePool + InitialSessionState
    Svrn7RunspaceContext.cs       $SVRN7 session variable
    TdaResourceAddress.cs         DID URL parser for TDA resource addresses
```

---

## 20. Testing

All tests use LiteDB `:memory:` — no disk I/O, no test isolation issues.

```bash
dotnet test                                    # all 3 projects (167 tests total)
dotnet test tests/Svrn7.Tests/                 # federation (94 tests)
dotnet test tests/Svrn7.Society.Tests/         # society (11 tests)
dotnet test tests/Svrn7.TDA.Tests/             # TDA + LOBE registry (62 tests)
dotnet test --collect:"XPlat Code Coverage"
```

`LobeManagerRegistryTests` covers: `RegisterFromDescriptor` (exact and prefix protocols),
`TryResolveProtocol` (exact beats prefix, longest-prefix wins), epoch gating, idempotency,
`FileSystemWatcher` hot-reload, and `IsRegistered`.

---

## 21. Naming Conventions

| Term                  | Correct                       | Incorrect                   |
|-----------------------|-------------------------------|-----------------------------|
| Protocol domain       | `svrn7.net`                   | `svrn7.io`                  |
| Resolution process    | DID Document Resolution       | DID Resolution              |
| Resolver interface    | `IDidDocumentResolver`        | `IDidResolver`              |
| VC resolver           | `IVcDocumentResolver`         | `IVcResolver`               |
| Smallest monetary unit| `grana`                       | `micro`, `satoshi`          |
| Primary token         | `SVRN7`                       | `SOVRONA` (informal only)   |
| DID method            | `did:drn`                     | `did:svrn7`                 |
| LOBE loading          | Eager / JIT                   | Always-on / Lazy            |
| PPML element 4        | `Device`                      | `DEVICE`                    |

---

## 22. NuGet Dependencies

| Package                                    | Version | Used In                      |
|--------------------------------------------|---------|------------------------------|
| `LiteDB`                                   | 5.0.21  | Svrn7.Store                  |
| `NBitcoin`                                 | 7.0.37  | Svrn7.Crypto, Svrn7.DIDComm  |
| `NSec.Cryptography`                        | 23.9.0  | Svrn7.Crypto, Svrn7.DIDComm  |
| `Blake3`                                   | 1.3.0   | Svrn7.Crypto                 |
| `Konscious.Security.Cryptography.Argon2`   | 1.3.1   | Svrn7.Crypto                 |
| `Microsoft.Extensions.*`                   | 8.0.x   | Svrn7.Federation, Society, TDA|
| `Microsoft.AspNetCore.Server.Kestrel`      | 2.2.0   | Svrn7.TDA                    |
| `System.Management.Automation`             | 7.4.6   | Svrn7.TDA                    |
| `xunit`                                    | 2.7.0   | Tests                        |
| `FluentAssertions`                         | 6.12.0  | Tests                        |

---

## 23. Roadmap

### v0.8.0 — TDA + LOBE Registry + Architectural Coherence (April 2026) <- *current*
- TDA Host: Kestrel, Switchboard, LobeManager, RunspacePool fully implemented
- Dynamic LOBE registry: `.lobe.json` descriptors + `FileSystemWatcher` hot-reload
- DIDComm protocol URIs: `did:drn:svrn7.net/protocols/...` (Locator DID URLs)
- PPML Legend 0.25 + PP-9 Consistent Code Generation formalised
- 11 standard LOBEs with MCP-aligned descriptors
- 15 IETF Internet-Drafts

### v0.9.0 — Epoch 2 Market Issuance
- Open-market transfer rules
- Full supply increase round-trip with Foundation signature
- Multi-signature governance operations

### v1.0.0 — Blockcore Mainnet Anchoring
- `did:drn` validator identity on Blockcore
- SLIP-0044 coin type registration
- On-chain Merkle tree head publication

### v1.1.0 — Production Release
- Full cross-Society DIDComm routing table
- NuGet publication on nuget.org

---

## IETF Internet-Drafts

| Draft                                       | Subject                                        |
|---------------------------------------------|------------------------------------------------|
| `draft-herman-did-w3c-drn-00`               | `did:drn` DID method + Web 7.0 profile         |
| `draft-herman-drn-resource-addressing-00`   | TDA Data Storage record addressing             |
| `draft-herman-vtc-proof-sets-01`            | Verifiable Trust Circle VC Proof Sets          |
| `draft-herman-didcomm-svrn7-transfer-00`    | SVRN7 DIDComm transfer protocol               |
| `draft-herman-svrn7-monetary-protocol-00`   | Monetary model and epoch governance            |
| `draft-herman-svrn7-overdraft-protocol-00`  | Society overdraft facility                     |
| `draft-herman-web7-society-architecture-00` | Society architecture                           |
| `draft-herman-web7-merkle-audit-log-00`     | RFC 6962 Merkle audit log                      |
| `draft-herman-web7-epoch-governance-00`     | Epoch transition governance                    |
| `draft-herman-did-method-governance-00`     | DID method name lifecycle                      |
| `draft-herman-svrn7-gdpr-erasure-00`        | GDPR erasure in a UTXO system                  |
| `draft-herman-svrn7-ai-legibility-00`       | AI legibility engineering                      |
| `draft-herman-tda-lobe-registry-00`         | TDA LOBE descriptor format and registry        |
| `draft-herman-cesr-svrn7-profile-00`        | CESR signature profile                         |
| `draft-herman-parchment-programming-00`     | PPML — Parchment Programming Modeling Language |

---

*Web 7.0 Foundation — Bindloss, Alberta, Canada — https://svrn7.net*
