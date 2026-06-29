using Svrn7.Federation;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;

namespace Svrn7.Society;

// ── Options ───────────────────────────────────────────────────────────────────

/// <summary>
/// Configuration options for the Society-level driver.
/// Extends Svrn7Options with Society-specific settings.
/// </summary>
public class Svrn7SocietyOptions : Svrn7Options
{
    /// <summary>This Society's own DID.</summary>
    public string SocietyDid { get; set; } = string.Empty;

    /// <summary>The Federation's DID — used as a valid payee in both epochs.</summary>
    public string FederationDid { get; set; } = string.Empty;

    /// <summary>
    /// Fixed grana drawn per overdraft event.
    /// Society wallet must be below CitizenEndowmentGrana to trigger a draw.
    /// </summary>
    public long DrawAmountGrana { get; set; } = 1_000_000_000_000L;  // 1,000 SVRN7

    /// <summary>
    /// Maximum accumulated overdrawn grana before registration is blocked.
    /// </summary>
    public long OverdraftCeilingGrana { get; set; } = 10_000_000_000_000L; // 10,000 SVRN7

    /// <summary>
    /// Timeout for DIDComm round-trip to Federation (e.g. overdraft draw).
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan FederationRoundTripTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// HTTP endpoint URL of the Federation TDA's POST /didcomm route.
    /// Example: "http://federation.svrn7.net:8443/didcomm".
    /// When configured, overdraft draw requests are delivered via DIDComm HTTP transport.
    /// When empty (default), requests are logged only (development/test mode).
    /// </summary>
    public string FederationEndpointUrl { get; set; } = string.Empty;

    /// <summary>
    /// File path for svrn7-msg.db — the durable DIDComm message database.
    /// Kept separate from svrn7.db so message writes never contend with
    /// wallet and identity writes on the same LiteDB file lock.
    /// </summary>
    public string MsgDbPath      { get; set; } = "data/svrn7-msg.db";

    /// <summary>
    /// Path to svrn7-schemas.db (Schema Registry — Society TDA Only, DSA 0.24).
    /// </summary>
    public string SchemasDbPath  { get; set; } = "data/svrn7-schemas.db";

    /// <summary>
    /// Ed25519 private key bytes for this Society's DIDComm messaging key.
    /// Used for signing outbound DIDComm messages and decrypting inbound ones.
    /// Must not be stored in plaintext in production — load from HSM or encrypted vault.
    /// </summary>
    public byte[] SocietyMessagingPrivateKeyEd25519 { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Ed25519 public key bytes of the Federation's DIDComm messaging key.
    /// Used to encrypt DIDComm messages addressed to the Federation.
    /// </summary>
    public byte[] FederationMessagingPublicKeyEd25519 { get; set; } = Array.Empty<byte>();
}

// ── ISvrn7SocietyDriver ───────────────────────────────────────────────────────

/// <summary>
/// Society-level driver interface for the SVRN7 Shared Reserve Currency (SRC).
/// Extends ISvrn7Driver with:
/// - Society-scoped citizen registration (RegisterCitizenInSocietyAsync)
/// - DIDComm SignThenEncrypt transfer handling (all cross-Society transfers)
/// - Cross-Society Epoch 1 transfers via TransferOrderCredential
/// - Overdraft management and Federation top-up protocol
/// - Multi-DID citizen support
/// - Cross-Society VC Document Resolution
/// </summary>
public interface ISvrn7SocietyDriver : ISvrn7Driver
{
    // ── Society identity ──────────────────────────────────────────────────────
    string SocietyDid { get; }
    Task<SocietyRecord?> GetOwnSocietyAsync(CancellationToken ct = default);

    // ── Society-scoped citizen registration ───────────────────────────────────
    /// <summary>
    /// Registers a citizen within this Society. Explicitly names the owning Society.
    /// Triggers overdraft draw if Society wallet is insufficient.
    /// Blocks if overdraft ceiling is hit (SocietyEndowmentDepletedException).
    /// </summary>
    Task<OperationResult> RegisterCitizenInSocietyAsync(
        RegisterCitizenInSocietyRequest request, CancellationToken ct = default);

    // ── DIDComm transfer entry point ──────────────────────────────────────────
    /// <summary>
    /// Handles an incoming packed DIDComm transfer request message.
    /// All transfers — same-Society and cross-Society — enter here.
    /// Returns a packed DIDComm receipt message.
    /// </summary>
    Task<string> HandleIncomingTransferMessageAsync(
        string packedDIDCommMessage, CancellationToken ct = default);

    // ── Cross-Society Epoch 1 transfers ───────────────────────────────────────
    /// <summary>
    /// Initiates a cross-Society citizen-to-citizen transfer.
    /// Debits payer, issues TransferOrderCredential VC, sends via DIDComm to target Society.
    /// Uses nonce-based idempotency (TransferId = Blake3 of canonical JSON).
    /// </summary>
    Task<OperationResult> TransferToExternalCitizenAsync(
        TransferRequest request,
        string targetSocietyDid,
        CancellationToken ct = default);

    /// <summary>
    /// Initiates a transfer to the Federation wallet (permitted in both Epoch 0 and 1).
    /// </summary>
    Task<OperationResult> TransferToFederationAsync(
        string payerDid,
        long amountGrana,
        string nonce,
        string signature,
        string? memo = null,
        CancellationToken ct = default);

    // ── Overdraft management ──────────────────────────────────────────────────
    Task<OverdraftStatus> GetOverdraftStatusAsync(CancellationToken ct = default);
    Task<SocietyOverdraftRecord?> GetOverdraftRecordAsync(CancellationToken ct = default);

    // ── Society membership ────────────────────────────────────────────────────
    Task<IReadOnlyList<string>> GetMemberCitizenDidsAsync(CancellationToken ct = default);
    Task<bool> IsMemberAsync(string citizenDid, CancellationToken ct = default);

    // ── Cross-Society VC Document Resolution ─────────────────────────────────
    /// <summary>
    /// Resolves a VC across all known Societies via DIDComm fan-out.
    /// Returns partial results if some Societies do not respond within timeout.
    /// </summary>
    Task<CrossSocietyVcQueryResult> FindVcsBySubjectAcrossSocietiesAsync(
        string subjectDid, TimeSpan? timeout = null, CancellationToken ct = default);
}
