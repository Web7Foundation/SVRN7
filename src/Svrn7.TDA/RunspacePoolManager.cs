using System.Management.Automation.Runspaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Svrn7.TDA;

// ── RunspacePoolManager ───────────────────────────────────────────────────────
//
// Derived from: "PowerShell Runspace Pool" (Runspace Pool element type) — DSA 0.24 Epoch 0.
//
// Wraps System.Management.Automation.Runspaces.RunspacePool.
// Owns the pool lifecycle (Open, Close, Dispose).
// Provides OpenAgentRunspace() for Switchboard use when dispatching task LOBEs.
// Owns the 60-second epoch refresh timer that keeps Svrn7RunspaceContext.CurrentEpoch
// current in all runspaces without per-runspace polling.

/// <summary>
/// Singleton that owns the PowerShell <see cref="RunspacePool"/> lifecycle.
/// Derived from: PowerShell Runspace Pool — DSA 0.24 Epoch 0 (PPML).
/// </summary>
public sealed class RunspacePoolManager : IDisposable
{
    private readonly TdaOptions              _opts;
    private readonly LobeManager             _lobes;
    private readonly Svrn7RunspaceContext    _ctx;
    private readonly ILogger<RunspacePoolManager> _log;

    private RunspacePool?  _pool;
    private Timer?         _epochTimer;
    private bool           _disposed;

    // ── Pool configuration (DSA 0.24 design spec) ────────────────────────────
    // minRunspaces: 2 — Agent 1 (coordinator, always open) + one task runspace warm.
    // maxRunspaces: configurable, default ProcessorCount × 2.

    public RunspacePoolManager(
        IOptions<TdaOptions>         opts,
        LobeManager                  lobes,
        Svrn7RunspaceContext         ctx,
        ILogger<RunspacePoolManager> log)
    {
        _opts  = opts.Value;
        _lobes = lobes;
        _ctx   = ctx;
        _log   = log;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the <see cref="InitialSessionState"/> via <see cref="LobeManager"/>,
    /// opens the pool, and starts the epoch refresh timer.
    /// Called once by <see cref="TdaHostedService"/> on startup.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var iss  = _lobes.BuildInitialSessionState();
        int min  = _opts.MinRunspaces;
        int max  = _opts.MaxRunspaces > 0
                    ? _opts.MaxRunspaces
                    : Environment.ProcessorCount * 2;

        _pool = RunspaceFactory.CreateRunspacePool(min, max, iss);
        _pool.ThreadOptions = PSThreadOptions.ReuseThread;
        _pool.Open();

        _log.LogInformation(
            "RunspacePoolManager: pool open (min={Min}, max={Max}).", min, max);

        // 60-second epoch refresh — keeps $SVRN7.CurrentEpoch current in all runspaces.
        _epochTimer = new Timer(
            _ => RefreshEpoch(),
            state:     null,
            dueTime:   TimeSpan.Zero,
            period:    TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Opens a new <see cref="PowerShell"/> instance bound to the pool.
    /// Used by the <see cref="DIDCommMessageSwitchboard"/> to dispatch task LOBEs.
    /// The caller is responsible for disposing the returned instance.
    /// </summary>
    public PowerShell CreatePipelineForPool()
    {
        if (_pool is null)
            throw new InvalidOperationException(
                "RunspacePoolManager has not been started. Call Start() first.");

        var ps = PowerShell.Create();
        ps.RunspacePool = _pool;
        return ps;
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

    // ── Status ────────────────────────────────────────────────────────────────

    /// <summary>Current available runspace count in the pool.</summary>
    public int AvailableRunspaces =>
        _pool?.GetAvailableRunspaces() ?? 0;

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _epochTimer?.Dispose();

        if (_pool is not null)
        {
            _pool.Close();
            _pool.Dispose();
            _log.LogInformation("RunspacePoolManager: pool closed.");
        }
    }
}
