#Requires -Version 7.0
<#
.SYNOPSIS
    SVRN7 Calendar LOBE — DIDComm-native calendar using iCalendar (RFC 5545) tunneling.

.DESCRIPTION
    Implements the did:drn:svrn7.net/protocols/Svrn7.Calendar.0.8.0/* DIDComm protocol.
    iCalendar (RFC 5545) VCALENDAR objects are tunneled verbatim inside DIDComm envelopes.
    ATTENDEE properties use did: URIs. No CalDAV server. No SMTP/MIME transport.

    Derived from: Calendar LOBE (Agent 1 LOBE) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    Protocol URIs:
        did:drn:svrn7.net/protocols/Svrn7.Calendar.0.8.0/event    — publish an event
        did:drn:svrn7.net/protocols/Svrn7.Calendar.0.8.0/invite   — METHOD:REQUEST meeting invite
        did:drn:svrn7.net/protocols/Svrn7.Calendar.0.8.0/response — METHOD:REPLY accept/decline
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Import-Web7CalendarEvent ───────────────────────────────────────────────────

function Import-Web7CalendarEvent {
    <#
    .SYNOPSIS
        Processes an inbound DIDComm calendar/1.0/event and stores it locally.

    .DESCRIPTION
        Resolves the inbox message, extracts the iCalendar VCALENDAR string,
        maps ATTENDEE did: URIs to citizen records, and returns a calendar event record.

        Derived from: Calendar LOBE (Agent 1 LOBE) — DSA 0.24 Epoch 0 (PPML).
        Protocol: did:drn:svrn7.net/protocols/Svrn7.Calendar.0.8.0/event

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — calendar event record with parsed fields.

    .EXAMPLE
        Import-Web7CalendarEvent -MessageDid "did:drn:alpha.svrn7.net/inbox/msg/5f43a2..."
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        Write-Verbose "Calendar LOBE: processing event $MessageDid"

        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { Write-Warning "Calendar LOBE: $MessageDid not found."; return $null }

        $body  = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        $vcal  = $body.icalendarBody
        if (-not $vcal) { Write-Warning "Calendar LOBE: no icalendarBody."; return $null }

        $record = @{
            MessageDid    = $MessageDid
            OrganizerDid  = $body.from
            Summary       = (Get-ICalProperty -Raw $vcal -Property 'SUMMARY')
            DtStart       = (Get-ICalProperty -Raw $vcal -Property 'DTSTART')
            DtEnd         = (Get-ICalProperty -Raw $vcal -Property 'DTEND')
            Location      = (Get-ICalProperty -Raw $vcal -Property 'LOCATION')
            Uid           = (Get-ICalProperty -Raw $vcal -Property 'UID')
            Attendees     = (Get-ICalAttendees -Raw $vcal)
            ICalendarBody = $vcal
            ReceivedAt    = [datetimeoffset]::UtcNow.ToString('o')
        }

        Write-Verbose "Calendar LOBE: event '$($record.Summary)' from $($record.OrganizerDid)"
        return $record
    }
}

# ── Receive-Web7MeetingRequest ─────────────────────────────────────────────────

function Receive-Web7MeetingRequest {
    <#
    .SYNOPSIS
        Processes an inbound calendar/1.0/invite (METHOD:REQUEST) message.

    .DESCRIPTION
        Parses the meeting invite and returns a pending invite record.
        Call New-Web7CalendarResponse to generate an accept/decline reply.

        Protocol: did:drn:svrn7.net/protocols/Svrn7.Calendar.0.8.0/invite

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — invite record with UID, organizer, summary, attendees.

    .EXAMPLE
        Dequeue-Svrn7Message -Did $msgDid | Receive-Web7MeetingRequest | New-Web7CalendarResponse -Accept
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $event = Import-Web7CalendarEvent -MessageDid $MessageDid
        if (-not $event) { return $null }
        $event['IsInvite'] = $true
        $event['Status']   = 'Pending'
        Write-Verbose "Calendar LOBE: meeting request '$($event.Summary)' from $($event.OrganizerDid)"
        return $event
    }
}

# ── New-Web7CalendarResponse ───────────────────────────────────────────────────

function New-Web7CalendarResponse {
    <#
    .SYNOPSIS
        Generates a calendar/1.0/response (METHOD:REPLY) for a meeting invite.

    .PARAMETER Invite
        The invite hashtable from Receive-Web7MeetingRequest (pipeline input).

    .PARAMETER Accept
        Switch: accept the invite.

    .PARAMETER Decline
        Switch: decline the invite.

    .OUTPUTS
        OutboundMessage — packed DIDComm message ready for Switchboard delivery.

    .EXAMPLE
        $invite | New-Web7CalendarResponse -Accept
    #>
    [CmdletBinding(DefaultParameterSetName = 'Accept')]
    param(
        [Parameter(Mandatory, ValueFromPipeline)] [hashtable] $Invite,
        [Parameter(ParameterSetName = 'Accept')]  [switch]    $Accept,
        [Parameter(ParameterSetName = 'Decline')] [switch]    $Decline
    )

    process {
        $partstat = if ($Accept) { 'ACCEPTED' } else { 'DECLINED' }


        $replyVcal = @"
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//SVRN7 TDA//EN
METHOD:REPLY
BEGIN:VEVENT
UID:$($Invite.Uid)
ORGANIZER:$($Invite.OrganizerDid)
ATTENDEE;PARTSTAT=$partstat:$($SVRN7.LocalDid)
SUMMARY:$($Invite.Summary)
DTSTART:$($Invite.DtStart)
END:VEVENT
END:VCALENDAR
"@

        $peerEndpoint = Resolve-SocietySenderEndpoint -Did $Invite.OrganizerDid
        if (-not $peerEndpoint) {
            Write-Warning "New-Web7CalendarResponse: no DIDComm service endpoint for '$($Invite.OrganizerDid)' — reply skipped."
            return
        }

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Calendar.0.8.0/response'
            from = $SVRN7.LocalDid
            to   = @($Invite.OrganizerDid)
            body = [ordered]@{
                from           = $SVRN7.LocalDid
                to             = $Invite.OrganizerDid
                icalendarBody  = $replyVcal
            }
        } | ConvertTo-Json -Compress -Depth 3

        [Svrn7.TDA.OutboundMessage]::new($peerEndpoint, $envelope)
    }
}

# ── Helpers ───────────────────────────────────────────────────────────────────

function Get-ICalProperty {
    param([string] $Raw, [string] $Property)
    if ($Raw -match "(?m)^${Property}[;:](.+)$") { return $Matches[1].Trim() }
    return $null
}

function Get-ICalAttendees {
    param([string] $Raw)
    $attendees = @()
    $pattern   = '(?m)^ATTENDEE[^:]*:(.+)$'
    foreach ($m in [regex]::Matches($Raw, $pattern)) {
        $attendees += $m.Groups[1].Value.Trim()
    }
    return $attendees
}

Export-ModuleMember -Function @(
    'Import-Web7CalendarEvent',
    'Receive-Web7MeetingRequest',
    'New-Web7CalendarResponse'
)
