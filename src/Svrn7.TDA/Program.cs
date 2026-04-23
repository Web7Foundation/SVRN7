using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        logging.SetMinimumLevel(LogLevel.Trace);
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

// ── Startup banner ────────────────────────────────────────────────────────────
{
    var cfg      = host.Services.GetRequiredService<IConfiguration>();
    var driver   = host.Services.GetRequiredService<Svrn7.Society.ISvrn7SocietyDriver>();
    var tdaOpts  = host.Services.GetRequiredService<IOptions<TdaOptions>>().Value;

    var rawVersion = typeof(Program).Assembly
                         .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                         ?.InformationalVersion
                     ?? typeof(Program).Assembly.GetName().Version?.ToString(3)
                     ?? "0.0.0";
    // Strip SemVer build metadata (git commit hash appended by the .NET SDK: "0.8.0+e542da3...")
    var version = rawVersion.Contains('+') ? rawVersion[..rawVersion.IndexOf('+')] : rawVersion;

    // ── LOBE / cmdlet counts (read descriptors directly — LobeManager not started yet) ──
    var lobesConfigPath = tdaOpts.LobesConfigPath;
    var lobeDir         = Path.GetDirectoryName(Path.GetFullPath(lobesConfigPath)) ?? AppContext.BaseDirectory;
    var lobeConfig      = File.Exists(lobesConfigPath)
        ? JsonSerializer.Deserialize<LobeConfig>(
              File.ReadAllText(lobesConfigPath),
              new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
          ?? new LobeConfig()
        : new LobeConfig();
    var descriptors = Directory.Exists(lobeDir)
        ? Directory.GetFiles(lobeDir, "*.lobe.json", SearchOption.AllDirectories)
              .Select(LobeDescriptor.LoadFromFile)
              .Where(d => d is not null)
              .Cast<LobeDescriptor>()
              .ToList()
        : new List<LobeDescriptor>();
    var totalProtocols = descriptors.Sum(d => d.Protocols.Count);
    var totalCmdlets   = descriptors.Sum(d => d.Cmdlets.Count);

    var federation = await driver.GetFederationAsync();
    var societies  = await driver.GetAllSocietiesAsync();
    var activeSocietyCount = societies.Count(s => s.IsActive);

    const string hr = "────────────────────────────────────────────────────────────────────────────────";
    Console.WriteLine(hr);
    Console.WriteLine($"  SVRN7 Trusted Digital Assistant (TDA)  v{version}");
    Console.WriteLine($"  Web 7.0 Foundation — https://svrn7.net");
    Console.WriteLine(hr);
    Console.WriteLine($"  Started     : {DateTimeOffset.Now.ToString("F")}");
    Console.WriteLine($"  Executable  : {Environment.ProcessPath ?? "(unknown)"}");
    Console.WriteLine($"  Runtime     : {RuntimeInformation.FrameworkDescription}");
    Console.WriteLine($"  OS          : {RuntimeInformation.OSDescription}");
    Console.WriteLine(hr);
    Console.WriteLine($"  Society DID : {cfg["Svrn7:SocietyDid"] ?? cfg["Tda:SocietyDid"] ?? "(not configured)"}");
    Console.WriteLine($"  Listen port : {cfg["Tda:ListenPort"] ?? "8443"}");
    Console.WriteLine($"  LOBEs       : {lobeConfig.Eager.Length} eager  {lobeConfig.Jit.Length} JIT  ({totalProtocols} protocols  {totalCmdlets} cmdlets)");
    // Print eager LOBE names, then JIT LOBE names, each on one indented line.
    var lobeNameOf = descriptors.ToDictionary(d => d.Lobe.Name, d => d);
    if (lobeConfig.Eager.Length > 0)
    {
        var eagerNames = lobeConfig.Eager
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Select(n => lobeNameOf.TryGetValue(n, out var d) ? d.Lobe.Name : n);
        Console.WriteLine($"    Eager     : {string.Join("  ", eagerNames)}");
    }
    if (lobeConfig.Jit.Length > 0)
    {
        var jitNames = lobeConfig.Jit
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Select(n => lobeNameOf.TryGetValue(n, out var d) ? d.Lobe.Name : n);
        Console.WriteLine($"    JIT       : {string.Join("  ", jitNames)}");
    }
    Console.WriteLine(hr);
    if (federation is not null)
    {
        Console.WriteLine($"  Federation  : {federation.FederationName}  ({federation.Did})");
        Console.WriteLine($"  Supply      : {federation.TotalSupplyGrana / 1_000_000m:N6} SVRN7  ({federation.TotalSupplyGrana:N0} grana)");
        Console.WriteLine($"  Epoch       : {driver.GetCurrentEpoch()}");
        Console.WriteLine($"  Societies   : {societies.Count} registered  ({activeSocietyCount} active)");
    }
    else
    {
        Console.WriteLine($"  Federation  : (not yet initialised — see DEBUG.md §E.0 to generate keys and POST federation/1.0/init to :{cfg["Tda:ListenPort"] ?? "8443"}/didcomm)");
        Console.WriteLine($"  Societies   : (not yet initialised — see DEBUG.md §B.1 to onboard the first society)");
    }
    Console.WriteLine(hr);
    Console.WriteLine();
}

await host.RunAsync();
