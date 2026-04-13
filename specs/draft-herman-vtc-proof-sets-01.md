# Verifiable Trust Circles (VTCs) using VC Proof Sets
# draft-herman-vtc-proof-sets-01
# Source: https://datatracker.ietf.org/doc/html/draft-herman-vtc-proof-sets-01
# Retrieved: April 2026

Internet-Draft: draft-herman-vtc-proof-sets-01
Published:      26 March 2026
Expires:        27 September 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Web 7.0 Foundation Governance Council
Datatracker:    https://datatracker.ietf.org/doc/draft-herman-vtc-proof-sets/

---

## Abstract

This document specifies Web 7.0 Verifiable Trust Circles (VTCs), a generalized mechanism for
expressing verifiable multi-party membership, belonging, and trust relationships using the W3C
Verifiable Credentials (VC) Data Model 2.0 and VC Data Integrity Proof Sets. VTCs extend the
Partof Architecture Reference Model (PARM) to provide a universal credential pattern that subsumes
prior pairwise constructs (Personhood Credentials (PHCs) and Verifiable Relationship Credentials
(VRCs)) and additionally supports voting-based decision making, meeting requests, task forces, and
digital societies.

---

## 1. Introduction

### 1.1 Motivation

- PHCs and VRCs both express a form of "belonging to" — they are specializations of the same
  universal pattern.
- The W3C VC Data Model 2.0 already provides Proof Sets as a standard mechanism for multi-party
  signing.
- A single, generalized VTC pattern — grounded in First Principles Thinking — can subsume both
  constructs and additionally support voting, community membership, digital governance, and
  inter-network trust.
- The SSC 7.0 Metamodel defines three controller layers (Beneficial, Intermediate, Technical)
  at which VTCs may apply, enabling rich composability.

### 1.2 Scope

This specification defines:
1. The VTC data model, including required and optional properties.
2. The roles of Initiator, Responder(s), and Notary within a VTC.
3. The lifecycle of a VTC Proof Set, from initial issuance through multi-party endorsement.
4. Use case profiles: self-credential, bilateral relationship, multi-party group, voting.
5. Privacy and security considerations specific to multi-party proof sets.

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are interpreted as described in BCP 14 [RFC2119] [RFC8174].

---

## 3. Terminology and Definitions

- **VTC (Verifiable Trust Circle)**: A Verifiable Credential whose credential subject identifies
  a multi-party trust relationship, and whose proof property contains a Proof Set with one proof
  contribution per participating member, plus the Notary's initial proof.
- **UMC (Universal Membership Credential)**: The generalized name for the VTC pattern when applied
  to the broader class of MemberOf, PartOf, and CitizenOf relationships.
- **Proof Set**: A set of proofs attached to a single secured document where the order of proofs
  does not matter. Each proof is contributed by a distinct signer. (W3C VC Data Integrity)
- **Initiator (A)**: The entity that proposes or originates a VTC. Identified by a DID.
  Corresponds to the "from" role.
- **Responder (B...Z)**: One or more entities that accept membership by contributing their proof
  to the Proof Set. Identified by DIDs. Corresponds to entries in the "to" array.
- **Notary (N)**: A trusted third party that issues the initial credential shell and contributes
  the first proof. Assigned to the VC "issuer" role.
- **PARM**: Partof Architecture Reference Model — the universal pattern underlying VTCs.
- **SSC 7.0 Metamodel**: Self-Sovereign Control 7.0 Metamodel — defines three controller layers.
- **DTG**: Digital Trust Graph — a graph of trust relationships between entities.
- **PHC**: Personhood Credential — a degenerate VTC where N=1.
- **VRC**: Verifiable Relationship Credential — a degenerate VTC where N=2.

---

## 4. Design Principles

### 4.1 As Simple As Possible But No Simpler
No new cryptographic primitives. The only structural addition is the deliberate use of the
proof array (Proof Set) to carry per-member proofs alongside the Notary proof.

### 4.2 First Principles Thinking
PHCs and VRCs are specializations of a single underlying relationship pattern (PARM). One
universal type covers all cases by varying cardinality of the "to" array and the Proof Set.

### 4.3 Privacy by Design
VTC credential subjects SHOULD use confidentialSubject semantics wherever selective disclosure
is required. ZKP integration in Proof Sets is explicitly supported.

### 4.4 Composability
VTCs compose at each layer of the SSC 7.0 Metamodel (Beneficial, Intermediate, Technical).

### 4.5 Cross-Network Trust
The PARM model is network-agnostic. VTCs support trust relationships across and between
independent, distinct networks and ecosystems.

---

## 5. The Partof Architecture Reference Model (PARM)

| Relationship Type | Example |
|-------------------|---------|
| MemberOf | Alice is a member of the Working Group Trust Circle. |
| PartOf | Bob is part of the study group. |
| CitizenOf | Carol is a citizen of the Digital Nation State of Sovronia. |
| EmployeeOf | Dave is an employee of Acme Corp (DID-identified). |
| ParticipantOf | Eve is a participant of the 09:00 meeting. |
| VoterFor | Frank has cast a vote for Candidate 1. |

All reduce to the same credential structure: a VC whose credentialSubject.id identifies the
group or decision entity, and whose proof array contains proofs from the Notary and each member.

---

## 6. VTC Data Model

### 6.1 Overview
A VTC is a valid W3C Verifiable Credential with:
- `issuer` — identifies the Notary (N).
- `credentialSubject.id` — identifies the relationship or group itself as a DID.
- `credentialSubject.from` — the Initiator's DID.
- `credentialSubject.to` — array of Responder DIDs.
- `proof` — array (Proof Set): Notary first, then Initiator, then Responders.

### 6.2 Minimal Pairwise VTC (N=2)
```json
{
  "@context": [
    "https://www.w3.org/ns/credentials/v2",
    "https://w3id.org/vtc/v1"
  ],
  "id": "did:envelope:1234",
  "type": ["VerifiableCredential", "VerifiableTrustCircle"],
  "issuer": "did:example:notaryabcd",
  "validFrom": "2026-01-01T00:00:00Z",
  "credentialSubject": {
    "id": "did:vrc:2468",
    "from": "did:example:alice",
    "to": ["did:example:bob"],
    "metadata": { "label": "Alice-Bob Bilateral Trust Circle" }
  },
  "proof": [
    { "id": "did:example:notaryabcd", "type": "DataIntegrityProof", "...": "<notary-proof>" },
    { "id": "did:example:alice",      "type": "DataIntegrityProof", "...": "<alice-proof>"  },
    { "id": "did:example:bob",        "type": "DataIntegrityProof", "...": "<bob-proof>"    }
  ]
}
```

### 6.3 Multi-Party VTC (N=3+)
Extend the "to" array and add one proof entry per additional Responder.

### 6.4 Self-Credential VTC (N=1, PHC Equivalent)
"from" and "to" both reference the Initiator's own DID.

### 6.5 Voting VTC
One VTC per candidate. Voters cast their vote by contributing their individual proof to the
VTC of the candidate they support. Vote count = number of valid member proofs.

### 6.6 Properties Reference

| Property | Req. | Description |
|----------|------|-------------|
| id | REQUIRED | DID identifying the VTC credential itself. |
| type | REQUIRED | MUST include "VerifiableCredential" and "VerifiableTrustCircle". |
| issuer | REQUIRED | DID of the Notary (N). |
| credentialSubject.id | REQUIRED | DID identifying the relationship or group. |
| credentialSubject.from | REQUIRED | DID of the Initiator (A). |
| credentialSubject.to | REQUIRED | Array of Responder DIDs. MAY be empty for open voting VTCs. |
| credentialSubject.metadata | OPTIONAL | Structured metadata (label, policy, expiry, etc.). |
| proof | REQUIRED | Array of proof objects (Proof Set). First proof MUST be from Notary. |
| proof[].id | REQUIRED | DID of the signer contributing this proof entry. |

---

## 7. VTC Proof Set Lifecycle

### 7.1 Phase 0 — Null VTC
Credential shell created by Notary. Only Notary proof present. t = 0.

### 7.2 Phase 1..t — Progressive Endorsement
Each Responder adds their proof using the W3C VC Data Integrity "add-proof-set-chain" algorithm.
VTC is valid for those t members who have signed.

### 7.3 Phase N — Complete VTC
All Responders have contributed their proofs. Fully executed multi-party trust relationship.

### 7.4 Adding a Proof
MUST follow W3C VC Data Integrity "add-proof-set-chain" algorithm. Proof is appended without
modifying prior proofs.

### 7.5 Proof Ordering
Proof Sets are unordered by definition. RECOMMENDED conventional order:
(1) Notary proof, (2) Initiator proof, (3) Responder proofs.

---

## 8. Roles and Participants

### 8.1 Notary (N) — Issuer
MUST be trusted by both Initiator and all Responders. Creates the credential shell and
contributes the first proof.

### 8.2 Initiator (A) — From
Proposes the trust circle. Appears in credentialSubject.from. Contributes proof to signify
acceptance.

### 8.3 Responders (B...Z) — To
Each identified in credentialSubject.to. Accepts membership by contributing individual proof.
Non-signing Responders are proposed but not yet verified members.

RULE: Cardinality t of verified members = number of valid member proofs (excluding Notary proof).

---

## 9. Use Cases

### 9.1 Bilateral Trust Relationship (VRC Equivalent)
from = Alice, to = [Bob]. Both contribute proofs.

### 9.2 Personhood Credential (PHC Equivalent)
from = Alice, to = [Alice]. Alice contributes her own proof.

### 9.3 Web 7.0 Foundation Governance Council
from = chair, to = [member1...memberN]. Members join by contributing proofs.

### 9.4 VC-Based Meeting Request
credentialSubject.id = meeting DID. Attendees RSVP by contributing proofs.

### 9.5 Voting-Based Decision Making
One VTC per candidate. Vote tally = count of valid member proofs in each candidate's VTC.

### 9.6 Verifiable Decentralized Registry
Append operations authorized through a VTC whose members are the registry trustees.

### 9.7 Digital Society / Digital Nation State
Society = VTC whose members are the citizens. Governance via subsidiary voting VTCs.

---

## 10. Conformance

A conforming VTC implementation MUST:
- Produce VTC credentials valid per W3C VC Data Model 2.0.
- Use a proof array (Proof Set) per W3C VC Data Integrity.
- Include the `issuer` property identifying the Notary.
- Include credentialSubject.id, credentialSubject.from, credentialSubject.to.
- Use "add-proof-set-chain" when adding proofs incrementally.

SHOULD:
- Include "VerifiableTrustCircle" in the type array.
- Implement selective disclosure mechanisms.

MAY:
- Extend credentialSubject.metadata with domain-specific claims.

---

## 11. Relationship to Other Specifications

- **W3C VC Data Model 2.0**: VTCs are valid W3C VCs. All normative requirements apply.
- **W3C VC Data Integrity**: VTCs rely on the Proof Set mechanism and "add-proof-set-chain".
- **ToIP DTGWG Design Principles**: Consistent with DTGWG Design Principles, DTG-ZKP Requirements.
- **SSC 7.0 Metamodel**: VTCs anchor at Beneficial, Intermediate, or Technical Controller layers.
- **Trust Spanning Protocol (TSP)**: Compatible as a credential format for channel-level membership.

---

## 12. Privacy Considerations

### 12.1 Selective Disclosure
Implementations SHOULD use confidentialSubject semantics and selective disclosure proof
mechanisms (e.g., BBS+ signatures) to allow proof of membership without revealing the full
membership list.

### 12.2 ZKP Integration
Members MAY contribute a ZKP as their proof entry. Implementations SHOULD define a profile
for ZKP-based proof entries.

### 12.3 Privacy Budget and Reconstruction Ceiling
The reconstruction ceiling MUST be maintained below the threshold defined by the applicable
trust framework.

### 12.4 Notary Trust
Verifiers MUST independently verify that the Notary is trusted by all relevant parties.
The Notary SHOULD be a well-known, community-governed DID with transparent governance.

---

## 13. Security Considerations

### 13.1 Voting Integrity
- **Eligibility**: Only eligible voters can contribute proofs.
- **Anonymity**: Voter DIDs SHOULD be anonymized or pseudonymized.
- **Non-repudiation**: Each proof is cryptographically bound to the voter's key.
- **Single-vote enforcement**: Policy SHOULD prevent duplicate proof contributions.

### 13.2 Proof Integrity
Verifiers MUST validate each proof entry independently. A valid Notary proof does not
substitute for validating member proofs.

### 13.3 Partial VTC Assertions
Verifiers MUST NOT assert full circle membership based on a subset of proofs. Assertions
MUST be scoped to the verified set of proof contributors at verification time.

---

## 14. IANA Considerations

No IANA actions. "VerifiableTrustCircle" SHOULD be registered in the W3C VC Extensions Registry
upon advancement of this specification.

---

## Appendix A: VTC Cardinality and Credential Type Mapping

| N (members) | VTC Type | Prior Equivalent | Proof Count |
|-------------|----------|-----------------|-------------|
| 0 | Null VTC | None | 1 (Notary only) |
| 1 | Self-Credential | PHC | 2 (Notary + A) |
| 2 | Bilateral | VRC | 3 (Notary + A + B) |
| N > 2 | Multi-Party | None (new) | N+2 (Notary + A + B...Z) |
| Open | Voting VTC | None (new) | 1 + votes cast |

---

## References

### Normative
- [RFC2119] Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174] Leiba, B. Ambiguity of Uppercase vs Lowercase. May 2017.
- [W3C.DID-CORE] Sporny et al. DIDs v1.0. W3C Recommendation, July 2022.
- [W3C.VC-DATA-INTEGRITY] Sporny, Longley. VC Data Integrity 1.0. W3C Recommendation, 2024.
- [W3C.VC-DATA-MODEL] Sporny et al. VC Data Model v2.0. W3C Recommendation, 2024.

### Informative
- [DISCUSSION-8] Herman, M. Web 7.0 VTCs. GitHub Discussion #8, trustoverip/dtgwg-cred-tf, 2025.
- [DTGWG-DESIGN] Trust over IP. DTGWG Design Principles. GitHub Discussion #11, 2025.
- [WEB70-VTC] Herman, M. SDO: VTCs using VC Proof Sets. March 2026.
  https://hyperonomy.com/2026/03/26/sdo-verifiable-trust-circles-vtcs-using-vc-proof-sets-web-7-0/
- [SSC-7] Herman, M. SSC 7.0 Metamodel. December 2025.
  https://hyperonomy.com/2025/12/10/self-sovereign-control-ssc-7-0-metamodel/
- [TSP] Trust over IP Foundation. Trust Spanning Protocol. 2025.
- [PHC-PAPER] Crites, B. Personhood Credentials. arXiv 2408.07892, 2024.

---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
URI: https://hyperonomy.com/about/
