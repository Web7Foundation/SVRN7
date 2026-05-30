# LOBE Specification — [LOBE Name]

> Copy this file, rename it to match your LOBE (e.g. `LOBE-Payments.md`), and fill in
> every section before handing it to the implementation step.  Sections marked
> **[REQUIRED]** must be completed.  Sections marked **[OPTIONAL]** may be left blank
> if not applicable.  See `docs/LOBEGUIDE.md` for full guidance on each step.

---

## 1. Identity [REQUIRED]

| Field | Value |
|---|---|
| LOBE name | `Svrn7.` *(e.g. `Svrn7.Payments`)* |
| LOBE ID | lowercase dot-separated *(e.g. `svrn7.payments`)* |
| Title | Human-readable *(e.g. `SVRN7 Payments LOBE`)* |
| Description | One or two sentences describing what this LOBE does. |
| Version | `0.1.0` |
| Load mode | `eager` / `jit` — choose one; default is `jit` |
| Minimum epoch | `0` / `1` / `2` |

---

## 2. Protocol(s) [REQUIRED]

Repeat this block for each DIDComm message type the LOBE handles.

### Protocol A

| Field | Value |
|---|---|
| URI | `did:drn:svrn7.net/protocols/{domain}/{version}/{action}` — see accepted `{action}` names in `LOBEGUIDE.md §Step 1` |
| Title | Short human label |
| Description | What this message does; list required body fields inline. |
| Direction | `inbound` |
| Match | `exact` / `prefix` |
| Entrypoint cmdlet | PowerShell function name *(e.g. `Invoke-PandoPaymentRequest`)* |
| Minimum epoch | `0` / `1` / `2` |

### Protocol B [OPTIONAL]

*(Copy the table above if this LOBE handles more than one inbound message type.)*

---

## 3. Inbound Message Body [REQUIRED]

Fields present in the JSON body of the inbound DIDComm message.

### Required fields

| Field name (camelCase) | Type | Constraints / description |
|---|---|---|
| `exampleDid` | string | DID of the payer. Must be an active citizen. |
| `amountGrana` | integer | > 0 |
| *(add rows)* | | |

### Optional fields

| Field name (camelCase) | Type | Default | Description |
|---|---|---|---|
| `replyEndpoint` | string (URL) | *(none)* | DIDComm HTTP endpoint for the reply. Include when calling from a TDA; omit from script tools. |
| `memo` | string | `null` | Free-text memo, max 256 characters. |
| *(add rows)* | | | |

---

## 4. Business Logic [REQUIRED]

Describe what the handler does step by step.  Be specific about driver methods.

**Driver context:**
- Society operations → `$SVRN7.Driver.*` (available in TDA runspace via `Svrn7RunspaceContext`)
- Federation operations → `Get-ActiveFederationDriver` → `ISvrn7Driver` / `ISvrn7SocietyDriver`

**Steps:**

1. *(e.g. Validate payer DID is an active citizen via `$SVRN7.Driver.IsCitizenActiveAsync($payerDid)`)*
2. *(e.g. Execute transfer via `$SVRN7.Driver.TransferAsync(...)`)*
3. *(add steps)*

**Idempotency:**
- [ ] This operation is idempotent (safe to retry on duplicate delivery)
- [ ] This operation is NOT idempotent — the Switchboard must dead-letter on first failure

**Retry behaviour:**
- [ ] Transactional — dead-letter immediately on failure (no retry)
- [ ] Non-transactional — retry up to platform default (`NonTransactionalMaxAttempts`)

*(Add the protocol URI to `Svrn7Constants.TransactionalProtocols` in `Svrn7.Core` if
transactional.)*

---

## 5. Reply Message [OPTIONAL]

Complete this section only if the LOBE sends a response back to the caller.

### Outbound message type

| Field | Value |
|---|---|
| URI | `did:drn:svrn7.net/protocols/{domain}/{version}/{result}` |
| Title | Short human label |

### Reply body fields

| Field name (camelCase) | Type | Description |
|---|---|---|
| `success` | boolean | Whether the operation succeeded. |
| *(add rows)* | | |

### Reply endpoint resolution

Choose the strategy that fits this protocol:

- [ ] **`replyEndpoint` in body** — the sender includes its endpoint in the message.
      Use for TDA-to-TDA calls where the federation DID may not yet have a DID Document.
- [ ] **DID Document lookup** — resolve the sender's DIDComm service entry via
      `Resolve-SocietySenderEndpoint -Did $msg.FromDid`.
      Use when the caller is guaranteed to be a registered TDA peer.
- [ ] **Both with fallback** — try `replyEndpoint` first, fall back to DID Document
      lookup, warn and return if neither yields an endpoint.
      Use when callers may be either script tools or live TDAs.

---

## 6. Dependencies [OPTIONAL]

| Dependency | Reason |
|---|---|
| `Svrn7.Society` | *(e.g. calls `Register-Svrn7CitizenInSociety`)* |
| `Svrn7.Federation` | *(e.g. calls `Invoke-Svrn7Transfer`)* |
| *(add rows)* | |

Eager LOBEs (Common, Federation, Society, UX) are always available in the runspace.
Only list JIT LOBEs if this LOBE explicitly imports them.

---

## 7. Cmdlets Exposed to Other LOBEs [OPTIONAL]

All cmdlet names must follow the `{ApprovedVerb}-{Noun}` PowerShell convention.
Use the `Pando` noun prefix for DIDComm handlers and `Svrn7` for driver wrappers.
See approved verbs and naming patterns in `LOBEGUIDE.md §Step 6`.

List any cmdlets this LOBE exports for use by other LOBEs or scripts (beyond the
protocol entrypoints above).

| Cmdlet name | Description |
|---|---|
| *(e.g. `Get-PandoPaymentStatus`)* | *(e.g. Returns payment status by transfer ID)* |

---

## 8. Error Handling Notes [OPTIONAL]

Describe any domain-specific exceptions and the expected handler behaviour.

| Exception / condition | Handler behaviour |
|---|---|
| *(e.g. `InsufficientFundsException`)* | *(e.g. Send error receipt with `success=false`; do not retry)* |
| *(add rows)* | |

---

## 9. Examples [OPTIONAL]

### Sample inbound message body

```json
{
  "@type": "did:drn:svrn7.net/protocols/{domain}/{version}/{action}",
  "payerDid": "did:drn:example",
  "amountGrana": 1000000,
  "replyEndpoint": "https://peer-tda.example.net:8443"
}
```

### Sample reply body

```json
{
  "success": true,
  "transferId": "abc123..."
}
```

---

## 10. Open Questions [OPTIONAL]

List anything that needs a decision before implementation begins.

- [ ] *(e.g. Should failed transfers send an error receipt or silently dead-letter?)*
- [ ] *(add items)*
