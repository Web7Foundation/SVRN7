using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Svrn7.TDA;

// ── RunspacePoolManager ───────────────────────────────────────────────────────
//
// Derived from: "PowerShell Runspace Pool" (Runspace Pool element type) — DSA 0.24 Epoch 0.
//
// Builds the shared InitialSessionState (ISS) via LobeManager and vends
// per-invocation IsolatedPipeline instances.  Each dispatch creates its own
// Runspace from the ISS — a crash or runaway cmdlet in one pipeline cannot
// affect any other concurrent dispatch.
// Owns the 60-second epoch refresh timer that keeps Svrn7RunspaceContext.CurrentEpoch
// current without per-runspace polling.

/// <summary>
/// Singleton that builds the shared <see cref="InitialSessionState"/> and vends
/// per-invocation <see cref="IsolatedPipeline"/> instances for LOBE dispatch.
/// Derived from: PowerShell Runspace Pool — DSA 0.24 Epoch 0 (PPML).
/// </summary>
public sealed class RunspacePoolManager : IDisposable
{
    private readonly LobeManager             _lobes;
    private readonly Svrn7RunspaceContext    _ctx;
    private readonly ILogger<RunspacePoolManager> _log;

    private InitialSessionState? _iss;
    private Timer?               _epochTimer;
    private bool                 _disposed;

    public RunspacePoolManager(
        IOptions<TdaOptions>         opts,
        LobeManager                  lobes,
        Svrn7RunspaceContext         ctx,
        ILogger<RunspacePoolManager> log)
    {
        _lobes = lobes;
        _ctx   = ctx;
        _log   = log;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the <see cref="InitialSessionState"/> via <see cref="LobeManager"/>
    /// and starts the epoch refresh timer.
    /// Called once by <see cref="SwitchboardHostedService"/> on startup.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _iss = _lobes.BuildInitialSessionState();
        _log.LogInformation("RunspacePoolManager: InitialSessionState built — per-invocation runspace isolation active.");

        // 60-second epoch refresh — keeps $SVRN7.CurrentEpoch current in all runspaces.
        _epochTimer = new Timer(
            _ => RefreshEpoch(),
            state:     null,
            dueTime:   TimeSpan.Zero,
            period:    TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Creates an isolated <see cref="PowerShell"/> pipeline backed by a dedicated
    /// <see cref="Runspace"/> opened from the shared <see cref="InitialSessionState"/>.
    /// Each LOBE invocation gets its own runspace — a crash or runaway cmdlet cannot
    /// affect other concurrent dispatches. Caller must dispose the returned instance.
    /// </summary>
    public IsolatedPipeline CreateIsolatedPipeline()
    {
        if (_iss is null)
            throw new InvalidOperationException(
                "RunspacePoolManager has not been started. Call Start() first.");
        return new IsolatedPipeline(_iss);
    }

    // ── Epoch refresh ─────────────────────────────────────────────────────────

    private void RefreshEpoch()
    {
        // In a full implementation this would query ISvrn7Driver.GetCurrentEpochAsync().
        // For v0.8.0 the epoch is always Endowment (0); the infrastructure is in place
        // for Epoch 1 advancement without code changes.
        var epoch = Svrn7.Core.Svrn7Constants.Epochs.Endowment;
        _ctx.SetEpoch(epoch);
        _log.LogDebug("RunspacePoolManager: epoch refreshed to {Epoch}.", epoch);
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _epochTimer?.Dispose();
        _log.LogInformation("RunspacePoolManager: disposed.");
    }
}

// ── IsolatedPipeline ──────────────────────────────────────────────────────────

/// <summary>
/// Pairs a <see cref="PowerShell"/> instance with its own dedicated
/// <see cref="Runspace"/> opened from the shared <see cref="InitialSessionState"/>.
/// Disposing closes and releases both. A fault in one <see cref="IsolatedPipeline"/>
/// cannot affect any other concurrent dispatch.
/// </summary>
public sealed class IsolatedPipeline : IDisposable
{
    /// <summary>The PowerShell pipeline bound to the dedicated runspace.</summary>
    public PowerShell Ps { get; }

    private readonly Runspace _runspace;
    private bool              _disposed;

    internal IsolatedPipeline(InitialSessionState iss)
    {
        _runspace = RunspaceFactory.CreateRunspace(iss);
        _runspace.Open();
        Ps = PowerShell.Create();
        Ps.Runspace = _runspace;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Ps.Dispose(); }         catch { /* best-effort */ }
        try { _runspace.Close(); }    catch { /* best-effort */ }
        try { _runspace.Dispose(); }  catch { /* best-effort */ }
    }
}
