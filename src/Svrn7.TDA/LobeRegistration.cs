using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Svrn7.TDA;

// ── LobeRegistration ──────────────────────────────────────────────────────────
//
// Derived from: LOBE descriptor format — draft-herman-parchment-programming-00.
//
// Parsed from {module-name}.lobe.json at startup (eager LOBEs) or at drop time
// (third-party LOBEs via FileSystemWatcher). Registered into the Switchboard's
// ConcurrentDictionary<string, LobeProtocolRegistration> keyed by @type URI.
//
// MCP alignment note (Epoch 0):
//   The cmdlets array with inputSchema/outputSchema mirrors MCP's Tool definition
//   (name, title, description, inputSchema, outputSchema, annotations). In a future
//   epoch, the TDA may expose registered LOBEs as MCP tools via a tools/list
//   interface — the LOBE descriptor becomes the MCP tool definition with no
//   translation needed.

// ── Top-level descriptor ──────────────────────────────────────────────────────

/// <summary>
/// Parsed representation of a LOBE descriptor file ({module-name}.lobe.json).
/// Derived from: draft-herman-parchment-programming-00, Section 9 (LOBE Registry).
/// </summary>
public sealed class LobeDescriptor
{
    [JsonPropertyName("lobe")]
    public LobeMetadata       Lobe        { get; init; } = new();

    [JsonPropertyName("protocols")]
    public List<LobeProtocol> Protocols   { get; init; } = new();

    [JsonPropertyName("cmdlets")]
    public List<LobeCmdlet>   Cmdlets     { get; init; } = new();

    [JsonPropertyName("dependencies")]
    public LobeDependencies   Dependencies{ get; init; } = new();

    [JsonPropertyName("ai")]
    public LobeAiMetadata     Ai          { get; init; } = new();

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Deserialises a LOBE descriptor from a JSON file path.
    /// Returns null and logs a warning if the file is missing or malformed.
    /// </summary>
    public static LobeDescriptor? LoadFromFile(string path)
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LobeDescriptor>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}

// ── Lobe metadata ─────────────────────────────────────────────────────────────

/// <summary>
/// Identity and provenance metadata for a LOBE module.
/// Mirrors MCP server-level metadata extended with SVRN7-specific fields.
/// </summary>
public sealed class LobeMetadata
{
    /// <summary>Unique LOBE identifier (reverse-domain style, e.g., "svrn7.email").</summary>
    [JsonPropertyName("id")]
    public string Id           { get; init; } = string.Empty;

    /// <summary>PowerShell module name (e.g., "Svrn7.Email").</summary>
    [JsonPropertyName("name")]
    public string Name         { get; init; } = string.Empty;

    /// <summary>Human-readable display name (e.g., "SVRN7 Email LOBE").</summary>
    [JsonPropertyName("title")]
    public string Title        { get; init; } = string.Empty;

    /// <summary>Full human-readable description of the LOBE's purpose and behaviour.</summary>
    [JsonPropertyName("description")]
    public string Description  { get; init; } = string.Empty;

    /// <summary>Semantic version (e.g., "0.7.1").</summary>
    [JsonPropertyName("version")]
    public string Version      { get; init; } = string.Empty;

    /// <summary>Author name.</summary>
    [JsonPropertyName("author")]
    public string Author       { get; init; } = string.Empty;

    /// <summary>Organisation name.</summary>
    [JsonPropertyName("organization")]
    public string Organization { get; init; } = string.Empty;

    /// <summary>LOBE website or documentation URI.</summary>
    [JsonPropertyName("website")]
    public string Website      { get; init; } = string.Empty;

    /// <summary>SPDX license identifier (e.g., "MIT").</summary>
    [JsonPropertyName("license")]
    public string License      { get; init; } = string.Empty;

    /// <summary>
    /// Minimum epoch required for this LOBE to be loaded.
    /// 0 = Epoch 0 (Endowment Phase). 1 = EcosystemUtility. 2 = MarketIssuance.
    /// </summary>
    [JsonPropertyName("epochRequired")]
    public int EpochRequired   { get; init; } = 0;

    /// <summary>
    /// PowerShell module file name (e.g., "Svrn7.Email.psm1").
    /// Resolved relative to the LOBE directory by LobeManager.
    /// </summary>
    [JsonPropertyName("module")]
    public string Module       { get; init; } = string.Empty;
}

// ── Protocol registration ─────────────────────────────────────────────────────

/// <summary>
/// Maps a DIDComm @type URI pattern to an entry-point cmdlet.
/// One LobeProtocol entry per @type URI the LOBE handles.
/// The Switchboard registers each entry into its ConcurrentDictionary.
/// </summary>
public sealed class LobeProtocol
{
    /// <summary>DIDComm @type URI (or URI prefix if match = "prefix").</summary>
    [JsonPropertyName("uri")]
    public string Uri          { get; init; } = string.Empty;

    /// <summary>Human-readable title for this protocol message type.</summary>
    [JsonPropertyName("title")]
    public string Title        { get; init; } = string.Empty;

    /// <summary>Full description of the protocol message type and expected body.</summary>
    [JsonPropertyName("description")]
    public string Description  { get; init; } = string.Empty;

    /// <summary>"inbound" | "outbound" | "bidirectional"</summary>
    [JsonPropertyName("direction")]
    public string Direction    { get; init; } = "inbound";

    /// <summary>
    /// URI matching strategy: "prefix" (default) or "exact".
    /// "prefix": any @type starting with Uri is routed to this registration.
    /// "exact":  only the exact @type value matches.
    /// </summary>
    [JsonPropertyName("match")]
    public string Match        { get; init; } = "prefix";

    /// <summary>
    /// PowerShell cmdlet name to invoke for this @type URI.
    /// The Switchboard opens a runspace from the pool and runs:
    ///   Get-TdaMessage -Did $messageDid | {Entrypoint} | Send-TdaMessage
    /// </summary>
    [JsonPropertyName("entrypoint")]
    public string Entrypoint   { get; init; } = string.Empty;

    /// <summary>Minimum epoch for this specific protocol URI. Default 0.</summary>
    [JsonPropertyName("epochRequired")]
    public int EpochRequired   { get; init; } = 0;
}

// ── Cmdlet definition ─────────────────────────────────────────────────────────

/// <summary>
/// Describes a single exported cmdlet in the LOBE module.
///
/// MCP alignment: mirrors MCP Tool definition (name, title, description,
/// inputSchema, outputSchema, annotations). inputSchema and outputSchema are
/// optional in Epoch 0. In a future epoch, these schemas enable AI-aided pipeline
/// composition and may be exposed via an MCP-compatible tools/list interface.
/// </summary>
public sealed class LobeCmdlet
{
    /// <summary>PowerShell cmdlet name (e.g., "Receive-TdaEmail"). Unique within the LOBE.</summary>
    [JsonPropertyName("name")]
    public string Name          { get; init; } = string.Empty;

    /// <summary>Human-readable display name (e.g., "Receive Email"). Mirrors MCP Tool.title.</summary>
    [JsonPropertyName("title")]
    public string Title         { get; init; } = string.Empty;

    /// <summary>Full description of what the cmdlet does, its inputs, and its outputs.</summary>
    [JsonPropertyName("description")]
    public string Description   { get; init; } = string.Empty;

    /// <summary>Minimum epoch required for this cmdlet. Default 0.</summary>
    [JsonPropertyName("epochRequired")]
    public int EpochRequired    { get; init; } = 0;

    /// <summary>Behavioural annotations. Mirrors MCP Tool.annotations.</summary>
    [JsonPropertyName("annotations")]
    public LobeCmdletAnnotations Annotations { get; init; } = new();

    /// <summary>
    /// JSON Schema (2020-12) describing the cmdlet's input parameters.
    /// Optional in Epoch 0. When present, enables AI pipeline composition
    /// and MCP tools/list compatibility in future epochs.
    /// Mirrors MCP Tool.inputSchema.
    /// </summary>
    [JsonPropertyName("inputSchema")]
    public JsonNode? InputSchema  { get; init; }

    /// <summary>
    /// JSON Schema (2020-12) describing the cmdlet's output.
    /// Optional in Epoch 0. When present, enables AI pipeline composition
    /// and MCP tools/list compatibility in future epochs.
    /// Mirrors MCP Tool.outputSchema.
    /// </summary>
    [JsonPropertyName("outputSchema")]
    public JsonNode? OutputSchema { get; init; }

    /// <summary>
    /// Illustrative pipeline usage string.
    /// Example: "Get-TdaMessage -Did $MessageDid | Receive-TdaEmail"
    /// </summary>
    [JsonPropertyName("pipelineExample")]
    public string? PipelineExample { get; init; }

    /// <summary>Example invocation string for documentation.</summary>
    [JsonPropertyName("example")]
    public string? Example        { get; init; }
}

// ── Cmdlet annotations ────────────────────────────────────────────────────────

/// <summary>
/// Behavioural hints for a LOBE cmdlet.
/// Mirrors MCP Tool.annotations, extended with SVRN7-specific fields.
/// These are informational in Epoch 0. In future epochs the Switchboard
/// and AI pipeline composer may enforce or use these constraints.
/// </summary>
public sealed class LobeCmdletAnnotations
{
    /// <summary>
    /// True if invoking this cmdlet multiple times with the same input is safe.
    /// Maps to MCP idempotentHint. Default null (unknown).
    /// </summary>
    [JsonPropertyName("idempotent")]
    public bool? Idempotent     { get; init; }

    /// <summary>
    /// True if this cmdlet writes to a Data Storage database or the DIDComm inbox.
    /// Maps to MCP readOnlyHint (inverted). Default null (unknown).
    /// </summary>
    [JsonPropertyName("modifiesState")]
    public bool? ModifiesState  { get; init; }

    /// <summary>Minimum epoch required for this cmdlet to function. Default 0.</summary>
    [JsonPropertyName("requiresEpoch")]
    public int RequiresEpoch    { get; init; } = 0;

    /// <summary>
    /// True if this cmdlet performs an irreversible action (e.g., transfer execution).
    /// Maps to MCP destructiveHint. Default false.
    /// </summary>
    [JsonPropertyName("destructive")]
    public bool Destructive     { get; init; } = false;

    /// <summary>
    /// Suggested position in a PowerShell pipeline.
    /// "source" = produces output, no pipeline input required.
    /// "transform" = receives pipeline input, produces pipeline output.
    /// "sink" = receives pipeline input, no useful pipeline output (e.g., Send-TdaMessage).
    /// null = position not specified.
    /// </summary>
    [JsonPropertyName("pipelinePosition")]
    public string? PipelinePosition { get; init; }
}

// ── Dependencies ──────────────────────────────────────────────────────────────

/// <summary>
/// LOBE-level dependencies. LobeManager resolves the dependency graph
/// and ensures all listed LOBEs are imported before this one.
/// </summary>
public sealed class LobeDependencies
{
    /// <summary>
    /// LOBE module names this LOBE depends on (e.g., ["Svrn7.Society"]).
    /// LobeManager resolves and imports these first.
    /// </summary>
    [JsonPropertyName("lobes")]
    public List<string> Lobes    { get; init; } = new();

    /// <summary>External NuGet or PS Gallery package dependencies (future use).</summary>
    [JsonPropertyName("packages")]
    public List<string> Packages { get; init; } = new();
}

// ── AI metadata ───────────────────────────────────────────────────────────────

/// <summary>
/// AI-legibility metadata for the LOBE.
/// Epoch 0: informational only — for human and AI developers reading the descriptor.
///
/// Future epoch note (documented, not yet implemented):
///   When AI-aided pipeline construction arrives in a future epoch, the cmdlets
///   array with inputSchema/outputSchema will be exactly what an AI needs to reason
///   about composability — the same way MCP tools enable AI reasoning about which
///   tools to chain. The TDA could expose registered LOBEs as MCP tools via a
///   tools/list interface; the LOBE descriptor becomes the MCP tool definition
///   with no translation needed.
/// </summary>
public sealed class LobeAiMetadata
{
    /// <summary>
    /// Epoch 0 note explaining the intended future use of this metadata block.
    /// Present on every descriptor to make the design intent visible.
    /// </summary>
    [JsonPropertyName("_note")]
    public string Note          { get; init; } = string.Empty;

    /// <summary>One-paragraph summary of what this LOBE does. Optimised for AI comprehension.</summary>
    [JsonPropertyName("summary")]
    public string Summary       { get; init; } = string.Empty;

    /// <summary>Concrete use cases for which an AI might select this LOBE.</summary>
    [JsonPropertyName("useCases")]
    public List<string> UseCases { get; init; } = new();

    /// <summary>
    /// Hints for pipeline composition. Describe how cmdlets chain with each other
    /// and with cmdlets from other LOBEs.
    /// </summary>
    [JsonPropertyName("compositionHints")]
    public List<string> CompositionHints { get; init; } = new();

    /// <summary>Known limitations relevant to an AI selecting or composing this LOBE.</summary>
    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; init; } = new();
}

// ── Switchboard registration record ──────────────────────────────────────────

/// <summary>
/// Denormalised record stored in the Switchboard's protocol registry.
/// One entry per LobeProtocol entry in the descriptor.
/// The Switchboard's ConcurrentDictionary is keyed by URI prefix or exact URI.
/// </summary>
public sealed record LobeProtocolRegistration(
    string LobeId,
    string LobeName,
    string ModulePath,
    string Entrypoint,
    string Match,          // "prefix" | "exact"
    int    EpochRequired
);
