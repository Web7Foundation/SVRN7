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
    private bool _disposed;

    public const string ColFederation  = "FederationRecords";
    public const string ColMethodReg   = "DidMethodRegistry";

    public FederationLiteContext(string connectionString)
    {
        _db = new LiteDatabase(connectionString);
        _db.GetCollection<SocietyDidMethodRecord>(ColMethodReg)
           .EnsureIndex(r => r.MethodName, unique: false);  // historical records — not unique
    }

    public ILiteCollection<FederationRecord>       Federation  => Get<FederationRecord>(ColFederation);
    public ILiteCollection<SocietyDidMethodRecord> MethodReg   => Get<SocietyDidMethodRecord>(ColMethodReg);

    private ILiteCollection<T> Get<T>(string name)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FederationLiteContext));
        return _db.GetCollection<T>(name);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _db.Dispose();
    }
}

// ── LiteFederationStore ───────────────────────────────────────────────────────

/// <summary>
/// IFederationStore implementation.
/// Manages FederationRecord and the global DID method name registry.
/// Method name dormancy is evaluated time-based — dormant records are never deleted.
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

    public Task<DidMethodStatus> GetMethodStatusAsync(string methodName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Find the most recent record for this method name
        var record = _ctx.MethodReg
            .Find(r => r.MethodName == methodName)
            .OrderByDescending(r => r.RegisteredAt)
            .FirstOrDefault();

        if (record is null)
            return Task.FromResult(DidMethodStatus.Active); // treat as Available — caller checks

        if (record.Status == DidMethodStatus.Active)
            return Task.FromResult(DidMethodStatus.Active);

        // Dormant — check if dormancy period has expired
        if (record.DormantUntil.HasValue && record.DormantUntil.Value <= DateTimeOffset.UtcNow)
            return Task.FromResult(DidMethodStatus.Active); // Available — but we return Active as flag meaning "re-registerable"

        return Task.FromResult(DidMethodStatus.Dormant);
    }

    public Task RegisterMethodAsync(SocietyDidMethodRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ctx.MethodReg.Insert(record);
        return Task.CompletedTask;
    }

    public Task DeregisterMethodAsync(string methodName, DateTimeOffset dormantUntil, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var record = _ctx.MethodReg
            .Find(r => r.MethodName == methodName && r.Status == DidMethodStatus.Active)
            .FirstOrDefault()
            ?? throw new NotFoundException("DidMethodRecord", methodName);
        record.Status        = DidMethodStatus.Dormant;
        record.DeregisteredAt= DateTimeOffset.UtcNow;
        record.DormantUntil  = dormantUntil;
        _ctx.MethodReg.Update(record);
        return Task.CompletedTask;
    }

    public Task<SocietyDidMethodRecord?> GetMethodRecordAsync(string methodName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var record = _ctx.MethodReg
            .Find(r => r.MethodName == methodName && r.Status == DidMethodStatus.Active)
            .FirstOrDefault();
        return Task.FromResult<SocietyDidMethodRecord?>(record);
    }

    public Task<IReadOnlyList<SocietyDidMethodRecord>> GetAllMethodsAsync(
        string? societyDid = null, DidMethodStatus? statusFilter = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var all = _ctx.MethodReg.FindAll();
        if (societyDid    is not null) all = all.Where(r => r.SocietyDid == societyDid);
        if (statusFilter  is not null) all = all.Where(r => r.Status     == statusFilter);
        return Task.FromResult<IReadOnlyList<SocietyDidMethodRecord>>(all.ToList());
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
