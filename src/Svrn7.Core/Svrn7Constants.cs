namespace Svrn7.Core;

/// <summary>
/// Protocol-level constants for SOVRONA (SVRN7) — the Shared Reserve Currency (SRC)
/// for the Web 7.0 digital ecosystem.
/// All monetary arithmetic is performed in grana. 1 SVRN7 = GranaPerSvrn7 grana.
/// </summary>
public static class Svrn7Constants
{
    // ── Monetary ─────────────────────────────────────────────────────────────
    /// <summary>Conversion factor: 1 SVRN7 = 1,000,000 grana.</summary>
    public const long GranaPerSvrn7 = 1_000_000L;

    /// <summary>
    /// Default citizen endowment in grana.
    /// Changed from 1,000 SVRN7 (10^9 grana) to 1,000 grana in v0.7.1.
    /// 1,000 grana = 0.001000 SVRN7.
    /// </summary>
    public const long CitizenEndowmentGrana = 1_000L;

    /// <summary>Citizen endowment expressed in SVRN7 for display only (0.001000 SVRN7).</summary>
    public const decimal CitizenEndowmentSvrn7Display =
        CitizenEndowmentGrana / (decimal)GranaPerSvrn7;

    /// <summary>Initial Federation total supply in SVRN7.</summary>
    public const long FederationInitialSupplySvrn7 = 1_000_000_000L;

    /// <summary>Initial Federation total supply in grana.</summary>
    public const long FederationInitialSupplyGrana = FederationInitialSupplySvrn7 * GranaPerSvrn7;

    // ── Protocol ──────────────────────────────────────────────────────────────
    /// <summary>Maximum memo field length in characters.</summary>
    public const int MaxMemoLength = 256;

    /// <summary>Maximum transfers allowed in a single batch.</summary>
    public const int MaxBatchSize = 100;

    /// <summary>Maximum age of a Merkle tree head before health check reports Degraded.</summary>
    public static readonly TimeSpan MaxTreeHeadAge = TimeSpan.FromHours(24);

    /// <summary>Transfer freshness window: ±10 minutes of server time.</summary>
    public static readonly TimeSpan TransferFreshnessWindow = TimeSpan.FromMinutes(10);

    /// <summary>Nonce replay protection window: 24 hours.</summary>
    public static readonly TimeSpan NonceReplayWindow = TimeSpan.FromHours(24);

    /// <summary>Default DID method dormancy period after deregistration.</summary>
    public static readonly TimeSpan DefaultDidMethodDormancyPeriod = TimeSpan.FromDays(30);

    /// <summary>Default overdraft draw timeout for DIDComm round-trip to Federation.</summary>
    public static readonly TimeSpan DefaultOverdraftDrawTimeout = TimeSpan.FromSeconds(30);

    // ── DID ───────────────────────────────────────────────────────────────────
    /// <summary>DID scheme prefix.</summary>
    public const string DidScheme = "did";

    /// <summary>Default DID method name for the Federation (overridden at startup).</summary>
    public const string DefaultDidMethodName = "drn";

    // ── CESR key prefixes ─────────────────────────────────────────────────────
    public const string CesrPrefixSecp256k1 = "0B";
    public const string CesrPrefixEd25519   = "0D";

    // ── Multicodec prefixes ───────────────────────────────────────────────────
    public static readonly byte[] MulticodecSecp256k1 = { 0xe7, 0x01 };
    public static readonly byte[] MulticodecEd25519   = { 0xed, 0x01 };

    // ── OTel meter name ───────────────────────────────────────────────────────
    public const string MeterName = "Svrn7";

    // ── DIDComm protocol URIs (svrn7.net) ─────────────────────────────────────
    public static class Protocols
    {
        public const string TransferRequest       = "did:drn:svrn7.net/protocols/transfer/1.0/request";
        public const string TransferReceipt       = "did:drn:svrn7.net/protocols/transfer/1.0/receipt";
        public const string TransferOrder         = "did:drn:svrn7.net/protocols/transfer/1.0/order";
        public const string TransferOrderReceipt  = "did:drn:svrn7.net/protocols/transfer/1.0/order-receipt";
        public const string OverdraftDrawRequest  = "did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-request";
        public const string OverdraftDrawReceipt  = "did:drn:svrn7.net/protocols/endowment/1.0/overdraft-draw-receipt";
        public const string EndowmentTopUp        = "did:drn:svrn7.net/protocols/endowment/1.0/top-up";
        public const string SupplyUpdate          = "did:drn:svrn7.net/protocols/supply/1.0/update";
        public const string DidResolveRequest     = "did:drn:svrn7.net/protocols/did/1.0/resolve-request";
        public const string DidResolveResponse    = "did:drn:svrn7.net/protocols/did/1.0/resolve-response";
        public const string OnboardRequest        = "did:drn:svrn7.net/protocols/onboard/1.0/request";
        public const string OnboardReceipt        = "did:drn:svrn7.net/protocols/onboard/1.0/receipt";
        public const string InvoiceRequest        = "did:drn:svrn7.net/protocols/invoice/1.0/request";
        public const string InvoiceReceipt        = "did:drn:svrn7.net/protocols/invoice/1.0/receipt";
    }

    // ── Epoch values ──────────────────────────────────────────────────────────
    public static class Epochs
    {
        public const int Endowment        = 0;
        public const int EcosystemUtility = 1;
        public const int MarketIssuance   = 2;
    }
}

// ── TdaResourceId ─────────────────────────────────────────────────────────────
//
// Derived from: draft-herman-drn-resource-addressing-00.
//
// Builds TDA resource DID URL locators for Data Storage database records.
// Form: did:drn:{networkId}/{db}/{type}/{key}
//
// Zero dependencies — string concatenation only. Safe to reference from any
// layer including Svrn7.Core, Svrn7.Store, Svrn7.Society, and Svrn7.TDA.
//
// Network ID is the method-specific identifier of the owning TDA DID:
//   SocietyDid = "did:drn:alpha.svrn7.net"  →  NetworkId = "alpha.svrn7.net"

/// <summary>
/// Builds and parses TDA resource DID URL locators for Data Storage database records.
/// Derived from: draft-herman-drn-resource-addressing-00.
/// </summary>
public static class TdaResourceId
{
    // ── Build ─────────────────────────────────────────────────────────────────

    /// <summary>Inbox message DID URL. Key = LiteDB ObjectId 24-char hex.</summary>
    /// <summary>Constructs a DID URL from arbitrary components.</summary>
    public static string Build(string networkId, string db, string type, string key)
        => $"did:drn:{networkId}/{db}/{type}/{key}";

    public static string InboxMessage(string networkId, string objectIdHex)
        => $"did:drn:{networkId}/inbox/msg/{objectIdHex}";

    /// <summary>Processed order DID URL. Key = LiteDB ObjectId 24-char hex.</summary>
    public static string ProcessedOrder(string networkId, string objectIdHex)
        => $"did:drn:{networkId}/inbox/processedorder/{objectIdHex}";

    /// <summary>Citizen record DID URL. Key = citizen DID suffix.</summary>
    public static string Citizen(string networkId, string citizenDidSuffix)
        => $"did:drn:{networkId}/main/citizen/{citizenDidSuffix}";

    /// <summary>Wallet DID URL. Key = owner DID suffix.</summary>
    public static string Wallet(string networkId, string ownerDidSuffix)
        => $"did:drn:{networkId}/main/wallet/{ownerDidSuffix}";

    /// <summary>UTXO DID URL. Key = Blake3 hex hash.</summary>
    public static string Utxo(string networkId, string blake3Hex)
        => $"did:drn:{networkId}/main/utxo/{blake3Hex}";

    /// <summary>Merkle log entry DID URL. Key = Blake3 hex hash of entry content.</summary>
    public static string LogEntry(string networkId, string blake3Hex)
        => $"did:drn:{networkId}/main/logentry/{blake3Hex}";

    /// <summary>Signed Merkle Tree Head DID URL. Key = Blake3 hex hash.</summary>
    public static string TreeHead(string networkId, string blake3Hex)
        => $"did:drn:{networkId}/main/treehead/{blake3Hex}";

    /// <summary>DID Document DID URL. Key = subject DID suffix.</summary>
    public static string DidDocument(string networkId, string subjectDidSuffix)
        => $"did:drn:{networkId}/dids/doc/{subjectDidSuffix}";

    /// <summary>VC record DID URL. Key = VC UUID string.</summary>
    public static string Vc(string networkId, string vcId)
        => $"did:drn:{networkId}/vcs/vc/{vcId}";

    /// <summary>Schema DID URL. Key = schema common name.</summary>
    public static string Schema(string networkId, string schemaName)
        => $"did:drn:{networkId}/schemas/schema/{schemaName}";

    // ── Parse ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the network ID from a TDA resource DID URL.
    /// "did:drn:alpha.svrn7.net/inbox/msg/5f43..." → "alpha.svrn7.net"
    /// </summary>
    public static string? ParseNetworkId(string didUrl)
    {
        if (!didUrl.StartsWith("did:drn:", StringComparison.Ordinal)) return null;
        var slashIdx = didUrl.IndexOf('/');
        return slashIdx < 0 ? null : didUrl[8..slashIdx];
    }

    /// <summary>
    /// Extracts the natural key (final path segment) from a TDA resource DID URL.
    /// "did:drn:alpha.svrn7.net/inbox/msg/5f43a2b1c8e9d7f012345678" → "5f43a2b1c8e9d7f012345678"
    /// </summary>
    public static string? ParseKey(string didUrl)
    {
        var lastSlash = didUrl.LastIndexOf('/');
        return lastSlash < 0 ? null : didUrl[(lastSlash + 1)..];
    }

    /// <summary>
    /// Extracts the network ID from a Society DID.
    /// "did:drn:alpha.svrn7.net" → "alpha.svrn7.net"
    /// </summary>
    public static string NetworkIdFromDid(string did)
        => did.StartsWith("did:drn:", StringComparison.Ordinal) ? did[8..] : did;
}
