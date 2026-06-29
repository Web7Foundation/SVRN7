# Architecture & Design — WsExample1

## System Overview

```
┌────────────────────────────────────────────────────────────────┐
│                         WSServer1                              │
│                                                                │
│  HttpListener prefixes                                         │
│  ├── http://localhost:7443/didcommws/   ← WebSocket upgrade    │
│  └── http://localhost:7443/health/      ← HTTP GET             │
│                                                                │
│  Main (async accept loop)                                      │
│  ├── IsWebSocketRequest → HandleClientAsync()  (fire-and-forget│
│  └── else → /health → 200 JSON  |  other → 400                │
│                                                                │
│  HandleClientAsync (per connection)                            │
│  ├── receive loop   (main task context)                        │
│  └── idle watchdog  (Task.Run)                                 │
│                                                                │
│  _connections : Dictionary<Guid, WebSocket>  (_lock)          │
│  _clientTasks : HashSet<Task>                (_taskLock)       │
└───────────────────────────┬────────────────────────────────────┘
                            │  ws://localhost:7443/didcommws
┌───────────────────────────▼────────────────────────────────────┐
│                         WSClient1                              │
│                                                                │
│  outer while(true)  — resets CTS each cycle                   │
│  └── inner reconnect loop  (up to MaxRetries)                  │
│       ├── ConnectAsync   (5 s timeout via linked CTS)          │
│       ├── SendAsync      attach message                        │
│       ├── Drain          pendingOutbound Queue                 │
│       ├── ReceiveLoopAsync   (background Task, fire-and-forget)│
│       └── Console.ReadLine   loop                              │
└────────────────────────────────────────────────────────────────┘
```

---

## Server Architecture

### Accept Loop

`Main` runs a `while (!cts.Token.IsCancellationRequested)` loop calling:

```csharp
HttpListenerContext context = await listener.GetContextAsync().WaitAsync(cts.Token);
```

`WaitAsync(cts.Token)` cancels the wait on Ctrl+C without leaving `GetContextAsync` orphaned.
Each WebSocket request is dispatched as a fire-and-forget `Task` immediately — the accept loop
never blocks on individual connection handling.

### Per-Connection Handler (`HandleClientAsync`)

Owns the full lifetime of one WebSocket connection:

1. `AcceptWebSocketAsync(subProtocol: null, keepAliveInterval: 10s)` — upgrades the HTTP request
2. Registers `_connections[id] = ws` under `_lock`
3. Starts the idle watchdog (`Task.Run`)
4. Runs the receive loop until the socket closes or `idleCts` is cancelled
5. `finally`: cancels watchdog, `await watchdog`, removes from registry, disposes socket and lock

### Idle Watchdog

A `Task.Run` loop polling every `WatchdogInterval` (5 s):

```
lastReceived updated on every message
    ↓
WatchdogInterval elapses
    ↓
DateTime.UtcNow - lastReceived > IdleTimeout (15 s)?
    ↓ yes
acquire sendLock
SendAsync  {"type":"timeout",...}
release sendLock
CloseOutputAsync           ← half-close: sends WS close frame to client
await Task.Delay(5s)       ← give client time to send its close frame
    (ReceiveAsync returns naturally when client responds → loop breaks cleanly)
if still running → idleCts.Cancel()  ← fallback abort
```

This sequencing avoids `ConnectionClosedPrematurely`: the TCP connection is not torn down until
after the close handshake or the 5 s fallback, whichever comes first.

### Concurrency Model

```
Main task (async)                    Watchdog (Task.Run)
─────────────────                    ───────────────────
ReceiveAsync (blocking on idleCts)
                                     Delay(WatchdogInterval)
                                     [timeout fires]
                                     sendLock.WaitAsync()   ← blocks until main releases
                                     SendAsync(timeout msg)
                                     sendLock.Release()
                                     CloseOutputAsync
                                     Delay(5s, idleCts)
ReceiveAsync returns (close frame)
break
finally: idleCts.Cancel()
         await watchdog             ← watchdog sees cancel, returns
```

`sendLock` (`SemaphoreSlim(1,1)`) is the critical guard. The receive loop acquires it before
echo replies; the watchdog acquires it before the timeout message. Concurrent `SendAsync` on the
same `WebSocket` instance corrupts the frame stream.

### Connection Registry

```csharp
static readonly Dictionary<Guid, WebSocket> _connections = new();
static readonly object _lock = new();
```

All mutations and reads of `_connections` use `lock (_lock)`. `ConnectionCount()` locks too.
The watchdog holds a reference to `ws` directly (captured closure) and does not interact with
the dictionary — it only sends and closes.

### Graceful Shutdown Sequence

```
Ctrl+C → cts.Cancel()
GetContextAsync().WaitAsync() throws OperationCanceledException → accept loop exits
→ snapshot _connections (under _lock)
→ foreach open socket: CloseOutputAsync (5 s budget, independent CTS)
→ snapshot _clientTasks (under _taskLock)
→ await Task.WhenAll(pending).WaitAsync(10s)   ← bounded wait
→ "Server stopped."
```

Handlers remove themselves from `_clientTasks` via `ContinueWith` so the final snapshot
only contains tasks still in flight.

---

## Client Architecture

### Reconnect Loop Structure

```csharp
while (true) {                                   // outer: resets CTS per cycle
    using CancellationTokenSource cts = new();
    _cts = cts;                                  // CancelKeyPress targets current CTS

    while (!cts.Token.IsCancellationRequested) { // inner: reconnect attempts
        try {
            ConnectAsync(connectCts)             // 5 s timeout via linked CTS
            SendAsync(attach)
            DrainQueue(pendingOutbound)
            _ = ReceiveLoopAsync(ws, cts.Token)  // fire-and-forget
            ReadLine loop                        // blocking; fills pendingOutbound on loss
        }
        catch (OCE) when (cts.IsCancellationRequested) { break; }  // Ctrl+C
        catch (OCE)                             { reconnect = true; } // connect timeout
        catch (WebSocketException)              { reconnect = true; } // network error
        catch (Exception)                       { reconnect = true; }

        retryCount++;
        if (retryCount >= MaxRetries) break;
        await Task.Delay(RetryInterval, cts.Token);
    }

    if (cts.Token.IsCancellationRequested) break; // propagate Ctrl+C out
}
```

### Connect Timeout

```csharp
using CancellationTokenSource connectCts =
    CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
connectCts.CancelAfter(TimeSpan.FromSeconds(5));
await ws.ConnectAsync(uri, connectCts.Token);
```

The linked source cancels on either user Ctrl+C (`cts`) or 5 s elapsed (`connectCts`).
The exception filter `when (cts.Token.IsCancellationRequested)` distinguishes the two: only
Ctrl+C exits the reconnect loop; a timeout falls through to the reconnect path.

### Pending Outbound Queue

Messages typed during a disconnect are preserved in `Queue<string> pendingOutbound`.
After the next successful connect and attach, the queue drains in order before normal I/O resumes.
This means no typed input is silently lost during transient network errors.

### CancelKeyPress Handler

```csharp
Console.CancelKeyPress += (_, e) => { e.Cancel = true; _cts?.Cancel(); };
```

Registered once before the outer loop. `_cts` is a static field updated each cycle to point at
the current session's CTS. Registering inside the loop would accumulate delegates and trigger
multiple cancellations on a single Ctrl+C.

---

## Message Protocol Reference

### Common Fields

| Field | Source | Description |
|---|---|---|
| `type` | literal | `"attach"`, `"detach"`, or `"timeout"` |
| `instanceId` | `Guid.NewGuid()` static | Unique per process run; same value across all reconnects |
| `appName` | `Assembly.GetName().Name` | Short assembly name |
| `mvid` | `Module.ModuleVersionId` | Unique per build output; changes on every compile |
| `appVersion` | `AssemblyInformationalVersionAttribute` | Carries git hash when SourceLink is configured |

### attach and detach — client → server

Additional field: `appFullName` (`Assembly.GetName().FullName`) — fully qualified name including
version, culture, and public key token.

`attach` is sent immediately after `ConnectAsync` succeeds.
`detach` is sent when the user types `bye`; the client closes the connection after sending.

### timeout — server → client

Includes `appFullName` of the server process. Sent before `CloseOutputAsync` so the client
receives the notification before the connection closes. The client detects the `type` field and
prints a human-readable message instead of displaying the raw JSON.

---

## Health Check Endpoint

`GET http://{host}:{port}/health`

The endpoint shares the same `HttpListener` instance and accept loop as the WebSocket endpoint.
Non-WebSocket requests are routed by `context.Request.Url?.AbsolutePath.TrimEnd('/')`:

- `/health` → 200 `application/json`
- anything else → 400

Response body:

```json
{
  "status": "ok",
  "connections": 2,
  "maxConnections": 100,
  "instanceId": "...",
  "appName": "WSServer1",
  "appVersion": "1.0.0"
}
```

`connections` is a live count from `_connections` under `_lock`. Useful for load balancer health
probes and monitoring dashboards.

---

## Best Practices

### 1. TCP Keepalive / Ping-Pong

`AcceptWebSocketAsync(null, TimeSpan.FromSeconds(10))` on the server and
`ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30)` on the client enable the built-in
WebSocket ping/pong mechanism. This keeps NAT mappings alive and detects dead peers without
requiring application-level heartbeat messages.

**Why server interval < client interval**: the server drives keepalive for connections it owns;
the client's interval is a fallback for when the server is silent.

### 2. Concurrent Send Safety

`WebSocket.SendAsync` is not safe for concurrent calls on the same instance. A `SemaphoreSlim(1,1)`
named `sendLock` per connection serialises all sends. Both the echo reply path and the watchdog
timeout message path acquire `sendLock` before calling `SendAsync`. Without this guard, two
simultaneous `SendAsync` calls corrupt the WebSocket frame stream and crash the connection.

### 3. Fragmented Message Reassembly

`ReceiveAsync` may return a partial frame with `EndOfMessage == false`. A `do { ReceiveAsync }
while (!result.EndOfMessage)` loop accumulates frames into a `MemoryStream`. The complete message
is decoded and processed only after `EndOfMessage == true`. The accumulation loop also enforces
the 1 MB cap per frame to bound memory use before the full message arrives.

### 4. Message Size Cap (1 MB)

Both server and client reject messages exceeding 1 MB by sending
`WebSocketCloseStatus.MessageTooBig` and returning from the handler. This prevents unbounded heap
allocation from malformed or malicious senders.

### 5. Graceful Shutdown

The server waits up to 10 s for all active client handlers to finish after broadcasting close
frames. `Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10))` gives handlers time to
complete their own close handshakes. Handlers that do not finish within the budget are abandoned
(the process exits naturally).

### 6. WebSocket Dispose

`ws.Dispose()` and `sendLock.Dispose()` are called in the `finally` block of `HandleClientAsync`.
On the client, `using ClientWebSocket ws = new()` ensures disposal even if an exception occurs
before `CloseAsync`.

### 7. Idle Timeout with Graceful Close

The watchdog sends `{"type":"timeout",...}` and `CloseOutputAsync` (half-close) before
cancelling `idleCts`. This allows:
- The client to receive the timeout notification while the connection is still open
- `ReceiveAsync` on the server to return naturally when the client sends its close frame
- Clean completion of the WebSocket close handshake

`idleCts.Cancel()` is only called as a 5 s fallback if the client does not respond.

### 8. Max Connections (100)

`ConnectionCount()` is checked before accepting a WebSocket upgrade. When the limit is reached
the server returns HTTP 503 without upgrading, protecting server resources from connection storms.

### 9. Connect Timeout (5 s)

Without a connect timeout, a server that accepts TCP but stalls the HTTP upgrade hangs the client
indefinitely. `connectCts.CancelAfter(TimeSpan.FromSeconds(5))` bounds this. The exception filter
`when (cts.Token.IsCancellationRequested)` ensures a user Ctrl+C during connect does not get
mistaken for a timeout and trigger a reconnect.

### 10. Fresh CancellationTokenSource for Close Operations

Inside `HandleClientAsync`, `CloseAsync`/`CloseOutputAsync` always use:

```csharp
using CancellationTokenSource closeCts = new(TimeSpan.FromSeconds(5));
```

Never the outer `ct`. During server shutdown, `cts.Cancel()` fires before the cleanup code
runs. Passing a cancelled token to `CloseAsync` causes it to throw immediately, skipping the
close frame and leaving the client with a broken connection. An independent CTS gives each close
operation its own 5 s budget regardless of server shutdown state.

### 11. CancelKeyPress — Single Handler Registration

The `Console.CancelKeyPress` handler is registered once and references `_cts`, a static field.
Each reconnect cycle updates `_cts` to the new `CancellationTokenSource`. Registering the handler
inside the loop accumulates delegates: after N reconnects, Ctrl+C fires N handlers and attempts
N cancellations.

### 12. AssemblyInformationalVersion in Protocol Messages

Every protocol message includes `appVersion` from `AssemblyInformationalVersionAttribute`. When
`<InformationalVersion>` or SourceLink is configured in the project file, this carries the git
commit hash. Operators can correlate live connection logs to the exact deployed binary without
maintaining a separate version-to-binary mapping.

### 13. MVID for Build Identity

`typeof(Program).Module.ModuleVersionId` returns a `Guid` that is regenerated on every compile.
It is portable across all .NET targets — including Android and iOS — without any
platform-specific heuristics. It provides finer identity than a version string: two builds from
the same source at the same declared version have different MVIDs.

---

## Non-Obvious Design Choices

**Why `CloseOutputAsync` instead of `CloseAsync` in the watchdog**

`CloseOutputAsync` sends the close frame without waiting for the client's echo. This is
intentional: the server's `ReceiveAsync` is still running and will naturally observe the client's
close frame. Using `CloseAsync` (which waits for the echo) in the watchdog while `ReceiveAsync`
is running in the main task would create a race — both sides would be waiting for the other to
respond first.

**Why `idleCts` is a linked source (`CreateLinkedTokenSource(ct)`)**

The idle watchdog must stop both when the connection goes idle (`idleCts.Cancel()`) and when the
server shuts down (`ct` cancelled). A linked source responds to either without requiring the
watchdog to check both tokens explicitly.

**Why JSON is built with raw string literals instead of a serialiser**

The dependency footprint is deliberately zero. Raw string literals (`$$"""..."""`) are readable,
compile-time constant, and produce no allocations beyond the interpolated values. For the small
fixed set of message types here, a serialiser adds complexity without benefit.

**Why `_connections` uses `Guid` keys but connections are also identified by the same `id` in logs**

The server-assigned GUID is generated fresh per connection (`Guid.NewGuid()`), not derived from
the client. This means the server log and the client log use different identifiers for the same
connection. The client identifies itself via `instanceId` in the attach message. This is
intentional: the server GUID is purely an internal handle for the connection registry.
