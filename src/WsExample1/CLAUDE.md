# CLAUDE.md — WsExample1

## Project

WebSocket server/client pair built on `System.Net.WebSockets` and `System.Net.HttpListener`.
Target framework: .NET 8. No third-party NuGet packages.

## Build & Run

```shell
# Build entire solution
dotnet build WsExample1.sln

# Run server (defaults: localhost:7443/didcommws)
dotnet run --project WSServer1

# Run server with custom options
dotnet run --project WSServer1 -- --host 0.0.0.0 --port 8080 --path /myws

# Run client (defaults: ws://localhost:7443/didcommws)
dotnet run --project WSClient1

# Run client with custom URL
dotnet run --project WSClient1 -- --url ws://localhost:7443/didcommws
```

Start the server first, then the client. To run both from Visual Studio, right-click the solution
and set multiple startup projects (WSServer1 first, WSClient1 second).

## Project Structure

```
WsExample1.sln
WSServer1/
  WSServer1.csproj
  Program.cs          ← single-class server (class Program // WSServer)
WSClient1/
  WSClient1.csproj
  Program.cs          ← single-class client (class Program // WSClient)
docs/
  ARCHITECTURE.md
CLAUDE.md
README.md
llms.txt
```

## Code Conventions

- `<ImplicitUsings>disable</ImplicitUsings>` — all `using` statements are explicit, alphabetically ordered within `System.*` then third-party (none currently)
- `<Nullable>enable</Nullable>` — use `?` suffix and null checks; do not suppress warnings with `!` unless the null is provably impossible
- No top-level statements — explicit `static async Task<int> Main(string[] args)` in both projects
- Static fields prefixed `_` (e.g. `_connections`, `_instanceId`, `_appVersion`)
- Timestamps via `Ts()` helper: `DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")`
- JSON built as raw interpolated string literals (`$$"""..."""`) — no serializer dependency
- `SemaphoreSlim(1,1)` per connection for send-side concurrency; always acquire before `SendAsync`
- Inside `HandleClientAsync`, close operations use an independent `new CancellationTokenSource(TimeSpan.FromSeconds(5))` — never the outer `ct`, which may already be cancelled on shutdown

## Key Constants

### WSServer1

| Name | Value | Purpose |
|---|---|---|
| `KeepAliveInterval` | 10 s | Built-in TCP ping/pong interval |
| `IdleTimeout` | 15 s | Max inactivity before server closes the connection |
| `WatchdogInterval` | 5 s | Idle watchdog polling frequency |
| `MaxMessageBytes` | 1 MB | Per-message receive cap |
| `MaxConnections` | 100 | Server-wide concurrent connection cap |

### WSClient1

| Name | Value | Purpose |
|---|---|---|
| `KeepAliveInterval` | 30 s | TCP keepalive interval |
| `RetryInterval` | 1 s | Delay between reconnect attempts |
| `MaxRetries` | 10 | Max consecutive reconnect attempts before giving up |
| Connect timeout | 5 s | `CancelAfter` on `connectCts` |

## Do Not

- Do not enable `<ImplicitUsings>` — all usings must remain explicit
- Do not use `System.Text.Json.JsonSerializer` or any serializer — JSON is built manually with raw string literals to maintain zero-dependency design
- Do not call `CloseAsync`/`CloseOutputAsync` with the outer `ct` inside `HandleClientAsync` — use an independent short-lived CTS; `ct` is cancelled during server shutdown before cleanup runs
- Do not call `ws.SendAsync` without first acquiring `sendLock` — the watchdog and receive loop both send; concurrent sends corrupt the WebSocket frame stream
- Do not register `Console.CancelKeyPress` inside the reconnect loop — register once before the outer loop targeting the `_cts` static field; registering inside accumulates handlers
