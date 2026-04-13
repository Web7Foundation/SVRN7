using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Svrn7.Core;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;
using Svrn7.Crypto;
using Svrn7.Identity;
using Svrn7.Ledger;
using Svrn7.Store;

namespace Svrn7.Federation;

// ── DI Extensions ─────────────────────────────────────────────────────────────

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all SVRN7 Federation-level services.
    /// Call this in Program.cs before building the host.
    /// </summary>
    public static IServiceCollection AddSvrn7Federation(
        this IServiceCollection services,
        Action<Svrn7Options> configure)
    {
        services.AddOptions<Svrn7Options>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<Svrn7LiteContext>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7Options>>().Value;
            return new Svrn7LiteContext(opts.Svrn7DbPath);
        });
        services.AddSingleton<DidRegistryLiteContext>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7Options>>().Value;
            return new DidRegistryLiteContext(opts.DidsDbPath);
        });
        services.AddSingleton<VcRegistryLiteContext>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7Options>>().Value;
            return new VcRegistryLiteContext(opts.VcsDbPath);
        });
        services.AddSingleton<FederationLiteContext>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7Options>>().Value;
            return new FederationLiteContext(opts.Svrn7DbPath);
        });

        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddSingleton<IWalletStore>(sp => new LiteWalletStore(sp.GetRequiredService<Svrn7LiteContext>()));
        services.AddSingleton<ITransferNonceStore>(sp => new LiteTransferNonceStore(sp.GetRequiredService<Svrn7LiteContext>()));
        services.AddSingleton<IIdentityRegistry>(sp => new LiteIdentityRegistry(sp.GetRequiredService<Svrn7LiteContext>()));
        services.AddSingleton<IMerkleLog>(sp => new MerkleLog(
            sp.GetRequiredService<Svrn7LiteContext>(),
            sp.GetRequiredService<ICryptoService>()));
        services.AddSingleton<IDidDocumentRegistry>(sp => new LiteDidDocumentRegistry(sp.GetRequiredService<DidRegistryLiteContext>()));
        services.AddSingleton<IVcRegistry>(sp => new LiteVcRegistry(sp.GetRequiredService<VcRegistryLiteContext>()));
        services.AddSingleton<IFederationStore>(sp => new LiteFederationStore(sp.GetRequiredService<FederationLiteContext>()));
        services.AddSingleton<IDidDocumentResolver>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7Options>>().Value;
            return new LocalDidDocumentResolver(
                sp.GetRequiredService<IDidDocumentRegistry>(),
                new[] { opts.DidMethodName });
        });
        services.AddSingleton<IVcDocumentResolver>(sp =>
            new LiteVcDocumentResolver(sp.GetRequiredService<IVcRegistry>()));
        services.AddSingleton<IVcService, VcService>();
        services.AddSingleton<ISanctionsChecker, PassthroughSanctionsChecker>();
        services.AddSingleton<ITransferValidator>(sp => new TransferValidator(
            sp.GetRequiredService<IWalletStore>(),
            sp.GetRequiredService<IIdentityRegistry>(),
            sp.GetRequiredService<ISanctionsChecker>(),
            sp.GetRequiredService<ITransferNonceStore>(),
            sp.GetRequiredService<ICryptoService>(),
            0 /* initial epoch */));

        services.AddSingleton<ISvrn7Driver>(sp => new Svrn7Driver(
            sp.GetRequiredService<IWalletStore>(),
            sp.GetRequiredService<IIdentityRegistry>(),
            sp.GetRequiredService<IMerkleLog>(),
            sp.GetRequiredService<IVcService>(),
            sp.GetRequiredService<IVcRegistry>(),
            sp.GetRequiredService<ITransferValidator>(),
            sp.GetRequiredService<ISanctionsChecker>(),
            sp.GetRequiredService<ICryptoService>(),
            sp.GetRequiredService<IFederationStore>(),
            sp.GetRequiredService<IDidDocumentRegistry>(),
            sp.GetRequiredService<IDidDocumentResolver>(),
            sp.GetRequiredService<IVcDocumentResolver>(),
            sp.GetRequiredService<IOptions<Svrn7Options>>(),
            sp.GetRequiredService<ILogger<Svrn7Driver>>(),
            Array.Empty<byte>() /* foundation private key — supplied at runtime */));

        return services;
    }

    public static IServiceCollection AddSvrn7BackgroundServices(this IServiceCollection services)
    {
        services.AddHostedService<Svrn7BackgroundService>();
        return services;
    }

    public static IServiceCollection AddSvrn7FederationHealthCheck(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<Svrn7HealthCheck>("svrn7");
        return services;
    }

    public static IServiceCollection AddSvrn7InMemorySanctionsChecker(this IServiceCollection services)
    {
        services.AddSingleton<InMemorySanctionsChecker>();
        services.AddSingleton<ISanctionsChecker>(sp => sp.GetRequiredService<InMemorySanctionsChecker>());
        return services;
    }

    public static IServiceCollection AddSvrn7SanctionsChecker<T>(this IServiceCollection services)
        where T : class, ISanctionsChecker
    {
        services.AddSingleton<ISanctionsChecker, T>();
        return services;
    }
}

// ── Background service ────────────────────────────────────────────────────────

/// <summary>
/// Two periodic timer loops:
/// (1) VC expiry sweep — calls ExpireStaleCredentialsAsync hourly.
/// (2) Merkle auto-sign — calls SignMerkleTreeHeadAsync hourly.
/// Intervals configurable via Svrn7Options.BackgroundSweepInterval.
/// </summary>
public sealed class Svrn7BackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scope;
    private readonly IOptions<Svrn7Options> _opts;
    private readonly ILogger<Svrn7BackgroundService> _log;

    public Svrn7BackgroundService(
        IServiceScopeFactory scope,
        IOptions<Svrn7Options> opts,
        ILogger<Svrn7BackgroundService> log)
    {
        _scope = scope;
        _opts  = opts;
        _log   = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _opts.Value.BackgroundSweepInterval;
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scope.CreateAsyncScope();
                var driver = scope.ServiceProvider.GetRequiredService<ISvrn7Driver>();

                var expired = await driver.ExpireStaleVcsAsync(stoppingToken);
                if (expired > 0)
                    _log.LogInformation("VC expiry sweep: {Count} credentials expired", expired);

                await driver.SignMerkleTreeHeadAsync(stoppingToken);
                _log.LogDebug("Merkle tree head signed");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _log.LogError(ex, "Background service sweep failed");
            }
        }
    }
}

// ── Health check ──────────────────────────────────────────────────────────────

/// <summary>
/// Reports 11 data points. Status is Degraded if Merkle tree head is older than
/// MaxTreeHeadAge (24 hours by default).
/// </summary>
public sealed class Svrn7HealthCheck : IHealthCheck
{
    private readonly ISvrn7Driver _driver;
    public Svrn7HealthCheck(ISvrn7Driver driver) => _driver = driver;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        var data = new Dictionary<string, object>();
        try
        {
            data["epoch"]              = _driver.GetCurrentEpoch();
            data["merkle_log_size"]    = await _driver.GetLogSizeAsync(ct);
            data["did_registry_count"] = await _driver.DidRegistry.CountAsync(ct);
            data["vc_registry_total"]  = await _driver.VcRegistry.CountAsync(ct);

            var head = await _driver.GetLatestTreeHeadAsync(ct);
            if (head is null)
            {
                data["tree_head_age_seconds"] = -1;
                return HealthCheckResult.Degraded("No Merkle tree head has been signed yet.", data: data);
            }

            var age = (DateTimeOffset.UtcNow - head.SignedAt).TotalSeconds;
            data["tree_head_age_seconds"] = (int)age;
            data["tree_head_root"]        = head.RootHash[..16] + "...";

            if (age > Svrn7Constants.MaxTreeHeadAge.TotalSeconds)
                return HealthCheckResult.Degraded(
                    $"Merkle tree head is {age:0}s old (threshold {Svrn7Constants.MaxTreeHeadAge.TotalSeconds}s).",
                    data: data);

            return HealthCheckResult.Healthy("SVRN7 Federation services healthy.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SVRN7 health check threw an exception.", ex, data);
        }
    }
}
