using System.Collections.Concurrent;
using System.Management.Automation;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Svrn7.Core;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;

namespace Svrn7.TDA;

// ── DIDCommMessageSwitchboard ─────────────────────────────────────────────────
//
// Derived from: "DIDComm Message Switchboard" (Switchboard element type) — DSA 0.24 Epoch 0.
//
// Design invariants (DSA 0.24 / PPML Derivation Rules):
//
//   1. SOLE INBOX READER: Only the Switchboard reads from IInboxStore.
//      No agent, LOBE, or storage component reads IInboxStore directly.
//
//   2. PASS-BY-REFERENCE: Cmdlets receive a LiteDB ObjectId string (the inbox message
//      reference), not a payload copy. The ObjectId is piped cmdlet-to-cmdlet in
//      the PowerShell pipeline. Payloads are resolved via Svrn7RunspaceContext.GetMessageAsync().
//
//   3. SOLE OUTBOUND SENDER: Only the Switchboard calls HttpClient.PostAsync for
//      outbound DIDComm delivery. Agents post to OutboundQueue; Switchboard delivers.
//
//   4. EPOCH GATING: The Switchboard checks CurrentEpoch before routing. Messages of
//      types restricted to future epochs are rejected with a DIDComm error, not silently
//      dropped.
//
//   5. IDEMPOTENCY: Before routing, the Switchboard checks IProcessedOrderStore.
//      Already-processed messages are marked Processed without re-executing the cmdlet.

/// <summary>
/// Routes inbound DIDComm messages by <c>@type</c> to the appropriate LOBE cmdlet
/// pipeline. Sole reader of <see cref="IInboxStore"/>. Sole caller of outbound
/// HttpClient delivery.
/// Derived from: DIDComm Message Switchboard — DSA 0.24 Epoch 0 (PPML).
/// </summary>
public sealed class DIDCommMessageSwitchboard
{
    private readonly Svrn7RunspaceContext                _ctx;
    private readonly RunspacePoolManager                _pool;
    private readonly IInboxStore                        _inbox;
    private readonly Svrn7.Core.Interfaces.IOutboxStore _outbox;
    private readonly LobeManager                        _lobes;
    private readonly IHttpClientFactory     _httpFactory;
    private readonly TdaOptions             _opts;
    private readonly ILogger<DIDCommMessageSwitchboard> _log;

    // Outbound queue: task LOBEs post packed messages here;
    // the drain loop delivers via HttpClient.
    private readonly ConcurrentQueue<OutboundMessage> _outboundQueue = new();

    private const int BatchSize                    = 20;
    private const int TransactionalMaxAttempts    = Svrn7.Core.Svrn7Constants.InboxMaxAttempts;
    private const int NonTransactionalMaxAttempts = Svrn7.Core.Svrn7Constants.InboxNonTransactionalMaxAttempts;
    private const int IdleMs                      = 100;
    private const int StoreBackoffMs              = 2_000; // backoff when inbox store throws
    private const int OutboundMaxAttempts         = 3;      // retries for outbound HTTP delivery

    private static bool IsTransactional(string messageType)
        => Svrn7.Core.Svrn7Constants.TransactionalProtocols.Contains(messageType);

    public DIDCommMessageSwitchboard(
        Svrn7RunspaceContext                ctx,
        RunspacePoolManager                 pool,
        IInboxStore                         inbox,
        Svrn7.Core.Interfaces.IOutboxStore  outbox,
        LobeManager                         lobes,
        IHttpClientFactory                  httpFactory,
        Microsoft.Extensions.Options.IOptions<TdaOptions> opts,
        ILogger<DIDCommMessageSwitchboard>  log)
    {
        _ctx         = ctx;
        _pool        = pool;
        _inbox       = inbox;
        _outbox      = outbox;
        _lobes       = lobes;
        _httpFactory = httpFactory;
        _opts        = opts.Value;
        _log         = log;
    }

    // ── Startup recovery ──────────────────────────────────────────────────────

    /// <summary>
    /// Called once by <see cref="SwitchboardHostedService"/> before the drain loop starts.
    /// Recovers two classes of state left behind by an unclean prior shutdown:
    ///
    ///   1. Stuck inbox messages — any message left in <c>Processing</c> status (the TDA
    ///      was killed mid-dispatch) is reset to <c>Pending</c> so it will be dequeued again.
    ///
    ///   2. Dead-letter outbox — any outbound message that exhausted all delivery attempts
    ///      in the previous session is re-enqueued into the outbound queue for another round
    ///      of retries. The operator can inspect <c>svrn7-inbox.db</c> for persistent failures.
    /// </summary>
    public async Task StartupAsync(CancellationToken ct)
    {
        // 1. Reset stuck inbox messages.
        try
        {
            await _inbox.ResetStuckMessagesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Switchboard: failed to reset stuck inbox messages on startup — " +
                "some messages may remain stuck in Processing.");
        }

        // 2. Re-enqueue dead-lettered outbound messages from the previous session.
        try
        {
            var pending = await _outbox.GetPendingAsync(ct);
            if (pending.Count > 0)
            {
                _log.LogInformation(
                    "Switchboard: re-enqueuing {Count} dead-lettered outbound message(s) from prior session.",
                    pending.Count);
                foreach (var record in pending)
                    _outboundQueue.Enqueue(new OutboundMessage(record.PeerEndpoint, record.PackedMessage));
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Switchboard: failed to read dead-letter outbox on startup — " +
                "previously failed outbound messages will not be retried this session.");
        }
    }

    // ── Main drain loop (runs in Agent 1 Runspace via SwitchboardHostedService) ─

    /// <summary>
    /// Continuous drain loop. Called by <see cref="SwitchboardHostedService"/>.
    /// Blocks the calling thread; use with a dedicated long-running task.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        _log.LogInformation("DIDCommMessageSwitchboard: drain loop started.");

        while (!ct.IsCancellationRequested)
        {
            // ── 1. Drain outbound queue ───────────────────────────────────────
            await DrainOutboundAsync(ct);

            // ── 2. Dequeue and dispatch inbound batch ─────────────────────────
            // Guarded by a top-level try/catch so a transient inbox store error
            // (LiteDB lock, disk full, etc.) backs off and retries rather than
            // crashing the drain loop and taking the TDA down.
            try
            {
                var batch = await _inbox.DequeueBatchAsync(BatchSize, ct);
                if (batch.Count == 0)
                {
                    await Task.Delay(IdleMs, ct);
                    continue;
                }

                _log.LogInformation("Switchboard: processing {Count} inbound message(s).", batch.Count);

                // Messages are dispatched sequentially — the Switchboard cannot know whether
                // two messages in a batch target the same society or credential. Sequential
                // processing is the only safe default for a transactional financial system.
                foreach (var msg in batch)
                {
                    if (ct.IsCancellationRequested) break;
                    await DispatchAsync(msg, ct);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogError(ex,
                    "Switchboard: inbox store error — backing off {Backoff}ms before retry.",
                    StoreBackoffMs);
                await Task.Delay(StoreBackoffMs, ct);
            }
        }

        _log.LogInformation("DIDCommMessageSwitchboard: drain loop stopped.");
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    private async Task DispatchAsync(InboxMessage msg, CancellationToken ct)
    {
        try
        {
            // ── Message TTL ───────────────────────────────────────────────────
            // Reject messages older than MaxMessageAgeSeconds. A stale financial
            // message from a crashed peer session must never execute.
            if (_opts.MaxMessageAgeSeconds > 0 &&
                (DateTimeOffset.UtcNow - msg.ReceivedAt).TotalSeconds > _opts.MaxMessageAgeSeconds)
            {
                var ageSecs = (DateTimeOffset.UtcNow - msg.ReceivedAt).TotalSeconds;
                _log.LogWarning(
                    "Switchboard: message {Id} (type={Type}) is {Age:F0}s old — " +
                    "exceeds MaxMessageAgeSeconds ({Max}). Dead-lettering.",
                    msg.Id, msg.MessageType, ageSecs, _opts.MaxMessageAgeSeconds);
                await _inbox.MarkFailedAsync(
                    msg.Id,
                    $"Message expired: age {ageSecs:F0}s exceeds limit of {_opts.MaxMessageAgeSeconds}s.",
                    retry: false, maxAttempts: TransactionalMaxAttempts, ct);
                return;
            }

            // ── Epoch gate ────────────────────────────────────────────────────
            if (!IsPermittedInEpoch(msg.MessageType, _ctx.CurrentEpoch))
            {
                _log.LogWarning(
                    "Switchboard: message type '{Type}' not permitted in Epoch {Epoch}. Rejecting.",
                    msg.MessageType, _ctx.CurrentEpoch);
                await _inbox.MarkFailedAsync(
                    msg.Id,
                    $"Epoch {_ctx.CurrentEpoch} does not permit {msg.MessageType}",
                    retry: false, maxAttempts: TransactionalMaxAttempts, ct);
                return;
            }

            // ── Idempotency ───────────────────────────────────────────────────
            // For cross-Society transfer orders, check IProcessedOrderStore.
            if (msg.MessageType == Svrn7Constants.Protocols.TransferOrder)
            {
                var existing = await _ctx.ProcessedOrders.GetReceiptAsync(msg.Id, ct);
                if (existing is not null)
                {
                    _log.LogDebug("Switchboard: message {Id} already processed (idempotency hit).", msg.Id);
                    await _inbox.MarkProcessedAsync(msg.Id, ct);
                    return;
                }
            }

            // ── Route by @type → LOBE cmdlet pipeline ─────────────────────────
            // Pass-by-reference: pass the ObjectId string, not the payload.
            // Dynamic registry lookup — LobeManager resolves @type to a registration.
            // Exact match is preferred over prefix match (longest-prefix tiebreak).
            var reg = _lobes.TryResolveProtocol(msg.MessageType);
            if (reg is null)
            {
                _log.LogWarning(
                    "Switchboard: no LOBE registered for @type '{Type}' — failing message.",
                    msg.MessageType);
                await _inbox.MarkFailedAsync(
                    msg.Id,
                    $"No LOBE registered for @type: {msg.MessageType}",
                    retry: false, maxAttempts: TransactionalMaxAttempts, ct);
                return;
            }

            // msg.Id is already a TDA resource DID URL — pass directly.
            _log.LogInformation(
                "Switchboard: routing {Did} (type={Type}) → {EP} [{LOBE}]",
                msg.Id, msg.MessageType, reg.Entrypoint, reg.LobeName);

            await InvokeCmdletPipelineAsync(reg.Entrypoint, reg.ModulePath, msg.Id, ct);
            await _inbox.MarkProcessedAsync(msg.Id, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogError(ex,
                "Switchboard: dispatch failed for message {Id} (attempt {Attempt}, transactional={T}).",
                msg.Id, msg.AttemptCount + 1, IsTransactional(msg.MessageType));
            try
            {
                // Transactional messages: never retry — dead-letter immediately.
                // Non-transactional messages: retry up to NonTransactionalMaxAttempts.
                var transactional = IsTransactional(msg.MessageType);
                await _inbox.MarkFailedAsync(
                    msg.Id, ex.ToString(),
                    retry: !transactional,
                    maxAttempts: transactional ? TransactionalMaxAttempts : NonTransactionalMaxAttempts,
                    ct);
            }
            catch (Exception markEx)
            {
                // Dead-lettering is best-effort. If the store is unavailable the
                // message stays in Processing and will be recovered by
                // ResetStuckMessagesAsync on next startup.
                _log.LogError(markEx,
                    "Switchboard: could not dead-letter message {Id} — store unavailable.",
                    msg.Id);
            }
        }
    }

    // ── Cmdlet pipeline invocation ────────────────────────────────────────────

    /// <summary>
    /// Creates a dedicated isolated <see cref="Runspace"/>, optionally imports a
    /// JIT LOBE module into it, then invokes the cmdlet or agent script pipeline.
    /// The runspace is disposed on return — a fault here cannot affect any other
    /// concurrent dispatch.
    ///
    /// Pipeline pattern (PowerShell, pass-by-reference):
    ///   Get-Web7Message -Did $did | Invoke-{Lobe}Cmdlet -MessageDid $did
    /// </summary>
    private async Task InvokeCmdletPipelineAsync(
        string cmdletOrScript, string modulePath, string didUrl, CancellationToken ct)
    {
        using var isolated = _pool.CreateIsolatedPipeline();
        var ps = isolated.Ps;

        // For LOBE cmdlets (not agent .ps1 scripts), ensure the module is present
        // in this runspace. Eager LOBEs are skipped (already in the ISS).
        // JIT LOBEs are imported now into this dedicated runspace.
        if (!cmdletOrScript.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            await _lobes.EnsureLoadedAsync(ps, modulePath, ct);

        ps.Commands.Clear();
        if (cmdletOrScript.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            // Agent script: executed with MessageDid parameter.
            ps.AddCommand(cmdletOrScript)
              .AddParameter("MessageDid", didUrl);
        }
        else
        {
            // LOBE cmdlet pipeline: Get-Web7Message | cmdlet (pass-by-reference).
            ps.AddCommand("Get-Web7Message")
              .AddParameter("Did", didUrl)
              .AddStatement()
              .AddCommand(cmdletOrScript)
              .AddParameter("MessageDid", didUrl);
        }

        _log.LogTrace("PS invoke: {Cmdlet} -MessageDid {Did}", cmdletOrScript, didUrl);

        // ps.Invoke() is synchronous. Wrap in Task.Run so it doesn't block the thread pool.
        // PowerShell does not honour CancellationToken internally — interruption is via
        // ps.Stop(). Apply an external timeout using WaitAsync + a linked CTS.
        var invokeTask = Task.Run(() => ps.Invoke());

        if (_opts.LobeInvocationTimeoutSeconds > 0)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_opts.LobeInvocationTimeoutSeconds));
            try
            {
                await invokeTask.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout fired (not shutdown) — stop the runspace and fail the message.
                ps.Stop();
                try { await invokeTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
                catch { /* best-effort wind-down; runspace disposed by IsolatedPipeline */ }
                throw new TimeoutException(
                    $"LOBE cmdlet '{cmdletOrScript}' timed out after " +
                    $"{_opts.LobeInvocationTimeoutSeconds}s for message {didUrl}.");
            }
        }
        else
        {
            await invokeTask.WaitAsync(ct);
        }

        var results = invokeTask.Result; // task is completed at this point

        _log.LogTrace("PS complete: {Cmdlet} → {Count} result(s).", cmdletOrScript, results.Count);

        // Forward PowerShell streams to the .NET logger.
        foreach (var v in ps.Streams.Verbose)
            _log.LogTrace("  [PS Verbose] {Message}", v.Message);
        foreach (var d in ps.Streams.Debug)
            _log.LogDebug("  [PS Debug] {Message}", d.Message);
        foreach (var i in ps.Streams.Information)
            _log.LogInformation("  [PS Info] {Message}", i.MessageData);
        foreach (var w in ps.Streams.Warning)
            _log.LogWarning("  [PS Warning] {Message}", w.Message);

        if (ps.HadErrors)
        {
            var errors = string.Join("; ", ps.Streams.Error.Select(e => e.ToString()));
            throw new InvalidOperationException(
                $"'{cmdletOrScript}' reported errors for message {didUrl}: {errors}");
        }

        // Enqueue any outbound messages returned by the pipeline.
        foreach (var result in results)
        {
            if (result?.BaseObject is OutboundMessage outbound)
                _outboundQueue.Enqueue(outbound);
        }
    }

    // ── Epoch gate ────────────────────────────────────────────────────────────

    private bool IsPermittedInEpoch(string messageType, int epoch)
    {
        // Option A: SVRN7 transfer order requires Epoch 1+ (protocol-level special case).
        // This is the only hardcoded epoch constraint in the Switchboard.
        // All other epoch requirements are declared in .lobe.json descriptors
        // and enforced by LobeManager.RegisterFromDescriptor at load time.
        if (messageType == Svrn7Constants.Protocols.TransferOrder ||
            messageType == Svrn7Constants.Protocols.TransferOrderReceipt)
            return epoch >= Svrn7Constants.Epochs.EcosystemUtility;

        // For all other types, check the registration's epochRequired field.
        var reg = _lobes.TryResolveProtocol(messageType);
        if (reg is not null)
            return epoch >= reg.EpochRequired;

        return true; // unknown type — epoch check deferred to routing failure
    }

    // ── Outbound delivery ─────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues a packed outbound DIDComm message for delivery.
    /// Called by LOBE cmdlets via the <c>Send-Web7Message</c> cmdlet wrapper,
    /// which posts an <see cref="OutboundMessage"/> to this queue.
    /// </summary>
    public void EnqueueOutbound(string peerEndpoint, string packedMessage)
        => _outboundQueue.Enqueue(new OutboundMessage(peerEndpoint, packedMessage));

    /// <summary>
    /// Number of outbound messages waiting in the in-memory delivery queue.
    /// Exposed for diagnostics and testing.
    /// </summary>
    public int PendingOutboundCount => _outboundQueue.Count;

    private async Task DrainOutboundAsync(CancellationToken ct)
    {
        // Drain the outbound queue — deliver each packed DIDComm message to the
        // peer TDA's POST /didcomm endpoint via HTTP/2.
        // Derived from: "HTTP Listener/Sender (HTTPClient)" — DSA 0.24 Epoch 0.
        while (_outboundQueue.TryDequeue(out var msg))
        {
            await DeliverOutboundAsync(msg, ct);
        }
    }

    private async Task DeliverOutboundAsync(OutboundMessage msg, CancellationToken ct)
    {
        var client   = _httpFactory.CreateClient("didcomm");
        var endpoint = msg.PeerEndpoint.TrimEnd('/') + "/didcomm";

        Exception? lastException = null;

        for (int attempt = 1; attempt <= OutboundMaxAttempts; attempt++)
        {
            try
            {
                using var content = new System.Net.Http.StringContent(
                    msg.PackedMessage,
                    System.Text.Encoding.UTF8,
                    "application/didcomm-encrypted+json");

                var response = await client.PostAsync(endpoint, content, ct);

                if (response.IsSuccessStatusCode)
                {
                    _log.LogInformation(
                        "Switchboard: outbound delivered to {Endpoint} ({Status}).",
                        endpoint, (int)response.StatusCode);
                    return; // success
                }

                // Non-success HTTP status — treat as a retryable failure.
                lastException = new HttpRequestException(
                    $"Peer returned HTTP {(int)response.StatusCode}");
                _log.LogWarning(
                    "Switchboard: peer returned {Status} (attempt {Attempt}/{Max}) for {Endpoint}.",
                    (int)response.StatusCode, attempt, OutboundMaxAttempts, endpoint);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                lastException = ex;
                _log.LogWarning(ex,
                    "Switchboard: outbound delivery failed (attempt {Attempt}/{Max}) to {Endpoint}.",
                    attempt, OutboundMaxAttempts, endpoint);
            }

            // Exponential backoff: 500 ms, 1 000 ms, 2 000 ms, …
            if (attempt < OutboundMaxAttempts)
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (1 << (attempt - 1))), ct);
        }

        // All attempts exhausted — dead-letter for operator inspection.
        _log.LogError(lastException,
            "Switchboard: outbound delivery to {Endpoint} failed after {Max} attempt(s). " +
            "Persisting to dead-letter outbox.", endpoint, OutboundMaxAttempts);

        var networkId = Svrn7.Core.TdaResourceId.NetworkIdFromDid(_opts.SocietyDid);
        var outboxId  = Svrn7.Core.TdaResourceId.Build(
            networkId, "inbox", "outbox", LiteDB.ObjectId.NewObjectId().ToString());

        await _outbox.EnqueueAsync(new Svrn7.Core.Models.OutboxRecord
        {
            Id            = outboxId,
            PeerEndpoint  = msg.PeerEndpoint,
            PackedMessage = msg.PackedMessage,
            MessageType   = "outbound",
            AttemptCount  = OutboundMaxAttempts,
            LastError     = lastException?.Message
        }, ct);
    }
}

// ── OutboundMessage ───────────────────────────────────────────────────────────

/// <summary>
/// Outbound DIDComm message waiting for delivery by the Switchboard.
/// LOBE cmdlets return this from the pipeline; the Switchboard delivers via HttpClient.
/// </summary>
public sealed record OutboundMessage(string PeerEndpoint, string PackedMessage);
