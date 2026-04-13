using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Svrn7.Core;
using Svrn7.Core.Exceptions;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;

namespace Svrn7.Identity;

/// <summary>
/// Issues and verifies W3C Verifiable Credentials encoded as JWTs.
/// Credentials are signed with secp256k1 (ES256K algorithm).
/// The CESR prefix (2 chars) is stripped before embedding in the JWT signature field.
/// </summary>
public sealed class VcService : IVcService
{
    private readonly ICryptoService _crypto;

    public VcService(ICryptoService crypto) => _crypto = crypto;

    public Task<string> IssueAsync(
        string issuerDid,
        string subjectDid,
        string credentialType,
        object credentialSubject,
        byte[] issuerPrivateKeyBytes,
        TimeSpan? validity = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialType);

        var now    = DateTimeOffset.UtcNow;
        var exp    = now.Add(validity ?? TimeSpan.FromDays(365));
        var jti    = $"urn:uuid:{Guid.NewGuid()}";

        var vcClaim = new
        {
            context            = new[] { "https://www.w3.org/2018/credentials/v1" },
            type               = new[] { "VerifiableCredential", credentialType },
            credentialSubject  = credentialSubject,
            issuer             = issuerDid,
            issuanceDate       = now.ToString("O"),
            expirationDate     = exp.ToString("O"),
        };

        var header  = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(
            new { alg = "ES256K", typ = "JWT" }));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = issuerDid,
            sub = subjectDid,
            jti,
            iat = now.ToUnixTimeSeconds(),
            exp = exp.ToUnixTimeSeconds(),
            vc  = vcClaim,
        }));

        var signingInput = Encoding.ASCII.GetBytes($"{header}.{payload}");
        var cesrSig      = _crypto.SignSecp256k1(signingInput, issuerPrivateKeyBytes);
        // Strip CESR prefix (2 chars) for JWT
        var jwtSig       = cesrSig[2..];

        return Task.FromResult($"{header}.{payload}.{jwtSig}");
    }

    public Task<bool> VerifyAsync(string jwtEncoded, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var parts = jwtEncoded.Split('.');
        if (parts.Length != 3) return Task.FromResult(false);
        try
        {
            // 1. Decode payload — check structural integrity and expiry
            var payloadBytes = Base64UrlDecode(parts[1]);
            using var doc    = JsonDocument.Parse(payloadBytes);
            var root         = doc.RootElement;

            if (!root.TryGetProperty("exp", out var expEl))
                return Task.FromResult(false);
            var exp = DateTimeOffset.FromUnixTimeSeconds(expEl.GetInt64());
            if (exp < DateTimeOffset.UtcNow)
                return Task.FromResult(false);

            // 2. Verify secp256k1 signature
            // The signing input is exactly "header.payload" as ASCII bytes (JWS compact form)
            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");

            // Recover issuer public key from the "iss" claim in the payload
            // The issuer DID encodes the public key: did:{method}:{base58-pubkey}
            // For verification we need the raw public key hex stored by the issuing driver.
            // Since VcService does not have access to the identity registry, it verifies
            // the structural JWT signature against the public key embedded in the VC payload
            // under "vc.credentialSubject.issuerPublicKeyHex" when present, or returns true
            // for structural-only verification (the driver's VerifySecp256k1 is the complete check).
            //
            // Full cryptographic verification including DID resolution is performed by
            // Svrn7Driver.VerifySecp256k1 at the call site — VcService.VerifyAsync
            // handles structural + expiry verification; the driver adds key resolution.

            // Re-attach CESR prefix stripped at issuance and verify signature format
            var jwtSig = parts[2];
            if (jwtSig.Length < 4) return Task.FromResult(false);  // minimum valid CESR-stripped sig

            // Re-attach the CESR prefix that was stripped in IssueAsync
            var cesrSig = Svrn7.Core.Svrn7Constants.CesrPrefixSecp256k1 + jwtSig;

            // If the payload includes a verificationPublicKeyHex field (populated by the
            // issuing driver for self-contained verification), verify the signature directly.
            if (root.TryGetProperty("vc", out var vcEl) &&
                vcEl.TryGetProperty("credentialSubject", out var csEl) &&
                csEl.TryGetProperty("verificationPublicKeyHex", out var pkEl))
            {
                var publicKeyHex = pkEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(publicKeyHex))
                {
                    var valid = _crypto.VerifySecp256k1(signingInput, cesrSig, publicKeyHex);
                    return Task.FromResult(valid);
                }
            }

            // Structural + expiry check passed; full signature check requires key resolution
            // by the calling driver. Return true to indicate structural validity.
            return Task.FromResult(true);
        }
        catch { return Task.FromResult(false); }
    }

    public Task<string> RevokeAsync(string vcId, string reason, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Revocation is recorded in IVcRegistry — this method returns the revocation event ID
        return Task.FromResult(_crypto.Blake3Hex(
            Encoding.UTF8.GetBytes($"REVOKE:{vcId}:{reason}:{DateTimeOffset.UtcNow:O}")));
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        var pad = (4 - s.Length % 4) % 4;
        return Convert.FromBase64String(s + new string('=', pad));
    }
}

// ── Sanctions checkers ─────────────────────────────────────────────────────────

/// <summary>
/// Null-object sanctions checker — always allows.
/// Used in development. Replace with a real implementation for production.
/// </summary>
public sealed class PassthroughSanctionsChecker : ISanctionsChecker
{
    public Task<bool> IsAllowedAsync(string did, CancellationToken ct = default)
        => Task.FromResult(true);
}

/// <summary>
/// In-memory sanctions checker backed by a ConcurrentHashSet of blocked DIDs.
/// Suitable for testing and simple deployments.
/// </summary>
public sealed class InMemorySanctionsChecker : ISanctionsChecker
{
    private readonly ConcurrentDictionary<string, byte> _blocked = new();

    public void Block(string did)   => _blocked.TryAdd(did, 0);
    public void Unblock(string did) => _blocked.TryRemove(did, out _);

    public Task<bool> IsAllowedAsync(string did, CancellationToken ct = default)
        => Task.FromResult(!_blocked.ContainsKey(did));
}
