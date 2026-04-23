# Svrn7.Federation

**SOVRONA (SVRN7) Federation-level driver** — the Shared Reserve Currency (SRC) for the Web 7.0 digital ecosystem.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

> **Disambiguation**: SVRN7 / SOVRONA is **not** Sovryn (Bitcoin DeFi), **not** SOVRIN (Hyperledger Indy), and **not** Solana. It is a standalone .NET 8 embedded library — not a blockchain, not a token.

---

## What This Package Provides

`Svrn7.Federation` is the top-level governance driver for a Web 7.0 Federation deployment. It manages:

- **Citizen and Society registration** with secp256k1 keypairs and W3C DID Documents
- **UTXO-based wallet transfers** with an 8-step validation pipeline (nonce, timestamp, sanctions, signature, balance)
- **W3C Verifiable Credential v2** issuance, verification, and revocation
- **RFC 6962 Merkle audit log** for tamper-evident transaction history
- **DID method name governance** (registration, deregistration, dormancy)
- **GDPR erasure** (`ErasePersonAsync`)
- **Three-epoch monetary lifecycle** (Endowment → Ecosystem Utility → Market Issuance)

For Society-level operations (citizen-in-society registration, DIDComm transfers, overdraft management), use [`Svrn7.Society`](https://www.nuget.org/packages/Svrn7.Society) instead — it includes this package.

---

## Installation

```xml
<PackageReference Include="Svrn7.Federation" Version="0.8.0" />
```

---

## Quick Start

### DI Registration

```csharp
builder.Services.AddSvrn7Federation(opts =>
{
    opts.FoundationPublicKeyHex = Environment.GetEnvironmentVariable("SVRN7_FOUNDATION_KEY")!;
    opts.Svrn7DbPath  = "data/svrn7.db";
    opts.DidsDbPath   = "data/svrn7-dids.db";
    opts.VcsDbPath    = "data/svrn7-vcs.db";
    opts.DidMethodName = "drn";
});
```

### Genesis (run once per deployment)

```csharp
var driver  = app.Services.GetRequiredService<ISvrn7Driver>();
var keyPair = driver.GenerateSecp256k1KeyPair();
// Store keyPair.PrivateKeyBytes in HSM — never in config

await driver.InitialiseFederationAsync(new InitialiseFederationRequest
{
    Did                      = "did:drn:foundation",
    PublicKeyHex             = keyPair.PublicKeyHex,
    FederationName           = "Web 7.0 Foundation",
    PrimaryDidMethodName     = "drn",
    TotalSupplyGrana         = Svrn7Constants.FederationInitialSupplyGrana,
    EndowmentPerSocietyGrana = 1_000_000 * Svrn7Constants.GranaPerSvrn7,
});
```

### Register a Society

```csharp
var socKey = driver.GenerateSecp256k1KeyPair();

await driver.RegisterSocietyAsync(new RegisterSocietyRequest
{
    Did                  = "did:drn:sovronia",
    PublicKeyHex         = socKey.PublicKeyHex,
    PrivateKeyBytes      = socKey.PrivateKeyBytes,
    SocietyName          = "Sovronia Digital Nation",
    PrimaryDidMethodName = "sovronia",
});
```

### Transfer

```csharp
await driver.TransferAsync(new TransferRequest
{
    PayerDid     = "did:drn:sovronia.svrn7.net/citizen/alice",
    PayeeDid     = "did:drn:sovronia.svrn7.net",
    AmountGrana  = 500_000,   // 0.5 SVRN7
    Nonce        = Guid.NewGuid().ToString(),
    Timestamp    = DateTimeOffset.UtcNow,
    Signature    = /* secp256k1 CESR over canonical JSON */,
});
```

---

## Key Interfaces

### `ISvrn7Driver` (44 members)

```csharp
// Identity
Task<OperationResult>  RegisterCitizenAsync(RegisterCitizenRequest)
Task<OperationResult>  RegisterSocietyAsync(RegisterSocietyRequest)
Task<CitizenRecord?>   GetCitizenAsync(string did)
Task<DidResolutionResult> ResolveDidAsync(string did)

// Value
Task<OperationResult>  TransferAsync(TransferRequest)
Task<long>             GetBalanceGranaAsync(string did)
Task<decimal>          GetBalanceSvrn7Async(string did)  // 1 SVRN7 = 1,000,000 grana

// Credentials
Task<VcRecord>         IssueVcAsync(VcRequest)
Task<IReadOnlyList<VcRecord>> GetVcsBySubjectAsync(string did)

// Audit
Task<string>           GetMerkleRootAsync()
Task<TreeHead>         SignMerkleTreeHeadAsync()

// Cryptography
Svrn7KeyPair           GenerateSecp256k1KeyPair()
Svrn7KeyPair           GenerateEd25519KeyPair()
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
Svrn7Constants.MaxMemoLength          // 256 chars
```

---

## Epoch Matrix

| Epoch | Name              | Permitted payer → payee                        |
|-------|-------------------|------------------------------------------------|
| 0     | Endowment         | Citizen → own Society or Federation only       |
| 1     | Ecosystem Utility | Citizen → any citizen, Society, or Federation  |
| 2     | Market Issuance   | Any participant → any participant              |

Epoch advancement requires a Foundation-signed governance operation. Epochs are forward-only.

---

## 8-Step Transfer Validation Pipeline

All transfers pass through `TransferValidator` in strict order:

| Step | Name                    | What it checks                          |
|------|-------------------------|-----------------------------------------|
| 0    | NormaliseDids           | Resolve to canonical primary DID        |
| 1    | ValidateFields          | Non-null, amount > 0, memo ≤ 256 chars  |
| 2    | ValidateEpochRules      | Epoch matrix compliance                 |
| 3    | ValidateNonce           | 24-hour replay window (LiteDB TTL)      |
| 4    | ValidateFreshness       | ±10 minute timestamp window             |
| 5    | ValidateSanctions       | `ISanctionsChecker` (pluggable)         |
| 6    | ValidateSignature       | secp256k1 CESR over canonical JSON      |
| 7    | ValidateBalance         | Dry-run UTXO sum (no state modified)    |

---

## Key Exceptions

| Exception                        | Thrown when                                      |
|----------------------------------|--------------------------------------------------|
| `InsufficientBalanceException`   | UTXO sum insufficient                            |
| `EpochViolationException`        | Transfer violates current epoch rules            |
| `NonceReplayException`           | Nonce reused within 24-hour window               |
| `StaleTransferException`         | Timestamp outside ±10 minute window              |
| `SignatureVerificationException` | secp256k1 signature invalid                      |
| `SanctionedPartyException`       | Payer or payee on sanctions list                 |
| `InvalidDidException`            | DID malformed or unresolvable                    |
| `DoubleSpendException`           | UTXO already spent                               |

---

## Persistence

Three LiteDB 5 embedded databases per deployment:

| Database        | Default path          | Contents                             |
|-----------------|-----------------------|--------------------------------------|
| `svrn7.db`      | `data/svrn7.db`       | Wallets, UTXOs, citizens, societies, Merkle log |
| `svrn7-dids.db` | `data/svrn7-dids.db`  | DID Documents, verification method index |
| `svrn7-vcs.db`  | `data/svrn7-vcs.db`   | Verifiable Credentials               |

---

## PowerShell Test Utilities

`Remove-Svrn7Databases` is exported from `Svrn7.Federation.psm1` and removes all LiteDB database files for a deployment. It is intended for test teardown — stop the TDA host before calling it.

```powershell
# Interactive — PowerShell prompts for confirmation (ConfirmImpact = High)
Remove-Svrn7Databases

# Non-interactive (CI / test scripts)
Remove-Svrn7Databases -Confirm:$false

# Preview without deleting
Remove-Svrn7Databases -WhatIf

# Custom paths (e.g. isolated test data directory)
Remove-Svrn7Databases `
    -Svrn7DbPath    tests/data/svrn7.db `
    -DidsDbPath     tests/data/svrn7-dids.db `
    -VcsDbPath      tests/data/svrn7-vcs.db `
    -InboxDbPath    tests/data/svrn7-inbox.db `
    -SchemasDbPath  tests/data/svrn7-schemas.db `
    -Confirm:$false
```

Defaults match the `Svrn7Options` / `SocietyOptions` path defaults (`data/*.db`). Each database also has a companion LiteDB journal file (`{path}-log`) which is removed automatically when present.

---

## Links

- **Project**: https://svrn7.net
- **GitHub**: https://github.com/web7foundation/svrn7
- **Disambiguation**: https://svrn7.net/docs/disambiguation
- **Society package**: https://www.nuget.org/packages/Svrn7.Society
- **IETF did:drn**: https://datatracker.ietf.org/doc/draft-herman-did-drn/

Copyright © 2026 Michael Herman, Web 7.0 Foundation (Bindloss, Alberta, Canada). MIT License.
