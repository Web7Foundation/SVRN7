using LiteDB;
using Svrn7.Core.Models;

namespace Svrn7.Store;

/// <summary>
/// LiteDB context for svrn7.db.
/// Collections: Wallets, Utxos, Citizens, CitizenDids, Societies,
/// Memberships, KeyBackups, Overdrafts, LogEntries, TreeNodes, TreeHeads.
/// </summary>
public sealed class Svrn7LiteContext : IDisposable
{
    private readonly LiteDatabase _db;
    private bool _disposed;

    public const string ColWallets     = "Wallets";
    public const string ColUtxos       = "Utxos";
    public const string ColCitizens    = "Citizens";
    public const string ColCitizenDids = "CitizenDids";
    public const string ColSocieties   = "Societies";
    public const string ColMemberships = "Memberships";
    public const string ColKeyBackups  = "KeyBackups";
    public const string ColOverdrafts  = "Overdrafts";
    public const string ColLogEntries  = "LogEntries";
    public const string ColTreeNodes   = "TreeNodes";
    public const string ColTreeHeads   = "TreeHeads";
    public const string ColNonces      = "Nonces";

    public Svrn7LiteContext(string connectionString)
    {
        _db = new LiteDatabase(connectionString);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        ThrowIfDisposed();
        _db.GetCollection<Wallet>(ColWallets).EnsureIndex(w => w.Did, unique: true);
        _db.GetCollection<Utxo>(ColUtxos).EnsureIndex(u => u.OwnerDid);
        _db.GetCollection<Utxo>(ColUtxos).EnsureIndex(u => u.IsSpent);
        _db.GetCollection<CitizenRecord>(ColCitizens).EnsureIndex(c => c.Did, unique: true);
        _db.GetCollection<CitizenDidRecord>(ColCitizenDids).EnsureIndex(r => r.CitizenPrimaryDid);
        _db.GetCollection<CitizenDidRecord>(ColCitizenDids).EnsureIndex(r => r.Did, unique: true);
        _db.GetCollection<SocietyRecord>(ColSocieties).EnsureIndex(s => s.Did, unique: true);
        _db.GetCollection<SocietyMembershipRecord>(ColMemberships).EnsureIndex(m => m.CitizenPrimaryDid, unique: true);
        _db.GetCollection<SocietyMembershipRecord>(ColMemberships).EnsureIndex(m => m.SocietyDid);
        _db.GetCollection<SocietyOverdraftRecord>(ColOverdrafts).EnsureIndex(o => o.SocietyDid, unique: true);
        _db.GetCollection<NonceRecord>(ColNonces).EnsureIndex(n => n.Nonce, unique: true);
        _db.GetCollection<NonceRecord>(ColNonces).EnsureIndex(n => n.ExpiresAt);
    }

    public ILiteCollection<Wallet> Wallets             => Col<Wallet>(ColWallets);
    public ILiteCollection<Utxo>   Utxos               => Col<Utxo>(ColUtxos);
    public ILiteCollection<CitizenRecord> Citizens     => Col<CitizenRecord>(ColCitizens);
    public ILiteCollection<CitizenDidRecord> CitizenDids=> Col<CitizenDidRecord>(ColCitizenDids);
    public ILiteCollection<SocietyRecord> Societies    => Col<SocietyRecord>(ColSocieties);
    public ILiteCollection<SocietyMembershipRecord> Memberships => Col<SocietyMembershipRecord>(ColMemberships);
    public ILiteCollection<SocietyOverdraftRecord> Overdrafts   => Col<SocietyOverdraftRecord>(ColOverdrafts);
    public ILiteCollection<LogEntry>  LogEntries       => Col<LogEntry>(ColLogEntries);
    public ILiteCollection<BsonDocument> TreeNodes     => _db.GetCollection(ColTreeNodes);
    public ILiteCollection<TreeHead>  TreeHeads        => Col<TreeHead>(ColTreeHeads);
    public ILiteCollection<NonceRecord> Nonces           => Col<NonceRecord>(ColNonces);

    private ILiteCollection<T> Col<T>(string name)
    {
        ThrowIfDisposed();
        return _db.GetCollection<T>(name);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Svrn7LiteContext));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _db.Dispose();
    }
}
