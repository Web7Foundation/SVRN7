#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Web 7.0 TDA — Agent 1 Coordinator Runspace Script.

.DESCRIPTION
    The coordinator runspace. Always open; never returned to the pool.
    Hosts the DIDComm Message Switchboard drain loop and activates the
    four Agent 1 LOBEs (Email, Calendar, Presence, Notifications) on first
    message of each type via JIT import.

    Derived from: Agent 1 Runspace (PowerShell Runspace) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    This script is invoked by SwitchboardHostedService at startup.
    It runs until the Host cancellation token fires (SIGTERM / Ctrl-C).

    LOBEs available in this runspace:
        Eager (pre-loaded via InitialSessionState):
            Svrn7.Common.psm1
            Svrn7.Federation.psm1
            Svrn7.Society.psm1
            Svrn7.UX.psm1           → ux/1.0/* (balance updates, notifications, registration)

        JIT (imported on first message of each type):
            Svrn7.Email.psm1        → did:drn:svrn7.net/protocols/email/1.0/*
            Svrn7.Calendar.psm1     → did:drn:svrn7.net/protocols/calendar/1.0/*
            Svrn7.Presence.psm1     → did:drn:svrn7.net/protocols/presence/1.0/*
            Svrn7.Notifications.psm1→ did:drn:svrn7.net/protocols/notification/1.0/*
            Svrn7.Identity.psm1     → did:drn:svrn7.net/protocols/did/1.0/*
                                      did:drn:svrn7.net/protocols/vc/1.0/*

    $SVRN7 session variable is pre-injected by LobeManager.
    $SVRN7_JIT_LOBES contains the array of JIT LOBE paths.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── JIT LOBE import tracking ──────────────────────────────────────────────────

$script:LoadedJitLobes = @{}

function Import-JitLobeIfNeeded {
    param([string] $LobeName)
    if ($script:LoadedJitLobes.ContainsKey($LobeName)) { return }

    $path = $SVRN7_JIT_LOBES | Where-Object {
        [System.IO.Path]::GetFileNameWithoutExtension($_) -eq $LobeName
    } | Select-Object -First 1

    if (-not $path) {
        Write-Warning "Agent 1: JIT LOBE '$LobeName' not found in SVRN7_JIT_LOBES."
        return
    }

    Import-Module $path -Force -ErrorAction Stop
    $script:LoadedJitLobes[$LobeName] = $true
    Write-Verbose "Agent 1: JIT LOBE '$LobeName' imported."
}

# ── Message routing helpers ───────────────────────────────────────────────────

function Invoke-EmailAgent {
    param([string] $MessageDid)
    Import-JitLobeIfNeeded -LobeName 'Svrn7.Email'
    try {
        $result = Receive-TdaEmail -MessageDid $MessageDid
        Write-Verbose "Agent 1 / Email: processed $MessageDid"
        return $result
    } catch {
        Write-Error "Agent 1 / Email: failed for $MessageDid — $_"
    }
}

function Invoke-CalendarAgent {
    param([string] $MessageDid, [string] $MessageType)
    Import-JitLobeIfNeeded -LobeName 'Svrn7.Calendar'
    try {
        $result = if ($MessageType -like '*/invite') {
            Get-TdaMessage -Did $MessageDid |
                Receive-TdaMeetingRequest |
                New-TdaCalendarResponse -Accept
        } else {
            Import-TdaCalendarEvent -MessageDid $MessageDid
        }
        Write-Verbose "Agent 1 / Calendar: processed $MessageDid"
        return $result
    } catch {
        Write-Error "Agent 1 / Calendar: failed for $MessageDid — $_"
    }
}

function Invoke-PresenceAgent {
    param([string] $MessageDid, [string] $MessageType)
    Import-JitLobeIfNeeded -LobeName 'Svrn7.Presence'
    try {
        $result = if ($MessageType -like '*/subscribe') {
            Add-TdaPresenceSubscription -MessageDid $MessageDid
        } else {
            Update-TdaPresence -MessageDid $MessageDid
        }
        Write-Verbose "Agent 1 / Presence: processed $MessageDid"
        return $result
    } catch {
        Write-Error "Agent 1 / Presence: failed for $MessageDid — $_"
    }
}

function Invoke-NotificationAgent {
    param([string] $MessageDid)
    Import-JitLobeIfNeeded -LobeName 'Svrn7.Notifications'
    try {
        $result = Invoke-TdaNotification -MessageDid $MessageDid
        Write-Verbose "Agent 1 / Notifications: processed $MessageDid"
        return $result
    } catch {
        Write-Error "Agent 1 / Notifications: failed for $MessageDid — $_"
    }
}

function Invoke-UxAgent {
    param([string] $MessageDid, [string] $MessageType)
    # UX LOBE is eager — already loaded, no JIT import needed.
    try {
        $result = switch -Wildcard ($MessageType) {
            '*/ux/1.0/balance-update'        { Render-TdaBalanceUpdate -MessageDid $MessageDid }
            '*/ux/1.0/notification'           { Render-TdaNotification -MessageDid $MessageDid }
            '*/ux/1.0/registration-complete'  { Render-TdaRegistrationComplete -MessageDid $MessageDid }
            default { Write-Warning "Agent 1 / UX: unhandled type $MessageType"; $null }
        }
        Write-Verbose "Agent 1 / UX: processed $MessageDid ($MessageType)"
        return $result
    } catch {
        Write-Error "Agent 1 / UX: failed for $MessageDid — $_"
    }
}

function Invoke-IdentityAgent {
    param([string] $MessageDid, [string] $MessageType)
    Import-JitLobeIfNeeded -LobeName 'Svrn7.Identity'
    try {
        $result = switch -Wildcard ($MessageType) {
            '*/did/1.0/resolve-request'               { Resolve-Svrn7Did -MessageDid $MessageDid }
            '*/vc/1.0/resolve-by-subject-request'     { Get-Svrn7VcById  -MessageDid $MessageDid }
            default { Write-Warning "Agent 1 / Identity: unhandled type $MessageType"; $null }
        }
        Write-Verbose "Agent 1 / Identity: processed $MessageDid ($MessageType)"
        return $result
    } catch {
        Write-Error "Agent 1 / Identity: failed for $MessageDid — $_"
    }
}

# ── Get-TdaMessage (pass-by-reference resolution) ────────────────────────────
# Exposed as a cmdlet for use by all LOBE pipelines in this runspace.

function Get-TdaMessage {
    <#
    .SYNOPSIS
        Resolves an inbox message by its TDA resource DID URL.
        Pass-by-reference entry point for all LOBE cmdlet pipelines.
    .PARAMETER Did
        TDA resource DID URL (did:drn:{networkId}/inbox/msg/{objectId}).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Did)
    process {
        $msg = $SVRN7.GetMessageAsync($Did).GetAwaiter().GetResult()
        if (-not $msg) { throw "Get-TdaMessage: message '$Did' not found." }
        # Pass through with the DID URL attached for downstream pipeline use
        $msg | Add-Member -NotePropertyName 'MessageDid' -NotePropertyValue $Did -PassThru
    }
}

# ── Send-TdaMessage (outbound queue entry point) ──────────────────────────────

function Send-TdaMessage {
    <#
    .SYNOPSIS
        Posts an outbound DIDComm message to the Switchboard's outbound queue.
    .PARAMETER OutboundMessage
        Hashtable with PeerEndpoint, PackedMessage, MessageType.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline)] [hashtable] $OutboundMessage)
    process {
        # The Switchboard reads this from the pipeline output and enqueues it.
        # Return as PSCustomObject so RunspacePool pipeline picks it up.
        [PSCustomObject]@{
            PeerEndpoint  = $OutboundMessage.PeerEndpoint
            PackedMessage = $OutboundMessage.PackedMessage
            MessageType   = $OutboundMessage.MessageType
        }
    }
}

# ── Periodic inbox depth check ────────────────────────────────────────────────

$script:LastDepthCheck = [datetime]::UtcNow
$script:DepthCheckInterval = [TimeSpan]::FromMinutes(5)

function Test-InboxDepthPeriodically {
    $now = [datetime]::UtcNow
    if (($now - $script:LastDepthCheck) -ge $script:DepthCheckInterval) {
        Import-JitLobeIfNeeded -LobeName 'Svrn7.Notifications'
        Test-TdaInboxDepth -ErrorAction SilentlyContinue
        $script:LastDepthCheck = $now
    }
}

# ── Main loop ─────────────────────────────────────────────────────────────────
# The Switchboard C# service calls individual dispatch functions rather than
# running this loop directly. This script is sourced once at pool init.
# The loop below is available for standalone testing.

Write-Host "Agent 1 Coordinator: script loaded. LOBEs ready." -ForegroundColor Cyan
Write-Verbose "Agent 1: $SVRN7 context available. Epoch $($SVRN7.CurrentEpoch)."
