# Decentralized Resource Name (DRN) DID Method
# draft-herman-did-w3c-drn-00
# Source: https://www.ietf.org/archive/id/draft-herman-did-w3c-drn-00.html
# Retrieved: April 2026

Internet-Draft: draft-herman-did-w3c-drn-00
Published:      24 March 2026
Expires:        25 September 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Decentralized Identifiers
Datatracker:    https://datatracker.ietf.org/doc/draft-herman-did-w3c-drn/

---

## Abstract

This document specifies the `did:drn` Decentralized Identifier (DID) method, which defines
a deterministic mapping from Uniform Resource Names (URNs) (RFC 8141) into a DID-compatible
identifier format called a Decentralized Resource Name (DRN). The `did:drn` method preserves
URN semantics, enables DID resolution without mandatory centralized infrastructure, and provides
optional cryptographic and service-layer extensibility. The method is fully compatible with the
W3C DID Core specification and the broader DID ecosystem.

This document also defines the Web 7.0 domain-style profile of `did:drn` and the fundamental
distinction between **Identity DIDs** (bare `did:drn` identifiers using `:` as delimiter,
identifying subjects with resolvable DID Documents) and **Locator DID URLs** (DID URL path
extensions using `/` as delimiter, addressing data records within a subject's namespace). This
distinction is a design principle of the Web 7.0 profile and is fully consistent with
W3C DID Core Section 3.2.

---

## 1. Introduction

Uniform Resource Names (URNs) [RFC8141] provide a well-established mechanism for assigning
persistent, location-independent identifiers to resources. However, URNs predate the
Decentralized Identifier (DID) ecosystem and lack native support for DID resolution, DID Document
retrieval, cryptographic verification methods, or service endpoint declaration.

The `did:drn` method bridges this gap. It defines a deterministic, reversible transformation
from any well-formed URN into a DID-compatible identifier called a Decentralized Resource Name (DRN).
The resulting DID is fully resolvable, is backwards compatible with the source URN, requires no
mandatory centralized registry, and is composable with other DID methods such as did:key, did:web,
and did:peer.

Primary design goals:
- Preservation of URN semantics and namespace-specific comparison rules.
- Deterministic, stateless baseline resolution requiring no external infrastructure.
- Optional cryptographic extensibility through verification methods.
- Optional service-layer extensibility through service endpoints.
- Full conformance with the W3C DID Core specification.

Note: DID URL path conventions for addressing records in Web 7.0 TDA LiteDB
Data Storage databases are specified in [DRN-RESOURCE]. That specification is
specific to the Web 7.0 / SVRN7 architecture and is not a general extension
to the did:drn method.

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

---

## 3. Terminology

- **URN**: A persistent, location-independent identifier of the form `urn:<NID>:<NSS>`.
- **NID**: Namespace Identifier — the registered URN namespace label (e.g., `isbn`, `uuid`).
- **NSS**: Namespace-Specific String — the portion of a URN following the NID.
- **DRN**: Decentralized Resource Name — a URN expressed within the `did:drn` method namespace.
- **DID**: A Decentralized Identifier — a URI that identifies a subject and resolves to a DID
  Document. A DID is a **persistent identity** for a subject. In `did:drn`, the `:` character
  is used as a delimiter within the method-specific identifier.
- **DID URL**: A DID extended with a path (`/`), query (`?`), or fragment (`#`) component
  per W3C DID Core Section 3.2. A DID URL is a **locator** — it addresses a resource
  relative to a DID subject. It is not itself an identity and has no DID Document.
- **Identity DID**: A bare `did:drn` DID that identifies a subject (citizen, society,
  federation). Uses `:` as the delimiter within the method-specific identifier.
  Example: `did:drn:alice.alpha.svrn7.net`. Resolvable to a DID Document.
- **Locator DID URL**: A `did:drn` DID URL with a path component (`/`) that addresses
  a specific resource within a subject's namespace. Uses `/` to separate the subject
  identity from the resource path. Example: `did:drn:alpha.svrn7.net/inbox/msg/5f43a2...`.
  Not resolvable to a DID Document; resolves to a data record.
- **DID Document**: A set of data describing the DID subject (W3C DID Core Section 5).
- **Resolver**: A software component that, given a DID, returns a DID Document.
- **Controller**: An entity with the capability to make changes to a DID Document.
- **Fingerprint**: A cryptographic hash of a canonical representation of the embedded URN.
- **Web 7.0 Profile**: A domain-style profile of `did:drn` in which the method-specific
  identifier uses `.`-separated labels (e.g., `alice.alpha.svrn7.net`) rather than a
  `urn:NID:NSS` form. See Section 5a.

---

## 4. Method Name

The method name is: `drn`

A DID conforming to this specification begins with the prefix `did:drn:`. Implementations
SHOULD produce lowercase prefixes in all output.

---

## 5. Method-Specific Identifier

### 5.1 Syntax

```abnf
did-w3c-drn = "did:drn:" drn
drn     = "urn:" NID ":" NSS
NID     = <URN Namespace Identifier per RFC 8141>
NSS     = <Namespace-Specific String per RFC 8141>
```

Conformant examples:
```
did:drn:urn:isbn:9780141036144
did:drn:urn:uuid:6ba7b810-9dad-11d1-80b4-00c04fd430c8
did:drn:urn:ietf:rfc:8141
did:drn:urn:epc:id:sgtin:0614141.107346.2017
```

### 5.2 Normalization

Implementations MUST normalize the embedded URN according to the lexical equivalence and
case-folding rules specified in RFC 8141 Section 3.1 before constructing or comparing a
`did:drn` identifier.

---

## 5a. Web 7.0 Domain-Style Profile

### 5a.1 Overview

The canonical `did:drn` method-specific identifier is a URN of the form `urn:NID:NSS`
(Section 5). The Web 7.0 / SVRN7 architecture defines a **domain-style profile** of
`did:drn` in which the method-specific identifier uses `.`-separated labels resembling
a domain name, without the `urn:` prefix.

This profile is an application-specific convention layered on top of the base `did:drn`
method. It does not alter the base method semantics; it specialises the NSS format for
the SVRN7 ecosystem.

### 5a.2 Identity DIDs — Using `:` as Delimiter

In the Web 7.0 profile, **Identity DIDs** identify subjects (citizens, societies,
federations). The method-specific identifier uses `:` as a structural delimiter — the
same delimiter used in URNs and in the base `did:drn` form.

```
did:drn:{network-id}
```

Where `{network-id}` is a dot-separated label string identifying the subject within
the SVRN7 network:

```
did:drn:alpha.svrn7.net              ← Society identity DID
did:drn:alice.alpha.svrn7.net        ← Citizen identity DID
did:drn:foundation.svrn7.net         ← Federation identity DID
```

**Properties of Identity DIDs:**

- Are **persistent** — they survive database migration, TDA restart, and epoch transitions.
- Are **resolvable** — an Identity DID resolves to a DID Document containing verification
  methods and service endpoints.
- Are **subjects** — the DID Document describes the citizen, society, or federation.
- Use `:` exclusively as the delimiter within the method-specific identifier. No `/`
  character appears in an Identity DID.

```abnf
identity-did     = "did:drn:" network-id
network-id       = label *( "." label )
label            = 1*( ALPHA / DIGIT / "-" )
```

### 5a.3 Locator DID URLs — Using `/` as Delimiter

**Locator DID URLs** address specific resources within a subject's namespace. They extend
an Identity DID with a DID URL path (`/`) that identifies a particular data record in a
TDA Data Storage database.

```
did:drn:{network-id}/{db}/{type}/{key}
```

The `/` character is the W3C DID Core DID URL path delimiter. It is **not** part of the
method-specific identifier — it separates the identity from the resource address.

```
did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1c8e9d7f012345678   ← inbox record
did:drn:alpha.svrn7.net/main/citizen/alice.alpha.svrn7.net    ← citizen DB record
did:drn:alpha.svrn7.net/main/logentry/a3f9b2c1d4e5f678...     ← Merkle log entry
did:drn:alpha.svrn7.net/schemas/schema/CitizenEndowmentCredential
```

**Properties of Locator DID URLs:**

- Are **locators** — they address a resource, not a subject.
- Are **NOT resolvable** to a DID Document — they identify data records, not DID subjects.
- Are **ephemeral** — inbox message Locator DID URLs are only meaningful while the
  record exists. Merkle log entry Locator DID URLs are permanent (content-addressed).
- Use `/` as the path delimiter, which is the W3C DID Core DID URL path convention.
- Are self-routing — the `{db}` and `{type}` segments identify the exact LiteDB file
  and collection without any external lookup.

The complete specification of Locator DID URL path conventions is in [DRN-RESOURCE].

### 5a.4 The Fundamental Distinction

The table below summarises the structural and semantic difference between the two forms:

| Property              | Identity DID                            | Locator DID URL                                    |
|-----------------------|-----------------------------------------|----------------------------------------------------|
| Delimiter after prefix| `:` (method-specific id delimiter)      | `/` (DID URL path delimiter)                       |
| Example               | `did:drn:alice.alpha.svrn7.net`         | `did:drn:alpha.svrn7.net/main/citizen/alice...`    |
| Identifies            | A subject (citizen, society, federation)| A data record in a TDA Data Storage database       |
| Has DID Document      | Yes                                     | No                                                 |
| Persistent            | Yes                                     | Record-lifetime (some permanent, some ephemeral)   |
| W3C DID Core role     | DID                                     | DID URL (path component)                           |
| Resolvable            | To a DID Document                       | To a data record (via TDA resolver)                |
| Analogy               | `https://example.com`                   | `https://example.com/path/to/resource`             |

### 5a.5 Consistency with W3C DID Core

This distinction is fully consistent with W3C DID Core [W3C.DID-CORE] Section 3.2,
which defines DID URLs as extensions of DIDs:

> "A DID URL is a network location identifier for a specific resource. It can be used
> to retrieve things like representations of DID subjects, verification methods,
> services, specific parts of a DID document, or other resources."

The Web 7.0 profile applies this standard distinction rigorously:

- Bare DID = Identity. `did:drn:alice.alpha.svrn7.net` identifies Alice as a subject.
- DID + `/` path = Locator. `did:drn:alpha.svrn7.net/main/citizen/alice.alpha.svrn7.net`
  locates Alice's citizen record in the Society TDA's main database.

The `:` vs `/` choice is therefore not arbitrary — it reflects the W3C DID Core
structural semantics, made explicit as a design principle of the Web 7.0 profile.

### 5a.6 Epoch 0 Anonymisation Note

In Epoch 0, citizen Identity DIDs are used directly as the key segment in Locator DID
URLs (e.g., `.../main/citizen/alice.alpha.svrn7.net`). This makes the citizen's identity
visible in the Locator DID URL. In a future epoch, the key segment will be replaced with
an anonymised form (Blake3 hash, GUID, or salted hash) to break the direct linkage
between a citizen's Identity DID and their Locator DID URL. See [DRN-RESOURCE] Section 9.

## 6. Core Properties

### 6.1 Determinism
A given URN MUST map deterministically to exactly one `did:drn` identifier.

### 6.2 Reversibility
The original URN MUST be exactly recoverable from the `did:drn` identifier without loss of information.

### 6.3 Infrastructure Independence
Baseline resolution MUST NOT require access to any centralized registry, distributed ledger,
or network service.

---

## 7. DID Resolution

### 7.1 Resolution Input
```
Input: did:drn:<urn>
```

### 7.2 Resolution Output
Minimum conformant DID Document:
```json
{
  "@context": "https://www.w3.org/ns/did/v1",
  "id": "did:drn:urn:isbn:9780141036144",
  "alsoKnownAs": [
    "urn:isbn:9780141036144"
  ]
}
```

### 7.3 Resolution Modes

**Mode 1 — Stateless Resolution (REQUIRED)**
Resolver constructs the DID Document locally from the DID string alone. Fully deterministic,
zero infrastructure dependency.

**Mode 2 — Deterministic Fingerprint (RECOMMENDED)**
```
fingerprint = hash(canonical-urn)
```
Expressed as a `did:key` identifier in the `equivalentId` property.

**Mode 3 — Discovery-Enhanced Resolution (OPTIONAL)**
Resolvers MAY perform external discovery (DNS-SD, HTTPS well-known, IPFS). External documents
MUST be validated for consistency with the locally constructed baseline before being returned.

---

## 8. DID Document Structure

### 8.1 Base Document
```json
{
  "@context": "https://www.w3.org/ns/did/v1",
  "id": "did:drn:<urn>",
  "alsoKnownAs": ["<urn>"]
}
```

### 8.2 Optional Properties
- **Verification Methods**: Ed25519VerificationKey2020 or equivalent (W3C DID Core Section 5.2).
- **Service Endpoints**: DRNResourceService or registered type (W3C DID Core Section 5.4).
- **Equivalent Identifier**: Mode 2 `did:key` fingerprint in `equivalentId`.

---

## 9. Controller Model

### 9.1 Default Behaviour
In Mode 1, the DID Document contains no `controller` property. Absence indicates control
has not been established.

### 9.2 Establishing Control
Control MAY be asserted via:
- Verifiable Credentials binding a controller identity to the DRN.
- Signed DID Documents.
- Namespace authority attestations.

When established, `controller` MUST be included and MUST reference a resolvable DID.

---

## 10. Verification and Trust

`did:drn` does not inherently provide authenticity guarantees in Mode 1. Trust assurances
SHOULD be layered via:
- Cryptographic proofs (JSON-LD Proofs, JOSE signatures).
- Third-party Verifiable Credentials.
- Namespace authority validation.

Consumers SHOULD NOT infer trustworthiness solely from the presence of the DID.

---

## 11. CRUD Operations

| Operation  | Status            | Notes |
|------------|-------------------|-------|
| Create     | Implicit          | No registration required; syntactic transformation of a URN. |
| Read       | REQUIRED          | Mode 1 (stateless) MUST be supported. |
| Update     | NOT SUPPORTED     | Only via Mode 3 external discovery service. |
| Deactivate | NOT SUPPORTED     | External service layers may implement independently. |

---

## 12. Interoperability

### 12.1 With URN Systems
Fully backward compatible. The embedded URN is preserved verbatim (after normalization).
`alsoKnownAs` ensures mapping back to the source URN.

### 12.2 With the DID Ecosystem
Compatible with W3C DID Core and DID Resolution. Composable with:
- `did:key` via the deterministic fingerprint mechanism.
- `did:web` via service endpoint references.
- `did:peer` for privacy-sensitive pairwise contexts.

---

## 13. Privacy Considerations

**Correlation Risks**: The deterministic mapping means any party observing a `did:drn`
identifier can recover the underlying URN immediately.

**Mitigations**:
- Use pairwise `did:peer` identifiers in correlation-sensitive contexts.
- Avoid `did:drn` identifiers from URNs encoding sensitive personal data.
- Use Verifiable Presentations with selective disclosure.

---

## 14. Security Considerations

**Mode 1 Limitation**: No proof-of-control. Any party can construct a syntactically valid
`did:drn` DID from any well-formed URN.

**Mode 3 Risk**: Spoofed or tampered external service endpoints.

**Recommendations**:
- Require cryptographic proof on Mode 3 DID Documents.
- Use VCs for controller binding.
- Protect Mode 3 endpoints with TLS 1.2+ [RFC8446].
- Validate embedded URN against RFC 8141 ABNF before resolution.

---

## 15. IANA Considerations

Requests registration of DID method name `drn` in the W3C DID Specification Registries.

| Field        | Value               |
|--------------|---------------------|
| Method Name  | `drn`               |
| Status       | provisional         |
| Specification| This document       |
| Contact      | See Author's Address|

---

## Appendix A: Complete Example

Source URN: `urn:isbn:9780141036144`
Derived DRN DID: `did:drn:urn:isbn:9780141036144`

Resolved DID Document (Modes 1 + 2 + service endpoint):
```json
{
  "@context": "https://www.w3.org/ns/did/v1",
  "id": "did:drn:urn:isbn:9780141036144",
  "alsoKnownAs": ["urn:isbn:9780141036144"],
  "equivalentId": ["did:key:zQm..."],
  "service": [
    {
      "id": "did:drn:urn:isbn:9780141036144#info",
      "type": "BookMetadata",
      "serviceEndpoint": "https://example.org/isbn/9780141036144"
    }
  ]
}
```

---

## References

### Normative
- [RFC2119] Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC3986] Berners-Lee et al. URI Generic Syntax. STD 66, January 2005.
- [RFC5234] Crocker, D. Augmented BNF. STD 68, January 2008.
- [RFC8141] Saint-Andre, P. Uniform Resource Names. April 2017.
- [RFC8174] Leiba, B. Ambiguity of Uppercase vs Lowercase in RFC 2119. May 2017.
- [RFC8446] Rescorla, E. TLS 1.3. August 2018.
- [W3C.DID-CORE] Sporny et al. DIDs v1.0. W3C Recommendation, July 2022.
- [W3C.DID-RESOLUTION] Sabadello, M. DID Resolution v0.3. W3C Working Group Note, 2023.

### Informative
- [W3C.DID-SPEC-REGISTRIES] Sporny, Steele. DID Specification Registries. 2023.
- [W3C.VC-DATA-MODEL] Sporny et al. VC Data Model v2.0. W3C CR, 2024.
- [WEB70-DRN] Herman, M. SDO: W3C DRN DID Method. March 2026. https://hyperonomy.com/2026/03/24/sdo-web-7-0-decentralized-resource-name-drn-did-method/
- [DRN-RESOURCE] Herman, M. Web 7.0 TDA Resource Addressing using DID URL Paths.
              draft-herman-drn-resource-addressing-00. Web 7.0 Foundation, 2026.
              Specifies DID URL path conventions for addressing records in Web 7.0
              TDA LiteDB Data Storage databases. Specific to Web 7.0 / SVRN7;
              not a general extension to the did:drn method.

---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
URI: https://hyperonomy.com/about/
