# LOBE Specification — Pando.Diagnostics

> See `docs/LOBEGUIDE.md` for full guidance on each step.

---

## 1. Identity [REQUIRED]

| Field | Value |
|---|---|
| LOBE name | `Pando.Diagnostics` |
| LOBE ID | `pando.diagnostics` |
| Title | `Pando Diagnostics LOBE` |
| Description | Wraps the pre-existing `Pando.Diagnostics` PowerShell module as a Pando LOBE, exposing server date/time retrieval via DIDComm for clock-skew detection and round-trip latency measurement. |
| Version | `0.1.0` |
| Load mode | `jit` |
| Minimum epoch | `0` |

---

## 2. Protocol(s) [REQUIRED]

### Protocol A

| Field | Value |
|---|---|
| URI | `did:drn:svrn7.net/protocols/Pando.Diagnostics/0.1/date-query` |
| Title | Diagnostics Date Query |
| Description | Requests the current server UTC date/time from the target TDA. Adapter calls the pre-existing `Get-TDADate` cmdlet unchanged. No required body fields. Optional: `replyEndpoint`. |
| Direction | `inbound` |
| Match | `exact` |
| Entrypoint cmdlet | `Invoke-PandoDiagnosticsDateQuery` |
| Minimum epoch | `0` |

---

## 3. Inbound Message Body [REQUIRED]

Fields present in the JSON body of the inbound DIDComm message.

### Required fields

*(none — the date-query message carries no required body fields)*

### Optional fields

| Field name (camelCase) | Type | Default | Description |
|---|---|---|---|
| `replyEndpoint` | string (URL) | *(none)* | DIDComm HTTP/2 endpoint for the reply. Include when calling from a script tool or a TDA whose DID Document is not yet registered. |

---

## 4. Business Logic [REQUIRED]

**Design note:** `Get-TDADate` is a **pre-existing cmdlet** from the `Pando.Diagnostics`
PowerShell module.  It has no knowledge of DIDComm, `$MessageDid`, or the outbound
contract — it simply returns `[datetimeoffset]::UtcNow`.  It is not modified for Pando.

`Invoke-PandoDiagnosticsDateQuery` is the thin DIDComm adapter.  It is the only
Pando-aware piece of code; all business logic is delegated to the existing cmdlet.

**Driver context:**
- No driver calls required — date/time comes from the TDA process clock via `Get-TDADate`.

**Steps:**

1. Parse the inbound message body with `ConvertFrom-Json`. No required field assertions needed.
2. Call the pre-existing `Get-TDADate` to obtain the server's current `[datetimeoffset]` (UTC).
3. Read `$SVRN7.CurrentEpoch` for inclusion in the reply.
4. Resolve the reply endpoint: check `replyEndpoint` body field first; fall back to `Resolve-SocietySenderEndpoint -Did $msg.FromDid`; if neither yields an endpoint, emit `Write-Warning` and return without error.
5. Package the `Get-TDADate` result into the outbound reply hashtable and return.

**Idempotency:**
- [x] This operation is idempotent (safe to retry on duplicate delivery)

**Retry behaviour:**
- [x] Non-transactional — retry up to platform default (`NonTransactionalMaxAttempts`)

*(Do **not** add this URI to `Svrn7Constants.TransactionalProtocols` — it is read-only and stateless.)*

---

## 5. Reply Message [OPTIONAL]

### Outbound message type

| Field | Value |
|---|---|
| URI | `did:drn:svrn7.net/protocols/Pando.Diagnostics/0.1/date-result` |
| Title | Diagnostics Date Result |

### Reply body fields

| Field name (camelCase) | Type | Description |
|---|---|---|
| `serverUtc` | string (ISO 8601) | Value returned by `Get-TDADate`, formatted as `yyyy-MM-ddTHH:mm:ss.fffffffZ`. |
| `serverUtcOffset` | string | Always `"+00:00"` (UTC). Included for symmetry with future local-time variants. |
| `currentEpoch` | integer | Value of `$SVRN7.CurrentEpoch` at dispatch time. |
| `respondedAt` | string (ISO 8601) | Timestamp at the point the reply hashtable is built. May differ slightly from `serverUtc` if epoch lookup is slow. |

### Reply endpoint resolution

- [x] **Both with fallback** — try `replyEndpoint` first, fall back to DID Document lookup, warn and return if neither yields an endpoint. Use when callers may be either script tools or live TDAs.

---

## 6. Dependencies [OPTIONAL]

| Dependency | Reason |
|---|---|
| `Pando.Diagnostics.Impl.psm1` (existing PS module) | Provides `Get-TDADate`. Loaded at the top of the LOBE `.psm1` with `Import-Module "$PSScriptRoot/Pando.Diagnostics.Impl.psm1"`. Not a SVRN7 LOBE dependency — a standard PowerShell module import. |

Eager LOBEs (Common, Federation, Society, UX) are always available in the runspace.
No JIT LOBE dependencies.

---

## 7. Cmdlets Exposed to Other LOBEs [OPTIONAL]

| Cmdlet name | Origin | Description |
|---|---|---|
| `Get-TDADate` | Pre-existing — `Pando.Diagnostics.Impl.psm1` | Returns the TDA server's current date and time as a `[datetimeoffset]` (UTC). Not modified for Pando. Other LOBEs may call it directly as a shared authoritative time source. |

---

## 8. Error Handling Notes [OPTIONAL]

| Exception / condition | Handler behaviour |
|---|---|
| No reply endpoint resolvable (`replyEndpoint` absent and `FromDid` has no DIDComm service entry) | `Write-Warning` and bare `return` — `Get-TDADate` succeeded; only delivery is skipped. Inbound message is marked Processed. |
| `ConvertFrom-Json` parse failure on body | Terminating error; Switchboard retries up to `NonTransactionalMaxAttempts` then dead-letters. |
| `Get-TDADate` throws (e.g. `Pando.Diagnostics.Impl.psm1` not found) | Terminating error propagates to Switchboard; message is retried then dead-lettered. |

---

## 9. Examples [OPTIONAL]

### Adapter code sketch

```powershell
# Pando.Diagnostics.psm1 — Pando adapter only; Get-TDADate is in the existing module

Import-Module "$PSScriptRoot/Pando.Diagnostics.Impl.psm1"   # pre-existing module; not modified

function Invoke-PandoDiagnosticsDateQuery {
    [CmdletBinding()]
    [OutputType([Svrn7.TDA.OutboundMessage])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )
    process {
        $msg  = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { throw "Invoke-PandoDiagnosticsDateQuery: message '$MessageDid' not found." }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop

        # Delegate entirely to the pre-existing cmdlet — no modification needed
        $now  = Get-TDADate

        Write-Information "Pando.Diagnostics: serverUtc=$($now.ToString('o')) epoch=$($SVRN7.CurrentEpoch)"

        $replyEndpoint = if ($body.PSObject.Properties['replyEndpoint']) { $body.replyEndpoint }
                         else { Resolve-SocietySenderEndpoint -Did $msg.FromDid }
        if (-not $replyEndpoint) {
            Write-Warning "Invoke-PandoDiagnosticsDateQuery: no reply endpoint — result not delivered."
            return
        }

        $payload = @{
            serverUtc       = $now.UtcDateTime.ToString('o')
            serverUtcOffset = '+00:00'
            currentEpoch    = $SVRN7.CurrentEpoch
            respondedAt     = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Compress

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Pando.Diagnostics/0.1/date-result'
            from = $SVRN7.Driver.SocietyDid
            to   = @($msg.FromDid)
            body = $payload
        } | ConvertTo-Json -Compress

        [Svrn7.TDA.OutboundMessage]::new($replyEndpoint, $envelope)
    }
}

Export-ModuleMember -Function @('Invoke-PandoDiagnosticsDateQuery')
```

### Sample inbound message body

```json
{
  "@type": "did:drn:svrn7.net/protocols/Pando.Diagnostics/0.1/date-query",
  "replyEndpoint": "http://client-tda.example.net:8443/didcomm"
}
```

Script-tool variant (no reply expected — omit `replyEndpoint`):

```json
{
  "@type": "did:drn:svrn7.net/protocols/Pando.Diagnostics/0.1/date-query"
}
```

### Sample reply body

```json
{
  "serverUtc":       "2026-05-29T22:47:13.4521830Z",
  "serverUtcOffset": "+00:00",
  "currentEpoch":    0,
  "respondedAt":     "2026-05-29T22:47:13.4523110Z"
}
```

### Sample PowerShell send (script tool, TDA running locally)

```powershell
Import-Module .\lobes\Svrn7.Federation\Svrn7.Federation.psm1

$msg = @{
    typ  = "application/didcomm-plain+json"
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = "did:drn:svrn7.net/protocols/Pando.Diagnostics/0.1/date-query"
    from = "did:drn:foundation.svrn7.net"
    to   = @("did:drn:bindloss.svrn7.net")
    body = "{}"
} | ConvertTo-Json

Send-DIDCommMessage -Body $msg
```

Expected TDA log (LogLevel.Information):

```
Switchboard: routing did:drn:bindloss.svrn7.net/inbox/msg/<id>
    (type=did:drn:svrn7.net/protocols/Pando.Diagnostics/0.1/date-query)
    → Invoke-PandoDiagnosticsDateQuery [Pando.Diagnostics]
[PS Verbose] Pando.Diagnostics: serverUtc=2026-05-29T22:47:13.4521830Z epoch=0
[PS Warning] Pando.Diagnostics: no reply endpoint — result not delivered.
```

---

## 10. Open Questions [OPTIONAL]

- [ ] Should `serverLocal` (TDA process local time + offset) be added to the reply for deployments in non-UTC time zones?
- [ ] Should this LOBE grow into a broader diagnostics surface (e.g. `diagnostics/1.0/health-query` returning inbox queue depth, epoch, and uptime)?

---

## 11. Implementation Notes

### `Get-TDADate` is internal to this LOBE

`Get-TDADate` is specific to `Pando.Diagnostics` and is not intended for use by other
LOBEs.  `Pando.Diagnostics.psm1` therefore exports only `Invoke-PandoDiagnosticsDateQuery`
in its `Export-ModuleMember` list.  `Get-TDADate` is loaded into the runspace via
`Import-Module "$PSScriptRoot/Pando.Diagnostics.Impl.psm1"` but is not re-exported.
If a future LOBE needs a shared authoritative time source, the function should be moved
to `Svrn7.Common.psm1` at that point.

### `Export-ModuleMember` and cross-LOBE visibility

Only functions listed in `Export-ModuleMember` in the adapter `.psm1` are visible to
other LOBEs in the runspace.  Functions imported from `.Impl.psm1` but not re-exported
by the adapter are available within the LOBE's own handlers but are not part of its
public surface.  Verify the `Export-ModuleMember` list matches the `cmdlets` array in
`.lobe.json` before testing.
