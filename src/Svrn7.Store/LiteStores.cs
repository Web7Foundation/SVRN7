using LiteDB;
using Svrn7.Core.Exceptions;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;

namespace Svrn7.Store;

/// <summary>
/// IWalletStore implementation backed by Svrn7LiteContext (svrn7.db).
/// </summary>
public sealed class LiteWalletStore : IWalletStore
{
    private readonly Svrn7LiteContext _ctx;
    public LiteWalletStore(Svrn7LiteContext ctx) => _ctx = ctx;

    public Task CreateWalletAsync(Wallet wallet, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ctx.Wallets.Insert(wallet);
        return Task.CompletedTask;
    }

    public Task<Wallet?> GetWalletAsync(string did, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var w = _ctx.Wallets.FindOne(x => x.Did == did);
        return Task.FromResult<Wallet?>(w);
    }

    public Task SetRestrictedAsync(string did, bool restricted, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var w = _ctx.Wallets.FindOne(x => x.Did == did)
            ?? throw new NotFoundException("Wallet", did);
        w.IsRestricted = restricted;
        _ctx.Wallets.Update(w);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Utxo>> GetUnspentUtxosAsync(string did, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var utxos = _ctx.Utxos
            .Find(u => u.OwnerDid == did && !u.IsSpent)
            .ToList();
        return Task.FromResult<IReadOnlyList<Utxo>>(utxos);
    }

    public Task AddUtxoAsync(Utxo utxo, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ctx.Utxos.Insert(utxo);
        return Task.CompletedTask;
    }

    public Task MarkUtxoSpentAsync(string utxoId, string spentByTxId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var utxo = _ctx.Utxos.FindOne(u => u.Id == utxoId)
            ?? throw new NotFoundException("Utxo", utxoId);
        if (utxo.IsSpent) throw new DoubleSpendException(utxoId);
        utxo.IsSpent    = true;
        utxo.SpentByTxId= spentByTxId;
        utxo.SpentAt    = DateTimeOffset.UtcNow;
        _ctx.Utxos.Update(utxo);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetAllCitizenDidsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var dids = _ctx.Citizens
            .FindAll()
            .Where(c => c.IsActive)
            .Select(c => c.Did)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(dids);
    }
}

/// <summary>
/// IIdentityRegistry implementation backed by Svrn7LiteContext (svrn7.db).
/// </summary>
public sealed class LiteIdentityRegistry : IIdentityRegistry
{
    private readonly Svrn7LiteContext _ctx;
    public LiteIdentityRegistry(Svrn7LiteContext ctx) => _ctx = ctx;

    // ── Citizens ──────────────────────────────────────────────────────────────

    public Task RegisterCitizenAsync(CitizenRecord citizen, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ctx.Citizens.Insert(citizen);
        return Task.CompletedTask;
    }

    public Task<CitizenRecord?> GetCitizenAsync(string did, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Resolve to primary DID first (handles multi-method-name DIDs)
        var primary = _ctx.CitizenDids.FindOne(r => r.Did == did)?.CitizenPrimaryDid ?? did;
        var c = _ctx.Citizens.FindOne(x => x.Did == primary);
        return Task.FromResult<CitizenRecord?>(c);
    }

    public Task<bool> IsCitizenActiveAsync(string did, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var primary = _ctx.CitizenDids.FindOne(r => r.Did == did)?.CitizenPrimaryDid ?? did;
        return Task.FromResult(_ctx.Citizens.FindOne(x => x.Did == primary)?.IsActive ?? false);
    }

    // ── Societies ─────────────────────────────────────────────────────────────

    public Task RegisterSocietyAsync(SocietyRecord society, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ctx.Societies.Insert(society);
        return Task.CompletedTask;
    }

    public Task<SocietyRecord?> GetSocietyAsync(string did, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<SocietyRecord?>(_ctx.Societies.FindOne(x => x.Did == did));
    }

    public Task<bool> IsSocietyActiveAsync(string did, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_ctx.Societies.FindOne(x => x.Did == did)?.IsActive ?? false);
    }

    public Task SetSocietyActiveAsync(string did, bool active, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var s = _ctx.Societies.FindOne(x => x.Did == did)
            ?? throw new NotFoundException("Society", did);
        s.IsActive = active;
        _ctx.Societies.Update(s);
        return Task.CompletedTask;
    }

    // ── Citizen DID multi-method support ──────────────────────────────────────

    public Task StoreCitizenDidAsync(CitizenDidRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ctx.CitizenDids.Insert(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CitizenDidRecord>> GetAllDidsForCitizenAsync(
        string primaryDid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var list = _ctx.CitizenDids
            .Find(r => r.CitizenPrimaryDid == primaryDid)
            .ToList();
        return Task.FromResult<IReadOnlyList<CitizenDidRecord>>(list);
    }

    public Task<string?> ResolveCitizenPrimaryDidAsync(string anyDid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // If this IS a primary DID, return it directly
        if (_ctx.Citizens.FindOne(c => c.Did == anyDid) != null)
            return Task.FromResult<string?>(anyDid);
        // Otherwise look up in CitizenDids index
        var primary = _ctx.CitizenDids.FindOne(r => r.Did == anyDid)?.CitizenPrimaryDid;
        return Task.FromResult(primary);
    }

    // ── Memberships ───────────────────────────────────────────────────────────

    public Task StoreMembershipAsync(SocietyMembershipRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ctx.Memberships.Insert(record);
        return Task.CompletedTask;
    }

    public Task<SocietyMembershipRecord?> GetMembershipAsync(
        string citizenPrimaryDid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<SocietyMembershipRecord?>(
            _ctx.Memberships.FindOne(m => m.CitizenPrimaryDid == citizenPrimaryDid));
    }

    public Task<IReadOnlyList<string>> GetMemberCitizenDidsAsync(
        string societyDid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var dids = _ctx.Memberships
            .Find(m => m.SocietyDid == societyDid)
            .Select(m => m.CitizenPrimaryDid)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(dids);
    }

    // ── Key backups ───────────────────────────────────────────────────────────

    public Task StoreKeyBackupAsync(string did, string encryptedKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var col = _ctx.Citizens;
        var c   = col.FindOne(x => x.Did == did) ?? throw new NotFoundException("Citizen", did);
        c.EncryptedPrivateKeyBase64 = encryptedKey;
        col.Update(c);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetAllCitizenDidsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var dids = _ctx.Citizens
            .FindAll()
            .Where(c => c.IsActive)
            .Select(c => c.Did)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(dids);
    }
}

/// <summary>
/// LiteDB-backed nonce store for transfer replay protection (Step 3).
///
/// Design:
///   - The Nonces collection has a unique index on Nonce and a non-unique index on ExpiresAt.
///   - IsReplayAsync first sweeps expired entries (ExpiresAt &lt; UtcNow), then attempts to
///     insert the new nonce. A duplicate-key exception on insert means the nonce is a replay.
///   - The sweep runs on every call — this is acceptable because nonce checks are already
///     on the hot path and the expired set is expected to be small (24-hour window × call rate).
///   - Thread safety: LiteDB write operations are serialised internally; no additional
///     locking is required for single-process deployments.
///   - Multi-process deployments: share a single svrn7.db file path; LiteDB handles
///     file-level locking. Only one writer process at a time is supported.
/// </summary>
public sealed class LiteTransferNonceStore : ITransferNonceStore
{
    private readonly Svrn7LiteContext _ctx;

    public LiteTransferNonceStore(Svrn7LiteContext ctx) => _ctx = ctx;

    public Task<bool> IsReplayAsync(
        string nonce, TimeSpan replayWindow, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var col     = _ctx.Nonces;
        var now     = DateTimeOffset.UtcNow;
        var expires = now.Add(replayWindow);

        // 1. Sweep expired nonces (index scan on ExpiresAt — O(expired) not O(all))
        col.DeleteMany(n => n.ExpiresAt < now);

        // 2. Attempt atomic insert; duplicate key == replay
        var record = new NonceRecord
        {
            Nonce     = nonce,
            SeenAt    = now,
            ExpiresAt = expires,
        };

        try
        {
            col.Insert(record);
            return Task.FromResult(false);   // first time seen — not a replay
        }
        catch (LiteDB.LiteException ex)
            when (ex.ErrorCode == LiteDB.LiteException.INDEX_DUPLICATE_KEY)
        {
            return Task.FromResult(true);    // duplicate nonce — replay detected
        }
    }
}

