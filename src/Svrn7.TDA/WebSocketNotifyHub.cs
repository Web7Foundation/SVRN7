using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Svrn7.TDA;

/// <summary>
/// Singleton hub for the PandoMail local-UI WebSocket connection.
/// Holds at most one active connection (1:1 PandoMail per Citizen TDA).
///
/// When a LOBE returns an OutboundMessage whose PeerEndpoint matches
/// <see cref="LocalEndpoint"/>, the Switchboard calls <see cref="PushAsync"/>
/// instead of making an outbound HTTP/2 POST. The LOBE itself is unaware
/// of the transport — only the Switchboard delivery path differs.
/// </summary>
public sealed class WebSocketNotifyHub
{
    /// <summary>
    /// Sentinel PeerEndpoint value: any OutboundMessage targeting this endpoint
    /// is delivered via WebSocket push rather than HTTP/2 POST.
    /// </summary>
    public const string LocalEndpoint = "ws://local/didcomm-notify";

    private readonly ILogger<WebSocketNotifyHub> _log;
    private WebSocket?            _socket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public WebSocketNotifyHub(ILogger<WebSocketNotifyHub> log)
    {
        _log = log;
    }

    public bool IsConnected
    {
        get
        {
            var ws = Volatile.Read(ref _socket);
            return ws is not null && ws.State == WebSocketState.Open;
        }
    }

    internal void Attach(WebSocket ws) =>
        Interlocked.Exchange(ref _socket, ws);

    internal void Detach(WebSocket ws) =>
        Interlocked.CompareExchange(ref _socket, null, ws);

    /// <summary>
    /// Pushes a DIDComm JSON envelope to the connected PandoMail client.
    /// No-op if no client is connected or the socket is not open.
    /// </summary>
    public async Task PushAsync(string json, CancellationToken ct = default)
    {
        var ws = Volatile.Read(ref _socket);
        if (ws is null || ws.State != WebSocketState.Open)
        {
            _log.LogDebug("WebSocketNotifyHub: PushAsync — no connected client (skipping).");
            return;
        }

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);

            string msgType = "(unknown)";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("type", out var t))
                    msgType = t.GetString() ?? msgType;
            }
            catch { }
            _log.LogDebug("WebSocketNotifyHub: → PandoMail type={Type} bytes={Bytes} preview='{Preview}'",
                msgType, bytes.Length, json.Length > 120 ? json[..120] : json);

            await ws.SendAsync(
                new ReadOnlyMemory<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct).ConfigureAwait(false);
            _log.LogDebug("WebSocketNotifyHub: send complete type={Type}.", msgType);
        }
        catch (WebSocketException ex)
        {
            _log.LogDebug(ex, "WebSocketNotifyHub: send failed (WebSocketException).");
        }
        catch (OperationCanceledException) { }
        finally
        {
            _sendLock.Release();
        }
    }
}
