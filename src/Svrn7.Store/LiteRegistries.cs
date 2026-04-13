using LiteDB;
using Svrn7.Core.Exceptions;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;

namespace Svrn7.Store;

// ── DidRegistryLiteContext ────────────────────────────────────────────────────

/// <summary>LiteDB context for svrn7-dids.db.</summary>
public sealed class DidRegistryLiteContext : IDisposable
{
    private readonly LiteDatabase _db;
    private bool _disposed;

    public const string ColDocuments = "Documents";
    public const string ColHistory   = "History";
    public const string ColVMIndex   = "VMIndex";

    public DidRegistryLiteContext(string connectionString)
    {
        _db = new LiteDatabase(connectionString);
        var docs = _db.GetCollection<DidDocument>(ColDocuments);
        docs.EnsureIndex(d => d.Did, unique: true);
        docs.EnsureIndex(d => d.MethodName);
        docs.EnsureIndex(d => d.Status);
    }

    public ILiteCollection<DidDocument>  Documents => Get<DidDocument>(ColDocuments);
    public ILiteCollection<DidDocument>  History   => Get<DidDocument>(ColHistory);
    public ILiteCollection<BsonDocument> VMIndex   => ThrowIfDisposed2()._db.GetCollection(ColVMIndex);

    private ILiteCollection<T> Get<T>(string name)
    {
        ThrowIfDisposed();
        return _db.GetCollection<T>(name);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DidRegistryLiteContext));
    }

    private DidRegistryLiteContext ThrowIfDisposed2()
    {
        ThrowIfDisposed();
        return this;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _db.Dispose();
    }
}

/// <summary>IDidDocumentRegistry implementation backed by svrn7-dids.db.</summary>
public sealed class LiteDidDocumentRegistry : IDidDocumentRegistry
{
    private readonly DidRegistryLiteContext _ctx;
    public LiteDidDocumentRegistry(DidRegistryLiteContext ctx) => _ctx = ctx;

    public Task CreateAsync(DidDocument document, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_ctx.Documents.FindOne(d => d.Did == document.Did) is not null)
            throw new InvalidDidException(document.Did, "A DID Document already exists for this DID.");
        document = document with { Version = 1, UpdatedAt = DateTimeOffset.UtcNow };
        _ctx.Documents.Insert(document);
        _ctx.History.Insert(document);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(DidDocument document, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var current = _ctx.Documents.FindOne(d => d.Did == document.Did)
            ?? throw new NotFoundException("DidDocument", document.Did);
        if (current.Status == DidStatus.Deactivated)
            throw new InvalidDidException(document.Did, "Cannot update a deactivated DID Document.");
        if (document.Version != current.Version + 1)
            throw new InvalidDidException(document.Did,
                $"Version must be {current.Version + 1}, got {document.Version}.");
        document = document with { UpdatedAt = DateTimeOffset.UtcNow };
        _ctx.Documents.Update(document);
        _ctx.History.Insert(document);
        return Task.CompletedTask;
    }

    public Task DeactivateAsync(string did, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var doc = _ctx.Documents.FindOne(d => d.Did == did)
            ?? throw new NotFoundException("DidDocument", did);
        doc.Status        = DidStatus.Deactivated;
        doc.DeactivatedAt = DateTimeOffset.UtcNow;
        _ctx.Documents.Update(doc);
        return Task.CompletedTask;
    }

    public Task SuspendAsync(string did, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var doc = _ctx.Documents.FindOne(d => d.Did == did)
            ?? throw new NotFoundException("DidDocument", did);
        if (doc.Status == DidStatus.Deactivated)
            throw new InvalidDidException(did, "Cannot suspend a deactivated DID Document.");
        doc.Status = DidStatus.Suspended;
        _ctx.Documents.Update(doc);
        return Task.CompletedTask;
    }

    public Task ReinstateAsync(string did, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var doc = _ctx.Documents.FindOne(d => d.Did == did)
            ?? throw new NotFoundException("DidDocument", did);
        if (doc.Status == DidStatus.Deactivated)
            throw new InvalidDidException(did, "Cannot reinstate a deactivated DID Document.");
        doc.Status = DidStatus.Active;
        _ctx.Documents.Update(doc);
        return Task.CompletedTask;
    }

    public Task<DidResolutionResult> ResolveAsync(string did, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var doc = _ctx.Documents.FindOne(d => d.Did == did);
        if (doc is null)
            return Task.FromResult(new DidResolutionResult
                { Did = did, Found = false, ErrorCode = "notFound", Document = null });
        return Task.FromResult(new DidResolutionResult
            { Did = did, Found = true, Document = doc });
    }

    public Task<DidDocument?> ResolveVersionAsync(string did, int version, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<DidDocument?>(
            _ctx.History.FindOne(d => d.Did == did && d.Version == version));
    }

    public Task<IReadOnlyList<DidDocument>> GetHistoryAsync(string did, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var history = _ctx.History.Find(d => d.Did == did)
            .OrderBy(d => d.Version).ToList();
        return Task.FromResult<IReadOnlyList<DidDocument>>(history);
    }

    public Task<bool> IsActiveAsync(string did, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(
            _ctx.Documents.FindOne(d => d.Did == did)?.Status == DidStatus.Active);
    }

    public Task<string?> FindDidByPublicKeyHexAsync(string publicKeyHex, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var entry = _ctx.VMIndex.FindOne(Query.EQ("PublicKeyHex", publicKeyHex));
        return Task.FromResult<string?>(entry?["Did"]?.AsString);
    }

    public Task<IReadOnlyList<DidDocument>> QueryAsync(
        string? methodName = null, DidStatus? status = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var all = _ctx.Documents.FindAll();
        if (methodName is not null) all = all.Where(d => d.MethodName == methodName);
        if (status is not null)     all = all.Where(d => d.Status == status);
        return Task.FromResult<IReadOnlyList<DidDocument>>(all.ToList());
    }

    public Task<long> CountAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult((long)_ctx.Documents.Count());
    }
}

// ── VcRegistryLiteContext ─────────────────────────────────────────────────────

/// <summary>LiteDB context for svrn7-vcs.db.</summary>
public sealed class VcRegistryLiteContext : IDisposable
{
    private readonly LiteDatabase _db;
    private bool _disposed;

    public const string ColVcRecords   = "VcRecords";
    public const string ColRevocations = "RevocationEvents";

    public VcRegistryLiteContext(string connectionString)
    {
        _db = new LiteDatabase(connectionString);
        var col = _db.GetCollection<VcRecord>(ColVcRecords);
        col.EnsureIndex(v => v.VcId, unique: true);
        col.EnsureIndex(v => v.SubjectDid);
        col.EnsureIndex(v => v.IssuerDid);
        col.EnsureIndex(v => v.Status);
    }

    public ILiteCollection<VcRecord>         VcRecords   => Get<VcRecord>(ColVcRecords);
    public ILiteCollection<RevocationEvent>  Revocations => Get<RevocationEvent>(ColRevocations);

    private ILiteCollection<T> Get<T>(string name)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VcRegistryLiteContext));
        return _db.GetCollection<T>(name);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _db.Dispose();
    }
}

/// <summary>IVcRegistry implementation backed by svrn7-vcs.db.</summary>
public sealed class LiteVcRegistry : IVcRegistry
{
    private readonly VcRegistryLiteContext _ctx;
    public LiteVcRegistry(VcRegistryLiteContext ctx) => _ctx = ctx;

    public Task StoreAsync(VcRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ctx.VcRecords.Insert(record);
        return Task.CompletedTask;
    }

    public Task<bool> StoreIfAbsentAsync(VcRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_ctx.VcRecords.FindOne(v => v.VcId == record.VcId) is not null)
            return Task.FromResult(false);
        _ctx.VcRecords.Insert(record);
        return Task.FromResult(true);
    }

    public Task<VcRecord?> GetByIdAsync(string vcId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rec = _ctx.VcRecords.FindOne(v => v.VcId == vcId);
        if (rec is not null) AutoExpireIfStale(rec);
        return Task.FromResult<VcRecord?>(rec);
    }

    public Task<IReadOnlyList<VcRecord>> GetBySubjectAsync(string subjectDid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var list = _ctx.VcRecords.Find(v => v.SubjectDid == subjectDid).ToList();
        list.ForEach(AutoExpireIfStale);
        return Task.FromResult<IReadOnlyList<VcRecord>>(list);
    }

    public Task<IReadOnlyList<VcRecord>> GetByIssuerAsync(string issuerDid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var list = _ctx.VcRecords.Find(v => v.IssuerDid == issuerDid).ToList();
        list.ForEach(AutoExpireIfStale);
        return Task.FromResult<IReadOnlyList<VcRecord>>(list);
    }

    public Task<IReadOnlyList<VcRecord>> QueryAsync(
        string? subjectDid = null, string? issuerDid = null,
        string? credentialType = null, VcStatus? status = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var all = _ctx.VcRecords.FindAll();
        if (subjectDid     is not null) all = all.Where(v => v.SubjectDid == subjectDid);
        if (issuerDid      is not null) all = all.Where(v => v.IssuerDid  == issuerDid);
        if (credentialType is not null) all = all.Where(v => v.Types.Contains(credentialType));
        if (status         is not null) all = all.Where(v => v.Status     == status);
        var list = all.ToList();
        list.ForEach(AutoExpireIfStale);
        return Task.FromResult<IReadOnlyList<VcRecord>>(list);
    }

    public Task<VcStatus> GetStatusAsync(string vcId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rec = _ctx.VcRecords.FindOne(v => v.VcId == vcId)
            ?? throw new NotFoundException("VcRecord", vcId);
        AutoExpireIfStale(rec);
        return Task.FromResult(rec.Status);
    }

    public Task RevokeAsync(string vcId, string reason, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rec = _ctx.VcRecords.FindOne(v => v.VcId == vcId)
            ?? throw new NotFoundException("VcRecord", vcId);
        if (rec.Status == VcStatus.Revoked)
            throw new InvalidCredentialException(vcId, "Already permanently revoked.");
        rec.Status       = VcStatus.Revoked;
        rec.RevokedAt    = DateTimeOffset.UtcNow;
        rec.RevokedReason= reason;
        _ctx.VcRecords.Update(rec);
        _ctx.Revocations.Insert(new RevocationEvent
            { VcId = vcId, RevokedBy = rec.IssuerDid, Reason = reason });
        return Task.CompletedTask;
    }

    public Task SuspendAsync(string vcId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rec = _ctx.VcRecords.FindOne(v => v.VcId == vcId)
            ?? throw new NotFoundException("VcRecord", vcId);
        if (rec.Status == VcStatus.Revoked) throw new InvalidCredentialException(vcId, "Cannot suspend a revoked VC.");
        rec.Status = VcStatus.Suspended;
        _ctx.VcRecords.Update(rec);
        return Task.CompletedTask;
    }

    public Task ReinstateAsync(string vcId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rec = _ctx.VcRecords.FindOne(v => v.VcId == vcId)
            ?? throw new NotFoundException("VcRecord", vcId);
        if (rec.Status == VcStatus.Revoked) throw new InvalidCredentialException(vcId, "Cannot reinstate a permanently revoked VC.");
        rec.Status = VcStatus.Active;
        _ctx.VcRecords.Update(rec);
        return Task.CompletedTask;
    }

    public Task<int> ExpireStaleCredentialsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var now   = DateTimeOffset.UtcNow;
        var stale = _ctx.VcRecords
            .Find(v => v.Status == VcStatus.Active && v.ExpiresAt != null && v.ExpiresAt < now)
            .ToList();
        foreach (var v in stale) { v.Status = VcStatus.Expired; _ctx.VcRecords.Update(v); }
        return Task.FromResult(stale.Count);
    }

    public Task<IReadOnlyList<RevocationEvent>> GetRevocationHistoryAsync(
        string vcId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var events = _ctx.Revocations.Find(r => r.VcId == vcId).ToList();
        return Task.FromResult<IReadOnlyList<RevocationEvent>>(events);
    }

    public Task<long> CountAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult((long)_ctx.VcRecords.Count());
    }

    private void AutoExpireIfStale(VcRecord rec)
    {
        if (rec.Status != VcStatus.Active) return;
        if (rec.ExpiresAt is null || rec.ExpiresAt > DateTimeOffset.UtcNow) return;
        rec.Status = VcStatus.Expired;
        _ctx.VcRecords.Update(rec);
    }
}
