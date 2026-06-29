# WsExample1

> .NET 8 WebSocket server/client pair using only the standard library — no NuGet packages.

Two console app projects demonstrating production-quality WebSocket patterns with
`System.Net.WebSockets` and `System.Net.HttpListener`. The code avoids framework abstractions
so that every mechanism is visible and auditable.

## Source files

- `WSServer1/Program.cs` — Single-class server. `HttpListener` accept loop, WebSocket upgrade,
  per-connection idle watchdog, echo logic, health check endpoint (`/health`), graceful shutdown.
- `WSClient1/Program.cs` — Single-class client. Reconnecting loop with retry limit, attach/detach
  protocol, pending outbound queue, timeout message handling, connect timeout.
- `WSServer1/WSServer1.csproj` — net8.0, ImplicitUsings disabled, Nullable enabled.
- `WSClient1/WSClient1.csproj` — net8.0, ImplicitUsings disabled, Nullable enabled.

## Documentation

- `README.md` — Setup, configuration options, protocol reference, health check, project layout.
- `docs/ARCHITECTURE.md` — ASCII diagrams, concurrency model, connection lifecycle,
  all 13 best practices with rationale.
- `CLAUDE.md` — Build/run commands and coding conventions for Claude Code.

## Key design decisions

- No serializer — JSON built with raw interpolated string literals (`$$"""..."""`)
- No top-level statements — explicit `static async Task<int> Main(string[] args)` in both projects
- `<ImplicitUsings>disable</ImplicitUsings>` — all usings are explicit and alphabetically ordered
- Server defaults: port 7443, WebSocket path `/didcommws`, health path `/health`
- Connection registry: `Dictionary<Guid, WebSocket>` guarded by `object _lock`
- Per-connection `SemaphoreSlim(1,1)` serialises all sends (echo reply + watchdog timeout message)
- Idle timeout 15 s: watchdog sends `{"type":"timeout",...}` then `CloseOutputAsync`,
  cancels `idleCts` only as a 5 s fallback if client does not close
- Fragmented message reassembly via `MemoryStream` accumulation loop, 1 MB cap
- Client reconnect: up to 10 attempts × 1 s; 5 s connect timeout via linked `CancellationTokenSource`
- `OperationCanceledException when (cts.Token.IsCancellationRequested)` filter distinguishes
  user Ctrl+C from connect timeout so reconnect logic stays intact
- Pending outbound `Queue<string>` preserves messages typed during a disconnect window
- `Console.CancelKeyPress` registered once before the outer loop; targets `_cts` static field
- Close operations inside `HandleClientAsync` always use an independent short-lived CTS,
  never the outer `ct` which is cancelled during server shutdown before cleanup runs
- `AssemblyInformationalVersion` in all protocol messages for build traceability
- `typeof(Program).Module.ModuleVersionId` for MVID — portable across all .NET platforms

## Protocol messages (UTF-8 JSON text frames)

`attach` — client → server on connect
`detach` — client → server on `bye` command
`timeout` — server → client before idle close

All messages carry: `type`, `instanceId` (per-run Guid), `appName`, `mvid` (per-build Guid), `appVersion`
`attach` and `detach` also carry: `appFullName`

## Concurrency summary

```
Main (async)                        Watchdog (Task.Run)
  ReceiveAsync loop                   Delay loop (5 s interval)
    acquires sendLock                   acquires sendLock
    SendAsync (echo reply)              SendAsync (timeout msg)
    releases sendLock                   releases sendLock
                                        CloseOutputAsync
                                        Delay 5 s → idleCts.Cancel()
```

`_clientTasks` (`HashSet<Task>`) tracks all active handlers; graceful shutdown awaits them all.
