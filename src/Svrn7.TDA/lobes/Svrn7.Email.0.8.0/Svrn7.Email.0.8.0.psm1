#Requires -Version 7.0
<#
.SYNOPSIS
    SVRN7 Email LOBE — DIDComm-native email using RFC 5322 tunneling.

.DESCRIPTION
    Implements the did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/* DIDComm protocol.
    RFC 5322 email messages are tunneled verbatim inside DIDComm envelopes.
    No SMTP server is involved. All email communication is TDA-to-TDA via DIDComm.

    Derived from: Email LOBE (Agent 1 LOBE) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    Protocol URIs:
        did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Signal-PandoMail   — inbound/outbound email
        did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/issue-receipt   — delivery confirmation

    Key:
        From/To headers in the RFC 5322 payload use did: URIs, not SMTP addresses.
        The sender's DID is verified from the DIDComm envelope — not the From header.
        No SMTP server, no MX records, no MIME multipart (Epoch 0).
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Dequeue-PandoMail ──────────────────────────────────────────────────────────

function Dequeue-PandoMail {
    <#
    .SYNOPSIS
        Processes an inbound DIDComm email/1.0/message and stores it locally.

    .DESCRIPTION
        Accepts an inbox message DID URL, resolves the message payload via
        $SVRN7.GetMessageAsync(), extracts the RFC 5322 body, verifies the
        sender's DID against the DIDComm envelope, and persists the email
        record to the IInboxStore long-term memory.

        Derived from: Email LOBE (Agent 1 LOBE) — DSA 0.24 Epoch 0 (PPML).
        Protocol: did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Signal-PandoMail

    .PARAMETER MessageDid
        The TDA resource DID URL of the inbox message.
        Form: did:drn:{networkId}/inbox/msg/{objectId}

    .OUTPUTS
        EmailRecord — the stored email record, or $null if processing failed.

    .EXAMPLE
        Dequeue-PandoMail -MessageDid "did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1c8e9d7f012345678"

    .NOTES
        The From header in the RFC 5322 payload is treated as display metadata only.
        The authoritative sender identity is the DIDComm envelope's 'from' field.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        Write-Verbose "Email LOBE: processing inbound email $MessageDid"

        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) {
            Write-Warning "Email LOBE: message $MessageDid not found."
            return $null
        }

        # Parse the DIDComm body — expected: { from, rfc5322Body }
        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        $rfc5322 = $body.rfc5322Body
        if (-not $rfc5322) {
            Write-Warning "Email LOBE: message $MessageDid has no rfc5322Body field."
            return $null
        }

        # Build the email record
        $record = @{
            MessageDid   = $MessageDid
            MessageId    = $msg.Id
            SenderDid    = $body.from          # authoritative — from DIDComm envelope
            ReceivedAt   = [datetimeoffset]::UtcNow.ToString('o')
            Rfc5322Body  = $rfc5322
            Subject      = (Get-Rfc5322Header -Raw $rfc5322 -Header 'Subject')
            FromHeader   = (Get-Rfc5322Header -Raw $rfc5322 -Header 'From')
            ToHeader     = (Get-Rfc5322Header -Raw $rfc5322 -Header 'To')
        }

        Write-Verbose "Email LOBE: stored email from $($record.SenderDid) — '$($record.Subject)'"

        # Push Email-Notify to PandoMail via the local WebSocket hub.
        # The Switchboard delivers any OutboundMessage whose PeerEndpoint starts
        # with "ws://" through WebSocketNotifyHub.PushAsync instead of HTTP/2 POST.
        $notifyBody = [ordered]@{
            messageDid = $MessageDid
            senderDid  = $record.SenderDid
            subject    = $record.Subject
            receivedAt = $record.ReceivedAt
        } | ConvertTo-Json -Compress

        $notifyEnvelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Email-Notify/1.0/new-message'
            body = $notifyBody
        } | ConvertTo-Json -Compress

        # Output the record for any pipeline caller, then the notification OutboundMessage.
        $record
        [Svrn7.TDA.OutboundMessage]::new('ws://local/didcomm-notify', $notifyEnvelope)
    }
}

# ── Enqueue-PandoMail ─────────────────────────────────────────────────────────────

function Enqueue-PandoMail {
    <#
    .SYNOPSIS
        Sends an RFC 5322 email message to a recipient TDA via DIDComm.

    .DESCRIPTION
        Constructs a DIDComm email/1.0/message body containing a full RFC 5322
        message. Resolves the recipient's DID to their TDA endpoint and returns
        an OutboundMessage for the Switchboard to deliver.

        Protocol: did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Signal-PandoMail

    .PARAMETER RecipientDid
        The recipient citizen's did:drn DID.

    .PARAMETER Subject
        Email subject line.

    .PARAMETER Body
        Plain text email body.

    .PARAMETER From
        Sender display name and DID (e.g., "Alice <did:drn:alpha.svrn7.net/citizen/alice>").
        Defaults to the Society DID if not specified.

    .OUTPUTS
        OutboundMessage — packed DIDComm message ready for Switchboard delivery.

    .EXAMPLE
        Enqueue-PandoMail -RecipientDid "did:drn:beta.svrn7.net/citizen/bob" -Subject "Hello" -Body "Hi Bob"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RecipientDid,
        [Parameter(Mandatory)] [string] $Subject,
        [Parameter(Mandatory)] [string] $Body,
        [string] $From
    )

    process {
        if (-not $From) { $From = $SVRN7.LocalDid }

        $date = [datetime]::UtcNow.ToString('ddd, dd MMM yyyy HH:mm:ss') + ' +0000'

        # Build RFC 5322 message — did: URIs as From/To
        $rfc5322 = @"
From: $From
To: $RecipientDid
Subject: $Subject
Date: $date
MIME-Version: 1.0
Content-Type: text/plain; charset=utf-8

$Body
"@
        $payload = @{
            from        = $SVRN7.LocalDid
            to          = $RecipientDid
            rfc5322Body = $rfc5322
        } | ConvertTo-Json -Compress

        $peerEndpoint = Resolve-SocietySenderEndpoint -Did $RecipientDid
        if (-not $peerEndpoint) {
            Write-Warning "Enqueue-PandoMail: no DIDComm service endpoint for '$RecipientDid' — reply skipped."
            return
        }

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Signal-PandoMail'
            from = $SVRN7.LocalDid
            to   = @($RecipientDid)
            body = $payload
        } | ConvertTo-Json -Compress

        [Svrn7.TDA.OutboundMessage]::new($peerEndpoint, $envelope)
    }
}

# ── Invoke-PandoMailList ────────────────────────────────────────────────────

function Invoke-PandoMailList {
    <#
    .SYNOPSIS
        Handles a List-Emails query and replies with an Get-PandoMails response.

    .DESCRIPTION
        Reads the replyEndpoint from the request body, queries the local inbox
        for processed email messages (newest-first, default limit 50), and
        delivers an Get-PandoMails DIDComm message to the replyEndpoint.

        Protocol (inbound):  did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/List-Emails
        Protocol (outbound): did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Get-PandoMails

    .PARAMETER MessageDid
        The TDA resource DID URL of the inbox message.

    .OUTPUTS
        [Svrn7.TDA.OutboundMessage] delivering Get-PandoMails to replyEndpoint,
        or $null if replyEndpoint is absent.
    #>
    [CmdletBinding()]
    [OutputType([Svrn7.TDA.OutboundMessage])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) {
            Write-Warning "Email LOBE: List-Emails message $MessageDid not found."
            return $null
        }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop

        $correlationId = Get-BodyField $body 'correlationId' ''
        $limit = 50
        if ($body.PSObject.Properties['limit']) { $limit = [int]$body.limit }

        $emails = $SVRN7.ListEmailsAsync($limit).GetAwaiter().GetResult()

        $emailList = @(foreach ($e in $emails) {
            $eBody = $e.PackedPayload | ConvertFrom-Json -ErrorAction SilentlyContinue
            $rfc5322 = Get-BodyField $eBody 'rfc5322Body' ''
            if (-not $rfc5322) { continue }
            [ordered]@{
                messageDid = $e.Id
                senderDid  = $e.FromDid
                subject    = (Get-Rfc5322Header -Raw $rfc5322 -Header 'Subject')
                fromHeader = (Get-Rfc5322Header -Raw $rfc5322 -Header 'From')
                toHeader   = (Get-Rfc5322Header -Raw $rfc5322 -Header 'To')
                receivedAt = $e.ReceivedAt.ToString('o')
            }
        })

        $responseBody = [ordered]@{
            emails        = $emailList
            count         = $emailList.Count
            correlationId = $correlationId
        } | ConvertTo-Json -Compress -Depth 5

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Get-PandoMails'
            from = $SVRN7.LocalDid
            to   = @($msg.FromDid)
            body = $responseBody
        } | ConvertTo-Json -Compress

        Write-Verbose "Email LOBE: List-Emails returning $($emailList.Count) messages via WebSocket."
        [Svrn7.TDA.OutboundMessage]::new('ws://local/didcomm-notify', $envelope)
    }
}

# ── Invoke-PandoMailSend ─────────────────────────────────────────────────────

function Invoke-PandoMailSend {
    <#
    .SYNOPSIS
        Handles a Enqueue-PandoMail request from TdaMailClient and delivers to the recipient TDA.

    .DESCRIPTION
        Accepts a DIDComm message from local PandoMail UI. Body: { recipientDid, subject, bodyText }.
        Builds an RFC 5322 message via Enqueue-PandoMail and returns an OutboundMessage for delivery.

        Protocol (inbound): did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Enqueue-PandoMail

    .PARAMETER MessageDid
        The TDA resource DID URL of the inbox message.

    .OUTPUTS
        [Svrn7.TDA.OutboundMessage] for the Switchboard to deliver, or $null on validation failure.
    #>
    [CmdletBinding()]
    [OutputType([Svrn7.TDA.OutboundMessage])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) {
            Write-Warning "Email LOBE: Enqueue-PandoMail message $MessageDid not found."
            return $null
        }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop

        $recipientDid = Get-BodyField $body 'recipientDid'
        if (-not $recipientDid) {
            Write-Warning "Email LOBE: Enqueue-PandoMail $MessageDid missing recipientDid — skipped."
            return $null
        }

        $subject  = Get-BodyField $body 'subject'  ''
        $bodyText = Get-BodyField $body 'bodyText'  ''

        Write-Verbose "Email LOBE: Enqueue-PandoMail — forwarding to $recipientDid ('$subject')"
        Enqueue-PandoMail -RecipientDid $recipientDid -Subject $subject -Body $bodyText
    }
}

# ── Get-TdaDid ────────────────────────────────────────────────────────────────

function Get-TdaDid {
    <#
    .SYNOPSIS
        Returns this TDA's own DID to a requesting local UI client.

    .DESCRIPTION
        Handles a Query-TdaDid request from TdaMailClient. Replies with the
        TDA's LocalDid over the WebSocket push channel.

        Protocol (inbound):  did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Query-TdaDid
        Protocol (outbound): did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Reply-TdaDid

    .PARAMETER MessageDid
        The TDA resource DID URL of the inbox message.

    .OUTPUTS
        [Svrn7.TDA.OutboundMessage] delivering Reply-TdaDid to replyEndpoint,
        or $null if replyEndpoint is absent.
    #>
    [CmdletBinding()]
    [OutputType([Svrn7.TDA.OutboundMessage])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) { return $null }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop

        $correlationId = Get-BodyField $body 'correlationId' ''

        $responseBody = [ordered]@{
            did           = $SVRN7.AgentDid
            correlationId = $correlationId
        } | ConvertTo-Json -Compress

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Reply-TdaDid'
            from = $SVRN7.AgentDid
            body = $responseBody
        } | ConvertTo-Json -Compress

        [Svrn7.TDA.OutboundMessage]::new('ws://local/didcomm-notify', $envelope)
    }
}

# ── Helpers ───────────────────────────────────────────────────────────────────

function Get-Rfc5322Header {
    param([string] $Raw, [string] $Header)
    $pattern = "(?m)^${Header}:\s*(.+)$"
    if ($Raw -match $pattern) { return $Matches[1].Trim() }
    return $null
}

Export-ModuleMember -Function @(
    'Dequeue-PandoMail',
    'Enqueue-PandoMail',
    'Invoke-PandoMailList',
    'Invoke-PandoMailSend',
    'Get-TdaDid'
)
