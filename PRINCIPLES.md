# SVRN7 Design Principles

These principles govern architectural and protocol decisions in the SVRN7 / Web 7.0 platform.
New design decisions must be consistent with these principles.

---

## P-001 — Everything is a TDA

Any node, service, registry, directory, or location in the SVRN7 network is a TDA (Trusted
Digital Agent) with a specific Role.  There are no traditional HTTP services, REST APIs,
or centralised servers — only TDAs communicating via DIDComm.

**Corollary:** When you need a registry, an index, a certificate authority, a package
feed, or any other "location service", it is always another TDA with the appropriate Role,
addressed by its DID.  Its capabilities are discovered and invoked via DIDComm message
exchange, not by calling a URL directly.

**Example:** The LOBE package registry (TDA-006) is a TDA whose Role includes serving
NuGet feed metadata.  The registry TDA's DID is stored in `TdaOptions.LobeRegistryDid`.
The consuming TDA sends a DIDComm request to that DID to obtain the feed URL — it does
not have the URL hardcoded.

---

## P-002 — Protocol URIs are derived from LOBE names and versions

DIDComm `@type` protocol URIs follow the convention:

```
did:drn:svrn7.net/protocols/{LobeName}.{lobe.version}/{action}
```

- `{LobeName}` — the full LOBE name exactly as it appears in `lobe.name` in the
  `.lobe.json` descriptor (e.g. `Svrn7.Email`, `Pando.Diagnostics`).  Case-preserved.
- `{lobe.version}` — the full three-part version from `lobe.version` (e.g. `0.8.0`).
- `{action}` — the message action name (e.g. `message`, `register-citizen`).

**Rationale:** The LOBE name is the single source of truth.  URI segments are not invented
independently.  This makes package-ID derivation from a URI trivial (second path segment
after `/protocols/` = NuGet package ID) and eliminates the class of mismatch bugs where
URIs diverge from the code that handles them.

**Examples:**

```
Svrn7.Email 0.8.0          did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/message
Svrn7.Onboarding 0.8.0     did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/register-citizen
Pando.Diagnostics 0.1.0    did:drn:svrn7.net/protocols/Pando.Diagnostics.0.1.0/date-query
```

See `docs/BACKLOG.md` TDA-007 for the full before/after table and version bump rules.

---

## P-003 — No public REST APIs

SVRN7 nodes do not expose REST, gRPC, GraphQL, or any other RPC interface to the outside
world.  All inter-node communication uses DIDComm.  Internal-to-process function calls
are the only non-DIDComm communication permitted.

**Rationale:** A single transport (DIDComm) with a single addressing scheme (DIDs) keeps
the security model simple, consistent, and auditable.  It also ensures that every message
between nodes is authenticated by construction.

---

## P-004 — LOBEs own exactly one URI namespace

A LOBE must own exactly one URI namespace segment — its own name.  A LOBE must not
register protocols under another LOBE's name segment.

**Rationale:** The bijection between LOBE name and URI segment is what makes P-002 work.
If a LOBE registers protocols under a foreign segment, the package-ID derivation breaks
and the protocol registry becomes inconsistent.

**Enforcement:** When a LOBE needs to handle message types that logically belong in
a separate domain (e.g. `Svrn7.Identity` handling both DID resolution and VC resolution),
the correct response is to split the LOBE, not to register under multiple segments.

---

## P-005 — Verification-first

All claims made about system behaviour — in documentation, commit messages, and code
comments — must be verified against the current source before being stated.  Do not
describe what a system "should do" or "probably does"; describe what it does, as
confirmed by reading the code.

Labels: `[fact]` (directly read from source), `[inference]` (logical derivation),
`[speculation]` (untested assumption).

---

## P-006 — Dead-letter, never drop

When a TDA cannot process an inbound message (unknown `@type`, handler error, policy
rejection), it dead-letters the message via `MarkFailedAsync(retry: false)`.  The message
is recorded in the inbox store with `Failed` status and is never silently discarded.

**Rationale:** Silent drops make debugging impossible.  A dead-lettered message is
observable, replayable, and auditable.

---

## P-007 — Cryptographic suites

The platform uses a fixed set of algorithms, one per function.  Do not introduce additional
suites without a documented rationale and a migration plan.

| Purpose | Algorithm | Library | Portable |
|---|---|---|---|
| DID genesis hash | Blake3 | Blake3.NET | Yes — managed, SIMD fallback on ARM |
| Identity signing (DID, transactions) | secp256k1 ECDSA | NBitcoin.Secp256k1 | Yes — fully managed |
| DIDComm message signing (JWS) | Ed25519 | NSec (libsodium) | Yes — ARM binaries in NuGet |
| DIDComm key agreement (JWE) | X25519 ECDH-ES | NSec (libsodium) | Yes — ARM binaries in NuGet |
| JWE KEK derivation | HKDF-SHA-256 | NSec (libsodium) | Yes — ARM binaries in NuGet |
| JWE key wrapping | AES-256 RFC 3394 | `System.Security.Cryptography.Aes` (ECB) | Yes — .NET runtime built-in |
| JWE content encryption | AES-256-GCM | `System.Security.Cryptography.AesGcm` | Yes — .NET runtime built-in |

**Portability:** All suites work on Windows, Linux, macOS, Android, and iOS under .NET 8+.
NSec's NuGet packages bundle ARM64/ARM native libsodium binaries for Android and iOS.
`System.Security.Cryptography` primitives are part of the .NET runtime on all targets.

**Wire format for DIDComm encryption:** ECDH-ES+A256KW (protected header), A256GCM (content),
as defined in JWA (RFC 7518).  The HKDF step uses NSec's built-in derivation over the
raw X25519 shared secret — not the JWA Concat KDF — since this is an internal protocol
and interoperability with third-party JWE implementations is not a current requirement.

---
