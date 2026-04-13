# SOVRONA (SVRN7) Society Overdraft Facility
# draft-herman-svrn7-overdraft-protocol-00
# Author: M. Herman, Web 7.0 Foundation
# Published: April 2026

Internet-Draft: draft-herman-svrn7-overdraft-protocol-00
Published:      April 2026
Expires:        October 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Web 7.0 Foundation Governance Council
Related:        draft-herman-svrn7-monetary-protocol-00
                draft-herman-web7-society-architecture-00
                draft-herman-didcomm-svrn7-transfer-00

---

## Abstract

This document specifies the SOVRONA (SVRN7) Society Overdraft Facility — a revolving credit
mechanism that allows a Federation to extend incremental grana advances to registered Societies
when their endowment wallets are exhausted. The facility is analogous to a central bank overdraft
window: it ensures that a Society's inability to fund citizen endowments does not block
registration operations, while maintaining a ceiling on total outstanding credit and a permanent
audit trail of all draw events. This document specifies the overdraft accounting model, the
DIDComm round-trip protocol for draw requests, the ceiling enforcement mechanism, the top-up
protocol, and the audit invariants that MUST hold throughout the facility's operation.

---

## 1. Introduction

In the Web 7.0 digital society ecosystem [WEB70-ARCH], a Society onboards Citizens by
transferring 1,000 SVRN7 (10^9 grana) from its own wallet to each new Citizen's wallet. A
Society that onboards many Citizens will eventually exhaust its wallet balance. Without a
replenishment mechanism, this would cause all subsequent citizen registrations to fail until
the Federation manually topped up the Society's wallet — an operational bottleneck incompatible
with automated onboarding at scale.

The Society Overdraft Facility provides a structured solution. When a Society's wallet balance
falls below the citizen endowment threshold, it may draw an increment of grana from the
Federation via a synchronous DIDComm round-trip. The draw is bounded by a configurable ceiling
to limit the Federation's exposure, and a permanent audit counter (`LifetimeDrawsGrana`) ensures
the complete draw history is always recoverable.

### 1.1 Analogy to Central Bank Overdraft

The facility is structurally analogous to a central bank overdraft window or intraday credit
facility, which is a standard tool in traditional payment systems:

- The **Federation** plays the role of the central bank (lender of last resort).
- The **Society** plays the role of a commercial bank (borrower).
- The **DrawAmountGrana** is the increment (analogous to a lombard loan tranche).
- The **OverdraftCeilingGrana** is the credit limit.
- The **LifetimeDrawsGrana** counter is the permanent ledger entry.
- **Top-up** is the repayment mechanism.

Unlike a commercial bank overdraft, interest is not charged — the facility is a governance
mechanism, not a revenue source.

### 1.2 Scope

This document specifies:
1. The overdraft accounting data model.
2. The trigger condition for initiating a draw.
3. The ceiling enforcement mechanism and ceiling breach handling.
4. The DIDComm draw request and receipt protocol.
5. The timeout and failure handling.
6. The top-up protocol.
7. The audit invariants.
8. Monitoring and operational guidance.

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

---

## 3. Terminology

- **DrawAmountGrana**: The fixed increment of grana drawn per overdraft event. Configurable
  per Society. Default: 1,000,000,000,000 grana (1,000 SVRN7).

- **OverdraftCeilingGrana**: The maximum cumulative outstanding grana (TotalOverdrawnGrana)
  permitted for a Society. Configurable per Society. Default: 10,000,000,000,000 grana
  (10,000 SVRN7).

- **TotalOverdrawnGrana**: The current outstanding drawn grana for a Society. Increases by
  DrawAmountGrana on each draw. Decreases (floor: 0) on top-up. This value is reset-able.

- **LifetimeDrawsGrana**: The cumulative total of all draws ever made by a Society. Increases
  by DrawAmountGrana on each draw. MUST NEVER decrease. This value is a permanent audit counter.

- **DrawCount**: The total number of draw events ever made by a Society. Monotonically increasing.

- **OverdraftStatus**: The current state of a Society's overdraft account:
  - `Clean` — TotalOverdrawnGrana = 0.
  - `Overdrawn` — 0 < TotalOverdrawnGrana < OverdraftCeilingGrana.
  - `Ceiling` — TotalOverdrawnGrana ≥ OverdraftCeilingGrana. Registration is blocked.

- **SocietyOverdraftRecord**: The persistent record tracking a Society's overdraft state.

---

## 4. Overdraft Accounting Data Model

Each Society that has ever triggered a draw MUST have a `SocietyOverdraftRecord`. The record
MUST contain:

| Field | Type | Description |
|-------|------|-------------|
| SocietyDid | string | Primary DID of the Society. |
| DrawAmountGrana | int64 | Fixed draw increment for this Society. |
| OverdraftCeilingGrana | int64 | Maximum allowed TotalOverdrawnGrana. |
| TotalOverdrawnGrana | int64 | Current outstanding drawn grana. Reset-able by top-up (floor: 0). |
| LifetimeDrawsGrana | int64 | Cumulative total drawn. MUST NEVER decrease. |
| DrawCount | int | Total number of draw events. Monotonically increasing. |
| LastDrawAt | datetime? | UTC timestamp of the most recent draw. Null if no draws yet. |
| Status | enum | `Clean`, `Overdrawn`, `Ceiling`. |

### 4.1 Status Derivation

Status MUST be derived from TotalOverdrawnGrana at the time of any state-modifying operation:

```
if TotalOverdrawnGrana == 0:
    Status = Clean
elif TotalOverdrawnGrana >= OverdraftCeilingGrana:
    Status = Ceiling
else:
    Status = Overdrawn
```

---

## 5. Draw Trigger Condition

A Society MUST check its wallet balance at the start of every `RegisterCitizenInSocietyAsync`
operation. The check is:

```
walletBalance = SUM(unspent UTXOs for SocietyDid)
if walletBalance < CitizenEndowmentGrana:
    invoke overdraft draw facility
```

`CitizenEndowmentGrana` = 1,000,000,000 grana (1,000 SVRN7).

If the wallet balance is at or above `CitizenEndowmentGrana`, the draw facility is not invoked
and citizen registration proceeds normally.

---

## 6. Ceiling Enforcement

Before dispatching a draw request, the Society MUST verify the ceiling condition:

```
if TotalOverdrawnGrana + DrawAmountGrana > OverdraftCeilingGrana:
    raise SocietyEndowmentDepletedException
```

`SocietyEndowmentDepletedException` MUST include:
- `SocietyDid`
- `TotalOverdrawnGrana`
- `OverdraftCeilingGrana`

When this exception is raised, the citizen registration operation MUST be aborted. No UTXO is
created, no DID Document is issued, and no Merkle log entry is appended.

The ceiling is a hard limit. The Federation does not override the ceiling automatically. The
Society MUST wait for a Federation top-up (Section 9) before registration can resume.

### 6.1 Ceiling Configuration

`OverdraftCeilingGrana` SHOULD be set to a multiple of `DrawAmountGrana`. Setting it to less
than `DrawAmountGrana` would cause the ceiling to be exceeded on the first draw, permanently
blocking the facility. Implementations SHOULD validate this constraint at startup.

---

## 7. Draw Request Protocol

### 7.1 Request Construction

The Society MUST construct an `OverdraftDrawRequest` DIDComm message [DRAFT-DIDCOMM-TRANSFER]:

```json
{
  "type": "did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-request",
  "id":   "<uuid>",
  "from": "<societyDid>",
  "to":   "<federationDid>",
  "body": {
    "societyDid":       "<societyDid>",
    "drawAmountGrana":  <DrawAmountGrana>,
    "drawCount":        <DrawCount + 1>,
    "reason":           "CitizenRegistration",
    "requestedAt":      "<ISO-8601-UTC>"
  }
}
```

The message MUST be packed using DIDComm SignThenEncrypt [DRAFT-DIDCOMM-TRANSFER] before dispatch.

### 7.2 Federation Processing

Upon receiving the `OverdraftDrawRequest`, the Federation MUST:

1. Verify the Society is registered and has `Status = Active`.
2. Verify the draw amount matches the Society's configured `DrawAmountGrana`.
3. Verify the Federation wallet has sufficient balance:
   `FederationWalletBalance ≥ DrawAmountGrana`. If not, return an error response.
4. Execute a UTXO transfer of `DrawAmountGrana` from the Federation wallet to the Society wallet.
5. Append a `Transfer` entry to the Federation's Merkle log (source: Federation wallet,
   destination: Society wallet, amount: DrawAmountGrana, reason: OverdraftDraw).
6. Issue an `OverdraftDrawReceipt` VC (Section 7.4).
7. Return the `OverdraftDrawReceipt` DIDComm response.

Steps 4 and 5 MUST be atomic. If the UTXO transfer fails, no log entry is appended and the
response returns an error.

### 7.3 Timeout Handling

The Society MUST apply a timeout to the draw round-trip. The default timeout is 30 seconds
and SHOULD be configurable. If the timeout expires before a response is received:

1. The Society MUST raise `FederationUnavailableException` with fields:
   - `Operation = "OverdraftDraw"`
   - `Timeout = <configured timeout>`
2. The citizen registration MUST be aborted.
3. The Society's overdraft accounting MUST NOT be updated (no draw occurred).
4. The payer's UTXOs MUST remain unmodified.

The Society SHOULD log the timeout event for operational monitoring.

### 7.4 OverdraftDrawReceipt VC

The Federation MUST issue an `OverdraftDrawReceipt` VC confirming the draw:

```json
{
  "type": ["VerifiableCredential", "OverdraftDrawReceipt"],
  "issuer": "<federationDid>",
  "credentialSubject": {
    "societyDid":            "<societyDid>",
    "drawnAmountGrana":      <DrawAmountGrana>,
    "newSocietyBalanceGrana":<postDrawBalance>,
    "drawCount":             <Society.DrawCount + 1>,
    "processedAt":           "<ISO-8601-UTC>"
  }
}
```

The Society MUST store this VC in its VC registry as an audit record.

---

## 8. Post-Draw Accounting

After receiving a successful `OverdraftDrawReceipt`, the Society MUST update its
`SocietyOverdraftRecord` atomically:

```
TotalOverdrawnGrana += DrawAmountGrana
LifetimeDrawsGrana  += DrawAmountGrana   // MUST NEVER decrease
DrawCount           += 1
LastDrawAt           = UtcNow
Status               = derive(TotalOverdrawnGrana, OverdraftCeilingGrana)
```

The update MUST be committed to persistent storage before citizen registration proceeds.
If the update fails (e.g., due to a database error), the citizen registration MUST be aborted
and the overdraft draw result MUST be treated as uncommitted.

---

## 9. Top-Up Protocol

The Federation MAY proactively top up a Society's balance to reduce its `TotalOverdrawnGrana`.
The Federation MUST send an `EndowmentTopUp` DIDComm message [DRAFT-DIDCOMM-TRANSFER]:

```json
{
  "type": "did:drn:svrn7.net/protocols/endowment/1.0/top-up",
  "from": "<federationDid>",
  "to":   "<societyDid>",
  "body": {
    "societyDid":       "<societyDid>",
    "topUpAmountGrana": <topUpAmount>,
    "reason":           "<human-readable rationale>",
    "sentAt":           "<ISO-8601-UTC>"
  }
}
```

The top-up message MUST also be accompanied by a real UTXO transfer:
```
FederationWallet → SocietyWallet: topUpAmountGrana (real UTXO operation)
```

Upon receiving a valid top-up, the Society MUST update its `SocietyOverdraftRecord`:

```
TotalOverdrawnGrana = MAX(0, TotalOverdrawnGrana - topUpAmountGrana)
Status               = derive(TotalOverdrawnGrana, OverdraftCeilingGrana)
```

`LifetimeDrawsGrana` MUST NOT be reduced by a top-up. The top-up amount may exceed
`TotalOverdrawnGrana`; any excess accrues to the Society's general operating balance.

---

## 10. Audit Invariants

The following invariants MUST hold at all times and MUST be verifiable from the persistent state:

### 10.1 Lifetime Monotonicity
```
LifetimeDrawsGrana at time T+1 ≥ LifetimeDrawsGrana at time T
```
`LifetimeDrawsGrana` MUST NEVER decrease. This is enforceable by a database-level constraint.

### 10.2 Draw Count Monotonicity
```
DrawCount at time T+1 ≥ DrawCount at time T
```
`DrawCount` MUST NEVER decrease.

### 10.3 Draw Consistency
```
LifetimeDrawsGrana = DrawCount * DrawAmountGrana
```
This invariant holds provided `DrawAmountGrana` has never changed for the Society. If
`DrawAmountGrana` is reconfigured (a governance operation outside this specification),
the invariant must be recomputed from the draw history.

### 10.4 Outstanding Balance Bound
```
0 ≤ TotalOverdrawnGrana ≤ OverdraftCeilingGrana
```
`TotalOverdrawnGrana` is bounded below by 0 (floor on top-up) and above by
`OverdraftCeilingGrana` (ceiling enforcement on draw).

### 10.5 Merkle Log Coverage
Every draw event MUST produce a corresponding `Transfer` entry in the Federation's Merkle log.
The absence of a Merkle log entry for a draw event is a protocol violation.

---

## 11. Monitoring Recommendations

Implementations SHOULD expose the following operational metrics:

| Metric | Description |
|--------|-------------|
| `svrn7.overdraft.total_overdrawn_grana` | Current TotalOverdrawnGrana per Society |
| `svrn7.overdraft.ceiling_grana` | Configured OverdraftCeilingGrana per Society |
| `svrn7.overdraft.lifetime_draws_grana` | Cumulative LifetimeDrawsGrana per Society |
| `svrn7.overdraft.draw_count` | Total DrawCount per Society |
| `svrn7.overdraft.societies_at_ceiling` | Count of Societies with Status = Ceiling |
| `svrn7.overdraft.draw_timeouts` | Count of draw requests that timed out |

A Society with `Status = Ceiling` that is not receiving top-ups will be unable to onboard new
Citizens. Operators SHOULD configure alerts for this condition.

---

## 12. Security Considerations

### 12.1 Ceiling as a Federation Credit Risk Control
The `OverdraftCeilingGrana` is the primary mechanism by which the Federation controls its
credit exposure to each Society. The Federation MUST configure the ceiling conservatively.
A Society at ceiling cannot register new Citizens, which creates operational pressure to
repay (via top-up) rather than leaving the ceiling as a soft limit.

### 12.2 Draw Request Authentication
Draw requests are DIDComm SignThenEncrypt messages. The Federation MUST verify the JWS
signature against the requesting Society's Ed25519 public key before processing any draw.
An unauthenticated draw request MUST be rejected.

### 12.3 Federation Wallet Depletion
If the Federation's own wallet is insufficient to fund a draw, the Federation MUST return an
error and NOT execute a partial draw. Partial draws would violate the supply conservation
invariant [DRAFT-MONETARY]. The Federation MUST monitor its wallet balance and issue supply
updates [DRAFT-MONETARY] before the wallet is exhausted.

### 12.4 Double-Draw Prevention
The draw protocol is synchronous: the Society dispatches a request and awaits a receipt.
On timeout, the Society does not update its accounting (Section 7.3). If the Federation
processed the draw before the timeout (but the response was lost in transit), the Society's
accounting will be incorrect: the Federation wallet will show a debit that the Society does
not recognise. Implementations SHOULD implement a draw request idempotency mechanism using
the DIDComm message `id` as a deduplication key, with the Federation returning a cached
receipt for duplicate requests within a 48-hour window.

---

## 13. Privacy Considerations

Overdraft records reveal a Society's citizen registration rate and overall financial health
to Federation operators. This is intentional — the Federation must be able to assess credit
risk. Overdraft records MUST be treated as confidential between the Society and the Federation
and MUST NOT be exposed to other Societies.

---

## 14. IANA Considerations

This document has no IANA actions.

---

## 15. References

### Normative
- [RFC2119] Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174] Leiba, B. Ambiguity of Uppercase vs Lowercase. May 2017.
- [RFC6962] Laurie, B. et al. Certificate Transparency. June 2013.

### Informative
- [DRAFT-MONETARY] Herman, M. SOVRONA (SVRN7) Monetary Transfer Protocol. draft-herman-svrn7-monetary-protocol-00.
- [WEB70-ARCH] Herman, M. Web 7.0 Digital Society Architecture. draft-herman-web7-society-architecture-00.
- [DRAFT-DIDCOMM-TRANSFER] Herman, M. DIDComm Transfer Protocol for SVRN7. draft-herman-didcomm-svrn7-transfer-00.
- [WEB70-IMPL] Herman, M. SOVRONA (SVRN7) .NET 8 Reference Implementation. https://github.com/web7foundation/svrn7.

---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
URI: https://hyperonomy.com/about/
