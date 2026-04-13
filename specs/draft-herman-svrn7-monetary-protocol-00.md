# SOVRONA (SVRN7) Monetary Transfer Protocol
# draft-herman-svrn7-monetary-protocol-00
# Author: M. Herman, Web 7.0 Foundation
# Published: April 2026

Internet-Draft: draft-herman-svrn7-monetary-protocol-00
Published:      April 2026
Expires:        October 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Web 7.0 Foundation Governance Council
Related:        draft-herman-web7-society-architecture-00
                draft-herman-didcomm-svrn7-transfer-00
                draft-herman-did-w3c-drn-00

---

## Abstract

This document specifies the SOVRONA (SVRN7) monetary transfer protocol — the Shared Reserve
Currency (SRC) for the Web 7.0 digital ecosystem of federated digital societies. The protocol
defines: the grana/SVRN7 denomination model; the canonical transfer request format and secp256k1
CESR signing procedure; an eight-step transfer validation pipeline with precise failure semantics;
a UTXO-based wallet accounting model; a three-epoch monetary governance lifecycle; and the supply
conservation invariant. The protocol is designed to be implementation-language-agnostic, enabling
conformant implementations in any programming environment that supports the required cryptographic
primitives.

---

## 1. Introduction

The SOVRONA Shared Reserve Currency (SVRN7) is the monetary foundation of the Web 7.0 digital
ecosystem, as described in [WEB70-ARCH]. Web 7.0 digital societies — digital nation states,
churches, guilds, associations, and any other form of organised community — conduct all economic
activity using a single, common reserve currency: SVRN7.

### 1.1 Motivation

Prior decentralised monetary systems either rely on a shared distributed ledger (requiring all
participants to agree on a global transaction order) or on a single trusted custodian. The SVRN7
protocol takes a third path: each Federation and each Society maintains its own independent
append-only Merkle audit log, and cross-Society transfers use DIDComm-signed credentials to
achieve eventual consistency without a shared ledger.

The protocol is grounded in three invariants:

1. **Conservation**: At any moment, the sum of all UTXO balances across all wallets in the
   ecosystem equals TotalSupplyGrana minus the unallocated Federation wallet balance.
2. **Monotonicity**: The total supply of SVRN7 can only increase; it can never decrease.
3. **Auditability**: Every monetary event is recorded in a tamper-evident Merkle log whose
   root is periodically signed by the Foundation governance key.

### 1.2 Scope

This document specifies:
1. The denomination model (grana, SVRN7, conversion).
2. The transfer request format and canonical signing procedure.
3. The eight-step transfer validation pipeline.
4. The UTXO wallet accounting model.
5. The three-epoch monetary governance lifecycle and epoch matrix.
6. The supply update operation.
7. The citizen endowment protocol.
8. GDPR Article 17 erasure semantics.

This document does not specify:
- DIDComm message packaging (see [DRAFT-DIDCOMM-TRANSFER]).
- The Federation/Society/Citizen architectural hierarchy (see [WEB70-ARCH]).
- DID method specifications (see [DRAFT-DID-DRN]).
- Verifiable Trust Circle specifications (see [DRAFT-VTC]).

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

---

## 3. Terminology

- **grana**: The atomic monetary unit of SVRN7. All protocol-level arithmetic MUST use grana.
  1 SVRN7 = 1,000,000 grana. Grana MUST be represented as a 64-bit signed integer (int64).

- **SVRN7**: The display denomination of SOVRONA. MUST NOT be used in protocol arithmetic.
  Display conversion: `SVRN7 = grana / 1,000,000`.

- **SRC (Shared Reserve Currency)**: The formal monetary category of SVRN7 within the Web 7.0
  ecosystem. SVRN7 is the sole SRC; digital Societies do not issue local currencies.

- **UTXO (Unspent Transaction Output)**: An atomic unit of wallet balance. A UTXO is created
  when grana arrives in a wallet and marked spent when grana leaves. UTXOs MUST never be
  physically deleted.

- **TransferId**: The Blake3 [BLAKE3] hash of the canonical transfer JSON (Section 5.2). Used as
  the global idempotency key for cross-Society transfers.

- **Federation**: The top-level governance entity. Holds the genesis wallet and governs epoch
  advancement and supply updates.

- **Society**: A digital community registered with the Federation. Holds its own wallet.
  Onboards citizens and manages their endowments.

- **Citizen**: An individual member of a Society. Holds a primary DID and a wallet seeded with
  the citizen endowment (Section 8).

- **Epoch**: A monetary policy phase governing which transfers are permitted between which parties.
  Epochs are forward-only and cannot be reversed.

- **Foundation Key**: The secp256k1 key pair whose public key is configured in the Federation
  deployment. Used to sign epoch advancement and supply update governance operations.

---

## 4. Denomination Model

### 4.1 Atomic Unit

The grana is the atomic monetary unit. All balances, transfer amounts, and arithmetic MUST use
grana. Implementations MUST represent grana as a 64-bit signed integer. Negative values are not
permitted in any balance or transfer amount field.

```
GranaPerSvrn7 = 1,000,000  (exactly)
```

### 4.2 Conversion

```
grana_to_svrn7(g) = g / 1,000,000  (decimal, display only)
svrn7_to_grana(s) = s * 1,000,000  (integer, exact)
```

Implementations MUST NOT perform monetary calculations in SVRN7. All intermediate results
MUST be computed in grana to avoid floating-point rounding errors.

### 4.3 Initial Supply

The Federation genesis wallet MUST be credited with:

```
InitialSupplyGrana = 1,000,000,000 * 1,000,000 = 10^15 grana
```

This corresponds to 1,000,000,000 SVRN7. The genesis credit is the only monetary creation event
that does not require a corresponding debit elsewhere in the system.

### 4.4 Supply Invariant

At any point in time, the following invariant MUST hold:

```
SUM(all UTXO balances where IsSpent = false) + FederationUnallocatedBalance = TotalSupplyGrana
```

Implementations MUST enforce this invariant at the protocol level. Supply update operations
(Section 9) are the only mechanism that changes `TotalSupplyGrana`.

---

## 5. Transfer Request

### 5.1 Fields

A transfer request MUST contain the following fields:

| Field | Type | Description |
|-------|------|-------------|
| PayerDid | string | W3C DID of the payer. MUST be resolvable and Active. |
| PayeeDid | string | W3C DID of the payee. MUST be resolvable and Active. |
| AmountGrana | int64 | Amount to transfer in grana. MUST be > 0. |
| Nonce | string | Unique string. MUST NOT have appeared in a prior valid request within the nonce replay window. |
| Timestamp | string | ISO 8601 UTC timestamp of request creation. |
| Signature | string | CESR-encoded secp256k1 signature over the canonical JSON (Section 5.2). |
| Memo | string? | Optional free-text memo. If present, MUST NOT exceed 256 characters. |

### 5.2 Canonical JSON for Signing

The signing input MUST be constructed as follows:

```json
{
  "PayerDid": "<payer-did>",
  "PayeeDid": "<payee-did>",
  "AmountGrana": <amount>,
  "Nonce": "<nonce>",
  "Timestamp": "<ISO-8601-UTC>",
  "Memo": <null-or-string>
}
```

Rules:
- Fields MUST appear in exactly this order.
- No whitespace between tokens (compact JSON serialisation).
- `Timestamp` MUST be serialised as ISO 8601 with full UTC offset (e.g., `2026-04-08T00:00:00.000+00:00`).
- `Memo` MUST be present even when null (serialised as JSON `null`).
- The encoding MUST be UTF-8.

The same canonical form MUST be used by both the signing client and the validating server. Any
deviation in field order, whitespace, or timestamp format will produce a signature verification
failure.

### 5.3 Signature Algorithm

The `Signature` field MUST be a CESR-encoded secp256k1 signature:

```
payload = UTF-8(canonical-json)
signature-bytes = secp256k1-sign(SHA-256(payload), payer-private-key)
Signature = "0B" + base64url-nopad(signature-bytes)
```

The CESR prefix `0B` [CESR] identifies the signature as a secp256k1 compact signature. The
base64url encoding MUST use the URL-safe alphabet without padding characters.

### 5.4 TransferId

```
TransferId = Blake3Hex(UTF-8(canonical-json))
```

where `Blake3Hex` produces the lowercase hexadecimal encoding of the 32-byte Blake3 hash.
`TransferId` is used as the global idempotency key for cross-Society transfers and as the basis
for UTXO identifiers created by the transfer.

---

## 6. Eight-Step Transfer Validation Pipeline

Validation MUST execute steps in strict ascending order. Failure at any step MUST immediately
terminate validation and return a typed error. The validator MUST NOT modify any state. State
changes occur only after all steps pass.

### Step 0 — Normalise DIDs

Resolve `PayerDid` and `PayeeDid` to their canonical primary DIDs using the identity registry.
If a DID resolves to an additional DID record, substitute the primary DID before proceeding.
This step ensures that a citizen holding DIDs under multiple method names is treated uniformly.

If a DID is not found in the identity registry, the original DID value is used unchanged (the
DID validity check occurs in Step 2).

### Step 1 — Validate Fields

MUST verify:
- `PayerDid` is non-null and non-empty.
- `PayeeDid` is non-null and non-empty.
- `PayerDid` ≠ `PayeeDid` (self-transfer MUST be rejected).
- `AmountGrana` > 0.
- `Memo` is null OR `len(Memo) ≤ 256`.
- `Nonce` is non-null and non-empty.
- `Timestamp` is parseable as ISO 8601.
- `Signature` is non-null and non-empty.

**Failure**: Return `InvalidFieldsError` with a description of the failing constraint.

### Step 2 — Validate Epoch Rules

Apply the epoch matrix for the current epoch value:

| Epoch | Permitted payees for a citizen payer |
|-------|-------------------------------------|
| 0 | This Society's own wallet DID OR the Federation wallet DID only |
| 1 | Any citizen DID, any Society wallet DID, or the Federation wallet DID |
| 2 | Any DID |

Implementations MUST resolve whether `PayeeDid` identifies a citizen, a Society, or the
Federation by querying the identity registry.

**Failure**: Return `EpochViolationError` with the current epoch and the violation type.

### Step 3 — Validate Nonce

The `Nonce` MUST NOT have appeared in a prior valid request within the nonce replay window
(default: 24 hours). Implementations MUST maintain a persistent or durable record of seen nonces
within the replay window. In-process nonce tracking is permissible for single-instance deployments;
distributed deployments MUST use a shared persistent store.

**Failure**: Return `NonceReplayError` with the nonce value.

### Step 4 — Validate Freshness

The `Timestamp` MUST be within ±10 minutes of the server's current UTC time at the moment of
validation. Implementations MUST use a monotonic or NTP-synchronised clock source.

**Failure**: Return `StaleTransferError` with the received timestamp and the server timestamp.

### Step 5 — Validate Sanctions

The implementation MUST invoke the configured `ISanctionsChecker` for both `PayerDid` and
`PayeeDid`. The default implementation (`PassthroughSanctionsChecker`) permits all participants.
Production deployments MUST replace this with a checker consulting a current sanctions list
appropriate to the deployment jurisdiction.

**Failure**: Return `SanctionedPartyError` with the sanctioned DID.

### Step 6 — Validate Signature

Reconstruct the canonical JSON (Section 5.2) from the request fields. Retrieve the payer's
secp256k1 public key from the identity registry using the normalised `PayerDid`. Verify the
CESR signature per Section 5.3.

**Failure**: Return `SignatureVerificationError`.

### Step 7 — Validate Balance

Compute the sum of unspent UTXOs for `PayerDid`. This sum MUST be ≥ `AmountGrana`. This step
is a dry-run: no UTXOs are marked spent, and no new UTXOs are created.

**Failure**: Return `InsufficientBalanceError` with `AvailableGrana` and `RequiredGrana`.

### Step 8 — Validate Society Membership (cross-Society Epoch 1 only)

This step applies only to cross-Society transfers in Epoch 1 and above. The `PayeeDid` MUST
identify a citizen who is a registered member of the target Society. If the `PayeeDid` identifies
a Society wallet or the Federation wallet, this step is skipped.

**Failure**: Return `SocietyMembershipError` with `PayeeDid` and `TargetSocietyDid`.

---

## 7. Transfer Execution

After all validation steps pass, the implementation MUST execute the following state changes
atomically. If any state change fails, all changes MUST be rolled back.

### 7.1 Same-Society Transfer

1. Select the minimum set of payer UTXOs whose sum ≥ `AmountGrana`.
2. Mark each selected UTXO as spent: `IsSpent = true`, `SpentAt = UtcNow`, `SpentByTxId = TransferId`.
3. Create a new UTXO for the payee: `OwnerDid = PayeeDid`, `AmountGrana = AmountGrana`,
   `Id = TransferId + ":payee"`.
4. If change is due: create a change UTXO for the payer: `OwnerDid = PayerDid`,
   `AmountGrana = sum(spent UTXOs) - AmountGrana`, `Id = TransferId + ":change"`.
5. Append a `Transfer` entry to the Merkle log.

### 7.2 Cross-Society Transfer

The originating Society MUST:
1. Execute steps 7.1.1–7.1.2 (debit the payer).
2. Issue a `TransferOrderCredential` VC (Section 7.3).
3. Dispatch the credential to the target Society via DIDComm SignThenEncrypt [DRAFT-DIDCOMM-TRANSFER].
4. Append a `CrossSocietyTransferDebit` entry to its Merkle log.

The receiving Society MUST:
1. Unpack and verify the incoming DIDComm message.
2. Check idempotency: if a VC with `VcId = TransferId` already exists, return the cached receipt
   and take no further action.
3. Credit the payee: create a new UTXO as in step 7.1.3.
4. Issue a `TransferReceiptCredential` VC (Section 7.4).
5. Append a `CrossSocietyTransferCredit` entry to its Merkle log.
6. Return the `TransferReceiptCredential` to the originating Society via DIDComm.

The originating Society, upon receiving the receipt, MUST append a `CrossSocietyTransferSettled`
entry to its Merkle log.

### 7.3 TransferOrderCredential

```json
{
  "type": ["VerifiableCredential", "TransferOrderCredential"],
  "issuer": "<originatingSocietyDid>",
  "credentialSubject": {
    "transferId":       "<TransferId>",
    "payerDid":         "<PayerDid>",
    "payeeDid":         "<PayeeDid>",
    "amountGrana":      <AmountGrana>,
    "originSocietyDid": "<originatingSocietyDid>",
    "targetSocietyDid": "<targetSocietyDid>",
    "epoch":            <currentEpoch>,
    "nonce":            "<Nonce>",
    "timestamp":        "<Timestamp>",
    "expiresAt":        "<Timestamp + 24h>"
  }
}
```

### 7.4 TransferReceiptCredential

```json
{
  "type": ["VerifiableCredential", "TransferReceiptCredential"],
  "issuer": "<receivingSocietyDid>",
  "credentialSubject": {
    "transferId":        "<TransferId>",
    "payeeDid":          "<PayeeDid>",
    "creditedGrana":     <AmountGrana>,
    "targetSocietyDid":  "<receivingSocietyDid>",
    "creditedAt":        "<UtcNow>"
  }
}
```

---

## 8. Citizen Endowment Protocol

Upon successful registration of a new citizen, the registering Society MUST:

1. Verify that its wallet balance ≥ `CitizenEndowmentGrana` (= 10^9 grana = 1,000 SVRN7).
   If not, invoke the overdraft facility before proceeding (see Section 10).
2. Execute a transfer of exactly `CitizenEndowmentGrana` from the Society wallet to the new
   citizen wallet. This transfer is NOT subject to the epoch matrix (it is a registration
   operation, not a citizen-initiated transfer) and does NOT require a citizen signature.
3. Issue a `Svrn7EndowmentCredential` VC to the citizen.
4. Append a `CitizenRegistration` entry to the Merkle log.

The endowment transfer is a real UTXO operation. No synthetic grana is created.

### 8.1 Svrn7EndowmentCredential

```json
{
  "type": ["VerifiableCredential", "Svrn7EndowmentCredential"],
  "issuer": "<societyDid>",
  "credentialSubject": {
    "citizenDid":       "<citizenDid>",
    "endowmentGrana":   1000000000,
    "endowmentSvrn7":   "1000.000000",
    "registeredAt":     "<UtcNow>",
    "epoch":            0
  }
}
```

---

## 9. Supply Update

Only the Federation MAY increase the total supply. A supply update request MUST be signed
with the Foundation governance key (secp256k1).

Supply update request format:

```json
{
  "newTotalSupplyGrana": <newTotal>,
  "reason":             "<human-readable governance reference>",
  "governanceRef":      "<URI to governance decision>",
  "requestedAt":        "<ISO-8601-UTC>",
  "signature":          "<CESR-secp256k1 over canonical JSON>"
}
```

The canonical JSON for signing MUST use the fields in the order shown above.

Implementations MUST enforce:
- `newTotalSupplyGrana` > current `TotalSupplyGrana` (supply MUST be monotonically increasing).
- The signature verifies against the configured Foundation public key.
- The `requestedAt` timestamp is within ±10 minutes of server time.

Upon successful verification, the Federation MUST:
1. Credit the Federation wallet with `newTotalSupplyGrana - currentTotalSupplyGrana` grana (a new UTXO).
2. Update `TotalSupplyGrana` to `newTotalSupplyGrana`.
3. Append a `SupplyUpdate` entry to the Merkle log.

---

## 10. Society Overdraft Facility

When a Society wallet balance falls below `CitizenEndowmentGrana`, the Society MAY draw an
increment of grana from the Federation via a synchronous DIDComm round-trip.

### 10.1 Draw Request

The Society MUST:
1. Verify that `TotalOverdrawnGrana + DrawAmountGrana ≤ OverdraftCeilingGrana`. If this
   condition would be violated, registration MUST be blocked and `SocietyEndowmentDepletedException`
   MUST be raised.
2. Send an `OverdraftDrawRequest` DIDComm message to the Federation.
3. Await an `OverdraftDrawReceipt` DIDComm response within `FederationRoundTripTimeout`
   (RECOMMENDED: 30 seconds). If the timeout expires, raise `FederationUnavailableException`.

### 10.2 Federation Response

The Federation MUST:
1. Verify the Society is registered and Active.
2. Transfer `DrawAmountGrana` from the Federation wallet to the Society wallet (real UTXO).
3. Return an `OverdraftDrawReceipt` DIDComm response.

### 10.3 Overdraft Accounting

After a successful draw, the Society MUST update:
- `TotalOverdrawnGrana += DrawAmountGrana` (resets to 0 on top-up, floor 0).
- `LifetimeDrawsGrana += DrawAmountGrana` (MUST never decrease; permanent audit counter).
- `DrawCount += 1`.
- `LastDrawAt = UtcNow`.
- `Status = Overdrawn` (or `Ceiling` if ceiling is now reached).

### 10.4 Top-Up

The Federation MAY send a top-up to reduce `TotalOverdrawnGrana`:
```
TotalOverdrawnGrana = MAX(0, TotalOverdrawnGrana - topUpAmountGrana)
```
`LifetimeDrawsGrana` MUST NOT be reduced by a top-up.

---

## 11. Epoch Governance

### 11.1 Epoch Values

| Value | Name | Description |
|-------|------|-------------|
| 0 | Endowment | Only citizen-to-own-Society and citizen-to-Federation transfers permitted. |
| 1 | Ecosystem Utility | Any citizen may transfer to any citizen, Society, or Federation. |
| 2 | Market Issuance | Any participant may transfer to any participant. |

### 11.2 Epoch Advancement

Epoch advancement MUST be authorised by a Foundation-signed governance operation. The advancement
request MUST be signed with the Foundation governance key (secp256k1).

Advancement request format:

```json
{
  "toEpoch":       <targetEpoch>,
  "governanceRef": "<URI to governance decision>",
  "reason":        "<human-readable rationale>",
  "notes":         "<optional additional context>",
  "requestedAt":   "<ISO-8601-UTC>",
  "signature":     "<CESR-secp256k1 over canonical JSON>"
}
```

Implementations MUST enforce:
- `toEpoch` > current epoch (backward epoch advancement MUST be rejected).
- The signature verifies against the configured Foundation public key.
- `requestedAt` is within ±10 minutes of server time.

Upon successful verification, the implementation MUST:
1. Update the current epoch value to `toEpoch`.
2. Append an `EpochTransition` entry to the Merkle log.

Epoch values are forward-only and MUST NOT be decremented under any circumstances.

---

## 12. UTXO Model

### 12.1 UTXO Structure

| Field | Type | Description |
|-------|------|-------------|
| Id | string | Unique identifier. Typically `TransferId + ":payee"` or `":change"`. |
| OwnerDid | string | Primary DID of the wallet owner. |
| AmountGrana | int64 | Balance in grana. MUST be > 0. |
| CreatedAt | datetime | UTC timestamp of UTXO creation. |
| IsSpent | bool | True if this UTXO has been consumed by a transfer. |
| SpentAt | datetime? | UTC timestamp when the UTXO was spent. Null if unspent. |
| SpentByTxId | string? | TransferId of the spending transfer. Null if unspent. |

### 12.2 Immutability

UTXOs MUST never be physically deleted. Once `IsSpent = true`, the UTXO record is immutable.
Implementations MUST reject any attempt to mark an already-spent UTXO as spent again
(`DoubleSpendError`).

### 12.3 Balance Computation

```
balance(did) = SUM(AmountGrana WHERE OwnerDid = did AND IsSpent = false)
```

---

## 13. GDPR Article 17 Erasure

A citizen may exercise their right to erasure under GDPR Article 17. The erasure request MUST
be authorised by a Foundation-signed erasure commitment.

Erasure commitment format:

```
ERASE:{citizenDid}:{requestTimestamp:ISO-8601-UTC}
```

The Foundation MUST sign the UTF-8 encoding of this string with its secp256k1 governance key.
The `requestTimestamp` MUST be within ±10 minutes of server time.

Upon successful verification, the implementation MUST:
1. Permanently deactivate the citizen's DID Document (`Status = Deactivated`).
2. Revoke all Active Verifiable Credentials issued to the citizen.
3. Overwrite the citizen's stored private key material with cryptographically random bytes,
   rendering it permanently unrecoverable.
4. Append a `GdprErasure` entry to the Merkle log.

The implementation MUST NOT:
- Delete UTXO records.
- Delete Merkle log entries.
- Delete DID Document records (deactivation is the permanent state; the record is retained).
- Delete VC records (revocation is the permanent state; the record is retained).

Retained records serve audit integrity. The erasure of the private key renders the identity
inoperable while preserving the historical transaction graph.

---

## 14. Error Reference

| Error | Step | Key fields |
|-------|------|------------|
| `InvalidFieldsError` | 1 | `FailingField`, `Constraint` |
| `EpochViolationError` | 2 | `CurrentEpoch`, `ViolationType` |
| `NonceReplayError` | 3 | `Nonce` |
| `StaleTransferError` | 4 | `ReceivedTimestamp`, `ServerTimestamp` |
| `SanctionedPartyError` | 5 | `SanctionedDid` |
| `SignatureVerificationError` | 6 | — |
| `InsufficientBalanceError` | 7 | `AvailableGrana`, `RequiredGrana` |
| `SocietyMembershipError` | 8 | `PayeeDid`, `TargetSocietyDid` |
| `DoubleSpendError` | exec | `UtxoId` |
| `SocietyEndowmentDepletedException` | overdraft | `SocietyDid`, `TotalOverdrawnGrana`, `CeilingGrana` |
| `FederationUnavailableException` | overdraft | `Operation`, `Timeout` |
| `EpochAdvancementError` | governance | `CurrentEpoch`, `RequestedEpoch` |

---

## 15. Security Considerations

### 15.1 Signature Freshness
The ±10 minute timestamp window (Step 4) mitigates replay attacks where an attacker captures
a valid signed transfer request and replays it after the nonce has expired from the replay cache.
Combined with the nonce replay window (Step 3), this provides two independent replay defences.

### 15.2 Nonce Uniqueness in Distributed Deployments
Single-instance deployments may use an in-process nonce cache. Multi-instance deployments MUST
use a shared persistent store for nonce tracking. Failure to do so in a distributed deployment
allows nonce replay across instances.

### 15.3 Foundation Key Protection
The Foundation secp256k1 private key authorises epoch advancement, supply updates, and GDPR
erasure. It MUST be stored in a Hardware Security Module (HSM) or equivalent offline cold storage.
It MUST NOT be present in application configuration files, environment variables, or source control.

### 15.4 UTXO Double-Spend Prevention
The validator (Step 7) is a dry-run; it reads but does not modify state. Transfer execution
(Section 7.1) marks UTXOs as spent. Implementations MUST enforce the constraint that a UTXO
cannot be spent twice (`DoubleSpendError`). Implementations MUST use database-level atomicity
(transactions) to prevent concurrent transfers from spending the same UTXO.

### 15.5 Cross-Society Idempotency
Cross-Society transfers are fire-and-forget. The receiving Society MUST implement idempotency
using `TransferId` as the deduplication key. Without this, a retried DIDComm delivery would
credit the payee twice.

---

## 16. Privacy Considerations

### 16.1 Transaction Graph Visibility
All UTXO records are retained permanently. An operator of a Society or Federation node has
visibility into all transactions within their deployment. Cross-Society transfer records are
visible to both the originating and receiving Society. Implementations MUST control access
to UTXO data in accordance with applicable data protection regulations.

### 16.2 GDPR Erasure Limits
GDPR Article 17 erasure (Section 13) removes the citizen's ability to sign new transactions
(key zeroed) and deactivates their identity (DID deactivated). However, historical UTXO records
and Merkle log entries referencing the citizen's DID are retained for audit integrity. Deployers
MUST inform citizens of this limitation at registration time.

---

## 17. IANA Considerations

This document has no IANA actions.

---

## 18. References

### Normative
- [RFC2119] Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174] Leiba, B. Ambiguity of Uppercase vs Lowercase in RFC 2119 Key Words. May 2017.
- [RFC6962] Laurie, B. et al. Certificate Transparency. RFC 6962, June 2013.
- [W3C.DID-CORE] Sporny, M. et al. Decentralized Identifiers (DIDs) v1.0. W3C Recommendation, 2022.
- [W3C.VC-DATA-MODEL] Sporny, M. et al. VC Data Model v2.0. W3C Recommendation, 2024.
- [CESR] Smith, S. Composable Event Streaming Representation. draft-ssmith-cesr. IETF.
- [BLAKE3] O'Connor, J. et al. BLAKE3 cryptographic hash function. https://github.com/BLAKE3-team/BLAKE3-specs.
- [SECP256K1] Standards for Efficient Cryptography Group. SEC 2: Recommended Elliptic Curve Domain Parameters. 2010.

### Informative
- [WEB70-ARCH] Herman, M. Web 7.0 Society Architecture. draft-herman-web7-society-architecture-00.
- [DRAFT-DID-DRN] Herman, M. Decentralized Resource Name (DRN) DID Method. draft-herman-did-w3c-drn-00.
- [DRAFT-VTC] Herman, M. Verifiable Trust Circles using VC Proof Sets. draft-herman-vtc-proof-sets-01.
- [DRAFT-DIDCOMM-TRANSFER] Herman, M. DIDComm Transfer Protocol for SVRN7. draft-herman-didcomm-svrn7-transfer-00.
- [WEB70-IMPL] Herman, M. SOVRONA (SVRN7) .NET 8 Reference Implementation. https://github.com/web7foundation/svrn7.
- [GDPR-17] European Parliament. General Data Protection Regulation, Article 17. 2016.

---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
URI: https://hyperonomy.com/about/
