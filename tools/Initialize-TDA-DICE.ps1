$PSVersionTable.PSVersion   # Major must be 7

# - The solution must be built before starting:

# Set-Location C:/SVRN7/repos/SVRN7
# dotnet build src/Svrn7.TDA/Svrn7.TDA.csproj

# - Verify `Pando.Diagnostics` is in the JIT list:

Set-Location src/Svrn7.TDA/bin/Debug/net8.0
Get-Content lobes/lobes.config.json | Select-String "Pando"

# Expected:
#
#     "Pando.Diagnostics.0.1.0/Pando.Diagnostics.0.1.0.psm1"
#
# ---
#
# Terminal layout
#
# Three PowerShell 7 terminals are needed throughout this guide.
#
# | Terminal   | Purpose                                                       |
# |------------|---------------------------------------------------------------|
# | **A — W5** | Runs the W5 TDA process on port 8445; watch log output here   |
# | **B — W6** | Runs the W6 TDA process on port 8446; watch log output here   |
# | **C — Sender** | Sends DIDComm messages; reads identity files              |
#
# ---
#
# Step 1 — Start W5 and W6 (Terminals A and B)

cls
Write-Host "--- Step 1 — Start W5 and W6 ---"
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
#Start-Process cmd.exe -ArgumentList '/k title W5 [Wanderer]:8445 && dotnet ".\Svrn7.TDA.dll" --port 8445 --name W5 --reset'
#Start-Process cmd.exe -ArgumentList '/k title W6 [Wanderer]:8446 && dotnet ".\Svrn7.TDA.dll" --port 8446 --name W6 --reset'
Start-Process cmd.exe -ArgumentList '/k title W6 [Wanderer]:8446 && dotnet ".\Svrn7.TDA.dll" --port 8446 --name W6'
pause 

# **Production / staging:** Add `--federationdomain svrn7.net` to auto-discover the
# Federation TDA endpoint via drn.directory DNS at startup.  The discovered URL is shown
# in the banner (`Fed Endpoint`) and exposed as `$SVRN7.FederationEndpointUrl` in every
# LOBE runspace.  Omit for standalone dev runs with no live drn.directory DNS record.
#
# W5 has no prior databases — this is a first run.  Expected startup banner:
#
# ────────────────────────────────────────────────────────────────────────────────
#   SVRN7 Trusted Digital Assistant (TDA)  v0.8.0
#   Web 7.0 Foundation — https://svrn7.net
# ────────────────────────────────────────────────────────────────────────────────
#   ...
#   TDA Name    : W5
#   First run   : yes — Wanderer identity created
#   Role        : Wanderer
#   Agent DID   : did:drn:wanderer.svrn7.net/agent/1.0/<genesis-hash-W5>
#   Listen port : 8445
#   LOBEs       : 4 eager  8 JIT  (N protocols  N cmdlets)
#     Eager     : Svrn7.Common  Svrn7.Federation  Svrn7.Society  Svrn7.UX
#     JIT       : ...  Pando.Diagnostics  ...
# ────────────────────────────────────────────────────────────────────────────────
#   Federation  : (not yet initialised ...)
#   Societies   : (not yet initialised ...)
# ────────────────────────────────────────────────────────────────────────────────
#
# Note the `Agent DID` line — this is W5's Wanderer identity.  It is also written to:
#
# 8445/mem/agent-identity.json
#
# ---
#
# Step 3 — Read the Wanderer DIDs (Terminal C)
#
# W5 and W6 each generate a unique public-key-derived DID on first run.  Read W6's DID
# (the self-send scenario uses W6 as both sender and recipient):

Write-Host "--- Step 3 — Read the Wanderer DIDs ---"
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0

$w6Did = (Get-Content 8446/mem/agent-identity.json | ConvertFrom-Json).did

Write-Host "W6 DID: $w6Did"

# Expected:
#
# W6 DID: did:drn:wanderer.svrn7.net/agent/1.0/<genesis-hash-W6>
#
# ---
#
# Step 4 — Import the send helper (Terminal C)
#
# Do this once per PowerShell session.

Write-Host "--- Step 4 — Import the send helper ---"
Import-Module .\lobes\Svrn7.Federation.0.8.0\Svrn7.Federation.0.8.0.psm1

# This gives you `Send-LocalDIDCommMessage` for the steps below.
#
# ---
#
# Step 5 — Send Query-TOD from W6 to itself (Terminal C)
#
# W6 sends the message to its own endpoint.  W6's own DID Document is already in its local
# registry, so `Resolve-SocietySenderEndpoint` succeeds and the `Issue-TOD` reply is
# delivered back to W6 without requiring federation or cross-TDA DID Document exchange.

Write-Host "--- Step 5 — Send Query-TOD from W6 to itself ---"
$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/Pando.Diagnostics.0.1.0/Query-TOD'
    from = $w6Did
    to   = @($w6Did)
    body = '{}'
} | ConvertTo-Json

Send-LocalDIDCommMessage -Port 8446 -Body $msg
