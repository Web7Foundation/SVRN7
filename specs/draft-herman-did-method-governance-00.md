# Self-Service DID Method Name Governance for Federated Digital Societies
# draft-herman-did-method-governance-00
# Author: M. Herman, Web 7.0 Foundation
# Published: April 2026

Internet-Draft: draft-herman-did-method-governance-00
Published:      April 2026
Expires:        October 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Web 7.0 Foundation Governance Council
Related:        draft-herman-web7-society-architecture-00
                draft-herman-svrn7-monetary-protocol-00
                draft-herman-did-w3c-drn-00

---

## Abstract

This document specifies a self-service DID method name governance model for federated digital
societies. In this model, a Federation maintains a collision-free registry of DID method names,
and member Societies may register and deregister method names on a self-service basis without
requiring Federation approval. The specification defines the method name syntax constraints, the
registration and deregistration protocol, the dormancy lifecycle, the immutability rule for
primary method names, and the forward-only resolution guarantee. The model is applicable to any
federated identity ecosystem in which multiple communities require distinct DID method names and
a shared authority must prevent namespace collision while preserving community sovereignty.

---

## 1. Introduction

The W3C DID Core specification [W3C.DID-CORE] defines the DID method name as the second
component of a DID, matching the grammar `method-name = 1*method-char` where
`method-char = %x61-7A / DIGIT` (lowercase a–z or 0–9). The specification does not define how
method names are allocated, governed, or reclaimed in a federated environment.

In the Web 7.0 digital society ecosystem [WEB70-ARCH], multiple Societies operate under a single
Federation. Each Society requires one or more DID method names to issue DIDs to its Citizens.
The Federation must prevent two Societies from using the same method name (which would make DID
resolution ambiguous) while preserving each Society's freedom to choose and manage its own
namespace.

### 1.1 Design Goals

This specification is designed to satisfy the following goals:

1. **Syntax conformance**: Method names must conform to the W3C DID Core syntax constraint.
2. **Collision prevention**: No two Active method names may belong to different Societies at
   the same time.
3. **Society sovereignty**: A Society may register additional method names without Federation
   approval.
4. **Primary name stability**: A Society's primary method name cannot be removed, ensuring that
   its own DID (issued under that name) remains stable.
5. **Forward-only resolution**: Deregistering a method name does not invalidate DIDs previously
   issued under it.
6. **Auditability**: All registration and deregistration events are recorded permanently.

### 1.2 Scope

This document specifies:
1. The method name syntax rule.
2. The registration protocol.
3. The deregistration and dormancy lifecycle.
4. The primary method name immutability rule.
5. The forward-only resolution guarantee.
6. The Federation registry data model.
7. Conformance requirements.

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

---

## 3. Terminology

- **Method Name**: The second component of a W3C DID. Must match `[a-z0-9]+`.
- **Federation**: The top-level governance entity that maintains the DID method name registry.
- **Society**: A digital community registered with the Federation. May own one or more method names.
- **Primary Method Name**: The method name under which a Society's own DID was formed at
  registration time. Immutable for the lifetime of the Society.
- **Additional Method Name**: Any method name registered by a Society after its primary method name.
- **Registry**: The Federation's persistent store of method name records.
- **Active**: A method name currently registered to a Society. New DIDs may be issued under an
  Active name.
- **Dormant**: A method name that has been deregistered and is within its dormancy period.
  New DID issuance is blocked. Re-registration is blocked until dormancy expires.
- **Available**: A method name that has never been registered, or whose dormancy period has
  expired. May be registered by any Society.
- **Dormancy Period**: The interval after deregistration during which a method name remains
  Dormant. Default: 30 days.
- **DID Method Record**: The registry entry for a method name, including its state, owning
  Society, registration timestamp, and dormancy expiry (if applicable).

---

## 4. Method Name Syntax

### 4.1 Normative Rule

A conformant method name MUST match the following ABNF [RFC5234]:

```abnf
method-name  = 1*method-char
method-char  = %x61-7A / DIGIT   ; lowercase a-z or 0-9
```

This rule is derived directly from the W3C DID Core specification [W3C.DID-CORE] Section 8.1.

### 4.2 Prohibited Characters

The following characters are explicitly prohibited in method names:

| Character | Reason |
|-----------|--------|
| Uppercase A–Z | W3C DID Core prohibits uppercase method names |
| Hyphen `-` | Not in `method-char` grammar |
| Underscore `_` | Not in `method-char` grammar |
| Dot `.` | Not in `method-char` grammar |
| Non-ASCII | Not in `method-char` grammar |

### 4.3 Validation

Implementations MUST validate method names against the regular expression `^[a-z0-9]+$` at
registration time. A registration request carrying a non-conformant method name MUST be rejected
with `InvalidMethodNameError`.

### 4.4 Examples

Valid method names: `drn`, `socalpha`, `socbeta`, `socalphahealth`, `alpha1`, `web7`

Invalid method names: `soc-alpha` (hyphen), `SocAlpha` (uppercase), `soc_alpha` (underscore),
`soc.alpha` (dot), `socalpha health` (space)

---

## 5. Registry Data Model

The Federation MUST maintain a persistent registry of DID Method Records. Each record MUST
contain the following fields:

| Field | Type | Description |
|-------|------|-------------|
| MethodName | string | The method name. Immutable after creation. |
| SocietyDid | string | DID of the owning Society. |
| IsPrimary | bool | True if this is the Society's primary method name. |
| Status | enum | `Active`, `Dormant`. Available names have no record. |
| RegisteredAt | datetime | UTC timestamp of initial registration. |
| DeregisteredAt | datetime? | UTC timestamp of deregistration. Null if Active. |
| DormantUntil | datetime? | UTC timestamp after which the name becomes Available again. Null if Active. |

### 5.1 Availability Check

Method name availability MUST be determined as follows:

```
if no record exists for methodName:
    status = Available
else if record.Status == Active:
    status = Active (owned by record.SocietyDid)
else if record.DormantUntil > UtcNow:
    status = Dormant
else:
    status = Available  (dormancy expired)
```

This check is purely time-based. No background cleanup job is required to transition Dormant
records to Available — the check at query time is sufficient.

### 5.2 Record Permanence

DID Method Records MUST never be physically deleted. The permanence of records enables:
- A complete audit trail of which Society owned a method name and when.
- Determination of dormancy expiry without external state.
- Resolution of historical DIDs issued under deregistered names.

---

## 6. Registration Protocol

### 6.1 Primary Method Name Registration

A Society's primary method name is registered as part of Society registration with the Federation.
This is a privileged operation: the Federation MUST validate that:
1. The method name is syntactically valid (`^[a-z0-9]+$`).
2. No Active record exists for the method name.
3. If a Dormant record exists, `DormantUntil ≤ UtcNow` (dormancy has expired).
4. The Society's DID is well-formed and the Society is not already registered.

The primary method name is recorded with `IsPrimary = true`.

### 6.2 Additional Method Name Registration

A Society MAY register additional method names at any time on a self-service basis. No
Federation or Foundation signature is required. The Society MUST be Active at the time of
registration. The Federation MUST validate that:
1. The method name is syntactically valid.
2. No Active record exists for the method name (owned by any Society, including the requesting Society).
3. If a Dormant record exists, `DormantUntil ≤ UtcNow`.

**Failure cases:**

| Condition | Error |
|-----------|-------|
| Method name fails `^[a-z0-9]+$` | `InvalidMethodNameError` |
| Method name Active under this Society | `DuplicateMethodNameError` |
| Method name Active under another Society | `DuplicateMethodNameError` (with owning Society DID) |
| Method name Dormant | `DormantMethodNameError` (with `DormantUntil` timestamp) |

### 6.3 Registration Record

Upon successful registration, the Federation MUST:
1. Create a DID Method Record with `Status = Active`, `RegisteredAt = UtcNow`.
2. Append a `DidMethodRegistration` entry to the Federation's Merkle log.

---

## 7. Deregistration and Dormancy Protocol

### 7.1 Primary Method Name Protection

A Society's primary method name (`IsPrimary = true`) MUST NOT be deregistered under any
circumstances. Implementations MUST enforce this with a permanent, non-overridable constraint.
A deregistration request targeting a primary method name MUST be rejected with
`PrimaryMethodNameError`.

This constraint exists because the Society's own DID is formed under its primary method name.
Deregistering the primary name would render the Society's identity unresolvable, breaking
the entire trust chain for all Citizens registered under that Society.

### 7.2 Deregistration of Additional Method Names

A Society MAY deregister any of its additional method names. The deregistration MUST:
1. Verify the method name is Active and owned by the requesting Society.
2. Verify the method name is not the Society's primary method name.
3. Update the record: `Status = Dormant`, `DeregisteredAt = UtcNow`,
   `DormantUntil = UtcNow + DormancyPeriod`.
4. Append a `DidMethodDeregistration` entry to the Federation's Merkle log.

**Failure cases:**

| Condition | Error |
|-----------|-------|
| Method name not found | `MethodNameNotFoundError` |
| Method name not owned by requesting Society | `UnauthorizedError` |
| Method name is primary | `PrimaryMethodNameError` |
| Method name already Dormant | `AlreadyDormantError` |

### 7.3 Dormancy Period

The dormancy period MUST default to 30 days. Implementations MAY make the dormancy period
configurable at the Federation level, but it MUST NOT be set to zero.

The dormancy period serves two purposes:
1. It prevents a Society from immediately reclaiming a name it just deregistered, which would
   allow it to rebrand in a way that could mislead parties who cached the old DID Document.
2. It gives external systems time to notice the deregistration and update their records before
   a different Society claims the name.

### 7.4 Forward-Only Resolution Guarantee

The deregistration of a method name MUST NOT invalidate any DID previously issued under that
name. The following guarantees MUST hold after deregistration:

1. All DID Documents created under the deregistered name MUST remain in the registry.
2. All DID Documents MUST remain resolvable via the DID Document registry (they are stored in
   the registry, not deleted).
3. `DidStatus` of existing DID Documents is unchanged by deregistration.
4. New DID issuance under the deregistered name is blocked (the method name is Dormant or
   Available, neither of which permits issuance).

This guarantee protects Citizens: their DIDs and Verifiable Credentials remain valid even if
the Society later deregisters the method name under which their DID was issued.

---

## 8. Multi-Method-Name Citizens

A Citizen may hold DIDs under multiple method names owned by their Society. This is useful
when a Society operates distinct namespaces for different contexts — for example, a general
identity method and a health-specific method.

### 8.1 Additional DID Assignment

A Society MAY assign an additional DID to a Citizen under any Active method name it owns.
The additional DID MUST be derived from the same secp256k1 public key as the primary DID,
ensuring that the identifier portion is consistent:

```
primary DID:   did:{primaryMethod}:{base58PubKey}
additional DID: did:{additionalMethod}:{base58PubKey}
```

### 8.2 Primary DID Resolution

All additional DIDs MUST resolve to the same primary DID record. Implementations MUST maintain
a join table mapping additional DIDs to their primary DID. All wallet operations and balance
queries MUST use the primary DID.

### 8.3 Normalisation

Step 0 of the transfer validation pipeline [DRAFT-MONETARY] MUST normalise any incoming DID
(primary or additional) to its primary form before any other validation step. This ensures that
a citizen holding DIDs under multiple method names is treated uniformly across all operations.

---

## 9. DID Method Status Queries

The Federation MUST expose the following query operations on the method name registry:

### 9.1 GetMethodStatus(methodName) → DidMethodStatus

Returns the current status of a method name:

| Return value | Meaning |
|-------------|---------|
| `Active` | Method name is registered to a Society. Returns `SocietyDid`. |
| `Dormant` | Method name was deregistered and is in dormancy. Returns `DormantUntil`. |
| `Available` | Method name has no record or dormancy has expired. |

### 9.2 GetAllMethods(societyDid?, statusFilter?) → List[DIDMethodRecord]

Returns all method name records, optionally filtered by owning Society DID and/or status.
Supports enumeration of all methods owned by a Society, or all Active methods in the Federation.

---

## 10. Merkle Log Integration

All method name governance events MUST be recorded in the Federation's Merkle log [RFC6962]:

| Event | Trigger |
|-------|---------|
| `DidMethodRegistration` | Successful registration of any method name (primary or additional) |
| `DidMethodDeregistration` | Successful deregistration of an additional method name |

Each log entry MUST include: `MethodName`, `SocietyDid`, `IsPrimary`, `Action`, `Timestamp`.

---

## 11. Cross-Registry Interoperability

### 11.1 Federation-to-Federation Resolution

This specification does not address DID method name governance across multiple Federations.
If two independent Federations each register the same method name for different Societies,
DID resolution must be scoped to a specific Federation to be unambiguous. Cross-Federation
resolution is outside the scope of this document.

### 11.2 Relationship to the W3C DID Specification Registries

Method names governed by this specification exist within the Federation's private registry.
They are not automatically registered in the W3C DID Specification Registries
[W3C.DID-SPEC-REGISTRIES]. Operators who wish their method names to be resolvable outside
their Federation SHOULD submit them to the W3C DID Specification Registries separately.

---

## 12. Security Considerations

### 12.1 Method Name Squatting
The self-service nature of registration means a Society could register method names that
resemble names of other Societies (e.g., `socgoogle`, `socmicrosoft`). The Federation MUST
enforce a first-come-first-served policy without any semantic approval mechanism. Social and
legal mechanisms for trademark protection are outside the scope of this specification.

### 12.2 Primary Method Name Hijacking
A Federation implementation bug that allows a primary method name to be deregistered would
render the owning Society's DID unresolvable. Implementations MUST apply the `IsPrimary`
constraint as a database-level check, not only as application-level logic.

### 12.3 Dormancy Bypass
If the dormancy check uses wall-clock time and the Federation's clock is compromised, an
attacker could claim that a name's dormancy has expired prematurely. Implementations MUST use
a monotonic or NTP-synchronised clock source for dormancy checks.

---

## 13. Privacy Considerations

The method name registry reveals the set of DID method names operated by each Society. This
is public information by design — the registry is the mechanism by which DID resolution is
routed correctly. Operators with confidentiality requirements SHOULD NOT register method names
that reveal sensitive organisational information.

---

## 14. IANA Considerations

This document has no IANA actions.

---

## 15. References

### Normative
- [RFC2119] Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174] Leiba, B. Ambiguity of Uppercase vs Lowercase. May 2017.
- [RFC5234] Crocker, D. Augmented BNF for Syntax Specifications: ABNF. January 2008.
- [RFC6962] Laurie, B. et al. Certificate Transparency. June 2013.
- [W3C.DID-CORE] Sporny, M. et al. Decentralized Identifiers (DIDs) v1.0. W3C Recommendation, 2022.

### Informative
- [WEB70-ARCH] Herman, M. Web 7.0 Digital Society Architecture. draft-herman-web7-society-architecture-00.
- [DRAFT-MONETARY] Herman, M. SOVRONA (SVRN7) Monetary Transfer Protocol. draft-herman-svrn7-monetary-protocol-00.
- [DRAFT-DID-DRN] Herman, M. Decentralized Resource Name (DRN) DID Method. draft-herman-did-w3c-drn-00.
- [W3C.DID-SPEC-REGISTRIES] Sporny, M. and O. Steele. DID Specification Registries. W3C Working Group Note, 2023.

---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
URI: https://hyperonomy.com/about/
