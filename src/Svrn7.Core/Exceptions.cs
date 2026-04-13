namespace Svrn7.Core.Exceptions;

/// <summary>Base exception for all SVRN7 domain exceptions.</summary>
public abstract class Svrn7Exception : Exception
{
    protected Svrn7Exception(string message) : base(message) { }
    protected Svrn7Exception(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when a transfer cannot proceed due to insufficient UTXO balance.</summary>
public sealed class InsufficientBalanceException : Svrn7Exception
{
    public long AvailableGrana { get; }
    public long RequiredGrana  { get; }
    public InsufficientBalanceException(long available, long required)
        : base($"Insufficient balance: available {available} grana, required {required} grana.")
    {
        AvailableGrana = available;
        RequiredGrana  = required;
    }
}

/// <summary>Thrown when a transfer violates the current epoch's transfer rules.</summary>
public sealed class EpochViolationException : Svrn7Exception
{
    public int    CurrentEpoch { get; }
    public string ViolationType{ get; }
    public EpochViolationException(int epoch, string type, string detail)
        : base($"Epoch {epoch} violation [{type}]: {detail}")
    {
        CurrentEpoch  = epoch;
        ViolationType = type;
    }
}

/// <summary>Thrown when a DID is malformed, unresolvable, or deactivated.</summary>
public sealed class InvalidDidException : Svrn7Exception
{
    public string Did { get; }
    public InvalidDidException(string did, string reason)
        : base($"Invalid DID '{did}': {reason}") => Did = did;
}

/// <summary>Thrown when a transfer nonce has already been used within the replay window.</summary>
public sealed class NonceReplayException : Svrn7Exception
{
    public string Nonce { get; }
    public NonceReplayException(string nonce)
        : base($"Nonce '{nonce}' has already been used within the 24-hour replay window.")
        => Nonce = nonce;
}

/// <summary>Thrown when a transfer timestamp is outside the ±10 minute freshness window.</summary>
public sealed class StaleTransferException : Svrn7Exception
{
    public StaleTransferException(DateTimeOffset timestamp, DateTimeOffset serverTime)
        : base($"Transfer timestamp {timestamp:O} is outside the ±10 minute window of server time {serverTime:O}.") { }
}

/// <summary>Thrown when a payer or payee is on the sanctions list.</summary>
public sealed class SanctionedPartyException : Svrn7Exception
{
    public string Did { get; }
    public SanctionedPartyException(string did)
        : base($"Party '{did}' is sanctioned and cannot participate in transfers.") => Did = did;
}

/// <summary>Thrown when a secp256k1 or Ed25519 signature fails verification.</summary>
public sealed class SignatureVerificationException : Svrn7Exception
{
    public SignatureVerificationException(string context)
        : base($"Signature verification failed: {context}") { }
}

/// <summary>Thrown when a requested entity (citizen, society, DID, VC) is not found.</summary>
public sealed class NotFoundException : Svrn7Exception
{
    public string EntityType { get; }
    public string Key        { get; }
    public NotFoundException(string entityType, string key)
        : base($"{entityType} '{key}' was not found.")
    {
        EntityType = entityType;
        Key        = key;
    }
}

/// <summary>Thrown when a citizen endowment operation is invalid.</summary>
public sealed class EndowmentException : Svrn7Exception
{
    public EndowmentException(string detail) : base($"Endowment error: {detail}") { }
}

/// <summary>Thrown when a Merkle log integrity check fails.</summary>
public sealed class MerkleIntegrityException : Svrn7Exception
{
    public MerkleIntegrityException(string detail) : base($"Merkle integrity failure: {detail}") { }
}

/// <summary>Thrown when a VC is invalid, expired, or revoked.</summary>
public sealed class InvalidCredentialException : Svrn7Exception
{
    public string VcId { get; }
    public InvalidCredentialException(string vcId, string reason)
        : base($"VC '{vcId}' is invalid: {reason}") => VcId = vcId;
}

/// <summary>Thrown when a double-spend is detected.</summary>
public sealed class DoubleSpendException : Svrn7Exception
{
    public string UtxoId { get; }
    public DoubleSpendException(string utxoId)
        : base($"UTXO '{utxoId}' has already been spent.") => UtxoId = utxoId;
}

/// <summary>Thrown when a configuration value is missing or invalid.</summary>
public sealed class ConfigurationException : Svrn7Exception
{
    public ConfigurationException(string detail) : base($"Configuration error: {detail}") { }
}

/// <summary>
/// Thrown when a Society's overdraft ceiling is reached.
/// New citizen registration is blocked until the Federation tops up.
/// </summary>
public sealed class SocietyEndowmentDepletedException : Svrn7Exception
{
    public string SocietyDid          { get; }
    public long   TotalOverdrawnGrana { get; }
    public long   CeilingGrana        { get; }
    public SocietyEndowmentDepletedException(string societyDid, long overdrawn, long ceiling)
        : base($"Society '{societyDid}' has reached its overdraft ceiling ({ceiling} grana). " +
               $"Currently overdrawn: {overdrawn} grana. Registration blocked until Federation tops up.")
    {
        SocietyDid          = societyDid;
        TotalOverdrawnGrana = overdrawn;
        CeilingGrana        = ceiling;
    }
}

/// <summary>
/// Thrown when a DIDComm round-trip to the Federation does not complete
/// within the configured timeout (e.g. overdraft draw request).
/// </summary>
public sealed class FederationUnavailableException : Svrn7Exception
{
    public string Operation { get; }
    public TimeSpan Timeout { get; }
    public FederationUnavailableException(string operation, TimeSpan timeout)
        : base($"Federation did not respond to '{operation}' within {timeout.TotalSeconds:0}s.")
    {
        Operation = operation;
        Timeout   = timeout;
    }
}

/// <summary>
/// Thrown when attempting to register a DID method name that is currently
/// active under another Society.
/// </summary>
public sealed class DuplicateDidMethodException : Svrn7Exception
{
    public string MethodName           { get; }
    public string CurrentOwnerSocietyDid{ get; }
    public DuplicateDidMethodException(string methodName, string ownerDid)
        : base($"DID method name '{methodName}' is already registered to Society '{ownerDid}'.")
    {
        MethodName            = methodName;
        CurrentOwnerSocietyDid= ownerDid;
    }
}

/// <summary>
/// Thrown when attempting to register a DID method name that is in its
/// dormancy period following deregistration.
/// </summary>
public sealed class DormantDidMethodException : Svrn7Exception
{
    public string        MethodName   { get; }
    public DateTimeOffset DormantUntil{ get; }
    public DormantDidMethodException(string methodName, DateTimeOffset dormantUntil)
        : base($"DID method name '{methodName}' is dormant until {dormantUntil:O}.")
    {
        MethodName    = methodName;
        DormantUntil  = dormantUntil;
    }
}

/// <summary>
/// Thrown when attempting to issue a new DID under a method name that is no
/// longer active for this Society (has been deregistered).
/// </summary>
public sealed class DeregisteredDidMethodException : Svrn7Exception
{
    public string        MethodName     { get; }
    public DateTimeOffset DeregisteredAt{ get; }
    public DeregisteredDidMethodException(string methodName, DateTimeOffset deregisteredAt)
        : base($"DID method name '{methodName}' was deregistered at {deregisteredAt:O} " +
               "and cannot be used to issue new DIDs.")
    {
        MethodName      = methodName;
        DeregisteredAt  = deregisteredAt;
    }
}

/// <summary>
/// Thrown when attempting to deregister a Society's primary DID method name.
/// The primary method name is immutable — it is the Society's identity anchor.
/// </summary>
public sealed class PrimaryDidMethodException : Svrn7Exception
{
    public string MethodName { get; }
    public PrimaryDidMethodException(string methodName)
        : base($"DID method name '{methodName}' is the primary method for this Society and cannot be deregistered.")
        => MethodName = methodName;
}

/// <summary>
/// Thrown when a transfer attempts to use a DID method name whose DIDs
/// are not resolvable at the target Society or Federation.
/// </summary>
public sealed class UnresolvableDidException : Svrn7Exception
{
    public string Did        { get; }
    public string MethodName { get; }
    public UnresolvableDidException(string did, string methodName)
        : base($"Cannot resolve DID '{did}' — method '{methodName}' has no registered resolver.")
    {
        Did        = did;
        MethodName = methodName;
    }
}
