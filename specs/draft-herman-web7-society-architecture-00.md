# Web 7.0 Digital Society Architecture
# draft-herman-web7-society-architecture-00
# Author: M. Herman, Web 7.0 Foundation
# Published: April 2026

Internet-Draft: draft-herman-web7-society-architecture-00
Published:      April 2026
Expires:        October 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Web 7.0 Foundation Governance Council
Related:        draft-herman-svrn7-monetary-protocol-00
                draft-herman-didcomm-svrn7-transfer-00
                draft-herman-did-w3c-drn-00
                draft-herman-vtc-proof-sets-01

---

## Abstract

This document specifies the Web 7.0 Digital Society Architecture — a framework for federated
communities of digital entities (people, organisations, and autonomous agents) that participate
in a common digital economy, governance structure, and identity ecosystem. The architecture
defines three hierarchical tiers: the Federation (top-level monetary and governance authority),
Societies (communities that register with the Federation and onboard citizens), and Citizens
(individuals who hold a self-sovereign identity and a wallet seeded with the Shared Reserve
Currency). The document specifies the obligations, rights, and protocol interfaces of each tier,
the DID-based identity model, the Verifiable Credential lifecycle, and the eleven governing
architectural principles that constrain all conformant implementations.

---

## 1. Introduction

Web 7.0 is a decentralised society architecture in which digital communities — nation states,
churches, sports associations, political parties, guilds, clans, and any other form of organised
group — operate as sovereign digital entities called Societies. Each Society is a member of a
Federation. The Federation governs the monetary supply, the DID method name namespace, and the
epoch-based monetary policy lifecycle. Citizens are the individuals who belong to Societies and
hold identities and wallets within the ecosystem.

### 1.1 Motivation

Prior decentralised identity and trust frameworks have addressed identity management (W3C DID),
credential issuance (W3C VC), and peer-to-peer communication (DIDComm). However, no existing
framework specifies how these primitives combine into a complete digital society with:

- A shared reserve currency (SVRN7) governed by a federation of digital societies.
- Self-service DID method name governance for each Society.
- A three-tier identity and wallet hierarchy.
- Cross-Society transfer protocols that preserve audit integrity without a shared ledger.
- GDPR-compliant identity lifecycle management.

This document provides that foundation. All other Web 7.0 specifications reference this document
as their architectural anchor.

### 1.2 Scope

This document specifies:
1. The three-tier hierarchy: Federation, Society, Citizen.
2. The obligations and rights of each tier.
3. The DID-based identity model and method name governance.
4. The Verifiable Credential lifecycle within the ecosystem.
5. The Merkle audit log architecture.
6. The eleven governing architectural principles.
7. Conformance requirements for Federation and Society implementations.

This document does not specify:
- The monetary transfer protocol (see [DRAFT-MONETARY]).
- DIDComm message packaging (see [DRAFT-DIDCOMM-TRANSFER]).
- The DID method specification for `did:drn` (see [DRAFT-DID-DRN]).
- Verifiable Trust Circle specifications (see [DRAFT-VTC]).

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

---

## 3. Terminology

- **Web 7.0**: The decentralised society architecture defined by this and related specifications.

- **Federation**: The top-level governance entity in a Web 7.0 deployment. There is exactly one
  Federation per deployment. The Federation holds the genesis wallet, governs the DID method name
  registry, and controls monetary policy (epoch advancement, supply updates).

- **Society**: A digital community registered with the Federation. A Society holds a wallet
  funded by the Federation endowment, operates its own embedded databases and Merkle log, and
  onboards Citizens by issuing them DIDs and endowment wallets.

- **Citizen**: An individual member of a Society. A Citizen holds a primary DID, a wallet
  seeded with the citizen endowment, and one or more Verifiable Credentials attesting to their
  membership and endowment.

- **Foundation**: The organisational entity (Web 7.0 Foundation) that holds the Foundation
  governance key pair. The Foundation key authorises epoch advancement, supply updates, and
  GDPR erasure requests.

- **Foundation Key**: The secp256k1 key pair whose public key is embedded in the Federation
  deployment configuration. Used to sign governance operations.

- **SRC (Shared Reserve Currency)**: SVRN7 (SOVRONA) — the sole monetary unit of the Web 7.0
  ecosystem. Digital Societies do not issue local currencies.

- **DID Method Name**: The second component of a W3C DID (e.g., `drn` in `did:drn:abc`).
  Each Society owns one or more DID method names. Method names MUST match `[a-z0-9]+`.

- **Primary DID**: The DID assigned to an entity (Citizen, Society, or Federation) at
  registration time. The primary DID is the wallet key — all balance lookups use it.

- **Merkle Log**: An RFC 6962 Certificate Transparency append-only log maintained by each
  Federation and Society deployment independently.

- **VTC (Verifiable Trust Circle)**: A multi-party Verifiable Credential expressing membership
  or governance relationships (see [DRAFT-VTC]).

---

## 4. The Three-Tier Hierarchy

```
Federation  (1 per deployment)
│   Genesis wallet: 10^15 grana
│   DID method name registry
│   Epoch governance
│   Supply governance
│   Foundation key verification
│
├── Society A  (1..N per Federation)
│   │   Society wallet: funded by Federation endowment
│   │   DID method names: e.g. "socalpha", "socalphahealth"
│   │   Own svrn7.db, svrn7-dids.db, svrn7-vcs.db
│   │   Own Merkle log
│   │
│   ├── Citizen 1  (1..N per Society)
│   │   DID: did:socalpha:citizen1
│   │   Wallet: 1,000 SVRN7 endowment
│   │   Verifiable Credentials: EndowmentVC, MembershipVC
│   │
│   └── Citizen N
│
└── Society B
    ...
```

### 4.1 Federation

The Federation is the root of trust for the entire ecosystem. It MUST:
- Maintain the genesis wallet containing the initial supply of 10^15 grana.
- Maintain the DID method name registry — a collision-free registry of method names and their
  owning Societies.
- Verify and execute epoch advancement requests signed by the Foundation Key.
- Verify and execute supply update requests signed by the Foundation Key.
- Verify and execute GDPR erasure requests signed by the Foundation Key.
- Respond to Society overdraft draw requests via DIDComm.
- Maintain a Federation-level Merkle log recording all governance-level events.

The Federation MAY:
- Operate multiple Societies directly.
- Provide DID Document resolution for method names it manages directly.

### 4.2 Society

A Society is a registered member of the Federation. It MUST:
- Maintain a Society wallet funded by a Federation endowment transfer.
- Maintain its own embedded databases (svrn7.db, svrn7-dids.db, svrn7-vcs.db).
- Maintain its own Merkle log recording all Society-level events.
- Own at least one DID method name (its primary method name, which is immutable).
- Issue Citizens a primary DID under one of its owned method names.
- Issue Citizens a wallet containing 1,000 SVRN7 at registration.
- Issue `Svrn7EndowmentCredential` and `Svrn7VtcCredential` VCs to newly registered Citizens.
- Process incoming DIDComm transfer messages.
- Maintain an overdraft record tracking drawn and outstanding grana.

A Society MAY:
- Register additional DID method names on a self-service basis (no Foundation signature required).
- Deregister non-primary method names (entering a 30-day dormancy period by default).
- Allow Citizens to hold additional DIDs under other Society method names.

### 4.3 Citizen

A Citizen is an individual member of a Society. Upon registration, a Citizen:
- MUST receive a primary DID under one of the Society's owned method names.
- MUST receive a wallet seeded with exactly `CitizenEndowmentGrana` (1,000 SVRN7).
- MUST receive a `Svrn7EndowmentCredential` VC from the Society.

A Citizen MAY:
- Hold additional DIDs under other method names registered to the Society.
- Transfer SVRN7 subject to the current epoch matrix.
- Exercise GDPR Article 17 erasure rights via the Foundation.

---

## 5. Identity Model

### 5.1 DID Structure

Every entity in the Web 7.0 ecosystem MUST be identified by a W3C Decentralized Identifier
[W3C.DID-CORE]. The general form is:

```
did:{methodName}:{identifier}
```

where `{identifier}` is the Base58btc encoding of the entity's secp256k1 public key.

### 5.2 DID Method Name Governance

DID method names are governed by the Federation's DID method name registry. The following
rules MUST be enforced:

1. **Syntax**: Method names MUST match the regular expression `[a-z0-9]+`. Hyphens, underscores,
   uppercase letters, and non-ASCII characters are not permitted. This constraint is derived from
   the W3C DID Core specification Section 8.1 [W3C.DID-CORE].

2. **Uniqueness**: Each method name MUST be registered to exactly one Society at any given time.
   The Federation MUST reject registration of a name that is currently Active under another Society.

3. **Self-service**: A Society MAY register additional method names without Foundation approval.
   Registration requires only that the name is syntactically valid and not currently Active.

4. **Primary method name**: The method name under which a Society's own DID was formed at
   registration time is the primary method name. The primary method name MUST NOT be deregistered.
   Implementations MUST enforce this with a permanent, non-overridable constraint.

5. **Dormancy**: A deregistered method name enters a dormancy period (default: 30 days) during
   which it cannot be re-registered by any Society. After dormancy expires, the name becomes
   available again. Dormancy records MUST be retained permanently.

6. **Forward-only**: Deregistration is permanent in the sense that existing DIDs issued under
   a deregistered name remain valid and resolvable indefinitely. Only new DID issuance under
   the name is blocked.

### 5.3 DID Document Registry

Each Society and the Federation MUST maintain its own DID Document registry (svrn7-dids.db).
DID Documents are versioned; version numbers are monotonically increasing integers. Deactivation
is permanent — once `Status = Deactivated`, no further updates are permitted.

### 5.4 DID Document Resolution Routing

A conformant resolver MUST route DID Document resolution by method name:

- **Local method names** (in the deployment's configured `DidMethodNames`): Resolved from the
  local DID Document registry without any network hop.
- **Foreign method names**: The resolver MUST look up the owning Society in the Federation
  method name registry, dispatch a DIDComm `did/1.0/resolve-request` message to the owning
  Society, and return the response.

If the owning Society does not respond within the configured timeout, the resolver MUST return
`errorCode = "resolutionTimeout"` rather than blocking indefinitely.

### 5.5 Multi-DID Citizens

A Citizen MAY hold DIDs under multiple method names registered to their Society. All additional
DIDs resolve to the same primary DID record. Wallet lookups MUST always use the primary DID.
Step 0 of the transfer validation pipeline [DRAFT-MONETARY] normalises any incoming DID to its
primary form before any other validation occurs.

---

## 6. Verifiable Credential Lifecycle

### 6.1 Credential Types

The following credential types are issued within the Web 7.0 ecosystem:

| Type | Issuer | Subject | Issued when |
|------|--------|---------|-------------|
| `Svrn7EndowmentCredential` | Society | Citizen | Citizen registration |
| `Svrn7VtcCredential` | Federation | Society | Society registration |
| `TransferOrderCredential` | Originating Society | Target Society | Cross-Society transfer initiation |
| `TransferReceiptCredential` | Receiving Society | Originating Society | Cross-Society transfer settlement |
| `OverdraftDrawReceipt` | Federation | Society | Overdraft draw completion |

### 6.2 VC Encoding

All Verifiable Credentials MUST be encoded as signed JWTs conforming to the W3C VC Data Model
v2.0 [W3C.VC-DATA-MODEL]. The JWT MUST be signed using secp256k1 (ES256K algorithm). Required
JWT claims:

| Claim | Description |
|-------|-------------|
| `iss` | DID of the issuing Society or Federation |
| `sub` | DID of the credential subject |
| `jti` | UUID used as `VcId` |
| `iat` | Unix timestamp of issuance |
| `exp` | Unix timestamp of expiry |

### 6.3 Auto-Expiry

Implementations MUST check VC expiry on every read path. If `exp < UtcNow` and
`Status == Active`, the VC status MUST be updated to `Expired` before the record is returned to
the caller.

### 6.4 Revocation

Revocation is permanent. Once `Status = Revoked`, the VC cannot be reinstated. Revocation
events MUST be retained permanently in the VC registry.

---

## 7. Merkle Audit Log Architecture

### 7.1 Deployment Model

Each Society and the Federation MUST maintain its own independent Merkle log. There is no
shared log across the ecosystem. With N Societies and 1 Federation, the total number of Merkle
logs is N + 1.

A cross-Society transfer produces entries in two independent logs:
- The originating Society's log records the debit (`CrossSocietyTransferDebit`).
- The receiving Society's log records the credit (`CrossSocietyTransferCredit`) and the
  settlement confirmation (`CrossSocietyTransferSettled`).

The Federation's log records only governance-level events (society registrations, epoch
transitions, supply updates).

### 7.2 Hash Construction

Implementations MUST use RFC 6962 [RFC6962] hash construction:

```
leaf_hash(entry)   = SHA-256(0x00 || entry-bytes)
node_hash(l, r)    = SHA-256(0x01 || l || r)
```

The `0x00` prefix for leaf nodes and `0x01` prefix for internal nodes prevent second-preimage
attacks and leaf/internal hash collisions respectively.

### 7.3 Signed Tree Heads

The Foundation's secp256k1 governance key MUST sign periodic tree heads containing:
- `RootHash`: the current Merkle root.
- `TreeSize`: the current number of log entries.
- `Timestamp`: the UTC time of signing.
- `Signature`: CESR secp256k1 signature over the canonical JSON of the above fields.

Implementations SHOULD sign tree heads at least every 24 hours. A tree head older than 24 hours
SHOULD be reported as a health degradation condition.

### 7.4 Entry Types

| EntryType | Recorded by | Trigger |
|-----------|-------------|---------|
| `CitizenRegistration` | Society | New citizen registered |
| `SocietyRegistration` | Federation | New society registered |
| `Transfer` | Society | Successful same-Society transfer |
| `EpochTransition` | Federation | Epoch advanced by governance operation |
| `SupplyUpdate` | Federation | Total supply increased |
| `DidMethodRegistration` | Federation | New DID method name registered |
| `DidMethodDeregistration` | Federation | DID method name deregistered |
| `CrossSocietyTransferDebit` | Originating Society | Payer debited |
| `CrossSocietyTransferCredit` | Receiving Society | Payee credited |
| `CrossSocietyTransferSettled` | Originating Society | Settlement confirmed |
| `GdprErasure` | Society | GDPR Article 17 erasure completed |

---

## 8. Eleven Governing Architectural Principles

### P1 — Identity Precedes Participation
No entity participates in the Web 7.0 ecosystem without a W3C DID. The DID is not an account
number assigned by an authority; it is a self-generated cryptographic identity. Registration,
wallet creation, and VC issuance all require a valid DID.

### P2 — Trust is Cryptographic, Not Institutional
Claims are accepted because they carry valid cryptographic proofs, not because they originate
from a trusted institution. Every transfer is validated by signature (Step 6). Every governance
operation is validated by Foundation Key signature.

### P3 — Supply Conservation is an Invariant
At any moment: SUM(all unspent UTXOs) + Federation unallocated balance = TotalSupplyGrana.
This invariant MUST be enforced mechanically by the UTXO model. No synthetic grana exists.

### P4 — Audit Records Are Permanent
UTXOs, Merkle log entries, DID Document versions, VC revocation events, and DID method name
records are never deleted. GDPR erasure is implemented by zeroing private key bytes — the
structural record is retained.

### P5 — Forward-Only Operations
Supply can only increase. DID Document deactivation is permanent. VC revocation is permanent.
Epoch advancement cannot be reversed. DID method deregistration enters dormancy. These are
architectural constraints, not implementation limitations.

### P6 — Namespace Sovereignty Belongs to the Society
Each Society's DID method name is a sovereign resource. The Federation enforces uniqueness but
does not grant or revoke method names on policy grounds. Registration is self-service.

### P7 — All Cross-Society Transfers Use DIDComm SignThenEncrypt
DIDComm SignThenEncrypt (JWS wrapped in Anoncrypt JWE) is the required pack mode for all
cross-Society monetary transfers. This provides non-repudiation — the JWS signature survives
decryption and is independently verifiable by any third party.

### P8 — Standards Compliance is Normative
The ecosystem implements W3C DID v1.0, W3C VC Data Model v2.0, DIDComm v2, RFC 6962, RFC 3394,
and RFC 7748 as normative specifications. Deviation is a defect. Compliance enables
interoperability with any conformant implementation.

### P9 — Partial Availability Over Total Unavailability
In a decentralised system, some participants will always be unreachable. Cross-Society VC
fan-out returns partial results with a `TimedOutSocieties` manifest. Overdraft draw throws
`FederationUnavailableException` on timeout rather than blocking indefinitely.

### P10 — The Citizen Retains Their DID
Society deregistration cannot invalidate citizen identities. Existing DIDs issued under a
deregistered method name remain fully resolvable and valid. Citizens are not dependent on the
continued existence of the Society that issued their DID.

### P11 — Non-Repudiation for Monetary Commitments
All cross-Society monetary commitments (`TransferOrderCredential`) MUST be signed such that
the signature can be independently verified by any third party. This requires DIDComm
SignThenEncrypt (P7). Authcrypt-only authentication, which evaporates on decryption, is
insufficient for monetary commitments.

---

## 9. Conformance

### 9.1 Federation Conformance

A conformant Federation implementation MUST:
- Maintain a genesis wallet with `InitialSupplyGrana = 10^15`.
- Maintain a DID method name registry enforcing uniqueness and dormancy rules.
- Process epoch advancement requests signed by the Foundation Key.
- Process supply update requests signed by the Foundation Key.
- Process GDPR erasure requests signed by the Foundation Key.
- Respond to Society overdraft draw DIDComm messages.
- Maintain an RFC 6962 Merkle log for all governance-level events.
- Sign tree heads with the Foundation Key at least every 24 hours.
- Implement all eleven governing architectural principles.

### 9.2 Society Conformance

A conformant Society implementation MUST:
- Maintain its own embedded databases (svrn7.db, svrn7-dids.db, svrn7-vcs.db).
- Maintain its own RFC 6962 Merkle log.
- Own at least one DID method name matching `[a-z0-9]+`.
- Issue Citizens primary DIDs under owned method names.
- Execute the eight-step transfer validation pipeline per [DRAFT-MONETARY].
- Issue `Svrn7EndowmentCredential` and `Svrn7VtcCredential` VCs at registration.
- Process DIDComm SignThenEncrypt transfer messages per [DRAFT-DIDCOMM-TRANSFER].
- Implement the overdraft facility per [DRAFT-MONETARY].

### 9.3 Interoperability

A conformant Society implementation in any programming language MUST interoperate with a
conformant Federation implementation in any programming language, provided both implementations
conform to:
- The transfer protocol [DRAFT-MONETARY].
- The DIDComm transfer protocol [DRAFT-DIDCOMM-TRANSFER].
- The DID Document structure [W3C.DID-CORE].
- The VC encoding [W3C.VC-DATA-MODEL].

The reference implementation is the SOVRONA (SVRN7) .NET 8 C# library [WEB70-IMPL].

---

## 10. Security Considerations

### 10.1 Foundation Key
The Foundation Key is the root of governance trust. Its compromise would allow an attacker to
advance epochs, increase supply, and authorise fraudulent GDPR erasures. The Foundation Key
MUST be stored in a Hardware Security Module (HSM) or equivalent offline cold storage and MUST
NEVER be present in application configuration files, environment variables, or source control.

### 10.2 Society Wallet Security
Society wallets fund citizen endowments. Loss of a Society wallet's private key does not
prevent the society from operating (the wallet is a database record, not a key-controlled
account) but does affect the Society's ability to sign new transfer operations. Society signing
keys MUST be stored encrypted at rest.

### 10.3 DID Method Name Squatting
The self-service nature of DID method name registration creates the possibility of name
squatting — a Society registering method names that resemble the names of other Societies.
The Federation MUST implement a first-come-first-served policy with no policy-based
approval or denial. Dispute resolution is outside the scope of this specification.

---

## 11. Privacy Considerations

### 11.1 Transaction Visibility
Society operators have visibility into all transactions within their deployment. The Federation
has visibility into governance events and overdraft draw records but not into individual citizen
transfers. Cross-Society transfer records are visible to both originating and receiving Society
operators.

### 11.2 DID Correlation
Because DID method names are assigned by Societies, the method name portion of a citizen's DID
reveals their Society affiliation. Implementations supporting privacy-sensitive use cases SHOULD
consider allowing citizens to use additional DIDs under method names that do not reveal Society
affiliation.

---

## 12. IANA Considerations

This document has no IANA actions.

---

## 13. References

### Normative
- [RFC2119] Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174] Leiba, B. Ambiguity of Uppercase vs Lowercase in RFC 2119 Key Words. May 2017.
- [RFC6962] Laurie, B. et al. Certificate Transparency. June 2013.
- [W3C.DID-CORE] Sporny, M. et al. Decentralized Identifiers (DIDs) v1.0. W3C Recommendation, 2022.
- [W3C.VC-DATA-MODEL] Sporny, M. et al. VC Data Model v2.0. W3C Recommendation, 2024.

### Informative
- [DRAFT-MONETARY] Herman, M. SOVRONA (SVRN7) Monetary Transfer Protocol. draft-herman-svrn7-monetary-protocol-00.
- [DRAFT-DIDCOMM-TRANSFER] Herman, M. DIDComm Transfer Protocol for SVRN7. draft-herman-didcomm-svrn7-transfer-00.
- [DRAFT-DID-DRN] Herman, M. Decentralized Resource Name (DRN) DID Method. draft-herman-did-w3c-drn-00.
- [DRAFT-VTC] Herman, M. Verifiable Trust Circles using VC Proof Sets. draft-herman-vtc-proof-sets-01.
- [WEB70-IMPL] Herman, M. SOVRONA (SVRN7) .NET 8 Reference Implementation. https://github.com/web7foundation/svrn7.

---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
URI: https://hyperonomy.com/about/
