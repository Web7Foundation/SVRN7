using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Svrn7.TDA;

/// <summary>
/// Hub for local-UI WebSocket connections on /didcomm-notify.
/// Supports multiple simultaneous clients (PandoMail, PS tooling, etc.).
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
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public WebSocketNotifyHub(ILogger<WebSocketNotifyHub> log)
    {
        _log = log;
    }

    public bool IsConnected =>
        _sockets.Values.Any(ws => ws.State == WebSocketState.Open);

    internal Guid Attach(WebSocket ws)
    {
        var id = Guid.NewGuid();
        _sockets[id] = ws;
        return id;
    }

    internal void Detach(Guid id) => _sockets.TryRemove(id, out _);

    /// <summary>
    /// Pushes a DIDComm JSON envelope to all connected local-UI clients.
    /// No-op if no clients are connected. Closed sockets are pruned on send.
    /// </summary>
    public async Task PushAsync(string json, CancellationToken ct = default)
    {
        if (_sockets.IsEmpty) return;

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);

            string msgType = "(unknown)";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("type", out var t))
                    msgType = t.GetString() ?? msgType;
            }
            catch { }

            foreach (var (id, ws) in _sockets.ToArray())
            {
                if (ws.State != WebSocketState.Open)
                {
                    _sockets.TryRemove(id, out _);
                    continue;
                }

                _log.LogDebug(
                    "WebSocketNotifyHub: → client {Id} type={Type} bytes={Bytes}",
                    id, msgType, bytes.Length);
                try
                {
                    await ws.SendAsync(
                        new ReadOnlyMemory<byte>(bytes),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
                }
                catch (WebSocketException ex)
                {
                    _log.LogDebug(ex, "WebSocketNotifyHub: send failed for client {Id} — removing.", id);
                    _sockets.TryRemove(id, out _);
                }
            }

            _log.LogDebug("WebSocketNotifyHub: push complete type={Type}.", msgType);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _sendLock.Release();
        }
    }
}
