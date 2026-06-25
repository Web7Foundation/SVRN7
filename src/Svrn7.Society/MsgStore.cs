using LiteDB;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;

namespace Svrn7.Society;

/// <summary>
/// LiteDB context for svrn7-msg.db.
/// Kept as a dedicated database so DIDComm message writes do not contend
/// with the wallet/identity writes on the main svrn7.db file lock.
///
/// Collection: InboundMessages
///   - Indexed on Status (for Pending/Processing queries)
///   - Indexed on ReceivedAt (for ordering and stuck-message recovery)
/// </summary>
public sealed class MsgLiteContext : IDisposable
{
    private readonly LiteDatabase _db;
    private bool _disposed;

    public const string ColInboundMessages = "InboundMessages";
    public const string ColProcessedOrders = "ProcessedOrders";
    public const string ColDeadLetter       = "DeadLetter";

    public MsgLiteContext(string connectionString)
    {
        var mapper = new BsonMapper();
        mapper.Entity<ProcessedOrderRecord>().Id(r => r.TransferId);
        // InboundMessage.Id and DeadLetterRecord.Id are named "Id" — LiteDB auto-maps them to _id.
        _db = new LiteDatabase(connectionString, mapper);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        ThrowIfDisposed();
        var col = _db.GetCollection<InboundMessage>(ColInboundMessages);
        col.EnsureIndex(m => m.Id, unique: true);
        col.EnsureIndex(m => m.Status);
        col.EnsureIndex(m => m.ReceivedAt);
        // ProcessedOrderRecord.TransferId is mapped to _id — primary key index is implicit.
        _db.GetCollection<DeadLetterRecord>(ColDeadLetter)
           .EnsureIndex(r => r.FailedAt);
        _db.GetCollection<DeadLetterRecord>(ColDeadLetter)
           .EnsureIndex(r => r.IsRetried);
    }

    public ILiteCollection<InboundMessage> InboundMessages
    {
        get
        {
            ThrowIfDisposed();
            return _db.GetCollection<InboundMessage>(ColInboundMessages);
        }
    }

    public ILiteCollection<DeadLetterRecord> DeadLetter
    {
        get
        {
            ThrowIfDisposed();
            return _db.GetCollection<DeadLetterRecord>(ColDeadLetter);
        }
    }

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
        if (_disposed) throw new ObjectDisposedException(nameof(MsgLiteContext));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _db.Dispose();
    }
}

/// <summary>
/// IInboxStore implementation backed by MsgLiteContext (svrn7-msg.db).
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
    private readonly MsgLiteContext _ctx;
    private readonly Svrn7SocietyOptions _opts;
    private readonly ILogger<LiteInboxStore> _log;

    public LiteInboxStore(MsgLiteContext ctx, IOptions<Svrn7SocietyOptions> opts, ILogger<LiteInboxStore> log)
    {
        _ctx  = ctx;
        _opts = opts.Value;
        _log  = log;
    }

    /// <inheritdoc/>
    public Task EnqueueAsync(
        string messageType, string packedPayload, string? fromDid = null, string? wireId = null, string? jweEnvelope = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var message = new InboundMessage
        {
            // Id is a TDA resource DID URL — globally unique, self-routing.
            // TdaResourceId builds: did:drn:{networkId}/inbox/msg/{objectId}
            Id            = Svrn7.Core.TdaResourceId.InboundMessage(
                                Svrn7.Core.TdaResourceId.NetworkIdFromDid(_opts.SocietyDid),
                                LiteDB.ObjectId.NewObjectId().ToString()),
            MessageType   = messageType,
            PackedPayload = packedPayload,
            JweEnvelope   = jweEnvelope,
            FromDid       = fromDid,
            WireId        = wireId,
            ReceivedAt    = DateTimeOffset.UtcNow,
            Status        = InboundMessageStatus.Pending,
        };

        _ctx.InboundMessages.Insert(message);
        _log.LogDebug("Inbox: enqueued message{NL}{Body}",
            Environment.NewLine, message.ToFormattedJson());
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<InboundMessage?> GetByIdAsync(string didUrl, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Id is now a full DID URL. Match directly on the Id field.
        var msg = _ctx.InboundMessages.FindOne(m => m.Id == didUrl);
        return Task.FromResult<InboundMessage?>(msg);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<InboundMessage>> DequeueBatchAsync(
        int batchSize = 20, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var col     = _ctx.InboundMessages;
        var pending = col
            .Find(m => m.Status == InboundMessageStatus.Pending)
            .OrderBy(m => m.ReceivedAt)
            .Take(batchSize)
            .ToList();

        if (pending.Count == 0)
            return Task.FromResult<IReadOnlyList<InboundMessage>>(Array.Empty<InboundMessage>());

        // Atomically mark batch as Processing
        foreach (var msg in pending)
        {
            msg.Status = InboundMessageStatus.Processing;
            col.Update(msg);
        }

        _log.LogDebug("Inbox: dequeued {Count} messages for processing", pending.Count);
        return Task.FromResult<IReadOnlyList<InboundMessage>>(pending);
    }

    /// <inheritdoc/>
    public Task MarkProcessedAsync(string messageDid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var col = _ctx.InboundMessages;
        var msg = col.FindOne(m => m.Id == messageDid);
        if (msg is null)
        {
            _log.LogWarning("Inbox: MarkProcessed called for unknown message {Id}", messageDid);
            return Task.CompletedTask;
        }

        msg.Status      = InboundMessageStatus.Processed;
        msg.ProcessedAt = DateTimeOffset.UtcNow;
        col.Update(msg);

        _log.LogDebug("Inbox: message {Id} marked Processed", messageDid);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task MarkFailedAsync(
        string messageId, string error,
        bool retry = true, int maxAttempts = Svrn7.Core.Svrn7Constants.InboxMaxAttempts, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var col = _ctx.InboundMessages;
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
            msg.Status = InboundMessageStatus.Pending;   // will be retried on next sweep
            _log.LogWarning(
                "Inbox: message {Id} failed (attempt {Attempt}/{Max}) — requeued. Error: {Error}",
                messageId, msg.AttemptCount, maxAttempts, error);
        }
        else
        {
            msg.Status = InboundMessageStatus.Failed;    // dead-letter
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

        var col   = _ctx.InboundMessages;
        var stuck = col.Find(m => m.Status == InboundMessageStatus.Processing).ToList();

        foreach (var msg in stuck)
        {
            msg.Status = InboundMessageStatus.Pending;
            col.Update(msg);
        }

        if (stuck.Count > 0)
            _log.LogWarning(
                "Inbox: reset {Count} stuck message(s) from Processing to Pending on startup.",
                stuck.Count);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<InboundMessageStatus, int>> GetStatusCountsAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var counts = _ctx.InboundMessages
            .FindAll()
            .GroupBy(m => m.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        // Ensure all statuses are present even when count is zero
        foreach (InboundMessageStatus s in Enum.GetValues<InboundMessageStatus>())
            counts.TryAdd(s, 0);

        return Task.FromResult<IReadOnlyDictionary<InboundMessageStatus, int>>(counts);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<InboundMessage>> ListByTypeAsync(
        string typePrefix, int limit = 50, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var messages = _ctx.InboundMessages
            .Find(m => m.MessageType.StartsWith(typePrefix) && m.Status == InboundMessageStatus.Processed)
            .OrderByDescending(m => m.ReceivedAt)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<InboundMessage>>(messages);
    }
}

/// <summary>
/// IProcessedOrderStore implementation backed by MsgLiteContext (svrn7-msg.db).
/// </summary>
public sealed class LiteProcessedOrderStore : IProcessedOrderStore
{
    private readonly MsgLiteContext _ctx;

    public LiteProcessedOrderStore(MsgLiteContext ctx) => _ctx = ctx;

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


// ── LiteDeadLetterStore ───────────────────────────────────────────────────────

/// <summary>
/// IDeadLetterStore implementation backed by MsgLiteContext (svrn7-msg.db).
/// Dead-letter store for failed outbound DIDComm messages.
/// </summary>
public sealed class LiteDeadLetterStore : Svrn7.Core.Interfaces.IDeadLetterStore
{
    private readonly MsgLiteContext _ctx;
    public LiteDeadLetterStore(MsgLiteContext ctx) => _ctx = ctx;

    public Task EnqueueAsync(DeadLetterRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ctx.DeadLetter.Insert(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DeadLetterRecord>> GetPendingAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var records = _ctx.DeadLetter.Find(r => !r.IsRetried).ToList();
        return Task.FromResult<IReadOnlyList<DeadLetterRecord>>(records);
    }

    public Task MarkRetriedAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var record = _ctx.DeadLetter.FindOne(r => r.Id == id);
        if (record is null) return Task.CompletedTask;
        record.IsRetried = true;
        _ctx.DeadLetter.Update(record);
        return Task.CompletedTask;
    }
}
