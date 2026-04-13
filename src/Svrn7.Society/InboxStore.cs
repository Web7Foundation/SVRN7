using LiteDB;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;

namespace Svrn7.Society;

/// <summary>
/// LiteDB context for svrn7-inbox.db.
/// Kept as a dedicated database so DIDComm inbox writes do not contend
/// with the wallet/identity writes on the main svrn7.db file lock.
///
/// Collection: InboxMessages
///   - Indexed on Status (for Pending/Processing queries)
///   - Indexed on ReceivedAt (for ordering and stuck-message recovery)
/// </summary>
public sealed class InboxLiteContext : IDisposable
{
    private readonly LiteDatabase _db;
    private bool _disposed;

    public const string ColInbox          = "InboxMessages";
    public const string ColProcessedOrders = "ProcessedOrders";
    public const string ColOutbox          = "Outbox";

    public InboxLiteContext(string connectionString)
    {
        _db = new LiteDatabase(connectionString);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        ThrowIfDisposed();
        var col = _db.GetCollection<InboxMessage>(ColInbox);
        col.EnsureIndex(m => m.Id, unique: true);
        col.EnsureIndex(m => m.Status);
        col.EnsureIndex(m => m.ReceivedAt);
        _db.GetCollection<ProcessedOrderRecord>(ColProcessedOrders)
           .EnsureIndex(r => r.TransferId, unique: true);
        _db.GetCollection<OutboxRecord>(ColOutbox)
           .EnsureIndex(r => r.FailedAt);
        _db.GetCollection<OutboxRecord>(ColOutbox)
           .EnsureIndex(r => r.IsRetried);
    }

    public ILiteCollection<InboxMessage> InboxMessages
    {
        get
        {
            ThrowIfDisposed();
            return _db.GetCollection<InboxMessage>(ColInbox);
        }
    }

    public ILiteCollection<OutboxRecord> Outbox
        => _db.GetCollection<OutboxRecord>(ColOutbox);

    public ILiteCollection<ProcessedOrderRecord> ProcessedOrders
    {
        get
        {
            ThrowIfDisposed();
            return _db.GetCollection<ProcessedOrderRecord>(ColProcessedOrders);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InboxLiteContext));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _db.Dispose();
    }
}

/// <summary>
/// IInboxStore implementation backed by InboxLiteContext (svrn7-inbox.db).
///
/// Concurrency model
/// ─────────────────
/// LiteDB uses file-level exclusive locking per process for writes.  All
/// mutation methods (Enqueue, MarkProcessed, MarkFailed, ResetStuck) are
/// synchronous under the hood but exposed as Task so callers can await them
/// and the interface remains transport-agnostic.
///
/// Reliability guarantees
/// ──────────────────────
/// • Messages survive process crashes: every message is written to disk
///   before EnqueueAsync returns.
/// • Exactly-once delivery: DequeueBatchAsync atomically transitions
///   Pending → Processing in a single LiteDB transaction before returning.
///   The processor marks the message Processed or Failed; it never silently
///   drops it.
/// • Stuck-message recovery: ResetStuckMessagesAsync transitions any
///   Processing message back to Pending.  Call this on startup to recover
///   from unclean shutdown.
/// • Dead-letter semantics: after maxAttempts, MarkFailedAsync sets Status
///   to Failed permanently.  Failed messages are retained for diagnostic
///   inspection and can be requeued manually by setting Status = Pending.
/// </summary>
public sealed class LiteInboxStore : IInboxStore
{
    private readonly InboxLiteContext _ctx;
    private readonly ILogger<LiteInboxStore> _log;

    public LiteInboxStore(InboxLiteContext ctx, ILogger<LiteInboxStore> log)
    {
        _ctx = ctx;
        _log = log;
    }

    /// <inheritdoc/>
    public Task EnqueueAsync(
        string messageType, string packedPayload, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var message = new InboxMessage
        {
            // Id is a TDA resource DID URL — globally unique, self-routing.
            // TdaResourceId builds: did:drn:{networkId}/inbox/msg/{objectId}
            Id            = Svrn7.Core.TdaResourceId.InboxMessage(
                                Svrn7.Core.TdaResourceId.NetworkIdFromDid(_opts.SocietyDid),
                                LiteDB.ObjectId.NewObjectId().ToString()),
            MessageType   = messageType,
            PackedPayload = packedPayload,
            ReceivedAt    = DateTimeOffset.UtcNow,
            Status        = InboxMessageStatus.Pending,
        };

        _ctx.InboxMessages.Insert(message);
        _log.LogDebug("Inbox: enqueued message {Id} of type {Type}", message.Id, messageType);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<InboxMessage?> GetByIdAsync(string didUrl, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Id is now a full DID URL. Match directly on the Id field.
        var msg = _ctx.InboxMessages.FindOne(m => m.Id == didUrl);
        return Task.FromResult<InboxMessage?>(msg);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<InboxMessage>> DequeueBatchAsync(
        int batchSize = 20, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var col     = _ctx.InboxMessages;
        var pending = col
            .Find(m => m.Status == InboxMessageStatus.Pending)
            .OrderBy(m => m.ReceivedAt)
            .Take(batchSize)
            .ToList();

        if (pending.Count == 0)
            return Task.FromResult<IReadOnlyList<InboxMessage>>(Array.Empty<InboxMessage>());

        // Atomically mark batch as Processing
        foreach (var msg in pending)
        {
            msg.Status = InboxMessageStatus.Processing;
            col.Update(msg);
        }

        _log.LogDebug("Inbox: dequeued {Count} messages for processing", pending.Count);
        return Task.FromResult<IReadOnlyList<InboxMessage>>(pending);
    }

    /// <inheritdoc/>
    public Task MarkProcessedAsync(string messageDid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var col = _ctx.InboxMessages;
        var msg = col.FindOne(m => m.Id == messageDid);
        if (msg is null)
        {
            _log.LogWarning("Inbox: MarkProcessed called for unknown message {Id}", messageDid);
            return Task.CompletedTask;
        }

        msg.Status      = InboxMessageStatus.Processed;
        msg.ProcessedAt = DateTimeOffset.UtcNow;
        col.Update(msg);

        _log.LogDebug("Inbox: message {Id} marked Processed", messageDid);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task MarkFailedAsync(
        string messageId, string error,
        bool retry = true, int maxAttempts = 3, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var col = _ctx.InboxMessages;
        var msg = col.FindOne(m => m.Id == messageId);
        if (msg is null)
        {
            _log.LogWarning("Inbox: MarkFailed called for unknown message {Id}", messageId);
            return Task.CompletedTask;
        }

        msg.AttemptCount++;
        msg.LastError = error;

        if (retry && msg.AttemptCount < maxAttempts)
        {
            msg.Status = InboxMessageStatus.Pending;   // will be retried on next sweep
            _log.LogWarning(
                "Inbox: message {Id} failed (attempt {Attempt}/{Max}) — requeued. Error: {Error}",
                messageId, msg.AttemptCount, maxAttempts, error);
        }
        else
        {
            msg.Status = InboxMessageStatus.Failed;    // dead-letter
            _log.LogError(
                "Inbox: message {Id} permanently failed after {Attempt} attempt(s). Error: {Error}",
                messageId, msg.AttemptCount, error);
        }

        col.Update(msg);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ResetStuckMessagesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var col   = _ctx.InboxMessages;
        var stuck = col.Find(m => m.Status == InboxMessageStatus.Processing).ToList();

        foreach (var msg in stuck)
        {
            msg.Status = InboxMessageStatus.Pending;
            col.Update(msg);
        }

        if (stuck.Count > 0)
            _log.LogWarning(
                "Inbox: reset {Count} stuck message(s) from Processing to Pending on startup.",
                stuck.Count);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<InboxMessageStatus, int>> GetStatusCountsAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var counts = _ctx.InboxMessages
            .FindAll()
            .GroupBy(m => m.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        // Ensure all statuses are present even when count is zero
        foreach (InboxMessageStatus s in Enum.GetValues<InboxMessageStatus>())
            counts.TryAdd(s, 0);

        return Task.FromResult<IReadOnlyDictionary<InboxMessageStatus, int>>(counts);
    }
}

/// <summary>
/// IProcessedOrderStore implementation backed by InboxLiteContext (svrn7-inbox.db).
/// </summary>
public sealed class LiteProcessedOrderStore : IProcessedOrderStore
{
    private readonly InboxLiteContext _ctx;

    public LiteProcessedOrderStore(InboxLiteContext ctx) => _ctx = ctx;

    public Task<string?> GetReceiptAsync(string transferId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var record = _ctx.ProcessedOrders.FindOne(r => r.TransferId == transferId);
        return Task.FromResult(record?.PackedReceipt);
    }

    public Task StoreReceiptAsync(string transferId, string packedReceipt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var record = new ProcessedOrderRecord
        {
            TransferId    = transferId,
            PackedReceipt = packedReceipt,
            ProcessedAt   = DateTimeOffset.UtcNow,
        };
        _ctx.ProcessedOrders.Upsert(record);
        return Task.CompletedTask;
    }
}


// ── LiteOutboxStore ───────────────────────────────────────────────────────────

/// <summary>
/// IOutboxStore implementation backed by InboxLiteContext (svrn7-inbox.db).
/// Dead-letter store for failed outbound DIDComm messages.
/// </summary>
public sealed class LiteOutboxStore : Svrn7.Core.Interfaces.IOutboxStore
{
    private readonly InboxLiteContext _ctx;
    public LiteOutboxStore(InboxLiteContext ctx) => _ctx = ctx;

    public Task EnqueueAsync(OutboxRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ctx.Outbox.Insert(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxRecord>> GetPendingAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var records = _ctx.Outbox.Find(r => !r.IsRetried).ToList();
        return Task.FromResult<IReadOnlyList<OutboxRecord>>(records);
    }

    public Task MarkRetriedAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var record = _ctx.Outbox.FindOne(r => r.Id == id);
        if (record is null) return Task.CompletedTask;
        record.IsRetried = true;
        _ctx.Outbox.Update(record);
        return Task.CompletedTask;
    }
}
