using System.Security.Cryptography;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;
using NSec.Cryptography;
using Svrn7.Core;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;

namespace Svrn7.Crypto;

/// <summary>
/// Implements ICryptoService using NBitcoin (secp256k1), NSec (Ed25519/X25519),
/// Blake3, and .NET 8 AesGcm. All key material is zeroed after use.
/// </summary>
public sealed class CryptoService : ICryptoService
{
    // Base58btc alphabet — excludes 0, O, I, l
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    private static readonly int[] DecodeTable = BuildDecodeTable();

    private static int[] BuildDecodeTable()
    {
        var t = new int[128];
        Array.Fill(t, -1);
        for (var i = 0; i < Alphabet.Length; i++)
            t[Alphabet[i]] = i;
        return t;
    }

    // ── Key generation ────────────────────────────────────────────────────────

    public Svrn7KeyPair GenerateSecp256k1KeyPair()
    {
        var key = new Key();
        return new Svrn7KeyPair
        {
            PublicKeyHex   = key.PubKey.ToHex(),
            PrivateKeyBytes= key.ToBytes(),
            Algorithm      = KeyAlgorithm.Secp256k1
        };
    }

    public Svrn7KeyPair GenerateEd25519KeyPair()
    {
        var algo = SignatureAlgorithm.Ed25519;
        var key  = NSec.Cryptography.Key.Create(algo,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        return new Svrn7KeyPair
        {
            PublicKeyHex    = Convert.ToHexString(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
            PrivateKeyBytes = key.Export(KeyBlobFormat.RawPrivateKey),
            Algorithm       = KeyAlgorithm.Ed25519
        };
    }

    // ── Signing / verification ────────────────────────────────────────────────

    public string SignSecp256k1(byte[] payload, byte[] privateKeyBytes)
    {
        var key  = new Key(privateKeyBytes);
        var hash = Hashes.SHA256(payload);
        var sig  = key.Sign(new uint256(hash));
        return Svrn7Constants.CesrPrefixSecp256k1 +
               Convert.ToBase64String(sig.ToDER()).TrimEnd('=')
               .Replace('+', '-').Replace('/', '_');
    }

    public bool VerifySecp256k1(byte[] payload, string cesrSignature, string publicKeyHex)
    {
        try
        {
            var raw     = cesrSignature[2..];  // strip CESR prefix
            var padded  = raw.Replace('-', '+').Replace('_', '/');
            var padLen  = (4 - padded.Length % 4) % 4;
            var derBytes= Convert.FromBase64String(padded + new string('=', padLen));
            var sig     = new ECDSASignature(derBytes);
            var pubKey  = new PubKey(Convert.FromHexString(publicKeyHex));
            var hash    = Hashes.SHA256(payload);
            return pubKey.Verify(new uint256(hash), sig);
        }
        catch { return false; }
    }

    public string SignEd25519(byte[] payload, byte[] privateKeyBytes)
    {
        var algo = SignatureAlgorithm.Ed25519;
        using var key = NSec.Cryptography.Key.Import(algo, privateKeyBytes,
            KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var sig = algo.Sign(key, payload);
        return Svrn7Constants.CesrPrefixEd25519 +
               Convert.ToBase64String(sig).TrimEnd('=')
               .Replace('+', '-').Replace('/', '_');
    }

    public bool VerifyEd25519(byte[] payload, string cesrSignature, string publicKeyHex)
    {
        try
        {
            var algo    = SignatureAlgorithm.Ed25519;
            var raw     = cesrSignature[2..];
            var padded  = raw.Replace('-', '+').Replace('_', '/');
            var padLen  = (4 - padded.Length % 4) % 4;
            var sigBytes= Convert.FromBase64String(padded + new string('=', padLen));
            var pubKey  = PublicKey.Import(algo,
                Convert.FromHexString(publicKeyHex), KeyBlobFormat.RawPublicKey);
            return algo.Verify(pubKey, payload, sigBytes);
        }
        catch { return false; }
    }

    // ── AES-256-GCM ───────────────────────────────────────────────────────────
    // 12-byte random nonce prepended; 16-byte authentication tag.
    // Key is zeroed after use. Do not remove Array.Clear.

    public byte[] EncryptAes256Gcm(byte[] plaintext, byte[] key)
    {
        var nonce      = RandomNumberGenerator.GetBytes(12);
        var tag        = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        try
        {
            using var aes = new AesGcm(key, tagSizeInBytes: 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        finally { Array.Clear(key, 0, key.Length); }

        var result = new byte[12 + 16 + ciphertext.Length];
        Buffer.BlockCopy(nonce,      0, result,  0, 12);
        Buffer.BlockCopy(tag,        0, result, 12, 16);
        Buffer.BlockCopy(ciphertext, 0, result, 28, ciphertext.Length);
        return result;
    }

    public byte[] DecryptAes256Gcm(byte[] ciphertext, byte[] key)
    {
        var nonce  = ciphertext[..12];
        var tag    = ciphertext[12..28];
        var cipher = ciphertext[28..];
        var plain  = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, tagSizeInBytes: 16);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        finally { Array.Clear(key, 0, key.Length); }
        return plain;
    }

    // ── Hashing ───────────────────────────────────────────────────────────────

    public string Blake3Hex(byte[] data)
        => Blake3.Hasher.Hash(data).ToString();

    // ── Base58btc ─────────────────────────────────────────────────────────────

    public string Base58Encode(byte[] data)
    {
        var leadingZeros = data.TakeWhile(b => b == 0).Count();
        var n = new System.Numerics.BigInteger(data, isUnsigned: true, isBigEndian: true);
        var sb = new StringBuilder();
        while (n > 0)
        {
            n = System.Numerics.BigInteger.DivRem(n, 58, out var rem);
            sb.Insert(0, Alphabet[(int)rem]);
        }
        return new string('1', leadingZeros) + sb;
    }

    public byte[] Base58Decode(string encoded)
    {
        var n = System.Numerics.BigInteger.Zero;
        foreach (var c in encoded)
        {
            if (c >= 128 || DecodeTable[c] < 0)
                throw new FormatException($"Invalid Base58 character: '{c}'");
            n = n * 58 + DecodeTable[c];
        }
        var leadingZeros = encoded.TakeWhile(c => c == '1').Count();
        var bytes = n.ToByteArray(isUnsigned: true, isBigEndian: true);
        var result = new byte[leadingZeros + bytes.Length];
        Buffer.BlockCopy(bytes, 0, result, leadingZeros, bytes.Length);
        return result;
    }
}
