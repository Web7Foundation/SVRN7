using System.Text.Json;

namespace Svrn7.Core.Models;

// ── Enumerations ──────────────────────────────────────────────────────────────

public enum KeyAlgorithm   { Secp256k1, Ed25519, X25519 }
public enum DidStatus      { Active, Suspended, Deactivated }
public enum VcStatus       { Active, Suspended, Revoked, Expired }
public enum OverdraftStatus{ Clean, Overdrawn, Ceiling }
/// <summary>
/// Functional role of a TDA instance in the Web 7.0 network.
/// Stored on DidDocument to identify what role the owning TDA plays.
/// </summary>
public enum Svrn7Role { Federation, Society, Citizen, Wanderer, LOBEPackageManager, LOBEMarketplace }

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
    public required string        Nonce      { get; set; }
    public required DateTimeOffset SeenAt    { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
}


/// <summary>Status of a durable inbox message.</summary>
public enum InboundMessageStatus { Pending, Processing, Processed, Failed }

/// <summary>
/// A durable DIDComm inbox message stored in svrn7-msg.db.
/// Messages survive process restarts and are processed exactly once by
/// DIDCommMessageProcessorService.
///
/// Lifecycle: Pending → Processing → Processed | Failed
/// Failed messages retain LastError for operational diagnostics.
/// </summary>
public record InboundMessage
{
    public required string             Id            { get; set; }  // TDA resource DID URL
                                                                      // e.g. did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1c8e9d7f012345678
    public required string             MessageType   { get; set; }  // Protocol URI
    public required string             PackedPayload { get; set; }  // Unpacked DIDComm body (plaintext)
    public string?                     JweEnvelope   { get; set; }  // Original JWE wire payload — preserved for auditability and non-repudiation
    public string?                     FromDid       { get; set; }  // Sender DID from DIDComm envelope
    public string?                     WireId        { get; set; }  // DIDComm wire 'id' from the sender's envelope (null for encrypted/opaque messages)
    public required DateTimeOffset     ReceivedAt    { get; set; }
    public InboundMessageStatus          Status        { get; set; } = InboundMessageStatus.Pending;
    public DateTimeOffset?             ProcessedAt   { get; set; }
    public string?                     LastError     { get; set; }
    public int                         AttemptCount  { get; set; }

    static readonly JsonSerializerOptions _prettyOpts = new() { WriteIndented = true };

    public string ToFormattedJson()
    {
        JsonElement bodyElement;
        try   { bodyElement = JsonSerializer.Deserialize<JsonElement>(PackedPayload); }
        catch { bodyElement = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(PackedPayload)); }

        return JsonSerializer.Serialize(new
        {
            id            = Id,
            messageType   = MessageType,
            fromDid       = FromDid,
            wireId        = WireId,
            receivedAt    = ReceivedAt,
            status        = Status.ToString(),
            attemptCount  = AttemptCount,
            processedAt   = ProcessedAt,
            lastError     = LastError,
            jweEnvelope   = JweEnvelope,
            body          = bodyElement
        }, _prettyOpts);
    }
}


/// <summary>
/// Durable record of a processed cross-Society TransferOrder, persisted in svrn7-msg.db.
/// Stores the packed DIDComm receipt so duplicate TransferOrders receive the same reply
/// without re-executing the credit logic.
/// </summary>
public record ProcessedOrderRecord
{
    public required string        TransferId    { get; set; }  // Blake3 hex — unique key
    public required string        PackedReceipt { get; set; }  // Packed DIDComm receipt
    public required DateTimeOffset ProcessedAt  { get; set; }
}

public record Wallet
{
    public required string Did           { get; set; }
    public long   BalanceGrana           { get; set; }
    public bool   IsRestricted           { get; set; } = true;
    public DateTimeOffset CreatedAt      { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// An unspent transaction output. Balances are composed of UTXOs.
/// Once IsSpent=true the record is immutable — never deleted.
/// </summary>
public record Utxo
{
    public required string Id         { get; set; }  // Blake3 of creation context
    public required string OwnerDid   { get; set; }
    public required long   AmountGrana{ get; set; }
    public bool            IsSpent    { get; set; }
    public string?         SpentByTxId{ get; set; }
    public DateTimeOffset  CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
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
    public required string Did                      { get; set; }  // primary DID
    public required string PublicKeyHex             { get; set; }
    public required string EncryptedPrivateKeyBase64{ get; set;  }
    public bool            IsActive                 { get; set; } = true;
    public DateTimeOffset  RegisteredAt             { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A DID held by a citizen under a specific DID method name.
/// A citizen may hold DIDs under multiple method names registered to their Society.
/// </summary>
public record CitizenDidRecord
{
    public required string CitizenPrimaryDid { get; set; }  // FK → CitizenRecord.Did
    public required string Did               { get; set; }  // the additional DID
    public required string MethodName        { get; set; }  // e.g. "socalpha", "socalphahealth" — must match [a-z0-9]+
    public bool            IsPrimary         { get; set; }
    public DateTimeOffset  IssuedAt          { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A registered society. Must be active to receive Epoch 0 transfers.
/// </summary>
public record SocietyRecord
{
    public required string Did          { get; set; }
    public required string PublicKeyHex { get; set; }
    public required string SocietyName  { get; set; }
    public bool            IsActive     { get; set; } = true;
    public DateTimeOffset  RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// The Federation identity and monetary policy record.
/// Has its own wallet in svrn7.db. TotalSupplyGrana is monotonically increasing.
/// </summary>
public record FederationRecord
{
    public required string Did                       { get; set; }
    public required string PublicKeyHex              { get; set; }
    public required string FederationName            { get; set; }
    public long            TotalSupplyGrana          { get; set;  }
    public long            EndowmentPerSocietyGrana  { get; set; }
    public bool            IsActive                  { get; set; } = true;
    public DateTimeOffset  CreatedAt                 { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Tracks society membership — which society a citizen belongs to.
/// </summary>
public record SocietyMembershipRecord
{
    public required string CitizenPrimaryDid { get; set; }
    public required string SocietyDid       { get; set; }
    public DateTimeOffset  JoinedAt         { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Tracks overdraft state for a Society. Revolving credit facility:
/// each draw of DrawAmountGrana is pre-approved up to OverdraftCeilingGrana.
/// When ceiling is hit, registration blocks until Federation tops up.
/// </summary>
public record SocietyOverdraftRecord
{
    public required string         SocietyDid            { get; set; }
    public required long           DrawAmountGrana        { get; set; }   // fixed per draw
    public required long           OverdraftCeilingGrana  { get; set; }   // max accumulated
    public long                    TotalOverdrawnGrana    { get; set;  }   // resets on top-up
    public long                    LifetimeDrawsGrana     { get; set;  }   // never resets
    public int                     DrawCount              { get; set;  }   // since last top-up
    public OverdraftStatus         Status                 { get; set;  } = OverdraftStatus.Clean;
    public DateTimeOffset?         LastDrawAt             { get; set;  }
    public DateTimeOffset?         LastCoveredAt          { get; set;  }
}

// ── DID Document models ────────────────────────────────────────────────────────

/// <summary>W3C DID Core §5.2.1 verification method.</summary>
public record DidVerificationMethod
{
    public required string  Id           { get; set; }  // e.g. "did:drn:abc#key-1"
    public required string  Type         { get; set; }  // e.g. "EcdsaSecp256k1VerificationKey2019"
    public required string  Controller   { get; set; }  // DID that controls this key
    public string?          PublicKeyHex { get; set; }  // hex-encoded; set for Secp256k1/Ed25519
    public string?          PublicKeyMultibase { get; set; }  // multibase-encoded alternative
}

/// <summary>W3C DID Core §5.4 service endpoint.</summary>
public record DidServiceEndpoint
{
    public required string Id              { get; set; }  // e.g. "did:drn:abc#svc-1"
    public required string Type            { get; set; }  // e.g. "DIDCommMessaging"
    public required string ServiceEndpoint { get; set; }  // URI or object JSON
}

/// <summary>W3C Data Integrity proof attached to a DID Document.</summary>
public record DidProof
{
    public required string         Type               { get; set; }  // e.g. "Ed25519Signature2020"
    public required DateTimeOffset Created            { get; set; }
    public required string         VerificationMethod { get; set; }  // key DID URL
    public required string         ProofPurpose       { get; set; }  // e.g. "assertionMethod"
    public required string         ProofValue         { get; set; }  // base58btc multibase
}

/// <summary>
/// A W3C DID Document stored in the registry.
/// Version is monotonically increasing. Deactivation is permanent.
/// W3C DID Core 1.0 properties are first-class fields; DocumentJson holds the canonical serialization.
/// </summary>
public record DidDocument
{
    public string                           Id                     { get; set; } = Guid.NewGuid().ToString("N");
    public required string                  Did                    { get; set; }  // W3C id
    public required string                  MethodName             { get; set; }  // SVRN7 registry key
    public string?                          Controller             { get; set; }  // W3C controller (single DID)
    public List<string>                     AlsoKnownAs            { get; set; } = [];
    public List<DidVerificationMethod>      VerificationMethod     { get; set; } = [];
    public List<string>                     Authentication         { get; set; } = [];  // DID URLs referencing VerificationMethod
    public List<string>                     AssertionMethod        { get; set; } = [];
    public List<string>                     KeyAgreement           { get; set; } = [];
    public List<string>                     CapabilityInvocation   { get; set; } = [];
    public List<string>                     CapabilityDelegation   { get; set; } = [];
    public List<DidServiceEndpoint>         ServiceEndpoints       { get; set; } = [];
    public DidProof?                        Proof                  { get; set; }
    /// <summary>
    /// Role of the TDA that owns this DIDDocument. Set once at registration; treat as
    /// immutable thereafter. Uses set (not init) for LiteDB compatibility.
    /// </summary>
    public Svrn7Role?                         Role                   { get; set; }
    /// <summary>Human-readable name for this TDA (e.g. "Web 7.0 Foundation"). Set at Wanderer creation and carried forward to promoted-role DIDDocuments.</summary>
    public string?                          Svrn7Name                { get; set; }
    public int                              Version                { get; set; }
    public DidStatus                        Status                 { get; set; } = DidStatus.Active;
    public required string                  DocumentJson           { get; set; }  // canonical W3C JSON
    public DateTimeOffset                   CreatedAt              { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset                   UpdatedAt              { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset?                  DeactivatedAt          { get; set; }
}

/// <summary>Result of a W3C DID Document Resolution.</summary>
public record DidResolutionResult
{
    public required DidDocument?  Document         { get; set; }
    public required string        Did              { get; set; }
    public required bool          Found            { get; set; }
    public string?                ErrorCode        { get; set; }  // W3C error code
    public DateTimeOffset         ResolvedAt       { get; set; } = DateTimeOffset.UtcNow;
}

// ── Verifiable Credential models ───────────────────────────────────────────────

/// <summary>
/// A stored Verifiable Credential record. The full JWT is stored in JwtEncoded.
/// VcHash is Blake3 of the JWT — used as a content fingerprint.
/// AutoExpireIfStale() is called on all read paths.
/// </summary>
public record VcRecord
{
    public required string        VcId         { get; set; }  // jti claim
    public required string        IssuerDid    { get; set; }
    public required string        SubjectDid   { get; set; }
    public required List<string>  Types        { get; set; }  // List<T> for LiteDB
    public required string        VcHash       { get; set; }  // Blake3 hex
    public required string        JwtEncoded   { get; set; }
    public VcStatus               Status       { get; set;  } = VcStatus.Active;
    public DateTimeOffset         IssuedAt     { get; set; }
    public DateTimeOffset?        ExpiresAt    { get; set; }
    public DateTimeOffset?        RevokedAt    { get; set;  }
    public string?                RevokedReason{ get; set;  }
}

/// <summary>An immutable revocation event record.</summary>
public record RevocationEvent
{
    public required string        VcId        { get; set; }
    public required string        RevokedBy   { get; set; }
    public required string        Reason      { get; set; }
    public DateTimeOffset         RevokedAt   { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Result of a VC Document Resolution.</summary>
public record VcResolutionResult
{
    public required VcRecord?     Record       { get; set; }
    public required string        VcId         { get; set; }
    public required bool          Found        { get; set; }
    public VcStatus?              CurrentStatus{ get; set; }
    public DateTimeOffset         ResolvedAt   { get; set; } = DateTimeOffset.UtcNow;
}

// ── Ledger models ──────────────────────────────────────────────────────────────

/// <summary>
/// An append-only Merkle log entry. TxId is Blake3 of canonical payload JSON.
/// EntryType distinguishes transfer, registration, epoch transition, supply update, etc.
/// </summary>
public record LogEntry
{
    public required string        TxId        { get; set; }
    public required string        EntryType   { get; set; }
    public required string        PayloadJson { get; set; }
    public required string        MerkleHash  { get; set; }  // SHA-256 leaf hash
    public DateTimeOffset         CreatedAt   { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A signed Merkle tree head snapshot.</summary>
public record TreeHead
{
    public required string        RootHash    { get; set; }
    public required long          TreeSize    { get; set; }
    public required string        Signature   { get; set; }  // secp256k1 CESR
    public DateTimeOffset         SignedAt    { get; set; } = DateTimeOffset.UtcNow;
}

// ── Transfer models ────────────────────────────────────────────────────────────

/// <summary>A request to transfer SVRN7 between participants.</summary>
public record TransferRequest
{
    public required string        PayerDid    { get; set; }
    public required string        PayeeDid    { get; set; }
    public required long          AmountGrana { get; set; }
    public required string        Nonce       { get; set; }
    public required DateTimeOffset Timestamp  { get; set; }
    public required string        Signature   { get; set; }  // secp256k1 CESR over canonical JSON
    public string?                Memo        { get; set; }
}

/// <summary>
/// A cross-Society transfer order VC payload.
/// Issued by the originating Society; consumed by the receiving Society.
/// TransferId is the Blake3 idempotency key.
/// </summary>
public record TransferOrderCredential
{
    public required string        TransferId        { get; set; }  // Blake3 of canonical transfer JSON
    public required string        PayerDid          { get; set; }
    public required string        PayeeDid          { get; set; }
    public required long          AmountGrana       { get; set; }
    public required string        OriginSocietyDid  { get; set; }
    public required string        TargetSocietyDid  { get; set; }
    public required int           Epoch             { get; set; }
    public required string        Nonce             { get; set; }
    public required DateTimeOffset Timestamp        { get; set; }
    public required DateTimeOffset ExpiresAt        { get; set; }
}

/// <summary>
/// Receipt issued by the receiving Society confirming payee has been credited.
/// Links back to the TransferOrderCredential via TransferId.
/// </summary>
public record TransferReceiptCredential
{
    public required string        TransferId        { get; set; }
    public required string        PayeeDid          { get; set; }
    public required long          CreditedGrana     { get; set; }
    public required string        TargetSocietyDid  { get; set; }
    public required DateTimeOffset CreditedAt       { get; set; }
}

// ── Overdraft / endowment DIDComm message payloads ────────────────────────────

public record OverdraftDrawRequest
{
    public required string        SocietyDid        { get; set; }
    public required long          DrawAmountGrana   { get; set; }
    public required int           DrawCount         { get; set; }
    public required string        Reason            { get; set; }  // e.g. "CitizenRegistration"
    public required DateTimeOffset RequestedAt      { get; set; }
}

public record OverdraftDrawReceipt
{
    public required string        SocietyDid        { get; set; }
    public required long          DrawAmountGrana   { get; set; }
    public required string        TransferId        { get; set; }  // Merkle log TxId
    public required DateTimeOffset ApprovedAt       { get; set; }
}

// ── Registration requests ─────────────────────────────────────────────────────

public record RegisterCitizenRequest
{
    public required DidDocument   DidDocument       { get; set; }
    public byte[]                 PrivateKeyBytes   { get; set; } = [];
}

/// <summary>
/// Superset of RegisterCitizenRequest — explicitly names the Society this
/// citizen belongs to. Used by ISvrn7SocietyDriver only.
/// </summary>
public record RegisterCitizenInSocietyRequest
{
    public required DidDocument   DidDocument          { get; set; }
    public byte[]                 PrivateKeyBytes      { get; set; } = [];
    public required string        SocietyDid           { get; set; }
    public string?                PreferredMethodName  { get; set; }  // null = Society primary
}

public record RegisterSocietyRequest
{
    public required DidDocument   DidDocument           { get; set; }
    public byte[]                 PrivateKeyBytes       { get; set; } = [];
    public required string        SocietyName           { get; set; }
    public required long          DrawAmountGrana       { get; set; }
    public required long          OverdraftCeilingGrana { get; set; }
}

// ── Key pair ──────────────────────────────────────────────────────────────────

public record Svrn7KeyPair
{
    public required string        PublicKeyHex      { get; set; }
    public required byte[]        PrivateKeyBytes   { get; set; }
    public required KeyAlgorithm  Algorithm         { get; set; }

    public void ZeroPrivateKey() => Array.Clear(PrivateKeyBytes, 0, PrivateKeyBytes.Length);
}

// ── Operation result ──────────────────────────────────────────────────────────

public record OperationResult
{
    public bool    Success      { get; set; }
    public string? ErrorMessage { get; set; }
    public object? Payload      { get; set; }

    public static OperationResult Ok(object? payload = null)
        => new() { Success = true, Payload = payload };

    public static OperationResult Fail(string error)
        => new() { Success = false, ErrorMessage = error };
}

/// <summary>
/// A failed outbound DIDComm message persisted to the dead-letter outbox.
/// Stored in svrn7-msg.db (MsgLiteContext) in the DeadLetter collection.
/// Created by DIDCommMessageSwitchboard.DeliverOutboundAsync after retry
/// exhaustion. Operators can inspect and manually retry via tooling.
/// </summary>
public record DeadLetterRecord
{
    public required string Id            { get; set; }  // TDA resource DID URL
    public required string PeerEndpoint  { get; set; }  // target TDA endpoint
    public required string PackedMessage { get; set; }  // packed DIDComm payload
    public required string MessageType   { get; set; }  // DIDComm @type URI
    public DateTimeOffset  FailedAt      { get; set; } = DateTimeOffset.UtcNow;
    public int             AttemptCount  { get; set; }
    public string?         LastError     { get; set; }
    public bool            IsRetried     { get; set; } = false;
}

