namespace Svrn7.Core.Models;

// ── Enumerations ──────────────────────────────────────────────────────────────

public enum KeyAlgorithm   { Secp256k1, Ed25519 }
public enum DidStatus      { Active, Suspended, Deactivated }
public enum VcStatus       { Active, Suspended, Revoked, Expired }
public enum OverdraftStatus{ Clean, Overdrawn, Ceiling }
public enum DidMethodStatus{ Active, Dormant }   // Available = not in registry

// ── Core monetary models ───────────────────────────────────────────────────────

/// <summary>
/// A wallet belonging to a citizen, society, or the federation.
/// All balances are stored in grana (1 SVRN7 = 1,000,000 grana).
/// IsRestricted=true blocks all outbound transfers (default at creation).
/// </summary>

/// <summary>
/// Persisted nonce record for transfer replay protection (Step 3).
/// Stored in the Nonces collection with a TTL index on ExpiresAt.
/// LiteDB does not have native TTL — the ITransferNonceStore implementation
/// runs a cleanup sweep on every CheckAndInsert call.
/// </summary>
public record NonceRecord
{
    public required string        Nonce      { get; init; }
    public required DateTimeOffset SeenAt    { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}


/// <summary>Status of a durable inbox message.</summary>
public enum InboxMessageStatus { Pending, Processing, Processed, Failed }

/// <summary>
/// A durable DIDComm inbox message stored in svrn7-inbox.db.
/// Messages survive process restarts and are processed exactly once by
/// DIDCommMessageProcessorService.
///
/// Lifecycle: Pending → Processing → Processed | Failed
/// Failed messages retain LastError for operational diagnostics.
/// </summary>
public record InboxMessage
{
    public required string             Id            { get; init; }  // TDA resource DID URL
                                                                      // e.g. did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1c8e9d7f012345678
    public required string             MessageType   { get; init; }  // Protocol URI
    public required string             PackedPayload { get; init; }  // Raw DIDComm packed string
    public required DateTimeOffset     ReceivedAt    { get; init; }
    public InboxMessageStatus          Status        { get; set; } = InboxMessageStatus.Pending;
    public DateTimeOffset?             ProcessedAt   { get; set; }
    public string?                     LastError     { get; set; }
    public int                         AttemptCount  { get; set; }
}


/// <summary>
/// Durable record of a processed cross-Society TransferOrder, persisted in svrn7-inbox.db.
/// Stores the packed DIDComm receipt so duplicate TransferOrders receive the same reply
/// without re-executing the credit logic.
/// </summary>
public record ProcessedOrderRecord
{
    public required string        TransferId    { get; init; }  // Blake3 hex — unique key
    public required string        PackedReceipt { get; init; }  // Packed DIDComm receipt
    public required DateTimeOffset ProcessedAt  { get; init; }
}

public record Wallet
{
    public required string Did           { get; init; }
    public long   BalanceGrana           { get; set; }
    public bool   IsRestricted           { get; set; } = true;
    public DateTimeOffset CreatedAt      { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// An unspent transaction output. Balances are composed of UTXOs.
/// Once IsSpent=true the record is immutable — never deleted.
/// </summary>
public record Utxo
{
    public required string Id         { get; init; }  // Blake3 of creation context
    public required string OwnerDid   { get; init; }
    public required long   AmountGrana{ get; init; }
    public bool            IsSpent    { get; set; }
    public string?         SpentByTxId{ get; set; }
    public DateTimeOffset  CreatedAt  { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SpentAt    { get; set; }
}

/// <summary>Represents a balance query result in both denominations.</summary>
public record BalanceResult(long Grana, decimal Svrn7);

// ── Identity models ────────────────────────────────────────────────────────────

/// <summary>
/// A registered citizen. EncryptedPrivateKeyBase64 stores the Argon2-derived
/// AES-256-GCM encrypted private key. Burned to CSPRNG bytes on GDPR erasure.
/// </summary>
public record CitizenRecord
{
    public required string Did                      { get; init; }  // primary DID
    public required string PublicKeyHex             { get; init; }
    public required string EncryptedPrivateKeyBase64{ get; set;  }
    public bool            IsActive                 { get; set; } = true;
    public DateTimeOffset  RegisteredAt             { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A DID held by a citizen under a specific DID method name.
/// A citizen may hold DIDs under multiple method names registered to their Society.
/// </summary>
public record CitizenDidRecord
{
    public required string CitizenPrimaryDid { get; init; }  // FK → CitizenRecord.Did
    public required string Did               { get; init; }  // the additional DID
    public required string MethodName        { get; init; }  // e.g. "socalpha", "socalphahealth" — must match [a-z0-9]+
    public bool            IsPrimary         { get; init; }
    public DateTimeOffset  IssuedAt          { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A registered society. Must be active to receive Epoch 0 transfers.
/// PrimaryDidMethodName is immutable — cannot be deregistered.
/// </summary>
public record SocietyRecord
{
    public required string Did                 { get; init; }
    public required string PublicKeyHex        { get; init; }
    public required string SocietyName         { get; init; }
    public required string PrimaryDidMethodName{ get; init; }  // immutable
    public bool            IsActive            { get; set; } = true;
    public DateTimeOffset  RegisteredAt        { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Tracks a DID method name registered to a Society.
/// Records are permanent — deregistered names are retained with status=Dormant.
/// </summary>
public record SocietyDidMethodRecord
{
    public required string         SocietyDid     { get; init; }
    public required string         MethodName     { get; init; }
    public bool                    IsPrimary      { get; init; }
    public DidMethodStatus         Status         { get; set;  } = DidMethodStatus.Active;
    public DateTimeOffset          RegisteredAt   { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset?         DeregisteredAt { get; set;  }
    public DateTimeOffset?         DormantUntil   { get; set;  }
}

/// <summary>
/// The Federation identity and monetary policy record.
/// Has its own wallet in svrn7.db. TotalSupplyGrana is monotonically increasing.
/// </summary>
public record FederationRecord
{
    public required string Did                       { get; init; }
    public required string PublicKeyHex              { get; init; }
    public required string FederationName            { get; init; }
    public required string PrimaryDidMethodName      { get; init; }
    public long            TotalSupplyGrana          { get; set;  }
    public long            EndowmentPerSocietyGrana  { get; init; }
    public bool            IsActive                  { get; set; } = true;
    public DateTimeOffset  CreatedAt                 { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Tracks society membership — which society a citizen belongs to.
/// </summary>
public record SocietyMembershipRecord
{
    public required string CitizenPrimaryDid { get; init; }
    public required string SocietyDid       { get; init; }
    public DateTimeOffset  JoinedAt         { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Tracks overdraft state for a Society. Revolving credit facility:
/// each draw of DrawAmountGrana is pre-approved up to OverdraftCeilingGrana.
/// When ceiling is hit, registration blocks until Federation tops up.
/// </summary>
public record SocietyOverdraftRecord
{
    public required string         SocietyDid            { get; init; }
    public required long           DrawAmountGrana        { get; init; }   // fixed per draw
    public required long           OverdraftCeilingGrana  { get; init; }   // max accumulated
    public long                    TotalOverdrawnGrana    { get; set;  }   // resets on top-up
    public long                    LifetimeDrawsGrana     { get; set;  }   // never resets
    public int                     DrawCount              { get; set;  }   // since last top-up
    public OverdraftStatus         Status                 { get; set;  } = OverdraftStatus.Clean;
    public DateTimeOffset?         LastDrawAt             { get; set;  }
    public DateTimeOffset?         LastCoveredAt          { get; set;  }
}

// ── DID Document models ────────────────────────────────────────────────────────

/// <summary>
/// A W3C DID Document stored in the registry.
/// Version is monotonically increasing. Deactivation is permanent.
/// </summary>
public record DidDocument
{
    public required string        Did          { get; init; }
    public required string        MethodName   { get; init; }
    public int                    Version      { get; set;  }
    public DidStatus              Status       { get; set;  } = DidStatus.Active;
    public required string        DocumentJson { get; set;  }
    public DateTimeOffset         CreatedAt    { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset         UpdatedAt    { get; set;  } = DateTimeOffset.UtcNow;
    public DateTimeOffset?        DeactivatedAt{ get; set;  }
}

/// <summary>Result of a W3C DID Document Resolution.</summary>
public record DidResolutionResult
{
    public required DidDocument?  Document         { get; init; }
    public required string        Did              { get; init; }
    public required bool          Found            { get; init; }
    public string?                ErrorCode        { get; init; }  // W3C error code
    public DateTimeOffset         ResolvedAt       { get; init; } = DateTimeOffset.UtcNow;
}

// ── Verifiable Credential models ───────────────────────────────────────────────

/// <summary>
/// A stored Verifiable Credential record. The full JWT is stored in JwtEncoded.
/// VcHash is Blake3 of the JWT — used as a content fingerprint.
/// AutoExpireIfStale() is called on all read paths.
/// </summary>
public record VcRecord
{
    public required string        VcId         { get; init; }  // jti claim
    public required string        IssuerDid    { get; init; }
    public required string        SubjectDid   { get; init; }
    public required List<string>  Types        { get; init; }  // List<T> for LiteDB
    public required string        VcHash       { get; init; }  // Blake3 hex
    public required string        JwtEncoded   { get; init; }
    public VcStatus               Status       { get; set;  } = VcStatus.Active;
    public DateTimeOffset         IssuedAt     { get; init; }
    public DateTimeOffset?        ExpiresAt    { get; init; }
    public DateTimeOffset?        RevokedAt    { get; set;  }
    public string?                RevokedReason{ get; set;  }
}

/// <summary>An immutable revocation event record.</summary>
public record RevocationEvent
{
    public required string        VcId        { get; init; }
    public required string        RevokedBy   { get; init; }
    public required string        Reason      { get; init; }
    public DateTimeOffset         RevokedAt   { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Result of a VC Document Resolution.</summary>
public record VcResolutionResult
{
    public required VcRecord?     Record       { get; init; }
    public required string        VcId         { get; init; }
    public required bool          Found        { get; init; }
    public VcStatus?              CurrentStatus{ get; init; }
    public DateTimeOffset         ResolvedAt   { get; init; } = DateTimeOffset.UtcNow;
}

// ── Ledger models ──────────────────────────────────────────────────────────────

/// <summary>
/// An append-only Merkle log entry. TxId is Blake3 of canonical payload JSON.
/// EntryType distinguishes transfer, registration, epoch transition, supply update, etc.
/// </summary>
public record LogEntry
{
    public required string        TxId        { get; init; }
    public required string        EntryType   { get; init; }
    public required string        PayloadJson { get; init; }
    public required string        MerkleHash  { get; init; }  // SHA-256 leaf hash
    public DateTimeOffset         CreatedAt   { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A signed Merkle tree head snapshot.</summary>
public record TreeHead
{
    public required string        RootHash    { get; init; }
    public required long          TreeSize    { get; init; }
    public required string        Signature   { get; init; }  // secp256k1 CESR
    public DateTimeOffset         SignedAt    { get; init; } = DateTimeOffset.UtcNow;
}

// ── Transfer models ────────────────────────────────────────────────────────────

/// <summary>A request to transfer SVRN7 between participants.</summary>
public record TransferRequest
{
    public required string        PayerDid    { get; init; }
    public required string        PayeeDid    { get; init; }
    public required long          AmountGrana { get; init; }
    public required string        Nonce       { get; init; }
    public required DateTimeOffset Timestamp  { get; init; }
    public required string        Signature   { get; init; }  // secp256k1 CESR over canonical JSON
    public string?                Memo        { get; init; }
}

/// <summary>
/// A cross-Society transfer order VC payload.
/// Issued by the originating Society; consumed by the receiving Society.
/// TransferId is the Blake3 idempotency key.
/// </summary>
public record TransferOrderCredential
{
    public required string        TransferId        { get; init; }  // Blake3 of canonical transfer JSON
    public required string        PayerDid          { get; init; }
    public required string        PayeeDid          { get; init; }
    public required long          AmountGrana       { get; init; }
    public required string        OriginSocietyDid  { get; init; }
    public required string        TargetSocietyDid  { get; init; }
    public required int           Epoch             { get; init; }
    public required string        Nonce             { get; init; }
    public required DateTimeOffset Timestamp        { get; init; }
    public required DateTimeOffset ExpiresAt        { get; init; }
}

/// <summary>
/// Receipt issued by the receiving Society confirming payee has been credited.
/// Links back to the TransferOrderCredential via TransferId.
/// </summary>
public record TransferReceiptCredential
{
    public required string        TransferId        { get; init; }
    public required string        PayeeDid          { get; init; }
    public required long          CreditedGrana     { get; init; }
    public required string        TargetSocietyDid  { get; init; }
    public required DateTimeOffset CreditedAt       { get; init; }
}

// ── Overdraft / endowment DIDComm message payloads ────────────────────────────

public record OverdraftDrawRequest
{
    public required string        SocietyDid        { get; init; }
    public required long          DrawAmountGrana   { get; init; }
    public required int           DrawCount         { get; init; }
    public required string        Reason            { get; init; }  // e.g. "CitizenRegistration"
    public required DateTimeOffset RequestedAt      { get; init; }
}

public record OverdraftDrawReceipt
{
    public required string        SocietyDid        { get; init; }
    public required long          DrawAmountGrana   { get; init; }
    public required string        TransferId        { get; init; }  // Merkle log TxId
    public required DateTimeOffset ApprovedAt       { get; init; }
}

// ── Registration requests ─────────────────────────────────────────────────────

public record RegisterCitizenRequest
{
    public required string        Did               { get; init; }
    public required string        PublicKeyHex      { get; init; }
    public required byte[]        PrivateKeyBytes   { get; init; }
}

/// <summary>
/// Superset of RegisterCitizenRequest — explicitly names the Society this
/// citizen belongs to. Used by ISvrn7SocietyDriver only.
/// </summary>
public record RegisterCitizenInSocietyRequest
{
    public required string        Did               { get; init; }
    public required string        PublicKeyHex      { get; init; }
    public required byte[]        PrivateKeyBytes   { get; init; }
    public required string        SocietyDid        { get; init; }
    public string?                PreferredMethodName{ get; init; }  // null = Society primary
}

public record RegisterSocietyRequest
{
    public required string        Did               { get; init; }
    public required string        PublicKeyHex      { get; init; }
    public required byte[]        PrivateKeyBytes   { get; init; }
    public required string        SocietyName       { get; init; }
    public required string        PrimaryDidMethodName { get; init; }
    public required long          DrawAmountGrana   { get; init; }
    public required long          OverdraftCeilingGrana { get; init; }
}

// ── Key pair ──────────────────────────────────────────────────────────────────

public record Svrn7KeyPair
{
    public required string        PublicKeyHex      { get; init; }
    public required byte[]        PrivateKeyBytes   { get; init; }
    public required KeyAlgorithm  Algorithm         { get; init; }

    public void ZeroPrivateKey() => Array.Clear(PrivateKeyBytes, 0, PrivateKeyBytes.Length);
}

// ── Operation result ──────────────────────────────────────────────────────────

public record OperationResult
{
    public bool    Success      { get; init; }
    public string? ErrorMessage { get; init; }
    public object? Payload      { get; init; }

    public static OperationResult Ok(object? payload = null)
        => new() { Success = true, Payload = payload };

    public static OperationResult Fail(string error)
        => new() { Success = false, ErrorMessage = error };
}

/// <summary>
/// A failed outbound DIDComm message persisted to the dead-letter outbox.
/// Stored in svrn7-inbox.db (InboxLiteContext) in the Outbox collection.
/// Created by DIDCommMessageSwitchboard.DeliverOutboundAsync after Polly
/// retry exhaustion. Operators can inspect and manually retry via tooling.
/// </summary>
public record OutboxRecord
{
    public required string Id            { get; init; }  // TDA resource DID URL
    public required string PeerEndpoint  { get; init; }  // target TDA endpoint
    public required string PackedMessage { get; init; }  // packed DIDComm payload
    public required string MessageType   { get; init; }  // DIDComm @type URI
    public DateTimeOffset  FailedAt      { get; init; } = DateTimeOffset.UtcNow;
    public int             AttemptCount  { get; init; }
    public string?         LastError     { get; set; }
    public bool            IsRetried     { get; set; } = false;
}

