# Svrn7.Society

**SOVRONA (SVRN7) Society-level driver** — the Shared Reserve Currency (SRC) for the Web 7.0 digital ecosystem.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

> **Disambiguation**: SVRN7 / SOVRONA is **not** Sovryn (Bitcoin DeFi), **not** SOVRIN (Hyperledger Indy), and **not** Solana. It is a standalone .NET 8 embedded library — not a blockchain, not a token.

---

## What This Package Provides

`Svrn7.Society` extends `Svrn7.Federation` with Society-scoped operations:

- **Society-scoped citizen registration** — registers citizens under the Society's DID method, issues 1,000 grana (0.001 SVRN7) endowment, records membership
- **DID method governance** — self-service registration and deregistration of additional DID method names
- **DIDComm SignThenEncrypt transfers** — cross-Society citizen-to-citizen transfers via `TransferOrderCredential`
- **Overdraft management** — revolving credit facility from the Federation when the Society wallet is low; delivered via DIDComm HTTP transport when `FederationEndpointUrl` is configured
- **Inbox/outbox** — `LiteInboxStore` backed by `svrn7-inbox.db`; `DIDCommMessageProcessorService` background service. `InboxMessage` stores `FromDid` (sender DID) and `WireId` (DIDComm wire `id`; null for encrypted messages), both threaded from `KestrelListenerService` via `IInboxStore.EnqueueAsync`.
- **Schema Registry** — JSON Schema 2020-12 registry for credential schemas (Society TDA only)
- **Cross-Society VC queries** — `FindVcsBySubjectAcrossSocietiesAsync` via DIDComm

This package is self-contained. Do **not** also call `AddSvrn7Federation()` — `AddSvrn7Society()` registers the full stack internally.

---

## Installation

```xml
<PackageReference Include="Svrn7.Society" Version="0.8.0" />
```

---

## Quick Start

### DI Registration

```csharp
builder.Services.AddSvrn7Society(opts =>
{
    opts.SocietyDid    = "did:drn:sovronia";     // did:drn:{methodName}
    opts.FederationDid = "did:drn:federation";
    opts.DidMethodName = "sovronia";
    opts.DidMethodNames = new List<string> { "sovronia" };

    opts.SocietyMessagingPrivateKeyEd25519   = societyEd25519PrivKey;
    opts.FederationMessagingPublicKeyEd25519 = federationEd25519PubKey;

    // Optional: enable DIDComm overdraft draw delivery to the Federation TDA
    opts.FederationEndpointUrl = "http://federation.example.net:8443/didcomm";

    opts.DrawAmountGrana       = 1_000_000_000_000L;  // 1,000 SVRN7 per draw
    opts.OverdraftCeilingGrana = 10_000_000_000_000L; // 10,000 SVRN7 ceiling

    opts.Svrn7DbPath  = "data/svrn7.db";
    opts.DidsDbPath   = "data/svrn7-dids.db";
    opts.VcsDbPath    = "data/svrn7-vcs.db";
    opts.InboxDbPath  = "data/svrn7-inbox.db";
});

// Optional: enable inbox background processor
builder.Services.AddSvrn7SocietyBackgroundServices();

// Optional: enable DIDComm HTTP transport for overdraft draws
builder.Services.AddHttpClient();
```

### Register a Citizen

```csharp
var driver     = app.Services.GetRequiredService<ISvrn7SocietyDriver>();
var citizenKey = driver.GenerateSecp256k1KeyPair();

await driver.RegisterCitizenInSocietyAsync(new RegisterCitizenInSocietyRequest
{
    Did             = "did:drn:sovronia.svrn7.net/citizen/alice",
    PublicKeyHex    = citizenKey.PublicKeyHex,
    PrivateKeyBytes = citizenKey.PrivateKeyBytes,
    SocietyDid      = "did:drn:sovronia.svrn7.net",
});
// Alice's wallet now contains 1,000 grana (0.001 SVRN7)
```

### Transfer Between Citizens

```csharp
await driver.TransferAsync(new TransferRequest
{
    PayerDid    = "did:drn:sovronia.svrn7.net/citizen/alice",
    PayeeDid    = "did:drn:sovronia.svrn7.net/citizen/bob",
    AmountGrana = 500,
    Nonce       = Guid.NewGuid().ToString(),
    Timestamp   = DateTimeOffset.UtcNow,
    Signature   = /* secp256k1 CESR over canonical JSON */,
});
```

### Check Overdraft Status

```csharp
var status = await driver.GetOverdraftStatusAsync();
// OverdraftStatus.Clean | Overdrawn | Ceiling
```

---

## Key Interfaces

### `ISvrn7SocietyDriver : ISvrn7Driver`

Society-specific additions over the 44-member `ISvrn7Driver`:

```csharp
// Citizen lifecycle
Task<OperationResult> RegisterCitizenInSocietyAsync(RegisterCitizenInSocietyRequest)
Task<bool>            IsMemberAsync(string citizenDid)
Task<IReadOnlyList<string>> GetMemberCitizenDidsAsync()
Task<OperationResult> AddCitizenDidAsync(string citizenPrimaryDid, string methodName)

// Cross-Society transfers
Task<OperationResult> TransferToExternalCitizenAsync(TransferRequest, string targetSocietyDid)
Task<OperationResult> TransferToFederationAsync(string payerDid, long amountGrana, ...)
Task<string>          HandleIncomingTransferMessageAsync(string packedDIDCommMessage)

// DID method governance
Task<OperationResult> RegisterSocietyDidMethodAsync(string methodName)
Task<OperationResult> DeregisterSocietyDidMethodAsync(string methodName)
Task<IReadOnlyList<SocietyDidMethodRecord>> GetSocietyDidMethodsAsync()

// Overdraft
Task<OverdraftStatus>         GetOverdraftStatusAsync()
Task<SocietyOverdraftRecord?> GetOverdraftRecordAsync()

// Cross-Society VC queries
Task<CrossSocietyVcQueryResult> FindVcsBySubjectAcrossSocietiesAsync(string subjectDid, ...)

// Society record
Task<SocietyRecord?> GetOwnSocietyAsync()
```

---

## DID Format

| Entity     | DID type        | DID format                                       | Example                                          |
|------------|-----------------|--------------------------------------------------|--------------------------------------------------|
| Federation | Identity DID    | `did:drn:{fedHost}`                              | `did:drn:svrn7.net`                              |
| Society    | Identity DID    | `did:drn:{societyHost}`                          | `did:drn:sovronia.svrn7.net`                     |
| Citizen    | Locator DID URL | `did:drn:{societyHost}/citizen/{localId}`   | `did:drn:sovronia.svrn7.net/citizen/alice`  |

DID method names must match `[a-z0-9]+` — no hyphens, no uppercase.

---

## Overdraft Facility

When the Society wallet balance falls below `CitizenEndowmentGrana` during citizen registration:

1. An `OverdraftDrawRequest` DIDComm message is built and the overdraft record is updated locally.
2. If `FederationEndpointUrl` is configured **and** `services.AddHttpClient()` has been called, the message is packed (Ed25519 SignThenEncrypt) and delivered to the Federation TDA via HTTP POST.
3. The Federation sends back an `OverdraftDrawReceipt` via the Society's inbound `/didcomm` route asynchronously.
4. If the `OverdraftCeilingGrana` is reached, `SocietyEndowmentDepletedException` is thrown and registration is blocked.

```csharp
// Overdraft statuses
OverdraftStatus.Clean     // No draws yet
OverdraftStatus.Overdrawn // Draws outstanding, below ceiling
OverdraftStatus.Ceiling   // Ceiling reached — registration blocked
```

---

## Monetary Units

| Unit    | Value          | Storage type |
|---------|----------------|--------------|
| `grana` | 1 (atomic)     | `long`       |
| `SVRN7` | 1,000,000 grana| `decimal`    |

```csharp
Svrn7Constants.GranaPerSvrn7          // 1_000_000L
Svrn7Constants.CitizenEndowmentGrana  // 1_000L (0.001 SVRN7 per citizen)
```

---

## Epoch Matrix

| Epoch | Name              | Society transfer rules                            |
|-------|-------------------|---------------------------------------------------|
| 0     | Endowment         | Citizen → own Society or Federation only          |
| 1     | Ecosystem Utility | Citizen → any citizen, Society, or Federation     |
| 2     | Market Issuance   | Open-market rules                                 |

---

## Key Exceptions

| Exception                          | Thrown when                                      |
|------------------------------------|--------------------------------------------------|
| `SocietyEndowmentDepletedException`| Overdraft ceiling reached                        |
| `EpochViolationException`          | Transfer violates epoch rules                    |
| `NonceReplayException`             | Nonce reused within 24-hour window               |
| `StaleTransferException`           | Timestamp outside ±10 minute window              |
| `SignatureVerificationException`   | Signature invalid                                |
| `InsufficientBalanceException`     | UTXO sum insufficient                            |
| `DeregisteredDidMethodException`   | Citizen DID issued under deregistered method     |
| `PrimaryDidMethodException`        | Attempt to deregister primary DID method         |

---

## Persistence

Four LiteDB 5 embedded databases per Society deployment:

| Database           | Default path               | Contents                               |
|--------------------|----------------------------|----------------------------------------|
| `svrn7.db`         | `data/svrn7.db`            | Wallets, UTXOs, citizens, membership, overdraft, Merkle log |
| `svrn7-dids.db`    | `data/svrn7-dids.db`       | DID Documents, verification method index |
| `svrn7-vcs.db`     | `data/svrn7-vcs.db`        | Verifiable Credentials                 |
| `svrn7-inbox.db`   | `data/svrn7-inbox.db`      | DIDComm inbox, outbox, processed orders, schemas |

---

## Links

- **Project**: https://svrn7.net
- **GitHub**: https://github.com/web7foundation/svrn7
- **Disambiguation**: https://svrn7.net/docs/disambiguation
- **Federation package**: https://www.nuget.org/packages/Svrn7.Federation
- **IETF did:drn**: https://datatracker.ietf.org/doc/draft-herman-did-drn/

Copyright © 2026 Michael Herman, Web 7.0 Foundation (Bindloss, Alberta, Canada). MIT License.
