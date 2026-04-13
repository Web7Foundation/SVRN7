# Web 7.0 TDA Resource Addressing using DID URL Paths
# draft-herman-drn-resource-addressing-00
# Author: M. Herman, Web 7.0 Foundation
# Published: April 2026

Internet-Draft: draft-herman-drn-resource-addressing-00
Published:      April 2026
Expires:        October 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Web 7.0 Foundation Governance Council
Related:        draft-herman-did-w3c-drn-00
                draft-herman-web7-society-architecture-00
                draft-herman-parchment-programming-00
                draft-herman-web7-merkle-audit-log-00

---

## Abstract

This document specifies a DID URL path convention for addressing records in the LiteDB
Data Storage databases of a Web 7.0 Trusted Digital Assistant (TDA). It defines a
structured DID URL scheme in which the authority segment identifies the owning TDA,
the first path segment identifies the Data Storage database, the second path segment
identifies the collection (resource type), and the final path segment is the record's
natural key — which is a citizen DID suffix for identity-bearing records, a Blake3 hex
hash for content-addressed records, or a LiteDB ObjectId hex string for surrogate-keyed
records.

This scheme makes TDA data records addressable as first-class locators within the
`did:drn` ecosystem, enables cross-TDA record resolution via DIDComm proxy, and
aligns the Merkle audit log's content-addressing model with the DID URL path convention.
The scheme is specific to the Web 7.0 / SOVRONA (SVRN7) TDA architecture and is not
intended as a general extension to the `did:drn` method.

---

## 1. Introduction

The `did:drn` DID method [DRAFT-DID-DRN] defines a deterministic mapping from Uniform
Resource Names into DID-compatible identifiers. The Web 7.0 TDA architecture [WEB70-ARCH]
introduces a further requirement: individual records within the TDA's LiteDB Data Storage
databases must be addressable as locators within the `did:drn` namespace, so that:

1. Inbound DIDComm messages can be passed by reference (as DID URL strings) between
   the DIDComm Message Switchboard and LOBE cmdlet pipelines, without copying payloads.

2. Merkle audit log entries can be referenced by their content-addressed DID URL in
   Signed Tree Heads and cross-TDA audit proofs.

3. Data records can be resolved cross-TDA via DIDComm proxy when a TDA receives a
   DID URL whose authority segment identifies a different TDA in the VTC7 mesh.

4. The "Society TDA Only" Conditional Components (Data Storage databases for DID
   Documents, Verifiable Credentials, and Schemas) are visibly scoped to Society TDA
   deployments by their database segment.

The W3C DID Core specification [W3C.DID-CORE] defines DID URLs as a superset of DIDs.
A DID URL may include a path, query, and fragment. A DID URL path addresses a resource
*located at* the DID subject — it is a locator, not an identity. This distinction is
fundamental: the DID URL paths defined in this document are locators; they do not
identify subjects and are not associated with DID Documents.

### 1.1 Design Goals

1. **Self-routing.** A DID URL MUST contain sufficient information to route a resolution
   request to the correct TDA, the correct Data Storage database, and the correct
   collection, without any external lookup table.

2. **Direct deserialisation.** The final path segment MUST be directly parseable as the
   record's natural key without encoding or transformation beyond a simple string split.

3. **Key type uniformity.** Records with a natural content-addressed key (Blake3 hash)
   use that key. Records with a natural identity key (DID suffix) use that key.
   Records with a human-meaningful common name (schemas) use that name.
   Only records with no natural key use a LiteDB ObjectId surrogate.

4. **Epoch-safe.** In future epochs, the citizen DID suffix in identity-bearing record
   DID URLs MAY be replaced with an anonymised form (hash, GUID, or salted hash) without
   changing any other segment of the scheme.

5. **Cross-TDA resolution.** A TDA that receives a DID URL whose authority segment
   identifies a different TDA MUST forward the resolution request to the owning TDA via
   DIDComm using the `did/1.0/resolve-request` protocol.

### 1.2 Scope

This document specifies:
1. The DID URL path structure for TDA Data Storage database record addressing (Section 3).
2. The complete database segment vocabulary (Section 4).
3. The complete type segment vocabulary per database (Section 5).
4. The natural key selection rules per record type (Section 6).
5. The cross-TDA resolution protocol (Section 7).
6. The Epoch 0 complete reference table (Section 8).
7. The future epoch anonymisation pathway (Section 9).

This document does not specify:
- The `did:drn` method itself (see [DRAFT-DID-DRN]).
- The TDA architecture (see [WEB70-ARCH]).
- The Merkle audit log structure (see [DRAFT-MERKLE]).
- The PPML derivation rules for Data Storage databases (see [DRAFT-PPML]).

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

---

## 3. Terminology

- **DID URL path**: The path component of a DID URL, introduced by `/`, as defined in
  [W3C.DID-CORE] Section 3.2. A DID URL path is a locator, not an identity.

- **Owning TDA**: The TDA whose `did:drn` authority segment appears in the DID URL. The
  owning TDA is the authoritative source for the addressed record.

- **Data Storage database**: A LiteDB embedded database file in a TDA deployment. Each
  Data Storage database corresponds to one database segment value in the DID URL path.
  Derived from: "Data Storage" element type in the PPML Legend — DSA 0.24 Epoch 0.

- **Collection**: A named set of records within a Data Storage database. Corresponds to
  the type segment value in the DID URL path.

- **Natural key**: The canonical identifier for a record that is independent of the storage
  system. May be a DID suffix (identity-bearing records), a Blake3 hex hash (content-
  addressed records), or a LiteDB ObjectId hex string (surrogate-keyed records).

- **Identity-bearing record**: A record whose subject has a citizen or Society DID. The
  natural key is the DID suffix (the portion of the DID after `did:drn:`).

- **Content-addressed record**: A record whose integrity is verifiable from its content.
  The natural key is the Blake3 hex hash of the record's canonical byte representation.

- **Surrogate-keyed record**: A record with no natural domain key. The natural key is the
  24-character hex string representation of its LiteDB ObjectId.

- **Named record**: A record whose natural key is a human-meaningful common name assigned
  at authoring time. Schema records are named records. The name MUST be unique within the
  collection and MUST consist only of alphanumeric characters, hyphens, and dots
  (e.g., `CitizenEndowmentCredential`, `TransferReceiptCredential`).

- **Blake3 hex hash**: The 64-character lowercase hexadecimal string produced by the
  BLAKE3 [BLAKE3] cryptographic hash function applied to the canonical byte
  representation of a record.

- **LiteDB ObjectId**: A 12-byte identifier assigned by LiteDB to each document,
  represented as a 24-character lowercase hexadecimal string.

---

## 3a. DIDComm Message Type URIs as Locator DID URLs

### 3a.1 Overview

The DIDComm V2 specification requires the `@type` field of a DIDComm message to be an
absolute URI per RFC 3986. In the Web 7.0 / SVRN7 ecosystem, DIDComm message type URIs
are **Locator DID URLs** of the form:

```
did:drn:svrn7.net/protocols/{family}/{version}/{message-type}
```

This is consistent with the Identity DID / Locator DID URL distinction defined in
`draft-herman-did-w3c-drn-00` Section 5a:

- `did:drn:svrn7.net` is the **Identity DID** of the SVRN7 protocol namespace — the
  subject that owns the protocol definitions.
- `/protocols/{family}/{version}/{message-type}` is the **DID URL path** that locates
  the specific protocol definition resource within that namespace.

The `:` delimiter separates the method from the network identifier (Identity DID form).
The `/` delimiter introduces the DID URL path (Locator form). Together they produce a
globally unique, self-describing protocol type identifier that is:

1. **Namespaced** — scoped to the `svrn7.net` network identifier.
2. **Resolvable** — can in principle resolve to a protocol specification document.
3. **Consistent** — uses the same `did:drn:` scheme as all other SVRN7 identifiers.
4. **Self-contained** — does not depend on an HTTP server being available at a URL.

### 3a.2 Relationship to Data Record Locator DID URLs

Both DIDComm message type URIs and Data Storage record DID URLs use the Locator DID URL
form with `/` path delimiter. They differ only in the path segments:

```
did:drn:svrn7.net/protocols/transfer/1.0/request    ← Protocol definition locator
did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1...        ← Data record locator
```

The protocol namespace uses `svrn7.net` (the canonical network identifier) rather than
a Society-specific identifier such as `alpha.svrn7.net`, because protocol definitions
are global to the SVRN7 ecosystem and not owned by any particular Society.

### 3a.3 Third-Party Protocol URIs

Third-party LOBE developers MUST use a namespace they control for their DIDComm message
type URIs. They MUST NOT register URIs in the `did:drn:svrn7.net/protocols/` namespace,
which is reserved for standard SVRN7 protocols.

Recommended form for third-party protocol URIs:

```
did:drn:{developer-domain}/protocols/{family}/{version}/{message-type}
```

Example:
```
did:drn:health.example.org/protocols/prescription/1.0/request
```

### 3a.4 Complete Protocol URI Registry (Epoch 0)

The following table is the normative registry of SVRN7 DIDComm message type URIs for
Epoch 0.

| Constant Name           | DID URL                                                              | Direction         |
|-------------------------|----------------------------------------------------------------------|-------------------|
| `TransferRequest`       | `did:drn:svrn7.net/protocols/transfer/1.0/request`                  | Citizen → Society |
| `TransferReceipt`       | `did:drn:svrn7.net/protocols/transfer/1.0/receipt`                  | Society → Citizen |
| `TransferOrder`         | `did:drn:svrn7.net/protocols/transfer/1.0/order`                    | Society → Society |
| `TransferOrderReceipt`  | `did:drn:svrn7.net/protocols/transfer/1.0/order-receipt`            | Society → Society |
| `OverdraftDrawRequest`  | `did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-request`  | Society → Federation |
| `OverdraftDrawReceipt`  | `did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-receipt`  | Federation → Society |
| `EndowmentTopUp`        | `did:drn:svrn7.net/protocols/endowment/1.0/top-up`                  | Federation → Society |
| `SupplyUpdate`          | `did:drn:svrn7.net/protocols/supply/1.0/update`                     | Federation → Societies |
| `DidResolveRequest`     | `did:drn:svrn7.net/protocols/did/1.0/resolve-request`               | Society → Society |
| `DidResolveResponse`    | `did:drn:svrn7.net/protocols/did/1.0/resolve-response`              | Society → Society |
| `OnboardRequest`        | `did:drn:svrn7.net/protocols/onboard/1.0/request`                   | Citizen → Society |
| `OnboardReceipt`        | `did:drn:svrn7.net/protocols/onboard/1.0/receipt`                   | Society → Citizen |
| `InvoiceRequest`        | `did:drn:svrn7.net/protocols/invoice/1.0/request`                   | Citizen → Society |
| `InvoiceReceipt`        | `did:drn:svrn7.net/protocols/invoice/1.0/receipt`                   | Society → Citizen |
| Email message           | `did:drn:svrn7.net/protocols/email/1.0/message`                     | TDA → TDA |
| Email receipt           | `did:drn:svrn7.net/protocols/email/1.0/receipt`                     | TDA → TDA |
| Calendar event          | `did:drn:svrn7.net/protocols/calendar/1.0/event`                    | TDA → TDA |
| Calendar invite         | `did:drn:svrn7.net/protocols/calendar/1.0/invite`                   | TDA → TDA |
| Calendar response       | `did:drn:svrn7.net/protocols/calendar/1.0/response`                 | TDA → TDA |
| Presence status         | `did:drn:svrn7.net/protocols/presence/1.0/status`                   | TDA → TDA |
| Presence subscribe      | `did:drn:svrn7.net/protocols/presence/1.0/subscribe`                | TDA → TDA |
| Presence unsubscribe    | `did:drn:svrn7.net/protocols/presence/1.0/unsubscribe`              | TDA → TDA |
| Notification alert      | `did:drn:svrn7.net/protocols/notification/1.0/alert`                | TDA → TDA |

## 4. DID URL Path Structure

### 4.1 General Form

```
did:drn:{network-id}/{db}/{type}/{key}
```

Where:

- `{network-id}` — The network identifier of the owning TDA (e.g., `alpha.svrn7.net`,
  `foundation.svrn7.net`). Identical to the method-specific identifier of the owning
  TDA's `did:drn` DID.

- `{db}` — The database segment. Identifies the LiteDB Data Storage database file.
  MUST be one of the values defined in Section 4.

- `{type}` — The type segment. Identifies the collection within the Data Storage
  database. MUST be one of the values defined in Section 5 for the given `{db}`.

- `{key}` — The natural key segment. The canonical identifier for the specific record.
  MUST conform to the key type rules in Section 6.

### 4.2 Parsing

A conformant parser MUST split the DID URL on `/` to extract segments:

```
parts    = didUrl.split('/')
authority = parts[0]           // "did:drn:alpha.svrn7.net"
db        = parts[1]           // "main"
type      = parts[2]           // "citizen"
key       = parts[3]           // natural key string
```

The natural key is always the final segment. No percent-encoding is applied to any
segment. All characters in the key MUST be URL-safe without encoding (hex digits,
alphanumeric, `.`, `-`).

### 4.3 Examples

```
did:drn:alpha.svrn7.net/main/citizen/alice.alpha.svrn7.net
did:drn:alpha.svrn7.net/main/logentry/a3f9b2c1d4e5f67890ab1234cd5678ef90ab12cd34ef5678
did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1c8e9d7f012345678
did:drn:alpha.svrn7.net/vcs/vc/vc-3fa85f64-5717-4562-b3fc-2c963f66afa6
did:drn:foundation.svrn7.net/main/society/alpha.svrn7.net
did:drn:alpha.svrn7.net/schemas/schema/5f43a2b1c8e9d7f012345678
```

---

## 5. Database Segment Vocabulary

The following database segments are defined. Each maps to exactly one LiteDB file
in a conformant TDA deployment.

| Segment    | LiteDB File          | Scope                  | Description                                      |
|------------|----------------------|------------------------|--------------------------------------------------|
| `main`     | `svrn7.db`           | All TDA types          | Primary SVRN7 Data Storage database. Wallets, UTXOs, citizens, societies, memberships, Merkle log, nonces, overdrafts, key backups. |
| `inbox`    | `svrn7-inbox.db`     | All TDA types          | Durable DIDComm inbox. Inbound messages and processed order receipts. |
| `dids`     | `svrn7-dids.db`      | Society TDA Only       | DID Document registry and version history.       |
| `vcs`      | `svrn7-vcs.db`       | Society TDA Only       | Verifiable Credential registry and revocation events. |
| `schemas`  | `svrn7-schemas.db`   | Society TDA Only       | JSON Schema registry for VC issuance and validation. |

The `dids`, `vcs`, and `schemas` segments are Conditional Components scoped to Society
TDA deployments — they correspond to the "Society TDA Only" Conditional Components group
in DSA 0.24 [DSA-024]. A Citizen-only TDA deployment MUST NOT expose DID URL paths
using these database segments.

---

## 6. Type Segment Vocabulary

### 5.1 `main` database (`svrn7.db`)

| Type Segment  | Collection          | Key Type              | Description                                    |
|---------------|---------------------|-----------------------|------------------------------------------------|
| `citizen`     | Citizens            | Identity (DID suffix) | CitizenRecord — primary DID, public key, status. |
| `wallet`      | Wallets             | Identity (DID suffix) | Wallet — UTXO set for a citizen or society.    |
| `utxo`        | UTXOs               | Content (Blake3)      | Unspent Transaction Output — atomic balance unit. |
| `society`     | Societies           | Identity (DID suffix) | SocietyRecord — society DID, name, status.     |
| `membership`  | Memberships         | Identity (DID suffix) | SocietyMembershipRecord — citizen + society.   |
| `logentry`    | LogEntries          | Content (Blake3)      | Merkle log entry — event type, payload, hash.  |
| `treehead`    | TreeHeads           | Content (Blake3)      | Signed Merkle Tree Head — root hash, signature.|
| `nonce`       | Nonces              | Surrogate (ObjectId)  | Transfer replay nonce with TTL expiry.         |
| `overdraft`   | Overdrafts          | Identity (DID suffix) | SocietyOverdraftRecord — society overdraft state. |
| `keybak`      | KeyBackups          | Identity (DID suffix) | Encrypted key backup for a citizen DID.        |

### 5.2 `inbox` database (`svrn7-inbox.db`)

| Type Segment     | Collection       | Key Type             | Description                                      |
|------------------|------------------|----------------------|--------------------------------------------------|
| `msg`            | InboxMessages    | Surrogate (ObjectId) | Inbound DIDComm message — type, payload, status. |
| `processedorder` | ProcessedOrders  | Surrogate (ObjectId) | Cross-Society TransferOrder idempotency receipt. |

### 5.3 `dids` database (`svrn7-dids.db`) — Society TDA Only

| Type Segment | Collection    | Key Type              | Description                                      |
|--------------|---------------|-----------------------|--------------------------------------------------|
| `doc`        | DIDDocuments  | Identity (DID suffix) | DID Document — verification methods, services.  |

### 5.4 `vcs` database (`svrn7-vcs.db`) — Society TDA Only

| Type Segment  | Collection         | Key Type              | Description                                     |
|---------------|--------------------|-----------------------|-------------------------------------------------|
| `vc`          | VcRecords          | Surrogate (VC UUID)   | Verifiable Credential record.                   |
| `revocation`  | RevocationEvents   | Surrogate (ObjectId)  | VC revocation event record.                     |

Note: VC records use a UUID as their natural key (assigned at issuance), not a LiteDB
ObjectId. The UUID is treated as a surrogate key for purposes of this specification.

### 5.5 `schemas` database (`svrn7-schemas.db`) — Society TDA Only

| Type Segment | Collection     | Key Type             | Description                                      |
|--------------|----------------|----------------------|--------------------------------------------------|
| `schema`     | Schemas        | Named (common name)  | JSON Schema document for VC type validation.
|              |                |                      | Key is the schema's common name
|              |                |                      | (e.g., `CitizenEndowmentCredential`).           |

---

## 7. Natural Key Selection Rules

### 6.1 Identity-Bearing Records (DID Suffix)

Records whose subject has a `did:drn` DID MUST use the DID suffix as the natural key.
The DID suffix is the method-specific identifier — the portion of the DID after
`did:drn:`.

```
Citizen DID:    did:drn:alice.alpha.svrn7.net
DID suffix:     alice.alpha.svrn7.net
DID URL:        did:drn:alpha.svrn7.net/main/citizen/alice.alpha.svrn7.net
```

```
Society DID:    did:drn:alpha.svrn7.net
DID suffix:     alpha.svrn7.net
DID URL:        did:drn:foundation.svrn7.net/main/society/alpha.svrn7.net
```

This rule applies to: `citizen`, `wallet`, `membership`, `society`, `overdraft`,
`keybak`, and `doc` type segments.

### 6.2 Content-Addressed Records (Blake3 Hash)

Records whose integrity is provable from their content MUST use the Blake3 hex hash of
their canonical byte representation as the natural key.

```
Blake3 hash:    a3f9b2c1d4e5f67890ab1234cd5678ef90ab12cd34ef5678ab90cd12ef345678
DID URL:        did:drn:alpha.svrn7.net/main/logentry/a3f9b2c1d4e5f67890ab1234cd5678ef90ab12cd34ef5678ab90cd12ef345678
```

The canonical byte representation for each content-addressed type:

**`utxo`**: UTF-8 encoding of the JSON serialisation of the UTXO record with fields
ordered: `id`, `ownerDid`, `amountGrana`, `createdAt`. The `id` field MUST be set to
the resulting Blake3 hex hash.

**`logentry`**: The RFC 6962 [RFC6962] leaf hash input: `0x00 || entry_bytes`, where
`entry_bytes` is the UTF-8 encoding of the canonical JSON serialisation of the log
entry with fields ordered: `sequenceNumber`, `eventType`, `payload`, `timestamp`. The
Blake3 hash is computed over this concatenation.

**`treehead`**: The UTF-8 encoding of the canonical JSON serialisation of the Signed
Tree Head with fields ordered: `treeSize`, `rootHash`, `timestamp`. The Blake3 hash
is computed before the governance key signature is applied.

This rule applies to: `utxo`, `logentry`, `treehead` type segments.

**Efficiency note**: For `logentry` records, the Blake3 hash is computed as part of
the Merkle log append operation. The DID URL key is therefore available at zero
additional computational cost — it is a direct product of the append operation.

### 6.3 Surrogate-Keyed Records (LiteDB ObjectId or UUID)

Records with no natural domain key or content-addressed key use a surrogate key:

- **LiteDB ObjectId** — 24-character lowercase hex string of the 12-byte LiteDB
  `ObjectId` assigned at insertion time.
- **UUID** — for `vc` records, the UUID assigned at VC issuance time, formatted as
  the standard 8-4-4-4-12 hyphenated lowercase string.

```
ObjectId DID URL:  did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1c8e9d7f012345678
UUID DID URL:      did:drn:alpha.svrn7.net/vcs/vc/vc-3fa85f64-5717-4562-b3fc-2c963f66afa6
```

This rule applies to: `msg`, `processedorder`, `nonce`, `revocation`, `schema`, `vc`.

### 6.4 Named Records (Common Name)

Schema records use a human-meaningful common name as their natural key. The name is
assigned by the schema author at authoring time and MUST be unique within the `schemas`
collection for a given TDA.

```
Schema name:    CitizenEndowmentCredential
DID URL:        did:drn:alpha.svrn7.net/schemas/schema/CitizenEndowmentCredential
```

The common name MUST satisfy the following constraints:

- MUST consist only of alphanumeric characters (A-Z, a-z, 0-9), hyphens (`-`), and
  dots (`.`).
- MUST NOT contain spaces, slashes, colons, or percent-encoded characters.
- SHOULD follow PascalCase or kebab-case conventions consistent with the VC type name
  the schema validates.
- SHOULD include a version suffix when breaking changes are introduced:
  `CitizenEndowmentCredential-v2`.

Example schema DID URLs:

```
did:drn:alpha.svrn7.net/schemas/schema/CitizenEndowmentCredential
did:drn:alpha.svrn7.net/schemas/schema/TransferReceiptCredential
did:drn:alpha.svrn7.net/schemas/schema/Svrn7VtcCredential
did:drn:alpha.svrn7.net/schemas/schema/CitizenEndowmentCredential-v2
```

This rule applies to: `schema` type segment only.

### 6.5 Deserialisation

A conformant implementation MUST deserialise the natural key from the final path
segment as follows:

```csharp
var parts = didUrl.Split('/');
var db    = parts[1];   // database segment
var type  = parts[2];   // type segment
var key   = parts[3];   // natural key string

// Identity-bearing: use key directly as DID suffix
// Content-addressed: LiteDB query on Blake3 hash field
// ObjectId surrogate: new LiteDB.ObjectId(key)
// UUID surrogate:     Guid.Parse(key.TrimStart("vc-"))
// Named record:       use key directly as string query (e.g., schema name)
```

---

## 8. Cross-TDA Resolution

### 7.1 Local Resolution

When a TDA receives a DID URL whose authority segment matches its own `did:drn` DID,
it MUST resolve the record locally by querying the appropriate Data Storage database
and collection.

### 7.2 Cross-TDA Proxy Resolution

When a TDA receives a DID URL whose authority segment identifies a different TDA, it
MUST forward the resolution request to the owning TDA via DIDComm using the
`did/1.0/resolve-request` protocol [SVRN7-PROTOCOLS].

Resolution flow:

```
Requesting TDA                          Owning TDA
     │                                       │
     │  DIDComm did/1.0/resolve-request      │
     │  { "didUrl": "did:drn:beta..." }      │
     │ ─────────────────────────────────►   │
     │                                       │  Local lookup
     │  DIDComm did/1.0/resolve-response     │  (db + type + key)
     │  { "record": { ... } }               │
     │ ◄─────────────────────────────────   │
     │                                       │
```

The requesting TDA MUST use `IDidDocumentResolver` to resolve the owning TDA's DID
Document and retrieve its DIDComm service endpoint before sending the request.

The owning TDA MUST authenticate the requesting TDA's DID before returning any record.
Records in the `main` database are only returned to TDAs in the same VTC7 trust circle.

### 7.3 Resolution Caching

A TDA MAY cache resolved cross-TDA records in its `IMemoryCache` instance. The
RECOMMENDED TTL values are:

| Database | TTL       | Rationale                                      |
|----------|-----------|------------------------------------------------|
| `dids`   | 10 minutes | DID Documents change infrequently              |
| `vcs`    | 5 minutes  | VCs may be revoked; shorter TTL reduces risk   |
| `main`   | 60 seconds | Wallet balances and citizen records may change |
| `inbox`  | 24 hours   | Inbox messages are immutable once written      |
| `schemas`| 30 minutes | Schema definitions change rarely               |

---

## 9. Epoch 0 Complete Reference

The following table provides the normative complete DID URL scheme for all record types
in an Epoch 0 TDA deployment.

| Record Type         | Database | Collection        | Key Type  | Example DID URL                                                        |
|---------------------|----------|-------------------|-----------|------------------------------------------------------------------------|
| Citizen             | main     | Citizens          | DID suffix| `did:drn:alpha.svrn7.net/main/citizen/alice.alpha.svrn7.net`           |
| Wallet              | main     | Wallets           | DID suffix| `did:drn:alpha.svrn7.net/main/wallet/alice.alpha.svrn7.net`            |
| UTXO                | main     | UTXOs             | Blake3    | `did:drn:alpha.svrn7.net/main/utxo/a3f9b2c1...`                        |
| Society             | main     | Societies         | DID suffix| `did:drn:foundation.svrn7.net/main/society/alpha.svrn7.net`            |
| Membership          | main     | Memberships       | DID suffix| `did:drn:alpha.svrn7.net/main/membership/alice.alpha.svrn7.net`        |
| Merkle log entry    | main     | LogEntries        | Blake3    | `did:drn:alpha.svrn7.net/main/logentry/a3f9b2c1...`                    |
| Merkle tree head    | main     | TreeHeads         | Blake3    | `did:drn:alpha.svrn7.net/main/treehead/a3f9b2c1...`                    |
| Nonce               | main     | Nonces            | ObjectId  | `did:drn:alpha.svrn7.net/main/nonce/5f43a2b1c8e9d7f012345678`          |
| Overdraft record    | main     | Overdrafts        | DID suffix| `did:drn:alpha.svrn7.net/main/overdraft/alpha.svrn7.net`               |
| Key backup          | main     | KeyBackups        | DID suffix| `did:drn:alpha.svrn7.net/main/keybak/alice.alpha.svrn7.net`            |
| Inbox message       | inbox    | InboxMessages     | ObjectId  | `did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1c8e9d7f012345678`           |
| Processed order     | inbox    | ProcessedOrders   | ObjectId  | `did:drn:alpha.svrn7.net/inbox/processedorder/5f43a2b1c8e9d7f012345678`|
| DID Document        | dids     | DIDDocuments      | DID suffix| `did:drn:alpha.svrn7.net/dids/doc/alice.alpha.svrn7.net`               |
| VC record           | vcs      | VcRecords         | VC UUID   | `did:drn:alpha.svrn7.net/vcs/vc/vc-3fa85f64-5717-4562-b3fc-2c963f66afa6` |
| Revocation event    | vcs      | RevocationEvents  | ObjectId  | `did:drn:alpha.svrn7.net/vcs/revocation/5f43a2b1c8e9d7f012345678`      |
| Schema              | schemas  | Schemas           | Named     | `did:drn:alpha.svrn7.net/schemas/schema/CitizenEndowmentCredential`    |

---

## 10. Future Epoch Anonymisation

In Epoch 0, identity-bearing records use the citizen's primary DID suffix as the
natural key. This makes the DID URL human-readable and directly traceable to the
citizen's identity — which is appropriate for the Endowment Phase when the VTC7 mesh
is small and governance is close.

In future epochs, the citizen DID suffix in identity-bearing record DID URLs MAY be
replaced with an anonymised form. Three candidate approaches are identified here for
future specification:

### 9.1 Blake3 Hash of Primary DID (Pseudonymous)

```
key = Blake3Hex(UTF8(citizenPrimaryDid))
did:drn:alpha.svrn7.net/main/citizen/a3f9b2c1d4e5f67890...
```

Properties:
- **Deterministic** — the same citizen always produces the same key; computable
  without a lookup table.
- **Pseudonymous** — opaque to anyone who does not already know the DID; reversible
  by an operator with access to the citizen registry.
- **Uniform** — all identity-bearing records use the same anonymisation function.

### 9.2 Blake3 Hash with Society Salt (Society-scoped Pseudonymity)

```
key = Blake3Hex(UTF8(citizenPrimaryDid) || UTF8(societyDid))
did:drn:alpha.svrn7.net/main/citizen/b7e2a9f1...
```

Properties:
- The same citizen has a different key in each Society, preventing cross-Society
  correlation from DID URL observation.
- Still deterministic and operator-reversible.

### 9.3 GUID (Opaque Surrogate)

```
key = Guid.NewGuid().ToString("N")  // assigned once at registration, stored
did:drn:alpha.svrn7.net/main/citizen/550e8400e29b41d4a716446655440000
```

Properties:
- Non-deterministic — requires a lookup table to resolve citizen DID → GUID.
- Maximum opacity — no relationship to the citizen's DID is visible.
- Supports epoch-rolled re-assignment: a new GUID per epoch breaks long-term
  linkability.

The choice among these approaches is deferred to the epoch transition specification.
All three are compatible with the DID URL path structure defined in this document —
only the key segment value changes; the database and type segments are unaffected.

---

## 11. Implementation Notes

### 10.1 Pass-by-Reference in LOBE Cmdlet Pipelines

The primary motivation for inbox message DID URLs is the pass-by-reference pattern
mandated by DSA 0.24 [DSA-024]. The DIDComm Message Switchboard passes an inbox
message DID URL string to each LOBE cmdlet pipeline rather than copying the message
payload. The cmdlet resolves the message by parsing the DID URL and querying
`IMemoryCache` (hot path) or the `inbox` Data Storage database (cold path):

```csharp
// Parse the DID URL
var parts    = didUrl.Split('/');
var objectId = new LiteDB.ObjectId(parts[3]);

// Resolve via cache or LiteDB
if (!_cache.TryGetValue(didUrl, out InboxMessage? msg))
{
    msg = _ctx.InboxMessages.FindById(objectId);
    _cache.Set(didUrl, msg, TimeSpan.FromHours(24));
}
```

Using the full DID URL as the cache key rather than the bare ObjectId ensures that
cross-TDA cached records do not collide with local records that share the same
ObjectId hex value.

### 10.2 Merkle Log Integration

For `logentry` and `treehead` records, the Blake3 hash is computed as part of the
Merkle log append operation [DRAFT-MERKLE]. The DID URL is therefore a zero-cost
product of the append operation:

```csharp
var entryHash = _crypto.Blake3Hex(entryBytes);        // computed by MerkleLog
var didUrl    = $"did:drn:{_opts.NetworkId}/main/logentry/{entryHash}";
// Store didUrl in the LogEntry record for cross-TDA audit reference
```

A Signed Tree Head SHOULD include the DID URL of the most recent log entry to enable
auditors to locate and verify individual entries by DID URL without scanning the full log.

### 10.3 Conditional Components and Database Segment Availability

The `dids`, `vcs`, and `schemas` database segments are only available in Society TDA
deployments (the "Society TDA Only" Conditional Components group — DSA 0.24). A
conformant implementation MUST return an error for DID URLs using these segments
when received by a Citizen-only TDA or Federation TDA.

---

## 12. Security Considerations

### 11.1 DID URL as Locator, Not Identity

DID URLs defined in this specification are locators. They MUST NOT be used as identity
claims, authentication tokens, or authorisation assertions. A DID URL proves only that
a record with the given key exists at the stated TDA — it does not prove the identity
of any party, nor does it authorise access to the record.

### 11.2 Cross-TDA Resolution Authentication

When forwarding a resolution request to an owning TDA, the requesting TDA MUST present
its own DID and sign the DIDComm request using its messaging private key. The owning
TDA MUST verify this signature before returning any record. An unauthenticated resolution
request MUST be rejected.

### 11.3 ObjectId Predictability

LiteDB ObjectIds contain a timestamp component and are therefore partially predictable.
An attacker who can enumerate ObjectId values could traverse inbox message DID URLs
sequentially. Implementations MUST enforce TDA-level authentication (mTLS at the Kestrel
listener) before any DID URL resolution is accepted. ObjectId-keyed records MUST NOT be
accessible without authenticated DIDComm.

### 11.4 Blake3 Hash Collision Resistance

Blake3 provides 256-bit output. Collision probability for a corpus of 2^64 records
is negligible (birthday bound: ~2^128 operations). Content-addressed record DID URLs
are collision-resistant for all practical TDA deployments.

### 11.5 Epoch Anonymisation Timing

When transitioning to an anonymised key scheme in a future epoch, existing DID URLs
using the citizen DID suffix MUST be considered deprecated and SHOULD be rotated.
Implementations MUST NOT serve the old DID URL form after the epoch transition
anonymisation is applied. A transition grace period MAY be defined in the epoch
transition specification.

---

## 13. Privacy Considerations

### 12.1 Citizen DID Suffix Visibility

In Epoch 0, identity-bearing record DID URLs include the citizen's primary DID suffix
in plaintext. Any party that observes a DID URL can determine the citizen's DID. DID
URLs MUST only be transmitted within authenticated DIDComm messages (SignThenEncrypt
mode) and MUST NOT appear in log files, error messages, or HTTP headers accessible
to unauthenticated parties.

### 12.2 ObjectId Timestamp Leakage

LiteDB ObjectIds encode a Unix timestamp in their first four bytes. Observers of
inbox message DID URLs can therefore determine the approximate time of message
receipt. Where timing privacy is required, implementations SHOULD consider replacing
LiteDB ObjectIds with random 12-byte values for inbox message records.

---

## 14. IANA Considerations

This document has no IANA actions.

---

## 15. References

### Normative

- [RFC2119]   Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174]   Leiba, B. Ambiguity of Uppercase vs Lowercase in RFC 2119. May 2017.
- [RFC3986]   Berners-Lee et al. URI Generic Syntax. STD 66, January 2005.
- [W3C.DID-CORE] Sporny, M. et al. Decentralized Identifiers (DIDs) v1.0.
              W3C Recommendation, July 2022.
- [BLAKE3]    O'Connor, J. et al. BLAKE3 cryptographic hash function.
              https://github.com/BLAKE3-team/BLAKE3-specs.

### Informative

- [DRAFT-DID-DRN]   Herman, M. Decentralized Resource Name (DRN) DID Method.
                    draft-herman-did-w3c-drn-00. Web 7.0 Foundation, 2026.
- [WEB70-ARCH]      Herman, M. Web 7.0 Society Architecture.
                    draft-herman-web7-society-architecture-00. Web 7.0 Foundation, 2026.
- [DRAFT-MERKLE]    Herman, M. Web 7.0 Merkle Audit Log.
                    draft-herman-web7-merkle-audit-log-00. Web 7.0 Foundation, 2026.
- [DRAFT-PPML]      Herman, M. Parchment Programming Modeling Language (PPML).
                    draft-herman-parchment-programming-00. Web 7.0 Foundation, 2026.
- [SVRN7-PROTOCOLS] Herman, M. SOVRONA (SVRN7) DIDComm Protocol URIs.
                    draft-herman-didcomm-svrn7-transfer-00. Web 7.0 Foundation, 2026.
- [DSA-024]         Herman, M. Web 7.0 Decentralized System Architecture (DSA) 0.24
                    Epoch 0. Diagram. Web 7.0 Foundation, April 2026.
- [RFC6962]         Laurie, B. et al. Certificate Transparency. RFC 6962, June 2013.

---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
URI:   https://hyperonomy.com/about/
