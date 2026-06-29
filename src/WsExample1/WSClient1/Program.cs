using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class Program // WSClient
{
    static readonly Guid _instanceId = Guid.NewGuid();

    // #2 CancelKeyPress registered once; points at the current session's CTS
    static CancellationTokenSource? _cts;

    // #3 message size cap
    const int MaxMessageBytes = 1 * 1024 * 1024;

    static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);
    static readonly TimeSpan RetryInterval     = TimeSpan.FromSeconds(1);
    const int MaxRetries = 10;

    static string Ts() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

    static async Task<int> Main(string[] args)
    {
        string url = "ws://localhost:7443/didcommws";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--url" when i + 1 < args.Length:
                    url = args[++i];
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

        // #2 register handler once; cancels whatever the current session CTS is
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _cts?.Cancel(); };

        while (true) {
            Console.WriteLine($"{Ts()} WSClient1 cycling");
            int retryCount = 0;
            Queue<string> pendingOutbound = new();   // lines typed while disconnected; drained after reconnect
            using CancellationTokenSource cts = new();
            _cts = cts;

            while (!cts.Token.IsCancellationRequested)
            {
                Console.WriteLine($"{Ts()} WSClient1 connecting to {url}");
                bool reconnect = false;

                try
                {
                    using ClientWebSocket ws = new();
                    ws.Options.KeepAliveInterval = KeepAliveInterval;   // #1 keepalive

                    using CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    connectCts.CancelAfter(TimeSpan.FromSeconds(5));
                    await ws.ConnectAsync(new Uri(url), connectCts.Token);
                    retryCount = 0;
                    Console.WriteLine($"{Ts()} Connected. Type messages and press Enter to send. 'bye' to disconnect. Empty line or Ctrl+C to quit.");

                    Guid mvid = typeof(Program).Module.ModuleVersionId;
                    string appName = typeof(Program).Assembly.GetName().Name ?? "";
                    string appFullName = typeof(Program).Assembly.GetName().FullName;
                    string appVersion = typeof(Program).Assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion ?? "unknown";

                    string attach = $$"""{"type":"attach","instanceId":"{{_instanceId}}","appName":"{{appName}}","appFullName":"{{appFullName}}","mvid":"{{mvid}}","appVersion":"{{appVersion}}"}""";
                    await ws.SendAsync(Encoding.UTF8.GetBytes(attach), WebSocketMessageType.Text, true, cts.Token);
                    Console.WriteLine($"{Ts()} sent: {attach}");

                    while (pendingOutbound.Count > 0)
                    {
                        string pending = pendingOutbound.Dequeue();
                        await ws.SendAsync(Encoding.UTF8.GetBytes(pending), WebSocketMessageType.Text, true, cts.Token);
                        Console.WriteLine($"{Ts()} sent (pending): {pending}");
                    }

                    _ = ReceiveLoopAsync(ws, cts.Token);

                    while (!cts.Token.IsCancellationRequested)
                    {
                        string? line = Console.ReadLine();
                        if (line == null || line.Length == 0)
                            break;

                        if (ws.State != WebSocketState.Open)
                        {
                            Console.WriteLine($"{Ts()} Connection lost.");
                            pendingOutbound.Enqueue(line);
                            reconnect = true;
                            break;
                        }

                        if (line == "bye")
                        {
                            string detach = $$"""{"type":"detach","instanceId":"{{_instanceId}}","appName":"{{appName}}","appFullName":"{{appFullName}}","mvid":"{{mvid}}","appVersion":"{{appVersion}}"}""";
                            await ws.SendAsync(Encoding.UTF8.GetBytes(detach), WebSocketMessageType.Text, true, cts.Token);
                            Console.WriteLine($"{Ts()} sent: {detach}");
                            break;
                        }

                        if (line == "crash")
                        {
                            Console.WriteLine($"{Ts()} crashing");
                            return -1;
                        }

                        byte[] bytes = Encoding.UTF8.GetBytes(line);
                        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
                    }

                    if (!reconnect && ws.State == WebSocketState.Open)
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token);
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested) { break; }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine($"{Ts()} Connection timed out.");
                    reconnect = true;
                }
                catch (WebSocketException ex)
                {
                    Console.Error.WriteLine($"{Ts()} WebSocket error ({ex.WebSocketErrorCode}): {ex.Message}");
                    reconnect = true;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"{Ts()} Error: {ex}");
                    reconnect = true;
                }

                if (!reconnect || cts.Token.IsCancellationRequested)
                    break;

                retryCount++;
                if (retryCount >= MaxRetries)
                {
                    Console.WriteLine($"{Ts()} Max retries ({MaxRetries}) reached, giving up.");
                    break;
                }

                Console.WriteLine($"{Ts()} Reconnecting in {RetryInterval.TotalSeconds:0}s... (attempt {retryCount}/{MaxRetries})");
                try { await Task.Delay(RetryInterval, cts.Token); }
                catch (OperationCanceledException) { break; }
            }

            Console.WriteLine($"{Ts()} Disconnected.");
            if (cts.Token.IsCancellationRequested)
                break;
        }

        return 0;
    }

    static async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        byte[] buffer = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                // #3 fragmented message reassembly: accumulate frames until EndOfMessage
                using MemoryStream ms = new();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType != WebSocketMessageType.Close)
                        ms.Write(buffer, 0, result.Count);

                    // #3 message size cap
                    if (ms.Length > MaxMessageBytes)
                    {
                        Console.Error.WriteLine($"{Ts()} Received message too large ({ms.Length} bytes), closing.");
                        using CancellationTokenSource closeCts = new(TimeSpan.FromSeconds(5));
                        try { await ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "message too large", closeCts.Token); }
                        catch { }
                        return;
                    }
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                string text = Encoding.UTF8.GetString(ms.ToArray());
                string? msgType = null;
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(text);
                    doc.RootElement.TryGetProperty("type", out JsonElement typeEl);
                    msgType = typeEl.GetString();
                }
                catch { }

                if (msgType == "timeout")
                    Console.WriteLine($"{Ts()} Server closed connection due to inactivity - will reconnect.");
                else
                    Console.WriteLine($"{Ts()} recv: {text}");
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            Console.Error.WriteLine($"{Ts()} Receive WebSocket error ({ex.WebSocketErrorCode}): {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{Ts()} Receive error: {ex}");
        }
    }

    static void PrintUsage()
    {
        Console.Error.WriteLine("""
            Usage: WSClient1 [options]

            Options:
              --url <url>   WebSocket server URL  (default: ws://localhost:7443/didcommws)
              -h, --help    Show this help message
            """);
    }
}
