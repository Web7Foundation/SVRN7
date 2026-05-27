using System.Collections.Concurrent;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Svrn7.TDA;

// ── LobeConfig ────────────────────────────────────────────────────────────────

/// <summary>
/// Deserialised representation of <c>lobes.config.json</c>.
/// Lists eager and JIT LOBEs by module filename.
/// </summary>
public sealed class LobeConfig
{
    public string[] Eager { get; init; } = [];
    public string[] Jit   { get; init; } = [];
}

// ── LobeManager ───────────────────────────────────────────────────────────────
//
// Derived from: "LobeManager" — DSA 0.24 Epoch 0 (PPML).
//
// Responsibilities:
//   1. Builds the shared InitialSessionState with eager LOBEs pre-imported.
//   2. RegisterFromDescriptor: parses .lobe.json and populates protocol registry.
//   3. EnsureLoadedAsync: JIT-imports a LOBE module on first use (idempotent).
//   4. FileSystemWatcher: hot-detects new .lobe.json files at runtime.
//   5. TryResolveProtocol: exact-match then longest-prefix-match lookup.

/// <summary>
/// Singleton that loads LOBE modules, maintains the protocol registry, and builds
/// the shared <see cref="InitialSessionState"/> for the PowerShell Runspace Pool.
/// Derived from: LobeManager — DSA 0.24 Epoch 0 (PPML).
/// </summary>
public sealed class LobeManager : IDisposable
{
    private readonly TdaOptions           _opts;
    private readonly Svrn7RunspaceContext _ctx;
    private readonly ILogger<LobeManager> _log;

    // Protocol registry — populated from .lobe.json descriptors.
    // Exact-match registry: keyed by full @type URI.
    // Prefix-match registry: keyed by URI prefix.
    // Lookup order: exact first, then longest prefix.
    private readonly ConcurrentDictionary<string, LobeProtocolRegistration>
        _exactRegistry  = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LobeProtocolRegistration>
        _prefixRegistry = new(StringComparer.OrdinalIgnoreCase);

    // Tracks which module paths have been imported in this process.
    private readonly ConcurrentDictionary<string, bool>
        _importedModules = new(StringComparer.OrdinalIgnoreCase);

    private InitialSessionState? _iss;
    private LobeConfig?          _config;
    private FileSystemWatcher?   _watcher;
    private bool                 _disposed;

    private string LobeBaseDir =>
        Path.GetDirectoryName(Path.GetFullPath(_opts.LobesConfigPath))
        ?? AppContext.BaseDirectory;

    public LobeManager(
        IOptions<TdaOptions>  opts,
        Svrn7RunspaceContext  ctx,
        ILogger<LobeManager>  log)
    {
        _opts = opts.Value;
        _ctx  = ctx;
        _log  = log;
    }

    // ── 1. BuildInitialSessionState ───────────────────────────────────────────

    /// <summary>
    /// Reads lobes.config.json, imports eager LOBEs, injects session variables,
    /// scans all *.lobe.json descriptors, and starts the FileSystemWatcher.
    /// Called once by RunspacePoolManager at startup.
    /// </summary>
    public InitialSessionState BuildInitialSessionState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _config = LoadLobeConfig();
        _log.LogInformation("LobeManager: {Eager} eager LOBE(s), {Jit} JIT LOBE(s).",
            _config.Eager.Length, _config.Jit.Length);

        // CreateDefault2() is minimal — built-in cmdlets like Write-Verbose are registered
        // but deferred to auto-import from $PSHOME, which doesn't exist in a NuGet-hosted
        // runspace. AddBuiltInCmdlets() pre-populates the ISS from SMA.dll via reflection
        // so no filesystem lookup is ever needed for built-in cmdlets.
        var iss = InitialSessionState.CreateDefault2();
        AddBuiltInCmdlets(iss);

        iss.Variables.Add(new SessionStateVariableEntry(
            "SVRN7", _ctx,
            "Svrn7RunspaceContext — SVRN7 driver, inbox, cache, epoch.",
            ScopedItemOptions.ReadOnly | ScopedItemOptions.AllScope));

        iss.Variables.Add(new SessionStateVariableEntry(
            "SVRN7_JIT_LOBES",
            _config.Jit.Select(ResolveLobePath).ToArray(),
            "Array of JIT LOBE module paths for on-demand Import-Module.",
            ScopedItemOptions.ReadOnly | ScopedItemOptions.AllScope));

        iss.Variables.Add(new SessionStateVariableEntry(
            "SVRN7_LOBES_DIR", LobeBaseDir,
            "Absolute path to the lobes directory — use instead of $PSScriptRoot in hosted runspaces.",
            ScopedItemOptions.ReadOnly | ScopedItemOptions.AllScope));

        foreach (var modulePath in _config.Eager)
        {
            var resolved = ResolveLobePath(modulePath);
            if (!File.Exists(resolved))
            {
                _log.LogWarning("LobeManager: eager LOBE not found — {Path}. Skipping.", resolved);
                continue;
            }
            iss.ImportPSModule(resolved);
            _importedModules[resolved] = true;
            _log.LogInformation("LobeManager: eager LOBE imported — {Path}", resolved);
        }

        _iss = iss;
        ScanDescriptors();
        StartFileSystemWatcher();
        return _iss;
    }

    // ── 2. RegisterFromDescriptor ─────────────────────────────────────────────

    /// <summary>
    /// Parses a .lobe.json descriptor file and registers all its protocol URI
    /// entries into the appropriate registry tier (exact or prefix).
    /// Resolves dependency graph before registering.
    /// Idempotent — re-registering an existing URI updates the entry.
    /// </summary>
    public void RegisterFromDescriptor(string descriptorPath)
    {
        var descriptor = LobeDescriptor.LoadFromFile(descriptorPath);
        if (descriptor is null)
        {
            _log.LogWarning("LobeManager: could not parse descriptor — {Path}.", descriptorPath);
            return;
        }

        if (descriptor.Lobe.EpochRequired > _ctx.CurrentEpoch)
        {
            _log.LogInformation(
                "LobeManager: LOBE '{Name}' requires Epoch {Req} (current {Cur}) — skipping.",
                descriptor.Lobe.Name, descriptor.Lobe.EpochRequired, _ctx.CurrentEpoch);
            return;
        }

        // Resolve module path relative to descriptor file location.
        var descriptorDir = Path.GetDirectoryName(descriptorPath) ?? LobeBaseDir;
        var modulePath    = Path.IsPathRooted(descriptor.Lobe.Module)
            ? descriptor.Lobe.Module
            : Path.GetFullPath(Path.Combine(descriptorDir, descriptor.Lobe.Module));

        // Resolve dependency graph first.
        foreach (var dep in descriptor.Dependencies.Lobes)
        {
            var depPath = Path.Combine(LobeBaseDir, $"{dep}.lobe.json");
            if (File.Exists(depPath) && !IsRegistered(dep))
            {
                _log.LogInformation("LobeManager: resolving dependency '{Dep}' for '{Name}'.",
                    dep, descriptor.Lobe.Name);
                RegisterFromDescriptor(depPath);
            }
        }

        int registered = 0;
        foreach (var proto in descriptor.Protocols)
        {
            if (proto.EpochRequired > _ctx.CurrentEpoch) continue;

            var reg = new LobeProtocolRegistration(
                descriptor.Lobe.Id,
                descriptor.Lobe.Name,
                modulePath,
                proto.Entrypoint,
                proto.Match,
                proto.EpochRequired);

            if (proto.Match.Equals("exact", StringComparison.OrdinalIgnoreCase))
                _exactRegistry[proto.Uri] = reg;
            else
                _prefixRegistry[proto.Uri] = reg;

            registered++;
            _log.LogDebug("LobeManager: [{Match}] '{Uri}' → {EP} ({Name})",
                proto.Match, proto.Uri, proto.Entrypoint, descriptor.Lobe.Name);
        }

        if (registered > 0)
            _log.LogInformation(
                "LobeManager: LOBE '{Name}' v{Ver} — {N} protocol(s) registered.",
                descriptor.Lobe.Name, descriptor.Lobe.Version, registered);
    }

    // ── 3. EnsureLoadedAsync ──────────────────────────────────────────────────

    /// <summary>
    /// Ensures a LOBE module is loaded in the dedicated runspace bound to <paramref name="ps"/>.
    /// Eager LOBEs are pre-registered in the <see cref="InitialSessionState"/> but ISS load
    /// failures are silently swallowed by <see cref="Runspace.Open"/>. This method therefore
    /// calls Import-Module for ALL LOBEs (eager and JIT alike) — Import-Module is idempotent
    /// when the module is already present, and recovers a silently-failed ISS load.
    /// </summary>
    public async Task EnsureLoadedAsync(
        PowerShell ps, string modulePath, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool isEager = _importedModules.ContainsKey(modulePath);
        _log.LogDebug("LobeManager: EnsureLoadedAsync — {Kind} '{Path}'.",
            isEager ? "eager" : "JIT", modulePath);

        if (!File.Exists(modulePath))
            throw new FileNotFoundException(
                $"LobeManager: module not found — '{modulePath}'.", modulePath);

        _log.LogInformation("LobeManager: importing into isolated runspace ({Kind}) — {Path}",
            isEager ? "eager/verify" : "JIT", modulePath);

        ps.Commands.Clear();
        ps.AddCommand("Import-Module")
          .AddParameter("Name",   modulePath)
          .AddParameter("Force",  false)
          .AddParameter("Global", true);  // must be global-scope so subsequent pipeline commands see it

        await Task.Run(() => ps.Invoke(), ct);

        if (ps.HadErrors)
        {
            var errors = string.Join("; ", ps.Streams.Error.Select(e => e.ToString()));
            throw new InvalidOperationException(
                $"LobeManager: Import-Module failed for '{modulePath}': {errors}");
        }

        _log.LogInformation("LobeManager: import complete — {Path}", modulePath);
    }

    // ── Protocol registry lookup ──────────────────────────────────────────────

    /// <summary>
    /// Resolves a DIDComm @type URI to a registration.
    /// Lookup: (1) exact match, (2) longest-prefix match. Returns null if not found.
    /// </summary>
    public LobeProtocolRegistration? TryResolveProtocol(string messageType)
    {
        if (_exactRegistry.TryGetValue(messageType, out var exact)) return exact;

        LobeProtocolRegistration? best    = null;
        int                       bestLen = 0;
        foreach (var (prefix, reg) in _prefixRegistry)
        {
            if (messageType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && prefix.Length > bestLen)
            {
                best    = reg;
                bestLen = prefix.Length;
            }
        }
        return best;
    }

    public bool IsRegistered(string lobeName) =>
        _exactRegistry .Values.Any(r => r.LobeName.Equals(lobeName, StringComparison.OrdinalIgnoreCase)) ||
        _prefixRegistry.Values.Any(r => r.LobeName.Equals(lobeName, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, LobeProtocolRegistration> ExactRegistrations  => _exactRegistry;
    public IReadOnlyDictionary<string, LobeProtocolRegistration> PrefixRegistrations => _prefixRegistry;

    // ── FileSystemWatcher ─────────────────────────────────────────────────────

    private void StartFileSystemWatcher()
    {
        if (!Directory.Exists(LobeBaseDir))
        {
            _log.LogWarning("LobeManager: LOBE directory '{Dir}' not found — " +
                            "FileSystemWatcher not started.", LobeBaseDir);
            return;
        }

        _watcher = new FileSystemWatcher(LobeBaseDir, "*.lobe.json")
        {
            NotifyFilter          = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = true,
            EnableRaisingEvents   = true
        };

        _watcher.Created += OnDescriptorChanged;
        _watcher.Changed += OnDescriptorChanged;

        _log.LogInformation(
            "LobeManager: FileSystemWatcher started — watching '{Dir}' for *.lobe.json.", LobeBaseDir);
    }

    private void OnDescriptorChanged(object sender, FileSystemEventArgs e)
    {
        _log.LogInformation(
            "LobeManager: descriptor change — {Path}. Re-registering protocols.", e.FullPath);

        // Fire off the FSW callback thread immediately — do not block it.
        // A 200 ms pause lets the writer finish flushing before we parse.
        var path   = e.FullPath;
        var config = _config;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200);
                RegisterFromDescriptor(path);

                // Warn if the newly detected LOBE is listed as eager in lobes.config.json.
                // The ISS is built once at startup and cannot be rebuilt at runtime without
                // restarting the TDA. The LOBE's cmdlets will still run (imported JIT per
                // runspace) but will not benefit from eager pre-loading.
                if (config is not null)
                {
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    var isEager  = config.Eager.Any(p =>
                        Path.GetFileNameWithoutExtension(p)
                            .Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    if (isEager)
                        _log.LogWarning(
                            "LobeManager: LOBE '{Name}' is configured as eager but was detected " +
                            "at runtime after startup. It will be treated as JIT this session. " +
                            "Restart the TDA to apply eager loading.", fileName);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "LobeManager: failed to register from '{Path}'.", path);
            }
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ScanDescriptors()
    {
        var files = Directory.GetFiles(LobeBaseDir, "*.lobe.json", SearchOption.AllDirectories);
        _log.LogInformation("LobeManager: scanning {N} descriptor(s) under '{Dir}'.",
            files.Length, LobeBaseDir);
        foreach (var f in files) RegisterFromDescriptor(f);
    }

    private LobeConfig LoadLobeConfig()
    {
        var path = _opts.LobesConfigPath;
        if (!File.Exists(path))
        {
            _log.LogWarning("LobeManager: lobes.config.json not found at '{Path}'.", path);
            return new LobeConfig();
        }
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LobeConfig>(json, LobeDescriptor.JsonOpts)
            ?? new LobeConfig();
    }

    private string ResolveLobePath(string configPath)
    {
        if (Path.IsPathRooted(configPath)) return configPath;
        return Path.GetFullPath(Path.Combine(LobeBaseDir, configPath));
    }

    // Legacy: kept for agent script compatibility.
    public string? ResolveJitLobe(string moduleName)
    {
        if (_config is null) return null;
        var match = _config.Jit.FirstOrDefault(p =>
            Path.GetFileNameWithoutExtension(p)
                .Equals(moduleName, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : ResolveLobePath(match);
    }

    public IReadOnlyList<string> JitLobePaths =>
        _config?.Jit.Select(ResolveLobePath).ToArray() ?? Array.Empty<string>();

    // ── Built-in cmdlet loader ────────────────────────────────────────────────

    /// <summary>
    /// Reflects over <c>System.Management.Automation.dll</c> (Core) and
    /// <c>Microsoft.PowerShell.Commands.Utility.dll</c> and adds every
    /// <see cref="Cmdlet"/> subclass directly to the ISS as a
    /// <see cref="SessionStateCmdletEntry"/>.
    /// This bypasses PowerShell's auto-import mechanism, which requires module
    /// manifests under <c>$PSHOME</c> — a path that does not exist when PS is
    /// hosted via NuGet. Direct entries are found before CreateDefault2()'s
    /// deferred module catalog entries, so no filesystem lookup ever occurs.
    /// </summary>
    private static void AddBuiltInCmdlets(InitialSessionState iss)
    {
        // Prevent PowerShell from auto-importing modules from $PSHOME when a cmdlet is
        // first called — that path doesn't exist in NuGet-hosted runspaces and causes
        // "Microsoft.PowerShell.Utility could not be loaded" at runtime.
        // All built-in cmdlets are pre-registered below, so auto-loading is never needed.
        iss.Variables.Add(new SessionStateVariableEntry(
            "PSModuleAutoLoadingPreference",
            PSModuleAutoLoadingPreference.None,
            "Disable module auto-loading in TDA hosted runspace.",
            ScopedItemOptions.AllScope));

        // Scan SMA.dll (Core) for ForEach-Object, Where-Object, Import-Module, etc.
        // Also scan Microsoft.PowerShell.Commands.Utility.dll for Write-Verbose,
        // Write-Host, ConvertFrom-Json, ConvertTo-Json, Select-Object, Sort-Object, etc.
        // Must use GetTypes() (not GetExportedTypes()) — many cmdlet classes are internal.
        var assemblies = new List<Assembly> { typeof(PowerShell).Assembly };

        // Load Microsoft.PowerShell.Commands.Utility (direct PackageReference — 7.4.1).
        // Assembly.Load resolves via the deps.json manifest; no filesystem path required.
        try   { assemblies.Add(Assembly.Load("Microsoft.PowerShell.Commands.Utility")); }
        catch { /* Utility not resolvable — Write-Verbose etc. remain deferred */ }

        foreach (var asm in assemblies)
        {
            foreach (var type in asm.GetTypes())
            {
                try
                {
                    if (type.IsAbstract || type.IsGenericType) continue;
                    if (!typeof(Cmdlet).IsAssignableFrom(type)) continue;
                    var attr = type.GetCustomAttribute<CmdletAttribute>();
                    if (attr is null) continue;
                    iss.Commands.Add(new SessionStateCmdletEntry(
                        $"{attr.VerbName}-{attr.NounName}", type, null));
                }
                catch { /* skip types that throw on reflection (e.g. TypeLoadException) */ }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        _iss = null;
    }
}
