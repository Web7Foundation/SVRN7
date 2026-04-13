# Epoch-Based Monetary Policy Governance for Web 7.0 Digital Societies
# draft-herman-web7-epoch-governance-00
# Author: M. Herman, Web 7.0 Foundation
# Published: April 2026

Internet-Draft: draft-herman-web7-epoch-governance-00
Published:      April 2026
Expires:        October 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Web 7.0 Foundation Governance Council
Related:        draft-herman-svrn7-monetary-protocol-00
                draft-herman-web7-society-architecture-00
                draft-herman-web7-merkle-audit-log-00

---

## Abstract

This document specifies the epoch-based monetary policy governance model for the SOVRONA
(SVRN7) Web 7.0 Shared Reserve Currency ecosystem. The epoch model defines three sequential
monetary policy phases — Endowment (Epoch 0), Ecosystem Utility (Epoch 1), and Market Issuance
(Epoch 2) — each permitting progressively broader transfer eligibility between participants.
Epoch advancement is a one-way, irreversible governance operation authorised by the Foundation
governance key pair, recorded permanently in the Federation's Merkle audit log. This document
specifies the epoch matrix (which transfers are permitted in each epoch), the governance
authorisation protocol, the forward-only invariant, and the distinctions between the Web 7.0
epoch model and conventional cryptocurrency consensus-based governance. The epoch model enables
a controlled, phased launch of the SVRN7 monetary ecosystem with strong auditability.

---

## 1. Introduction

A common challenge in the launch of a new monetary system is the bootstrapping problem: how
can a currency gain utility before it has broad adoption, and how can adoption be encouraged
without creating speculative or disruptive dynamics before the underlying infrastructure is
proven? Traditional cryptocurrency launches address this with mechanisms such as vesting
schedules, lock-up periods, and mining difficulty adjustments, but these are typically enforced
at the protocol layer of a shared ledger with no human override.

The Web 7.0 epoch model takes a different approach: a small number of well-defined policy phases,
each explicitly authorised by a human governance decision, enforced at the validation layer of
every participating node. The model is analogous in spirit to staged rollouts in software
deployment, or to the phased opening of a new financial market — but it is cryptographically
enforced rather than merely contractually agreed.

### 1.1 Design Principles

The epoch model is governed by three principles:

1. **Determinism**: The transfer eligibility rules for each epoch are fully specified in this
   document. There is no ambiguity about what is permitted in Epoch 0, 1, or 2.

2. **Irreversibility**: Epoch advancement cannot be reversed. Once Epoch 1 is active, Epoch 0
   rules cannot be reinstated. This provides certainty to participants: the rules can only
   become more permissive, never more restrictive.

3. **Auditability**: Every epoch transition is recorded in the Federation's Merkle audit log
   with a governance reference that links the technical event to the human decision that
   authorised it.

### 1.2 Distinctions from Cryptocurrency Governance

The epoch model differs from typical cryptocurrency governance in several important respects:

| Aspect | Typical cryptocurrency | Web 7.0 epoch model |
|--------|----------------------|---------------------|
| Governance mechanism | Token-weighted voting | Foundation governance key signature |
| Enforcement | Protocol layer (consensus) | Validation layer (each node independently) |
| Reversibility | Varies; hard forks possible | Strictly irreversible |
| Transparency | On-chain transactions | Merkle log + external governance reference |
| Number of phases | Continuous parameter space | Three discrete, named phases |
| Participation in governance | Token holders | Foundation (as delegated by Web 7.0 Foundation Governance Council) |

### 1.3 Scope

This document specifies:
1. The three epoch values and their names.
2. The epoch matrix (transfer eligibility per epoch).
3. The epoch advancement authorisation protocol.
4. The forward-only invariant and its enforcement.
5. The Merkle log recording of epoch transitions.
6. Guidance for implementers on epoch state management.
7. Future considerations for Epoch 3 and beyond.

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

---

## 3. Terminology

- **Epoch**: A named monetary policy phase identified by a non-negative integer. The epoch
  value governs which transfers are permitted by the validation pipeline.

- **Epoch Matrix**: The lookup table specifying, for each epoch value, which (payer type,
  payee type) combinations are permitted.

- **Epoch Advancement**: The irreversible transition from epoch N to epoch M (where M > N).
  Requires a Foundation-signed governance operation.

- **Foundation Governance Key**: The secp256k1 key pair held by the Web 7.0 Foundation whose
  public key is embedded in all Federation and Society deployment configurations.

- **Governance Reference**: A URI linking an epoch advancement to the human-readable governance
  decision that authorised it (e.g., a Foundation Governance Council resolution document).

- **Endowment Epoch (Epoch 0)**: The initial operating phase. Citizens may only transfer to
  their own Society's wallet or to the Federation wallet.

- **Ecosystem Utility Epoch (Epoch 1)**: The second phase. Any citizen may transfer to any
  citizen, any Society wallet, or the Federation wallet.

- **Market Issuance Epoch (Epoch 2)**: The third phase. Any participant may transfer to any
  other participant.

- **Participant**: Any entity with an Active DID and a wallet in the ecosystem: a Citizen,
  a Society, or the Federation.

---

## 4. Epoch Values and Names

| Value | Name | Phase description |
|-------|------|-------------------|
| 0 | Endowment | System bootstrapping. Citizens may only transfer to their Society or the Federation. |
| 1 | Ecosystem Utility | Broad citizen-to-citizen utility. Full cross-Society transfers enabled. |
| 2 | Market Issuance | Unrestricted participation. All entities may transact freely. |

The epoch value MUST be stored as a non-negative integer. The initial epoch at deployment
MUST be 0. The maximum defined epoch value in this specification is 2. Values greater than 2
are reserved for future specification and MUST NOT be used by conformant implementations
without a successor document defining their semantics.

---

## 5. Epoch Matrix

The epoch matrix specifies transfer eligibility. Step 2 of the transfer validation pipeline
[DRAFT-MONETARY] applies this matrix to validate every transfer request.

### 5.1 Epoch 0 — Endowment

| Payer type | Permitted payee types |
|------------|----------------------|
| Citizen (any Society) | This Society's wallet DID only |
| Citizen (any Society) | The Federation wallet DID only |
| Society wallet | Not applicable (Society transfers are internal operations, not citizen-initiated) |
| Federation wallet | Not applicable |

In Epoch 0, a citizen MAY transfer SVRN7 to exactly two destinations: their own Society's
wallet or the Federation wallet. All other transfer destinations MUST be rejected with
`EpochViolationError`.

**Rationale**: Epoch 0 serves the bootstrapping phase of a new digital society. Citizens
receive their endowment (from the Society) and may return value to the Society or the
Federation, but peer-to-peer exchange is not yet enabled. This limits the risk of speculative
dynamics before the ecosystem has reached critical mass.

### 5.2 Epoch 1 — Ecosystem Utility

| Payer type | Permitted payee types |
|------------|----------------------|
| Citizen (any Society) | Any citizen DID in any Society |
| Citizen (any Society) | Any Society wallet DID |
| Citizen (any Society) | The Federation wallet DID |

In Epoch 1, full citizen-to-citizen and citizen-to-society transfers are enabled. Cross-Society
transfers additionally require Step 8 of the validation pipeline (Society Membership validation).

**Rationale**: Epoch 1 represents the target operating state of the mature ecosystem. Citizens
can exchange value freely, create markets, and conduct commerce. Cross-Society transfers enable
inter-community economic activity.

### 5.3 Epoch 2 — Market Issuance

All transfers between any pair of Active participants are permitted, subject only to balance
and signature validation. No epoch-based restrictions apply.

**Rationale**: Epoch 2 is the maximally permissive state, enabling the Federation and Societies
to participate in transfers as payers on equal footing with citizens. This enables governance
operations, market-making, and institutional transfers that may require the Federation or a
Society to initiate outbound transfers.

### 5.4 Implementation Notes

The epoch matrix MUST be applied before signature verification (Step 2 fires before Step 6 in
the validation pipeline [DRAFT-MONETARY]). This means that an epoch violation can be detected
without a valid signature — reducing unnecessary cryptographic work for clearly ineligible
transfers.

Implementations MUST treat the epoch as a deployment-global value. The current epoch MUST be
the same for all transfer validations within a single deployment instance. The epoch MUST be
stored persistently — an implementation restart MUST NOT reset the epoch to 0.

---

## 6. Epoch Advancement Authorisation Protocol

### 6.1 Governance Decision

Epoch advancement is a significant governance event that affects all participants in the
ecosystem. Before advancing the epoch, the Web 7.0 Foundation Governance Council MUST:
1. Publish a governance resolution documenting the rationale for advancement.
2. Assign a stable URI to the resolution document.
3. Record the resolution in the Foundation's governance log.

The `governanceRef` field in the advancement request (Section 6.2) MUST reference this URI.

### 6.2 Advancement Request Format

The Foundation MUST construct an epoch advancement request as follows:

```json
{
  "toEpoch":       <target-epoch-integer>,
  "governanceRef": "<URI to governance resolution>",
  "reason":        "<human-readable rationale>",
  "notes":         "<optional additional context or null>",
  "requestedAt":   "<ISO-8601-UTC>",
  "signature":     "<CESR-secp256k1 over canonical JSON>"
}
```

### 6.3 Canonical JSON for Signing

The signing input MUST be the compact JSON of the above object with fields in the order shown,
UTF-8 encoded. The `signature` field MUST be omitted from the JSON before signing:

```json
{"toEpoch":<int>,"governanceRef":"<URI>","reason":"<string>","notes":<string|null>,"requestedAt":"<ISO-8601-UTC>"}
```

Signature:
```
payload-bytes = UTF-8(canonical-json-without-signature)
hash          = SHA-256(payload-bytes)
sig-bytes     = secp256k1-sign-compact(hash, foundation-private-key)
signature     = "0B" + base64url-nopad(sig-bytes)
```

### 6.4 Validation

The receiving Federation node MUST:

1. Verify the signature against the Foundation public key.
2. Verify freshness: `|UtcNow - requestedAt| ≤ 10 minutes`.
3. Verify the forward-only invariant: `toEpoch > currentEpoch`.
   If `toEpoch ≤ currentEpoch`, MUST raise `EpochAdvancementError` with fields
   `CurrentEpoch = currentEpoch`, `RequestedEpoch = toEpoch`.
4. Verify `toEpoch ≤ MAX_DEFINED_EPOCH` (currently 2).

On success:
1. Set `currentEpoch = toEpoch` in persistent storage.
2. Append an `EpochTransition` entry to the Federation's Merkle log.
3. Broadcast the new epoch value to all registered Societies via DIDComm or an administrative
   channel (broadcast mechanism is implementation-defined).

### 6.5 Society Synchronisation

All registered Societies MUST advance their epoch when the Federation advances. Societies that
are temporarily offline MUST synchronise their epoch value upon reconnection before processing
any new transfers. A Society operating on a stale epoch value will reject transfers that the
Federation considers valid (or accept transfers that the Federation would reject) — both of
which produce inconsistent state.

The recommended synchronisation mechanism is:
- Societies query the Federation for the current epoch on startup.
- The Federation broadcasts `EpochTransition` DIDComm messages to all registered Societies on
  advancement.
- Societies verify the broadcast signature against the Foundation public key before updating
  their local epoch value.

---

## 7. The Forward-Only Invariant

### 7.1 Statement

```
currentEpoch at time T+1 ≥ currentEpoch at time T
```

The epoch value MUST be monotonically non-decreasing. No mechanism exists to decrease the
epoch value. This is an architectural constraint, not an implementation choice.

### 7.2 Enforcement

The forward-only invariant MUST be enforced at two levels:

1. **Protocol level**: The advancement validation (Section 6.4, step 3) MUST reject any
   request with `toEpoch ≤ currentEpoch`.

2. **Storage level**: Implementations SHOULD use a database write that fails if the new epoch
   value is less than the current stored value (e.g., a conditional update: `UPDATE epoch
   SET value = :new WHERE value < :new`). This prevents a bug in the protocol layer from
   bypassing the invariant.

### 7.3 Rationale

The forward-only invariant provides certainty to ecosystem participants. Once Epoch 1 is active,
participants may build products and services that rely on citizen-to-citizen transfers being
permitted. A regression to Epoch 0 would break those products. The irreversibility guarantee
means that ecosystem participants can rely on the current epoch as a floor, not just a point
in time.

This is analogous to the forward-only invariant on supply in [DRAFT-MONETARY]: once grana is
created, it is not destroyed. Both invariants serve the same purpose — providing participants
with predictable, non-regressing guarantees about the monetary environment.

---

## 8. Epoch State Management

### 8.1 Persistent Storage

The current epoch MUST be stored in a persistent, durable store (the same svrn7.db database
that holds wallets and UTXO records). It MUST survive application restart and database
migration.

### 8.2 In-Memory Caching

Implementations MAY cache the current epoch value in memory for performance. The cached value
MUST be refreshed from the persistent store on startup and after processing an epoch advancement
request. The in-memory cache MUST be treated as authoritative during normal operation.

### 8.3 Race Condition Prevention

In multi-threaded environments, epoch read-modify-write operations MUST be protected by a
database-level transaction or mutex. A concurrent transfer validation and epoch advancement
MUST NOT produce a state where a transfer is validated against the old epoch but committed
after the new epoch takes effect.

---

## 9. Epoch Transition Log Entry

Every successful epoch advancement MUST produce an `EpochTransition` entry in the Federation's
Merkle log [DRAFT-MERKLE]:

```json
{
  "entryType":     "EpochTransition",
  "timestamp":     "<ISO-8601-UTC>",
  "deploymentDid": "<federationDid>",
  "fromEpoch":     <previous-epoch>,
  "toEpoch":       <new-epoch>,
  "governanceRef": "<URI>"
}
```

The `governanceRef` URI links the technical event to the human governance decision. Auditors
can verify:
1. That the epoch advanced (from the Merkle log entry).
2. When it advanced (from the `timestamp`).
3. Why it advanced (by following the `governanceRef` URI to the governance resolution).
4. That the entry has not been tampered with (from the STH signature covering it).

---

## 10. Monitoring and Operational Guidance

### 10.1 Epoch Readiness Assessment

Before advancing from Epoch 0 to Epoch 1, the Foundation Governance Council SHOULD assess:
- Total number of registered Societies and Citizens.
- Total SVRN7 in circulation (sum of citizen wallets).
- DIDComm transport availability across all registered Societies (required for cross-Society
  transfers in Epoch 1).
- Health of all Society Merkle logs (all STHs current).
- Completion of any planned security audits of the cross-Society transfer protocol.

### 10.2 Epoch Monitoring Metrics

Implementations SHOULD expose:

| Metric | Description |
|--------|-------------|
| `svrn7.epoch.current` | Current epoch integer value (0, 1, or 2) |
| `svrn7.epoch.transfers_rejected_epoch_violation` | Count of transfers rejected by Step 2 |
| `svrn7.epoch.last_transition_timestamp` | UTC timestamp of most recent epoch transition |

A spike in `transfers_rejected_epoch_violation` after an epoch advancement indicates that
some clients or Societies are operating with a stale epoch value and generating ineligible
transfer requests.

---

## 11. Future Epochs

This specification defines epochs 0, 1, and 2. The space of epoch values above 2 is reserved
for future specification. A successor document MAY define Epoch 3 and above. Any successor
document MUST:
1. Define the transfer eligibility matrix for the new epoch.
2. Specify the governance process for advancing to the new epoch.
3. Specify any new validation steps required by the new epoch.

Implementations SHOULD handle unknown epoch values gracefully: a transfer validated against
an epoch value for which no matrix entry exists MUST be rejected with `UnknownEpochError`
rather than raising an unhandled exception.

---

## 12. Security Considerations

### 12.1 Foundation Key Criticality
The Foundation governance key is the sole mechanism for epoch advancement. Its compromise
would allow an attacker to advance the epoch prematurely, enabling transfer types that the
ecosystem is not yet prepared for. The Foundation Key MUST be stored in an HSM and MUST
require multi-party authorisation for epoch advancement operations.

### 12.2 Stale Epoch in Distributed Deployments
In a multi-Society deployment, all Societies MUST advance their epoch synchronously with the
Federation. A Society operating on a stale epoch will either incorrectly reject valid transfers
(if its epoch is behind the Federation's) or incorrectly accept ineligible transfers (if it
received an advancement before other nodes). Implementations MUST treat epoch synchronisation
as a safety-critical operation.

### 12.3 Governance Reference URI Permanence
The `governanceRef` URI embedded in the Merkle log entry MUST be permanent. If the governance
resolution document is moved or deleted, the audit trail is broken. The Foundation MUST use
content-addressed storage (e.g., IPFS) or a DOI for governance reference URIs.

---

## 13. Privacy Considerations

The epoch value is a global, public parameter. It does not constitute personal data. The
`EpochTransition` Merkle log entry contains no personal data beyond the `deploymentDid`.

---

## 14. IANA Considerations

This document has no IANA actions.

---

## 15. References

### Normative
- [RFC2119] Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174] Leiba, B. Ambiguity of Uppercase vs Lowercase. May 2017.

### Informative
- [DRAFT-MONETARY] Herman, M. SOVRONA (SVRN7) Monetary Transfer Protocol. draft-herman-svrn7-monetary-protocol-00.
- [WEB70-ARCH] Herman, M. Web 7.0 Digital Society Architecture. draft-herman-web7-society-architecture-00.
- [DRAFT-MERKLE] Herman, M. RFC 6962 Merkle Audit Log Profile for Web 7.0. draft-herman-web7-merkle-audit-log-00.
- [WEB70-IMPL] Herman, M. SOVRONA (SVRN7) .NET 8 Reference Implementation. https://github.com/web7foundation/svrn7.

---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
URI: https://hyperonomy.com/about/
