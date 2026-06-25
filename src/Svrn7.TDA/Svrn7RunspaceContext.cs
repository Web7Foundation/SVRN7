using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;
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
    private readonly IInboxStore             _inbox;
    private readonly IDeadLetterStore        _deadLetter;
    private readonly IMemoryCache            _cache;
    private readonly IProcessedOrderStore    _processedOrders;
    private readonly PendingResolutionStore  _pendingResolutions;
    private readonly string                  _agentIdentityPath;
    private volatile int                     _currentEpoch;
    private volatile string                  _parentTdaDid         = string.Empty;
    private volatile string                  _parentTdaEndpointUrl = string.Empty;
    private          string                  _federationEndpointUrl = string.Empty;

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { WriteIndented = false };
    private static readonly JsonSerializerOptions _jsonOptsCi =
        new() { PropertyNameCaseInsensitive = true };

    // ── Public surface (accessible as $SVRN7.* in PowerShell) ────────────────

    /// <summary>
    /// The Society-level SVRN7 driver. All monetary and identity operations.
    /// Derived from: "SVRN7 LOBE" inside Agent 1 Runspace — DSA 0.24 Epoch 0.
    /// </summary>
    public ISvrn7SocietyDriver Driver { get; }

    /// <summary>
    /// The local TDA's DID regardless of role (Wanderer, Citizen, Society, or Federation).
    /// Sourced from agent-identity.json at startup. LOBE cmdlets use <c>$SVRN7.LocalDid</c>.
    /// </summary>
    public string LocalDid { get; }

    /// <summary>
    /// The functional role of this TDA instance. Exposed to LOBE cmdlets as
    /// <c>$SVRN7.Role</c> for role-based guards.
    /// </summary>
    public Svrn7Role Role { get; }

    /// <summary>
    /// The current epoch value. Refreshed every 60 seconds by
    /// <see cref="IsolatedRunspaceFactory"/>. Cmdlets read this for epoch gating.
    /// </summary>
    public int CurrentEpoch => _currentEpoch;

    /// <summary>
    /// DID of the parent tier. Society DID for Citizens; Federation DID for Societies.
    /// Updated at runtime by <see cref="SetParentTda"/> after successful registration.
    /// </summary>
    public string ParentTdaDid => _parentTdaDid;

    /// <summary>
    /// DIDComm endpoint URL of the parent tier (e.g., <c>http://localhost:8442/didcomm</c>).
    /// Updated at runtime by <see cref="SetParentTda"/> after successful registration.
    /// </summary>
    public string ParentTdaEndpointUrl => _parentTdaEndpointUrl;

    /// <summary>
    /// Full DIDComm endpoint URL of this TDA (e.g., <c>http://localhost:8443/didcomm</c>).
    /// Included in outbound receipts and results so peers can store this TDA as their parent.
    /// </summary>
    public string ServiceEndpointUrl { get; }

    /// <summary>
    /// DIDComm endpoint URL of the Federation TDA, discovered at TDA startup via
    /// drn.directory DNS. Empty when <c>--federationdomain</c> / <c>Tda:FederationDomain</c>
    /// is not configured, or when no drn.directory TXT record was found.
    /// In LOBE cmdlets: <c>$SVRN7.FederationEndpointUrl</c>.
    /// </summary>
    public string FederationEndpointUrl => _federationEndpointUrl;

    // ── Internal surface (used by Switchboard, not directly by cmdlets) ───────

    internal IInboxStore         Inbox           => _inbox;
    internal IMemoryCache        Cache           => _cache;
    internal IProcessedOrderStore ProcessedOrders => _processedOrders;

    public Svrn7RunspaceContext(
        ISvrn7SocietyDriver    driver,
        IInboxStore            inbox,
        IDeadLetterStore       deadLetter,
        IMemoryCache           cache,
        IProcessedOrderStore   processedOrders,
        PendingResolutionStore pendingResolutions,
        int                    initialEpoch           = 0,
        Svrn7Role              role                   = Svrn7Role.Federation,
        string                 agentDid               = "",
        string                 parentTdaDid           = "",
        string                 parentTdaEndpointUrl   = "",
        string                 serviceEndpointUrl     = "",
        string                 agentIdentityPath      = "",
        string                 federationEndpointUrl  = "")
    {
        Driver                 = driver;
        Role                   = role;
        LocalDid               = agentDid;
        ServiceEndpointUrl     = serviceEndpointUrl;
        _parentTdaDid          = parentTdaDid;
        _parentTdaEndpointUrl  = parentTdaEndpointUrl;
        _federationEndpointUrl = federationEndpointUrl;
        _agentIdentityPath     = agentIdentityPath;
        _inbox                 = inbox;
        _deadLetter            = deadLetter;
        _cache                 = cache;
        _processedOrders       = processedOrders;
        _pendingResolutions    = pendingResolutions;
        _currentEpoch          = initialEpoch;
    }

    /// <summary>
    /// Refreshes the epoch value. Called by <see cref="IsolatedRunspaceFactory"/>
    /// on a 60-second timer. Thread-safe via volatile write.
    /// </summary>
    internal void SetEpoch(int epoch) => _currentEpoch = epoch;

    // ── DID Resolution correlated async relay ─────────────────────────────────

    /// <summary>
    /// Stores a pending resolution correlation entry. Called by <c>Resolve-Svrn7Did</c>
    /// when a local miss requires escalation to the parent TDA tier.
    /// Keyed by <paramref name="originalRequestId"/> which is carried through every relay hop.
    /// </summary>
    public void AddPendingResolution(
        string correlationId,
        string requestedDid,
        string immediateRequesterDid,
        string immediateRequesterEndpoint)
        => _pendingResolutions.Add(correlationId, new PendingResolutionEntry(
            immediateRequesterDid, immediateRequesterEndpoint, requestedDid, DateTimeOffset.UtcNow));

    /// <summary>
    /// Removes and returns the pending resolution entry for <paramref name="correlationId"/>.
    /// Returns <c>null</c> if no entry exists (meaning this TDA was the original requester).
    /// Called by <c>Invoke-Svrn7DidResolveResponse</c> to decide whether to relay or terminate.
    /// </summary>
    public PendingResolutionEntry? TryCompletePendingResolution(string correlationId)
        => _pendingResolutions.TryRemove(correlationId);

    // ── Parent TDA wiring ─────────────────────────────────────────────────────

    /// <summary>
    /// Updates the in-memory parent TDA DID and endpoint and persists both to
    /// <c>agent-identity.json</c>. Called by receipt/result LOBE handlers after
    /// successful registration with a Society or Federation.
    /// Thread-safe: volatile writes for the in-memory fields; file write is fire-and-forget.
    /// </summary>
    public void SetParentTda(string did, string endpointUrl)
    {
        _parentTdaDid         = did         ?? string.Empty;
        _parentTdaEndpointUrl = endpointUrl ?? string.Empty;

        if (string.IsNullOrEmpty(_agentIdentityPath)) return;
        try
        {
            var json = File.Exists(_agentIdentityPath)
                ? File.ReadAllText(_agentIdentityPath)
                : "{}";
            var node = JsonNode.Parse(json)!.AsObject();
            node["parentTdaDid"]         = _parentTdaDid;
            node["parentTdaEndpointUrl"] = _parentTdaEndpointUrl;
            File.WriteAllText(_agentIdentityPath,
                node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-critical — in-memory update already succeeded */ }
    }

    // ── DID Document exchange helpers ─────────────────────────────────────────

    /// <summary>
    /// Serialises the local registry's DID Document for <paramref name="did"/> to a
    /// JSON string using <c>System.Text.Json</c>. Returns <c>null</c> if not found.
    /// Used by LOBE cmdlets to embed DID Documents in outbound receipts and results.
    /// </summary>
    public string? GetDidDocumentJson(string did)
    {
        var result = Driver.DidRegistry.ResolveAsync(did).GetAwaiter().GetResult();
        return result.Document is null ? null
            : JsonSerializer.Serialize(result.Document, _jsonOpts);
    }

    /// <summary>
    /// Deserialises a DID Document from <paramref name="didDocumentJson"/> and stores it
    /// in the local DID registry if no document with that DID already exists (idempotent).
    /// Called by receipt/result LOBE handlers to persist received DID Documents.
    /// Assigns a new registry-local <c>Id</c> to avoid LiteDB key conflicts.
    /// </summary>
    public async Task StoreReceivedDidDocumentAsync(string didDocumentJson)
    {
        var doc = JsonSerializer.Deserialize<DidDocument>(didDocumentJson, _jsonOptsCi);
        if (doc is null) return;

        var existing = await Driver.DidRegistry.ResolveAsync(doc.Did);
        if (existing.Document is null)
        {
            // Assign a fresh local LiteDB Id so IDs from peer registries don't collide.
            doc = doc with { Id = Guid.NewGuid().ToString("N") };
            await Driver.CreateDidAsync(doc);
        }
    }

    /// <summary>
    /// Returns up to <paramref name="limit"/> processed email messages, newest-first.
    /// Called by the <c>Invoke-PandoMailList</c> LOBE cmdlet to fulfil
    /// <c>List-Emails</c> protocol requests.
    /// </summary>
    public async Task<IReadOnlyList<InboundMessageView>> ListEmailsAsync(
        int limit = 50, CancellationToken ct = default)
    {
        // Filter to the inbound email message type only — not protocol control messages
        // (List-Emails, Enqueue-PandoMail, etc.) which share the same LOBE prefix but
        // carry no rfc5322Body and must not appear in the inbox listing.
        const string emailTypePrefix = "did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Signal-PandoMail";
        var messages = await _inbox.ListByTypeAsync(emailTypePrefix, limit, ct);
        return messages
            .Select(m => new InboundMessageView(m.Id, m.MessageType, m.PackedPayload, m.FromDid, m.AttemptCount, m.ReceivedAt))
            .ToList();
    }

    /// <summary>
    /// Returns up to <paramref name="limit"/> sent email messages (Enqueue-PandoMail type),
    /// newest-first. Called by the <c>Invoke-PandoMailListSent</c> LOBE cmdlet to fulfil
    /// <c>List-OutboundEmails</c> protocol requests.
    /// </summary>
    public async Task<IReadOnlyList<InboundMessageView>> ListSentEmailsAsync(
        int limit = 50, CancellationToken ct = default)
    {
        const string sentTypePrefix = "did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Enqueue-PandoMail";
        var messages = await _inbox.ListByTypeAsync(sentTypePrefix, limit, ct);
        return messages
            .Select(m => new InboundMessageView(m.Id, m.MessageType, m.PackedPayload, m.FromDid, m.AttemptCount, m.ReceivedAt))
            .ToList();
    }

    /// <summary>
    /// Returns all pending dead-letter records. Called by the <c>Invoke-PandoMailListDeadLetters</c>
    /// LOBE cmdlet to fulfil <c>List-DeadLetters</c> protocol requests.
    /// </summary>
    public async Task<IReadOnlyList<DeadLetterRecord>> ListDeadLettersAsync(
        CancellationToken ct = default)
        => await _deadLetter.GetPendingAsync(ct);

    // ── Pass-by-reference message resolution ─────────────────────────────────

    /// <summary>
    /// Resolves an inbox message by its TDA resource DID URL.
    ///
    /// Because <see cref="LiteInboxStore.EnqueueAsync"/> now stores the full DID URL
    /// as <c>InboundMessage.Id</c>, the DID URL is both the pass-by-reference handle
    /// and the direct LiteDB lookup key. No parsing needed.
    ///
    /// Hot path: IMemoryCache (TTL 24 h — matches the nonce replay window).
    /// Cold path: IInboxStore.GetByIdAsync → populate cache → return.
    ///
    /// Derived from: pass-by-reference pattern — DSA 0.24 Epoch 0.
    /// </summary>
    public async Task<InboundMessageView?> GetMessageAsync(
        string messageDid, CancellationToken ct = default)
    {
        // DID URL is the cache key — no cross-TDA collision possible.
        if (_cache.TryGetValue(messageDid, out InboundMessageView? cached))
            return cached;

        // GetByIdAsync queries by Id == messageDid directly.
        var msg = await _inbox.GetByIdAsync(messageDid, ct);
        if (msg is null) return null;

        var view = new InboundMessageView(msg.Id, msg.MessageType, msg.PackedPayload, msg.FromDid, msg.AttemptCount, msg.ReceivedAt);
        _cache.Set(messageDid, view, TimeSpan.FromHours(24));
        return view;
    }
}

// ── InboundMessageView ──────────────────────────────────────────────────────────

/// <summary>
/// Read-only projection of an <see cref="InboundMessage"/> for LOBE cmdlet consumption.
/// Cmdlets receive this via <see cref="Svrn7RunspaceContext.GetMessageAsync"/>.
/// The <see cref="Id"/> is the pass-by-reference handle passed through pipelines.
/// </summary>
public sealed record InboundMessageView(
    string         Id,
    string         MessageType,
    string         PackedPayload,
    string?        FromDid,
    int            AttemptCount,
    DateTimeOffset ReceivedAt);
