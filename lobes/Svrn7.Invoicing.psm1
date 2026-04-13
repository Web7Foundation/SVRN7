#Requires -Version 7.0
<#
.SYNOPSIS
    SVRN7 Invoicing LOBE — invoice processing via DIDComm invoice protocol.

.DESCRIPTION
    Implements the did:drn:svrn7.net/protocols/invoice/1.0/* DIDComm protocol.
    Computes SVRN7 transfer amounts from invoice line items, executes transfers
    via Invoke-Svrn7Transfer or Invoke-Svrn7ExternalTransfer, and issues a
    TransferReceiptCredential VC as a DIDComm invoice/1.0/receipt.

    Derived from: Agent N — Invoicing (PowerShell Runspace) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    Protocol URIs:
        did:drn:svrn7.net/protocols/invoice/1.0/request — inbound invoice request
        did:drn:svrn7.net/protocols/invoice/1.0/receipt — outbound transfer receipt

    Pipeline:
        Get-TdaMessage | ConvertFrom-TdaInvoiceRequest |
        Resolve-InvoiceAmount | Invoke-Svrn7Transfer |
        New-TdaInvoiceReceipt | Send-TdaMessage
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── ConvertFrom-TdaInvoiceRequest ─────────────────────────────────────────────

function ConvertFrom-TdaInvoiceRequest {
    <#
    .SYNOPSIS
        Extracts invoice fields from an inbound invoice/1.0/request message.

    .PARAMETER MessageDid
        TDA resource DID URL of the inbox message.

    .OUTPUTS
        Hashtable — { MessageDid, PayerDid, PayeeDid, LineItems[], DueDate, Currency }

    .EXAMPLE
        Get-TdaMessage -Did $msgDid | ConvertFrom-TdaInvoiceRequest
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)]
        [string] $MessageDid
    )

    process {
        $msg = $SVRN7.GetMessageAsync($MessageDid).GetAwaiter().GetResult()
        if (-not $msg) {
            Write-Warning "Invoicing LOBE: message $MessageDid not found."
            return $null
        }

        $body = $msg.PackedPayload | ConvertFrom-Json -ErrorAction Stop

        if (-not $body.payerDid -or -not $body.payeeDid) {
            throw "Invoicing LOBE: invoice/1.0/request missing payerDid or payeeDid."
        }
        if (-not $body.lineItems -or $body.lineItems.Count -eq 0) {
            throw "Invoicing LOBE: invoice/1.0/request has no lineItems."
        }

        return @{
            MessageDid  = $MessageDid
            PayerDid    = $body.payerDid
            PayeeDid    = $body.payeeDid
            LineItems   = $body.lineItems   # array of { description, amountGrana }
            DueDate     = $body.dueDate
            Currency    = $body.currency ?? 'SRC'
            InvoiceId   = $body.invoiceId ?? [guid]::NewGuid().ToString()
            RequestedAt = [datetimeoffset]::UtcNow.ToString('o')
        }
    }
}

# ── Resolve-InvoiceAmount ─────────────────────────────────────────────────────

function Resolve-InvoiceAmount {
    <#
    .SYNOPSIS
        Computes the total transfer amount in grana from invoice line items.

    .PARAMETER Invoice
        Invoice hashtable from ConvertFrom-TdaInvoiceRequest (pipeline input).

    .OUTPUTS
        Hashtable — invoice extended with TotalGrana field.

    .EXAMPLE
        ConvertFrom-TdaInvoiceRequest | Resolve-InvoiceAmount
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [hashtable] $Invoice
    )

    process {
        $total = 0L
        foreach ($item in $Invoice.LineItems) {
            if ($item.amountGrana -lt 0) {
                throw "Invoicing LOBE: line item '$($item.description)' has negative amountGrana."
            }
            $total += [long]$item.amountGrana
        }

        if ($total -le 0) {
            throw "Invoicing LOBE: invoice total must be greater than zero (got $total grana)."
        }

        $Invoice['TotalGrana'] = $total
        Write-Verbose "Invoicing LOBE: invoice $($Invoice.InvoiceId) total = $total grana"
        return $Invoice
    }
}

# ── New-TdaInvoiceReceipt ─────────────────────────────────────────────────────

function New-TdaInvoiceReceipt {
    <#
    .SYNOPSIS
        Builds an invoice/1.0/receipt OutboundMessage after a successful transfer.

    .DESCRIPTION
        Accepts the transfer result hashtable (pipeline input) and constructs
        a DIDComm receipt containing the TransferReceiptCredential VC.

    .PARAMETER TransferResult
        Result hashtable from Invoke-Svrn7Transfer or Invoke-Svrn7ExternalTransfer.
        Expected fields: TransferId, PayerDid, PayeeDid, AmountGrana, ReceiptVcId,
                         InvoiceId, Success.

    .OUTPUTS
        Hashtable — OutboundMessage for the Switchboard.

    .EXAMPLE
        ConvertFrom-TdaInvoiceRequest | Resolve-InvoiceAmount |
            Invoke-Svrn7Transfer | New-TdaInvoiceReceipt | Send-TdaMessage
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [hashtable] $TransferResult
    )

    process {
        $mySocietyDid = $SVRN7.Driver.GetSocietyDidAsync().GetAwaiter().GetResult()

        $payload = @{
            from           = $mySocietyDid
            to             = $TransferResult.PayerDid
            success        = $TransferResult.Success
            invoiceId      = $TransferResult.InvoiceId
            transferId     = $TransferResult.TransferId
            payerDid       = $TransferResult.PayerDid
            payeeDid       = $TransferResult.PayeeDid
            amountGrana    = $TransferResult.AmountGrana
            receiptVcId    = $TransferResult.ReceiptVcId
            settledAt      = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Compress

        $endpoint = Resolve-TdaEndpoint -Did $TransferResult.PayerDid

        Write-Verbose "Invoicing LOBE: receipt for invoice $($TransferResult.InvoiceId) — transferId $($TransferResult.TransferId)"

        return @{
            PeerEndpoint  = $endpoint
            PackedMessage = $payload
            MessageType   = 'did:drn:svrn7.net/protocols/invoice/1.0/receipt'
        }
    }
}

# ── Send-TdaInvoiceError ──────────────────────────────────────────────────────

function Send-TdaInvoiceError {
    <#
    .SYNOPSIS
        Sends an invoice/1.0/receipt with success=false on transfer failure.

    .PARAMETER PayerDid
        The requesting payer's DID.

    .PARAMETER InvoiceId
        The invoice identifier.

    .PARAMETER ErrorMessage
        Human-readable error description.

    .OUTPUTS
        Hashtable — OutboundMessage for the Switchboard.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $PayerDid,
        [Parameter(Mandatory)] [string] $InvoiceId,
        [Parameter(Mandatory)] [string] $ErrorMessage
    )

    process {
        $mySocietyDid = $SVRN7.Driver.GetSocietyDidAsync().GetAwaiter().GetResult()

        $payload = @{
            from       = $mySocietyDid
            to         = $PayerDid
            success    = $false
            invoiceId  = $InvoiceId
            error      = $ErrorMessage
            failedAt   = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json -Compress

        $endpoint = Resolve-TdaEndpoint -Did $PayerDid
        return @{
            PeerEndpoint  = $endpoint
            PackedMessage = $payload
            MessageType   = 'did:drn:svrn7.net/protocols/invoice/1.0/receipt'
        }
    }
}

# ── Helpers ───────────────────────────────────────────────────────────────────

function Resolve-TdaEndpoint {
    param([string] $Did)
    $doc = $SVRN7.Driver.ResolveDidDocumentAsync($Did).GetAwaiter().GetResult()
    $svc = $doc.Service | Where-Object { $_.type -eq 'DIDComm' } | Select-Object -First 1
    if (-not $svc) { throw "No DIDComm service endpoint for $Did" }
    return $svc.serviceEndpoint
}

Export-ModuleMember -Function @(
    'ConvertFrom-TdaInvoiceRequest',
    'Resolve-InvoiceAmount',
    'New-TdaInvoiceReceipt',
    'Send-TdaInvoiceError'
)
