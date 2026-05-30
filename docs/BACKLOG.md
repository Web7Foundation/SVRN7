# SVRN7 TDA — Backlog

---

## ~~TDA-002~~ — Federation/society query via DIDComm ✓ *implemented*

Protocol `federation/1.0/society-list` → handler `Invoke-Web7SocietyList` in
`Svrn7.Federation` LOBE.  Returns count, activeCount, and a societies array to
the `replyEndpoint`.  See LOBEDEBUG.md §4.4 for the send pattern.

---

## TDA-001 — Hot-reload for JIT LOBEs

**Area:** `LobeManager`, `RunspacePoolManager`

**Summary:** Updated or newly registered JIT LOBEs currently require a TDA restart to
take effect. `lobes.config.json` is read once at startup; JIT modules are cached in
runspaces for their lifetime.

**What is needed:**

- `FileSystemWatcher` on the lobes directory to detect `.psm1` changes and new
  `lobes.config.json` entries
- `LobeManager` dirty-flag mechanism per LOBE
- `RunspacePoolManager` drains and recreates runspaces that have a dirty LOBE loaded,
  on the next dispatch cycle

**Out of scope:** Eager LOBEs (loaded into the Initial Session State) always require a
TDA restart regardless of this feature.
