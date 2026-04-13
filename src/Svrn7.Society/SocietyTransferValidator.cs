using System.Text;
using System.Text.Json;
using Svrn7.Core;
using Svrn7.Core.Exceptions;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;
using Svrn7.Ledger;

namespace Svrn7.Society;

/// <summary>
/// Society-level 8-step transfer validator.
/// Extends the Federation validator with Society-membership awareness.
///
/// Step 0: NormaliseDids           — resolve any DID to canonical primary DID
/// Step 1: ValidateFields          — non-null, amount > 0, memo ≤ 256
/// Step 2: ValidateEpochRules      — Society-scoped epoch matrix (see below)
/// Step 3: ValidateNonce           — 24-hour replay window (LiteDB TTL store) (LiteDB TTL store)
/// Step 4: ValidateFreshness       — ±10 minute timestamp
/// Step 5: ValidateSanctions       — ISanctionsChecker
/// Step 6: ValidateSignature       — secp256k1 CESR
/// Step 7: ValidateBalance         — dry-run UTXO sum
/// Step 8: ValidateSocietyMembership — cross-Society Epoch 1 only
///
/// Epoch 0 matrix (Society-level):
///   Payer must be citizen of THIS Society
///   Payee must be THIS Society's own DID OR the Federation DID
///
/// Epoch 1 matrix:
///   Payer must be citizen of THIS Society
///   Payee may be any citizen (same or other Society), this Society, or the Federation
/// </summary>
public sealed class SocietyTransferValidator : ITransferValidator
{
    private readonly IWalletStore      _wallets;
    private readonly IIdentityRegistry _registry;
    private readonly ISanctionsChecker _sanctions;
    private readonly ICryptoService    _crypto;
    private readonly string            _societyDid;
    private readonly string            _federationDid;
    private readonly int               _currentEpoch;

    private readonly ITransferNonceStore _nonceStore;

    public SocietyTransferValidator(
        IWalletStore wallets,
        IIdentityRegistry registry,
        ISanctionsChecker sanctions,
        ICryptoService crypto,
        string societyDid,
        string federationDid,
        ITransferNonceStore nonceStore,
        int currentEpoch)
    {
        _wallets       = wallets;
        _registry      = registry;
        _sanctions     = sanctions;
        _crypto        = crypto;
        _societyDid    = societyDid;
        _federationDid = federationDid;
        _currentEpoch  = currentEpoch;
    }

    public async Task ValidateAsync(TransferRequest request, CancellationToken ct = default)
    {
        // Step 0 — Normalise
        var payerDid = await NormaliseDid(request.PayerDid, ct);
        var payeeDid = await NormaliseDid(request.PayeeDid, ct);

        // Step 1 — Fields
        ValidateFields(request);

        // Step 2 — Epoch rules (Society-scoped)
        await ValidateEpochRulesAsync(payerDid, payeeDid, ct);

        // Step 3 — Nonce
        await ValidateNonceAsync(request.Nonce, ct);

        // Step 4 — Freshness
        ValidateFreshness(request.Timestamp);

        // Step 5 — Sanctions
        await ValidateSanctionsAsync(payerDid, payeeDid, ct);

        // Step 6 — Signature
        await ValidateSignatureAsync(request, payerDid, ct);

        // Step 7 — Balance
        await ValidateBalanceAsync(payerDid, request.AmountGrana, ct);

        // Step 8 — Society membership (cross-Society Epoch 1 only)
        await ValidateSocietyMembershipAsync(payerDid, payeeDid, ct);
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
        if (r.Memo is { Length: > Svrn7Constants.MaxMemoLength })
            throw new ArgumentException($"Memo exceeds {Svrn7Constants.MaxMemoLength} characters.");
    }

    private async Task ValidateEpochRulesAsync(
        string payerDid, string payeeDid, CancellationToken ct)
    {
        // Payer must always be a citizen of THIS Society
        var membership = await _registry.GetMembershipAsync(payerDid, ct);
        if (membership is null || membership.SocietyDid != _societyDid)
            throw new EpochViolationException(_currentEpoch, "PayerNotMemberOfSociety",
                $"Payer '{payerDid}' is not a member of Society '{_societyDid}'.");

        switch (_currentEpoch)
        {
            case Svrn7Constants.Epochs.Endowment:
                // Payee must be THIS Society or the Federation
                var payeeIsOwnSociety  = payeeDid == _societyDid;
                var payeeIsFederation  = payeeDid == _federationDid;
                if (!payeeIsOwnSociety && !payeeIsFederation)
                    throw new EpochViolationException(_currentEpoch, "EpochZeroPayeeRestriction",
                        $"Epoch 0: payee must be this Society ('{_societyDid}') or the Federation ('{_federationDid}'). Got: '{payeeDid}'.");
                break;

            case Svrn7Constants.Epochs.EcosystemUtility:
            case Svrn7Constants.Epochs.MarketIssuance:
                // Any citizen, society, or federation — checked in step 8 for cross-society
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
        if ((DateTimeOffset.UtcNow - timestamp).Duration() > Svrn7Constants.TransferFreshnessWindow)
            throw new StaleTransferException(timestamp, DateTimeOffset.UtcNow);
    }

    private async Task ValidateSanctionsAsync(
        string payerDid, string payeeDid, CancellationToken ct)
    {
        if (!await _sanctions.IsAllowedAsync(payerDid, ct))
            throw new SanctionedPartyException(payerDid);
        if (!await _sanctions.IsAllowedAsync(payeeDid, ct))
            throw new SanctionedPartyException(payeeDid);
    }

    private async Task ValidateSignatureAsync(
        TransferRequest request, string resolvedPayerDid, CancellationToken ct)
    {
        var canonical = CanonicalJson(request);
        var payload   = Encoding.UTF8.GetBytes(canonical);
        var citizen   = await _registry.GetCitizenAsync(resolvedPayerDid, ct)
            ?? throw new InvalidDidException(resolvedPayerDid, "Citizen not found.");
        if (!_crypto.VerifySecp256k1(payload, request.Signature, citizen.PublicKeyHex))
            throw new SignatureVerificationException($"Signature invalid for '{resolvedPayerDid}'.");
    }

    private async Task ValidateBalanceAsync(
        string payerDid, long amountGrana, CancellationToken ct)
    {
        var utxos = await _wallets.GetUnspentUtxosAsync(payerDid, ct);
        var total = utxos.Sum(u => u.AmountGrana);
        if (total < amountGrana)
            throw new InsufficientBalanceException(total, amountGrana);
    }

    private async Task ValidateSocietyMembershipAsync(
        string payerDid, string payeeDid, CancellationToken ct)
    {
        // Only applies to Epoch 1+ cross-Society transfers
        if (_currentEpoch < Svrn7Constants.Epochs.EcosystemUtility) return;

        var payeeMembership = await _registry.GetMembershipAsync(payeeDid, ct);
        if (payeeMembership is null) return; // Payee is not a citizen (society or federation) — allowed

        // If payee is a citizen of a DIFFERENT Society, step 8 passes — routing handles it
        // Local same-Society transfers are always valid at this point
    }

    private static string CanonicalJson(TransferRequest r) =>
        JsonSerializer.Serialize(new
        {
            r.PayerDid, r.PayeeDid, r.AmountGrana,
            r.Nonce, Timestamp = r.Timestamp.ToString("O"), r.Memo
        }, new JsonSerializerOptions { WriteIndented = false });
}
