namespace Svrn7.TDA;

// ── TdaResourceAddress ────────────────────────────────────────────────────────
//
// Derived from: draft-herman-drn-resource-addressing-00.
//
// Builds and parses DID URL locators for records in TDA Data Storage databases.
// Form: did:drn:{networkId}/{db}/{type}/{key}
//
// Key type conventions (Epoch 0):
//   Identity-bearing records : citizen/society DID suffix (e.g., alice.alpha.svrn7.net)
//   Content-addressed records: Blake3 hex hash (64 chars) — utxo, logentry, treehead
//   Named records            : human-meaningful common name — schema
//   VC UUID records          : VC UUID string — vc
//   Surrogate-keyed records  : LiteDB ObjectId 24-char hex — msg, processedorder, nonce, revocation
//
// All segment values must be URL-safe without encoding (hex, alphanumeric, '.', '-').

/// <summary>
/// Builds and parses DID URL paths for TDA Data Storage database record addressing.
/// Derived from: draft-herman-drn-resource-addressing-00 (DSA 0.24 Epoch 0).
/// </summary>
public sealed class TdaResourceAddress
{
    // ── Database segment constants ────────────────────────────────────────────

    public static class Db
    {
        /// <summary>svrn7.db — wallets, UTXOs, citizens, societies, Merkle log, nonces.</summary>
        public const string Main    = "main";

        /// <summary>svrn7-inbox.db — inbox messages and processed order receipts.</summary>
        public const string Inbox   = "inbox";

        /// <summary>svrn7-dids.db — DID Document registry. Society TDA Only.</summary>
        public const string Dids    = "dids";

        /// <summary>svrn7-vcs.db — VC registry and revocation events. Society TDA Only.</summary>
        public const string Vcs     = "vcs";

        /// <summary>svrn7-schemas.db — JSON Schema registry. Society TDA Only.</summary>
        public const string Schemas = "schemas";
    }

    // ── Type segment constants ────────────────────────────────────────────────

    public static class Type
    {
        // main
        public const string Citizen       = "citizen";
        public const string Wallet        = "wallet";
        public const string Utxo          = "utxo";
        public const string Society       = "society";
        public const string Membership    = "membership";
        public const string LogEntry      = "logentry";
        public const string TreeHead      = "treehead";
        public const string Nonce         = "nonce";
        public const string Overdraft     = "overdraft";
        public const string KeyBackup     = "keybak";

        // inbox
        public const string Message       = "msg";
        public const string ProcessedOrder= "processedorder";

        // dids
        public const string DidDocument   = "doc";

        // vcs
        public const string Vc            = "vc";
        public const string Revocation    = "revocation";

        // schemas
        public const string Schema        = "schema";
    }

    // ── Parsed components ─────────────────────────────────────────────────────

    /// <summary>Owning TDA network identifier (e.g., "alpha.svrn7.net").</summary>
    public string NetworkId { get; }

    /// <summary>Database segment (e.g., "inbox", "main", "dids", "vcs", "schemas").</summary>
    public string Database  { get; }

    /// <summary>Type segment (e.g., "msg", "citizen", "logentry").</summary>
    public string RecordType{ get; }

    /// <summary>Natural key segment — ObjectId hex, Blake3 hash, DID suffix, common name, or VC UUID.</summary>
    public string Key       { get; }

    private TdaResourceAddress(string networkId, string db, string type, string key)
    {
        NetworkId  = networkId;
        Database   = db;
        RecordType = type;
        Key        = key;
    }

    // ── Build ─────────────────────────────────────────────────────────────────
    // All builders delegate to Svrn7.Core.TdaResourceId — single source of truth.
    // TdaResourceAddress adds typed constants, parse logic, and key extraction
    // helpers on top of the Core builder.

    /// <summary>Constructs a DID URL from components. Delegates to <see cref="Svrn7.Core.TdaResourceId"/>.</summary>
    public static string Build(string networkId, string db, string type, string key)
        => Svrn7.Core.TdaResourceId.Build(networkId, db, type, key);

    // ── Typed builders ────────────────────────────────────────────────────────

    /// <summary>Inbox message DID URL. Key = LiteDB ObjectId 24-char hex.</summary>
    public static string InboxMessage(string networkId, string objectIdHex)
        => Svrn7.Core.TdaResourceId.InboxMessage(networkId, objectIdHex);

    /// <summary>Processed order DID URL. Key = LiteDB ObjectId 24-char hex.</summary>
    public static string ProcessedOrder(string networkId, string objectIdHex)
        => Svrn7.Core.TdaResourceId.ProcessedOrder(networkId, objectIdHex);

    /// <summary>Citizen record DID URL. Key = citizen DID suffix.</summary>
    public static string Citizen(string networkId, string citizenDidSuffix)
        => Svrn7.Core.TdaResourceId.Citizen(networkId, citizenDidSuffix);

    /// <summary>Wallet DID URL. Key = owner DID suffix.</summary>
    public static string Wallet(string networkId, string ownerDidSuffix)
        => Svrn7.Core.TdaResourceId.Wallet(networkId, ownerDidSuffix);

    /// <summary>UTXO DID URL. Key = Blake3 hex hash (64 chars).</summary>
    public static string Utxo(string networkId, string blake3Hex)
        => Svrn7.Core.TdaResourceId.Utxo(networkId, blake3Hex);

    /// <summary>Society record DID URL. Key = society DID suffix.</summary>
    public static string Society(string networkId, string societyDidSuffix)
        => Svrn7.Core.TdaResourceId.Society(networkId, societyDidSuffix);

    /// <summary>Membership DID URL. Key = citizen DID suffix.</summary>
    public static string Membership(string networkId, string citizenDidSuffix)
        => Svrn7.Core.TdaResourceId.Membership(networkId, citizenDidSuffix);

    /// <summary>Merkle log entry DID URL. Key = Blake3 hex hash of entry content.</summary>
    public static string LogEntry(string networkId, string blake3Hex)
        => Svrn7.Core.TdaResourceId.LogEntry(networkId, blake3Hex);

    /// <summary>Signed Merkle Tree Head DID URL. Key = Blake3 hex hash.</summary>
    public static string TreeHead(string networkId, string blake3Hex)
        => Svrn7.Core.TdaResourceId.TreeHead(networkId, blake3Hex);

    /// <summary>DID Document DID URL. Key = subject DID suffix.</summary>
    public static string DidDocument(string networkId, string subjectDidSuffix)
        => Svrn7.Core.TdaResourceId.DidDocument(networkId, subjectDidSuffix);

    /// <summary>VC record DID URL. Key = VC UUID string.</summary>
    public static string Vc(string networkId, string vcId)
        => Svrn7.Core.TdaResourceId.Vc(networkId, vcId);

    /// <summary>Schema DID URL. Key = common name (alphanumeric, hyphens, dots).</summary>
    public static string Schema(string networkId, string schemaName)
    {
        ValidateSchemaName(schemaName);
        return Svrn7.Core.TdaResourceId.Schema(networkId, schemaName);
    }

    // ── Parse ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a TDA resource DID URL into its four components.
    /// Returns null if the string is not a conformant TDA resource DID URL.
    /// </summary>
    public static TdaResourceAddress? TryParse(string didUrl)
    {
        if (string.IsNullOrWhiteSpace(didUrl)) return null;
        if (!didUrl.StartsWith("did:drn:", StringComparison.Ordinal)) return null;

        // Split on '/' to separate authority from path segments
        var slashIdx = didUrl.IndexOf('/');
        if (slashIdx < 0) return null;  // no path — this is a bare DID, not a DID URL

        var authority = didUrl[8..slashIdx];  // strip "did:drn:"
        var path      = didUrl[(slashIdx + 1)..];
        var parts     = path.Split('/');

        if (parts.Length != 3) return null;  // must be exactly {db}/{type}/{key}

        return new TdaResourceAddress(
            networkId: authority,
            db:        parts[0],
            type:      parts[1],
            key:       parts[2]);
    }

    /// <summary>
    /// Parses a TDA resource DID URL. Throws <see cref="FormatException"/> if invalid.
    /// </summary>
    public static TdaResourceAddress Parse(string didUrl)
        => TryParse(didUrl)
           ?? throw new FormatException(
               $"'{didUrl}' is not a conformant TDA resource DID URL " +
               "(expected: did:drn:{{networkId}}/{{db}}/{{type}}/{{key}}).");

    // ── Key extraction helpers ────────────────────────────────────────────────

    /// <summary>
    /// Extracts the LiteDB ObjectId from a surrogate-keyed DID URL (msg, processedorder, nonce, revocation).
    /// Throws if the key is not a valid 24-char ObjectId hex string.
    /// </summary>
    public LiteDB.ObjectId ToObjectId()
    {
        if (!LiteDB.ObjectId.TryParse(Key, out var oid))
            throw new InvalidOperationException(
                $"DID URL key '{Key}' is not a valid LiteDB ObjectId hex string.");
        return oid;
    }

    /// <summary>
    /// Returns the DID suffix key as a full did:drn DID string.
    /// Used for identity-bearing records (citizen, wallet, membership, society, doc).
    /// </summary>
    public string ToDidString() => $"did:drn:{Key}";

    /// <summary>
    /// Returns the Blake3 hex hash key.
    /// Used for content-addressed records (utxo, logentry, treehead).
    /// </summary>
    public string ToBlake3Hex() => Key;

    /// <summary>
    /// Returns the schema common name key.
    /// </summary>
    public string ToSchemaName() => Key;

    // ── DID URL string representation ─────────────────────────────────────────

    /// <summary>Returns the full DID URL string.</summary>
    public override string ToString()
        => Build(NetworkId, Database, RecordType, Key);

    // ── Validation ────────────────────────────────────────────────────────────

    private static void ValidateSchemaName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Schema name must not be empty.", nameof(name));

        foreach (var ch in name)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '.')
                throw new ArgumentException(
                    $"Schema name '{name}' contains invalid character '{ch}'. " +
                    "Only alphanumeric characters, hyphens, and dots are permitted.",
                    nameof(name));
        }
    }
}
