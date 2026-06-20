# Web 7.0 Pando — Citizen TDA Debug Guide
#
# Covers the Citizen TDA registration flow: discovering Societies from the Federation,
# generating Citizen key material, sending the registration request, and verifying the
# onboarding receipt.
#
# Prerequisite: Complete SOCIETYDEBUG.md first — at least one Society must be
# registered with the Federation before a Citizen can join.
#
# For the Wanderer → Citizen promotion flow using a running TDA instance, also see
# WANDERERDEBUG.md Steps 10–14.  This guide covers the same flow from first
# principles, useful when scripting or testing without a running Citizen TDA.
#
# ---
#
# Overview
#
# A Citizen TDA starts as a Wanderer and is promoted to Citizen after completing the
# registration handshake with a Society TDA.  Post-registration:
#
# - parentTdaDid and parentTdaEndpointUrl in agent-identity.json point to the Society.
# - $SVRN7.ParentTdaEndpointUrl is available to all LOBE handlers.
# - The Citizen's DID Document (with secp256k1 key and DIDComm service endpoint) is held
#   by both the Citizen TDA and the Society TDA.
#
# Key protocols received by a Citizen TDA:
#
# | Protocol URI                                                              | Handler                      |
# |---------------------------------------------------------------------------|------------------------------|
# | did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/society-list-result   | Invoke-Web7SocietyListResult |
# | did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/receipt               | Invoke-Web7OnboardReceipt    |
#
# ---
#
# Prerequisites
#
# - PowerShell 7 (pwsh.exe).
# - Solution built; output directory is the working directory.
# - Federation TDA running on port 8441, initialised.
# - Society TDA running on port 8442, registered with Federation.

Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0

# ---
#
# Step 1 — Launch the Citizen TDA

Write-Host "--- Step 1 — Launch the Citizen TDA ---"
dotnet .\Svrn7.TDA.dll --port 8443 --name mwherman

# The Citizen TDA starts as a Wanderer.  Expected startup banner:
#
#   TDA Name    : mwherman
#   First run   : yes — Wanderer identity created
#   Role        : Wanderer
#   Agent DID   : did:drn:wanderer.svrn7.net/agent/1.0/<genesis-hash>
#   Listen port : 8443
#
# Note the Agent DID — this is the Wanderer identity used as the from field when
# sending society-list to the Federation.
#
# ---
#
# Step 2 — Load the send helper (separate PowerShell terminal)

Write-Host "--- Step 2 — Load the send helper ---"
Set-Location C:/SVRN7/repos/SVRN7/src/Svrn7.TDA/bin/Debug/net8.0
Import-Module .\lobes\Svrn7.Federation.0.8.0\Svrn7.Federation.0.8.0.psm1

# Read the Wanderer DID from the identity file:

$wandererDid = (Get-Content 8443/mem/agent-identity.json | ConvertFrom-Json).did
Write-Host "Wanderer DID: $wandererDid"

# ---
#
# E.3a — Discover available Societies (society-list)
#
# The Citizen TDA sends society-list to the Federation.  The Federation resolves the
# reply endpoint from the sender's DID Document and replies with each Society's DID Document.

Write-Host "--- E.3a — Discover available Societies ---"
$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/Svrn7.Federation.0.8.0/society-list'
    from = $wandererDid
    to   = @('did:drn:foundation.svrn7.net')
    body = '{}'
} | ConvertTo-Json

Send-LocalDIDCommMessage -Port 8441 -Body $msg

# Expected log — Federation TDA (port 8441):
#
# info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
#       Switchboard: routing ... (type=.../Svrn7.Federation.0.8.0/society-list)
#           → Invoke-Web7SocietyList [Svrn7.Federation]
# info: ...
#       [PS Info] Invoke-Web7SocietyList: 1 society/societies, replying to http://localhost:8443/didcomm
# info: ...
#       Switchboard: outbound delivered to http://localhost:8443/didcomm (202).
#
# ---
#
# E.3b — Society-list result received
#
# Invoke-Web7SocietyListResult runs automatically on the Citizen TDA.  Expected log:
#
# info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
#       Switchboard: routing ... (type=.../Svrn7.Federation.0.8.0/society-list-result)
#           → Invoke-Web7SocietyListResult [Svrn7.Federation]
# info: ...
#       [PS Info] Invoke-Web7SocietyListResult: stored 1 society DID Document(s) from 1 result(s)
#
# The Citizen TDA's local DID registry now contains the Society's DID Document,
# including the Society's serviceEndpointUrl (http://localhost:8442/didcomm).
# No further network lookup is needed for the registration step.
#
# ---
#
# E.3 — Generate Citizen key material
#
# The Citizen DID is derived from a secp256k1 key pair — distinct from the Wanderer
# GUID DID.  Generate once and save the output.

Write-Host "--- E.3 — Generate Citizen key material ---"
$citizenKp  = New-Svrn7KeyPair
$citizenDid = New-Svrn7Did -KeyPair $citizenKp -Role Citizen -SocietyName 'bindloss'

Write-Host "Citizen DID : $($citizenDid.Did)"
Write-Host "Public key  : $($citizenKp.PublicKeyHex)"
Write-Host "Private key : $($citizenKp.PrivateKeyHex)   <-- store securely"

# Example output (values will differ):
#
# Citizen DID : did:drn:bindloss.svrn7.net/citizen/1.0/<64-char genesis-hash>
# Public key  : 0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798
# Private key : <32-byte hex — keep secret>
#
# ---
#
# E.4 — Send register-citizen to the Society
#
# serviceEndpointUrl is the Citizen TDA's DIDComm endpoint — the Society uses it to
# create the Citizen's DID Document and to deliver the receipt reply.

Write-Host "--- E.4 — Send register-citizen to the Society ---"
$body = @{
    citizenDid         = $citizenDid.Did
    publicKeyHex       = $citizenKp.PublicKeyHex
    displayName        = 'mwherman'
    serviceEndpointUrl = 'http://localhost:8443/didcomm'   # Citizen TDA endpoint
} | ConvertTo-Json -Compress

$msg = @{
    typ  = 'application/didcomm-plain+json'
    id   = "did:drn:svrn7.net/didcomm/msg/$([System.Guid]::NewGuid().ToString('N'))"
    type = 'did:drn:svrn7.net/protocols/Svrn7.Onboarding.0.8.0/register-citizen'
    from = $citizenDid.Did
    to   = @('did:drn:bindloss.svrn7.net')
    body = $body
} | ConvertTo-Json

Send-LocalDIDCommMessage -Port 8442 -Body $msg

# Expected log — Society TDA (port 8442):
#
# info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
#       Switchboard: routing ... (type=.../Svrn7.Onboarding.0.8.0/register-citizen)
#           → ConvertFrom-Web7OnboardRequest [Svrn7.Onboarding]
# info: ...
#       [PS Info] Citizen did:bindloss:3J98... registered. Endowment: 1000000000 grana.
# info: ...
#       Switchboard: outbound delivered to http://localhost:8443/didcomm (202).
#
# ---
#
# E.4r — Onboarding receipt received
#
# Invoke-Web7OnboardReceipt runs automatically on the Citizen TDA.  Expected log:
#
# info: Svrn7.TDA.DIDCommMessageSwitchboard[0]
#       Switchboard: routing ... (type=.../Svrn7.Onboarding.0.8.0/receipt)
#           → Invoke-Web7OnboardReceipt [Svrn7.Onboarding]
# info: ...
#       [PS Info] Invoke-Web7OnboardReceipt: registered with did:drn:bindloss.svrn7.net
#           at http://localhost:8442/didcomm
#
# On receipt the Citizen TDA:
# 1. Stores citizenDidDocument in its local DID registry
# 2. Stores societyDidDocument in its local DID registry
# 3. Sets parentTdaDid = societyDid and parentTdaEndpointUrl = societyEndpointUrl
#    (persisted to agent-identity.json)
#
# ---
#
# Step 3 — Verify agent-identity.json

Write-Host "--- Step 3 — Verify agent-identity.json ---"
Get-Content 8443/mem/agent-identity.json | ConvertFrom-Json |
    Select-Object did, parentTdaDid, parentTdaEndpointUrl

# Expected:
#
# did                  parentTdaDid                   parentTdaEndpointUrl
# ---                  ------------                   --------------------
# did:drn:wanderer...  did:drn:bindloss.svrn7.net     http://localhost:8442/didcomm
#
# The Citizen TDA is now registered.  On the next restart it reads parentTdaDid and
# parentTdaEndpointUrl from agent-identity.json automatically — no appsettings.json
# entries needed.
#
# ---
#
# Resetting the Citizen TDA

Write-Host "--- Resetting the Citizen TDA ---"
Remove-Item -Recurse -Force 8443\mem -ErrorAction SilentlyContinue
# Or:
dotnet .\Svrn7.TDA.dll --port 8443 --name mwherman --reset

# After a reset, repeat the full flow from E.3a.
#
# ---
#
# Available Protocol URIs — Citizen TDA (inbound)
#
# | type URI                                             | Handler                      |
# |------------------------------------------------------|------------------------------|
# | .../Svrn7.Federation.0.8.0/society-list-result      | Invoke-Web7SocietyListResult |
# | .../Svrn7.Onboarding.0.8.0/receipt                  | Invoke-Web7OnboardReceipt    |
#
# Full URI prefix: did:drn:svrn7.net/protocols/
#
# ---
#
# Error Reference
#
# | Symptom                                          | Cause                                              | Fix                                                  |
# |--------------------------------------------------|----------------------------------------------------|------------------------------------------------------|
# | No society-list-result received                  | Citizen TDA Kestrel not yet listening on port 8443 | Wait for KestrelListenerService started on port 8443 |
# | society-list-result stored 0 societies           | No Societies registered with Federation            | Complete SOCIETYDEBUG.md §E.2r first                 |
# | No receipt received after register-citizen       | Society TDA could not reach port 8443              | Confirm Citizen TDA is running and listening         |
# | agent-identity.json missing parentTdaDid         | Invoke-Web7OnboardReceipt did not run              | Check Citizen TDA log for routing errors             |
