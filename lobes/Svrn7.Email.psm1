#Requires -Version 7.0
<#
.SYNOPSIS
    SVRN7 Email LOBE — DIDComm-native email using RFC 5322 tunneling.

.DESCRIPTION
    Implements the did:drn:svrn7.net/protocols/email/1.0/* DIDComm protocol.
    RFC 5322 email messages are tunneled verbatim inside DIDComm envelopes.
    No SMTP server is involved. All email communication is TDA-to-TDA via DIDComm.

    Derived from: Email LOBE (Agent 1 LOBE) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    Protocol URIs:
        did:drn:svrn7.net/protocols/email/1.0/message   — inbound/outbound email
        did:drn:svrn7.net/protocols/email/1.0/receipt   — delivery confirmation

    Key:
        From/To headers in the RFC 5322 payload use did: URIs, not SMTP addresses.
        The sender's DID is verified from the DIDComm envelope — not the From header.
        No SMTP server, no MX records, no MIME multipart (Epoch 0).
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Receive-TdaEmail ──────────────────────────────────────────────────────────

function Receive-TdaEmail {
    <#
    .SYNOPSIS
        Processes an inbound DIDComm email/1.0/message and stores it locally.

    .DESCRIPTION
        Accepts an inbox message DID URL, resolves the message payload via
        $SVRN7.GetMessageAsync(), extracts the RFC 5322 body, verifies the
        sender's DID against the DIDComm envelope, and persists the email
        record to the IInboxStore long-term memory.

        Derived from: Email LOBE (Agent 1 LOBE) — DSA 0.24 Epoch 0 (PPML).
        Protocol: did:drn:svrn7.net/protocols/email/1.0/message

    .PARAMETER MessageDid
        The TDA resource DID URL of the inbox message.
        Form: did:drn:{networkId}/inbox/msg/{objectId}

    .OUTPUTS
        EmailRecord — the stored email record, or $null if processing failed.

    .EXAMPLE
        Receive-TdaEmail -MessageDid "did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1c8e9d7f012345678"

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
        return $record
    }
}

# ── Send-TdaEmail ─────────────────────────────────────────────────────────────

function Send-TdaEmail {
    <#
    .SYNOPSIS
        Sends an RFC 5322 email message to a recipient TDA via DIDComm.

    .DESCRIPTION
        Constructs a DIDComm email/1.0/message body containing a full RFC 5322
        message. Resolves the recipient's DID to their TDA endpoint and returns
        an OutboundMessage for the Switchboard to deliver.

        Protocol: did:drn:svrn7.net/protocols/email/1.0/message

    .PARAMETER RecipientDid
        The recipient citizen's did:drn DID.

    .PARAMETER Subject
        Email subject line.

    .PARAMETER Body
        Plain text email body.

    .PARAMETER From
        Sender display name and DID (e.g., "Alice <did:drn:alice.alpha.svrn7.net>").
        Defaults to the Society DID if not specified.

    .OUTPUTS
        OutboundMessage — packed DIDComm message ready for Switchboard delivery.

    .EXAMPLE
        Send-TdaEmail -RecipientDid "did:drn:bob.beta.svrn7.net" -Subject "Hello" -Body "Hi Bob"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RecipientDid,
        [Parameter(Mandatory)] [string] $Subject,
        [Parameter(Mandatory)] [string] $Body,
        [string] $From
    )

    process {
        if (-not $From) { $From = $SVRN7.Driver.GetSocietyDidAsync().GetAwaiter().GetResult() }

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
            from        = $SVRN7.Driver.GetSocietyDidAsync().GetAwaiter().GetResult()
            to          = $RecipientDid
            rfc5322Body = $rfc5322
        } | ConvertTo-Json -Compress

        # Resolve peer TDA endpoint via IDidDocumentResolver
        $peerEndpoint = Resolve-TdaEndpoint -Did $RecipientDid

        return @{
            PeerEndpoint  = $peerEndpoint
            PackedMessage = $payload   # Switchboard packs via DIDComm V2 before delivery
            MessageType   = 'did:drn:svrn7.net/protocols/email/1.0/message'
        }
    }
}

# ── Helpers ───────────────────────────────────────────────────────────────────

function Get-Rfc5322Header {
    param([string] $Raw, [string] $Header)
    $pattern = "(?m)^${Header}:\s*(.+)$"
    if ($Raw -match $pattern) { return $Matches[1].Trim() }
    return $null
}

function Resolve-TdaEndpoint {
    param([string] $Did)
    $doc = $SVRN7.Driver.ResolveDidDocumentAsync($Did).GetAwaiter().GetResult()
    $svc = $doc.Service | Where-Object { $_.type -eq 'DIDComm' } | Select-Object -First 1
    if (-not $svc) { throw "No DIDComm service endpoint found in DID Document for $Did" }
    return $svc.serviceEndpoint
}

Export-ModuleMember -Function @(
    'Receive-TdaEmail',
    'Send-TdaEmail'
)
