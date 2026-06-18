# Web 7.0 Pando — DNS Debug Guide

Covers `drn.directory` federation endpoint discovery — the only use of DNS in the
Web 7.0 Pando architecture.

---

## Background

`drn.directory` is the Web 7.0 Foundation-controlled DNS zone that provides out-of-band
bootstrap discovery of Federation TDA DIDComm endpoints. A new Citizen or Society only
needs to know the federation domain (e.g. `svrn7.net`) to locate the Federation TDA
without any prior DIDComm state.

TXT record convention:
```
federation.<domain>.drn.directory  IN TXT  "<DIDComm endpoint URL>"
```

After endpoint discovery, all communication is DIDComm v2. DNS is the addressing layer only.

Spec: `specs/draft-herman-did-w3c-drn-00.md` Section 5b.

---

## Prerequisites

- PowerShell 7.2+
- TDA built: `dotnet build src/Svrn7.TDA/Svrn7.TDA.csproj`
- `DnsClient.dll` in the TDA output directory (added as NuGet `DnsClient 1.7.0`)

All standalone scenarios below assume you have `cd`'d to the TDA output directory first:

```powershell
cd src\Svrn7.TDA\bin\Debug\net8.0
```

> **Note:** Always load `.psm1` files with `Import-Module`, not dot-source. PS7 detects
> the `.psm1` extension on dot-source and applies module-context scoping, making functions
> invisible to the caller.

---

## Cmdlet: `Resolve-FederationEndpoint`

Available in all TDA runspaces and standalone PowerShell sessions via `Svrn7.Common`.

### Signature

```powershell
Resolve-FederationEndpoint [-FederationDid] <string>
```

### Parameters

| Parameter | Type | Mandatory | Description |
|---|---|---|---|
| `FederationDid` | string | Yes | Federation DID, method-specific id, or bare domain (see accepted forms below). |

### Accepted input forms

| Input | Example |
|---|---|
| Full Federation DID | `did:drn:federation.svrn7.net/agent/1.0/abc123` |
| Method-specific id | `federation.svrn7.net` |
| Bare domain | `svrn7.net` |

All three forms resolve to the same drn.directory query label:
`federation.svrn7.net.drn.directory`

### Return value

`[string]` — the DIDComm endpoint URL from the TXT record, or `$null` when no record exists.

---

## Scenarios

### D.1 — Resolve from a full Federation DID (standalone PowerShell)

```powershell
Import-Module .\lobes\Svrn7.Common.0.8.0\Svrn7.Common.0.8.0.psm1
Initialize-Svrn7Assemblies

$endpoint = Resolve-FederationEndpoint `
    -FederationDid "did:drn:federation.svrn7.net/agent/1.0/abc123"

Write-Host "Federation endpoint: $endpoint"
# http://localhost:8441/didcomm  (testnet)
# https://tda.svrn7.net:8441/didcomm  (production)
```

---

### D.2 — Resolve from a bare domain

```powershell
$endpoint = Resolve-FederationEndpoint -FederationDid "svrn7.net"
Write-Host "Federation endpoint: $endpoint"
```

---

### D.3 — Use the endpoint to send a society-list DIDComm message

```powershell
$endpoint = Resolve-FederationEndpoint -FederationDid "svrn7.net"
if (-not $endpoint) { throw "No drn.directory record found for svrn7.net" }

$msg = [ordered]@{
    typ  = "application/didcomm-plain+json"
    id   = [System.Guid]::NewGuid().ToString("N")
    type = "did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/society-list"
    from = "did:drn:wanderer.testnet.svrn7.net/agent/1.0/<your-guid>"
    to   = @("<federation-did>")
    body = @{ replyEndpoint = "http://localhost:8445/didcomm" }
} | ConvertTo-Json -Depth 5

Send-DIDCommMessage -Uri $endpoint -Body $msg
```

---

### D.4 — C# API in a TDA runspace

```powershell
$endpoint = [Svrn7.TDA.DrnDirectory]::GetFederationEndpointAsync(
    "did:drn:federation.svrn7.net/agent/1.0/abc123"
).GetAwaiter().GetResult()

Write-Host "Endpoint: $endpoint"
```

---

### D.5 — Verify DnsClient.dll is present

```powershell
if (Test-Path .\DnsClient.dll) { Write-Host "OK: DnsClient.dll" } else { Write-Host "MISSING — run: dotnet build src/Svrn7.TDA/Svrn7.TDA.csproj" }
```

---

## C# API

### `DrnDirectory.GetFederationEndpointAsync` (public)

```csharp
// Svrn7.TDA.DrnDirectory
public static Task<string?> GetFederationEndpointAsync(
    string federationDidOrDomain, CancellationToken ct = default)
```

Accepts a full Federation DID, method-specific id, or bare domain. Returns the TXT value
(DIDComm endpoint URL) or `null` when no drn.directory record exists.

```csharp
var endpoint = await DrnDirectory.GetFederationEndpointAsync("svrn7.net", ct);
if (endpoint is null)
    throw new InvalidOperationException("No drn.directory record for svrn7.net");
```

### `DnsTxtHelper.GetTxtRecordsAsync` (internal)

Low-level DNS TXT query used by `DrnDirectory`. Not part of the public API.

---

## Error Reference

| Symptom | Cause | Fix |
|---|---|---|
| `DnsClient.dll not found` | NuGet not in output | `dotnet build src/Svrn7.TDA/Svrn7.TDA.csproj` |
| `$null` returned | No TXT record in drn.directory | Verify the Foundation has published the record |
| NXDOMAIN / exception | Query label malformed | Check input — all three forms are accepted |
| `http://` endpoint in production | Dev record still active | Update drn.directory TXT to `https://` |
