# SOVRONA (SVRN7) — Web 7.0 Shared Reserve Currency (SRC) Library

> **Version 0.8.0** | .NET 8 | LiteDB | DIDComm v2 | W3C DID + VC | RFC 6962 Merkle Log | CC BY-SA 4.0 (docs) / MIT (code)

[![CI](https://github.com/web7foundation/svrn7/actions/workflows/ci.yml/badge.svg)](https://github.com/web7foundation/svrn7/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/Code-MIT-yellow.svg)](https://opensource.org/licenses/MIT) [![License: CC BY-SA 4.0](https://img.shields.io/badge/Docs-CC--BY--SA--4.0-lightgrey.svg)](https://creativecommons.org/licenses/by-sa/4.0/)

SVRN7 (SOVRONA) is the proposed Shared Reserve Currency (SRC) for the Web 7.0 digital ecosystem,
implemented as an embeddable .NET 8 library that manages citizen and society wallets,
enforces a governance-controlled three-epoch monetary lifecycle, and maintains a
cryptographically tamper-evident audit log of all transactions. Unlike traditional
cryptocurrencies, SVRN7 is built on a foundation of self-sovereign identity — every
participant is a DID holder, every entitlement is a Verifiable Credential, and trust
between parties is established through standards-based cryptographic proofs rather than
a shared blockchain or central authority.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Solution Structure](#2-solution-structure)
3. [Key Design Decisions](#3-key-design-decisions)
4. [Monetary Model](#4-monetary-model)
5. [Identity Model](#5-identity-model)
6. [DID Method Names](#6-did-method-names)
7. [Transfer Protocol](#7-transfer-protocol)
8. [Overdraft Facility](#8-overdraft-facility)
9. [DIDComm v2 Integration](#9-didcomm-v2-integration)
10. [Verifiable Credentials](#10-verifiable-credentials)
11. [Merkle Audit Log](#11-merkle-audit-log)
12. [GDPR Compliance](#12-gdpr-compliance)
13. [Getting Started — Federation](#13-getting-started--federation)
14. [Getting Started — Society](#14-getting-started--society)
15. [Configuration Reference](#15-configuration-reference)
16. [DIDComm Protocol URIs](#16-didcomm-protocol-uris)
17. [Exception Reference](#17-exception-reference)
18. [Testing](#18-testing)
19. [Naming Conventions](#19-naming-conventions)
20. [NuGet Dependencies](#20-nuget-dependencies)
21. [Roadmap](#21-roadmap)

---

## 1. Architecture Overview

Two NuGet packages in a strict dependency hierarchy:

```
Svrn7.Society   ← Society-level driver, DIDComm transfers, Federation resolvers
    └─ Svrn7.Federation ← Federation-level driver, ISvrn7Driver, options
         ├─ Svrn7.DIDComm   ← Full DIDComm v2 — five pack modes
         ├─ Svrn7.Identity  ← VC issuance / verification (W3C VC v2 JWT)
         ├─ Svrn7.Ledger    ← RFC 6962 Merkle log, 8-step transfer validator
         ├─ Svrn7.Store     ← LiteDB persistence — three independent databases
         ├─ Svrn7.Crypto    ← secp256k1, Ed25519, AES-256-GCM, Blake3, Base58btc
         └─ Svrn7.Core      ← Models, interfaces, exceptions, constants (zero deps)
```

### Three-Database Architecture

| Database | Default file | Contents |
|---|---|---|
| `svrn7.db` | `data/svrn7.db` | Wallets, UTXOs, citizens, societies, memberships, overdraft records, Merkle log, tree heads |
| `svrn7-dids.db` | `data/svrn7-dids.db` | DID Documents, version history, verification method index |
| `svrn7-vcs.db` | `data/svrn7-vcs.db` | Verifiable Credentials, revocation events |

All three paths can be set to `:memory:` for zero-disk testing.

### Deployment Topology

```
┌──────────────────────────────────────────────────────────┐
│  Web 7.0 Federation   (ISvrn7Driver)                     │
│  • Federation wallet — sole source of all SVRN7          │
│  • Global DID method name registry                       │
│  • Supply governance (monotonically increasing)          │
└──────────┬───────────────────┬───────────────┬───────────┘
           │  DIDComm          │               │
    ┌──────┴──────┐    ┌───────┴──────┐  ┌────┴──────────┐
    │  Society A  │    │  Society B   │  │  Society N    │
    │  did:soc-a  │    │  did:soc-b   │  │  did:soc-n    │
    │  citizens   │    │  citizens    │  │  citizens     │
    └─────────────┘    └──────────────┘  └───────────────┘
```

---

## 2. Solution Structure

```
Svrn7.sln
├── src/
│   ├── Svrn7.Core/
│   │   ├── Svrn7Constants.cs      Protocol constants, DIDComm URIs, epoch values
│   │   ├── Models.cs              All record types (Wallet, Utxo, CitizenRecord, ...)
│   │   ├── Exceptions.cs          19 typed domain exceptions
│   │   └── Interfaces.cs          All interfaces (IDidDocumentResolver, IVcDocumentResolver, ...)
│   ├── Svrn7.Crypto/
│   │   └── CryptoService.cs       secp256k1, Ed25519, AES-256-GCM, Blake3, Base58btc
│   ├── Svrn7.Store/
│   │   ├── Svrn7LiteContext.cs    svrn7.db LiteDB context
│   │   ├── LiteStores.cs          LiteWalletStore, LiteIdentityRegistry
│   │   ├── LiteRegistries.cs      LiteDidDocumentRegistry, LiteVcRegistry
│   │   └── LiteFederationAndResolvers.cs
│   │                              LiteFederationStore, LocalDidDocumentResolver,
│   │                              LiteVcDocumentResolver
│   ├── Svrn7.Ledger/
│   │   ├── MerkleLog.cs           RFC 6962 SHA-256 Merkle log
│   │   └── TransferValidator.cs   8-step federation transfer validator
│   ├── Svrn7.Identity/
│   │   └── VcService.cs           W3C VC v2 JWT issue, verify, revoke
│   ├── Svrn7.DIDComm/
│   │   ├── DIDCommPacker.cs       5 pack modes + RFC 3394 key wrap + RFC 7748 key conversion
│   │   └── DIDCommService.cs      DIDCommPackingService (high-level facade)
│   ├── Svrn7.Federation/
│   │   ├── ISvrn7Driver.cs        ISvrn7Driver (41+ members) + Svrn7Options
│   │   ├── Svrn7Driver.cs         Concrete Federation driver
│   │   └── ServiceCollectionExtensions.cs  AddSvrn7Federation() DI registration
│   └── Svrn7.Society/
│       ├── ISvrn7SocietyDriver.cs ISvrn7SocietyDriver : ISvrn7Driver
│       ├── Svrn7SocietyDriver.cs  Concrete Society driver
│       ├── SocietyTransferValidator.cs  8-step Society validator (Step 0: NormaliseDids)
│       ├── FederationResolvers.cs FederationDidDocumentResolver, FederationVcDocumentResolver
│       ├── DIDCommServices.cs     DIDCommTransferHandler, DIDCommMessageProcessorService
│       └── SocietyExtensions.cs   AddSvrn7Society() DI registration
└── tests/
    ├── Svrn7.Tests/               207 federation-level facts, :memory: databases
    └── Svrn7.Society.Tests/       Society citizen registration, DID methods, overdraft
```

---

## 3. Key Design Decisions

### Supply Conservation

Total SVRN7 in circulation at any moment equals exactly `FederationRecord.TotalSupplyGrana`
minus the Federation wallet balance. No synthetic grana are ever created. The Federation
wallet is the sole source of all SVRN7.

### Endowment Chain

```
Federation wallet
    → Society wallet     (at Society registration, EndowmentPerSocietyGrana)
        → Citizen wallet (at citizen registration, CitizenEndowmentGrana = 1,000 SVRN7)
```

All transfers are real UTXO transfers. Supply conservation holds at all times.

### UTXO Model

All balances are composed of UTXOs. A spent UTXO is immutable — never deleted —
giving a complete spend graph for independent audit.

### Idempotency

Cross-Society transfers use `TransferId = Blake3(canonical transfer JSON)` as a nonce.
A receiving Society that processes the same `TransferId` twice returns the cached receipt
without double-crediting.

### DIDComm-First

All transfers flow through DIDComm Authcrypt. Citizens send transfer requests as
encrypted DIDComm messages. Societies exchange `TransferOrderCredential` and
`TransferReceiptCredential` VCs via DIDComm. Overdraft draws use the
`endowment/1.0/overdraft-draw-request` protocol.

---

## 4. Monetary Model

### Units

| Unit | Value | Note |
|---|---|---|
| `grana` | 1 | Smallest unit. All arithmetic uses `long`. |
| `SVRN7` | 1,000,000 grana | Display denomination only |

### Epochs

| Epoch | Name | Permitted Transfers |
|---|---|---|
| 0 | Endowment | Citizen → own Society wallet or Federation wallet only |
| 1 | Ecosystem Utility | Any citizen → any citizen in any Society or the Federation |
| 2 | Market Issuance | Reserved |

Epoch advancement requires a Foundation-signed governance operation.

### Initial Supply

1,000,000,000 SVRN7 = 10¹⁵ grana. Configured at genesis.
Additional supply via `UpdateFederationSupplyAsync()` with Foundation signature.
Supply is monotonically increasing — reduction is architecturally impossible.

---

## 5. Identity Model

### Hierarchy

```
Federation (1)
    └─ Societies (N)  — each with 1..N DID method names
         └─ Citizens (M per Society)  — each with 1..N DIDs
```

### Primary DID

Every participant has exactly one primary DID. It is the wallet key and cannot be
deregistered or changed.

### Multi-DID Citizens

A citizen can hold additional DIDs under any method name that is currently **Active**
for their Society. Example — Society Alpha owns `socalpha` and `socalphahealth`:

- `did:socalpha:citizen123` — primary
- `did:socalphahealth:citizen123` — additional (health context)

`IIdentityRegistry.ResolveCitizenPrimaryDidAsync(anyDid)` resolves any DID back to
the citizen's primary DID. The transfer validator calls this in Step 0 (`NormaliseDids`)
before all other validation steps.

---

## 6. DID Method Names

### Lifecycle

```
Never existed
    │  RegisterAdditionalDidMethodAsync()  [self-service, uniqueness check only]
    ▼
  Active ──────────────── ← any Society re-registers after dormancy expires
    │  DeregisterDidMethodAsync()
    ▼
  Dormant  (DormantUntil = DeregisteredAt + DormancyPeriod)
    │  DormantUntil < UtcNow
    ▼
  Available  [time-based — no record cleanup required]
    │  RegisterMethodAsync() by any Society
    ▼
  Active  (new record; old record retained permanently for audit)
```

### Rules

- Must match `[a-z0-9]+` per W3C DID spec.
- Unique across the Federation while Active.
- Dormancy records are retained permanently — availability evaluated by time comparison.
- The primary method name (set at Society creation) **cannot** be deregistered.
- Issued DIDs under a deregistered method name remain fully resolvable — deregistration
  only prevents **new** DID issuance under that name.

### DID Method Exceptions

| Exception | Thrown When |
|---|---|
| `DuplicateDidMethodException` | Name currently Active under another Society |
| `DormantDidMethodException` | Name within its dormancy period |
| `DeregisteredDidMethodException` | Issuing new DID under deregistered method |
| `PrimaryDidMethodException` | Attempting to deregister primary method name |

---

## 7. Transfer Protocol

### 8-Step Validator

Both `TransferValidator` (Federation) and `SocietyTransferValidator` (Society) run
these steps in strict order. Failure at any step throws a typed exception.

| Step | Name | Description |
|---|---|---|
| 0 | NormaliseDids | Resolve any DID to canonical primary DID *(Society only)* |
| 1 | ValidateFields | Non-null, amount > 0, memo ≤ 256 chars |
| 2 | ValidateEpochRules | Epoch matrix enforcement |
| 3 | ValidateNonce | 24-hour replay window |
| 4 | ValidateFreshness | ±10 minute timestamp window |
| 5 | ValidateSanctions | ISanctionsChecker |
| 6 | ValidateSignature | secp256k1 CESR over canonical JSON |
| 7 | ValidateBalance | Dry-run UTXO sum (no spend yet) |
| 8 | ValidateSocietyMembership | Cross-Society Epoch 1 only: payee must be known citizen |

### Cross-Society Transfer Flow (Epoch 1)

```
Society A (payer's home)                     Society B (payee's home)
    │                                                │
    │  1. Validate payer (8 steps)                   │
    │  2. Debit payer UTXO                           │
    │  3. Issue TransferOrderCredential VC            │
    │  4. DIDComm Authcrypt ──────────────────────►  │
    │                                    5. Unpack   │
    │                                    6. Validate VC
    │                                    7. Credit payee UTXO
    │                                    8. Issue TransferReceiptCredential VC
    │  ◄──────────────────────────────── 9. DIDComm receipt
    │  10. Append settlement to Merkle log            │
```

Atomicity: fire-and-forget + nonce idempotency.
`TransferId = Blake3(canonical transfer JSON)`.

---

## 8. Overdraft Facility

When a Society wallet falls below `CitizenEndowmentGrana` during registration,
the library automatically requests an overdraft draw from the Federation.

### Draw Flow

```
RegisterCitizenInSocietyAsync()
    │
    ├─ Balance < CitizenEndowmentGrana?
    │      YES → check: TotalOverdrawnGrana + DrawAmountGrana > OverdraftCeilingGrana?
    │                  YES → throw SocietyEndowmentDepletedException
    │            DIDComm OverdraftDrawRequest → Federation
    │            (synchronous, timeout = OverdraftDrawTimeout)
    │            Federation transfers DrawAmountGrana → Society wallet
    │            Federation returns OverdraftDrawReceipt VC
    │            TotalOverdrawnGrana += DrawAmountGrana
    │            LifetimeDrawsGrana  += DrawAmountGrana  ← never resets
    │
    └─ Continue citizen registration
```

### Overdraft Status

| Status | Condition |
|---|---|
| `Clean` | `TotalOverdrawnGrana == 0` |
| `Overdrawn` | `0 < TotalOverdrawnGrana < OverdraftCeilingGrana` |
| `Ceiling` | `TotalOverdrawnGrana >= OverdraftCeilingGrana` — registration **blocked** |

### Federation Top-Up

```
TotalOverdrawnGrana = Max(0, TotalOverdrawnGrana - topUpAmount)
```

Overshoot goes to Society operating balance. `TotalOverdrawnGrana` floors at zero.

---

## 9. DIDComm v2 Integration

### Five Pack Modes

| Mode | Algorithm | Default Use |
|---|---|---|
| `Plaintext` | None | Testing |
| `Anoncrypt` | ECDH-ES+A256KW / AES-256-GCM | Sender anonymous |
| `Authcrypt` | ECDH-1PU+A256KW / AES-256-GCM | **All transfers** |
| `SignOnly` | EdDSA (Ed25519) JWS | Attestation without encryption |
| `SignThenEncrypt` | JWS inside Anoncrypt JWE | Maximum assurance |

### Cryptographic Details

- Ed25519 → X25519: birational map per RFC 7748 §4.1 with scalar clamping.
- Key wrap: RFC 3394 AES-256.
- Shared secret expansion: HKDF-SHA-256.
- Content encryption: AES-256-GCM with 12-byte random nonce, 16-byte tag.
- CEKs and ephemeral keys zeroed with `Array.Clear()` after use.

### Background Message Processor

`DIDCommMessageProcessorService` runs three loops on `BackgroundSweepInterval`:

1. VC expiry sweep (`ExpireStaleVcsAsync`)
2. Merkle tree head auto-sign (`SignMerkleTreeHeadAsync`)
3. DIDComm inbox dispatch (`IDIDCommTransferHandler`)

---


## 9a. TDA Host and LOBE Registry

The TDA (Trusted Digital Assistant) is a .NET 8 console application (Generic Host + Kestrel HTTP/2 + mTLS) that hosts the PowerShell Runspace Pool, DIDComm Message Switchboard, and all LOBE modules.

### Standard LOBE Inventory (v0.8.0)

| # | Module | Type | Protocol families | Description |
|---|---|---|---|---|
| 1 | `Svrn7.Common.psm1` | Eager | — | Shared helpers, DID URL parsing, logging |
| 2 | `Svrn7.Federation.psm1` | Eager | transfer/1.0/*, did/1.0/* | DID management, key pairs, base registry |
| 3 | `Svrn7.Society.psm1` | Eager | transfer/1.0/*, onboard/1.0/* | Monetary + identity operations |
| 4 | `Svrn7.UX.psm1` | Eager | ux/1.0/* | UX adapter — balance updates, notifications |
| 5 | `Svrn7.Email.psm1` | JIT | email/1.0/* | RFC 5322 email tunneled over DIDComm |
| 6 | `Svrn7.Calendar.psm1` | JIT | calendar/1.0/* | iCalendar events and meeting invites |
| 7 | `Svrn7.Presence.psm1` | JIT | presence/1.0/* | TDA availability status |
| 8 | `Svrn7.Notifications.psm1` | JIT | notification/1.0/* | Typed alert dispatch |
| 9 | `Svrn7.Onboarding.psm1` | JIT | onboard/1.0/* | Citizen registration pipeline |
|10 | `Svrn7.Invoicing.psm1` | JIT | invoice/1.0/* | Invoice-to-payment pipeline |
|11 | `Svrn7.Identity.psm1` | JIT | did/1.0/*, vc/1.0/* | DID Document + VC resolution |

Each LOBE ships a `.lobe.json` descriptor declaring its protocol URI registrations, MCP-aligned `inputSchema`/`outputSchema` on every cmdlet, and AI legibility metadata. The Switchboard uses these descriptors for dynamic routing — no hardcoded routing table.

### LOBE Loading

- **Eager**: imported into `InitialSessionState` at TDA startup. Available in every runspace with zero import cost.
- **JIT**: imported on first inbound message of a matching `@type` via `LobeManager.EnsureLoadedAsync()`. Subsequent calls are no-ops.

### Dynamic LOBE Registration

Third-party LOBEs can be hot-loaded without TDA restart: drop `{Name}.psm1`, `{Name}.psd1`, and `{Name}.lobe.json` into the TDA's LOBE directory. `FileSystemWatcher` detects the descriptor within milliseconds and registers the protocol URIs into the Switchboard.

## 10. Verifiable Credentials

### Credential Types Issued

| Type | Issuer | Subject | Validity |
|---|---|---|---|
| `Svrn7EndowmentCredential` | Society | Citizen | 5 years |
| `Svrn7SocietyRegistrationCredential` | Federation | Society | Indefinite |
| `Svrn7EpochCredential` | Federation | Federation | Per epoch |
| `TransferOrderCredential` | Originating Society | Payee | 24 hours |
| `TransferReceiptCredential` | Receiving Society | Payer | 24 hours |

### Lifecycle

```
Active
  │ SuspendVcAsync()       │ RevokeVcAsync()
  ▼                        ▼
Suspended                Revoked (permanent)
  │ ReinstateVcAsync()
  ▼
Active

Active  →  Expired  (auto-detected on read — no background sweep required)
```

### IVcDocumentResolver — Federation-Level Search

| Method | Description |
|---|---|
| `ResolveAsync(vcId)` | Core resolution with status metadata |
| `FindBySubjectAsync(did, status?)` | All VCs for a subject |
| `FindByIssuerAsync(did, status?)` | All VCs issued by a DID |
| `FindByTypeAsync(type, status?)` | All VCs of a given credential type |
| `FindBySocietyAsync(did, status?)` | All VCs associated with a Society |
| `FindBySubjectAcrossSocietiesAsync(did, timeout?)` | Cross-Society fan-out with partial-result manifest |
| `IsValidAsync(vcId)` | Lightweight single-call validity check |
| `GetStatusBatchAsync(vcIds)` | Batch status check |
| `FindExpiringAsync(window)` | VCs expiring within given window |
| `GetRevocationHistoryAsync(subject?, issuer?, since?)` | Filtered revocation history |
| `GetCountsByTypeAsync()` | Type distribution (for dashboards) |
| `GetCountsByStatusAsync()` | Status distribution (for dashboards) |

The cross-Society fan-out returns a `CrossSocietyVcQueryResult` containing
`RespondedSocieties` and `TimedOutSocieties` — partial results are always returned
rather than blocking on an unresponsive Society.

---

## 11. Merkle Audit Log

### Algorithm — RFC 6962

```
Leaf:     SHA-256(0x00 || data)
Internal: SHA-256(0x01 || left || right)
Odd node: propagates upward without duplication
```

### Entry Types

| EntryType | Trigger |
|---|---|
| `CitizenRegistration` | RegisterCitizenInSocietyAsync |
| `SocietyRegistration` | RegisterSocietyAsync |
| `FederationSupplyUpdate` | UpdateFederationSupplyAsync |
| `EpochTransition` | AdvanceEpochAuthorisedAsync |
| `TransferCompleted` | TransferAsync |
| `CrossSocietyTransferDebit` | TransferToExternalCitizenAsync (originating) |
| `CrossSocietyTransferCredit` | HandleTransferOrderAsync (receiving) |
| `CrossSocietyTransferSettled` | HandleTransferReceiptAsync |
| `DidMethodRegistration` | RegisterAdditionalDidMethodAsync |
| `DidMethodDeregistration` | DeregisterOwnDidMethodAsync |
| `VcRevocation` | RevokeVcAsync |
| `GdprErasure` | ErasePersonAsync |

### Tree Heads

`DIDCommMessageProcessorService` signs a `TreeHead` on every sweep. Tree heads
contain root hash, tree size, and secp256k1 CESR signature. Accessible via
`GetLatestTreeHeadAsync()`.

---

## 12. GDPR Compliance

`ErasePersonAsync(did, controllerSignature, requestTimestamp)`:

1. Validates controller signature.
2. Burns `EncryptedPrivateKeyBase64` to CSPRNG bytes — private key permanently lost.
3. Nullifies all PII fields on `CitizenRecord`.
4. Deactivates all DID Documents for the citizen.
5. Revokes all VCs where citizen is subject.
6. Appends `GdprErasure` to Merkle log (non-repudiable proof).
7. UTXO records retained — required for supply conservation audit.

---

## 13. Getting Started — Federation

### Install

```xml
<PackageReference Include="Svrn7.Federation" Version="0.8.0" />
```

### Register Services

```csharp
builder.Services.AddSvrn7(opts =>
{
    opts.FoundationPublicKeyHex  = Environment.GetEnvironmentVariable("SVRN7_FOUNDATION_KEY")!;
    opts.Svrn7DbPath  = "data/svrn7.db";
    opts.DidsDbPath   = "data/svrn7-dids.db";
    opts.VcsDbPath    = "data/svrn7-vcs.db";
    opts.DidMethodName           = "web7";
    opts.DidMethodDormancyPeriod = TimeSpan.FromDays(30);
});
```

### Initialise Federation (once at genesis)

```csharp
var driver  = app.Services.GetRequiredService<ISvrn7Driver>();
var keyPair = driver.GenerateSecp256k1KeyPair();
// Store keyPair.PrivateKeyBytes in HSM — never in config

await driver.InitialiseFederationAsync(new InitialiseFederationRequest
{
    Did                      = "did:web7:federation",
    PublicKeyHex             = keyPair.PublicKeyHex,
    FederationName           = "Web 7.0 Foundation",
    PrimaryDidMethodName     = "web7",
    TotalSupplyGrana         = Svrn7Constants.FederationInitialSupplyGrana,
    EndowmentPerSocietyGrana = 1_000_000 * Svrn7Constants.GranaPerSvrn7,
});
```

### Register a Society

```csharp
var societyKey = driver.GenerateSecp256k1KeyPair();
await driver.RegisterSocietyAsync(new RegisterSocietyRequest
{
    Did                   = "did:socalpha:my-society",
    PublicKeyHex          = societyKey.PublicKeyHex,
    PrivateKeyBytes       = societyKey.PrivateKeyBytes,
    SocietyName           = "Alpha Society",
    PrimaryDidMethodName  = "socalpha",
    DrawAmountGrana       = 100_000 * Svrn7Constants.GranaPerSvrn7,
    OverdraftCeilingGrana = 1_000_000 * Svrn7Constants.GranaPerSvrn7,
});
```

---

## 14. Getting Started — Society

### Install

```xml
<PackageReference Include="Svrn7.Society" Version="0.8.0" />
```

### Register Services

```csharp
builder.Services.AddSvrn7Society(opts =>
{
    opts.SocietyDid    = "did:socalpha:my-society";
    opts.FederationDid = "did:web7:federation";
    opts.DidMethodNames = new List<string> { "socalpha" };
    opts.DrawAmountGrana         = 100_000 * Svrn7Constants.GranaPerSvrn7;
    opts.OverdraftCeilingGrana   = 1_000_000 * Svrn7Constants.GranaPerSvrn7;
    opts.OverdraftDrawTimeout    = TimeSpan.FromSeconds(30);
    opts.SocietyMessagingPrivateKeyEd25519   = societyEd25519PrivKey;
    opts.FederationMessagingPublicKeyEd25519 = federationEd25519PubKey;
    opts.FederationEndpoint = "https://federation.svrn7.net/didcomm";
});
```

### Register a Citizen

```csharp
var driver    = app.Services.GetRequiredService<ISvrn7SocietyDriver>();
var citizenKey = driver.GenerateSecp256k1KeyPair();

await driver.RegisterCitizenInSocietyAsync(new RegisterCitizenInSocietyRequest
{
    Did             = "did:socalpha:citizen-alice",
    PublicKeyHex    = citizenKey.PublicKeyHex,
    PrivateKeyBytes = citizenKey.PrivateKeyBytes,
    SocietyDid      = "did:socalpha:my-society",
    // PreferredMethodName = null  → uses Society's primary method name
});
// Alice's wallet now contains CitizenEndowmentGrana = 1,000 SVRN7
```

### Register an Additional DID Method Name

```csharp
// Self-service — uniqueness is the only constraint
await driver.RegisterOwnAdditionalDidMethodAsync("socalphahealth");

// Issue Alice an additional DID under the new method
await driver.AddCitizenDidAsync(
    citizenPrimaryDid: "did:socalpha:citizen-alice",
    additionalDid:     "did:socalphahealth:citizen-alice",
    methodName:        "socalphahealth");
```

### Cross-Society Transfer (Epoch 1)

```csharp
await driver.TransferToExternalCitizenAsync(
    request:          transferRequest,          // payer is Alice in socalpha
    targetSocietyDid: "did:socbeta:their-society");
// Debit is immediate; credit async via DIDComm TransferOrderCredential
```

### Deregister a DID Method Name

```csharp
// Primary method cannot be deregistered — throws PrimaryDidMethodException
await driver.DeregisterOwnDidMethodAsync("socalphahealth");
// Method enters dormancy for DidMethodDormancyPeriod (default 30 days)
// Existing DIDs under "socalphahealth" remain valid and resolvable
// New DIDs under "socalphahealth" are blocked (DeregisteredDidMethodException)
```

---

## 15. Configuration Reference

### Svrn7Options

| Property | Default | Description |
|---|---|---|
| `FoundationPublicKeyHex` | *(required)* | Foundation governance secp256k1 public key |
| `Svrn7DbPath` | `data/svrn7.db` | Main LiteDB path |
| `DidsDbPath` | `data/svrn7-dids.db` | DID Document LiteDB path |
| `VcsDbPath` | `data/svrn7-vcs.db` | VC LiteDB path |
| `DidMethodName` | `drn` | Primary DID method name for this Federation |
| `DidMethodDormancyPeriod` | `30 days` | Duration deregistered names are dormant |
| `BackgroundSweepInterval` | `1 hour` | VC expiry + Merkle sign sweep interval |

### Svrn7SocietyOptions *(extends Svrn7Options)*

| Property | Default | Description |
|---|---|---|
| `SocietyDid` | *(required)* | This Society's own DID |
| `FederationDid` | *(required)* | Federation DID |
| `DidMethodNames` | *(required, ≥ 1)* | DID method names owned by this Society |
| `DrawAmountGrana` | 100,000 SVRN7 | Fixed overdraft draw amount per event |
| `OverdraftCeilingGrana` | 1,000,000 SVRN7 | Maximum accumulated overdraft |
| `OverdraftDrawTimeout` | `30 seconds` | Federation DIDComm round-trip timeout |
| `SocietyMessagingPrivateKeyEd25519` | *(required)* | Ed25519 private key for DIDComm |
| `FederationMessagingPublicKeyEd25519` | *(required)* | Federation Ed25519 public key |
| `FederationEndpoint` | *(required)* | Federation DIDComm service endpoint URL |

---

## 16. DIDComm Protocol URIs

All SVRN7 DIDComm `@type` URIs are **Locator DID URLs** — `did:drn:svrn7.net/protocols/...` — not `https://` URIs. This is architecturally coherent with the `did:drn` identity model. The SVRN7 ecosystem is intentionally self-contained; cross-ecosystem interoperability with non-SVRN7 DIDComm agents is not a goal.

**Core constants** (`Svrn7Constants.Protocols.*`):

| Constant | URI |
|---|---|
| `TransferRequest` | `did:drn:svrn7.net/protocols/transfer/1.0/request` |
| `TransferReceipt` | `did:drn:svrn7.net/protocols/transfer/1.0/receipt` |
| `TransferOrder` | `did:drn:svrn7.net/protocols/transfer/1.0/order` |
| `TransferOrderReceipt` | `did:drn:svrn7.net/protocols/transfer/1.0/order-receipt` |
| `OverdraftDrawRequest` | `did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-request` |
| `OverdraftDrawReceipt` | `did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-receipt` |
| `EndowmentTopUp` | `did:drn:svrn7.net/protocols/endowment/1.0/top-up` |
| `SupplyUpdate` | `did:drn:svrn7.net/protocols/supply/1.0/update` |
| `DidResolveRequest` | `did:drn:svrn7.net/protocols/did/1.0/resolve-request` |
| `DidResolveResponse` | `did:drn:svrn7.net/protocols/did/1.0/resolve-response` |
| `OnboardRequest` | `did:drn:svrn7.net/protocols/onboard/1.0/request` |
| `OnboardReceipt` | `did:drn:svrn7.net/protocols/onboard/1.0/receipt` |
| `InvoiceRequest` | `did:drn:svrn7.net/protocols/invoice/1.0/request` |
| `InvoiceReceipt` | `did:drn:svrn7.net/protocols/invoice/1.0/receipt` |

**LOBE protocol families** (declared in `.lobe.json` descriptors):

| Family | URI prefix | LOBE |
|---|---|---|
| Email | `did:drn:svrn7.net/protocols/email/1.0/` | `Svrn7.Email` |
| Calendar | `did:drn:svrn7.net/protocols/calendar/1.0/` | `Svrn7.Calendar` |
| Presence | `did:drn:svrn7.net/protocols/presence/1.0/` | `Svrn7.Presence` |
| Notification | `did:drn:svrn7.net/protocols/notification/1.0/` | `Svrn7.Notifications` |
| UX | `did:drn:svrn7.net/protocols/ux/1.0/` | `Svrn7.UX` |
| DID resolution | `did:drn:svrn7.net/protocols/did/1.0/` | `Svrn7.Identity` |
| VC resolution | `did:drn:svrn7.net/protocols/vc/1.0/` | `Svrn7.Identity` |

---

## 17. Exception Reference

| Exception | Thrown When |
|---|---|
| `InsufficientBalanceException` | UTXO sum insufficient for transfer |
| `EpochViolationException` | Transfer violates current epoch rules |
| `InvalidDidException` | DID malformed, unresolvable, or deactivated |
| `NonceReplayException` | Nonce reused within 24-hour window |
| `StaleTransferException` | Timestamp outside ±10 minute window |
| `SanctionedPartyException` | Payer or payee on sanctions list |
| `SignatureVerificationException` | secp256k1 or Ed25519 signature invalid |
| `NotFoundException` | Entity not found |
| `DoubleSpendException` | UTXO already spent |
| `InvalidCredentialException` | VC invalid, expired, or revoked |
| `ConfigurationException` | Options missing or invalid |
| `MerkleIntegrityException` | Merkle log integrity failure |
| `SocietyEndowmentDepletedException` | Overdraft ceiling reached — registration blocked |
| `FederationUnavailableException` | DIDComm round-trip to Federation timed out |
| `DuplicateDidMethodException` | Method name already Active under another Society |
| `DormantDidMethodException` | Method name within dormancy period |
| `DeregisteredDidMethodException` | Issuing DID under deregistered method |
| `PrimaryDidMethodException` | Attempting to deregister primary method |
| `UnresolvableDidException` | DID method has no registered resolver |

---

## 18. Testing

All tests use LiteDB `:memory:` — no disk I/O, no test isolation issues.

```bash
dotnet test                          # all tests
dotnet test tests/Svrn7.Tests/       # federation only
dotnet test tests/Svrn7.Society.Tests/  # society only
dotnet test --collect:"XPlat Code Coverage"
```

### Test Fixture Pattern

```csharp
public class MyTests : IAsyncLifetime
{
    private TestFixture _fx = null!;
    public Task InitializeAsync() { _fx = new TestFixture(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task ShouldRegisterCitizen()
    {
        var key = _fx.Crypto.GenerateSecp256k1KeyPair();
        var result = await _fx.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
        {
            Did = "did:drn:citizen-test",
            PublicKeyHex = key.PublicKeyHex,
            PrivateKeyBytes = key.PrivateKeyBytes,
        });
        result.Success.Should().BeTrue();
    }
}
```

---

## 19. Naming Conventions

| Term | Correct | Incorrect |
|---|---|---|
| Protocol domain | `svrn7.net` | `svrn7.io` |
| Resolution process | DID Document Resolution | DID Resolution |
| Resolver interface | `IDidDocumentResolver` | `IDidResolver` |
| Local resolver | `LocalDidDocumentResolver` | `LocalDidResolver` |
| Federation resolver | `FederationDidDocumentResolver` | `FederationDidResolver` |
| VC resolver | `IVcDocumentResolver` | `IVcResolver` |
| Smallest monetary unit | `grana` | `micro`, `satoshi` |
| Primary token | `SVRN7` | `SOVRONA` *(informal only)* |

---

## 20. NuGet Dependencies

| Package | Version | Used In |
|---|---|---|
| `LiteDB` | 5.0.21 | Svrn7.Store |
| `NBitcoin` | 7.0.37 | Svrn7.Crypto, Svrn7.DIDComm |
| `NSec.Cryptography` | 23.9.0 | Svrn7.Crypto, Svrn7.DIDComm |
| `Blake3` | 1.3.0 | Svrn7.Crypto |
| `Konscious.Security.Cryptography.Argon2` | 1.3.1 | Svrn7.Crypto |
| `Microsoft.Extensions.*` | 8.0.x | Svrn7.Federation, Svrn7.Society |
| `xunit` | 2.7.0 | Tests |
| `FluentAssertions` | 6.12.0 | Tests |

---

## 21. Roadmap

### v0.7.0 — DIDComm Production Hardening
- Persistent DIDComm inbox (LiteDB queue replaces in-process ConcurrentQueue)
- Live `FederationDidDocumentResolver` — real DIDComm round-trip to owning Society
- Live `FederationVcDocumentResolver` — real cross-Society fan-out
- OTel meters: `svrn7.federation.*` gauges
- Health checks: `ISvrn7HealthCheck`

### v0.8.0 — LOBE Registry + Architectural Coherence (April 2026)
- Dynamic LOBE loading via `.lobe.json` descriptors + `FileSystemWatcher` hot-reload
- Switchboard: hardcoded routing replaced with descriptor-driven dynamic dispatch
- DIDComm protocol URIs: `https://` → `did:drn:svrn7.net/protocols/...` (Locator DID URLs)
- PPML Legend 0.25 formally specified in parchment draft; "DEVICE" → "Device"
- `TdaResourceId` zero-dependency DID URL builder in `Svrn7.Core`
- `InboxMessage.Id` is now a TDA resource DID URL (not UUID)
- Schema Registry, dead-letter outbox, nine `.lobe.json` descriptors with MCP-aligned schemas
- New IETF draft: `draft-herman-drn-resource-addressing-00`
- Identity DID vs Locator DID URL formalised in `draft-herman-did-w3c-drn-00` Section 5a

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
- Nonce replay window in LiteDB TTL collection
- IETF Internet-Draft alignment: `draft-herman-did-drn`, `draft-herman-vtc-proof-sets`
- NuGet publication on nuget.org

---

*Web 7.0 Foundation — Bindloss, Alberta, Canada — https://svrn7.net*
