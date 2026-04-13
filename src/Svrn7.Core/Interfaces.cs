using Svrn7.Core.Models;

namespace Svrn7.Core.Interfaces;

// ── Cryptography ──────────────────────────────────────────────────────────────

public interface ICryptoService
{
    Svrn7KeyPair GenerateSecp256k1KeyPair();
    Svrn7KeyPair GenerateEd25519KeyPair();
    string  SignSecp256k1(byte[] payload, byte[] privateKeyBytes);   // returns CESR string
    string  SignEd25519(byte[] payload, byte[] privateKeyBytes);     // returns CESR string
    bool    VerifySecp256k1(byte[] payload, string cesrSignature, string publicKeyHex);
    bool    VerifyEd25519(byte[] payload, string cesrSignature, string publicKeyHex);
    byte[]  EncryptAes256Gcm(byte[] plaintext, byte[] key);          // nonce prepended
    byte[]  DecryptAes256Gcm(byte[] ciphertext, byte[] key);
    string  Blake3Hex(byte[] data);
    string  Base58Encode(byte[] data);
    byte[]  Base58Decode(string encoded);
}

// ── Wallet store ──────────────────────────────────────────────────────────────

public interface IWalletStore
{
    Task CreateWalletAsync(Wallet wallet, CancellationToken ct = default);
    Task<Wallet?> GetWalletAsync(string did, CancellationToken ct = default);
    Task SetRestrictedAsync(string did, bool restricted, CancellationToken ct = default);
    Task<IReadOnlyList<Utxo>> GetUnspentUtxosAsync(string did, CancellationToken ct = default);
    Task AddUtxoAsync(Utxo utxo, CancellationToken ct = default);
    Task MarkUtxoSpentAsync(string utxoId, string spentByTxId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllCitizenDidsAsync(CancellationToken ct = default);
}

// ── Identity registry ─────────────────────────────────────────────────────────


/// <summary>
/// Durable nonce store for transfer replay protection (Step 3).
/// Backed by a LiteDB collection with an index on ExpiresAt.
/// CheckAndInsertAsync atomically checks whether the nonce was seen before
/// within the replay window and inserts it if not.
/// </summary>
public interface ITransferNonceStore
{
    /// <summary>
    /// Returns false (and inserts the nonce) if this nonce has not been seen within
    /// the replay window. Returns true if the nonce is a replay (already seen).
    /// Also sweeps expired entries on each call.
    /// </summary>
    Task<bool> IsReplayAsync(string nonce, TimeSpan replayWindow, CancellationToken ct = default);
}


public interface IIdentityRegistry
{
    Task RegisterCitizenAsync(CitizenRecord citizen, CancellationToken ct = default);
    Task<CitizenRecord?> GetCitizenAsync(string did, CancellationToken ct = default);
    Task<bool> IsCitizenActiveAsync(string did, CancellationToken ct = default);

    Task RegisterSocietyAsync(SocietyRecord society, CancellationToken ct = default);
    Task<SocietyRecord?> GetSocietyAsync(string did, CancellationToken ct = default);
    Task<bool> IsSocietyActiveAsync(string did, CancellationToken ct = default);
    Task SetSocietyActiveAsync(string did, bool active, CancellationToken ct = default);

    Task StoreCitizenDidAsync(CitizenDidRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<CitizenDidRecord>> GetAllDidsForCitizenAsync(string primaryDid, CancellationToken ct = default);
    Task<string?> ResolveCitizenPrimaryDidAsync(string anyDid, CancellationToken ct = default);

    Task StoreMembershipAsync(SocietyMembershipRecord record, CancellationToken ct = default);
    Task<SocietyMembershipRecord?> GetMembershipAsync(string citizenPrimaryDid, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetMemberCitizenDidsAsync(string societyDid, CancellationToken ct = default);

    Task StoreKeyBackupAsync(string did, string encryptedKey, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllCitizenDidsAsync(CancellationToken ct = default);
}

// ── Merkle log ────────────────────────────────────────────────────────────────

public interface IMerkleLog
{
    Task<string> AppendAsync(string entryType, string payloadJson, CancellationToken ct = default);
    Task<string> ComputeRootAsync(CancellationToken ct = default);
    Task<TreeHead> SignTreeHeadAsync(byte[] privateKeyBytes, CancellationToken ct = default);
    Task<bool> VerifyInclusionProofAsync(string txId, CancellationToken ct = default);
    Task<long> GetSizeAsync(CancellationToken ct = default);
    Task<TreeHead?> GetLatestTreeHeadAsync(CancellationToken ct = default);
}

// ── VC service ────────────────────────────────────────────────────────────────

public interface IVcService
{
    Task<string> IssueAsync(string issuerDid, string subjectDid,
        string credentialType, object credentialSubject,
        byte[] issuerPrivateKeyBytes, TimeSpan? validity = null,
        CancellationToken ct = default);

    Task<bool> VerifyAsync(string jwtEncoded, CancellationToken ct = default);
    Task<string> RevokeAsync(string vcId, string reason, CancellationToken ct = default);
}

// ── Sanctions checker ─────────────────────────────────────────────────────────

public interface ISanctionsChecker
{
    Task<bool> IsAllowedAsync(string did, CancellationToken ct = default);
}

// ── Transfer validator ────────────────────────────────────────────────────────

public interface ITransferValidator
{
    Task ValidateAsync(TransferRequest request, CancellationToken ct = default);
}

// ── DID Document registry ──────────────────────────────────────────────────────

public interface IDidDocumentRegistry
{
    Task CreateAsync(DidDocument document, CancellationToken ct = default);
    Task UpdateAsync(DidDocument document, CancellationToken ct = default);  // version must be current+1
    Task DeactivateAsync(string did, CancellationToken ct = default);        // permanent
    Task SuspendAsync(string did, CancellationToken ct = default);
    Task ReinstateAsync(string did, CancellationToken ct = default);
    Task<DidResolutionResult> ResolveAsync(string did, CancellationToken ct = default);
    Task<DidDocument?> ResolveVersionAsync(string did, int version, CancellationToken ct = default);
    Task<IReadOnlyList<DidDocument>> GetHistoryAsync(string did, CancellationToken ct = default);
    Task<bool> IsActiveAsync(string did, CancellationToken ct = default);
    Task<string?> FindDidByPublicKeyHexAsync(string publicKeyHex, CancellationToken ct = default);
    Task<IReadOnlyList<DidDocument>> QueryAsync(string? methodName = null,
        DidStatus? status = null, CancellationToken ct = default);
    Task<long> CountAsync(CancellationToken ct = default);
}

// ── DID Document Resolver ─────────────────────────────────────────────────────

/// <summary>
/// Routes DID Document resolution by method name.
/// Same-method DIDs resolve locally; foreign-method DIDs are routed
/// via DIDComm to the owning Society (through the Federation).
/// </summary>
public interface IDidDocumentResolver
{
    Task<DidResolutionResult> ResolveAsync(string did, CancellationToken ct = default);
}

// ── VC registry ───────────────────────────────────────────────────────────────

public interface IVcRegistry
{
    Task StoreAsync(VcRecord record, CancellationToken ct = default);
    Task<bool> StoreIfAbsentAsync(VcRecord record, CancellationToken ct = default);
    Task<VcRecord?> GetByIdAsync(string vcId, CancellationToken ct = default);
    Task<IReadOnlyList<VcRecord>> GetBySubjectAsync(string subjectDid, CancellationToken ct = default);
    Task<IReadOnlyList<VcRecord>> GetByIssuerAsync(string issuerDid, CancellationToken ct = default);
    Task<IReadOnlyList<VcRecord>> QueryAsync(string? subjectDid = null,
        string? issuerDid = null, string? credentialType = null,
        VcStatus? status = null, CancellationToken ct = default);
    Task<VcStatus> GetStatusAsync(string vcId, CancellationToken ct = default);
    Task RevokeAsync(string vcId, string reason, CancellationToken ct = default);
    Task SuspendAsync(string vcId, CancellationToken ct = default);
    Task ReinstateAsync(string vcId, CancellationToken ct = default);
    Task<int> ExpireStaleCredentialsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RevocationEvent>> GetRevocationHistoryAsync(string vcId, CancellationToken ct = default);
    Task<long> CountAsync(CancellationToken ct = default);
}

// ── VC Document Resolver ──────────────────────────────────────────────────────

/// <summary>
/// Federation-level VC Document Resolver. Resolves VC identifiers and provides
/// rich search, enumeration and status operations across Society boundaries.
/// </summary>
public interface IVcDocumentResolver
{
    // Core resolution
    Task<VcResolutionResult> ResolveAsync(string vcId, CancellationToken ct = default);

    // Single-predicate find operations
    Task<IReadOnlyList<VcRecord>> FindBySubjectAsync(string subjectDid,
        VcStatus? statusFilter = null, CancellationToken ct = default);
    Task<IReadOnlyList<VcRecord>> FindByIssuerAsync(string issuerDid,
        VcStatus? statusFilter = null, CancellationToken ct = default);
    Task<IReadOnlyList<VcRecord>> FindByTypeAsync(string credentialType,
        VcStatus? statusFilter = null, CancellationToken ct = default);
    Task<IReadOnlyList<VcRecord>> FindBySocietyAsync(string societyDid,
        VcStatus? statusFilter = null, CancellationToken ct = default);

    // Cross-Society fan-out (DIDComm-based, returns partial results on timeout)
    Task<CrossSocietyVcQueryResult> FindBySubjectAcrossSocietiesAsync(
        string subjectDid, TimeSpan? timeout = null, CancellationToken ct = default);

    // Status operations
    Task<bool> IsValidAsync(string vcId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, VcStatus>> GetStatusBatchAsync(
        IEnumerable<string> vcIds, CancellationToken ct = default);

    // Expiry and revocation
    Task<IReadOnlyList<VcRecord>> FindExpiringAsync(TimeSpan withinWindow,
        CancellationToken ct = default);
    Task<IReadOnlyList<RevocationEvent>> GetRevocationHistoryAsync(
        string? subjectDid = null, string? issuerDid = null,
        DateTimeOffset? since = null, CancellationToken ct = default);

    // Reporting / metrics
    Task<IReadOnlyDictionary<string, long>> GetCountsByTypeAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<VcStatus, long>> GetCountsByStatusAsync(CancellationToken ct = default);
}

/// <summary>Result of a cross-Society fan-out VC query with partial-result support.</summary>
public record CrossSocietyVcQueryResult
{
    public required IReadOnlyList<VcRecord> Records          { get; init; }
    public required IReadOnlyList<string>   RespondedSocieties{ get; init; }
    public required IReadOnlyList<string>   TimedOutSocieties { get; init; }
    public bool IsComplete => TimedOutSocieties.Count == 0;
}

// ── Federation store ──────────────────────────────────────────────────────────

public interface IFederationStore
{
    Task InitialiseAsync(FederationRecord record, CancellationToken ct = default);
    Task<FederationRecord?> GetAsync(CancellationToken ct = default);
    Task UpdateSupplyAsync(long newTotalSupplyGrana, CancellationToken ct = default);
    Task<DidMethodStatus> GetMethodStatusAsync(string methodName, CancellationToken ct = default);
    Task RegisterMethodAsync(SocietyDidMethodRecord record, CancellationToken ct = default);
    Task DeregisterMethodAsync(string methodName, DateTimeOffset dormantUntil, CancellationToken ct = default);
    Task<SocietyDidMethodRecord?> GetMethodRecordAsync(string methodName, CancellationToken ct = default);
    Task<IReadOnlyList<SocietyDidMethodRecord>> GetAllMethodsAsync(
        string? societyDid = null, DidMethodStatus? statusFilter = null,
        CancellationToken ct = default);
}

// ── Society membership store ──────────────────────────────────────────────────


/// <summary>
/// Durable inbox store for incoming DIDComm messages.
/// Backed by a dedicated LiteDB database (svrn7-inbox.db) so the inbox
/// survives process restarts and is independent of svrn7.db writes.
///
/// Concurrency model: single writer (LiteDB file lock), any number of readers.
/// The background processor is the only writer; application code calls EnqueueAsync.
/// </summary>

/// <summary>
/// Durable store for cross-Society TransferOrder idempotency receipts.
/// Backed by InboxLiteContext (svrn7-inbox.db).
/// Ensures duplicate TransferOrders receive the same packed DIDComm receipt
/// without re-executing the credit logic.
/// </summary>
public interface IProcessedOrderStore
{
    /// <summary>Returns the cached packed receipt, or null if not yet processed.</summary>
    Task<string?> GetReceiptAsync(string transferId, CancellationToken ct = default);

    /// <summary>Stores the packed receipt for a completed TransferOrder.</summary>
    Task StoreReceiptAsync(string transferId, string packedReceipt, CancellationToken ct = default);
}

public interface IInboxStore
{
    /// <summary>Persists a new incoming message with Status = Pending.</summary>
    Task EnqueueAsync(string messageType, string packedPayload, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single message by its LiteDB ObjectId string.
    /// Used by <see cref="Svrn7RunspaceContext.GetMessageAsync"/> for pass-by-reference
    /// resolution in LOBE cmdlet pipelines. Returns null if not found.
    /// </summary>
    Task<InboxMessage?> GetByIdAsync(string objectId, CancellationToken ct = default);

    /// <summary>
    /// Returns up to <paramref name="batchSize"/> Pending messages and marks
    /// them Processing atomically. Returns an empty list when the inbox is empty.
    /// </summary>
    Task<IReadOnlyList<InboxMessage>> DequeueBatchAsync(
        int batchSize = 20, CancellationToken ct = default);

    /// <summary>Marks a message Processed and records the completion timestamp.</summary>
    Task MarkProcessedAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    /// Marks a message Failed, records the error text, and increments AttemptCount.
    /// Resets Status to Pending if <paramref name="retry"/> is true and
    /// AttemptCount is below <paramref name="maxAttempts"/>.
    /// </summary>
    Task MarkFailedAsync(string messageId, string error,
        bool retry = true, int maxAttempts = 3, CancellationToken ct = default);

    /// <summary>
    /// Resets any messages stuck in Processing back to Pending.
    /// Call on startup to recover from unclean shutdown.
    /// </summary>
    Task ResetStuckMessagesAsync(CancellationToken ct = default);

    /// <summary>Returns the count of messages per status for monitoring.</summary>
    Task<IReadOnlyDictionary<InboxMessageStatus, int>> GetStatusCountsAsync(
        CancellationToken ct = default);
}

public interface ISocietyMembershipStore
{
    Task StoreOverdraftAsync(SocietyOverdraftRecord record, CancellationToken ct = default);
    Task<SocietyOverdraftRecord?> GetOverdraftAsync(string societyDid, CancellationToken ct = default);
    Task UpdateOverdraftAsync(SocietyOverdraftRecord record, CancellationToken ct = default);
}

// ── DIDComm transfer handler ──────────────────────────────────────────────────

/// <summary>
/// Handles incoming DIDComm transfer protocol messages.
/// All transfers — same-Society and cross-Society — flow through DIDComm.
/// </summary>
public interface IDIDCommTransferHandler
{
    /// <summary>Handles an incoming transfer request from a citizen payer.</summary>
    Task<string> HandleTransferRequestAsync(string packedMessage, CancellationToken ct = default);

    /// <summary>Handles an incoming cross-Society TransferOrder VC from another Society.</summary>
    Task<string> HandleTransferOrderAsync(string packedMessage, CancellationToken ct = default);

    /// <summary>Handles a TransferReceipt confirming a cross-Society credit.</summary>
    Task HandleTransferReceiptAsync(string packedMessage, CancellationToken ct = default);
}

/// <summary>
/// Durable dead-letter outbox for failed outbound DIDComm messages.
/// Backed by svrn7-inbox.db (InboxLiteContext). Operators can inspect
/// and retry failed messages via the Outbox collection.
/// </summary>
public interface IOutboxStore
{
    /// <summary>Persists a failed outbound message to the dead-letter outbox.</summary>
    Task EnqueueAsync(OutboxRecord record, CancellationToken ct = default);

    /// <summary>Returns all unretried records for operator inspection.</summary>
    Task<IReadOnlyList<OutboxRecord>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>Marks a record as retried (whether retry succeeded or not).</summary>
    Task MarkRetriedAsync(string id, CancellationToken ct = default);
}

