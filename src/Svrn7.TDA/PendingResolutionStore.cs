using System.Collections.Concurrent;

namespace Svrn7.TDA;

/// <summary>
/// Correlation entry for an in-flight DID resolution request that has been escalated
/// to a parent TDA tier and is awaiting the async <c>did-resolve-response</c> reply.
/// </summary>
public sealed record PendingResolutionEntry(
    string         ImmediateRequesterDid,
    string         ImmediateRequesterEndpoint,
    string         RequestedDid,
    DateTimeOffset CreatedAt);

/// <summary>
/// Singleton in-memory store for pending DID resolution relay entries.
/// Keyed by <c>originalRequestId</c> — the same correlation ID carried through
/// every relay hop. Thread-safe; entries are removed on first successful match.
/// </summary>
public sealed class PendingResolutionStore
{
    private readonly ConcurrentDictionary<string, PendingResolutionEntry> _pending = new();

    public void Add(string correlationId, PendingResolutionEntry entry) =>
        _pending[correlationId] = entry;

    public PendingResolutionEntry? TryRemove(string correlationId) =>
        _pending.TryRemove(correlationId, out var e) ? e : null;
}
