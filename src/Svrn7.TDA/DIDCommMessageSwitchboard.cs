using System.Collections.Concurrent;
using System.Management.Automation;
using Microsoft.Extensions.Logging;
using Svrn7.Core;
using Svrn7.Core.Interfaces;

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

    private const int BatchSize    = 20;
    private const int MaxAttempts  = 3;
    private const int IdleMs       = 100;

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

            // ── 2. Dequeue inbound batch ──────────────────────────────────────
            var batch = await _inbox.DequeueBatchAsync(BatchSize, ct);
            if (batch.Count == 0)
            {
                await Task.Delay(IdleMs, ct);
                continue;
            }

            _log.LogInformation("Switchboard: processing {Count} inbound message(s).", batch.Count);

            foreach (var msg in batch)
            {
                if (ct.IsCancellationRequested) break;
                await DispatchAsync(msg, ct);
            }
        }

        _log.LogInformation("DIDCommMessageSwitchboard: drain loop stopped.");
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    private async Task DispatchAsync(InboxMessage msg, CancellationToken ct)
    {
        try
        {
            // ── Epoch gate ────────────────────────────────────────────────────
            if (!IsPermittedInEpoch(msg.MessageType, _ctx.CurrentEpoch))
            {
                _log.LogWarning(
                    "Switchboard: message type '{Type}' not permitted in Epoch {Epoch}. Rejecting.",
                    msg.MessageType, _ctx.CurrentEpoch);
                await _inbox.MarkFailedAsync(
                    msg.Id,
                    $"Epoch {_ctx.CurrentEpoch} does not permit {msg.MessageType}",
                    retry: false, maxAttempts: MaxAttempts, ct);
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
                    retry: false, maxAttempts: MaxAttempts, ct);
                return;
            }

            // msg.Id is already a TDA resource DID URL — pass directly.
            _log.LogInformation(
                "Switchboard: routing {Did} (type={Type}) → {EP} [{LOBE}]",
                msg.Id, msg.MessageType, reg.Entrypoint, reg.LobeName);

            // Ensure the LOBE module is loaded before invoking the cmdlet.
            using var ensurePs = _pool.CreatePipelineForPool();
            await _lobes.EnsureLoadedAsync(ensurePs, reg.ModulePath, ct);

            await InvokeCmdletPipelineAsync(reg.Entrypoint, msg.Id, ct);
            await _inbox.MarkProcessedAsync(msg.Id, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogError(ex,
                "Switchboard: dispatch failed for message {Id} (attempt {Attempt}).",
                msg.Id, msg.AttemptCount + 1);
            await _inbox.MarkFailedAsync(
                msg.Id, ex.ToString(),
                retry: true, maxAttempts: MaxAttempts, ct);
        }
    }

    // ── Cmdlet pipeline invocation ────────────────────────────────────────────

    /// <summary>
    /// Opens a runspace from the pool and invokes the LOBE cmdlet pipeline,
    /// passing the LiteDB ObjectId as the -MessageId parameter.
    ///
    /// Pipeline pattern (PowerShell, pass-by-reference):
    ///   Get-TdaMessage -Id $msgId | Invoke-{Lobe}Cmdlet | Send-TdaMessage
    /// </summary>
    private async Task InvokeCmdletPipelineAsync(
        string cmdletOrScript, string didUrl, CancellationToken ct)
    {
        using var ps = _pool.CreatePipelineForPool();

        if (cmdletOrScript.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            // Agent script: executed with MessageDid parameter.
            ps.AddCommand(cmdletOrScript)
              .AddParameter("MessageDid", didUrl);
        }
        else
        {
            // LOBE cmdlet pipeline: Get-TdaMessage | cmdlet (pass-by-reference).
            ps.AddCommand("Get-TdaMessage")
              .AddParameter("Did", didUrl)
              .AddStatement()
              .AddCommand(cmdletOrScript)
              .AddParameter("MessageDid", didUrl);
        }

        var results = await Task.Run(() => ps.Invoke(), ct);

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
    /// Called by LOBE cmdlets via the <c>Send-TdaMessage</c> cmdlet wrapper,
    /// which posts an <see cref="OutboundMessage"/> to this queue.
    /// </summary>
    public void EnqueueOutbound(string peerEndpoint, string packedMessage)
        => _outboundQueue.Enqueue(new OutboundMessage(peerEndpoint, packedMessage));

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
        // Uses the named HttpClient "didcomm" registered in TdaServiceCollectionExtensions.
        // Polly retry: exponential backoff, 3 attempts — configured at registration time.
        var client  = _httpFactory.CreateClient("didcomm");
        var endpoint = msg.PeerEndpoint.TrimEnd('/') + "/didcomm";

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
                    "Switchboard: outbound message delivered to {Endpoint} ({Status}).",
                    endpoint, (int)response.StatusCode);
            }
            else
            {
                _log.LogWarning(
                    "Switchboard: peer TDA returned {Status} for outbound message to {Endpoint}.",
                    (int)response.StatusCode, endpoint);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Dead-letter: persist the failed message to the outbox for operator inspection.
            _log.LogError(ex,
                "Switchboard: outbound delivery failed to {Endpoint} after retries. "
                "Persisting to dead-letter outbox.", endpoint);

            var networkId = Svrn7.Core.TdaResourceId.NetworkIdFromDid(_opts.SocietyDid);
            var outboxId  = Svrn7.Core.TdaResourceId.Build(
                networkId, "inbox", "outbox", LiteDB.ObjectId.NewObjectId().ToString());

            await _outbox.EnqueueAsync(new Svrn7.Core.Models.OutboxRecord
            {
                Id           = outboxId,
                PeerEndpoint = msg.PeerEndpoint,
                PackedMessage= msg.PackedMessage,
                MessageType  = "outbound",
                AttemptCount = 3,
                LastError    = ex.Message
            }, ct);
        }
    }
}

// ── OutboundMessage ───────────────────────────────────────────────────────────

/// <summary>
/// Outbound DIDComm message waiting for delivery by the Switchboard.
/// LOBE cmdlets return this from the pipeline; the Switchboard delivers via HttpClient.
/// </summary>
public sealed record OutboundMessage(string PeerEndpoint, string PackedMessage);
