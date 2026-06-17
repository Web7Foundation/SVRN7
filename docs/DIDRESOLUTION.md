# Web 7.0 Pando — DID Document Resolution Design

---

## Social Architecture

### Roles and DID Documents

Every TDA is created as a **Wanderer** and always retains its Wanderer DID Document.
Joining a Federation or Society adds a second DID Document in the new role.

| Event | Local DID Documents | Copy distributed to |
|---|---|---|
| TDA first boot | Wanderer | — |
| Initialize as Federation | Wanderer + Federation | — |
| Register Society to Federation | Wanderer + Society | Federation (stored) |
| Register Citizen to Society | Wanderer + Citizen | Society (stored) |

### Stored-Copy Consequence for Resolution

- A **Society's** local registry holds its own DID Documents **plus copies of all its Citizens' DID Documents**.
- A **Federation's** local registry holds its own DID Documents **plus copies of all its registered Societies' DID Documents**.

This means most resolution requests are satisfied locally without a network hop.

---

## Resolution Logic

DIDComm V2 is fundamentally asynchronous — messages are fire-and-forget at the
protocol layer, and responses arrive as separate inbound messages. Resolution across
multiple TDAs therefore uses **correlated async relay**: each intermediate TDA dispatches
an outbound `did-resolve-request`, holds a pending correlation entry keyed on
`originalRequestId`, and sends a `did-resolve-response` onward when the correlated
reply arrives in its inbox. From the originating TDA's perspective the result arrives
in one logical request/response cycle; every underlying message is async.

DIDComm's `thid` (thread ID) and `pthid` (parent thread ID) fields provide native
support for threading correlated messages. `originalRequestId` in the body carries
the correlation ID explicitly through relay hops.

### Step 1 — Local lookup (always, regardless of role)

Every TDA attempts to resolve the requested DID against its local `IDidDocumentRegistry` first.
If found, a `did-resolve-response` is issued immediately and resolution is complete.

### Step 2 — Escalation on local miss

If the local registry returns `notFound`, the TDA escalates based on its own role:

| Role | Action on local miss |
|---|---|
| Wanderer | Return `notFound` — no parent to escalate to |
| Citizen | Dispatch `did-resolve-request` to Society TDA; hold pending correlation; relay Society's response to original requester when it arrives |
| Society | Dispatch `did-resolve-request` to Federation TDA; hold pending correlation; relay Federation's response to original requester when it arrives |
| Federation | Look up which Society owns the DID method; dispatch to that Society; hold pending correlation; relay Society's response to the requesting Society when it arrives |

If the Federation has no Society registered for the requested DID method, it returns `notFound` immediately without dispatching.

### Resolution flow

```
receive did-resolve-request
│
├── Try local registry
│     └── FOUND → did-resolve-response to requester              ← terminal
│
└── NOT FOUND
      ├── Wanderer   → notFound                                   ← terminal
      │
      ├── Citizen    → dispatch did-resolve-request to Society
      │                 [hold pending correlation on originalRequestId]
      │                 ← Society's did-resolve-response arrives →
      │                 relay response → original requester        ← terminal
      │
      ├── Society    → dispatch did-resolve-request to Federation
      │                 [hold pending correlation on originalRequestId]
      │                 ← Federation's did-resolve-response arrives →
      │                 relay response → original requester        ← terminal
      │
      └── Federation → look up target Society (method registry)
                        NOT REGISTERED → notFound                 ← terminal
                        dispatch did-resolve-request to target Society
                        [hold pending correlation on originalRequestId]
                        ← Society's did-resolve-response arrives →
                        relay response → requesting Society        ← terminal
```

---

## End-to-End Scenarios

### Citizen resolves its own DID
```
Citizen: local HIT → response to self
```

### Citizen resolves another Citizen in the same Society
```
Citizen A: local miss
  → Society: local HIT (holds copy of Citizen B's DID Doc)
      → response to Citizen A
```

### Citizen resolves a Citizen in a different Society
```
Citizen A: local miss
  → Society A: local miss
      → Federation: local miss
          → Society B: local HIT (holds copy of Citizen B's DID Doc)
              → Federation → Society A → Citizen A
```

### Citizen resolves a Society DID
```
Citizen A: local miss
  → Society A: local miss
      → Federation: local HIT (holds copy of Society B's DID Doc)
          → Society A → Citizen A
```

### Society resolves a Citizen DID in another Society
```
Society A: local miss
  → Federation: local miss
      → Society B: local HIT
          → Federation → Society A
```

---

## Protocol Messages

### `did-resolve-request`

Sent by any TDA initiating or relaying a resolution request.

```json
{
  "typ":  "application/didcomm-plain+json",
  "type": "did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-request",
  "from": "<requesting TDA DID>",
  "to":   ["<target TDA DID>"],
  "body": {
    "requestedDid":         "did:drn:...",
    "requestId":            "<uuid>",
    "originalRequesterDid": "<DID of the TDA that originated the request>",
    "originalRequestId":    "<uuid of the originating request>"
  }
}
```

`originalRequesterDid` and `originalRequestId` are set once by the initiating TDA and
carried unchanged through every relay hop. A relaying TDA uses them to know where to
send the final response.

### `did-resolve-response`

Sent by the TDA that resolved (or failed to resolve) the DID, and relayed back through
the chain to the original requester.

```json
{
  "typ":  "application/didcomm-plain+json",
  "type": "did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-response",
  "from": "<responding TDA DID>",
  "to":   ["<next hop or original requester DID>"],
  "body": {
    "requestedDid":      "did:drn:...",
    "found":             true,
    "didDocument":       { },
    "resolvedAt":        "2026-06-17T00:00:00Z",
    "originalRequestId": "<uuid>"
  }
}
```

When `found` is `false`, `didDocument` is `null`. `originalRequestId` correlates the
response back to the originating request at every hop.

---

## Error Codes

| Code | Meaning |
|---|---|
| `notFound` | DID not found locally and no escalation path resolved it |
| `methodNotSupported` | Federation has no Society registered for the requested DID method |
| `invalidDid` | DID string could not be parsed |
| `resolutionTimeout` | Target Society or Federation did not respond within `FederationRoundTripTimeout` |

---

## Implementation: Key Classes and Cmdlets

### C# Layer

| Class | Role |
|---|---|
| `IDidDocumentRegistry` / `LiteDidDocumentRegistry` | Local LiteDB store — CRUD + local resolve |
| `IDidDocumentResolver` / `FederationDidDocumentResolver` | Routing layer — local method → registry; foreign method → DIDComm escalation |
| `DIDDocumentService` | OpenTelemetry-traced wrapper around `IDidDocumentRegistry` for C# callers |

`FederationDidDocumentResolver.ResolveAsync` inspects the DID method name against
`Svrn7SocietyOptions.DidMethodNames` (local) or the Federation method registry (remote).

### LOBE Cmdlets (`Svrn7.Identity.0.8.0.psm1`)

| Cmdlet | Role |
|---|---|
| `Resolve-Svrn7Did` | Handles inbound `did-resolve-request`; tries local first; escalates by role; passes `originalRequesterDid`/`originalRequestId` forward |
| `Invoke-Svrn7DidResolveResponse` | Handles inbound `did-resolve-response`; if `originalRequesterDid` ≠ self, relays response to original requester using `originalRequestId` |
| `Get-DIDDocument` | Local utility — resolves a DID directly; no DIDComm; no escalation |
| `Resolve-Svrn7CitizenIdentity` | Resolves a DID + fetches all VCs for that citizen; returns `{ CitizenDid, DIDDocument, Credentials[], ResolvedAt }` |
| `Get-Svrn7VcById` | Handles inbound `vc-resolve-by-subject-request`; queries local VC registry; sends response |
| `Invoke-Svrn7VcResolveResponse` | Handles inbound `vc-resolve-by-subject-response`; terminal — logs outcome |

### Key change from prior design

The previous design dispatched a DIDComm resolve-request and returned `notFound`
immediately, relying on the inbox processor to populate a local cache asynchronously.
The original caller received `notFound` on the first attempt and had to retry later.

The amended design uses **correlated async relay**: each hop dispatches a
`did-resolve-request`, holds a pending correlation entry keyed on `originalRequestId`,
and sends the `did-resolve-response` onward when the correlated reply arrives in its
inbox. The original caller receives a definitive answer — found or not found — in one
logical request/response cycle without retry logic or cache warming.

`Invoke-Svrn7DidResolveResponse` is no longer terminal — it checks `originalRequesterDid`
and, if set and different from self, relays the response to the next hop using
`originalRequestId` to match the pending correlation entry.

---

## DID Document Lifecycle

```
DidStatus: Active | Suspended | Deactivated
```

- `Active` — normal; resolvable
- `Suspended` — temporarily inactive; resolvable (status is visible in the document)
- `Deactivated` — permanent; document is returned with `Deactivated` status; not removed from registry

Version increments on every `UpdateAsync` call. `ResolveVersionAsync` retrieves a
specific historical snapshot. `GetHistoryAsync` returns all versions ordered oldest-first.
