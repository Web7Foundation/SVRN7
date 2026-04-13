using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
    private readonly ILogger<KestrelListenerService> _log;

    private WebApplication? _app;

    public KestrelListenerService(
        IOptions<TdaOptions>               opts,
        IDIDCommService                    didComm,
        IInboxStore                        inbox,
        ILogger<KestrelListenerService>    log)
    {
        _opts    = opts.Value;
        _didComm = didComm;
        _inbox   = inbox;
        _log     = log;
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();

        // ── Kestrel: HTTP/2 + mTLS ────────────────────────────────────────────
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
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

        _app = builder.Build();

        // ── Single route: POST /didcomm ───────────────────────────────────────
        _app.MapPost("/didcomm", HandleInboundAsync);

        await _app.StartAsync(ct);
        _log.LogInformation(
            "KestrelListenerService: listening on port {Port} (mTLS={Mtls}).",
            _opts.ListenPort, _opts.RequireMutualTls);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_app is not null)
            await _app.StopAsync(ct);
    }

    // ── POST /didcomm handler ─────────────────────────────────────────────────

    /// <summary>
    /// Inbound DIDComm processing pipeline:
    ///   1. Read packed JWE body.
    ///   2. UnpackAsync (JWE decrypt + JWS verify) — security boundary.
    ///   3. EnqueueAsync → svrn7-inbox.db (write-ahead log).
    ///   4. Return 202 Accepted.
    ///
    /// If UnpackAsync fails: return 400, do not enqueue.
    /// All subsequent processing is asynchronous via DIDCommMessageSwitchboard.
    /// </summary>
    private async Task HandleInboundAsync(HttpContext http)
    {
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
                _opts.SocietyMessagingPrivateKeyEd25519,
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
        await _inbox.EnqueueAsync(
            unpacked.Type,
            unpacked.Body,
            http.RequestAborted);

        _log.LogInformation(
            "KestrelListenerService: enqueued message type='{Type}'.", unpacked.Type);

        http.Response.StatusCode = StatusCodes.Status202Accepted;
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
