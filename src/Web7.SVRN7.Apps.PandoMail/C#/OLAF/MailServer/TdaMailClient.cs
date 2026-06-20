using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Web7.SVRN7.Apps
{
    public sealed record EmailSummary(
        string   MessageDid,
        string   SenderDid,
        string   Subject,
        string   FromHeader,
        string   ToHeader,
        DateTime ReceivedAt);

    /// <summary>
    /// PandoMail ↔ local Citizen TDA transport over WebSocket (ws://localhost:{port}/didcomm-notify).
    /// All outbound messages (Enqueue-PandoMail, List-Emails requests) go over the WebSocket.
    /// All inbound messages (Get-PandoMails replies, Email-Notify pushes) arrive over the same socket.
    /// TDA→TDA mail delivery remains HTTP/2 — this client is local-only.
    /// </summary>
    public sealed class TdaMailClient : IDisposable
    {
        private readonly string              _wsUri;
        private readonly HttpClient          _http;
        private ClientWebSocket              _ws;
        private readonly CancellationTokenSource _cts = new();

        // Pending List-Emails requests keyed by correlationId → completion source.
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();

        /// <summary>Fired on the thread-pool when TDA pushes an Email-Notify envelope.</summary>
        public event Action<string> EmailNotifyReceived;

        /// <summary>The connected TDA's agent DID, populated after GetTdaDidAsync() completes.</summary>
        public string TdaDid { get; private set; } = string.Empty;

        /// <summary>True when the WebSocket connection to the TDA is open.</summary>
        public bool IsConnected => _ws.State == WebSocketState.Open;

        /// <summary>The WebSocket URI this client connects to.</summary>
        public string WsUri { get; }

        public TdaMailClient(int port)
        {
            WsUri  = $"ws://localhost:{port}/didcomm-notify";
            _wsUri = WsUri;

            // Shared HttpClient for the ClientWebSocket HTTP/2 handshake (RFC 8441).
            var handler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true };
            _http = new HttpClient(handler)
            {
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionOrHigher
            };

            _ws = new ClientWebSocket();
            ConfigureWebSocket(_ws);
        }

        private static void ConfigureWebSocket(ClientWebSocket ws)
        {
            // Request WebSocket over HTTP/2 (RFC 8441 extended CONNECT).
            ws.Options.HttpVersion       = HttpVersion.Version20;
            ws.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        }

        // ── Connection lifecycle ────────────────────────────────────────────────

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            Debug.WriteLine($"[TdaMailClient] WS CONNECT {_wsUri}");
            try
            {
                await _ws.ConnectAsync(new Uri(_wsUri), _http, ct);
                Debug.WriteLine($"[TdaMailClient] WS CONNECT complete state={_ws.State}");
                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            }
            catch (WebSocketException ex)
            {
                Debug.WriteLine($"[TdaMailClient] WS CONNECT FAILED: {ex.Message} (WebSocketErrorCode={ex.WebSocketErrorCode} HttpStatusCode={ex.WebSocketErrorCode} InnerException={ex.InnerException?.Message})");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TdaMailClient] WS CONNECT FAILED ({ex.GetType().Name}): {ex.Message}  InnerException={ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
                throw;
            }
        }

        // ── Outbound: Send a composed email ────────────────────────────────────

        public async Task SendAsync(string recipientDid, string subject, string bodyText,
            CancellationToken ct = default)
        {
            string msgBody = JsonSerializer.Serialize(new { recipientDid, subject, bodyText });
            await SendEnvelopeAsync(
                "did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Enqueue-PandoMail",
                msgBody, ct);
        }

        // ── Inbound: Request current email list ────────────────────────────────

        public async Task<List<EmailSummary>> ListEmailsAsync(int limit = 50,
            CancellationToken ct = default)
        {
            string correlationId = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[correlationId] = tcs;

            try
            {
                string msgBody = JsonSerializer.Serialize(new
                {
                    correlationId,
                    limit
                });
                await SendEnvelopeAsync(
                    "did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/List-Emails",
                    msgBody, ct);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                timeout.Token.Register(() => tcs.TrySetCanceled());

                string replyJson = await tcs.Task;
                return ParseEmailList(replyJson);
            }
            finally
            {
                _pending.TryRemove(correlationId, out _);
            }
        }

        // ── Query: TDA's own DID ────────────────────────────────────────────────

        public async Task<string> GetTdaDidAsync(CancellationToken ct = default)
        {
            string correlationId = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[correlationId] = tcs;

            try
            {
                string msgBody = JsonSerializer.Serialize(new
                {
                    correlationId
                });
                await SendEnvelopeAsync(
                    "did:drn:svrn7.net/protocols/Svrn7.Email.0.8.0/Query-TdaDid",
                    msgBody, ct);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                timeout.Token.Register(() => tcs.TrySetCanceled());

                string replyJson = await tcs.Task;
                TdaDid = ParseTdaDid(replyJson);
                return TdaDid;
            }
            finally
            {
                _pending.TryRemove(correlationId, out _);
            }
        }

        // ── Core send ───────────────────────────────────────────────────────────

        private async Task SendEnvelopeAsync(string type, string body, CancellationToken ct)
        {
            string envelope = JsonSerializer.Serialize(new
            {
                typ  = "application/didcomm-plain+json",
                id   = "did:drn:svrn7.net/didcomm/msg/" + Guid.NewGuid().ToString("N"),
                type,
                body
            });
            byte[] bytes = Encoding.UTF8.GetBytes(envelope);
            Debug.WriteLine($"[TdaMailClient] WS SEND type={type} bytes={bytes.Length} state={_ws.State}");
            try
            {
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
                Debug.WriteLine($"[TdaMailClient] WS SEND complete type={type}");
            }
            catch (WebSocketException ex)
            {
                Debug.WriteLine($"[TdaMailClient] WS SEND FAILED: {ex.Message} (WebSocketErrorCode={ex.WebSocketErrorCode} InnerException={ex.InnerException?.Message})");
                throw;
            }
        }

        // ── Receive loop ────────────────────────────────────────────────────────

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    using var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var recvJson = Encoding.UTF8.GetString(ms.ToArray());
                    Debug.WriteLine($"[TdaMailClient] WS RECV {ms.Length} bytes preview='{(recvJson.Length > 120 ? recvJson[..120] : recvJson)}'");
                    DispatchReceived(recvJson);
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }

        private void DispatchReceived(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                string type = root.TryGetProperty("type", out JsonElement t)
                    ? t.GetString() ?? "" : "";

                Debug.WriteLine($"[TdaMailClient] WS DISPATCH type={type}");

                if (type.EndsWith("/Reply-TdaDid", StringComparison.Ordinal))
                {
                    string cid = ExtractCorrelationId(root);
                    if (!string.IsNullOrEmpty(cid) && _pending.TryGetValue(cid, out var tcs))
                        tcs.TrySetResult(json);
                }
                else if (type.EndsWith("/Get-PandoMails", StringComparison.Ordinal))
                {
                    string cid = ExtractCorrelationId(root);
                    if (!string.IsNullOrEmpty(cid) && _pending.TryGetValue(cid, out var tcs))
                    {
                        tcs.TrySetResult(json);
                    }
                    else
                    {
                        // No correlationId match — complete the first pending request.
                        foreach (var kv in _pending)
                        {
                            kv.Value.TrySetResult(json);
                            break;
                        }
                    }
                }
                else if (type.EndsWith("/new-message", StringComparison.Ordinal) ||
                         type.Contains("Email-Notify", StringComparison.OrdinalIgnoreCase))
                {
                    EmailNotifyReceived?.Invoke(json);
                }
            }
            catch { }
        }

        private static string ExtractCorrelationId(JsonElement root)
        {
            if (!root.TryGetProperty("body", out JsonElement bodyEl)) return "";

            if (bodyEl.ValueKind == JsonValueKind.String)
            {
                try
                {
                    using JsonDocument inner = JsonDocument.Parse(bodyEl.GetString()!);
                    return inner.RootElement.TryGetProperty("correlationId", out JsonElement c)
                        ? c.GetString() ?? "" : "";
                }
                catch { return ""; }
            }

            return bodyEl.TryGetProperty("correlationId", out JsonElement cv)
                ? cv.GetString() ?? "" : "";
        }

        // ── Parsing ─────────────────────────────────────────────────────────────

        private static string ParseTdaDid(string envelopeJson)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(envelopeJson);
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("body", out JsonElement bodyEl)) return string.Empty;

                JsonElement resolved = bodyEl;
                if (bodyEl.ValueKind == JsonValueKind.String)
                {
                    using JsonDocument inner = JsonDocument.Parse(bodyEl.GetString()!);
                    resolved = inner.RootElement.Clone();
                }

                return resolved.TryGetProperty("did", out JsonElement didEl)
                    ? didEl.GetString() ?? string.Empty : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static List<EmailSummary> ParseEmailList(string envelopeJson)
        {
            var result = new List<EmailSummary>();
            try
            {
                using JsonDocument doc = JsonDocument.Parse(envelopeJson);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("body", out JsonElement bodyEl)) return result;

                JsonElement resolved = bodyEl;
                if (bodyEl.ValueKind == JsonValueKind.String)
                {
                    using JsonDocument inner = JsonDocument.Parse(bodyEl.GetString()!);
                    resolved = inner.RootElement.Clone();
                }

                if (!resolved.TryGetProperty("emails", out JsonElement emailsEl)) return result;

                foreach (JsonElement e in emailsEl.EnumerateArray())
                {
                    result.Add(new EmailSummary(
                        MessageDid:  GetStr(e, "messageDid"),
                        SenderDid:   GetStr(e, "senderDid"),
                        Subject:     GetStrOrNull(e, "subject"),
                        FromHeader:  GetStrOrNull(e, "fromHeader"),
                        ToHeader:    GetStrOrNull(e, "toHeader"),
                        ReceivedAt:  e.TryGetProperty("receivedAt", out JsonElement rv)
                                     && DateTime.TryParse(rv.GetString(), out DateTime dt)
                                     ? dt.ToLocalTime() : DateTime.Now));
                }
            }
            catch { }
            return result;
        }

        private static string GetStr(JsonElement el, string name) =>
            el.TryGetProperty(name, out JsonElement v) ? v.GetString() ?? string.Empty : string.Empty;

        private static string GetStrOrNull(JsonElement el, string name) =>
            el.TryGetProperty(name, out JsonElement v) ? v.GetString() ?? string.Empty : string.Empty;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _ws.Dispose();
            _http.Dispose();
        }
    }
}
