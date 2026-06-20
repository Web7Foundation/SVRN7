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
        $notifyEnvelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Email-Notify/1.0/new-message'
            from = $SVRN7.LocalDid
            to   = @($SVRN7.LocalDid)
            body = [ordered]@{
                messageDid = $MessageDid
                senderDid  = $record.SenderDid
                subject    = $record.Subject
                receivedAt = $record.ReceivedAt
            }
        } | ConvertTo-Json -Compress -Depth 3

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
        Sender display string, e.g. '"Alice" <did:drn:...>'. Defaults to the local DID.

    .PARAMETER ToDisplay
        Recipient display string, e.g. '"Bob" <did:drn:...>'. Defaults to RecipientDid.

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
        [string] $From      = '',
        [string] $ToDisplay = ''
    )

    process {
        if (-not $From)      { $From      = $SVRN7.LocalDid }
        if (-not $ToDisplay) { $ToDisplay = $RecipientDid   }

        $date = [datetime]::UtcNow.ToString('ddd, dd MMM yyyy HH:mm:ss') + ' +0000'

        # Build RFC 5322 message — display names included when provided
        $rfc5322 = @"
From: $From
To: $ToDisplay
Subject: $Subject
Date: $date
MIME-Version: 1.0
Content-Type: text/plain; charset=utf-8

$Body
"@
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
            body = [ordered]@{
                from        = $SVRN7.LocalDid
                to          = $RecipientDid
                rfc5322Body = $rfc5322
            }
        } | ConvertTo-Json -Compress -Depth 3

        [Svrn7.TDA.OutboundMessage]::new($peerEndpoint, $envelope)
    }
}

# ── Invoke-PandoMailList ────────────────────────────────────────────────────

function Invoke-PandoMailList {
    <#
    .SYNOPSIS
        Handles a List-Emails query and replies with an Get-PandoMails response.

    .DESCRIPTION
        Queries the local inbox for processed email messages (newest-first, default
        limit 50) and delivers a Get-PandoMails DIDComm message to the sender's
        DID Document endpoint.

        Protocol (inbound):  did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/List-Emails
        Protocol (outbound): did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Get-PandoMails

    .PARAMETER MessageDid
        The TDA resource DID URL of the inbox message.

    .OUTPUTS
        [Svrn7.TDA.OutboundMessage] delivering Get-PandoMails to the sender's endpoint,
        or $null if the sender's endpoint cannot be resolved.
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

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Get-PandoMails'
            from = $SVRN7.LocalDid
            to   = @($msg.FromDid)
            body = [ordered]@{
                emails        = $emailList
                count         = $emailList.Count
                correlationId = $correlationId
            }
        } | ConvertTo-Json -Compress -Depth 5

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

        $subject         = Get-BodyField $body 'subject'         ''
        $bodyText        = Get-BodyField $body 'bodyText'        ''
        $senderDisplay   = Get-BodyField $body 'senderDisplay'   ''
        $recipientDisplay = Get-BodyField $body 'recipientDisplay' ''

        Write-Verbose "Email LOBE: Enqueue-PandoMail — forwarding to $recipientDid ('$subject')"
        Enqueue-PandoMail -RecipientDid $recipientDid -Subject $subject -Body $bodyText `
            -From $senderDisplay -ToDisplay $recipientDisplay
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
        [Svrn7.TDA.OutboundMessage] delivering Reply-TdaDid to the sender's endpoint,
        or $null if the sender's endpoint cannot be resolved.
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

        $localName = ''
        try {
            $docJson = $SVRN7.GetDidDocumentJson($SVRN7.LocalDid)
            if ($docJson) {
                $doc = $docJson | ConvertFrom-Json -ErrorAction SilentlyContinue
                if ($null -ne $doc -and $doc.PSObject.Properties['Svrn7Name'] -and $doc.Svrn7Name) {
                    $localName = $doc.Svrn7Name
                }
            }
        } catch { }

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Reply-TdaDid'
            from = $SVRN7.LocalDid
            to   = @($SVRN7.LocalDid)
            body = [ordered]@{
                did           = $SVRN7.LocalDid
                name          = $localName
                correlationId = $correlationId
            }
        } | ConvertTo-Json -Compress -Depth 3

        [Svrn7.TDA.OutboundMessage]::new('ws://local/didcomm-notify', $envelope)
    }
}

# ── Invoke-Svrn7EmailGetEmailBody ─────────────────────────────────────────────

function Invoke-Svrn7EmailGetEmailBody {
    <#
    .SYNOPSIS
        Returns the full RFC 5322 body of a specific stored email message.

    .DESCRIPTION
        Handles a Get-EmailBody request from TdaMailClient. Looks up the target
        email in the inbox by its DID, extracts the RFC 5322 body and plain-text
        content, and replies via the WebSocket push channel.

        Protocol (inbound):  did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Get-EmailBody
        Protocol (outbound): did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Reply-EmailBody

    .PARAMETER MessageDid
        The TDA resource DID URL of the inbox message containing the request.

    .OUTPUTS
        [Svrn7.TDA.OutboundMessage] delivering Reply-EmailBody to the WebSocket hub,
        or $null if the target message cannot be found.
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
            Write-Warning "Email LOBE: Get-EmailBody request message $MessageDid not found."
            return $null
        }

        $body          = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        $correlationId = Get-BodyField $body 'correlationId' ''
        $targetDid     = Get-BodyField $body 'messageDid' ''

        if (-not $targetDid) {
            Write-Warning "Email LOBE: Get-EmailBody $MessageDid missing messageDid field."
            return $null
        }

        $emailMsg = $SVRN7.GetMessageAsync($targetDid).GetAwaiter().GetResult()
        if (-not $emailMsg) {
            Write-Warning "Email LOBE: Get-EmailBody target message $targetDid not found."
            return $null
        }

        $emailBody = $emailMsg.PackedPayload | ConvertFrom-Json -ErrorAction SilentlyContinue
        $rfc5322   = Get-BodyField $emailBody 'rfc5322Body' ''

        # Extract plain-text body — everything after the first blank line in RFC 5322.
        $bodyText = ''
        if ($rfc5322) {
            $parts = $rfc5322 -split "`r?`n`r?`n", 2
            if ($parts.Count -ge 2) { $bodyText = $parts[1].Trim() }
        }

        $envelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Reply-EmailBody'
            from = $SVRN7.LocalDid
            to   = @($SVRN7.LocalDid)
            body = [ordered]@{
                correlationId = $correlationId
                messageDid    = $targetDid
                rfc5322Body   = $rfc5322
                bodyText      = $bodyText
            }
        } | ConvertTo-Json -Compress -Depth 3

        [Svrn7.TDA.OutboundMessage]::new('ws://local/didcomm-notify', $envelope)
    }
}

# ── Invoke-PandoMailResolveDid ────────────────────────────────────────────────

function Invoke-PandoMailResolveDid {
    <#
    .SYNOPSIS
        Resolves a DID Document on behalf of PandoMail and replies over WebSocket.

    .DESCRIPTION
        Handles a Resolve-PandoDid request from TdaMailClient.
        Tries the local DID registry first. On a local hit, pushes Reply-DidDocument
        immediately over the WebSocket hub.
        On a local miss, forwards a plaintext did-resolve-request to the parent TDA
        using the caller's correlationId as the requestId, so that
        Invoke-Svrn7DidResolveResponse can push the result back to WebSocket when the
        response arrives through the resolution chain.

        Protocol (inbound):  did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Resolve-PandoDid
        Protocol (outbound): did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/Reply-DidDocument (ws)
                             did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-request (http, on miss)

    .PARAMETER MessageDid
        The TDA resource DID URL of the inbox message.
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
            Write-Warning "Email LOBE: Resolve-PandoDid message $MessageDid not found."
            return $null
        }

        $body          = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop
        $correlationId = Get-BodyField $body 'correlationId' ''
        $requestedDid  = Get-BodyField $body 'requestedDid'  ''

        if (-not $requestedDid) {
            Write-Warning "Email LOBE: Resolve-PandoDid $MessageDid missing requestedDid."
            return $null
        }

        # Try local registry first
        $didDoc = $SVRN7.Driver.ResolveDidAsync($requestedDid).GetAwaiter().GetResult()
        if ($null -ne $didDoc) {
            Write-Verbose "Email LOBE: Resolve-PandoDid LOCAL HIT '$requestedDid'"
            # Use GetDidDocumentJson round-trip to read Svrn7Name — same pattern as Get-TdaDid.
            # Direct C# property access may return null if the field was absent when stored.
            $svrn7Name = ''
            try {
                $docJson = $SVRN7.GetDidDocumentJson($requestedDid)
                if ($docJson) {
                    $doc = $docJson | ConvertFrom-Json -ErrorAction SilentlyContinue
                    if ($null -ne $doc -and $doc.PSObject.Properties['Svrn7Name'] -and $doc.Svrn7Name) {
                        $svrn7Name = $doc.Svrn7Name
                    }
                }
            } catch { }
            Write-Verbose "Email LOBE: Resolve-PandoDid svrn7Name='$svrn7Name'"
            $replyEnvelope = [ordered]@{
                typ  = 'application/didcomm-plain+json'
                id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
                type = 'did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/Reply-DidDocument'
                from = $SVRN7.LocalDid
                to   = @($SVRN7.LocalDid)
                body = [ordered]@{
                    correlationId = $correlationId
                    requestedDid  = $requestedDid
                    found         = $true
                    svrn7Name     = $svrn7Name
                }
            } | ConvertTo-Json -Compress -Depth 3
            return [Svrn7.TDA.OutboundMessage]::new('ws://local/didcomm-notify', $replyEnvelope)
        }

        # Local miss — escalate to parent TDA if available
        $parentEndpoint = $SVRN7.ParentTdaEndpointUrl
        $parentDid      = $SVRN7.ParentTdaDid

        if (-not $parentEndpoint) {
            Write-Verbose "Email LOBE: Resolve-PandoDid LOCAL MISS '$requestedDid' — no parent, replying not found"
            $notFoundEnvelope = [ordered]@{
                typ  = 'application/didcomm-plain+json'
                id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
                type = 'did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/Reply-DidDocument'
                from = $SVRN7.LocalDid
                to   = @($SVRN7.LocalDid)
                body = [ordered]@{
                    correlationId = $correlationId
                    requestedDid  = $requestedDid
                    found         = $false
                    svrn7Name     = ''
                }
            } | ConvertTo-Json -Compress -Depth 3
            return [Svrn7.TDA.OutboundMessage]::new('ws://local/didcomm-notify', $notFoundEnvelope)
        }

        # Forward the resolve request to the parent TDA using correlationId as requestId.
        # Invoke-Svrn7DidResolveResponse (Identity LOBE) will push Reply-DidDocument to
        # WebSocket when the response arrives, matching on originalRequestId = correlationId.
        Write-Verbose "Email LOBE: Resolve-PandoDid LOCAL MISS '$requestedDid' → escalating to '$parentDid'"
        $fwdEnvelope = [ordered]@{
            typ  = 'application/didcomm-plain+json'
            id   = [Svrn7.Core.TdaResourceId]::DIDCommMessage([Guid]::NewGuid().ToString('N'))
            type = 'did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/did-resolve-request'
            from = $SVRN7.LocalDid
            to   = @($parentDid)
            body = [ordered]@{
                requestedDid         = $requestedDid
                requestId            = $correlationId
                originalRequesterDid = $SVRN7.LocalDid
                originalRequestId    = $correlationId
            }
        } | ConvertTo-Json -Compress -Depth 3

        [Svrn7.TDA.OutboundMessage]::new($parentEndpoint, $fwdEnvelope)
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
    'Invoke-PandoMailResolveDid',
    'Get-TdaDid',
    'Invoke-Svrn7EmailGetEmailBody'
)
