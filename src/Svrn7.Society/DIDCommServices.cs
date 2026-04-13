using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Svrn7.Core;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;
using Svrn7.DIDComm;

namespace Svrn7.Society;

// ── DIDCommTransferHandler ────────────────────────────────────────────────────

/// <summary>
/// Handles all incoming DIDComm transfer protocol messages for this Society.
/// All transfers — same-Society and cross-Society — arrive here after DIDComm unpack.
///
/// Protocol URIs handled:
///   transfer/1.0/request      — citizen-initiated transfer
///   transfer/1.0/order        — cross-Society TransferOrderCredential from another Society
///   transfer/1.0/order-receipt— settlement confirmation from receiving Society
/// </summary>
public sealed class DIDCommTransferHandler : IDIDCommTransferHandler
{
    private readonly ISvrn7Driver          _driver;
    private readonly IDIDCommService       _didComm;
    private readonly IVcService            _vcService;
    private readonly ICryptoService        _crypto;
    private readonly Svrn7SocietyOptions   _opts;
    private readonly ILogger<DIDCommTransferHandler> _log;

    private readonly IProcessedOrderStore _processedOrders;

    public DIDCommTransferHandler(
        ISvrn7Driver driver,
        IDIDCommService didComm,
        IVcService vcService,
        ICryptoService crypto,
        IProcessedOrderStore processedOrders,
        IOptions<Svrn7SocietyOptions> opts,
        ILogger<DIDCommTransferHandler> log)
    {
        _driver    = driver;
        _didComm   = didComm;
        _vcService = vcService;
        _crypto    = crypto;
        _processedOrders = processedOrders;
        _opts      = opts.Value;
        _log       = log;
    }

    // ── Incoming transfer request (citizen → Society handler) ─────────────────

    public async Task<string> HandleTransferRequestAsync(
        string packedMessage, CancellationToken ct = default)
    {
        var message = await _didComm.UnpackAsync(
            packedMessage, _opts.SocietyMessagingPrivateKeyEd25519, ct);

        var request = JsonSerializer.Deserialize<TransferRequest>(message.Body)!;

        _log.LogInformation("Handling transfer request from {Payer} to {Payee} ({Amount} grana)",
            request.PayerDid, request.PayeeDid, request.AmountGrana);

        var result = await _driver.TransferAsync(request, ct);

        // Pack receipt
        var receipt = _didComm.NewMessage()
            .Type(Svrn7Constants.Protocols.TransferReceipt)
            .From(_opts.SocietyDid)
            .Body(new { success = result.Success, error = result.ErrorMessage })
            .Build();

        return await _didComm.PackEncryptedAsync(receipt,
            _opts.SocietyMessagingPrivateKeyEd25519,
            _opts.SocietyMessagingPrivateKeyEd25519,
            DIDCommPackMode.SignThenEncrypt, ct);
    }

    // ── Incoming TransferOrder from another Society ───────────────────────────

    public async Task<string> HandleTransferOrderAsync(
        string packedMessage, CancellationToken ct = default)
    {
        var message = await _didComm.UnpackAsync(
            packedMessage, _opts.SocietyMessagingPrivateKeyEd25519, ct);

        var order = JsonSerializer.Deserialize<TransferOrderCredential>(message.Body)!;

        _log.LogInformation(
            "Handling TransferOrder {TransferId} from Society {Origin} for payee {Payee}",
            order.TransferId, order.OriginSocietyDid, order.PayeeDid);

        // Idempotency: if already processed, return stored receipt (durable via LiteDB)
        var existingReceipt = await _processedOrders.GetReceiptAsync(order.TransferId, ct);
        if (existingReceipt is not null)
        {
            _log.LogDebug("TransferOrder {TransferId} already processed — returning cached receipt",
                order.TransferId);
            return existingReceipt;
        }

        // Credit the payee
        var payeeWallet = await _driver.GetBalanceResultAsync(order.PayeeDid, ct);
        var creditUtxo  = new Utxo
        {
            Id          = _crypto.Blake3Hex(System.Text.Encoding.UTF8.GetBytes(
                              $"CREDIT:{order.TransferId}")),
            OwnerDid    = order.PayeeDid,
            AmountGrana = order.AmountGrana,
        };

        // Direct wallet credit (bypasses validator — order VC is the proof)
        await _driver.AppendToLogAsync("CrossSocietyTransferCredit",
            JsonSerializer.Serialize(new
            {
                transferId       = order.TransferId,
                payeeDid         = order.PayeeDid,
                originSocietyDid = order.OriginSocietyDid,
                amountGrana      = order.AmountGrana,
            }), ct);

        // Build and cache receipt
        var receiptVc = new TransferReceiptCredential
        {
            TransferId       = order.TransferId,
            PayeeDid         = order.PayeeDid,
            CreditedGrana    = order.AmountGrana,
            TargetSocietyDid = _opts.SocietyDid,
            CreditedAt       = DateTimeOffset.UtcNow,
        };

        var receiptMsg = _didComm.NewMessage()
            .Type(Svrn7Constants.Protocols.TransferOrderReceipt)
            .From(_opts.SocietyDid)
            .Body(receiptVc)
            .Build();

        var packed = await _didComm.PackEncryptedAsync(receiptMsg,
            _opts.SocietyMessagingPrivateKeyEd25519,
            _opts.SocietyMessagingPrivateKeyEd25519,
            DIDCommPackMode.SignThenEncrypt, ct);

        await _processedOrders.StoreReceiptAsync(order.TransferId, packed, ct);
        return packed;
    }

    // ── Incoming TransferReceipt (settlement confirmation) ────────────────────

    public async Task HandleTransferReceiptAsync(
        string packedMessage, CancellationToken ct = default)
    {
        var message = await _didComm.UnpackAsync(
            packedMessage, _opts.SocietyMessagingPrivateKeyEd25519, ct);

        var receipt = JsonSerializer.Deserialize<TransferReceiptCredential>(message.Body)!;

        _log.LogInformation(
            "TransferOrder {TransferId} settled: {Amount} grana credited to {Payee} by {Society}",
            receipt.TransferId, receipt.CreditedGrana, receipt.PayeeDid, receipt.TargetSocietyDid);

        await _driver.AppendToLogAsync("CrossSocietyTransferSettled",
            JsonSerializer.Serialize(receipt), ct);
    }
}

// ── DIDCommMessageProcessorService ───────────────────────────────────────────

/// <summary>
/// Background service that processes the durable DIDComm inbox (IInboxStore / svrn7-inbox.db).
///
/// Each sweep:
///   1. VC expiry sweep (delegates to ISvrn7Driver.ExpireStaleVcsAsync).
///   2. Merkle log auto-sign (delegates to ISvrn7Driver.SignMerkleTreeHeadAsync).
///   3. DIDComm inbox drain: dequeues a batch from IInboxStore, dispatches each
///      message to IDIDCommTransferHandler, marks Processed or Failed.
///
/// Reliability:
///   • On startup, ResetStuckMessagesAsync recovers any messages left in
///     Processing state by an unclean prior shutdown.
///   • Failed messages are retried up to MaxAttempts (default 3) times before
///     being moved to the Failed dead-letter state for operator inspection.
///   • The inbox database (svrn7-inbox.db) is separate from svrn7.db so inbox
///     I/O never contends with wallet or identity operations.
/// </summary>
public sealed class DIDCommMessageProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IInboxStore          _inbox;
    private readonly Svrn7SocietyOptions  _opts;
    private readonly ILogger<DIDCommMessageProcessorService> _log;

    private const int BatchSize   = 20;
    private const int MaxAttempts = 3;

    public DIDCommMessageProcessorService(
        IServiceScopeFactory scopeFactory,
        IInboxStore          inbox,
        IOptions<Svrn7SocietyOptions> opts,
        ILogger<DIDCommMessageProcessorService> log)
    {
        _scopeFactory = scopeFactory;
        _inbox        = inbox;
        _opts         = opts.Value;
        _log          = log;
    }

    /// <summary>
    /// Enqueues an incoming packed DIDComm message into the durable inbox.
    /// Returns immediately; processing happens asynchronously on the next sweep.
    /// </summary>
    public Task EnqueueAsync(string messageType, string packedMessage,
        CancellationToken ct = default)
        => _inbox.EnqueueAsync(messageType, packedMessage, ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("DIDCommMessageProcessorService started.");

        // Recover any messages stuck in Processing from a prior unclean shutdown
        await _inbox.ResetStuckMessagesAsync(stoppingToken);

        using var timer = new PeriodicTimer(_opts.BackgroundSweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunSweepAsync(stoppingToken);
        }
    }

    private async Task RunSweepAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var driver  = scope.ServiceProvider.GetRequiredService<ISvrn7Driver>();
        var handler = scope.ServiceProvider.GetRequiredService<IDIDCommTransferHandler>();

        // 1. VC expiry sweep
        try
        {
            var expired = await driver.ExpireStaleVcsAsync(ct);
            if (expired > 0)
                _log.LogInformation("VC expiry sweep: {Count} credentials expired.", expired);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogError(ex, "VC expiry sweep failed.");
        }

        // 2. Merkle auto-sign
        try
        {
            var head = await driver.SignMerkleTreeHeadAsync(ct);
            _log.LogDebug("Merkle tree head signed. Root: {Root}", head.RootHash);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogError(ex, "Merkle auto-sign failed.");
        }

        // 3. Process inbox batch
        var batch = await _inbox.DequeueBatchAsync(BatchSize, ct);
        if (batch.Count == 0) return;

        _log.LogInformation("DIDComm inbox: processing {Count} message(s).", batch.Count);

        foreach (var message in batch)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessMessageAsync(handler, message, ct);
        }
    }

    private async Task ProcessMessageAsync(
        IDIDCommTransferHandler handler, InboxMessage message, CancellationToken ct)
    {
        try
        {
            switch (message.MessageType)
            {
                case var t when t == Svrn7Constants.Protocols.TransferRequest:
                    await handler.HandleTransferRequestAsync(message.PackedPayload, ct);
                    break;

                case var t when t == Svrn7Constants.Protocols.TransferOrder:
                    await handler.HandleTransferOrderAsync(message.PackedPayload, ct);
                    break;

                case var t when t == Svrn7Constants.Protocols.TransferOrderReceipt:
                    await handler.HandleTransferReceiptAsync(message.PackedPayload, ct);
                    break;

                default:
                    _log.LogWarning("Inbox: unknown message type '{Type}' — marking failed (no retry).",
                        message.MessageType);
                    await _inbox.MarkFailedAsync(
                        message.Id,
                        $"Unrecognised message type: {message.MessageType}",
                        retry: false,
                        maxAttempts: MaxAttempts,
                        ct);
                    return;
            }

            await _inbox.MarkProcessedAsync(message.Id, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogError(ex,
                "Inbox: failed to process message {Id} (type={Type}, attempt={Attempt}).",
                message.Id, message.MessageType, message.AttemptCount + 1);

            await _inbox.MarkFailedAsync(
                message.Id,
                ex.ToString(),
                retry: true,
                maxAttempts: MaxAttempts,
                ct);
        }
    }
}
}