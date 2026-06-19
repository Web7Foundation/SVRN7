# Web 7.0 Pando — Claude Code Project Context

---

## Repository Structure

```
SVRN7/
├── src/
│   ├── Svrn7.Core/          Models, interfaces, enums (DidDocument, Svrn7Role, DidStatus, etc.)
│   ├── Svrn7.Crypto/        secp256k1 key generation, Base58, signing (CryptoService)
│   ├── Svrn7.DIDComm/       DIDComm V2 pack/unpack (all modes: plaintext, JWS, JWE, SignThenEncrypt)
│   ├── Svrn7.Store/         LiteDB registries: LiteDidDocumentRegistry, LiteInboxStore, etc.
│   ├── Svrn7.Federation/    ISvrn7Driver, Svrn7Driver — federation + DID management
│   ├── Svrn7.Society/       ISvrn7SocietyDriver — adds citizen/VC/transfer layer
│   ├── Svrn7.Identity/      DIDDocumentService, DID resolve pipeline
│   ├── Svrn7.Ledger/        Epoch, supply, Merkle ledger
│   ├── Svrn7.TDA/           Trusted Digital Assistant — Kestrel host + LOBE runtime
│   └── Web7.SVRN7.Apps.PandoMail/   WinForms email client (.NET 8, Windows)
├── tests/
│   ├── Svrn7.Tests/
│   ├── Svrn7.TDA.Tests/
│   └── Svrn7.Society.Tests/
├── tools/                   Initialize-Testnet.ps1, Build-LOBEPackages.ps1, Pando.Packaging.psm1
└── docs/                    DEBUG.md, DIDDEBUG.md, WANDERERDEBUG.md, LOBEDEBUG.md, etc.
```

---

## TDA Architecture

```
POST /didcomm (HTTP/2, Kestrel)
  └── KestrelListenerService
        └── IDIDCommService.UnpackAsync       ← JWE decrypt + JWS verify at inbound boundary
              └── LiteInboxStore.EnqueueAsync
                    └── DIDCommMessageSwitchboard (drain loop)
                          └── LobeManager.TryResolveProtocol(@type)
                                └── LobeManager.EnsureLoadedAsync(modulePath)
                                      └── Import-Module [-Force] in IsolatedRunspaceFactory
                                            └── Cmdlet invocation (PowerShell.Invoke())
                                                  └── OutboundMessage (plaintext envelope)
                                                        └── PackOutboundAsync            ← SignThenEncrypt at outbound boundary (HTTP only)
                                                              └── DeliverAsync (HTTP/2)
```

**Key components:**

| Class | Role |
|---|---|
| `KestrelListenerService` | Single inbound gate: `POST /didcomm` only |
| `DIDCommMessageSwitchboard` | Inbox drain loop; routes by `@type`; outbound delivery |
| `LobeManager` | Protocol registry; eager/JIT import; `EnsureLoadedAsync` |
| `IsolatedRunspaceFactory` | Runspace pool (min 2, max ProcessorCount×2) |
| `TdaHost` / `$SVRN7` | Context object injected into every runspace; exposes `Driver`, `GetMessageAsync`, `CurrentEpoch`, etc. |

**Eager LOBEs** are loaded once at startup into the `InitialSessionState`.
**JIT LOBEs** run `Import-Module -Force` on every dispatch — hot-update without TDA restart (overhead ~30 ms, tracked as TDA-001a).

---

## Design Rationale: HTTP/2 for `POST /didcomm`

The TDA's public inbound surface is HTTP/2-only (no HTTP/1.1 fallback). Reasons:

1. **mTLS mutual peer authentication** — TDA-to-TDA communication requires both sides to authenticate with certificates. HTTP/2 + TLS 1.3 via ALPN is the modern standard for this on Kestrel; it eliminates downgrade attacks that are possible when HTTP/1.1 is allowed as a fallback.

2. **Multiplexed concurrent streams without HOL blocking** — A TDA may receive many inbound DIDComm messages simultaneously from multiple peers. HTTP/2 multiplexes them over a single connection; HTTP/1.1 head-of-line blocking would serialize them.

3. **Minimal, single-endpoint attack surface** — One verb (`POST`), one path (`/didcomm`), one protocol, one port. Disabling HTTP/1.1 removes an entire class of HTTP/1.1-specific parsing vulnerabilities (request smuggling, header injection).

4. **Mature on Kestrel / same transport as gRPC** — .NET's HTTP/2 stack is battle-tested via gRPC. Same deployment, same operational patterns, no additional runtime dependency.

5. **HPACK header compression** — DIDComm headers (`Content-Type: application/didcomm-encrypted+json`, etc.) are identical on every message. HTTP/2 compresses them after the first exchange.

**Why not gRPC directly?** gRPC framing (Protobuf + length-prefixed binary) adds schema complexity for third-party LOBE authors and clients that only need to POST a JSON envelope. Raw HTTP/2 POST keeps the wire format simple.

**Local UI clients (PandoMail):** PandoMail is on .NET 8. `TdaMailClient.Send()` uses `HttpClient` with HTTP/2 + mTLS directly for `POST /didcomm`. The `/didcomm-notify` push channel uses RFC 8441 (WebSocket over HTTP/2 extended CONNECT) — a deliberate design choice for server-push, not a .NET limitation.

---

## Design Rationale: SignThenEncrypt at Outbound Boundary (DIDCommMessageSwitchboard)

`DIDCommMessageSwitchboard.PackOutboundAsync` applies SignThenEncrypt (secp256k1 ES256K JWS inside X25519 JWE) to every outbound message before HTTP delivery. LOBEs always return plaintext `OutboundMessage` objects — they have no access to key material and no knowledge of which pack mode to use. The Switchboard is the sole place where packing decisions are made.

**Transport rule (P-008):**

| Transport | Pack mode |
|---|---|
| HTTP (`POST /didcomm`) — TDA-to-TDA | SignThenEncrypt (secp256k1 ES256K JWS inside X25519 JWE) |
| WebSocket (`/didcomm-notify`) — TDA-to-local-UI | Plaintext (`application/didcomm-plain+json`) |

**Why plaintext for WebSocket?** The `/didcomm-notify` channel is localhost-only. PandoMail holds no key material and shares the Citizen TDA's DID. Encryption would require giving PandoMail long-lived private keys — that contradicts the shared-identity design and would expand the attack surface unnecessarily.

**Symmetric design:** This is the outbound counterpart of the decrypt-at-boundary pattern on the inbound side. `KestrelListenerService` unpacks every inbound message at the HTTP boundary before anything enters the inbox; `DIDCommMessageSwitchboard.PackOutboundAsync` packs every outbound HTTP message at the delivery boundary before anything leaves the process.

**Fallback behaviour:** If the recipient's DID Document does not contain an `X25519KeyAgreementKey2020` entry — for example, a TDA bootstrapped before X25519 keys were added — `PackOutboundAsync` logs a warning and sends plaintext. This is a degraded mode. All TDAs bootstrapped with the current codebase include an X25519 key by default.

**Key material in `TdaOptions`:**

| Field | Key type | Use |
|---|---|---|
| `AgentKeyAgreementPrivateKey` | X25519 (32 bytes) | Inbound JWE decryption (`KestrelListenerService.UnpackAsync`) |
| `AgentSigningPrivateKey` | secp256k1 (32 bytes) | Outbound JWS signing (`DIDCommMessageSwitchboard.PackOutboundAsync`) |

Both are loaded from `agent-identity.json` at startup and held for the process lifetime. Neither is passed into LOBE runspaces.

---

## Design Rationale: Decrypt-at-Boundary (KestrelListenerService)

`KestrelListenerService` fully unpacks every inbound DIDComm message — JWE decryption and JWS signature verification — **before** anything is written to the inbox. The decrypted plaintext body is what gets stored in `InboxMessage.PackedPayload`. The original wire envelope is preserved separately in `InboxMessage.JweEnvelope` for audit. This is a deliberate architectural constraint, not an implementation convenience.

Storing the raw JWE and decrypting later (in the Switchboard or in a LOBE) would break the system in five distinct ways:

1. **Routing breaks entirely** — `DIDCommMessageSwitchboard` routes by `InboxMessage.MessageType`, which is extracted from the unpacked inner payload at enqueue time. A stored JWE has no accessible `@type`; the Switchboard would have to assign a synthetic type (`application/didcomm-encrypted+json`) that no LOBE handles, dead-lettering every encrypted message.

2. **Every LOBE breaks** — LOBE handlers access message content as:
   ```powershell
   $body = $msg.PackedPayload | ConvertFrom-Json
   ```
   If `PackedPayload` is a JWE envelope, `ConvertFrom-Json` returns the JWE structure, not the application payload. All existing LOBEs would require a decrypt-first step, making LOBE authoring significantly more complex and error-prone.

3. **Key material must enter the LOBE runspace** — Decrypting in a LOBE requires the X25519 private key (`AgentKeyAgreementPrivateKey`) to be available there. Currently it is held only by `KestrelListenerService` and never enters a runspace. Injecting it into `$SVRN7` expands the attack surface: a buggy or malicious LOBE would have access to the TDA's decryption key.

4. **`FromDid` is unavailable for reply routing** — The sender DID is extracted from the JWS inner layer during `UnpackJwsAsync`. Without unpacking at the boundary, `InboxMessage.FromDid` is null. LOBE handlers that need to send a reply have no sender identity without repeating the full unpack.

5. **Stuck-message recovery corrupts the retry cycle** — `LiteInboxStore.ResetStuckMessagesAsync` requeues `Processing` messages to `Pending` on startup after a crash. If stored payloads are JWE envelopes the Switchboard cannot route, each recovery attempt fails, re-enqueues, and fails again until `maxAttempts` is exhausted and the message is permanently dead-lettered. Every unclean shutdown would silently destroy messages.

**The `JweEnvelope` field** satisfies the audit/forensics use case without any of the above costs: the original wire format is always available for inspection, replay, or legal hold, while the pipeline operates entirely on verified plaintext.

---

## LOBE Authoring

### Folder and file convention

```
lobes/
└── {Name}.{version}/
      ├── {Name}.{version}.psm1       ← entry-point module (exported cmdlets only)
      ├── {Name}.{version}.lobe.json  ← descriptor
      └── {Name}.Impl.{version}.psm1  ← optional implementation module
```

### `lobe.json` required fields

```json
{
  "lobe": { "name": "Pando.Diagnostics", "version": "0.1.0", "module": "Pando.Diagnostics.0.1.0.psm1" },
  "protocols": [
    {
      "uri":        "did:drn:svrn7.net/protocols/Pando.Diagnostics.0.1.0/Query-TOD",
      "direction":  "inbound",
      "match":      "exact",
      "entrypoint": "Invoke-PandoDiagnosticsDateQuery"
    }
  ]
}
```

`match` is `"exact"` or `"prefix"`. `direction` is always `"inbound"` for handler protocols.

### psm1 rules

- `#Requires -Version 7.2` and `Set-StrictMode -Version Latest` at the top of every LOBE psm1.
- `$ErrorActionPreference = 'Stop'`
- Use `Assert-BodyFields` / `Get-BodyField` (from `Svrn7.Common`) for all body field access — `Set-StrictMode` throws on absent `PSCustomObject` properties before guards can fire.
- Every handler returns either `[Svrn7.TDA.OutboundMessage]` (to trigger outbound delivery) or `$null` (no reply).
- **LOBEs always construct plaintext envelopes** (`typ = 'application/didcomm-plain+json'`). The Switchboard applies SignThenEncrypt for HTTP delivery and plaintext for WebSocket delivery — LOBEs do not call `DIDCommPackingService` directly.
- `Export-ModuleMember -Function @(...)` must be explicit.

### Handler signature

```powershell
function Invoke-MyLobeVerb {
    [CmdletBinding()]
    [OutputType([Svrn7.TDA.OutboundMessage])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )
    process {
        $msg  = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        # ... handle ...
        $endpoint = Resolve-SocietySenderEndpoint -Did $msg.FromDid
        if (-not $endpoint) { Write-Warning "...: cannot resolve endpoint — result not delivered."; return }
        [Svrn7.TDA.OutboundMessage]::new($endpoint, $envelope)
    }
}
```

---

## Protocol URI Convention

```
did:drn:svrn7.net/protocols/{LOBE}.{version}/{Verb-Noun}
```

Examples:
- `did:drn:svrn7.net/protocols/Pando.Diagnostics.0.1.0/Query-TOD`
- `did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/initialize-federation`
- `did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/message`

`svrn7.net` always (never `svrn7.io`). Verb-Noun uses PascalCase or kebab-case consistently within a LOBE.

**Outbound `PeerEndpoint`** is the full URL including path — used verbatim, no suffix appended:
```
http://localhost:8443/didcomm
```

---

## TDA Launch and Data Layout

```powershell
dotnet .\Svrn7.TDA.dll --port 8443 --name MyTDA [--url http://localhost] [--reset]
```

| Parameter | Default | Notes |
|---|---|---|
| `--port` | (required) | Listen port; also scopes all data |
| `--name` | (required) | Stored as `Svrn7Name` in Wanderer DID Document |
| `--url` | `http://localhost` | Base URL for DID Document service endpoint; full endpoint = `{url}:{port}/didcomm` |
| `--reset` | off | Deletes `{port}/mem/` before start; forces fresh Wanderer bootstrap |

**Data layout** (relative to `Svrn7.TDA.dll`):

```
{BaseDir}/
├── lobes/                    shared LOBE catalog (all TDA instances on this machine)
│   └── lobes.config.json
└── {port}/
      └── mem/
            ├── svrn7.db
            ├── svrn7-dids.db
            ├── svrn7-inbox.db
            ├── svrn7-vcs.db
            ├── svrn7-schemas.db
            └── agent-identity.json
```

Testnet script: `tools/Initialize-Testnet.ps1` launches Wanderer1–4 on ports 8441–8444 in separate titled console windows.

---

## DID and Role Model

### Roles (additive, not exclusive)

| Role | `Svrn7Role` enum | DID format |
|---|---|---|
| Wanderer | `Wanderer` | `did:drn:wanderer.svrn7.net/agent/1.0/<genesis-hash>` |
| Citizen | `Citizen` | `did:drn:<societyname>.svrn7.net/citizen/1.0/<genesis-hash>` |
| Society | `Society` | `did:drn:federation.svrn7.net/<societyname>/1.0/<genesis-hash>` |
| Federation | `Federation` | `did:drn:federation.svrn7.net/federation/1.0/<genesis-hash>` |

All DIDs use the `did:drn:` method. `<genesis-hash>` = `Blake3(genesis_secp256k1_compressed_pubkey_bytes)` hex-encoded (64 chars). Derived once from the initial key pair and never changes — key rotation updates the DID Document's `verificationMethod` entries without altering the DID itself. `New-Svrn7Did` derives the DID deterministically from the key pair and role.

### `DidStatus` enum

`Active` | `Suspended` | `Deactivated`

### `Svrn7Role` enum (partial)

`Wanderer` | `Citizen` | `Society` | `Federation` | `LOBEPackageManager` | `LOBEMarketplace`

### First-run Wanderer bootstrap

On startup with an empty DID registry the TDA auto-generates a Wanderer identity and writes it to `{port}/mem/agent-identity.json`:

```json
{
  "did":                "did:drn:wanderer.svrn7.net/agent/1.0/<genesis-hash>",
  "publicKeyHex":       "<secp256k1 compressed public key, 66 hex chars>",
  "privateKeyHex":      "<secp256k1 private key, 64 hex chars>",
  "x25519PublicKeyHex": "<X25519 public key, 64 hex chars>",
  "x25519PrivateKeyHex":"<X25519 private key, 64 hex chars>",
  "role":               "Wanderer",
  "createdAt":          "2026-01-01T00:00:00.000+00:00"
}
```

- `publicKeyHex` / `privateKeyHex` — secp256k1 identity key. Used for DID genesis hash derivation, DIDComm JWS signing (`AgentSigningPrivateKey`), and transaction signing.
- `x25519PublicKeyHex` / `x25519PrivateKeyHex` — X25519 key agreement key. Used for JWE encryption/decryption (`AgentKeyAgreementPrivateKey`). Published in the DID Document as `X25519KeyAgreementKey2020`; required for receiving SignThenEncrypt messages.

---

## Commit Convention

- **Never** add `Co-Authored-By`, `Generated by`, or any AI attribution to commits or source files.
- Commit messages: imperative mood, concise subject line, blank line before body if needed.

---

## PowerShell Requirements

- **PowerShell 7.2+ required** everywhere — LOBE psm1 files, debug guides, tooling scripts.
- `Set-StrictMode -Version Latest` is active in all LOBEs — never access `PSCustomObject` properties without guards (`Assert-BodyFields` / `Get-BodyField`).
- Dot-sourcing `.psm1` files in PS 7 applies module-context scoping. Use `[scriptblock]::Create([System.IO.File]::ReadAllText($path)).Invoke()` for dynamic loading outside the LOBE runtime.
- `Initialize-Svrn7Assemblies -ModuleRoot $PSScriptRoot` must be called before accessing any `Svrn7.*` .NET types in a standalone PS session (outside a TDA runspace). It is called automatically by `New-Svrn7KeyPair`, `New-Svrn7Did`, etc. if the driver is not already initialised.
- `Send-DIDCommMessage` (in `Svrn7.Common`) is send-only from PS — there is no inbound listener in a PS session. A running TDA is required to receive DIDComm replies.

---

## Known Limitations

| Limitation | Detail | Backlog ref |
|---|---|---|
| JIT LOBE reimport on every dispatch | `Import-Module -Force` runs each time for hot-update support; ~30 ms overhead per message. Deferred to Epoch 1. | TDA-001a |
| PS cannot receive DIDComm replies | A standalone PowerShell session has no HTTP/2 listener — replies require a running TDA as the `from` DID. | — |

---

## Naming Conventions (apply everywhere)

- Protocol URIs: `svrn7.net` (never `svrn7.io`)
- Cmdlets: `Verb-Noun` PascalCase — e.g. `Send-EmailNotify`, `Invoke-PandoDiagnosticsDateQuery`
- "DID Document Resolver" / "DID Document Resolution" (not "DID Resolver")
- Interfaces: `IDidDocumentResolver`, `ISvrn7Driver`, `ISvrn7SocietyDriver`
- Implementations: `LocalDidDocumentResolver`, `FederationDidDocumentResolver`
- "Shared Reserve Currency (SRC)" (not "Reserve Currency")
- `DidDocument` (one word, not `DIDDocument`) in C# types; `DIDDocument` in prose
- LiteDB registry classes: `Lite{Entity}Registry` / `Lite{Entity}Store`

---

## PandoMail ↔ Citizen TDA Integration

### Overview

`Web7.SVRN7.Apps.PandoMail` is a .NET 8 WinForms Outlook 2003-style email client.
It integrates with the Web 7.0 Pando Citizen TDA in a **1:1 relationship**
(one PandoMail instance per Citizen TDA instance, same machine).

---

### Transport: DIDComm V2 over Local WebSocket

The TDA pushes async notifications to PandoMail. There is **no polling**.

The notification channel is a **localhost-only WebSocket endpoint** on the TDA,
on the **same port** as `POST /didcomm`:

```
ws://localhost:{port}/didcomm-notify
```

Single port serves both surfaces:

| Path | Protocol | Direction |
|---|---|---|
| `/didcomm` | HTTP/2 (`POST`) | Inbound DIDComm from remote TDAs |
| `/didcomm-notify` | WebSocket (RFC 8441 — HTTP/2 extended CONNECT) | Outbound push to local UI clients |

**Kestrel uses `HttpProtocols.Http2` only.** RFC 8441 enables WebSocket over HTTP/2
on `/didcomm-notify` without enabling HTTP/1.1 on the listener. PandoMail is on .NET 8
and `HttpClient` supports RFC 8441, so no `HttpProtocols.Http1AndHttp2` workaround
is needed and no HTTP/1.1 attack surface is introduced.

This endpoint is **not published in the Citizen TDA's DID Document** — it is a
private local UI attachment point, not a peer-to-peer TDA interface.

Messages on this channel are **DIDComm V2 plaintext envelopes**
(`application/didcomm-plain+json`). The channel is localhost-only; PandoMail
holds no key material and shares the Citizen TDA's DID. The `@type` field
determines dispatch on the PandoMail side. See `Principles.md` P-008.

---

### Inbound Flow (TDA → PandoMail)

```
Sender TDA (remote)
  └── DIDComm V2 SignThenEncrypt (secp256k1 JWS + X25519 JWE)
        └── POST /didcomm → Local Citizen TDA (Kestrel, HTTP/2, mTLS)
              └── KestrelListenerService.UnpackAsync  ← decrypt + verify at inbound boundary
                    └── Switchboard (routes by @type)
                          └── Svrn7.Email LOBE (.psm1)
                                ├── Persist to LiteDB (Long-Term Message Memory)
                                ├── Decode SMTP-over-DIDComm payload
                                └── OutboundMessage (plaintext) → ws://localhost:{port}/didcomm-notify
                                      └── TdaMailClient (PandoMail background thread)
                                            ├── DIDComm unpack (plaintext — no decrypt/verify)
                                            ├── Dispatch on @type
                                            └── BeginInvoke → MessageStore.Append()
                                                  └── SortableBindingList<MailMessage>
                                                        └── DataGridView refresh
```

---

### Outbound Flow (PandoMail → Recipient TDA)

```
PandoMail Compose UI
  └── TdaMailClient.Send(MailMessage)
        └── POST /didcomm (localhost, mTLS)
              └── Svrn7.Email LOBE
                    └── Wrap as SMTP-over-DIDComm
                          └── DIDComm V2 SignThenEncrypt → Recipient TDA
```

---

### DIDComm Protocol Type URI

Inbound email notification `@type`:

```
did:drn:svrn7.net/protocols/Email-Notify/1.0/new-message
```

Follows the Locator DID URL convention (`draft-herman-drn-resource-addressing-00`).
Protocol URIs use `svrn7.net` (not `svrn7.io`).

---

### Key Classes to Add/Modify

| Component | Project | Purpose |
|---|---|---|
| `TdaMailClient` | `Web7.SVRN7.Apps.PandoMail` | `Send()` → POST /didcomm; WebSocket receive loop |
| `MessageStore` | `Web7.SVRN7.Apps.PandoMail` | Replace `Inbox.xml` source with `TdaMailClient` initial load + live append |
| `MainForm` | `Web7.SVRN7.Apps.PandoMail` | `BeginInvoke` marshal on WebSocket notification receipt |
| `Svrn7.Email` LOBE | `Svrn7.Email.psm1` | After LiteDB persist, send DIDComm Email-Notify envelope over WebSocket |

---

### Svrn7.Email LOBE

- Handles **SMTP messages over DIDComm** — this LOBE already exists
- On inbound message: persist to LiteDB first, then push notification to PandoMail
  via WebSocket
- The LOBE does **not** write directly to any PandoMail data structure —
  pass-by-reference semantics apply (LiteDB ObjectId passed, not payload copy)

---

### Open Decisions

1. **PandoMail DID identity: shared DID (decided).** PandoMail shares the Citizen TDA's
   DID — same identity, no key fragment or sub-DID. `TdaMailClient` posts to the local TDA
   using the shared DID; the TDA signs and forwards using its own key material.

2. **Initial inbox load:** How PandoMail populates `MessageStore` on startup
   (before any WebSocket push arrives) — options are a DIDComm query message to
   the `Svrn7.Email` LOBE, or a localhost-only REST endpoint on the TDA.

---

### Architecture Constraints

- PandoMail targets **.NET 8** (Windows only). `TdaMailClient.Send()` uses `HttpClient`
  with HTTP/2 + mTLS directly for `POST /didcomm`. The `/didcomm-notify` push channel
  uses RFC 8441 (WebSocket over HTTP/2 extended CONNECT) — both Kestrel and `HttpClient`
  support it at .NET 8.
- Both `/didcomm` and `/didcomm-notify` are on the **same port** for a given TDA.
  Kestrel uses `HttpProtocols.Http2` only — RFC 8441 carries the WebSocket upgrade
  over HTTP/2 streams, so no HTTP/1.1 is needed and no second port is opened.
- The TDA's public inbound surface remains **`POST /didcomm` only** (Kestrel,
  HTTP/2, mTLS). The WebSocket endpoint is a separate, localhost-scoped path on the
  same port.
- The WebSocket Local UI Attachment Point pattern is **reusable** — any local UI
  app (calendar, contacts, tasks) on any OS where the TDA runs can connect to the
  same WebSocket endpoint and dispatch on `@type`. Future notification types:

  ```
  did:drn:svrn7.net/protocols/Calendar-Notify/1.0/new-event
  did:drn:svrn7.net/protocols/Presence-Notify/1.0/status-change
  ```
