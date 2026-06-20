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
            Svrn7.Common.0.8.0.psm1
            Svrn7.Federation.0.8.0.psm1
            Svrn7.Society.0.8.0.psm1
            Svrn7.UX.0.8.0.psm1           → ux/1.0/* (balance updates, notifications, registration)

        JIT (imported on first message of each type):
            Svrn7.Email.0.8.0.psm1        → did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/*
            Svrn7.Calendar.0.8.0.psm1     → did:drn:svrn7.net/protocols/Svrn7.Calendar.0.8.0/*
            Svrn7.Presence.0.8.0.psm1     → did:drn:svrn7.net/protocols/Svrn7.Presence.0.8.0/*
            Svrn7.Notifications.0.8.0.psm1→ did:drn:svrn7.net/protocols/Svrn7.Notifications.0.8.0/*
            Svrn7.Identity.0.8.0.psm1     → did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-*
                                      did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/vc-*

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
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($_)
        $stem -eq $LobeName -or $stem -like "$LobeName.*"
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
        $result = Dequeue-PandoMail -MessageDid $MessageDid
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
            Dequeue-Svrn7Message -Did $MessageDid |
                Receive-Web7MeetingRequest |
                New-Web7CalendarResponse -Accept
        } else {
            Import-Web7CalendarEvent -MessageDid $MessageDid
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
            Add-Web7PresenceSubscription -MessageDid $MessageDid
        } else {
            Update-Web7Presence -MessageDid $MessageDid
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
        $result = Invoke-Web7Notification -MessageDid $MessageDid
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
            '*/ux/1.0/balance-update'        { Render-Web7BalanceUpdate -MessageDid $MessageDid }
            '*/ux/1.0/notification'           { Render-Web7Notification -MessageDid $MessageDid }
            '*/ux/1.0/registration-complete'  { Render-Web7RegistrationComplete -MessageDid $MessageDid }
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
            '*/Svrn7.Identity/0.8.0/did-resolve-request'               { Resolve-Svrn7Did -MessageDid $MessageDid }
            '*/Svrn7.Identity/0.8.0/vc-resolve-by-subject-request'     { Get-Svrn7VcById  -MessageDid $MessageDid }
            default { Write-Warning "Agent 1 / Identity: unhandled type $MessageType"; $null }
        }
        Write-Verbose "Agent 1 / Identity: processed $MessageDid ($MessageType)"
        return $result
    } catch {
        Write-Error "Agent 1 / Identity: failed for $MessageDid — $_"
    }
}

# ── Dequeue-Svrn7Message (pass-by-reference resolution) ────────────────────────────
# Exposed as a cmdlet for use by all LOBE pipelines in this runspace.

function Dequeue-Svrn7Message {
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
        if (-not $msg) { throw "Dequeue-Svrn7Message: message '$Did' not found." }
        # Pass through with the DID URL attached for downstream pipeline use
        $msg | Add-Member -NotePropertyName 'MessageDid' -NotePropertyValue $Did -PassThru
    }
}

# ── Enqueue-Svrn7Message (outbound queue entry point) ──────────────────────────────

function Enqueue-Svrn7Message {
    <#
    .SYNOPSIS
        Posts an outbound DIDComm message to the Switchboard's outbound queue.
    .PARAMETER OutboundMessage
        OutboundMessage from a LOBE handler (pipeline input).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline)] [Svrn7.TDA.OutboundMessage] $OutboundMessage)
    process {
        # Emits the OutboundMessage to the pipeline. The Switchboard collects it
        # from the pipeline result set and calls _outboundQueue.Enqueue() in C#.
        $OutboundMessage
    }
}

# ── Periodic inbox depth check ────────────────────────────────────────────────

$script:LastDepthCheck = [datetime]::UtcNow
$script:DepthCheckInterval = [TimeSpan]::FromMinutes(5)

function Test-InboxDepthPeriodically {
    $now = [datetime]::UtcNow
    if (($now - $script:LastDepthCheck) -ge $script:DepthCheckInterval) {
        Import-JitLobeIfNeeded -LobeName 'Svrn7.Notifications'
        Test-Web7InboxDepth -ErrorAction SilentlyContinue
        $script:LastDepthCheck = $now
    }
}

# ── Main loop ─────────────────────────────────────────────────────────────────
# The Switchboard C# service calls individual dispatch functions rather than
# running this loop directly. This script is sourced once at pool init.
# The loop below is available for standalone testing.

Write-Host "Agent 1 Coordinator: script loaded. LOBEs ready." -ForegroundColor Cyan
Write-Verbose "Agent 1: $SVRN7 context available. Epoch $($SVRN7.CurrentEpoch)."
