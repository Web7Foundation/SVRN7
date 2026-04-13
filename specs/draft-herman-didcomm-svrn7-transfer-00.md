# DIDComm Transfer Protocol for SVRN7
# draft-herman-didcomm-svrn7-transfer-00
# Author: M. Herman, Web 7.0 Foundation
# Published: April 2026

Internet-Draft: draft-herman-didcomm-svrn7-transfer-00
Published:      April 2026
Expires:        October 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Web 7.0 Foundation Governance Council
Related:        draft-herman-svrn7-monetary-protocol-00
                draft-herman-web7-society-architecture-00
                draft-herman-did-w3c-drn-00

---

## Abstract

This document specifies the DIDComm v2 messaging protocol used for monetary transfers,
overdraft management, DID Document resolution, and Verifiable Credential queries in the
Web 7.0 SOVRONA (SVRN7) ecosystem. It defines the protocol URI namespace, the message types
and their payload schemas, the required DIDComm pack mode (SignThenEncrypt), the idempotency
model for cross-Society transfers, and the message processing requirements for conformant
Federation and Society implementations. This specification enables cross-implementation
interoperability: a conformant Society implemented in any programming language can exchange
monetary transfer messages with any conformant Federation or Society implementation.

---

## 1. Introduction

DIDComm Messaging v2 [DIDCOMM-V2] is the communication protocol for all cross-boundary
interactions in the Web 7.0 ecosystem. It provides sender authentication, recipient
confidentiality, and transport independence — properties that are essential for monetary
commitments that must remain auditable by third parties.

### 1.1 Why DIDComm

DIDComm is not merely a transport choice for the SVRN7 ecosystem — it is a trust architecture
choice. The properties it provides are:

**Transport independence**: A DIDComm message packed with SignThenEncrypt carries its own
authentication and confidentiality. It can be delivered over HTTPS, WebSocket, a message queue,
or a database record, and its security properties hold in all cases.

**Non-repudiation via SignThenEncrypt**: The JWS signature applied before encryption survives
decryption intact. Any third party holding the sender's public key (available from the sender's
DID Document) can verify the signature independently. This is essential for `TransferOrderCredential`
messages, which represent irrevocable monetary commitments.

**DID-addressed routing**: Messages are addressed to DIDs rather than IP addresses or URLs.
This allows routing to change without breaking the addressing scheme, and allows receivers to
verify that a message was intended for them.

### 1.2 Scope

This document specifies:
1. The SVRN7 DIDComm protocol URI namespace.
2. All SVRN7 DIDComm message types and their payload schemas.
3. The required pack mode and key types.
4. The cross-Society transfer flow using DIDComm.
5. The overdraft draw DIDComm flow.
6. The DID Document resolution DIDComm flow.
7. The VC cross-Society query DIDComm flow.
8. Message processing requirements.
9. Idempotency requirements.

This document does not specify:
- The monetary accounting model (see [DRAFT-MONETARY]).
- The Web 7.0 architectural hierarchy (see [WEB70-ARCH]).
- Transport adapters (HTTP, WebSocket, etc.) — these are implementation concerns.

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

---

## 3. Terminology

- **DIDComm v2**: DIDComm Messaging version 2 as specified in [DIDCOMM-V2].

- **SignThenEncrypt**: A DIDComm pack mode in which the message body is first signed with the
  sender's Ed25519 key (producing a JWS), then the JWS is wrapped in an Anoncrypt JWE addressed
  to the recipient's X25519 key (derived from their Ed25519 key via [RFC7748]). The JWS signature
  survives decryption and is independently verifiable.

- **Authcrypt**: A DIDComm pack mode using ECDH-1PU (see [ECDH-1PU]). Authentication is
  embedded in the key derivation and cannot be extracted after decryption. NOT RECOMMENDED for
  monetary commitments in this ecosystem (see Section 4.2).

- **Ed25519 Messaging Key**: The Ed25519 key pair used for DIDComm signing and, via the
  birational map of [RFC7748], for X25519 key agreement.

- **Protocol URI**: A URN-like string identifying the protocol family and message type for a
  DIDComm message. All SVRN7 protocol URIs use the `svrn7.net` domain.

- **TransferId**: Blake3 hash of the canonical transfer JSON [DRAFT-MONETARY]. Used as the
  cross-Society idempotency key.

---

## 4. DIDComm Pack Mode

### 4.1 Required Mode: SignThenEncrypt

All SVRN7 DIDComm messages MUST use the SignThenEncrypt pack mode unless an explicit exception
is stated in this document (Section 4.3). The pack procedure is:

1. Construct the DIDComm plaintext message (Section 5).
2. Sign the serialised message body with the sender's Ed25519 private key using JWS EdDSA:
   ```
   jws = JWS-sign(message-body-bytes, sender-ed25519-private-key)
   ```
3. Set the message body to the JWS compact serialisation.
4. Encrypt the signed message to the recipient's X25519 public key using Anoncrypt
   (ECDH-ES+AES256KW with a fresh ephemeral key pair):
   ```
   jwe = ECDH-ES-AES256KW-encrypt(jws, recipient-x25519-public-key)
   ```
5. The packed message is the JWE compact serialisation.

X25519 public keys MUST be derived from Ed25519 public keys using the birational map defined
in [RFC7748] Section 4.1, with scalar clamping applied to the private key.

### 4.2 Why Not Authcrypt

Authcrypt (ECDH-1PU) provides sender authentication embedded in the key derivation. However,
the authentication is only verifiable by the recipient — it evaporates once the message is
decrypted, because reproducing the ECDH-1PU derivation requires the recipient's private key.

For `TransferOrderCredential` messages, which represent irrevocable monetary commitments by a
Society, non-repudiation is required: the originating Society MUST NOT be able to later deny
having issued the order. The JWS produced by SignThenEncrypt provides this: any party holding
the originating Society's Ed25519 public key (available from its DID Document) can verify the
signature independently, without the recipient's cooperation.

SignThenEncrypt MUST be used for all cross-Society monetary transfers. SignThenEncrypt is also
the default for all other SVRN7 DIDComm messages for consistency.

### 4.3 Exception: Anoncrypt for Fallback Responses

When a receiver cannot determine the sender's identity (e.g., in an error response where the
original sender's DID is unknown or unresolvable), the response MAY be packed using Anoncrypt
with an empty sender key. This is the only permitted use of Anoncrypt in this protocol.

### 4.4 Key Types

| Purpose | Algorithm | CESR Prefix |
|---------|-----------|-------------|
| Signing (JWS in SignThenEncrypt) | Ed25519 | `0D` |
| Key agreement (ECDH-ES in JWE) | X25519 (derived from Ed25519) | — |
| Transfer request signing | secp256k1 | `0B` |

Note that DIDComm messaging keys (Ed25519/X25519) are distinct from transfer signing keys
(secp256k1). Each entity MUST maintain both key types.

---

## 5. Protocol URI Namespace

### 5.0 URI Scheme Rationale

All SVRN7 DIDComm protocol type URIs use the `did:drn:` scheme rather than `https://`.
This is a deliberate architectural decision consistent with the Web 7.0 identity model.

DIDComm V2 requires the `@type` field to be an absolute URI per RFC 3986. The `did:drn:`
scheme is a valid absolute URI scheme. The choice of `did:drn:` over `https://` reflects
three properties of the SVRN7 ecosystem:

1. **Self-contained ecosystem**: Cross-ecosystem interoperability with non-SVRN7 DIDComm
   agents is not a goal. The ecosystem is intentionally closed — TDA-to-TDA only.

2. **Architectural coherence**: Protocol type URIs are Locator DID URLs
   (`draft-herman-did-w3c-drn-00` Section 5a) within the `did:drn:svrn7.net` namespace.
   `did:drn:svrn7.net` is the Identity DID of the SVRN7 protocol namespace. The path
   `/protocols/{family}/{version}/{message-type}` locates the specific protocol definition
   within that namespace. This is the same Identity DID / Locator DID URL distinction
   used for all other `did:drn` identifiers in the SVRN7 ecosystem.

3. **No HTTP dependency**: The `did:drn:` scheme does not require an HTTP server to be
   available at any URL. The URI is a globally unique identifier, not a dereferenceable
   endpoint.

The canonical form is:

```
did:drn:svrn7.net/protocols/{family}/{version}/{message-type}
```

Implementations MUST reject messages with `@type` values using the `https://svrn7.net/`
prefix or any other scheme. Only `did:drn:svrn7.net/protocols/` is the authorised
namespace for standard SVRN7 protocol type URIs.

### 5.1 Complete URI Registry

| Constant | URI | Direction |
|----------|-----|-----------|
| `TransferRequest` | `did:drn:svrn7.net/protocols/transfer/1.0/request` | Citizen → Society |
| `TransferReceipt` | `did:drn:svrn7.net/protocols/transfer/1.0/receipt` | Society → Citizen |
| `TransferOrder` | `did:drn:svrn7.net/protocols/transfer/1.0/order` | Society → Society |
| `TransferOrderReceipt` | `did:drn:svrn7.net/protocols/transfer/1.0/order-receipt` | Society → Society |
| `OverdraftDrawRequest` | `did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-request` | Society → Federation |
| `OverdraftDrawReceipt` | `did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-receipt` | Federation → Society |
| `EndowmentTopUp` | `did:drn:svrn7.net/protocols/endowment/1.0/top-up` | Federation → Society |
| `SupplyUpdate` | `did:drn:svrn7.net/protocols/supply/1.0/update` | Federation → Societies |
| `DidResolveRequest` | `did:drn:svrn7.net/protocols/did/1.0/resolve-request` | Society → Society |
| `DidResolveResponse` | `did:drn:svrn7.net/protocols/did/1.0/resolve-response` | Society → Society |
| `VcResolveBySubjectRequest` | `did:drn:svrn7.net/protocols/vc/1.0/resolve-by-subject-request` | Society → Society |
| `VcResolveBySubjectResponse` | `did:drn:svrn7.net/protocols/vc/1.0/resolve-by-subject-response` | Society → Society |

---

## 6. Message Structure

All SVRN7 DIDComm messages MUST conform to the following plaintext structure before packing:

```json
{
  "type": "<protocol-uri>",
  "id":   "<uuid>",
  "from": "<sender-did>",
  "to":   "<recipient-did>",
  "body": { ... }
}
```

- `type`: REQUIRED. One of the URIs in Section 5.1.
- `id`: REQUIRED. A UUID uniquely identifying this message instance.
- `from`: REQUIRED. The sender's DID. MUST be resolvable.
- `to`: REQUIRED. The recipient's DID. MUST be resolvable.
- `body`: REQUIRED. Message-type-specific payload (see Section 7).

---

## 7. Message Types and Payloads

### 7.1 TransferRequest (Citizen → Society)

Sent by a citizen to their Society to initiate a transfer. The Society validates the request
per the eight-step pipeline [DRAFT-MONETARY] and returns a `TransferReceipt`.

**Body schema:**
```json
{
  "payerDid":    "<string>",
  "payeeDid":    "<string>",
  "amountGrana": <int64>,
  "nonce":       "<string>",
  "timestamp":   "<ISO-8601-UTC>",
  "signature":   "<CESR-secp256k1>",
  "memo":        "<string | null>"
}
```

### 7.2 TransferReceipt (Society → Citizen)

Returned by the Society in response to a `TransferRequest`. Indicates success or failure.

**Body schema:**
```json
{
  "success":      <bool>,
  "transferId":   "<string | null>",
  "errorCode":    "<string | null>",
  "errorMessage": "<string | null>"
}
```

### 7.3 TransferOrder (Society → Society)

Sent by the originating Society to the target Society to initiate a cross-Society transfer.
Contains the `TransferOrderCredential` VC as the body payload. The `TransferId` serves as
the idempotency key.

**Body schema:**
```json
{
  "transferId":        "<string>",
  "payerDid":          "<string>",
  "payeeDid":          "<string>",
  "amountGrana":       <int64>,
  "originSocietyDid":  "<string>",
  "targetSocietyDid":  "<string>",
  "epoch":             <int>,
  "nonce":             "<string>",
  "timestamp":         "<ISO-8601-UTC>",
  "expiresAt":         "<ISO-8601-UTC>",
  "orderVcJwt":        "<JWT string — the TransferOrderCredential>"
}
```

**Idempotency**: The receiving Society MUST check whether a VC with `VcId = transferId`
already exists in its VC registry before processing. If it does, the Society MUST return the
cached `TransferOrderReceipt` without re-crediting the payee.

### 7.4 TransferOrderReceipt (Society → Society)

Returned by the receiving Society to the originating Society upon successful processing of a
`TransferOrder`. Contains the `TransferReceiptCredential` VC.

**Body schema:**
```json
{
  "transferId":        "<string>",
  "payeeDid":          "<string>",
  "creditedGrana":     <int64>,
  "targetSocietyDid":  "<string>",
  "creditedAt":        "<ISO-8601-UTC>",
  "receiptVcJwt":      "<JWT string — the TransferReceiptCredential>"
}
```

### 7.5 OverdraftDrawRequest (Society → Federation)

Sent by a Society to the Federation when its wallet balance falls below
`CitizenEndowmentGrana`. The Federation MUST process this synchronously and return an
`OverdraftDrawReceipt` within the configured timeout.

**Body schema:**
```json
{
  "societyDid":       "<string>",
  "drawAmountGrana":  <int64>,
  "drawCount":        <int>,
  "reason":           "<string>",
  "requestedAt":      "<ISO-8601-UTC>"
}
```

### 7.6 OverdraftDrawReceipt (Federation → Society)

Returned by the Federation confirming the overdraft draw. The Federation has transferred
`drawAmountGrana` from the Federation wallet to the Society wallet.

**Body schema:**
```json
{
  "societyDid":            "<string>",
  "drawnAmountGrana":      <int64>,
  "newSocietyBalanceGrana":<int64>,
  "drawCount":             <int>,
  "processedAt":           "<ISO-8601-UTC>",
  "receiptVcJwt":          "<JWT string — the OverdraftDrawReceipt VC>"
}
```

### 7.7 EndowmentTopUp (Federation → Society)

Sent by the Federation to proactively reduce a Society's outstanding overdraft balance.

**Body schema:**
```json
{
  "societyDid":       "<string>",
  "topUpAmountGrana": <int64>,
  "reason":           "<string>",
  "sentAt":           "<ISO-8601-UTC>"
}
```

### 7.8 SupplyUpdate (Federation → Societies)

Broadcast by the Federation to all registered Societies when the total supply is updated.
Informs Societies of the new supply figure.

**Body schema:**
```json
{
  "newTotalSupplyGrana": <int64>,
  "previousSupplyGrana": <int64>,
  "governanceRef":       "<string>",
  "updatedAt":           "<ISO-8601-UTC>"
}
```

### 7.9 DidResolveRequest (Society → Society)

Sent by a Society to the owning Society of a foreign DID method name to request DID Document
resolution for a specific DID.

**Body schema:**
```json
{
  "did":         "<string>",
  "requestedAt": "<ISO-8601-UTC>"
}
```

### 7.10 DidResolveResponse (Society → Society)

Returned by the owning Society with the resolved DID Document, or an error code.

**Body schema:**
```json
{
  "did":           "<string>",
  "found":         <bool>,
  "errorCode":     "<string | null>",
  "didDocument":   { ... } | null
}
```

Error codes: `"notFound"`, `"methodNotSupported"`, `"deactivated"`, `"resolutionTimeout"`.

### 7.11 VcResolveBySubjectRequest (Society → Society)

Sent during a cross-Society VC fan-out to request all VCs for a given subject DID.

**Body schema:**
```json
{
  "subjectDid":  "<string>",
  "requestedAt": "<ISO-8601-UTC>"
}
```

### 7.12 VcResolveBySubjectResponse (Society → Society)

Returned with the set of VCs for the requested subject DID held by the responding Society.

**Body schema:**
```json
{
  "subjectDid": "<string>",
  "vcJwts":     ["<JWT>", ...],
  "respondedAt":"<ISO-8601-UTC>"
}
```

---

## 8. Cross-Society Transfer Flow

### 8.1 Complete Flow Diagram

```
Originating Society                    Receiving Society
        |                                      |
        | 1. Validate 8 steps                  |
        | 2. Debit payer UTXO                  |
        | 3. Issue TransferOrderCredential VC   |
        | 4. Pack SignThenEncrypt               |
        | 5. Dispatch TransferOrder ─────────► |
        |                                      | 6. Unpack + verify JWS
        |                                      | 7. Idempotency check
        |                                      | 8. Credit payee UTXO
        |                                      | 9. Issue TransferReceiptCredential
        |                                      |10. Append CrossSocietyTransferCredit
        |                                      |11. Pack SignThenEncrypt
        | 12. Receive TransferOrderReceipt ◄── |
        | 13. Append CrossSocietyTransferSettled|
        |                                      |
```

### 8.2 Originating Society Obligations

The originating Society MUST:
1. Complete all eight validation steps per [DRAFT-MONETARY] including Step 8 (Society Membership).
2. Mark the payer's UTXOs as spent before dispatching the `TransferOrder`.
3. Append `CrossSocietyTransferDebit` to its Merkle log before dispatching.
4. Pack the `TransferOrder` using SignThenEncrypt with its Ed25519 messaging key.
5. Dispatch the packed message to the receiving Society via the configured transport adapter.
6. Await a `TransferOrderReceipt` response.
7. Upon receipt, append `CrossSocietyTransferSettled` to its Merkle log.
8. If no receipt arrives within the configured timeout, log the unresolved transfer for
   reconciliation. The payer's UTXO MUST NOT be un-spent retroactively.

### 8.3 Receiving Society Obligations

The receiving Society MUST:
1. Unpack the incoming DIDComm message using the Society's Ed25519 messaging private key.
2. Verify the JWS signature against the originating Society's Ed25519 public key (from its DID Document).
3. Check idempotency: if a VC with `VcId = transferId` already exists, return the cached receipt.
4. Credit the payee by creating a new UTXO.
5. Issue a `TransferReceiptCredential` VC with `VcId = transferId`.
6. Append `CrossSocietyTransferCredit` to its Merkle log.
7. Pack and return the `TransferOrderReceipt` using SignThenEncrypt.

---

## 9. Overdraft Draw Flow

```
Society                                Federation
   |                                       |
   | 1. Check ceiling                      |
   | 2. Pack OverdraftDrawRequest          |
   | 3. Dispatch SignThenEncrypt ─────────► |
   |                                       | 4. Verify Society registration
   |                                       | 5. Transfer DrawAmountGrana (UTXO)
   |                                       | 6. Issue OverdraftDrawReceipt VC
   |                                       | 7. Pack SignThenEncrypt
   | 8. Receive OverdraftDrawReceipt ◄──── |
   | 9. Update overdraft accounting        |
   |10. Proceed with citizen registration  |
   |                                       |
```

The Society MUST set a timeout for step 8 (RECOMMENDED: 30 seconds). If the timeout expires,
the Society MUST raise `FederationUnavailableException` and abort the citizen registration.
No UTXO is created and no citizen is registered on a timeout.

---

## 10. DID Document Resolution Flow

```
Requesting Society                    Owning Society
       |                                    |
       | 1. Look up method name in           |
       |    Federation registry             |
       | 2. Pack DidResolveRequest          |
       | 3. Dispatch SignThenEncrypt ───────►|
       |                                    | 4. Look up DID in local registry
       |                                    | 5. Pack DidResolveResponse
       | 6. Receive DidResolveResponse ◄─── |
       | 7. Cache in local registry         |
       |                                    |
```

If the owning Society does not respond within `FederationRoundTripTimeout`, the requesting
Society MUST return `errorCode = "resolutionTimeout"`. The caller SHOULD retry; the local cache
will be populated when the DIDComm response eventually arrives and is processed by the inbox
service.

---

## 11. Cross-Society VC Fan-Out Flow

```
Requesting Society                Society A        Society B     ...Society N
       |                              |                |               |
       | 1. Identify all known        |                |               |
       |    Societies                 |                |               |
       | 2. Pack VcResolveBySubject  |                |               |
       |    Request for each         |                |               |
       | 3. Dispatch in parallel ───►|               ─►              ─►
       |                              | 4. Query local |               |
       |                              |    VC registry  |               |
       |                              | 5. Pack response|               |
       | 6. Receive responses ◄──── (within timeout window)
       | 7. Build CrossSocietyVcQueryResult:
       |    - Records: all VCs received
       |    - RespondedSocieties: those that replied
       |    - TimedOutSocieties: those that did not
       |
```

The requesting Society MUST apply an individual timeout to each fan-out request. Partial results
MUST be returned when some Societies time out. The `CrossSocietyVcQueryResult` MUST include
both `RespondedSocieties` and `TimedOutSocieties` to allow callers to assess completeness.

---

## 12. Message Processing Requirements

### 12.1 Inbox Processing

Implementations MUST provide an inbox processing service that:
- Receives packed DIDComm messages from the transport layer.
- Unpacks each message using the deployment's Ed25519 messaging private key.
- Routes the message to the appropriate handler based on the `type` field.
- Handles `TransferOrder`, `TransferOrderReceipt`, `OverdraftDrawReceipt`, `DidResolveRequest`,
  `DidResolveResponse`, `VcResolveBySubjectRequest`, `VcResolveBySubjectResponse`, and
  `EndowmentTopUp` message types.

### 12.2 Duplicate Detection

The inbox service MUST detect and discard duplicate messages. The message `id` field MUST be
used as the deduplication key. Implementations MUST maintain a persistent record of processed
message IDs within a configurable deduplication window (RECOMMENDED: 48 hours).

### 12.3 Unknown Message Types

Messages with an unrecognised `type` field SHOULD be logged and discarded. The inbox service
MUST NOT raise an unhandled exception for unknown message types.

### 12.4 Signature Verification

The inbox service MUST verify the JWS signature on every SignThenEncrypt message before
passing it to the handler. Signature verification MUST use the sender's Ed25519 public key
retrieved from their DID Document. Messages with invalid or unverifiable signatures MUST be
discarded.

---

## 13. Idempotency Requirements

### 13.1 Cross-Society Transfers

The receiving Society MUST implement idempotency for `TransferOrder` messages using `transferId`
as the deduplication key. The implementation MUST check whether a VC with `VcId = transferId`
exists in its VC registry before crediting the payee.

If the transfer has already been processed:
- The receiving Society MUST return the cached `TransferOrderReceipt` without re-crediting.
- The receiving Society MUST NOT create a duplicate UTXO.
- The receiving Society MUST NOT append a duplicate Merkle log entry.

### 13.2 Overdraft Draws

Overdraft draw requests are not inherently idempotent. The Federation MUST process each
`OverdraftDrawRequest` as a distinct draw event. The Society is responsible for not sending
duplicate draw requests by maintaining accurate overdraft accounting locally.

---

## 14. Transport Adapter

This specification defines the DIDComm message structure but does not mandate a specific
transport. Implementations MUST provide a pluggable transport adapter. The adapter is responsible
for:
- Sending packed DIDComm messages to recipient endpoints.
- Delivering incoming packed messages to the inbox processing service.

RECOMMENDED transport: HTTPS POST to a well-known DIDComm service endpoint registered in the
recipient's DID Document:

```json
"service": [
  {
    "id": "<did>#didcomm",
    "type": "DIDCommMessaging",
    "serviceEndpoint": "https://example.org/didcomm"
  }
]
```

The transport MUST use TLS 1.2 or higher. Certificate transparency [RFC6962] is RECOMMENDED
for all HTTPS endpoints.

---

## 15. Security Considerations

### 15.1 SignThenEncrypt vs Authcrypt
See Section 4.2. SignThenEncrypt MUST be used for all monetary commitments. Implementations
that use Authcrypt for `TransferOrder` messages are non-conformant.

### 15.2 Replay Prevention
Each DIDComm message carries a unique `id`. The inbox service MUST deduplicate on `id` within
the deduplication window. This prevents replay of packed DIDComm messages at the messaging layer,
complementing the nonce replay prevention at the monetary protocol layer [DRAFT-MONETARY].

### 15.3 Transport Security
All transport adapters MUST use TLS 1.2 or higher. The DIDComm encryption layer provides
confidentiality independently of transport security, but transport-level TLS provides an
additional defence-in-depth layer and prevents passive traffic analysis.

### 15.4 DID Document Freshness
Implementations MUST not cache sender DID Documents indefinitely. A cached DID Document used
for signature verification SHOULD be re-fetched if it is older than a configurable freshness
threshold (RECOMMENDED: 1 hour). This ensures that key rotation is reflected promptly.

---

## 16. Privacy Considerations

### 16.1 SignThenEncrypt and Sender Visibility
SignThenEncrypt signs the message before encryption. The JWS is visible to the recipient after
decryption. Any party who later obtains the recipient's private key can attribute the message
to the sender. Implementations MUST protect recipient private keys accordingly.

### 16.2 Metadata Minimisation
The `from` field in the DIDComm message header reveals the sender's DID to the recipient.
Implementations handling privacy-sensitive use cases MAY omit the `from` field and use Anoncrypt
(per Section 4.3) where sender anonymity is required and non-repudiation is not.

---

## 17. IANA Considerations

This document has no IANA actions.

The protocol URI namespace `did:drn:svrn7.net/protocols/` is owned by the Web 7.0 Foundation
and is not an IANA registry. Implementations MUST NOT use this namespace for protocol types
not defined in this document or future revisions thereof.

---

## 18. References

### Normative
- [RFC2119] Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174] Leiba, B. Ambiguity of Uppercase vs Lowercase in RFC 2119 Key Words. May 2017.
- [RFC7748] Langley, A. et al. Elliptic Curves for Diffie-Hellman Key Agreement. January 2016.
- [RFC6962] Laurie, B. et al. Certificate Transparency. June 2013.
- [DIDCOMM-V2] Hardman, D. et al. DIDComm Messaging v2. https://identity.foundation/didcomm-messaging/spec/.
- [ECDH-1PU] Madden, N. Public Key Authenticated Encryption for JOSE: ECDH-1PU. draft-madden-jose-ecdh-1pu.
- [W3C.DID-CORE] Sporny, M. et al. Decentralized Identifiers (DIDs) v1.0. W3C Recommendation, 2022.
- [W3C.VC-DATA-MODEL] Sporny, M. et al. VC Data Model v2.0. W3C Recommendation, 2024.

### Informative
- [DRAFT-MONETARY] Herman, M. SOVRONA (SVRN7) Monetary Transfer Protocol. draft-herman-svrn7-monetary-protocol-00.
- [WEB70-ARCH] Herman, M. Web 7.0 Digital Society Architecture. draft-herman-web7-society-architecture-00.
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
