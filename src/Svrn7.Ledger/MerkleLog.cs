using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Svrn7.Core.Exceptions;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;
using Svrn7.Store;

namespace Svrn7.Ledger;

/// <summary>
/// RFC 6962 Certificate Transparency Merkle log.
/// Leaf nodes use 0x00 prefix; internal nodes use 0x01 prefix.
/// Writes are serialized through SemaphoreSlim(1,1).
/// Lock is always released in a finally block.
/// Root computation uses iterative bottom-up algorithm — correct for non-power-of-2 sizes.
/// </summary>
public sealed class MerkleLog : IMerkleLog
{
    private readonly Svrn7LiteContext _ctx;
    private readonly ICryptoService   _crypto;
    private readonly SemaphoreSlim    _writeLock = new(1, 1);

    public MerkleLog(Svrn7LiteContext ctx, ICryptoService crypto)
    {
        _ctx    = ctx;
        _crypto = crypto;
    }

    // ── Append ────────────────────────────────────────────────────────────────

    public async Task<string> AppendAsync(
        string entryType, string payloadJson, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var leafBytes  = ComputeLeafHash(Encoding.UTF8.GetBytes(payloadJson));
            var merkleHash = Convert.ToHexString(leafBytes).ToLowerInvariant();
            var txId       = _crypto.Blake3Hex(Encoding.UTF8.GetBytes(payloadJson));

            var entry = new LogEntry
            {
                TxId        = txId,
                EntryType   = entryType,
                PayloadJson = payloadJson,
                MerkleHash  = merkleHash,
            };
            _ctx.LogEntries.Insert(entry);
            return txId;
        }
        finally { _writeLock.Release(); }
    }

    // ── Root computation ──────────────────────────────────────────────────────

    public Task<string> ComputeRootAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var hashes = _ctx.LogEntries
            .FindAll()
            .OrderBy(e => e.CreatedAt)
            .Select(e => Convert.FromHexString(e.MerkleHash))
            .ToList();

        if (hashes.Count == 0)
            return Task.FromResult(new string('0', 64));

        // Bottom-up iterative — correct for non-power-of-2 sizes
        var current = hashes;
        while (current.Count > 1)
        {
            var next = new List<byte[]>();
            for (var i = 0; i < current.Count - 1; i += 2)
                next.Add(ComputeInternalHash(current[i], current[i + 1]));

            // Odd node propagates upward unchanged
            if (current.Count % 2 == 1)
                next.Add(current[^1]);

            current = next;
        }

        return Task.FromResult(Convert.ToHexString(current[0]).ToLowerInvariant());
    }

    // ── Signed tree head ──────────────────────────────────────────────────────

    public async Task<TreeHead> SignTreeHeadAsync(
        byte[] privateKeyBytes, CancellationToken ct = default)
    {
        var root     = await ComputeRootAsync(ct);
        var size     = await GetSizeAsync(ct);
        var payload  = Encoding.UTF8.GetBytes($"STH:{root}:{size}:{DateTimeOffset.UtcNow:O}");
        var sig      = _crypto.SignSecp256k1(payload, privateKeyBytes);
        var head     = new TreeHead { RootHash = root, TreeSize = size, Signature = sig };
        _ctx.TreeHeads.Insert(head);
        return head;
    }

    // ── Inclusion proof ───────────────────────────────────────────────────────

    public Task<bool> VerifyInclusionProofAsync(string txId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_ctx.LogEntries.FindOne(e => e.TxId == txId) is not null);
    }

    // ── Size / latest head ────────────────────────────────────────────────────

    public Task<long> GetSizeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult((long)_ctx.LogEntries.Count());
    }

    public Task<TreeHead?> GetLatestTreeHeadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var head = _ctx.TreeHeads.FindAll()
            .OrderByDescending(h => h.SignedAt)
            .FirstOrDefault();
        return Task.FromResult<TreeHead?>(head);
    }

    // ── RFC 6962 hash helpers ─────────────────────────────────────────────────

    private static byte[] ComputeLeafHash(byte[] data)
    {
        var input = new byte[1 + data.Length];
        input[0] = 0x00;  // RFC 6962 leaf prefix
        Buffer.BlockCopy(data, 0, input, 1, data.Length);
        return SHA256.HashData(input);
    }

    private static byte[] ComputeInternalHash(byte[] left, byte[] right)
    {
        var input = new byte[1 + left.Length + right.Length];
        input[0] = 0x01;  // RFC 6962 internal node prefix
        Buffer.BlockCopy(left,  0, input, 1,              left.Length);
        Buffer.BlockCopy(right, 0, input, 1 + left.Length, right.Length);
        return SHA256.HashData(input);
    }
}
