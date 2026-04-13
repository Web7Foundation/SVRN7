using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Svrn7.Society;
using Svrn7.TDA;

// ── Web 7.0 Trusted Digital Assistant (TDA) — Console App Entry Point ────────
//
// Derived from: Citizen/Society Trusted Digital Assistant (Host) — DSA 0.24 Epoch 0 (PPML).
//
// Runtime: .NET 8 console app using Generic Host + Kestrel HTTP/2 + mTLS.
// Single inbound surface: POST /didcomm (KestrelListenerService).
// No gRPC. No public REST API. Closed TDA-to-TDA ecosystem.
//
// Startup sequence (matches DSA 0.24 derivation chain):
//   1.  AddSvrn7Society()     — full SVRN7 stack (driver, stores, DIDComm, resolvers)
//   2.  AddSvrn7Tda()         — TDA Host: IMemoryCache, $SVRN7, LobeManager,
//                               RunspacePoolManager, Switchboard, KestrelListenerService
//   3.  UseConsoleLifetime()  — SIGTERM / Ctrl-C graceful shutdown
//   4.  host.RunAsync()       — blocks until shutdown

var host = Host.CreateDefaultBuilder(args)
    .UseConsoleLifetime()
    .ConfigureLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddConsole();
    })
    .ConfigureServices((ctx, services) =>
    {
        // ── 1. SVRN7 Society stack ────────────────────────────────────────────
        // Derived from the SVRN7 LOBE (inside Agent 1 Runspace) — DSA 0.24.
        services.AddSvrn7Society(opts =>
        {
            // In production, load these from environment variables or a secrets manager.
            // These defaults are for development/test only.
            opts.SocietyDid                      = ctx.Configuration["Svrn7:SocietyDid"]
                                                   ?? "did:drn:alpha.svrn7.net";
            opts.FederationDid                   = ctx.Configuration["Svrn7:FederationDid"]
                                                   ?? "did:drn:foundation.svrn7.net";
            opts.Svrn7DbPath                     = ctx.Configuration["Svrn7:DbPath"]
                                                   ?? "svrn7.db";
            opts.DidsDbPath                      = ctx.Configuration["Svrn7:DidsDbPath"]
                                                   ?? "svrn7-dids.db";
            opts.VcsDbPath                       = ctx.Configuration["Svrn7:VcsDbPath"]
                                                   ?? "svrn7-vcs.db";
            opts.InboxDbPath                     = ctx.Configuration["Svrn7:InboxDbPath"]
                                                   ?? "svrn7-inbox.db";
            opts.SchemasDbPath                   = ctx.Configuration["Svrn7:SchemasDbPath"]
                                                   ?? "svrn7-schemas.db";
            opts.SocietyMessagingPrivateKeyEd25519 = Array.Empty<byte>(); // supplied at runtime
        });

        // Background services from Svrn7.Society (VC expiry, Merkle auto-sign).
        services.AddSvrn7SocietyBackgroundServices();

        // ── 2. TDA Host: five Critical DSA 0.24 components ───────────────────
        services.AddSvrn7Tda(opts =>
        {
            opts.SocietyDid                        = ctx.Configuration["Tda:SocietyDid"]
                                                     ?? "did:drn:alpha.svrn7.net";
            opts.SocietyMessagingPrivateKeyEd25519 = Array.Empty<byte>(); // supplied at runtime
            opts.ListenPort                        = int.Parse(
                                                     ctx.Configuration["Tda:ListenPort"] ?? "8443");
            opts.TlsCertificatePath                = ctx.Configuration["Tda:TlsCertPath"];
            opts.TlsCertificatePassword            = ctx.Configuration["Tda:TlsCertPassword"];
            opts.RequireMutualTls                  = bool.Parse(
                                                     ctx.Configuration["Tda:RequireMutualTls"] ?? "true");
            opts.AcceptSelfSignedPeerCertificates  = bool.Parse(
                                                     ctx.Configuration["Tda:AcceptSelfSigned"] ?? "false");
            opts.MinRunspaces                      = 2;
            opts.MaxRunspaces                      = 0; // default: ProcessorCount × 2
            opts.LobesConfigPath                   = ctx.Configuration["Tda:LobesConfigPath"]
                                                     ?? "./lobes/lobes.config.json";
        });
    })
    .Build();

await host.RunAsync();
