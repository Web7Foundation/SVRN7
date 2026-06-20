using LiteDB;
using Svrn7.Core;
using Svrn7.Core.Exceptions;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;

namespace Svrn7.Store;

// ── FederationLiteContext ─────────────────────────────────────────────────────

/// <summary>
/// LiteDB context for the Federation-specific portion of svrn7.db.
/// Adds FederationRecords and DidMethodRegistry collections.
/// </summary>
public sealed class FederationLiteContext : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly bool _ownsDatabase;
    private bool _disposed;

    public const string ColFederation = "FederationRecords";

    /// <summary>
    /// Opens a new exclusive LiteDB connection. Use only when no other context has the file open.
    /// </summary>
    public FederationLiteContext(string connectionString)
    {
        var mapper = new BsonMapper();
        mapper.Entity<FederationRecord>().Id(f => f.Did);
        _db = new LiteDatabase(connectionString, mapper);
        _ownsDatabase = true;
    }

    /// <summary>
    /// Shares an already-open LiteDatabase (e.g. from Svrn7LiteContext).
    /// Does NOT dispose the database — the owner is responsible for its lifetime.
    /// The caller must ensure FederationRecord is mapped in the shared database's BsonMapper
    /// (Svrn7LiteContext.BuildMapper() does this).
    /// </summary>
    public FederationLiteContext(LiteDatabase sharedDb)
    {
        _db = sharedDb;
        _ownsDatabase = false;
    }

    public ILiteCollection<FederationRecord> Federation => Get<FederationRecord>(ColFederation);

    private ILiteCollection<T> Get<T>(string name)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FederationLiteContext));
        return _db.GetCollection<T>(name);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsDatabase) _db.Dispose();
    }
}

// ── LiteFederationStore ───────────────────────────────────────────────────────

/// <summary>
/// IFederationStore implementation. Manages the FederationRecord in svrn7.db.
/// </summary>
public sealed class LiteFederationStore : IFederationStore
{
    private readonly FederationLiteContext _ctx;

    public LiteFederationStore(FederationLiteContext ctx) => _ctx = ctx;

    public Task InitialiseAsync(FederationRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_ctx.Federation.Count() > 0)
            throw new ConfigurationException("Federation has already been initialised.");
        _ctx.Federation.Insert(record);
        return Task.CompletedTask;
    }

    public Task<FederationRecord?> GetAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<FederationRecord?>(_ctx.Federation.FindAll().FirstOrDefault());
    }

    public Task UpdateSupplyAsync(long newTotalSupplyGrana, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fed = _ctx.Federation.FindAll().FirstOrDefault()
            ?? throw new ConfigurationException("Federation has not been initialised.");
        if (newTotalSupplyGrana <= fed.TotalSupplyGrana)
            throw new InvalidOperationException(
                $"Supply is monotonically increasing. New value {newTotalSupplyGrana} must exceed current {fed.TotalSupplyGrana}.");
        fed.TotalSupplyGrana = newTotalSupplyGrana;
        _ctx.Federation.Update(fed);
        return Task.CompletedTask;
    }
}

// ── LocalDidDocumentResolver ──────────────────────────────────────────────────

/// <summary>
/// IDidDocumentResolver that resolves DIDs owned by this deployment's local registry.
/// Foreign-method DIDs fall through to the FederationDidDocumentResolver (in Svrn7.Society).
/// </summary>
public sealed class LocalDidDocumentResolver : IDidDocumentResolver
{
    private readonly IDidDocumentRegistry _registry;
    private readonly ISet<string>         _localMethodNames;

    public LocalDidDocumentResolver(
        IDidDocumentRegistry registry,
        IEnumerable<string> localMethodNames)
    {
        _registry         = registry;
        _localMethodNames = new HashSet<string>(localMethodNames, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<DidResolutionResult> ResolveAsync(string did, CancellationToken ct = default)
    {
        // Parse method name from DID: did:{method}:{id}
        var parts = did.Split(':', 3);
        if (parts.Length < 3 || parts[0] != "did")
            return new DidResolutionResult { Did = did, Found = false, ErrorCode = "invalidDid", Document = null };

        var methodName = parts[1];
        if (!_localMethodNames.Contains(methodName))
            return new DidResolutionResult { Did = did, Found = false, ErrorCode = "methodNotSupported", Document = null };

        return await _registry.ResolveAsync(did, ct);
    }
}

// ── LiteVcDocumentResolver ────────────────────────────────────────────────────

/// <summary>
/// Local IVcDocumentResolver backed by LiteVcRegistry.
/// Cross-Society fan-out is handled by FederationVcDocumentResolver (in Svrn7.Society).
/// </summary>
public sealed class LiteVcDocumentResolver : IVcDocumentResolver
{
    private readonly IVcRegistry _registry;

    public LiteVcDocumentResolver(IVcRegistry registry) => _registry = registry;

    public async Task<VcResolutionResult> ResolveAsync(string vcId, CancellationToken ct = default)
    {
        var record = await _registry.GetByIdAsync(vcId, ct);
        if (record is null)
            return new VcResolutionResult { VcId = vcId, Found = false, Record = null };
        return new VcResolutionResult
            { VcId = vcId, Found = true, Record = record, CurrentStatus = record.Status };
    }

    public async Task<IReadOnlyList<VcRecord>> FindBySubjectAsync(
        string subjectDid, VcStatus? statusFilter = null, CancellationToken ct = default)
    {
        var list = await _registry.GetBySubjectAsync(subjectDid, ct);
        return statusFilter.HasValue ? list.Where(v => v.Status == statusFilter).ToList() : list;
    }

    public async Task<IReadOnlyList<VcRecord>> FindByIssuerAsync(
        string issuerDid, VcStatus? statusFilter = null, CancellationToken ct = default)
    {
        var list = await _registry.GetByIssuerAsync(issuerDid, ct);
        return statusFilter.HasValue ? list.Where(v => v.Status == statusFilter).ToList() : list;
    }

    public async Task<IReadOnlyList<VcRecord>> FindByTypeAsync(
        string credentialType, VcStatus? statusFilter = null, CancellationToken ct = default)
        => await _registry.QueryAsync(credentialType: credentialType, status: statusFilter, ct: ct);

    public async Task<IReadOnlyList<VcRecord>> FindBySocietyAsync(
        string societyDid, VcStatus? statusFilter = null, CancellationToken ct = default)
    {
        var byIssuer  = await _registry.QueryAsync(issuerDid: societyDid,  status: statusFilter, ct: ct);
        var bySubject = await _registry.QueryAsync(subjectDid: societyDid, status: statusFilter, ct: ct);
        return byIssuer.Concat(bySubject)
                       .DistinctBy(v => v.VcId)
                       .ToList();
    }

    // Cross-Society fan-out — local resolver returns only local results
    public Task<CrossSocietyVcQueryResult> FindBySubjectAcrossSocietiesAsync(
        string subjectDid, TimeSpan? timeout = null, CancellationToken ct = default)
        => throw new NotSupportedException(
            "Cross-Society VC queries require FederationVcDocumentResolver from Svrn7.Society.");

    public async Task<bool> IsValidAsync(string vcId, CancellationToken ct = default)
    {
        var status = await _registry.GetStatusAsync(vcId, ct);
        return status == VcStatus.Active;
    }

    public async Task<IReadOnlyDictionary<string, VcStatus>> GetStatusBatchAsync(
        IEnumerable<string> vcIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, VcStatus>();
        foreach (var id in vcIds)
        {
            var rec = await _registry.GetByIdAsync(id, ct);
            result[id] = rec?.Status ?? VcStatus.Revoked;
        }
        return result;
    }

    public async Task<IReadOnlyList<VcRecord>> FindExpiringAsync(
        TimeSpan withinWindow, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.Add(withinWindow);
        return await _registry.QueryAsync(status: VcStatus.Active, ct: ct)
            .ContinueWith(t => t.Result
                .Where(v => v.ExpiresAt.HasValue && v.ExpiresAt.Value <= cutoff)
                .ToList() as IReadOnlyList<VcRecord>, ct);
    }

    public async Task<IReadOnlyList<RevocationEvent>> GetRevocationHistoryAsync(
        string? subjectDid = null, string? issuerDid = null,
        DateTimeOffset? since = null, CancellationToken ct = default)
    {
        // For local resolver: find VCs matching filters then pull revocation history
        var vcs = await _registry.QueryAsync(subjectDid: subjectDid, issuerDid: issuerDid,
            status: VcStatus.Revoked, ct: ct);
        var events = new List<RevocationEvent>();
        foreach (var vc in vcs)
        {
            var history = await _registry.GetRevocationHistoryAsync(vc.VcId, ct);
            events.AddRange(since.HasValue
                ? history.Where(e => e.RevokedAt >= since.Value)
                : history);
        }
        return events;
    }

    public async Task<IReadOnlyDictionary<string, long>> GetCountsByTypeAsync(CancellationToken ct = default)
    {
        var all = await _registry.QueryAsync(ct: ct);
        return all.SelectMany(v => v.Types.Select(t => t))
                  .GroupBy(t => t)
                  .ToDictionary(g => g.Key, g => (long)g.Count());
    }

    public async Task<IReadOnlyDictionary<VcStatus, long>> GetCountsByStatusAsync(CancellationToken ct = default)
    {
        var all = await _registry.QueryAsync(ct: ct);
        return all.GroupBy(v => v.Status)
                  .ToDictionary(g => g.Key, g => (long)g.Count());
    }
}
