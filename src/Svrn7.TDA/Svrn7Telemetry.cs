using System.Diagnostics;

namespace Svrn7.TDA;

/// <summary>
/// Central <see cref="ActivitySource"/> for SVRN7 TDA distributed tracing.
/// Compatible with OpenTelemetry — attach any OTEL exporter by subscribing to
/// the <c>Svrn7.TDA</c> source name. Calls are zero-cost when no listener is registered.
/// </summary>
public static class Svrn7Telemetry
{
    public const string SourceName    = "Svrn7.TDA";
    public const string SourceVersion = "1.0.0";

    public static readonly ActivitySource Source =
        new(SourceName, SourceVersion);

    // ── Activity names ────────────────────────────────────────────────────────
    public const string ActivityDispatch  = "didcomm.dispatch";
    public const string ActivityInvoke    = "didcomm.invoke";
    public const string ActivityDeliver   = "didcomm.deliver";

    // ── Tag names (follow OpenTelemetry messaging semconv where possible) ─────
    public const string TagMessageId      = "messaging.message_id";
    public const string TagMessageType    = "messaging.message_type";
    public const string TagAttemptCount   = "messaging.attempt_count";
    public const string TagLobeName       = "svrn7.lobe_name";
    public const string TagLobeEntrypoint = "svrn7.lobe_entrypoint";
    public const string TagOutcome        = "svrn7.outcome";
    public const string TagPeerEndpoint   = "svrn7.peer_endpoint";
}
