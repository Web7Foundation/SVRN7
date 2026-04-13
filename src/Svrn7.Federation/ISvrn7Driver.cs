using Svrn7.Core.Models;

namespace Svrn7.Federation;

// ── Options ───────────────────────────────────────────────────────────────────

/// <summary>
/// Configuration options for the Federation-level SVRN7 driver.
/// All path options must be set. ValidateOnStart() enforces this.
/// </summary>
public class Svrn7Options
{
    /// <summary>secp256k1 public key hex of the Foundation governance keypair.</summary>
    public string FoundationPublicKeyHex { get; set; } = string.Empty;

    /// <summary>File path for svrn7.db (wallets, citizens, societies, Merkle log).</summary>
    public string Svrn7DbPath  { get; set; } = "data/svrn7.db";

    /// <summary>File path for svrn7-dids.db (DID Documents).</summary>
    public string DidsDbPath   { get; set; } = "data/svrn7-dids.db";

    /// <summary>File path for svrn7-vcs.db (Verifiable Credentials).</summary>
    public string VcsDbPath    { get; set; } = "data/svrn7-vcs.db";

    /// <summary>
    /// DID method name for this Federation deployment.
    /// Must match [a-z0-9]+ per W3C DID spec.
    /// </summary>
    public string DidMethodName { get; set; } = "drn";

    /// <summary>
    /// Duration after deregistration during which a DID method name
    /// cannot be re-registered by any Society. Default: 30 days.
    /// </summary>
    public TimeSpan DidMethodDormancyPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Background service sweep interval. Default: 1 hour.</summary>
    public TimeSpan BackgroundSweepInterval { get; set; } = TimeSpan.FromHours(1);
}

// ── ISvrn7Driver interface ────────────────────────────────────────────────────

/// <summary>
/// Primary facade for the SVRN7 Federation-level library.
/// SVRN7 (SOVRONA) is the Shared Reserve Currency (SRC) for the Web 7.0 digital ecosystem.
/// All 44 members are implemented by Svrn7Driver.
/// Implement IAsyncDisposable — dispose when host shuts down.
/// </summary>
public interface ISvrn7Driver : IAsyncDisposable
{
    // ── Properties ─────────────────────────────────────────────────────────────
    IDidDocumentRegistry DidRegistry { get; }
    IVcRegistry          VcRegistry  { get; }

    // ── Epoch control ──────────────────────────────────────────────────────────
    int  GetCurrentEpoch();
    Task AdvanceEpochAuthorisedAsync(int toEpoch, string governanceRef,
        string foundationSignature, string? notes = null, CancellationToken ct = default);
    Task RecordEpochTransitionAsync(int toEpoch, string governanceRef,
        string? notes = null, CancellationToken ct = default);

    // ── Citizen lifecycle ──────────────────────────────────────────────────────
    Task<OperationResult> RegisterCitizenAsync(RegisterCitizenRequest request,
        CancellationToken ct = default);
    Task<CitizenRecord?> GetCitizenAsync(string did, CancellationToken ct = default);
    Task<bool> IsCitizenActiveAsync(string did, CancellationToken ct = default);
    Task<IReadOnlyList<CitizenDidRecord>> GetAllDidsForCitizenAsync(
        string primaryDid, CancellationToken ct = default);
    Task<string?> ResolveCitizenPrimaryDidAsync(string anyDid, CancellationToken ct = default);

    // ── Society lifecycle ──────────────────────────────────────────────────────
    Task<OperationResult> RegisterSocietyAsync(RegisterSocietyRequest request,
        CancellationToken ct = default);
    Task<SocietyRecord?> GetSocietyAsync(string did, CancellationToken ct = default);
    Task<bool> IsSocietyActiveAsync(string did, CancellationToken ct = default);
    Task DeactivateSocietyAsync(string did, CancellationToken ct = default);

    // ── DID method names ───────────────────────────────────────────────────────
    Task<OperationResult> RegisterAdditionalDidMethodAsync(string societyDid,
        string methodName, CancellationToken ct = default);
    Task<OperationResult> DeregisterDidMethodAsync(string societyDid,
        string methodName, CancellationToken ct = default);
    Task<DidMethodStatus> GetDidMethodStatusAsync(string methodName,
        CancellationToken ct = default);
    Task<IReadOnlyList<SocietyDidMethodRecord>> GetAllDidMethodsAsync(
        string? societyDid = null, DidMethodStatus? statusFilter = null,
        CancellationToken ct = default);

    // ── Transfers ──────────────────────────────────────────────────────────────
    Task<OperationResult> TransferAsync(TransferRequest request,
        CancellationToken ct = default);
    Task<IReadOnlyList<OperationResult>> BatchTransferAsync(
        IEnumerable<TransferRequest> requests, CancellationToken ct = default);

    // ── Balance ────────────────────────────────────────────────────────────────
    Task<decimal> GetBalanceSvrn7Async(string did, CancellationToken ct = default);
    Task<long>    GetBalanceGranaAsync(string did, CancellationToken ct = default);
    Task<BalanceResult> GetBalanceResultAsync(string did, CancellationToken ct = default);

    // ── Federation supply ──────────────────────────────────────────────────────
    Task<FederationRecord?> GetFederationAsync(CancellationToken ct = default);
    Task<OperationResult> UpdateFederationSupplyAsync(long newTotalSupplyGrana,
        string foundationSignature, string governanceRef, CancellationToken ct = default);

    // ── DID Document registry ─────────────────────────────────────────────────
    Task CreateDidAsync(DidDocument document, CancellationToken ct = default);
    Task UpdateDidAsync(DidDocument document, CancellationToken ct = default);
    Task<DidResolutionResult> ResolveDidAsync(string did, CancellationToken ct = default);
    Task DeactivateDidAsync(string did, CancellationToken ct = default);
    Task SuspendDidAsync(string did, CancellationToken ct = default);
    Task ReinstateDidAsync(string did, CancellationToken ct = default);
    Task<IReadOnlyList<DidDocument>> GetDidHistoryAsync(string did,
        CancellationToken ct = default);
    Task<bool> IsDidActiveAsync(string did, CancellationToken ct = default);
    Task<string?> FindDidByPublicKeyAsync(string publicKeyHex,
        CancellationToken ct = default);

    // ── VC registry ────────────────────────────────────────────────────────────
    Task StoreVcAsync(VcRecord record, CancellationToken ct = default);
    Task<VcRecord?> GetVcByIdAsync(string vcId, CancellationToken ct = default);
    Task<IReadOnlyList<VcRecord>> GetVcsBySubjectAsync(string subjectDid,
        CancellationToken ct = default);
    Task<IReadOnlyList<VcRecord>> GetVcsByIssuerAsync(string issuerDid,
        CancellationToken ct = default);
    Task RevokeVcAsync(string vcId, string reason, CancellationToken ct = default);
    Task SuspendVcAsync(string vcId, CancellationToken ct = default);
    Task ReinstateVcAsync(string vcId, CancellationToken ct = default);
    Task<VcStatus> GetVcStatusAsync(string vcId, CancellationToken ct = default);
    Task<int> ExpireStaleVcsAsync(CancellationToken ct = default);

    // ── Merkle log ─────────────────────────────────────────────────────────────
    Task<string> AppendToLogAsync(string entryType, string payloadJson,
        CancellationToken ct = default);
    Task<string> GetMerkleRootAsync(CancellationToken ct = default);
    Task<TreeHead> SignMerkleTreeHeadAsync(CancellationToken ct = default);
    Task<long> GetLogSizeAsync(CancellationToken ct = default);
    Task<TreeHead?> GetLatestTreeHeadAsync(CancellationToken ct = default);

    // ── GDPR ───────────────────────────────────────────────────────────────────
    Task<OperationResult> ErasePersonAsync(string did, string controllerSignature,
        DateTimeOffset requestTimestamp, CancellationToken ct = default);

    // ── Crypto helpers ─────────────────────────────────────────────────────────
    Svrn7KeyPair GenerateSecp256k1KeyPair();
    Svrn7KeyPair GenerateEd25519KeyPair();
    string  SignSecp256k1(byte[] payload, byte[] privateKeyBytes);
    bool    VerifySecp256k1(byte[] payload, string cesrSig, string publicKeyHex);
    Task<string> Blake3HexAsync(byte[] data, CancellationToken ct = default);
    Task<string> Base58EncodeAsync(byte[] data, CancellationToken ct = default);

    // ── Wallet admin ───────────────────────────────────────────────────────────
    Task<int> LiftAllWalletRestrictionsAsync(CancellationToken ct = default);
}
