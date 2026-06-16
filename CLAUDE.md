# Web 7.0 Pando — Claude Code Project Context

## Web7Mail ↔ Citizen TDA Integration

### Overview

`Web7.SVRN7.Apps.Web7Mail` is a .NET Framework 4.0 WinForms Outlook 2003-style email
client. It integrates with the Web 7.0 Pando Citizen TDA in a **1:1 relationship**
(one Web7Mail instance per Citizen TDA instance, same machine).

---

### Transport: DIDComm V2 over Local WebSocket

The TDA pushes async notifications to Web7Mail. There is **no polling**.

The notification channel is a **localhost-only WebSocket endpoint** on the TDA,
separate from the public `POST /didcomm` surface:

```
ws://localhost:{port}/didcomm-notify
```

This endpoint is **not published in the Citizen TDA's DID Document** — it is a
private local UI attachment point, not a peer-to-peer TDA interface.

Messages on this channel are **DIDComm V2 SignThenEncrypt envelopes**, identical
in format to TDA-to-TDA envelopes. The `@type` field determines dispatch on the
Web7Mail side.

---

### Inbound Flow (TDA → Web7Mail)

```
Sender TDA (remote)
  └── DIDComm V2 SignThenEncrypt
        └── POST /didcomm → Local Citizen TDA (Kestrel, HTTP/2, mTLS)
              └── Switchboard (routes by @type)
                    └── Svrn7.Email LOBE (.psm1)
                          ├── Persist to LiteDB (Long-Term Message Memory)
                          ├── Decode SMTP-over-DIDComm payload
                          └── Send DIDComm Email-Notify envelope
                                └── ws://localhost:{port}/didcomm-notify
                                      └── TdaMailClient (Web7Mail background thread)
                                            ├── DIDComm unpack (verify + decrypt)
                                            ├── Dispatch on @type
                                            └── BeginInvoke → MessageStore.Append()
                                                  └── SortableBindingList<MailMessage>
                                                        └── DataGridView refresh
```

---

### Outbound Flow (Web7Mail → Recipient TDA)

```
Web7Mail Compose UI
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
| `TdaMailClient` | `Web7.SVRN7.Apps.Web7Mail` | `Send()` → POST /didcomm; WebSocket receive loop |
| `MessageStore` | `Web7.SVRN7.Apps.Web7Mail` | Replace `Inbox.xml` source with `TdaMailClient` initial load + live append |
| `MainForm` | `Web7.SVRN7.Apps.Web7Mail` | `BeginInvoke` marshal on WebSocket notification receipt |
| `Svrn7.Email` LOBE | `Svrn7.Email.psm1` | After LiteDB persist, send DIDComm Email-Notify envelope over WebSocket |

---

### Svrn7.Email LOBE

- Handles **SMTP messages over DIDComm** — this LOBE already exists
- On inbound message: persist to LiteDB first, then push notification to Web7Mail
  via WebSocket
- The LOBE does **not** write directly to any Web7Mail data structure —
  pass-by-reference semantics apply (LiteDB ObjectId passed, not payload copy)

---

### Open Decisions

1. **Web7Mail DID identity:** Web7Mail may share/derive its DID from the Citizen
   TDA DID using a key fragment (e.g. `did:drn:svrn7.net:alice#web7mail-ui`).
   Not yet decided — requires deliberate choice before implementing DIDComm unpack
   in `TdaMailClient`.

2. **Initial inbox load:** How Web7Mail populates `MessageStore` on startup
   (before any WebSocket push arrives) — options are a DIDComm query message to
   the `Svrn7.Email` LOBE, or a localhost-only REST endpoint on the TDA.

---

### Naming Conventions (apply everywhere)

- Protocol URIs: `svrn7.net` (not `svrn7.io`)
- DIDComm verb-noun cmdlet convention: e.g. `Send-EmailNotify` not `send-emailnotify`
- "DID Document Resolver" / "DID Document Resolution" (not "DID Resolver")
- Interface: `IDidDocumentResolver`
- Implementations: `LocalDidDocumentResolver`, `FederationDidDocumentResolver`
- "Shared Reserve Currency (SRC)" (not "Reserve Currency")

---

### Architecture Constraints

- Web7Mail targets **.NET Framework 4.0** (Windows only) — no HTTP/2 client support
  in this target. The localhost WebSocket channel (`ws://`) avoids this constraint.
  If Web7Mail is ever upgraded to .NET 8, `TdaMailClient` can use HTTP/2 + mTLS
  directly for `POST /didcomm`.
- The TDA's public inbound surface remains **`POST /didcomm` only** (Kestrel,
  HTTP/2, mTLS). The WebSocket endpoint is a separate, localhost-scoped surface.
- The WebSocket Local UI Attachment Point pattern is **reusable** — any local UI
  app (calendar, contacts, tasks) on any OS where the TDA runs can connect to the
  same WebSocket endpoint and dispatch on `@type`. Future notification types:

  ```
  did:drn:svrn7.net/protocols/Calendar-Notify/1.0/new-event
  did:drn:svrn7.net/protocols/Presence-Notify/1.0/status-change
  ```
