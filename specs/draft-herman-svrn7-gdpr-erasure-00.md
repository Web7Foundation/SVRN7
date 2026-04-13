# GDPR Article 17 Erasure in Decentralised Monetary and Identity Systems
# draft-herman-svrn7-gdpr-erasure-00
# Author: M. Herman, Web 7.0 Foundation
# Published: April 2026

Internet-Draft: draft-herman-svrn7-gdpr-erasure-00
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

This document specifies a protocol for implementing the General Data Protection Regulation
(GDPR) Article 17 "right to erasure" in decentralised identity and monetary systems where
immutable audit logs and UTXO ledgers constrain full data deletion. The protocol defines a
principled approach that satisfies the operative intent of Article 17 — rendering personal
data permanently inaccessible and inoperable — while preserving the structural audit trail
required for financial system integrity. The erasure procedure covers: Foundation-signed
authorisation, private key zeroing, DID Document deactivation, Verifiable Credential
revocation, and Merkle log event recording. The document also specifies what MUST and MUST NOT
be deleted, the tension between erasure and audit integrity, and the legal disclosure
requirements that deployers MUST satisfy at citizen registration time.

---

## 1. Introduction

The General Data Protection Regulation (GDPR) Article 17 grants data subjects the right to
obtain erasure of their personal data without undue delay. In traditional centralised systems,
this is implemented by deleting database records. In decentralised identity and monetary
systems, full deletion conflicts with two structural requirements:

1. **Audit integrity**: An append-only Merkle log [DRAFT-MERKLE] provides tamper evidence
   precisely because entries cannot be deleted. Deleting an entry changes the root hash,
   invalidating all subsequent signed tree heads (STHs).

2. **Financial record retention**: UTXO records constitute a financial transaction history.
   Many jurisdictions require financial records to be retained for a minimum period (commonly
   5–10 years) regardless of GDPR erasure requests.

This document specifies a protocol that resolves this tension by distinguishing between the
two components of a citizen's personal data in the Web 7.0 ecosystem:

- **Operative data**: Private key material, which enables the citizen to sign new transactions.
  This MUST be permanently destroyed.
- **Structural data**: UTXO records, Merkle log entries, and DID Document records (marked
  Deactivated). These MUST be retained for audit integrity.

The erasure of the private key renders the identity permanently inoperable — the subject can
no longer sign new transactions, their DID is deactivated, and their VCs are revoked — while
the structural records that prove the historical accuracy of the ledger are preserved.

### 1.1 Legal Basis

GDPR Article 17(3)(b) permits refusal of erasure where processing is necessary for compliance
with a legal obligation or for the performance of a task carried out in the public interest.
GDPR Article 17(3)(e) permits refusal where processing is necessary for the establishment,
exercise, or defence of legal claims.

Financial transaction records fall under Article 17(3)(b) in jurisdictions with mandatory
financial record retention laws. The Merkle log entries fall under Article 17(3)(e) — they
are the evidentiary basis for resolving transfer disputes.

Deployers MUST obtain legal advice specific to their jurisdiction before making erasure
commitments. This document provides a technical framework; it does not constitute legal advice.

### 1.2 Scope

This document specifies:
1. The erasure authorisation protocol.
2. What MUST be erased (operative data).
3. What MUST be retained (structural data).
4. The private key zeroing procedure.
5. DID Document deactivation.
6. Verifiable Credential revocation.
7. Merkle log recording of the erasure event.
8. Disclosure requirements at registration time.

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

---

## 3. Terminology

- **Data Subject**: The natural person whose personal data is the subject of the erasure request.
  In this context, the registered Citizen.

- **Controller**: The organisation responsible for processing personal data. In this context,
  the Society that registered the Citizen.

- **Operative Data**: Personal data that enables the data subject to perform actions —
  specifically, the secp256k1 private key material used to sign transfer requests.

- **Structural Data**: Records that document historical facts — specifically, UTXO records,
  Merkle log entries, and DID Document records (in Deactivated state). These records reference
  the subject's DID but do not contain the private key.

- **Erasure Request**: A request by the Data Subject to invoke Article 17 rights.

- **Erasure Commitment**: A Foundation-signed string binding the subject DID, the request
  timestamp, and the Foundation's authorisation.

- **Key Zeroing**: The process of overwriting private key bytes with cryptographically random
  data, rendering the key permanently unrecoverable.

- **Inoperability**: The state of a citizen identity after erasure: DID is Deactivated, VCs
  are Revoked, private key is zeroed. The identity cannot sign new transactions.

---

## 4. What Constitutes Personal Data

In the Web 7.0 citizen identity model, the following items may constitute personal data:

| Item | Category | Notes |
|------|----------|-------|
| secp256k1 private key | Operative | Enables signing. MUST be zeroed. |
| Primary DID | Structural | May be pseudonymous. Retained in Deactivated state. |
| DID Document | Structural | Contains public key only. Retained. |
| Verifiable Credentials | Structural | Revoked, but records retained. |
| UTXO records | Structural | Financial records. Retained. |
| Merkle log entries | Structural | Audit records. Retained. |
| Citizen registration record | Structural | Retained; encrypted private key zeroed. |
| Additional DIDs | Structural | DID Documents deactivated; public keys retained. |

Implementations MUST NOT treat the DID itself as personal data requiring deletion. The DID
is a pseudonymous identifier; its deletion from the DID Document registry would corrupt the
audit trail and break the Merkle log integrity invariant.

---

## 5. Erasure Authorisation Protocol

### 5.1 Erasure Request Submission

The Data Subject MUST submit an erasure request to the Controller (Society) through a
documented channel. The Controller MUST verify the requester's identity (matching to the
registered Citizen) before forwarding the request to the Foundation for signing.

### 5.2 Erasure Commitment Construction

The Foundation MUST construct an erasure commitment string:

```
ERASE:{citizenDid}:{requestTimestamp}
```

where:
- `{citizenDid}` is the primary DID of the Citizen.
- `{requestTimestamp}` is the ISO 8601 UTC timestamp of the Foundation's authorisation
  decision, at second precision minimum.

The Foundation MUST sign the UTF-8 encoding of this string with its secp256k1 governance key:

```
commitment-bytes = UTF-8("ERASE:{citizenDid}:{requestTimestamp}")
signature-bytes  = secp256k1-sign(SHA-256(commitment-bytes), foundation-private-key)
Signature        = "0B" + base64url-nopad(signature-bytes)
```

The Foundation delivers the `citizenDid`, `requestTimestamp`, and `Signature` to the Controller.

### 5.3 Commitment Validation

Upon receiving the erasure commitment, the Controller MUST:

1. Verify the signature against the Foundation public key:
   ```
   verify(SHA-256(UTF-8("ERASE:{citizenDid}:{requestTimestamp}")), Signature, foundation-pubkey)
   ```
   If verification fails, raise `SignatureVerificationError` and abort.

2. Verify freshness: `|UtcNow - requestTimestamp| ≤ 10 minutes`. If stale, raise
   `StaleRequestError` and abort.

3. Verify the citizen exists in the identity registry.

4. Proceed with the erasure procedure (Section 6).

The 10-minute freshness window prevents replay of a valid erasure commitment against a
different subject or at a different time. The Foundation MUST NOT issue the same erasure
commitment twice; each commitment is a single-use authorisation.

---

## 6. Erasure Procedure

The erasure procedure MUST execute the following steps atomically where possible. If any step
fails, the completed steps MUST NOT be rolled back — partial erasure is preferable to no
erasure, as rollback would reinstate operative access for the subject.

### Step 1 — Validate Authorisation

Validate the erasure commitment per Section 5.3. Abort if validation fails.

### Step 2 — Deactivate Primary DID

Set `DidStatus = Deactivated` in the DID Document registry for the citizen's primary DID.
Deactivation is permanent and MUST NOT be reversible. The DID Document record MUST be retained
in the registry.

If the citizen holds additional DIDs under other method names, each additional DID Document
MUST also be deactivated.

**Effect**: The citizen's identity is no longer resolvable as Active. Any system that checks
DID status before accepting a transfer request will reject requests from this DID.

### Step 3 — Revoke Active Verifiable Credentials

Enumerate all Verifiable Credentials with `SubjectDid = citizenPrimaryDid` and
`Status = Active`. For each Active VC:
- Set `Status = Revoked`.
- Set `RevokedAt = UtcNow`.
- Set `RevocationReason = "GDPR Article 17 erasure"`.

VC records MUST be retained in the VC registry. Only the Status field is changed.

**Effect**: All credentials attesting to the citizen's membership, endowment, and transfers
are revoked. Verifiers will be informed that these credentials are no longer valid.

### Step 4 — Zero the Private Key

Retrieve the citizen's stored secp256k1 private key (stored encrypted in the citizen record).
Overwrite the encrypted key field with 48 bytes of cryptographically random data, prefixed
with the sentinel string `"BURNED:"`:

```
EncryptedPrivateKeyBase64 = "BURNED:" + base64(CSPRNG(41))
```

The 41-byte random suffix ensures the total overwritten value is 48 bytes after base64 encoding.
The `"BURNED:"` prefix enables automated audit tools to verify that key zeroing has occurred.

After zeroing:
- The citizen's private key is permanently unrecoverable.
- The citizen cannot sign new transfer requests.
- The citizen cannot create new DID Documents.

**Effect**: Operative access is permanently revoked. The identity is inoperable.

### Step 5 — Append GdprErasure Log Entry

Append a `GdprErasure` entry to the Merkle log:

```json
{
  "entryType":    "GdprErasure",
  "timestamp":    "<ISO-8601-UTC>",
  "deploymentDid":"<societyDid>",
  "subjectDid":   "<citizenPrimaryDid>",
  "requestedAt":  "<requestTimestamp>",
  "completedAt":  "<UtcNow>"
}
```

This entry is the permanent record that an erasure was performed. It does not contain the
private key, any credential content, or any personal information beyond the DID and timestamps.

---

## 7. What MUST Be Retained

The following data MUST be retained after erasure to preserve audit integrity:

| Data | Retention reason |
|------|-----------------|
| Citizen record (with zeroed key) | Evidence of registration; needed to verify erasure completion |
| DID Document records (Deactivated) | Historical DID resolution; Merkle log integrity |
| UTXO records | Financial transaction history; legal retention requirements |
| Revoked VC records | Credential history; disputes about past credential validity |
| Merkle log entries | Tamper-evident audit trail; cannot be deleted without corrupting root |
| GdprErasure Merkle log entry | Permanent record that erasure was performed |
| SocietyOverdraftRecord | Society accounting; not citizen personal data |

### 7.1 Consistency with GDPR Principles

GDPR Article 5(1)(e) (storage limitation) requires that personal data not be kept in a form
that permits identification longer than necessary for the purpose for which it is processed.
The following analysis applies to the retained structural data:

- **UTXO records**: The purpose is financial record-keeping. Retention is necessary and
  proportionate. Deployers MUST comply with applicable financial record retention laws.
- **DID (pseudonymous)**: The DID is a pseudonymous identifier derived from a public key.
  The private key is zeroed; the DID can no longer be linked to the subject's actions.
- **Merkle log entries**: The purpose is tamper-evidence. Retention is necessary and
  proportionate to the integrity requirement.

---

## 8. What MUST NOT Be Retained

| Data | Disposition |
|------|------------|
| secp256k1 private key | MUST be zeroed (Section 6, Step 4) |
| Any decrypted copy of the private key | MUST be zeroed from memory and not written to disk |
| Erasure commitment signature (used once) | SHOULD be discarded after Step 1 validation |

---

## 9. Disclosure Requirements at Registration Time

Deployers MUST inform Data Subjects of the following limitations at citizen registration time,
before any personal data is collected:

1. **Audit log retention**: The citizen's DID and transaction references will be recorded in
   an append-only Merkle audit log that cannot be modified or deleted.

2. **UTXO retention**: UTXO records documenting the citizen's wallet history will be retained
   for the duration required by applicable financial record retention law.

3. **Erasure effect**: Exercising the right to erasure will render the citizen's identity
   permanently inoperable (private key zeroed, DID deactivated, VCs revoked) but will not
   remove historical records from the audit log or UTXO ledger.

4. **DID retention**: The DID Document (in Deactivated state) will be retained in the registry.

Deployers SHOULD obtain explicit, informed consent acknowledging these limitations at
registration time. Deployers MUST NOT represent erasure as equivalent to full data deletion.

---

## 10. Erasure Confirmation

After completing the erasure procedure, the Controller MUST issue an Erasure Confirmation to
the Data Subject containing:
- The citizen DID that was erased.
- The timestamp of each completed step.
- A reference to the `GdprErasure` Merkle log entry.
- A statement that the private key has been zeroed and the identity is permanently inoperable.
- A statement of what data has been retained and why.

---

## 11. Relationship to GDPR Supervisory Authority Requirements

Deployers MUST maintain records of erasure requests and their outcomes for a period no less
than the applicable supervisory authority's audit retention requirement (commonly 3 years).
The `GdprErasure` Merkle log entry provides a tamper-evident record of the erasure event.
Deployers SHOULD supplement this with a separate erasure request log containing the Data
Subject's identity verification evidence and the Foundation's authorisation commitment.

---

## 12. Security Considerations

### 12.1 Replay Prevention
The 10-minute freshness window (Section 5.3) prevents an erasure commitment from being replayed
after its authorisation window. Implementations MUST maintain a record of processed erasure
commitments within this window to prevent replay within the window.

### 12.2 Key Zeroing Verification
After zeroing, the implementation MUST verify that the stored key field begins with the
`"BURNED:"` sentinel. This provides an automated audit check. Implementations SHOULD expose
a periodic audit operation that scans citizen records for the `"BURNED:"` sentinel in all
records flagged as erased.

### 12.3 Memory Zeroing
Implementations MUST zero any in-memory copies of the private key after use. In managed runtimes
(e.g., .NET, JVM), explicit buffer zeroing (`Array.Clear`) MUST be used because garbage
collection may not release memory immediately. The zeroing MUST occur in a `finally` block
to ensure it executes even on exception paths.

### 12.4 Foundation Key in Erasure Path
The Foundation's secp256k1 private key is used to sign erasure commitments. Its compromise
would allow an attacker to authorise fraudulent erasures. The Foundation Key MUST be subject
to the same HSM-level protection required for governance operations.

---

## 13. Privacy Considerations

### 13.1 DID as Personal Data
Whether a pseudonymous DID constitutes personal data under GDPR depends on whether the data
subject can be identified from it. If the DID is derived from a public key and no linkage
table exists between the DID and real-world identity, it may be pseudonymous under GDPR
Recital 26. Deployers MUST assess this on a case-by-case basis.

### 13.2 GdprErasure Merkle Entry
The `GdprErasure` entry contains the erased citizen's DID. If the DID is personal data in the
deployer's jurisdiction, the entry itself constitutes personal data. Deployers MUST restrict
access to the Merkle log accordingly and MUST NOT disclose `GdprErasure` entries except as
required by law.

---

## 14. IANA Considerations

This document has no IANA actions.

---

## 15. References

### Normative
- [RFC2119] Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174] Leiba, B. Ambiguity of Uppercase vs Lowercase. May 2017.
- [GDPR] European Parliament and Council. General Data Protection Regulation (EU) 2016/679. April 2016.
- [GDPR-17] GDPR Article 17 — Right to erasure ('right to be forgotten').

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
