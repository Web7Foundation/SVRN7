# Web 7.0 Pando DID Document Resolution Protocol
# draft-herman-did-resolution-protocol-00
# Author: M. Herman, Web 7.0 Foundation
# Published: June 2026

Internet-Draft: draft-herman-did-resolution-protocol-00
Published:      June 2026
Expires:        December 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Web 7.0 Foundation Governance Council
Related:        draft-herman-web7-society-architecture-00
                draft-herman-did-w3c-drn-00
                draft-herman-didcomm-svrn7-transfer-00
                draft-herman-drn-resource-addressing-00

---

## Abstract

This document specifies the DID Document Resolution Protocol for the Web 7.0 Pando
platform. The protocol defines how a Trusted Digital Assistant (TDA) resolves a
Decentralized Identifier (DID) to a DID Document within a three-tier hierarchy of
Federation, Society, and Citizen TDAs. Resolution always attempts a local registry
lookup first. On a local miss, the requesting TDA escalates the request up the
hierarchy using DIDComm V2 correlated async relay — a pattern in which each
intermediate TDA dispatches a `did-resolve-request`, holds a pending correlation
entry, and forwards the `did-resolve-response` onward when the correlated reply
arrives in its inbox. The protocol ensures every requesting TDA receives a
definitive response — found or not found — without requiring retry logic or
asynchronous cache warming at the application layer.

---

## 1. Introduction

The Web 7.0 Pando platform organises TDAs into a three-tier hierarchy:

- **Federation** — the top-level authority; maintains a registry of Societies and
  their DID method names.
- **Society** — a community that is registered with a Federation; maintains a
  registry of its Citizens.
- **Citizen** — an individual TDA registered with a Society.

Every TDA also holds a **Wanderer** identity, which is its initial and permanent
baseline role regardless of what other roles are acquired later.

DID Documents are the primary identity artifacts in this hierarchy. Their reliable
resolution across tier boundaries is a prerequisite for all DIDComm protocols,
Verifiable Credential verification, and transfer authorization.

### 1.1 Motivation

Prior approaches to cross-tier DID Document resolution in the platform used an
asynchronous cache pattern: a TDA dispatched a `did-resolve-request` and immediately
returned `notFound`, relying on a background inbox processor to populate a local
cache when the remote response eventually arrived. Callers were required to retry
resolution later and had no way to know when the cache would be ready.

This document replaces that pattern with a correlated async relay protocol that
is consistent with DIDComm V2's native asynchronous messaging model while
delivering a complete, correlated response to the original requester.

### 1.2 Scope

This document specifies:

1. The social architecture governing DID Document ownership and stored-copy
   distribution across the three-tier hierarchy.
2. The local-first resolution rule applied by every TDA regardless of role.
3. The escalation rules that apply on a local miss, differentiated by role.
4. The DIDComm V2 protocol messages (`did-resolve-request` and `did-resolve-response`),
   including the correlation fields that enable multi-hop relay.
5. Error codes returned when resolution cannot be completed.

This document does not specify:

- DID method syntax — see [DRN-DID] and [DRN-RESOURCE].
- The DID Document data model — see W3C DID Core.
- The Federation registration protocol — see [SOCIETY-ARCH].
- Verifiable Credential resolution — that is a separate protocol within the
  `Svrn7.Identity` LOBE.

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT,
RECOMMENDED, NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described
in BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all capitals as
shown here.

---

## 3. Terminology

- **TDA** — Trusted Digital Assistant. A Kestrel-hosted process that manages one
  or more DID identities and handles DIDComm messages via a LOBE runtime.
- **DID Document** — A W3C DID Core document describing a subject's verification
  methods and service endpoints.
- **Local Registry** — The `IDidDocumentRegistry` (LiteDB-backed) maintained by a
  TDA containing DID Documents it owns or holds as stored copies.
- **Wanderer** — The baseline role every TDA holds from first boot. A Wanderer TDA
  has no parent tier to escalate to.
- **Citizen** — A TDA that has been registered with a Society. Its parent tier is
  its Society.
- **Society** — A TDA that has been registered with a Federation. Its parent tier
  is its Federation.
- **Federation** — The top-tier TDA. It has no parent tier.
- **Stored Copy** — A DID Document written to a TDA's local registry upon membership
  registration, distinct from the authoritative copy held by the subject TDA.
- **Correlated Async Relay** — The multi-hop pattern in which each intermediate TDA
  dispatches a DIDComm request, holds a pending correlation entry keyed on a
  correlation identifier, and forwards the response when the correlated reply
  arrives in its DIDComm inbox.
- **Pending Correlation Entry** — Application-layer state (e.g. a
  `TaskCompletionSource` keyed on `originalRequestId`) held by an intermediate TDA
  while awaiting a `did-resolve-response` from the next hop.

---

## 4. Social Architecture and Stored Copies

### 4.1 Role Acquisition

Every TDA MUST be created in the Wanderer role. A TDA acquires additional roles
through explicit registration events. Roles are additive: a TDA that becomes a
Society retains its Wanderer DID Document alongside its Society DID Document.

| Registration Event | DID Documents Held Locally | DID Document Copy Sent To |
|---|---|---|
| TDA first boot | Wanderer | — |
| Initialize as Federation | Wanderer + Federation | — |
| Register Society to Federation | Wanderer + Society | Federation |
| Register Citizen to Society | Wanderer + Citizen | Society |

### 4.2 Stored-Copy Distribution

When a Society registers with a Federation, the Society TDA MUST send a copy of its
Society DID Document to the Federation TDA. The Federation TDA MUST store this copy
in its local registry.

When a Citizen registers with a Society, the Citizen TDA MUST send a copy of its
Citizen DID Document to the Society TDA. The Society TDA MUST store this copy in
its local registry.

### 4.3 Resolution Consequence

The stored-copy model has the following consequence for resolution:

- A **Society's** local registry holds its own DID Documents plus copies of all
  its registered Citizens' DID Documents.
- A **Federation's** local registry holds its own DID Documents plus copies of all
  its registered Societies' DID Documents.

As a result, the majority of resolution requests are satisfied locally without a
network hop. Cross-tier escalation is only required when a TDA is asked to resolve
a DID that neither it nor its membership copies cover.

---

## 5. Resolution Protocol

### 5.1 Local-First Rule

Every TDA receiving a `did-resolve-request` MUST attempt local registry resolution
first, regardless of the TDA's role. If the local registry returns a valid DID
Document with status `Active` or `Suspended`, the TDA MUST issue a
`did-resolve-response` immediately. Resolution is complete.

A DID Document with status `Deactivated` MUST be returned with `found: true` and
the document included. The `Deactivated` status is expressed within the document.
Deactivated documents are never removed from the local registry.

### 5.2 Escalation Rules

If the local registry returns `notFound`, the TDA MUST apply the following
escalation rule based on its own role.

#### 5.2.1 Wanderer

A Wanderer TDA MUST return `notFound`. A Wanderer has no parent tier and MUST NOT
attempt cross-tier escalation.

#### 5.2.2 Citizen

A Citizen TDA MUST dispatch a `did-resolve-request` to its Society TDA. The Citizen
MUST set `originalRequesterDid` to its own DID and `originalRequestId` to the
`requestId` of the inbound request. The Citizen MUST hold a pending correlation
entry keyed on `originalRequestId`.

When the `did-resolve-response` arrives — whether `found: true` or `found: false`
— the Citizen MUST match it against the pending correlation entry and relay the
response to the original requester.

#### 5.2.3 Society

A Society TDA MUST dispatch a `did-resolve-request` to its Federation TDA. The
Society MUST propagate the `originalRequesterDid` and `originalRequestId` fields
from the inbound request unchanged. The Society MUST hold a pending correlation
entry keyed on `originalRequestId`.

When the `did-resolve-response` arrives, the Society MUST match it against the
pending correlation entry and relay the response to `originalRequesterDid`.

#### 5.2.4 Federation

A Federation TDA MUST look up which Society owns the DID method of the requested
DID using its Federation method registry. If no Society is registered for that
method, the Federation MUST immediately return `notFound` with error code
`methodNotSupported`.

If a Society is found, the Federation MUST dispatch a `did-resolve-request` to
that Society, propagating `originalRequesterDid` and `originalRequestId` unchanged.
The Federation MUST hold a pending correlation entry keyed on `originalRequestId`.

When the `did-resolve-response` arrives, the Federation MUST relay it to
`originalRequesterDid` (the requesting Society).

### 5.3 Correlated Async Relay

DIDComm V2 is an asynchronous messaging protocol. A `did-resolve-response` arrives
as a separate inbound DIDComm message to the intermediate TDA's inbox; it is not
delivered in-band on the same transport connection as the outbound request.

Each intermediate TDA MUST maintain a pending correlation store keyed on
`originalRequestId`. On receipt of a `did-resolve-response`, the TDA MUST:

1. Extract `originalRequestId` from the response body.
2. Look up the matching pending correlation entry.
3. If found: relay the response to `originalRequesterDid` and remove the entry.
4. If not found: log a warning and discard the message.

DIDComm V2's `thid` (thread ID) and `pthid` (parent thread ID) header fields MAY
be used in addition to `originalRequestId` for thread correlation. Implementations
MUST treat `originalRequestId` as the authoritative correlation key.

### 5.4 Resolution Flow

```
TDA receives did-resolve-request
│
├── Step 1: Query local IDidDocumentRegistry
│     └── FOUND (Active or Suspended or Deactivated)
│           → issue did-resolve-response to requester             [terminal]
│
└── NOT FOUND — apply escalation rule for own role:
      │
      ├── Wanderer
      │     → did-resolve-response { found: false, errorCode: "notFound" }
      │                                                            [terminal]
      │
      ├── Citizen
      │     → dispatch did-resolve-request to Society TDA
      │       [hold pending correlation on originalRequestId]
      │       ← Society's did-resolve-response arrives in inbox
      │     → relay did-resolve-response to original requester    [terminal]
      │
      ├── Society
      │     → dispatch did-resolve-request to Federation TDA
      │       [hold pending correlation on originalRequestId]
      │       ← Federation's did-resolve-response arrives in inbox
      │     → relay did-resolve-response to originalRequesterDid  [terminal]
      │
      └── Federation
            → look up target Society in method registry
            │
            ├── NOT REGISTERED
            │     → did-resolve-response { found: false,
            │         errorCode: "methodNotSupported" }            [terminal]
            │
            └── REGISTERED
                  → dispatch did-resolve-request to target Society
                    [hold pending correlation on originalRequestId]
                    ← Society's did-resolve-response arrives in inbox
                  → relay did-resolve-response to originalRequesterDid
                                                                   [terminal]
```

---

## 6. Protocol Messages

### 6.1 `did-resolve-request`

This message is sent by a TDA initiating a resolution request, and propagated
unchanged (except for the `from` and `to` fields) by intermediate TDAs when
escalating to the next tier.

```
type: did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-request
```

**DIDComm envelope:**

```json
{
  "typ":  "application/didcomm-plain+json",
  "id":   "<message-id>",
  "type": "did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-request",
  "thid": "<thread-id>",
  "from": "<dispatching TDA DID>",
  "to":   ["<target TDA DID>"],
  "body": {
    "requestedDid":         "did:drn:...",
    "requestId":            "<uuid — unique to this hop>",
    "originalRequesterDid": "<DID of the TDA that originated the request>",
    "originalRequestId":    "<uuid — set once by the originating TDA, never changed>"
  }
}
```

**Body field semantics:**

- `requestedDid` (REQUIRED) — The DID to resolve.
- `requestId` (REQUIRED) — A UUID unique to this hop's dispatch. Each relay hop
  generates a new `requestId` for its own outbound message.
- `originalRequesterDid` (REQUIRED) — The DID of the TDA that originated the
  resolution request. Set once by the initiating TDA and propagated unchanged.
- `originalRequestId` (REQUIRED) — The `requestId` from the originating TDA's
  initial request. Used as the correlation key throughout the relay chain. Set once
  and propagated unchanged.

When the originating TDA sends the first request, `originalRequesterDid` MUST be
set to its own DID and `originalRequestId` MUST be set to the same value as
`requestId`.

### 6.2 `did-resolve-response`

This message is returned by the TDA that resolves (or fails to resolve) the DID.
Intermediate TDAs relay this message toward `originalRequesterDid`.

```
type: did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-response
```

**DIDComm envelope:**

```json
{
  "typ":  "application/didcomm-plain+json",
  "id":   "<message-id>",
  "type": "did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-response",
  "thid": "<thread-id>",
  "from": "<responding or relaying TDA DID>",
  "to":   ["<next-hop or original requester DID>"],
  "body": {
    "requestedDid":      "did:drn:...",
    "found":             true,
    "didDocument":       { },
    "errorCode":         null,
    "resolvedAt":        "2026-06-17T00:00:00Z",
    "originalRequestId": "<uuid>"
  }
}
```

**Body field semantics:**

- `requestedDid` (REQUIRED) — Echo of the DID that was requested.
- `found` (REQUIRED) — `true` if a DID Document was located; `false` otherwise.
- `didDocument` (REQUIRED when `found` is `true`, MUST be `null` when `false`) —
  The resolved DID Document.
- `errorCode` (OPTIONAL) — Present when `found` is `false`. See Section 7.
- `resolvedAt` (REQUIRED) — ISO 8601 timestamp of the resolution attempt.
- `originalRequestId` (REQUIRED) — Echoed from the `did-resolve-request`. Used by
  each intermediate TDA to match the response to its pending correlation entry.

---

## 7. Error Codes

All error codes follow W3C DID Resolution conventions.

| Error Code | Condition |
|---|---|
| `notFound` | The requested DID was not found in any reachable registry |
| `methodNotSupported` | The Federation has no Society registered for the requested DID method name |
| `invalidDid` | The DID string could not be parsed (malformed syntax) |
| `resolutionTimeout` | The target TDA did not respond within the configured `FederationRoundTripTimeout` |

A TDA MUST NOT return a response with `found: true` and a non-null `errorCode`.

---

## 8. End-to-End Resolution Scenarios

### 8.1 Citizen Resolves Its Own DID

```
Citizen A → local HIT → did-resolve-response to self
```

### 8.2 Citizen Resolves a Citizen in the Same Society

Society holds a stored copy of all its Citizens' DID Documents.

```
Citizen A: local miss
  → did-resolve-request to Society
      Society: local HIT (stored copy of Citizen B)
        → did-resolve-response to Citizen A
```

### 8.3 Citizen Resolves a Citizen in a Different Society

```
Citizen A: local miss
  → did-resolve-request to Society A
      Society A: local miss
        → did-resolve-request to Federation
            Federation: local miss
              → did-resolve-request to Society B
                  Society B: local HIT (holds Citizen B)
                    → did-resolve-response to Federation
                        → relayed to Society A
                            → relayed to Citizen A
```

### 8.4 Citizen Resolves a Society DID

Federation holds a stored copy of all its Societies' DID Documents.

```
Citizen A: local miss
  → did-resolve-request to Society A
      Society A: local miss
        → did-resolve-request to Federation
            Federation: local HIT (stored copy of Society B)
              → did-resolve-response to Society A
                  → relayed to Citizen A
```

### 8.5 Society Resolves a Citizen DID in Another Society

```
Society A: local miss
  → did-resolve-request to Federation
      Federation: local miss
        → did-resolve-request to Society B
            Society B: local HIT
              → did-resolve-response to Federation
                  → relayed to Society A
```

### 8.6 Unregistered DID Method

```
Citizen A: local miss
  → did-resolve-request to Society A
      Society A: local miss
        → did-resolve-request to Federation
            Federation: method not in registry
              → did-resolve-response { found: false, errorCode: "methodNotSupported" }
                  → relayed to Society A → relayed to Citizen A
```

---

## 9. Timeout and Partial Failure

Each TDA that holds a pending correlation entry MUST apply a timeout equal to
`FederationRoundTripTimeout` (configurable; default implementation-defined). If the
correlated `did-resolve-response` does not arrive within this window, the TDA MUST:

1. Remove the pending correlation entry.
2. Return a `did-resolve-response` with `found: false` and
   `errorCode: "resolutionTimeout"` to the original requester.

Intermediate TDAs MUST NOT hold a pending correlation entry indefinitely. A
`resolutionTimeout` response informs the original requester that escalation was
attempted but the target did not respond, allowing the requester to retry or fail
gracefully.

---

## 10. Security Considerations

### 10.1 DIDComm Message Integrity

All `did-resolve-request` and `did-resolve-response` messages MUST be transmitted
as DIDComm V2 envelopes. Implementations SHOULD use SignThenEncrypt packing where
key material is available. Plaintext packing MAY be used for intra-network
communication where the transport layer provides equivalent integrity guarantees
(e.g. mTLS).

### 10.2 Stored-Copy Authenticity

Stored copies of DID Documents MUST be verified against the sender's DID Document
and signing key material at the time of registration. A TDA MUST NOT store a DID
Document copy without verifying that it was sent by the DID subject or an
authorized delegate.

### 10.3 Relay Chain Integrity

An intermediate TDA MUST verify that the `did-resolve-response` it receives
originated from the TDA it sent the corresponding `did-resolve-request` to. The
`from` field of the response envelope MUST match the `to` field of the outbound
request. Mismatches MUST be logged and the response MUST be discarded.

### 10.4 `originalRequesterDid` Spoofing

A malicious TDA could set `originalRequesterDid` to a DID other than its own in
order to cause a response to be delivered to an unintended recipient. Implementations
SHOULD validate that `originalRequesterDid` matches the `from` field of the inbound
`did-resolve-request` envelope. If they do not match, the request MUST be rejected.

---

## 11. IANA Considerations

This document has no IANA actions.

---

## 12. References

### 12.1 Normative References

- **[RFC2119]** Bradner, S., "Key words for use in RFCs to Indicate Requirement
  Levels", BCP 14, RFC 2119, March 1997.
- **[RFC8174]** Leiba, B., "Ambiguity of Uppercase vs Lowercase in RFC 2119 Key
  Words", BCP 14, RFC 8174, May 2017.
- **[W3C-DID]** W3C, "Decentralized Identifiers (DIDs) v1.0", July 2022.
  https://www.w3.org/TR/did-core/
- **[DIDCOMM-V2]** DIF, "DIDComm Messaging v2.0", 2022.
  https://identity.foundation/didcomm-messaging/spec/

### 12.2 Informative References

- **[DRN-DID]** Herman, M., "Decentralized Resource Name (DRN) DID Method",
  draft-herman-did-w3c-drn-00, March 2026.
- **[DRN-RESOURCE]** Herman, M., "DRN Resource Addressing",
  draft-herman-drn-resource-addressing-00.
- **[SOCIETY-ARCH]** Herman, M., "Web 7.0 Digital Society Architecture",
  draft-herman-web7-society-architecture-00, April 2026.

---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
