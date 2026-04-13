#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Web 7.0 TDA — Agent N Invoicing Runspace Script.

.DESCRIPTION
    Task runspace opened on demand by the Switchboard for invoice/1.0/request messages.
    Returned to the RunspacePool after each task completes.

    Derived from: Agent N — Invoicing (PowerShell Runspace) — DSA 0.24 Epoch 0 (PPML).

.NOTES
    DIDComm protocol: did:drn:svrn7.net/protocols/invoice/1.0/request
    Routing: Switchboard → Invoke-AgentRunspace Invoicing $msgDid

    Full pipeline:
        Get-TdaMessage -Did $msgDid
            | ConvertFrom-TdaInvoiceRequest     ← Svrn7.Invoicing.psm1 (JIT)
            | Resolve-InvoiceAmount             ← Svrn7.Invoicing.psm1 (JIT)
            | Invoke-Svrn7Transfer              ← Svrn7.Society.psm1 (eager, same-Society)
            | New-TdaInvoiceReceipt             ← Svrn7.Invoicing.psm1 (JIT)
            | Send-TdaMessage

    Cross-Society invoices use Invoke-Svrn7ExternalTransfer instead.
    On failure: Send-TdaInvoiceError is called.

    Parameters (injected by Switchboard via AddParameter):
        $MessageDid — TDA resource DID URL of the inbox message
#>

param(
    [Parameter(Mandatory)]
    [string] $MessageDid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# JIT import — Invoicing LOBE
$invoicingPath = $SVRN7_JIT_LOBES |
    Where-Object { $_ -like '*Svrn7.Invoicing*' } |
    Select-Object -First 1

if ($invoicingPath) {
    Import-Module $invoicingPath -Force -ErrorAction Stop
}

# ── Execute invoicing pipeline ────────────────────────────────────────────────

Write-Verbose "Agent N / Invoicing: processing $MessageDid"

$invoice    = $null
$invoiceId  = $null

try {
    # Step 1: Resolve and parse the invoice request
    $invoice = Get-TdaMessage -Did $MessageDid |
               ConvertFrom-TdaInvoiceRequest |
               Resolve-InvoiceAmount

    if (-not $invoice) {
        throw "Invoice parsing returned null for $MessageDid"
    }

    $invoiceId = $invoice.InvoiceId
    Write-Verbose "Agent N / Invoicing: invoice $invoiceId — $($invoice.TotalGrana) grana"

    # Step 2: Determine transfer type — same-Society or cross-Society
    # Check membership to determine same-Society vs cross-Society transfer
    $membershipCheck = Test-Svrn7SocietyMember -Did $invoice.PayeeDid

    $transferResult = if ($membershipCheck.IsMember) {
        # Same-Society transfer — use Invoke-Svrn7IncomingTransfer
        # (Society.psm1). This processes an already-authenticated transfer
        # request backed by the signed invoice credential.
        Write-Verbose "Agent N / Invoicing: same-Society transfer for $invoiceId"
        $xferResult = Invoke-Svrn7IncomingTransfer `
            -PayerDid    $invoice.PayerDid `
            -PayeeDid    $invoice.PayeeDid `
            -AmountGrana $invoice.TotalGrana `
            -Nonce       ([guid]::NewGuid().ToString())

        @{
            Success     = $xferResult.Success
            TransferId  = $xferResult.TransferId
            PayerDid    = $invoice.PayerDid
            PayeeDid    = $invoice.PayeeDid
            AmountGrana = $invoice.TotalGrana
            ReceiptVcId = $xferResult.ReceiptVcId
            InvoiceId   = $invoiceId
            ErrorMessage= $xferResult.ErrorMessage
        }
    } else {
        # Cross-Society transfer — Invoke-Svrn7ExternalTransfer
        # Requires TargetSocietyDid resolved from payee DID Document.
        Write-Verbose "Agent N / Invoicing: cross-Society transfer for $invoiceId"
        $payeeDoc         = $SVRN7.Driver.ResolveDidDocumentAsync(
            $invoice.PayeeDid).GetAwaiter().GetResult()
        $targetSocietyDid = $payeeDoc.Controller  # Society that controls payee DID

        $xferResult = Invoke-Svrn7ExternalTransfer `
            -PayerDid         $invoice.PayerDid `
            -PayeeDid         $invoice.PayeeDid `
            -TargetSocietyDid $targetSocietyDid `
            -AmountGrana      $invoice.TotalGrana `
            -Nonce            ([guid]::NewGuid().ToString())

        @{
            Success     = $xferResult.Success
            TransferId  = $xferResult.TransferId
            PayerDid    = $invoice.PayerDid
            PayeeDid    = $invoice.PayeeDid
            AmountGrana = $invoice.TotalGrana
            ReceiptVcId = $xferResult.ReceiptVcId
            InvoiceId   = $invoiceId
            ErrorMessage= $xferResult.ErrorMessage
        }
    }

    if (-not $transferResult.Success) {
        throw "Transfer failed: $($transferResult.ErrorMessage)"
    }

    # Step 3: Build and return the invoice receipt
    $outbound = $transferResult | New-TdaInvoiceReceipt

    Write-Host "Agent N / Invoicing: invoice $invoiceId settled. " +
               "Transfer: $($transferResult.TransferId)" `
               -ForegroundColor Green

    [PSCustomObject]@{
        PeerEndpoint  = $outbound.PeerEndpoint
        PackedMessage = $outbound.PackedMessage
        MessageType   = $outbound.MessageType
    }

} catch {
    Write-Error "Agent N / Invoicing: failed for $MessageDid — $_"

    if ($invoice -and (Get-Command Send-TdaInvoiceError -ErrorAction SilentlyContinue)) {
        $errOutbound = Send-TdaInvoiceError `
            -PayerDid     $invoice.PayerDid `
            -InvoiceId    ($invoiceId ?? 'unknown') `
            -ErrorMessage $_.ToString()

        [PSCustomObject]@{
            PeerEndpoint  = $errOutbound.PeerEndpoint
            PackedMessage = $errOutbound.PackedMessage
            MessageType   = $errOutbound.MessageType
        }
    }
}
