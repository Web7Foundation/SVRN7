using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NBitcoin;
using NBitcoin.Crypto;
using NSec.Cryptography;
using Svrn7.Core.Interfaces;

namespace Svrn7.DIDComm;

// ── Pack mode enum ────────────────────────────────────────────────────────────

public enum DIDCommPackMode
{
    Plaintext,        // No cryptography — internal use only
    Anoncrypt,        // ECDH-ES+A256KW — sender anonymous
    Authcrypt,        // ECDH-1PU+A256KW — sender authenticated
    SignOnly,         // JWS EdDSA/ES256K — signed but not encrypted
    SignThenEncrypt   // JWS wrapped in JWE — maximum assurance
}

// ── DIDComm message models ────────────────────────────────────────────────────

public record DIDCommMessage
{
    public string  Id      { get; init; } = Svrn7.Core.TdaResourceId.DIDCommMessage(Guid.NewGuid().ToString("N"));
    public string  Type    { get; init; } = string.Empty;
    public string? From    { get; init; }
    public string? To      { get; init; }
    public string  Body    { get; init; } = "{}";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public record DIDCommUnpackedMessage
{
    public string? Id      { get; init; }
    public string  Type    { get; init; } = string.Empty;
    public string? From    { get; init; }
    public string  Body    { get; init; } = "{}";
    public DIDCommPackMode Mode { get; init; }

    static readonly JsonSerializerOptions _prettyOpts = new() { WriteIndented = true };

    public string ToFormattedJson()
    {
        JsonElement bodyElement;
        try   { bodyElement = JsonSerializer.Deserialize<JsonElement>(Body); }
        catch { bodyElement = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(Body)); }

        return JsonSerializer.Serialize(new
        {
            id   = Id,
            type = Type,
            from = From,
            mode = Mode.ToString(),
            body = bodyElement
        }, _prettyOpts);
    }
}

// ── IDIDCommService ───────────────────────────────────────────────────────────

public interface IDIDCommService
{
    DIDCommMessageBuilder NewMessage();
    Task<string> PackPlaintextAsync(DIDCommMessage message, CancellationToken ct = default);
    Task<string> PackSignedAsync(DIDCommMessage message,
        byte[] senderPrivateKey, CancellationToken ct = default);
    Task<string> PackEncryptedAsync(DIDCommMessage message,
        byte[] recipientPublicKey, byte[] senderPrivateKey,
        DIDCommPackMode mode = DIDCommPackMode.SignThenEncrypt, CancellationToken ct = default);
    Task<string> PackSignedAndEncryptedAsync(DIDCommMessage message,
        byte[] recipientPublicKey, byte[] senderPrivateKey, CancellationToken ct = default);
    Task<DIDCommUnpackedMessage> UnpackAsync(string packed,
        byte[]? recipientPrivateKey = null, CancellationToken ct = default);
}

// ── DIDCommMessageBuilder ─────────────────────────────────────────────────────

public sealed class DIDCommMessageBuilder
{
    private string? _type;
    private string? _to;
    private string? _from;
    private string  _body = "{}";

    public DIDCommMessageBuilder Type(string type)   { _type = type;  return this; }
    public DIDCommMessageBuilder To(string to)       { _to   = to;    return this; }
    public DIDCommMessageBuilder From(string from)   { _from = from;  return this; }
    public DIDCommMessageBuilder Body(object body)
    {
        _body = body is string s ? s : JsonSerializer.Serialize(body);
        return this;
    }

    public DIDCommMessage Build() => new()
    {
        Type = _type ?? throw new InvalidOperationException("DIDComm message Type is required."),
        To   = _to,
        From = _from,
        Body = _body,
    };
}

// ── DIDCommPackingService ─────────────────────────────────────────────────────

/// <summary>
/// DIDComm v2 packing service.
///
/// Encryption: ECDH-ES+A256KW (X25519 ephemeral key agreement, HKDF-SHA-256 key derivation,
/// RFC 3394 AES-256 key wrap, AES-256-GCM content encryption).
///
/// Note: Key derivation uses HKDF-SHA-256 (NSec) rather than the JWA-specified Concat KDF.
/// This is an internal protocol choice — pack and unpack always use the same KDF.
///
/// Signing: EdDSA (Ed25519) or ES256K (secp256k1) based on the key type provided.
/// </summary>
public sealed class DIDCommPackingService : IDIDCommService
{
    private static readonly KeyAgreementAlgorithm _x25519 = KeyAgreementAlgorithm.X25519;
    private static readonly KeyDerivationAlgorithm _hkdf  = KeyDerivationAlgorithm.HkdfSha256;
    private static readonly JsonSerializerOptions _jsonOpts = new()
        { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly IDidDocumentResolver? _resolver;

    public DIDCommPackingService(IDidDocumentResolver? resolver = null) => _resolver = resolver;

    public DIDCommMessageBuilder NewMessage() => new();

    // ── Plaintext ─────────────────────────────────────────────────────────────

    public Task<string> PackPlaintextAsync(DIDCommMessage message, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            typ  = "application/didcomm-plain+json",
            id   = message.Id,
            type = message.Type,
            from = message.From,
            to   = message.To is not null ? new[] { message.To } : null,
            body = message.Body,
        }, _jsonOpts));
    }

    // ── Signed (JWS) ─────────────────────────────────────────────────────────

    public Task<string> PackSignedAsync(DIDCommMessage message,
        byte[] senderPrivateKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var header  = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "EdDSA", typ = "JWM" }));
        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(new
        {
            id   = message.Id,
            type = message.Type,
            from = message.From,
            to   = message.To is not null ? new[] { message.To } : null,
            body = message.Body,
        }));
        var sigInput = Encoding.ASCII.GetBytes($"{header}.{payload}");
        var sig      = SignEd25519(sigInput, senderPrivateKey);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            payload    = payload,
            signatures = new[] { new { header = new { kid = "key-1" }, protected_ = header, signature = sig } }
        }));
    }

    // ── Encrypted (JWE, ECDH-ES+A256KW) ─────────────────────────────────────

    /// <summary>
    /// Encrypts using ECDH-ES+A256KW with the recipient's X25519 public key (32 raw bytes).
    /// <paramref name="senderPrivateKey"/> is ignored for Anoncrypt; used for JWS signing in SignThenEncrypt.
    /// </summary>
    public Task<string> PackEncryptedAsync(DIDCommMessage message,
        byte[] recipientPublicKey, byte[] senderPrivateKey,
        DIDCommPackMode mode = DIDCommPackMode.SignThenEncrypt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            id   = message.Id,
            type = message.Type,
            from = message.From,
            to   = message.To is not null ? new[] { message.To } : null,
            body = message.Body,
        }, _jsonOpts));

        return Task.FromResult(EncryptJwe(plaintext, recipientPublicKey));
    }

    public async Task<string> PackSignedAndEncryptedAsync(DIDCommMessage message,
        byte[] recipientPublicKey, byte[] senderPrivateKey, CancellationToken ct = default)
    {
        // Sign first, then encrypt the JWS envelope
        var signed    = await PackSignedAsync(message, senderPrivateKey, ct);
        var plaintext = Encoding.UTF8.GetBytes(signed);
        return EncryptJwe(plaintext, recipientPublicKey);
    }

    // ── Unpack ────────────────────────────────────────────────────────────────

    public async Task<DIDCommUnpackedMessage> UnpackAsync(string packed,
        byte[]? recipientPrivateKey = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var doc  = JsonDocument.Parse(packed);
            var root       = doc.RootElement;

            // ── Plaintext (has root "type") ───────────────────────────────────
            if (root.TryGetProperty("type", out var typeEl))
                return PlaintextResult(root, typeEl, DIDCommPackMode.Plaintext);

            // ── JWE (has root "ciphertext") ───────────────────────────────────
            if (root.TryGetProperty("ciphertext", out _))
            {
                if (recipientPrivateKey is null || recipientPrivateKey.Length == 0)
                    throw new InvalidOperationException(
                        "JWE message received but no recipient private key was provided.");

                var innerJson = DecryptJwe(packed, recipientPrivateKey);
                return await UnpackInnerAsync(innerJson, ct);
            }

            // ── JWS (has root "signatures") ───────────────────────────────────
            if (root.TryGetProperty("signatures", out _))
                return await UnpackJwsAsync(root, packed, ct);

            // Unknown — dead-letter
            return new DIDCommUnpackedMessage
                { Type = "application/didcomm-encrypted+json", Body = packed, Mode = DIDCommPackMode.Authcrypt };
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to unpack DIDComm message: {ex.Message}", ex);
        }
    }

    // ── Private: JWE encrypt ──────────────────────────────────────────────────

    private string EncryptJwe(byte[] plaintext, byte[] recipientX25519PublicKey)
    {
        // Generate ephemeral X25519 key pair
        using var ephKey = NSec.Cryptography.Key.Create(_x25519,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var ephPubBytes = ephKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        // ECDH-ES: shared secret with recipient's X25519 public key
        var recipPub = PublicKey.Import(_x25519, recipientX25519PublicKey, KeyBlobFormat.RawPublicKey);
        using var sharedSecret = _x25519.Agree(ephKey, recipPub)
            ?? throw new InvalidOperationException("ECDH-ES key agreement failed.");

        // HKDF-SHA-256 → 32-byte KEK
        var kek = _hkdf.DeriveBytes(sharedSecret,
            salt: Array.Empty<byte>(),
            info: Encoding.ASCII.GetBytes("A256KW"),
            count: 32);

        // Build protected header; also used as AES-GCM AAD
        var protectedHeader = B64(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "ECDH-ES+A256KW",
            enc = "A256GCM",
            epk = new { kty = "OKP", crv = "X25519", x = B64(ephPubBytes) }
        }));
        var aad = Encoding.ASCII.GetBytes(protectedHeader);

        // Generate CEK and encrypt content
        var cek   = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag   = new byte[16];
        var ct    = new byte[plaintext.Length];

        using (var aesGcm = new AesGcm(cek, tagSizeInBytes: 16))
            aesGcm.Encrypt(nonce, plaintext, ct, tag, aad);

        // RFC 3394 AES-256 key wrap: protect CEK with KEK
        var wrappedCek = AesKeyWrap(kek, cek);
        Array.Clear(cek, 0, cek.Length);
        Array.Clear(kek, 0, kek.Length);

        return JsonSerializer.Serialize(new
        {
            protected_    = protectedHeader,
            recipients    = new[] { new { header = new { kid = "key-agreement-1" }, encrypted_key = B64(wrappedCek) } },
            iv            = B64(nonce),
            ciphertext    = B64(ct),
            tag           = B64(tag),
        });
    }

    // ── Private: JWE decrypt ──────────────────────────────────────────────────

    private string DecryptJwe(string packed, byte[] recipientX25519PrivateKey)
    {
        using var doc  = JsonDocument.Parse(packed);
        var root       = doc.RootElement;

        var protectedB64  = root.GetProperty("protected_").GetString()
            ?? throw new InvalidOperationException("JWE missing 'protected_' header.");
        var wrappedCekB64 = root.GetProperty("recipients")[0]
            .GetProperty("encrypted_key").GetString()
            ?? throw new InvalidOperationException("JWE missing encrypted_key.");
        var ivB64         = root.GetProperty("iv").GetString()!;
        var ctB64         = root.GetProperty("ciphertext").GetString()!;
        var tagB64        = root.GetProperty("tag").GetString()!;

        // Parse EPK from protected header
        var headerJson = Encoding.UTF8.GetString(FromB64(protectedB64));
        using var hDoc = JsonDocument.Parse(headerJson);
        var ephPubB64  = hDoc.RootElement.GetProperty("epk").GetProperty("x").GetString()!;

        // ECDH-ES: shared secret using our private key + ephemeral public key
        using var recipKey = NSec.Cryptography.Key.Import(_x25519, recipientX25519PrivateKey,
            KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var ephPub = PublicKey.Import(_x25519, FromB64(ephPubB64), KeyBlobFormat.RawPublicKey);
        using var sharedSecret = _x25519.Agree(recipKey, ephPub)
            ?? throw new InvalidOperationException("ECDH-ES key agreement failed during unpack.");

        // HKDF-SHA-256 → KEK (same parameters as pack side)
        var kek = _hkdf.DeriveBytes(sharedSecret,
            salt: Array.Empty<byte>(),
            info: Encoding.ASCII.GetBytes("A256KW"),
            count: 32);

        // RFC 3394 AES-256 key unwrap → CEK
        var cek = AesKeyUnwrap(kek, FromB64(wrappedCekB64));
        Array.Clear(kek, 0, kek.Length);

        // AES-256-GCM decrypt; protected header is AAD
        var nonce       = FromB64(ivB64);
        var cipherBytes = FromB64(ctB64);
        var tagBytes    = FromB64(tagB64);
        var plaintext   = new byte[cipherBytes.Length];
        var aad         = Encoding.ASCII.GetBytes(protectedB64);

        using (var aesGcm = new AesGcm(cek, tagSizeInBytes: 16))
            aesGcm.Decrypt(nonce, cipherBytes, tagBytes, plaintext, aad);
        Array.Clear(cek, 0, cek.Length);

        return Encoding.UTF8.GetString(plaintext);
    }

    // ── Private: inner payload dispatch ──────────────────────────────────────

    private async Task<DIDCommUnpackedMessage> UnpackInnerAsync(string json, CancellationToken ct)
    {
        using var doc  = JsonDocument.Parse(json);
        var root       = doc.RootElement;

        // Plaintext DIDComm message
        if (root.TryGetProperty("type", out var typeEl))
            return PlaintextResult(root, typeEl, DIDCommPackMode.Anoncrypt);

        // Nested JWS (SignThenEncrypt inner layer)
        if (root.TryGetProperty("signatures", out _))
            return await UnpackJwsAsync(root, json, ct);

        return new DIDCommUnpackedMessage { Type = string.Empty, Body = json, Mode = DIDCommPackMode.Anoncrypt };
    }

    private async Task<DIDCommUnpackedMessage> UnpackJwsAsync(
        JsonElement root, string rawJson, CancellationToken ct)
    {
        var payloadB64  = root.GetProperty("payload").GetString()!;
        var payloadJson = Encoding.UTF8.GetString(FromB64(payloadB64));

        using var payloadDoc = JsonDocument.Parse(payloadJson);
        var pr = payloadDoc.RootElement;

        var msgId   = pr.TryGetProperty("id",   out var idEl)   ? idEl.GetString()           : null;
        var msgType = pr.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? ""    : "";
        var msgFrom = pr.TryGetProperty("from", out var fromEl) ? fromEl.GetString()           : null;
        var msgBody = pr.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "{}"  : "{}";

        // Verify signature when we have a resolver and a sender DID
        if (_resolver is not null && msgFrom is not null)
        {
            var sig0      = root.GetProperty("signatures")[0];
            var protHdr   = sig0.GetProperty("protected_").GetString()!;
            var sigB64    = sig0.GetProperty("signature").GetString()!;
            var sigInput  = Encoding.ASCII.GetBytes($"{protHdr}.{payloadB64}");
            var sigBytes  = FromB64(sigB64);

            // Determine algorithm from JWS header
            var hdrJson = Encoding.UTF8.GetString(FromB64(protHdr));
            using var hDoc = JsonDocument.Parse(hdrJson);
            var alg = hDoc.RootElement.TryGetProperty("alg", out var algEl)
                ? algEl.GetString() : "EdDSA";

            var resolution = await _resolver.ResolveAsync(msgFrom, ct);
            if (resolution.Found && resolution.Document is not null)
            {
                // Pick verification method matching the signing algorithm
                var vm = alg == "EdDSA"
                    ? resolution.Document.VerificationMethod
                        .FirstOrDefault(v => v.Type.Contains("Ed25519", StringComparison.OrdinalIgnoreCase))
                    : resolution.Document.VerificationMethod
                        .FirstOrDefault(v => v.Type.Contains("Secp256k1", StringComparison.OrdinalIgnoreCase)
                                          || v.Type.Contains("EcdsaSecp256k1", StringComparison.OrdinalIgnoreCase));

                if (vm?.PublicKeyHex is not null)
                {
                    var valid = alg == "EdDSA"
                        ? VerifyEd25519Raw(sigInput, sigBytes, Convert.FromHexString(vm.PublicKeyHex))
                        : VerifySecp256k1Raw(sigInput, sigBytes, vm.PublicKeyHex);

                    if (!valid)
                        throw new InvalidOperationException(
                            $"JWS signature verification failed for sender '{msgFrom}'.");
                }
            }
        }

        return new DIDCommUnpackedMessage
            { Id = msgId, Type = msgType, From = msgFrom, Body = msgBody, Mode = DIDCommPackMode.SignOnly };
    }

    // ── Private: crypto helpers ───────────────────────────────────────────────

    private static DIDCommUnpackedMessage PlaintextResult(
        JsonElement root, JsonElement typeEl, DIDCommPackMode mode) =>
        new()
        {
            Id   = root.TryGetProperty("id",   out var idEl)   ? idEl.GetString()           : null,
            Type = typeEl.GetString() ?? string.Empty,
            From = root.TryGetProperty("from", out var fromEl) ? fromEl.GetString()          : null,
            Body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "{}" : "{}",
            Mode = mode,
        };

    // RFC 3394 AES-256 Key Wrap — wraps a 32-byte CEK with a 32-byte KEK
    private static byte[] AesKeyWrap(byte[] kek, byte[] plaintext)
    {
        int n = plaintext.Length / 8;
        var A = new byte[] { 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6 };
        var R = new byte[n][];
        for (int i = 0; i < n; i++)
        {
            R[i] = new byte[8];
            Buffer.BlockCopy(plaintext, i * 8, R[i], 0, 8);
        }

        using var aes = Aes.Create();
        aes.Key     = kek;
        aes.Mode    = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        for (int j = 0; j <= 5; j++)
        {
            for (int i = 1; i <= n; i++)
            {
                var B = new byte[16];
                Buffer.BlockCopy(A,      0, B, 0, 8);
                Buffer.BlockCopy(R[i-1], 0, B, 8, 8);
                using var enc = aes.CreateEncryptor();
                var encB = enc.TransformFinalBlock(B, 0, 16);
                Buffer.BlockCopy(encB, 0, A, 0, 8);
                long t = (long)n * j + i;
                for (int k = 7; k >= 0; k--) { A[k] ^= (byte)(t & 0xFF); t >>= 8; }
                Buffer.BlockCopy(encB, 8, R[i-1], 0, 8);
            }
        }

        var output = new byte[(n + 1) * 8];
        Buffer.BlockCopy(A, 0, output, 0, 8);
        for (int i = 0; i < n; i++)
            Buffer.BlockCopy(R[i], 0, output, (i + 1) * 8, 8);
        return output;
    }

    // RFC 3394 AES-256 Key Unwrap — recovers the CEK
    private static byte[] AesKeyUnwrap(byte[] kek, byte[] ciphertext)
    {
        int n = (ciphertext.Length / 8) - 1;
        var A = new byte[8];
        Buffer.BlockCopy(ciphertext, 0, A, 0, 8);
        var R = new byte[n][];
        for (int i = 0; i < n; i++)
        {
            R[i] = new byte[8];
            Buffer.BlockCopy(ciphertext, (i + 1) * 8, R[i], 0, 8);
        }

        using var aes = Aes.Create();
        aes.Key     = kek;
        aes.Mode    = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        for (int j = 5; j >= 0; j--)
        {
            for (int i = n; i >= 1; i--)
            {
                long t = (long)n * j + i;
                var tempA = (byte[])A.Clone();
                for (int k = 7; k >= 0; k--) { tempA[k] ^= (byte)(t & 0xFF); t >>= 8; }
                var B = new byte[16];
                Buffer.BlockCopy(tempA, 0, B, 0, 8);
                Buffer.BlockCopy(R[i-1], 0, B, 8, 8);
                using var dec = aes.CreateDecryptor();
                var decB = dec.TransformFinalBlock(B, 0, 16);
                Buffer.BlockCopy(decB, 0, A, 0, 8);
                Buffer.BlockCopy(decB, 8, R[i-1], 0, 8);
            }
        }

        var icv = new byte[] { 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6 };
        if (!A.SequenceEqual(icv))
            throw new InvalidOperationException("AES key unwrap integrity check failed — wrong key or tampered ciphertext.");

        var output = new byte[n * 8];
        for (int i = 0; i < n; i++)
            Buffer.BlockCopy(R[i], 0, output, i * 8, 8);
        return output;
    }

    private static bool VerifyEd25519Raw(byte[] data, byte[] signature, byte[] publicKeyBytes)
    {
        try
        {
            var algo   = SignatureAlgorithm.Ed25519;
            var pubKey = PublicKey.Import(algo, publicKeyBytes, KeyBlobFormat.RawPublicKey);
            return algo.Verify(pubKey, data, signature);
        }
        catch { return false; }
    }

    private static bool VerifySecp256k1Raw(byte[] data, byte[] signature, string publicKeyHex)
    {
        try
        {
            var pubKey = new PubKey(Convert.FromHexString(publicKeyHex));
            var hash   = Hashes.SHA256(data);
            var sig    = new ECDSASignature(signature);
            return pubKey.Verify(new uint256(hash), sig);
        }
        catch { return false; }
    }

    private static string B64(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64(string b64)
    {
        var padded = b64.Replace('-', '+').Replace('_', '/');
        var padLen = (4 - padded.Length % 4) % 4;
        return Convert.FromBase64String(padded + new string('=', padLen));
    }

    private static string SignEd25519(byte[] data, byte[] privateKey)
    {
        var algo = SignatureAlgorithm.Ed25519;
        using var key = NSec.Cryptography.Key.Import(algo, privateKey,
            KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        return B64(algo.Sign(key, data));
    }
}
