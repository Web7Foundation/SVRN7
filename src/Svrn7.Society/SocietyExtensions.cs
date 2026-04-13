using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Svrn7.Federation;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;
using Svrn7.DIDComm;
using Svrn7.Store;

namespace Svrn7.Society;

// ── LiteSocietyMembershipStore ────────────────────────────────────────────────

/// <summary>
/// ISocietyMembershipStore implementation backed by Svrn7LiteContext (svrn7.db).
/// Manages SocietyOverdraftRecord: creation, read, and update.
/// Records are never deleted — updates are in-place via LiteDB.Update().
/// </summary>
public sealed class LiteSocietyMembershipStore : ISocietyMembershipStore
{
    private readonly Svrn7LiteContext _ctx;
    public LiteSocietyMembershipStore(Svrn7LiteContext ctx) => _ctx = ctx;

    public Task StoreOverdraftAsync(SocietyOverdraftRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ctx.Overdrafts.Insert(record);
        return Task.CompletedTask;
    }

    public Task<SocietyOverdraftRecord?> GetOverdraftAsync(
        string societyDid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<SocietyOverdraftRecord?>(
            _ctx.Overdrafts.FindOne(o => o.SocietyDid == societyDid));
    }

    public Task UpdateOverdraftAsync(SocietyOverdraftRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ctx.Overdrafts.Update(record);
        return Task.CompletedTask;
    }
}

// ── Society DI Extensions ─────────────────────────────────────────────────────

public static class SocietyServiceCollectionExtensions
{
    /// <summary>
    /// Registers all SVRN7 Society-level services.
    ///
    /// This method is self-contained — it registers the full stack including
    /// the Federation base services, DIDComm, and Society-specific extensions.
    /// Do NOT call AddSvrn7() separately when using this method.
    ///
    /// Service registration order:
    ///   1. Svrn7SocietyOptions (validates on start)
    ///   2. All base Svrn7 services (same as AddSvrn7 but keyed to SocietyOptions)
    ///   3. DIDComm services (IDIDCommService → DIDCommPackingService)
    ///   4. Federation resolvers (IDidDocumentResolver → FederationDidDocumentResolver,
    ///                            IVcDocumentResolver  → FederationVcDocumentResolver)
    ///   5. ISocietyMembershipStore → LiteSocietyMembershipStore
    ///   6. ISvrn7SocietyDriver → Svrn7SocietyDriver
    ///   7. IDIDCommTransferHandler → DIDCommTransferHandler
    ///   8. IInboxStore → LiteInboxStore (svrn7-inbox.db)
    ///   9. DIDCommMessageProcessorService (background hosted service)
    /// </summary>
    public static IServiceCollection AddSvrn7Society(
        this IServiceCollection services,
        Action<Svrn7SocietyOptions> configure)
    {
        // 1. Society options
        services.AddOptions<Svrn7SocietyOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Forward SocietyOptions as base Svrn7Options so all base services resolve correctly.
        // TryAdd prevents duplicate registration if AddSvrn7() was called first.
        services.TryAddSingleton<IOptions<Svrn7Options>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>().Value;
            return Options.Create<Svrn7Options>(opts);
        });

        // 2. Database contexts (singletons — one file per process)
        services.TryAddSingleton<Svrn7LiteContext>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>().Value;
            return new Svrn7LiteContext(opts.Svrn7DbPath);
        });
        services.TryAddSingleton<DidRegistryLiteContext>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>().Value;
            return new DidRegistryLiteContext(opts.DidsDbPath);
        });
        services.TryAddSingleton<VcRegistryLiteContext>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>().Value;
            return new VcRegistryLiteContext(opts.VcsDbPath);
        });
        services.TryAddSingleton<FederationLiteContext>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>().Value;
            return new FederationLiteContext(opts.Svrn7DbPath);
        });
        services.TryAddSingleton<InboxLiteContext>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>().Value;
            return new InboxLiteContext(opts.InboxDbPath);
        });

        // 3. Core service registrations
        services.TryAddSingleton<Svrn7.Crypto.CryptoService>();
        services.TryAddSingleton<ICryptoService>(sp => sp.GetRequiredService<Svrn7.Crypto.CryptoService>());
        services.TryAddSingleton<IWalletStore>(sp =>
            new LiteWalletStore(sp.GetRequiredService<Svrn7LiteContext>()));
        services.TryAddSingleton<IIdentityRegistry>(sp =>
            new LiteIdentityRegistry(sp.GetRequiredService<Svrn7LiteContext>()));
        services.TryAddSingleton<IMerkleLog>(sp =>
            new Svrn7.Ledger.MerkleLog(
                sp.GetRequiredService<Svrn7LiteContext>(),
                sp.GetRequiredService<ICryptoService>()));
        services.TryAddSingleton<IDidDocumentRegistry>(sp =>
            new LiteDidDocumentRegistry(sp.GetRequiredService<DidRegistryLiteContext>()));
        services.TryAddSingleton<IVcRegistry>(sp =>
            new LiteVcRegistry(sp.GetRequiredService<VcRegistryLiteContext>()));
        services.TryAddSingleton<IFederationStore>(sp =>
            new LiteFederationStore(sp.GetRequiredService<FederationLiteContext>()));
        services.TryAddSingleton<IInboxStore>(sp =>
            new LiteInboxStore(
                sp.GetRequiredService<InboxLiteContext>(),
                sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LiteInboxStore>>()));
        services.TryAddSingleton<IProcessedOrderStore>(sp =>
            new LiteProcessedOrderStore(sp.GetRequiredService<InboxLiteContext>()));
        services.TryAddSingleton<ITransferNonceStore>(sp =>
            new LiteTransferNonceStore(sp.GetRequiredService<Svrn7LiteContext>()));
        services.TryAddSingleton<IVcService>(sp =>
            new Svrn7.Identity.VcService(sp.GetRequiredService<ICryptoService>()));
        services.TryAddSingleton<ISanctionsChecker, Svrn7.Identity.PassthroughSanctionsChecker>();

        // Transfer validator — Society-aware (8 steps including membership check)
        services.TryAddSingleton<ITransferValidator>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>().Value;
            return new Svrn7.Society.SocietyTransferValidator(
                sp.GetRequiredService<IWalletStore>(),
                sp.GetRequiredService<IIdentityRegistry>(),
                sp.GetRequiredService<ISanctionsChecker>(),
                sp.GetRequiredService<ICryptoService>(),
                sp.GetRequiredService<ITransferNonceStore>(),
                opts.SocietyDid,
                opts.FederationDid,
                currentEpoch: 0);
        });

        // 4. DIDComm services
        services.TryAddSingleton<IDIDCommService, DIDCommPackingService>();

        // 5. Federation-aware resolvers (replace base LocalDidDocumentResolver)
        // These are registered as concrete types AND as interfaces so they can be
        // injected where specifically needed.
        services.AddSingleton<FederationDidDocumentResolver>(sp =>
            new FederationDidDocumentResolver(
                sp.GetRequiredService<IDidDocumentRegistry>(),
                sp.GetRequiredService<IFederationStore>(),
                sp.GetRequiredService<IDIDCommService>(),
                sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>(),
                sp.GetRequiredService<ILogger<FederationDidDocumentResolver>>()));

        services.AddSingleton<FederationVcDocumentResolver>(sp =>
            new FederationVcDocumentResolver(
                new LiteVcDocumentResolver(sp.GetRequiredService<IVcRegistry>()),
                sp.GetRequiredService<IFederationStore>(),
                sp.GetRequiredService<IDIDCommService>(),
                sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>(),
                sp.GetRequiredService<ILogger<FederationVcDocumentResolver>>()));

        // Register as interfaces — FederationX resolvers take precedence over any base registration
        services.AddSingleton<IDidDocumentResolver>(sp =>
            sp.GetRequiredService<FederationDidDocumentResolver>());
        services.AddSingleton<IVcDocumentResolver>(sp =>
            sp.GetRequiredService<FederationVcDocumentResolver>());

        // 6. Federation-level ISvrn7Driver (used as inner by the Society driver)
        services.TryAddSingleton<ISvrn7Driver>(sp =>
            new Svrn7.Federation.Svrn7Driver(
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
                sp.GetRequiredService<ILogger<Svrn7.Federation.Svrn7Driver>>(),
                Array.Empty<byte>())); // Foundation private key supplied at runtime

        // 7. Society membership store
        services.TryAddSingleton<ISocietyMembershipStore>(sp =>
            new LiteSocietyMembershipStore(sp.GetRequiredService<Svrn7LiteContext>()));

        // 8. Society driver
        services.AddSingleton<ISvrn7SocietyDriver>(sp =>
            new Svrn7SocietyDriver(
                sp.GetRequiredService<ISvrn7Driver>(),
                sp.GetRequiredService<IIdentityRegistry>(),
                sp.GetRequiredService<IWalletStore>(),
                sp.GetRequiredService<IMerkleLog>(),
                sp.GetRequiredService<IVcService>(),
                sp.GetRequiredService<IVcRegistry>(),
                sp.GetRequiredService<ICryptoService>(),
                sp.GetRequiredService<ISocietyMembershipStore>(),
                sp.GetRequiredService<IDIDCommService>(),
                sp.GetRequiredService<IVcDocumentResolver>(),
                sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>(),
                sp.GetRequiredService<ILogger<Svrn7SocietyDriver>>()));

        // Schema Registry + Schema Resolver (Society TDA Only — DSA 0.24)
        // Derived from: Schema Registry (LiteDB) + Schema Resolver — DSA 0.24 Epoch 0.
        // PPML Conditional: instantiated only when AddSvrn7Society() is called.
        services.TryAddSingleton<SchemaLiteContext>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>().Value;
            return new SchemaLiteContext(opts.SchemasDbPath);
        });
        services.TryAddSingleton<ISchemaRegistry>(sp =>
            new LiteSchemaRegistry(
                sp.GetRequiredService<SchemaLiteContext>(),
                sp.GetRequiredService<ILogger<LiteSchemaRegistry>>()));
        services.TryAddSingleton<ISchemaResolver>(sp =>
            new LiteSchemaResolver(sp.GetRequiredService<ISchemaRegistry>()));

        // 9. DIDComm transfer handler
        services.AddSingleton<IDIDCommTransferHandler>(sp =>
            new DIDCommTransferHandler(
                sp.GetRequiredService<ISvrn7Driver>(),
                sp.GetRequiredService<IDIDCommService>(),
                sp.GetRequiredService<IVcService>(),
                sp.GetRequiredService<ICryptoService>(),
                sp.GetRequiredService<IProcessedOrderStore>(),
                sp.GetRequiredService<IOptions<Svrn7SocietyOptions>>(),
                sp.GetRequiredService<ILogger<DIDCommTransferHandler>>()));

        return services;
    }

    /// <summary>
    /// Registers the DIDCommMessageProcessorService background service.
    /// Call after AddSvrn7Society().
    /// </summary>
    public static IServiceCollection AddSvrn7SocietyBackgroundServices(
        this IServiceCollection services)
    {
        services.AddHostedService<DIDCommMessageProcessorService>();
        return services;
    }

    /// <summary>
    /// Registers an InMemorySanctionsChecker that allows blocking individual DIDs at runtime.
    /// Call after AddSvrn7Society() to override the default PassthroughSanctionsChecker.
    /// </summary>
    public static IServiceCollection AddSvrn7InMemorySanctionsChecker(
        this IServiceCollection services)
    {
        services.AddSingleton<Svrn7.Identity.InMemorySanctionsChecker>();
        services.AddSingleton<ISanctionsChecker>(sp =>
            sp.GetRequiredService<Svrn7.Identity.InMemorySanctionsChecker>());
        return services;
    }

    /// <summary>
    /// Registers a custom sanctions checker implementation.
    /// Call after AddSvrn7Society() to override the default PassthroughSanctionsChecker.
    /// </summary>
    public static IServiceCollection AddSvrn7SanctionsChecker<T>(
        this IServiceCollection services) where T : class, ISanctionsChecker
    {
        services.AddSingleton<ISanctionsChecker, T>();
        return services;
    }
}
