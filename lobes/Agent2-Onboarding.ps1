#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Web 7.0 TDA — Agent 2 Onboarding Runspace Script.

.DESCRIPTION
    Task runspace opened on demand by the Switchboard for onboard/1.0/request messages.
    Returned to the RunspacePool after each task completes.

    Derived from: Agent 2 — Onboarding (PowerShell Runspace) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    DIDComm protocol: did:drn:svrn7.net/protocols/onboard/1.0/request
    Routing: Switchboard → Invoke-AgentRunspace Onboarding $msgDid

    Full pipeline:
        Get-TdaMessage -Did $msgDid
            | ConvertFrom-TdaOnboardRequest
            | Register-Svrn7CitizenInSociety   ← Svrn7.Society.psm1 (eager)
            | New-TdaOnboardReceipt             ← Svrn7.Onboarding.psm1 (JIT)
            | Send-TdaMessage

    On failure: Send-TdaOnboardError is called with the error message.

    Parameters (injected by Switchboard via AddParameter):
        $MessageDid — TDA resource DID URL of the inbox message
#>

param(
    [Parameter(Mandatory)]
    [string] $MessageDid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# JIT import — Onboarding LOBE not in eager list
$onboardingPath = $SVRN7_JIT_LOBES |
    Where-Object { $_ -like '*Svrn7.Onboarding*' } |
    Select-Object -First 1

if ($onboardingPath) {
    Import-Module $onboardingPath -Force -ErrorAction Stop
}

# ── Execute onboarding pipeline ───────────────────────────────────────────────

Write-Verbose "Agent 2 / Onboarding: processing $MessageDid"

$citizenDid = $null

try {
    # Step 1: Resolve and parse the onboard request
    $request = Get-TdaMessage -Did $MessageDid | ConvertFrom-TdaOnboardRequest
    if (-not $request) {
        throw "ConvertFrom-TdaOnboardRequest returned null for $MessageDid"
    }

    $citizenDid = $request.CitizenDid
    Write-Verbose "Agent 2 / Onboarding: registering citizen $citizenDid"

    # Step 2: Register citizen in Society + endowment (Svrn7.Society.psm1 — eager)
    # Register-Svrn7CitizenInSociety is an existing cmdlet in Svrn7.Society.psm1.
    # It calls ISvrn7SocietyDriver.RegisterCitizenInSocietyAsync internally.
    # Register-Svrn7CitizenInSociety requires a KeyPair PSCustomObject.
    # The Society stores the citizen's public key; private key is not transmitted.
    $citizenKeyPair = [PSCustomObject]@{
        PublicKeyHex    = $request.PublicKeyHex
        PrivateKeyBytes = [byte[]]@()   # Society never holds citizen private keys
    }

    $registrationResult = Register-Svrn7CitizenInSociety `
        -CitizenDid $request.CitizenDid `
        -KeyPair    $citizenKeyPair

    if (-not $registrationResult.Success) {
        throw "Registration failed: $($registrationResult.ErrorMessage)"
    }

    # Merge InvoiceId into result for receipt building
    $registrationResult['InvoiceId'] = $request.MessageDid

    # Step 3: Build and return the receipt (Onboarding LOBE)
    $outbound = $registrationResult | New-TdaOnboardReceipt

    Write-Host "Agent 2 / Onboarding: citizen $citizenDid registered. " +
               "Endowment: $($registrationResult.EndowmentGrana) grana." `
               -ForegroundColor Green

    # Return the OutboundMessage to the Switchboard pipeline
    [PSCustomObject]@{
        PeerEndpoint  = $outbound.PeerEndpoint
        PackedMessage = $outbound.PackedMessage
        MessageType   = $outbound.MessageType
    }

} catch {
    Write-Error "Agent 2 / Onboarding: failed for $MessageDid — $_"

    # Send error receipt if we know the citizen DID
    if ($citizenDid -and (Get-Command Send-TdaOnboardError -ErrorAction SilentlyContinue)) {
        $errOutbound = Send-TdaOnboardError `
            -CitizenDid    $citizenDid `
            -ErrorMessage  $_.ToString()

        [PSCustomObject]@{
            PeerEndpoint  = $errOutbound.PeerEndpoint
            PackedMessage = $errOutbound.PackedMessage
            MessageType   = $errOutbound.MessageType
        }
    }
}
