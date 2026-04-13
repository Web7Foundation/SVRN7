using Svrn7.Core.Interfaces;
using System.Text;
using System.Text.Json;
using Svrn7.Core;
using Svrn7.Core.Exceptions;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;

namespace Svrn7.Ledger;

/// <summary>
/// Federation-level 7-step transfer validator.
/// Steps execute in strict order; failure at any step short-circuits with a typed exception.
///
/// Step 0: NormaliseDids        — resolve any DID to canonical primary DID
/// Step 1: ValidateFields       — non-null, amount > 0, memo ≤ 256
/// Step 2: ValidateEpochRules   — epoch matrix (Federation level: citizen→society or citizen→citizen)
/// Step 3: ValidateNonce        — 24-hour replay window (LiteDB TTL store)
/// Step 4: ValidateFreshness    — ±10 minute timestamp window
/// Step 5: ValidateSanctions    — ISanctionsChecker
/// Step 6: ValidateSignature    — secp256k1 CESR over canonical JSON
/// Step 7: ValidateBalance      — dry-run UTXO sum before any spend
///
/// Step 3 uses a LiteDB-backed ITransferNonceStore for durable nonce replay
/// protection. The store sweeps expired entries (ExpiresAt &lt; UtcNow) on every
/// call and rejects duplicate nonces within the 24-hour replay window.
/// </summary>
public sealed class TransferValidator : ITransferValidator
{
    private readonly IWalletStore      _wallets;
    private readonly IIdentityRegistry _registry;
    private readonly ISanctionsChecker _sanctions;
    private readonly ICryptoService    _crypto;
    private readonly int               _currentEpoch;

    private readonly ITransferNonceStore _nonceStore;

    public TransferValidator(
        IWalletStore wallets,
        IIdentityRegistry registry,
        ISanctionsChecker sanctions,
        ICryptoService crypto,
        ITransferNonceStore nonceStore,
        int currentEpoch)
    {
        _wallets      = wallets;
        _registry     = registry;
        _sanctions    = sanctions;
        _crypto       = crypto;
        _nonceStore   = nonceStore;
        _currentEpoch = currentEpoch;
    }

    public async Task ValidateAsync(TransferRequest request, CancellationToken ct = default)
    {
        // Step 0 — Normalise DIDs to primary (handles multi-method-name citizens)
        var payerDid = await NormaliseDid(request.PayerDid, ct);
        var payeeDid = await NormaliseDid(request.PayeeDid, ct);

        // Step 1 — Field validation
        ValidateFields(request);

        // Step 2 — Epoch rules
        await ValidateEpochRulesAsync(payerDid, payeeDid, ct);

        // Step 3 — Nonce replay (LiteDB TTL store)
        await ValidateNonceAsync(request.Nonce, ct);

        // Step 4 — Freshness
        ValidateFreshness(request.Timestamp);

        // Step 5 — Sanctions
        await ValidateSanctionsAsync(payerDid, payeeDid, ct);

        // Step 6 — Signature
        ValidateSignature(request, payerDid);

        // Step 7 — Balance (dry-run — no UTXOs marked spent here)
        await ValidateBalanceAsync(payerDid, request.AmountGrana, ct);
    }

    private async Task<string> NormaliseDid(string did, CancellationToken ct)
    {
        var primary = await _registry.ResolveCitizenPrimaryDidAsync(did, ct);
        return primary ?? did;
    }

    private static void ValidateFields(TransferRequest r)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(r.PayerDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(r.PayeeDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(r.Nonce);
        ArgumentException.ThrowIfNullOrWhiteSpace(r.Signature);
        if (r.AmountGrana <= 0)
            throw new InvalidAmountException(r.AmountGrana);
        if (r.Memo is not null && r.Memo.Length > Svrn7Constants.MaxMemoLength)
            throw new ArgumentException(
                $"Memo exceeds {Svrn7Constants.MaxMemoLength} characters.", nameof(r.Memo));
    }

    private async Task ValidateEpochRulesAsync(string payerDid, string payeeDid, CancellationToken ct)
    {
        var payerIsCitizen = await _registry.IsCitizenActiveAsync(payerDid, ct);

        switch (_currentEpoch)
        {
            case Svrn7Constants.Epochs.Endowment:
                if (!payerIsCitizen)
                    throw new EpochViolationException(_currentEpoch, "PayerMustBeCitizen",
                        "Only citizens may initiate transfers in Epoch 0.");
                var payeeIsSociety = await _registry.IsSocietyActiveAsync(payeeDid, ct);
                if (!payeeIsSociety)
                    throw new EpochViolationException(_currentEpoch, "PayeeMustBeActiveSociety",
                        "Epoch 0 transfers may only target active Societies.");
                break;

            case Svrn7Constants.Epochs.EcosystemUtility:
            case Svrn7Constants.Epochs.MarketIssuance:
                if (!payerIsCitizen)
                    throw new EpochViolationException(_currentEpoch, "PayerMustBeCitizen",
                        "Only citizens may initiate transfers.");
                break;

            default:
                throw new EpochViolationException(_currentEpoch, "UnknownEpoch",
                    $"Epoch {_currentEpoch} is not recognised.");
        }
    }

    private async Task ValidateNonceAsync(string nonce, CancellationToken ct)
    {
        var isReplay = await _nonceStore.IsReplayAsync(
            nonce, Svrn7Constants.NonceReplayWindow, ct);
        if (isReplay)
            throw new NonceReplayException(nonce);
    }

    private static void ValidateFreshness(DateTimeOffset timestamp)
    {
        var now  = DateTimeOffset.UtcNow;
        var diff = (now - timestamp).Duration();
        if (diff > Svrn7Constants.TransferFreshnessWindow)
            throw new StaleTransferException(timestamp, now);
    }

    private async Task ValidateSanctionsAsync(string payerDid, string payeeDid, CancellationToken ct)
    {
        if (!await _sanctions.IsAllowedAsync(payerDid, ct))
            throw new SanctionedPartyException(payerDid);
        if (!await _sanctions.IsAllowedAsync(payeeDid, ct))
            throw new SanctionedPartyException(payeeDid);
    }

    private void ValidateSignature(TransferRequest request, string resolvedPayerDid)
    {
        var canonical = CanonicalJson(request);
        var payload   = Encoding.UTF8.GetBytes(canonical);

        // Look up payer's public key
        var payer = _registry.GetCitizenAsync(resolvedPayerDid)
            .GetAwaiter().GetResult();  // sync — already have primary DID
        var pubKeyHex = payer?.PublicKeyHex
            ?? throw new InvalidDidException(resolvedPayerDid, "Citizen not found for signature verification.");

        if (!_crypto.VerifySecp256k1(payload, request.Signature, pubKeyHex))
            throw new SignatureVerificationException($"Transfer signature invalid for payer '{resolvedPayerDid}'.");
    }

    private async Task ValidateBalanceAsync(string payerDid, long amountGrana, CancellationToken ct)
    {
        var utxos          = await _wallets.GetUnspentUtxosAsync(payerDid, ct);
        var totalAvailable = utxos.Sum(u => u.AmountGrana);
        if (totalAvailable < amountGrana)
            throw new InsufficientBalanceException(totalAvailable, amountGrana);
    }

    private static string CanonicalJson(TransferRequest r) =>
        JsonSerializer.Serialize(new
        {
            r.PayerDid, r.PayeeDid, r.AmountGrana,
            r.Nonce,    Timestamp = r.Timestamp.ToString("O"),
            r.Memo
        }, new JsonSerializerOptions { WriteIndented = false });
}

/// <summary>Thrown when a transfer amount is zero or negative.</summary>
public sealed class InvalidAmountException : Exception
{
    public long Amount { get; }
    public InvalidAmountException(long amount)
        : base($"Transfer amount must be positive. Got: {amount} grana.") => Amount = amount;
}
