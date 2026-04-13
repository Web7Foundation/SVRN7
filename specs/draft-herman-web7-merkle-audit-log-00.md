# RFC 6962 Merkle Audit Log Profile for Web 7.0 Decentralised Monetary Systems
# draft-herman-web7-merkle-audit-log-00
# Author: M. Herman, Web 7.0 Foundation
# Published: April 2026

Internet-Draft: draft-herman-web7-merkle-audit-log-00
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

This document defines a profile of RFC 6962 Certificate Transparency for use as a tamper-evident
audit log in Web 7.0 decentralised monetary and identity systems. The profile specifies the hash
construction algorithm, the entry type taxonomy, the signed tree head structure and governance
key binding, the deployment model (one log per Federation or Society deployment instance), the
audit verification procedure, and the health monitoring requirements. Unlike the original RFC 6962
application domain (TLS certificate transparency), this profile applies to monetary transfers,
identity lifecycle events, and governance operations in a federated digital society. The profile
is designed to provide tamper evidence equivalent to a shared distributed ledger without requiring
consensus across participants.

---

## 1. Introduction

Certificate Transparency [RFC6962] is a technology developed by Google to audit TLS certificates
for the entire web. It is built on a Merkle tree [MERKLE] — an append-only log whose root hash
is a cryptographic commitment to all entries ever appended. Any modification to any historical
entry produces a different root hash, which can be detected by recomputing the tree.

The Web 7.0 digital society ecosystem [WEB70-ARCH] requires tamper-evident audit logging for
monetary transfers, DID lifecycle events, VC revocation, epoch transitions, and governance
operations. A distributed ledger (blockchain) could provide this guarantee, but at the cost of
requiring all participants to reach consensus on a global transaction order. RFC 6962 provides
the same tamper-evidence guarantee without consensus: each Federation and Society maintains its
own independent log, and cross-Society consistency is achieved through DIDComm-signed credentials
[DRAFT-DIDCOMM-TRANSFER] rather than shared state.

### 1.1 Motivation

The choice of RFC 6962 as the audit mechanism for Web 7.0 is grounded in the following
observations:

1. **Proven at scale**: Google's Certificate Transparency system has secured TLS certificates
   for the entire web since 2013. The hash construction and signed tree head model are battle-tested.
2. **No consensus requirement**: Each deployment maintains its own log independently. There is
   no global ordering problem to solve.
3. **Standard-based**: Building on a published IETF RFC rather than a proprietary or novel
   cryptographic construction maximises interoperability and AI legibility.
4. **Verifiable without trust**: Any party holding the Foundation's public key can verify a
   signed tree head. Verifying the log itself requires only the log entries and SHA-256.

### 1.2 Scope

This document specifies:
1. Hash construction (leaf and internal nodes).
2. Root computation for non-power-of-2 tree sizes.
3. The entry schema for each event type.
4. The signed tree head structure and governance key binding.
5. The deployment model (one log per deployment instance).
6. Merkle inclusion proof verification.
7. Health monitoring requirements.
8. Audit verification procedure.

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

All SHA-256 hash values in this document are 32 bytes (256 bits) in length.

---

## 3. Terminology

- **Merkle Log**: An append-only sequence of log entries whose integrity is protected by a
  Merkle tree computed over all entries.

- **Log Entry**: A single record appended to the Merkle log. Contains an `EntryType`, a JSON
  payload, and the UTC timestamp of appending.

- **Leaf Hash**: The SHA-256 hash of a single log entry, computed with the RFC 6962 leaf prefix.

- **Root Hash**: The SHA-256 hash that summarises the entire log. Computed bottom-up from leaf
  hashes using the RFC 6962 internal node prefix.

- **Signed Tree Head (STH)**: A record containing the current `RootHash`, `TreeSize`, and
  `Timestamp`, signed by the Foundation governance key pair (secp256k1).

- **Foundation Key**: The secp256k1 governance key pair of the Web 7.0 Foundation. Its public
  key is configured in each Federation and Society deployment. Used to sign STHs.

- **Deployment**: A single running instance of a Federation or Society node. Each deployment
  has exactly one Merkle log.

- **EntryType**: A string label identifying the category of a log entry (e.g.,
  `"CitizenRegistration"`, `"Transfer"`).

---

## 4. Hash Construction

### 4.1 Leaf Hash

For a log entry `e` serialised as `payload-bytes` (UTF-8 encoded JSON), the leaf hash MUST be:

```
leaf_hash(e) = SHA-256(0x00 || payload-bytes)
```

The `0x00` prefix byte is mandated by RFC 6962 Section 2.1 to prevent second-preimage attacks:
an attacker cannot construct a leaf whose hash equals an existing internal node hash.

### 4.2 Internal Node Hash

For an internal tree node with left child hash `l` and right child hash `r`:

```
node_hash(l, r) = SHA-256(0x01 || l || r)
```

The `0x01` prefix byte is mandated by RFC 6962 Section 2.1 to prevent leaf/internal hash
collisions: an attacker cannot construct an internal node whose hash equals an existing leaf hash.

### 4.3 Root Computation

The Merkle root MUST be computed using the following iterative algorithm:

```
function compute_root(leaf_hashes):
    if len(leaf_hashes) == 0:
        return SHA-256(0x00 || "")   // empty tree: hash of empty string with leaf prefix
    hashes = leaf_hashes
    while len(hashes) > 1:
        next_level = []
        for i in range(0, len(hashes), 2):
            if i + 1 < len(hashes):
                next_level.append(node_hash(hashes[i], hashes[i+1]))
            else:
                next_level.append(hashes[i])   // odd node: propagate unchanged
        hashes = next_level
    return hashes[0]
```

**Odd-node propagation**: When a level has an odd number of nodes, the rightmost node is
propagated unchanged to the next level. This is the RFC 6962 §2.1 construction for non-power-
of-2 tree sizes. Implementations MUST NOT duplicate the rightmost node (which would produce a
different root from the correct odd-node-propagation result).

### 4.4 Empty Log

When the log contains no entries, the root MUST be the 32-byte zero hash:

```
EMPTY_ROOT = 0x00...00 (32 bytes)
```

---

## 5. Entry Schema

Each log entry MUST be serialised as a JSON object with the following mandatory fields:

| Field | Type | Description |
|-------|------|-------------|
| `entryType` | string | One of the entry types defined in Section 5.1. |
| `timestamp` | string | ISO 8601 UTC timestamp of the event. |
| `deploymentDid` | string | DID of the Federation or Society that owns this log. |

Entry types MAY include additional fields specific to the event category. All additional fields
MUST be JSON-serialisable. Implementations MUST serialise entries in a deterministic field order
(alphabetical by field name is RECOMMENDED) to ensure that the same event produces the same
payload bytes across implementations.

### 5.1 Entry Type Taxonomy

#### 5.1.1 CitizenRegistration

Appended by a Society when a new citizen is successfully registered.

```json
{
  "entryType":     "CitizenRegistration",
  "timestamp":     "<ISO-8601-UTC>",
  "deploymentDid": "<societyDid>",
  "citizenDid":    "<citizenPrimaryDid>",
  "societyDid":    "<societyDid>",
  "endowmentGrana": 1000000000
}
```

#### 5.1.2 SocietyRegistration

Appended by the Federation when a new Society is successfully registered.

```json
{
  "entryType":          "SocietyRegistration",
  "timestamp":          "<ISO-8601-UTC>",
  "deploymentDid":      "<federationDid>",
  "societyDid":         "<societyDid>",
  "primaryMethodName":  "<methodName>",
  "endowmentGrana":     <endowmentAmount>
}
```

#### 5.1.3 Transfer

Appended by a Society when a same-Society transfer is successfully committed.

```json
{
  "entryType":    "Transfer",
  "timestamp":    "<ISO-8601-UTC>",
  "deploymentDid":"<societyDid>",
  "transferId":   "<Blake3Hex>",
  "payerDid":     "<did>",
  "payeeDid":     "<did>",
  "amountGrana":  <int64>
}
```

#### 5.1.4 EpochTransition

Appended by the Federation when an epoch advancement is successfully executed.

```json
{
  "entryType":     "EpochTransition",
  "timestamp":     "<ISO-8601-UTC>",
  "deploymentDid": "<federationDid>",
  "fromEpoch":     <int>,
  "toEpoch":       <int>,
  "governanceRef": "<URI>"
}
```

#### 5.1.5 SupplyUpdate

Appended by the Federation when the total supply is successfully updated.

```json
{
  "entryType":          "SupplyUpdate",
  "timestamp":          "<ISO-8601-UTC>",
  "deploymentDid":      "<federationDid>",
  "previousSupplyGrana":<int64>,
  "newSupplyGrana":     <int64>,
  "governanceRef":      "<URI>"
}
```

#### 5.1.6 DidMethodRegistration

Appended by the Federation when a DID method name is registered.

```json
{
  "entryType":    "DidMethodRegistration",
  "timestamp":    "<ISO-8601-UTC>",
  "deploymentDid":"<federationDid>",
  "methodName":   "<string>",
  "societyDid":   "<did>",
  "isPrimary":    <bool>
}
```

#### 5.1.7 DidMethodDeregistration

Appended by the Federation when a DID method name is deregistered.

```json
{
  "entryType":    "DidMethodDeregistration",
  "timestamp":    "<ISO-8601-UTC>",
  "deploymentDid":"<federationDid>",
  "methodName":   "<string>",
  "societyDid":   "<did>",
  "dormantUntil": "<ISO-8601-UTC>"
}
```

#### 5.1.8 CrossSocietyTransferDebit

Appended by the originating Society when a payer's UTXO is debited for a cross-Society transfer.

```json
{
  "entryType":       "CrossSocietyTransferDebit",
  "timestamp":       "<ISO-8601-UTC>",
  "deploymentDid":   "<originatingSocietyDid>",
  "transferId":      "<Blake3Hex>",
  "payerDid":        "<did>",
  "payeeDid":        "<did>",
  "amountGrana":     <int64>,
  "targetSocietyDid":"<did>"
}
```

#### 5.1.9 CrossSocietyTransferCredit

Appended by the receiving Society when a payee's UTXO is credited.

```json
{
  "entryType":        "CrossSocietyTransferCredit",
  "timestamp":        "<ISO-8601-UTC>",
  "deploymentDid":    "<receivingSocietyDid>",
  "transferId":       "<Blake3Hex>",
  "payeeDid":         "<did>",
  "creditedGrana":    <int64>,
  "originSocietyDid": "<did>"
}
```

#### 5.1.10 CrossSocietyTransferSettled

Appended by the originating Society upon receiving the `TransferOrderReceipt` confirmation.

```json
{
  "entryType":       "CrossSocietyTransferSettled",
  "timestamp":       "<ISO-8601-UTC>",
  "deploymentDid":   "<originatingSocietyDid>",
  "transferId":      "<Blake3Hex>",
  "settledAt":       "<ISO-8601-UTC>"
}
```

#### 5.1.11 GdprErasure

Appended by a Society when a GDPR Article 17 erasure is completed [DRAFT-MONETARY].

```json
{
  "entryType":    "GdprErasure",
  "timestamp":    "<ISO-8601-UTC>",
  "deploymentDid":"<societyDid>",
  "subjectDid":   "<citizenDid>",
  "requestedAt":  "<ISO-8601-UTC>",
  "completedAt":  "<ISO-8601-UTC>"
}
```

---

## 6. Signed Tree Head

### 6.1 Structure

A Signed Tree Head (STH) MUST contain the following fields:

| Field | Type | Description |
|-------|------|-------------|
| RootHash | string | Hex-encoded 32-byte SHA-256 Merkle root. |
| TreeSize | int64 | Number of entries in the log at time of signing. |
| Timestamp | string | ISO 8601 UTC timestamp of signing. |
| Signature | string | CESR-encoded secp256k1 signature over the canonical JSON. |

### 6.2 Canonical JSON for Signing

```json
{
  "rootHash":  "<hex-string>",
  "treeSize":  <int64>,
  "timestamp": "<ISO-8601-UTC>"
}
```

Fields MUST appear in this order. No whitespace between tokens. UTF-8 encoding.

### 6.3 Signature Algorithm

```
payload        = UTF-8(canonical-json)
signature-bytes = secp256k1-sign(SHA-256(payload), foundation-private-key)
Signature       = "0B" + base64url-nopad(signature-bytes)
```

### 6.4 Signing Frequency

Implementations MUST sign tree heads at least every 24 hours. Implementations SHOULD sign
tree heads immediately after each significant governance operation (epoch transition, supply
update, GDPR erasure). STHs MUST be stored persistently in the log's database.

### 6.5 Verification

Any party holding the Foundation's secp256k1 public key can verify an STH:
1. Recompute the root hash from the full set of log entries using the algorithm in Section 4.3.
2. Compare to `STH.RootHash`. If they differ, the log has been tampered with.
3. Reconstruct the canonical JSON from `STH.RootHash`, `STH.TreeSize`, `STH.Timestamp`.
4. Verify the CESR signature against the Foundation public key.

---

## 7. Deployment Model

### 7.1 One Log Per Deployment

Each Federation and Society MUST maintain exactly one Merkle log in its own embedded database.
There is no shared log. With N Societies and 1 Federation, the total number of logs in the
ecosystem is N + 1.

This model has the following properties:

| Property | Value |
|----------|-------|
| Shared consensus required | No |
| Network connectivity required for local operations | No |
| Cross-Society audit | Requires querying both logs and correlating on TransferId |
| Tamper evidence | Per-deployment, verified by STH signatures |

### 7.2 Log Entries Are Never Deleted

Log entries MUST never be physically deleted. This is an invariant of the append-only model:
deletion would change the root hash, which would invalidate all subsequent STHs.

### 7.3 Log Integrity Recovery

If a log entry is suspected of tampering, the integrity of the log can be verified by:
1. Retrieving all log entries in sequence number order.
2. Recomputing all leaf hashes.
3. Recomputing the Merkle root bottom-up.
4. Comparing the computed root to the most recent STH's `RootHash`.
5. Verifying the STH signature against the Foundation public key.

A mismatch at step 4 indicates that at least one log entry has been modified since the STH
was signed. The tampered entry can be isolated by binary search over the subtrees.

---

## 8. Merkle Inclusion Proof

A Merkle inclusion proof demonstrates that a specific log entry is included in the log
at a specific tree size, without revealing all other entries.

### 8.1 Proof Structure

An inclusion proof for entry at index `i` in a tree of size `n` consists of:
- `leaf_hash`: The hash of the entry being proved.
- `audit_path`: The sequence of sibling hashes required to recompute the root.

### 8.2 Proof Verification

```
function verify_inclusion(leaf_hash, audit_path, root_hash, tree_size):
    current = leaf_hash
    for sibling in audit_path:
        if current is left sibling:
            current = node_hash(current, sibling)
        else:
            current = node_hash(sibling, current)
    return current == root_hash
```

Implementations SHOULD support inclusion proof generation and verification for all log entries.

---

## 9. Health Monitoring

### 9.1 Health Check

Implementations MUST expose a health check that reports the following:
- Current log size (`TreeSize`).
- Age of the most recent STH in seconds.
- `RootHash` of the most recent STH (truncated or full, as appropriate for the monitoring system).

### 9.2 Degraded Condition

The health check MUST report `Degraded` if:
- No STH has been signed (log has entries but no STH).
- The most recent STH is older than `MaxTreeHeadAge` (default: 24 hours).

### 9.3 Unhealthy Condition

The health check MUST report `Unhealthy` if:
- The log database is inaccessible.
- The recomputed root hash does not match the most recent STH.

---

## 10. Relationship to RFC 6962

This profile is derived from RFC 6962 [RFC6962] and adopts its hash construction verbatim
(leaf prefix `0x00`, internal prefix `0x01`, odd-node propagation). It differs from RFC 6962
in the following respects:

| Aspect | RFC 6962 | This Profile |
|--------|----------|-------------|
| Domain | TLS certificate transparency | Monetary and identity event logging |
| Log entry format | DER-encoded certificate structures | JSON objects with typed EntryType field |
| Signing key | Log operator's RSA or ECDSA key | Foundation secp256k1 governance key |
| Signature encoding | DER | CESR (`0B` prefix + base64url-nopad) |
| Deployment model | Public, multi-party log | Private, per-deployment log |
| Consensus | Multiple monitors cross-check | Single Federation or Society operator |

---

## 11. Security Considerations

### 11.1 Foundation Key as Single Point of Trust
The STH signature depends on the Foundation's secp256k1 private key. Compromise of this key
would allow an attacker to sign a fraudulent STH over a tampered log. The Foundation Key MUST
be stored in an HSM or equivalent offline cold storage and MUST NOT be present in application
configuration files or environment variables.

### 11.2 Clock Manipulation
The STH timestamp is signed but the signing algorithm does not prevent a future-dated STH.
Operators SHOULD use NTP-synchronised clocks and SHOULD reject STHs whose timestamps are more
than 5 minutes in the future.

### 11.3 Log Truncation Attacks
An attacker with write access to the log database could truncate the log to remove recent
entries. This would produce a different root hash from any previously signed STH, which would
be detected by verification. Implementations MUST protect the log database with appropriate
filesystem and database-level access controls.

### 11.4 Merkle Tree Collision Resistance
The security of this log relies on SHA-256 collision resistance. As of the date of this
specification, SHA-256 is considered secure against known attacks. Implementations SHOULD
monitor cryptographic standards bodies for any future weakening of SHA-256 and plan for
algorithm agility.

---

## 12. Privacy Considerations

### 12.1 Citizen DID Visibility
Log entries include citizen DIDs in plaintext. Any party with read access to the log can
enumerate all citizen registrations, transfers, and erasures. Implementations MUST restrict
read access to the log database to authorised operators only.

### 12.2 GDPR Erasure Entries
The `GdprErasure` entry includes the subject's DID, which constitutes personal data. Operators
MUST NOT expose `GdprErasure` entries to parties other than the Federation and the subject's
legal counsel without appropriate legal basis.

---

## 13. IANA Considerations

This document has no IANA actions.

---

## 14. References

### Normative
- [RFC2119] Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174] Leiba, B. Ambiguity of Uppercase vs Lowercase. May 2017.
- [RFC6962] Laurie, B. et al. Certificate Transparency. RFC 6962, June 2013.
- [MERKLE] Merkle, R. A Digital Signature Based on a Conventional Encryption Function. 1987.

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
