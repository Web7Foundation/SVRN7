using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class Program // WSServer
{
    static readonly Dictionary<Guid, WebSocket> _connections = new();
    static readonly object _lock = new();

    // #4 graceful shutdown: track active client tasks
    static readonly HashSet<Task> _clientTasks = new();
    static readonly object _taskLock = new();

    static readonly Guid   _instanceId  = Guid.NewGuid();
    static readonly Guid   _mvid        = typeof(Program).Module.ModuleVersionId;
    static readonly string _appName     = typeof(Program).Assembly.GetName().Name ?? "";
    static readonly string _appFullName = typeof(Program).Assembly.GetName().FullName;
    static readonly string _appVersion  = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";

    static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);
    static readonly TimeSpan IdleTimeout       = TimeSpan.FromSeconds(15);
    static readonly TimeSpan WatchdogInterval  = TimeSpan.FromSeconds(5);

    const int MaxMessageBytes = 1 * 1024 * 1024;
    const int MaxConnections  = 100;

    static string Ts() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

    static async Task<int> Main(string[] args)
    {
        string host = "localhost";
        int port = 7443;
        string path = "/didcommws";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out port) || port < 1 || port > 65535)
                    {
                        Console.Error.WriteLine($"{Ts()} Error: invalid port '{args[i]}'");
                        PrintUsage();
                        return 1;
                    }
                    break;
                case "--path" when i + 1 < args.Length:
                    path = args[++i];
                    if (!path.StartsWith('/'))
                        path = "/" + path;
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"{Ts()} Error: unknown argument '{args[i]}'");
                    PrintUsage();
                    return 1;
            }
        }

        string prefix       = $"http://{host}:{port}{path}/";
        string healthPrefix = $"http://{host}:{port}/health/";
        Console.WriteLine($"{Ts()} WSServer1 listening on {prefix}");
        Console.WriteLine($"{Ts()} Health check:         {healthPrefix}");

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Prefixes.Add(healthPrefix);
        listener.Start();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        Console.WriteLine($"{Ts()} Waiting for connections... (Ctrl+C to stop)");
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                HttpListenerContext context = await listener.GetContextAsync().WaitAsync(cts.Token);
                if (context.Request.IsWebSocketRequest)
                {
                    if (ConnectionCount() >= MaxConnections)
                    {
                        Console.WriteLine($"{Ts()} Connection rejected — max connections ({MaxConnections}) reached");
                        context.Response.StatusCode = 503;
                        context.Response.Close();
                    }
                    else
                    {
                        // #4 track task for graceful shutdown
                        Task clientTask = HandleClientAsync(context, cts.Token);
                        lock (_taskLock) _clientTasks.Add(clientTask);
                        _ = clientTask.ContinueWith(t => { lock (_taskLock) _clientTasks.Remove(t); });
                    }
                }
                else
                {
                    string? reqPath = context.Request.Url?.AbsolutePath.TrimEnd('/');
                    if (reqPath == "/health")
                    {
                        string healthJson = JsonSerializer.Serialize(new { status = "ok", connections = ConnectionCount(), maxConnections = MaxConnections, instanceId = _instanceId, appName = _appName, appVersion = _appVersion });
                        byte[] healthBytes = Encoding.UTF8.GetBytes(healthJson);
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = healthBytes.Length;
                        await context.Response.OutputStream.WriteAsync(healthBytes, 0, healthBytes.Length, cts.Token);
                        context.Response.Close();
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }

        // #4 graceful shutdown: send close frames then wait for all client tasks
        WebSocket[] sockets;
        lock (_lock)
        {
            sockets = new WebSocket[_connections.Count];
            _connections.Values.CopyTo(sockets, 0);
        }
        if (sockets.Length > 0)
        {
            Console.WriteLine($"{Ts()} Closing {sockets.Length} active connection(s)...");
            foreach (WebSocket ws in sockets)
            {
                if (ws.State == WebSocketState.Open)
                {
                    using CancellationTokenSource closeCts = new(TimeSpan.FromSeconds(5));
                    try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "server shutting down", closeCts.Token); }
                    catch { }
                }
            }
        }

        Task[] pending;
        lock (_taskLock)
        {
            pending = new Task[_clientTasks.Count];
            _clientTasks.CopyTo(pending);
        }
        if (pending.Length > 0)
        {
            Console.WriteLine($"{Ts()} Waiting for {pending.Length} client(s) to disconnect...");
            try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10)); }
            catch { }
        }

        Console.WriteLine($"{Ts()} Server stopped.");
        return 0;
    }

    static async Task HandleClientAsync(HttpListenerContext context, CancellationToken ct)
    {
        // #1 keepalive: built-in ping/pong via KeepAliveInterval
        HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(null, KeepAliveInterval);
        WebSocket ws = wsContext.WebSocket;
        Guid id = Guid.NewGuid();

        // #1 concurrent send safety: one send at a time per connection
        SemaphoreSlim sendLock = new(1, 1);

        lock (_lock)
            _connections[id] = ws;

        Console.WriteLine($"{Ts()} [{id}] connected  ({ConnectionCount()} total)");

        // #7 idle timeout: watchdog cancels idleCts if no message received within IdleTimeout
        DateTime lastReceived = DateTime.UtcNow;
        using CancellationTokenSource idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        Task watchdog = Task.Run(async () =>
        {
            try
            {
                while (!idleCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(WatchdogInterval, idleCts.Token);
                    if (DateTime.UtcNow - lastReceived > IdleTimeout)
                    {
                        Console.WriteLine($"{Ts()} [{id}] idle timeout ({IdleTimeout.TotalSeconds}s), closing");

                        string timeoutMsg = JsonSerializer.Serialize(new { type = "timeout", instanceId = _instanceId, appName = _appName, appFullName = _appFullName, mvid = _mvid, appVersion = _appVersion });
                        using CancellationTokenSource sendCts = new(TimeSpan.FromSeconds(5));
                        await sendLock.WaitAsync(sendCts.Token);
                        try { await ws.SendAsync(Encoding.UTF8.GetBytes(timeoutMsg), WebSocketMessageType.Text, true, sendCts.Token); }
                        catch { }
                        finally { sendLock.Release(); }

                        // graceful close: send close frame, let ReceiveAsync return naturally when client responds
                        using CancellationTokenSource closeCts = new(TimeSpan.FromSeconds(5));
                        try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "idle timeout", closeCts.Token); }
                        catch { }

                        // fallback: if client doesn't respond within 5s, abort the pending ReceiveAsync
                        try { await Task.Delay(TimeSpan.FromSeconds(5), idleCts.Token); }
                        catch (OperationCanceledException) { return; }
                        idleCts.Cancel();
                    }
                }
            }
            catch (OperationCanceledException) { }
        });

        byte[] buffer = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open && !idleCts.Token.IsCancellationRequested)
            {
                // #3 fragmented message reassembly: accumulate frames until EndOfMessage
                using MemoryStream ms = new();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, idleCts.Token);
                    if (result.MessageType != WebSocketMessageType.Close)
                        ms.Write(buffer, 0, result.Count);

                    // #3 message size cap
                    if (ms.Length > MaxMessageBytes)
                    {
                        Console.Error.WriteLine($"{Ts()} [{id}] message too large ({ms.Length} bytes), closing");
                        using CancellationTokenSource closeCts = new(TimeSpan.FromSeconds(5));
                        try { await ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "message too large", closeCts.Token); }
                        catch { }
                        return;
                    }
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // CloseReceived = client initiated; complete the handshake
                    // Closed/CloseSent = server already sent close frame (e.g. idle timeout); skip
                    // use independent CTS: ct may already be cancelled on server shutdown
                    if (ws.State == WebSocketState.CloseReceived)
                    {
                        using CancellationTokenSource closeCts = new(TimeSpan.FromSeconds(5));
                        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token); }
                        catch { }
                    }
                    break;
                }

                lastReceived = DateTime.UtcNow;

                string text = Encoding.UTF8.GetString(ms.ToArray());
                Console.WriteLine($"{Ts()} [{id}] recv: {text}");

                // #1 send with lock to prevent concurrent sends from watchdog
                byte[] reply = Encoding.UTF8.GetBytes($">>> {text}");
                await sendLock.WaitAsync(ct);
                try { await ws.SendAsync(reply, WebSocketMessageType.Text, true, ct); }
                finally { sendLock.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            Console.Error.WriteLine($"{Ts()} [{id}] WebSocket error ({ex.WebSocketErrorCode}): {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{Ts()} [{id}] error: {ex}");
        }
        finally
        {
            idleCts.Cancel();
            await watchdog;

            if (ws.State == WebSocketState.Open)
            {
                using CancellationTokenSource closeCts = new(TimeSpan.FromSeconds(5));
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", closeCts.Token); }
                catch { }
            }

            lock (_lock)
                _connections.Remove(id);

            Console.WriteLine($"{Ts()} [{id}] disconnected ({ConnectionCount()} remaining)");

            // #6 dispose WebSocket
            sendLock.Dispose();
            ws.Dispose();
        }
    }

    static int ConnectionCount()
    {
        lock (_lock)
            return _connections.Count;
    }

    static void PrintUsage()
    {
        Console.Error.WriteLine("""
            Usage: WSServer1 [options]

            Options:
              --host <host>   Hostname or IP to listen on  (default: localhost)
              --port <port>   TCP port to listen on        (default: 7443)
              --path <path>   WebSocket URL path           (default: /didcommws)
              -h, --help      Show this help message
            """);
    }
}
