using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Svrn7.Core.Interfaces;
using Svrn7.DIDComm;
using Svrn7.Society;

namespace Svrn7.TDA;

// ── TdaOptions ────────────────────────────────────────────────────────────────

/// <summary>
/// Configuration for the TDA Host.
/// All options with <see cref="RequiredAttribute"/> must be supplied before startup.
/// Derived from: Citizen/Society Trusted Digital Assistant (Host) — DSA 0.24 Epoch 0 (PPML).
/// </summary>
public sealed class TdaOptions
{
    // ── Society identity ──────────────────────────────────────────────────────

    /// <summary>Society DID — e.g., "did:drn:alpha.svrn7.net".</summary>
    [Required]
    public string SocietyDid { get; init; } = string.Empty;

    /// <summary>
    /// Society Ed25519 messaging private key (raw 32 bytes).
    /// Used by KestrelListenerService for UnpackAsync (DIDComm V2 Messaging boundary).
    /// </summary>
    [Required]
    public byte[] SocietyMessagingPrivateKeyEd25519 { get; init; } = Array.Empty<byte>();

    // ── Network ───────────────────────────────────────────────────────────────

    /// <summary>Port for Kestrel HTTP/2 + mTLS inbound listener (default 8443).</summary>
    public int ListenPort { get; init; } = 8443;

    /// <summary>TLS certificate path (.pfx or .pem). Null = cleartext development mode.</summary>
    public string? TlsCertificatePath { get; init; }

    /// <summary>TLS certificate password (if .pfx). Null = no password.</summary>
    public string? TlsCertificatePassword { get; init; }

    /// <summary>
    /// Require mutual TLS (mTLS) — peer TDA must present a valid certificate.
    /// Default true. Set false only in development/test environments.
    /// </summary>
    public bool RequireMutualTls { get; init; } = true;

    /// <summary>
    /// Accept self-signed peer certificates. Development mode only.
    /// Never true in production.
    /// </summary>
    public bool AcceptSelfSignedPeerCertificates { get; init; } = false;

    // ── PowerShell Runspace Pool ──────────────────────────────────────────────

    /// <summary>
    /// Minimum runspaces in the pool (default 2 — Agent 1 coordinator + one task runspace).
    /// </summary>
    public int MinRunspaces { get; init; } = 2;

    /// <summary>
    /// Maximum runspaces. 0 = ProcessorCount × 2 (default).
    /// </summary>
    public int MaxRunspaces { get; init; } = 0;

    // ── LOBE configuration ────────────────────────────────────────────────────

    /// <summary>Path to lobes.config.json. Default: "./lobes/lobes.config.json".</summary>
    public string LobesConfigPath { get; init; } = "./lobes/lobes.config.json";

    // ── Data Storage databases ────────────────────────────────────────────────

    /// <summary>Path to svrn7-inbox.db (Long-Term Message Memory).</summary>
    public string InboxDbPath { get; init; } = "svrn7-inbox.db";
}

// ── SwitchboardHostedService ──────────────────────────────────────────────────
//
// Derived from: "DIDComm Message Switchboard" (hosted service wrapper) — DSA 0.24 Epoch 0.
//
// Runs the DIDCommMessageSwitchboard.RunAsync() drain loop as a .NET BackgroundService.
// The Switchboard itself contains the routing logic; this service owns the Task lifetime.

/// <summary>
/// BackgroundService wrapper that runs the <see cref="DIDCommMessageSwitchboard"/>
/// drain loop for the lifetime of the TDA Host.
/// Derived from: DIDComm Message Switchboard — DSA 0.24 Epoch 0 (PPML).
/// </summary>
public sealed class SwitchboardHostedService : BackgroundService
{
    private readonly DIDCommMessageSwitchboard _switchboard;
    private readonly RunspacePoolManager       _pool;
    private readonly ILogger<SwitchboardHostedService> _log;

    public SwitchboardHostedService(
        DIDCommMessageSwitchboard          switchboard,
        RunspacePoolManager                pool,
        ILogger<SwitchboardHostedService>  log)
    {
        _switchboard = switchboard;
        _pool        = pool;
        _log         = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start the Runspace Pool before the Switchboard begins dispatching.
        _pool.Start();
        _log.LogInformation("SwitchboardHostedService: RunspacePool started.");

        await _switchboard.RunAsync(stoppingToken);
    }
}

// ── TDA DI Extensions ─────────────────────────────────────────────────────────

/// <summary>
/// Registers all TDA Host services for the five Critical DSA 0.24 components.
///
/// Registration order:
///   1.  TdaOptions
///   2.  IMemoryCache (in-process hot cache — Data Access element — DSA 0.24)
///   3.  Svrn7RunspaceContext ($SVRN7 session variable — all runspaces)
///   4.  LobeManager (LOBE loader — eager + JIT)
///   5.  RunspacePoolManager (PowerShell Runspace Pool lifecycle)
///   6.  DIDCommMessageSwitchboard (sole inbox reader + outbound queue)
///   7.  SwitchboardHostedService (drain loop BackgroundService)
///   8.  KestrelListenerService (POST /didcomm, HTTP/2 + mTLS)
///
/// Call after AddSvrn7Society() in Program.cs.
/// </summary>
public static class TdaServiceCollectionExtensions
{
    public static IServiceCollection AddSvrn7Tda(
        this IServiceCollection services,
        Action<TdaOptions>      configure)
    {
        // 1. TDA options
        services.AddOptions<TdaOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // 2. IMemoryCache
        // Derived from: "IMemoryCache" (Data Access element) — DSA 0.24 Epoch 0.
        services.AddSingleton<IMemoryCache>(
            _ => new MemoryCache(new MemoryCacheOptions()));

        // 3. Svrn7RunspaceContext ($SVRN7)
        // Derived from: "$SVRN7 session variable" — DSA 0.24 Epoch 0.
        services.AddSingleton<Svrn7RunspaceContext>(sp =>
        {
            var opts   = sp.GetRequiredService<IOptions<TdaOptions>>().Value;
            var driver = sp.GetRequiredService<ISvrn7SocietyDriver>();
            var inbox  = sp.GetRequiredService<IInboxStore>();
            var cache  = sp.GetRequiredService<IMemoryCache>();
            var orders = sp.GetRequiredService<IProcessedOrderStore>();
            return new Svrn7RunspaceContext(driver, inbox, cache, orders,
                initialEpoch: Svrn7.Core.Svrn7Constants.Epochs.Endowment);
        });

        // 4. LobeManager
        // Derived from: "LobeManager" (LOBE layer) — DSA 0.24 Epoch 0.
        services.AddSingleton<LobeManager>();

        // 5. RunspacePoolManager
        // Derived from: "PowerShell Runspace Pool" — DSA 0.24 Epoch 0.
        services.AddSingleton<RunspacePoolManager>();

        // 6. HttpClient (named "didcomm") — outbound DIDComm delivery to peer TDAs.
        // Derived from: "HTTP Listener/Sender (HTTPClient)" outbound path — DSA 0.24.
        // Polly retry: exponential backoff, 3 attempts, 500ms base delay.
        services.AddHttpClient("didcomm", client =>
        {
            client.DefaultRequestVersion = new System.Version(2, 0);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // IOutboxStore — dead-letter outbox for failed outbound messages.
        services.TryAddSingleton<Svrn7.Core.Interfaces.IOutboxStore>(sp =>
            new Svrn7.Society.LiteOutboxStore(
                sp.GetRequiredService<Svrn7.Store.InboxLiteContext>()));

        // DIDCommMessageSwitchboard — sole inbox reader + outbound delivery.
        // LobeManager injected for dynamic protocol registry lookup.
        // Derived from: "DIDComm Message Switchboard" — DSA 0.24 Epoch 0.
        services.AddSingleton<DIDCommMessageSwitchboard>(sp =>
            new DIDCommMessageSwitchboard(
                sp.GetRequiredService<Svrn7RunspaceContext>(),
                sp.GetRequiredService<RunspacePoolManager>(),
                sp.GetRequiredService<IInboxStore>(),
                sp.GetRequiredService<Svrn7.Core.Interfaces.IOutboxStore>(),
                sp.GetRequiredService<LobeManager>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TdaOptions>>(),
                sp.GetRequiredService<ILogger<DIDCommMessageSwitchboard>>()));

        // 7. SwitchboardHostedService (drain loop)
        services.AddHostedService<SwitchboardHostedService>();

        // 8. KestrelListenerService (POST /didcomm, HTTP/2 + mTLS)
        // Derived from: "HTTP Listener/Sender (HTTPClient)" — DSA 0.24 Epoch 0.
        services.AddHostedService<KestrelListenerService>();

        return services;
    }
}
