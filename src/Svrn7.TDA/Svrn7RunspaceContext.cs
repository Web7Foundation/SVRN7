using Microsoft.Extensions.Caching.Memory;
using Svrn7.Core.Interfaces;
using Svrn7.Society;

namespace Svrn7.TDA;

// ── Svrn7RunspaceContext ──────────────────────────────────────────────────────
//
// Derived from: "$SVRN7 session variable" — DSA 0.24 Epoch 0 (PPML).
//
// This object is injected into every PowerShell runspace in the pool as the
// session variable $SVRN7. It gives all LOBE cmdlets direct in-process access
// to the SVRN7 driver stack, inbox store, memory cache, processed orders store,
// and the current epoch value — without any message-passing or service locator.
//
// Design rule (PP-4 / DSA 0.24):
//   SVRN7 is a LOBE, not an agent. ISvrn7SocietyDriver is a cognitive capability
//   available to all runspaces via $SVRN7.Driver. Agents never resolve ISvrn7
//   SocietyDriver through the DI container directly.
//
// Pass-by-reference rule (DSA 0.24):
//   Cmdlets receive a LiteDB ObjectId string (the inbox message reference), not a
//   payload copy. They call $SVRN7.GetMessageAsync(id) to resolve the payload via
//   IMemoryCache (hot path) or IInboxStore (cold path). This class owns that
//   resolution logic, keeping it consistent across all LOBE cmdlets.

/// <summary>
/// Shared runtime context injected into every PowerShell runspace as <c>$SVRN7</c>.
/// Provides in-process access to the full SVRN7 driver stack, the durable inbox,
/// the memory cache, and the current epoch value.
/// </summary>
public sealed class Svrn7RunspaceContext
{
    private readonly IInboxStore         _inbox;
    private readonly IMemoryCache        _cache;
    private readonly IProcessedOrderStore _processedOrders;
    private volatile int                 _currentEpoch;

    // ── Public surface (accessible as $SVRN7.* in PowerShell) ────────────────

    /// <summary>
    /// The Society-level SVRN7 driver. All monetary and identity operations.
    /// Derived from: "SVRN7 LOBE" inside Agent 1 Runspace — DSA 0.24 Epoch 0.
    /// </summary>
    public ISvrn7SocietyDriver Driver { get; }

    /// <summary>
    /// The current epoch value. Refreshed every 60 seconds by
    /// <see cref="RunspacePoolManager"/>. Cmdlets read this for epoch gating.
    /// </summary>
    public int CurrentEpoch => _currentEpoch;

    // ── Internal surface (used by Switchboard, not directly by cmdlets) ───────

    internal IInboxStore         Inbox           => _inbox;
    internal IMemoryCache        Cache           => _cache;
    internal IProcessedOrderStore ProcessedOrders => _processedOrders;

    public Svrn7RunspaceContext(
        ISvrn7SocietyDriver  driver,
        IInboxStore          inbox,
        IMemoryCache         cache,
        IProcessedOrderStore processedOrders,
        int                  initialEpoch = 0)
    {
        Driver           = driver;
        _inbox           = inbox;
        _cache           = cache;
        _processedOrders = processedOrders;
        _currentEpoch    = initialEpoch;
    }

    /// <summary>
    /// Refreshes the epoch value. Called by <see cref="RunspacePoolManager"/>
    /// on a 60-second timer. Thread-safe via volatile write.
    /// </summary>
    internal void SetEpoch(int epoch) => _currentEpoch = epoch;

    // ── Pass-by-reference message resolution ─────────────────────────────────

    /// <summary>
    /// Resolves an inbox message by its TDA resource DID URL.
    ///
    /// Because <see cref="LiteInboxStore.EnqueueAsync"/> now stores the full DID URL
    /// as <c>InboxMessage.Id</c>, the DID URL is both the pass-by-reference handle
    /// and the direct LiteDB lookup key. No parsing needed.
    ///
    /// Hot path: IMemoryCache (TTL 24 h — matches the nonce replay window).
    /// Cold path: IInboxStore.GetByIdAsync → populate cache → return.
    ///
    /// Derived from: pass-by-reference pattern — DSA 0.24 Epoch 0.
    /// </summary>
    public async Task<InboxMessageView?> GetMessageAsync(
        string messageDid, CancellationToken ct = default)
    {
        // DID URL is the cache key — no cross-TDA collision possible.
        if (_cache.TryGetValue(messageDid, out InboxMessageView? cached))
            return cached;

        // GetByIdAsync queries by Id == messageDid directly.
        var msg = await _inbox.GetByIdAsync(messageDid, ct);
        if (msg is null) return null;

        var view = new InboxMessageView(msg.Id, msg.MessageType, msg.PackedPayload, msg.AttemptCount);
        _cache.Set(messageDid, view, TimeSpan.FromHours(24));
        return view;
    }
}

// ── InboxMessageView ──────────────────────────────────────────────────────────

/// <summary>
/// Read-only projection of an <see cref="InboxMessage"/> for LOBE cmdlet consumption.
/// Cmdlets receive this via <see cref="Svrn7RunspaceContext.GetMessageAsync"/>.
/// The <see cref="Id"/> is the pass-by-reference handle passed through pipelines.
/// </summary>
public sealed record InboxMessageView(
    string Id,
    string MessageType,
    string PackedPayload,
    int    AttemptCount);
