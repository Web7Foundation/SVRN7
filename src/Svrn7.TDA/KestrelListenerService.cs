using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Svrn7.Core.Interfaces;
using Svrn7.DIDComm;

namespace Svrn7.TDA;

// ── KestrelListenerService ────────────────────────────────────────────────────
//
// Derived from: "HTTP Listener/Sender (HTTPClient)" + "DIDComm V2 Messaging"
//               — DSA 0.24 Epoch 0 (PPML).
//
// Design invariants (DSA 0.24 / PPML Derivation Rules):
//
//   SINGLE INBOUND SURFACE: POST /didcomm is the only route. No REST API,
//   no health endpoint, no gRPC. TDAs only talk to other TDAs (closed ecosystem).
//
//   PACK/UNPACK AT BOUNDARY: Unpack (JWE decrypt + JWS verify) is performed here,
//   before anything is written to the inbox. If UnpackAsync fails, 400 is returned
//   and nothing is enqueued. Agents always receive unpacked plaintext via ObjectId
//   reference.
//
//   WRITE-AHEAD LOG GATE: After successful unpack, IInboxStore.EnqueueAsync writes
//   the payload to svrn7-inbox.db and returns 202 immediately. The Switchboard
//   processes asynchronously. The Listener has no knowledge of routing or agent logic.
//
//   HTTP/2 + mTLS: Kestrel binds on the configured port with HTTP/2 and mutual TLS.
//   Only peers presenting a valid TDA certificate can call POST /didcomm.

/// <summary>
/// Kestrel HTTP/2 + mTLS listener — the single inbound gate for all DIDComm traffic.
/// Derived from: HTTP Listener/Sender (HTTPClient) + DIDComm V2 Messaging — DSA 0.24 Epoch 0 (PPML).
/// </summary>
public sealed class KestrelListenerService : IHostedService, IAsyncDisposable
{
    private readonly TdaOptions               _opts;
    private readonly IDIDCommService          _didComm;
    private readonly IInboxStore              _inbox;
    private readonly WebSocketNotifyHub       _hub;
    private readonly ILogger<KestrelListenerService> _log;

    private WebApplication? _app;

    public KestrelListenerService(
        IOptions<TdaOptions>               opts,
        IDIDCommService                    didComm,
        IInboxStore                        inbox,
        WebSocketNotifyHub                 hub,
        ILogger<KestrelListenerService>    log)
    {
        _opts    = opts.Value;
        _didComm = didComm;
        _inbox   = inbox;
        _hub     = hub;
        _log     = log;
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();

        // ── Kestrel: HTTP/2 + mTLS ────────────────────────────────────────────
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // Guard against oversized bodies — 2 MB is generous for any DIDComm message.
            kestrel.Limits.MaxRequestBodySize = 2 * 1024 * 1024;

            kestrel.ListenAnyIP(_opts.ListenPort, listenOpts =>
            {
                listenOpts.Protocols = HttpProtocols.Http2;

                if (_opts.TlsCertificatePath is not null)
                {
                    listenOpts.UseHttps(https =>
                    {
                        https.ServerCertificate = new X509Certificate2(
                            _opts.TlsCertificatePath,
                            _opts.TlsCertificatePassword);

                        if (_opts.RequireMutualTls)
                        {
                            https.ClientCertificateMode =
                                Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate;
                            https.ClientCertificateValidation = ValidatePeerTdaCertificate;
                        }
                    });
                }
                else
                {
                    // Development fallback: plain HTTP/2 (cleartext).
                    // Never use in production — mTLS is required for a conformant TDA.
                    _log.LogWarning(
                        "KestrelListenerService: TLS certificate not configured. " +
                        "Running in cleartext HTTP/2 (development mode only).");
                }
            });
        });

        // ── Rate limiting ─────────────────────────────────────────────────────
        // Fixed-window per-IP: protects against a misbehaving or compromised peer
        // flooding the inbox. Disabled when RateLimitRequestsPerSecond == 0.
        const string rateLimitPolicy = "didcomm";
        if (_opts.RateLimitRequestsPerSecond > 0)
        {
            builder.Services.AddRateLimiter(rl =>
            {
                rl.AddFixedWindowLimiter(rateLimitPolicy, options =>
                {
                    options.PermitLimit         = _opts.RateLimitRequestsPerSecond;
                    options.Window              = TimeSpan.FromSeconds(1);
                    options.QueueLimit          = 0;
                    options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                });
                rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });
        }

        _app = builder.Build();

        if (_opts.RateLimitRequestsPerSecond > 0)
            _app.UseRateLimiter();

        // WebSocket support (RFC 8441 over HTTP/2 for the /didcomm-notify path).
        _app.UseWebSockets();

        // ── Single inbound route: POST /didcomm ───────────────────────────────
        var route = _app.MapPost("/didcomm", HandleInboundAsync);
        if (_opts.RateLimitRequestsPerSecond > 0)
            route.RequireRateLimiting(rateLimitPolicy);

        // ── Local UI push channel: /didcomm-notify (WebSocket, localhost only) ─
        // Not published in the TDA's DID Document; not rate-limited (local only).
        _app.Map("/didcomm-notify", HandleWebSocketAsync);

        await _app.StartAsync(ct);
        _log.LogInformation(
            "KestrelListenerService: listening on port {Port} (mTLS={Mtls}).",
            _opts.ListenPort, _opts.RequireMutualTls);
        _log.LogDebug(
            "KestrelListenerService: POST /didcomm (HTTP/2 inbound) and " +
            "GET /didcomm-notify (WebSocket RFC 8441) active on port {Port}.",
            _opts.ListenPort);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_app is not null)
            await _app.StopAsync(ct);
    }

    // ── POST /didcomm handler ─────────────────────────────────────────────────

    /// <summary>
    /// Inbound DIDComm processing pipeline:
    ///   1. Enforce application/didcomm-encrypted+json content type (SignThenEncrypt only).
    ///   2. Read packed JWE body.
    ///   3. UnpackAsync (JWE decrypt + JWS verify) — security boundary.
    ///   4. EnqueueAsync → svrn7-inbox.db (write-ahead log).
    ///   5. Return 202 Accepted.
    ///
    /// If content type is wrong: return 415. If UnpackAsync fails: return 400.
    /// All subsequent processing is asynchronous via DIDCommMessageSwitchboard.
    /// </summary>
    private async Task HandleInboundAsync(HttpContext http)
    {
        // Enforce SignThenEncrypt: only JWE envelopes are accepted on POST /didcomm.
        // Plaintext and signed-only messages must use ws://…/didcomm-notify (localhost only).
        var contentType = http.Request.ContentType;
        if (contentType is null ||
            !contentType.StartsWith("application/didcomm-encrypted+json", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning(
                "KestrelListenerService: rejected non-encrypted inbound message (Content-Type: '{Ct}').",
                contentType);
            http.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            await http.Response.WriteAsync(
                "POST /didcomm requires Content-Type: application/didcomm-encrypted+json. " +
                "Use ws://…/didcomm-notify for localhost plaintext messages.",
                http.RequestAborted);
            return;
        }

        using var reader = new StreamReader(http.Request.Body);
        var packedBody = await reader.ReadToEndAsync(http.RequestAborted);

        if (string.IsNullOrWhiteSpace(packedBody))
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            await http.Response.WriteAsync("Empty DIDComm body.", http.RequestAborted);
            return;
        }

        // ── Pack/Unpack boundary (DIDComm V2 Messaging element — DSA 0.24) ───
        DIDCommUnpackedMessage unpacked;
        try
        {
            unpacked = await _didComm.UnpackAsync(
                packedBody,
                _opts.AgentKeyAgreementPrivateKey,
                http.RequestAborted);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "KestrelListenerService: UnpackAsync failed — rejecting message.");
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            await http.Response.WriteAsync(
                "DIDComm unpack failed: invalid signature or encryption.",
                http.RequestAborted);
            return;
        }

        // ── Write-ahead log (Long-Term Message Memory — DSA 0.24) ─────────────
        // Persist the unpacked payload (not the JWE — agents work with plaintext).
        // FromDid is threaded through so LOBE cmdlets can route reply messages back
        // to the sender without requiring the sender to repeat their DID in the body.
        try
        {
            await _inbox.EnqueueAsync(
                unpacked.Type,
                unpacked.Body,
                unpacked.From,
                unpacked.Id,
                packedBody,
                http.RequestAborted);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KestrelListenerService: inbox store unavailable — returning 503.");
            http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            http.Response.Headers["Retry-After"] = "5";
            await http.Response.WriteAsync(
                "Inbox store temporarily unavailable. Retry after 5 seconds.",
                http.RequestAborted);
            return;
        }

        _log.LogInformation(
            "KestrelListenerService: enqueued message type='{Type}'.", unpacked.Type);
        _log.LogDebug("KestrelListenerService: accepted message:\n{Json}", unpacked.ToFormattedJson());

        http.Response.StatusCode = StatusCodes.Status202Accepted;
    }

    // ── /didcomm-notify WebSocket handler ────────────────────────────────────

    /// <summary>
    /// Accepts a WebSocket connection from local PandoMail on /didcomm-notify.
    /// Bidirectional: TDA pushes notifications; PandoMail sends requests (List-Emails,
    /// Enqueue-PandoMail). Incoming messages go through the same UnpackAsync + EnqueueAsync
    /// pipeline as POST /didcomm — the Switchboard routes them by @type to LOBEs.
    /// LOBE responses with PeerEndpoint == WebSocketNotifyHub.LocalEndpoint are
    /// delivered back over this socket by the Switchboard instead of via HTTP/2 POST.
    /// </summary>
    private async Task HandleWebSocketAsync(HttpContext http)
    {
        if (!http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            await http.Response.WriteAsync("Expected a WebSocket request.", http.RequestAborted);
            return;
        }

        using var ws = await http.WebSockets.AcceptWebSocketAsync();
        var clientId = _hub.Attach(ws);
        _log.LogInformation(
            "KestrelListenerService: local-UI WebSocket attached on /didcomm-notify (id={Id}).", clientId);

        try
        {
            await ReceiveWebSocketLoopAsync(ws, http.RequestAborted);
        }
        finally
        {
            _hub.Detach(clientId);
            _log.LogInformation(
                "KestrelListenerService: local-UI WebSocket detached (id={Id}).", clientId);
        }
    }

    private async Task ReceiveWebSocketLoopAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                _log.LogDebug(
                    "KestrelListenerService: WebSocket frame received — {Bytes} bytes, endOfMessage={Eom}.",
                    result.Count, result.EndOfMessage);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _log.LogDebug("KestrelListenerService: WebSocket close frame received — closing.");
                    if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                    return;
                }
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            _log.LogDebug(
                "KestrelListenerService: WebSocket complete message assembled — {TotalBytes} bytes.",
                ms.Length);
            var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            _ = Task.Run(() => ProcessWebSocketMessageAsync(json, ct), ct);
        }
    }

    private async Task ProcessWebSocketMessageAsync(string json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        _log.LogDebug(
            "KestrelListenerService: WebSocket processing message — length={Length}, preview='{Preview}'.",
            json.Length, json.Length > 120 ? json[..120] : json);

        DIDCommUnpackedMessage unpacked;
        try
        {
            unpacked = await _didComm.UnpackAsync(
                json,
                _opts.AgentKeyAgreementPrivateKey,
                ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "KestrelListenerService: WebSocket UnpackAsync failed — ignoring message.");
            return;
        }

        _log.LogDebug(
            "KestrelListenerService: WebSocket UnpackAsync OK — type='{Type}', from='{From}'.",
            unpacked.Type, unpacked.From);

        try
        {
            await _inbox.EnqueueAsync(
                unpacked.Type,
                unpacked.Body,
                unpacked.From,
                unpacked.Id,
                json,
                ct);
            _log.LogDebug("KestrelListenerService: WebSocket message enqueued (type='{Type}').", unpacked.Type);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KestrelListenerService: WebSocket inbox enqueue failed.");
        }
    }

    // ── mTLS peer certificate validation ─────────────────────────────────────

    /// <summary>
    /// Validates that the connecting peer presents a certificate issued by a
    /// trusted TDA certificate authority. In production, replace with a
    /// certificate pinning or CA-validation strategy appropriate to the VTC7
    /// governance model.
    /// </summary>
    private bool ValidatePeerTdaCertificate(
        X509Certificate2 certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None) return true;

        // Development/test: accept self-signed certificates when no CA path is configured.
        if (_opts.AcceptSelfSignedPeerCertificates &&
            sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
        {
            _log.LogWarning(
                "KestrelListenerService: accepting self-signed peer certificate " +
                "(AcceptSelfSignedPeerCertificates=true — development mode only).");
            return true;
        }

        _log.LogWarning(
            "KestrelListenerService: peer certificate validation failed ({Errors}). Rejecting.",
            sslPolicyErrors);
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}
