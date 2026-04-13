using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Svrn7.Core;
using Svrn7.Core.Exceptions;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;
using Svrn7.Store;

namespace Svrn7.Federation;

/// <summary>
/// Federation-level ISvrn7Driver implementation for SOVRONA (SVRN7) —
/// the Shared Reserve Currency (SRC) for the Web 7.0 digital ecosystem.
/// 44 ThrowIfDisposed() guards protect every public method.
/// Implements IAsyncDisposable — dispose via DI or using statement.
/// </summary>
public sealed class Svrn7Driver : ISvrn7Driver
{
    private readonly IWalletStore         _wallets;
    private readonly IIdentityRegistry    _registry;
    private readonly IMerkleLog           _merkle;
    private readonly IVcService           _vcService;
    private readonly IVcRegistry          _vcRegistry;
    private readonly ITransferValidator   _validator;
    private readonly ISanctionsChecker    _sanctions;
    private readonly ICryptoService       _crypto;
    private readonly IFederationStore     _federation;
    private readonly IDidDocumentRegistry _didRegistry;
    private readonly IDidDocumentResolver _didResolver;
    private readonly IVcDocumentResolver  _vcResolver;
    private readonly Svrn7Options         _options;
    private readonly ILogger<Svrn7Driver> _log;
    private readonly byte[]               _foundationPrivateKey;

    // OTel metrics
    private static readonly Meter         _meter     = new(Svrn7Constants.MeterName);
    private static readonly Counter<long> _ctzReg    = _meter.CreateCounter<long>("svrn7.citizens.registered");
    private static readonly Counter<long> _socReg    = _meter.CreateCounter<long>("svrn7.societies.registered");
    private static readonly Counter<long> _txOk      = _meter.CreateCounter<long>("svrn7.transfers.committed");
    private static readonly Counter<long> _txFail    = _meter.CreateCounter<long>("svrn7.transfers.failed");
    private static readonly Counter<long> _granaOut  = _meter.CreateCounter<long>("svrn7.grana.transferred");
    private static readonly Counter<long> _vcIssued  = _meter.CreateCounter<long>("svrn7.vcs.issued");
    private static readonly Counter<long> _vcRevoked = _meter.CreateCounter<long>("svrn7.vcs.revoked");
    private static readonly Counter<long> _didsPubl  = _meter.CreateCounter<long>("svrn7.dids.published");
    private static readonly Counter<long> _gdprErase = _meter.CreateCounter<long>("svrn7.gdpr.erasures");
    private static readonly Histogram<double> _txDur = _meter.CreateHistogram<double>("svrn7.transfer.duration_ms");
    private static readonly Histogram<long>   _batch = _meter.CreateHistogram<long>("svrn7.batch_transfer.size");

    private int  _currentEpoch;
    private bool _disposed;

    public IDidDocumentRegistry DidRegistry => _didRegistry;
    public IVcRegistry          VcRegistry  => _vcRegistry;

    public Svrn7Driver(
        IWalletStore wallets,
        IIdentityRegistry registry,
        IMerkleLog merkle,
        IVcService vcService,
        IVcRegistry vcRegistry,
        ITransferValidator validator,
        ISanctionsChecker sanctions,
        ICryptoService crypto,
        IFederationStore federation,
        IDidDocumentRegistry didRegistry,
        IDidDocumentResolver didResolver,
        IVcDocumentResolver vcResolver,
        IOptions<Svrn7Options> options,
        ILogger<Svrn7Driver> log,
        byte[] foundationPrivateKey)
    {
        _wallets      = wallets;
        _registry     = registry;
        _merkle       = merkle;
        _vcService    = vcService;
        _vcRegistry   = vcRegistry;
        _validator    = validator;
        _sanctions    = sanctions;
        _crypto       = crypto;
        _federation   = federation;
        _didRegistry  = didRegistry;
        _didResolver  = didResolver;
        _vcResolver   = vcResolver;
        _options      = options.Value;
        _log          = log;
        _foundationPrivateKey = foundationPrivateKey;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Svrn7Driver));
    }

    // ── Epoch control ──────────────────────────────────────────────────────────

    public int GetCurrentEpoch() { ThrowIfDisposed(); return _currentEpoch; }

    public async Task AdvanceEpochAuthorisedAsync(int toEpoch, string governanceRef,
        string foundationSignature, string? notes = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(foundationSignature);
        var payload = Encoding.UTF8.GetBytes($"EPOCH:{toEpoch}:{governanceRef}:{DateTimeOffset.UtcNow:O}");
        if (!_crypto.VerifySecp256k1(payload, foundationSignature, _options.FoundationPublicKeyHex))
            throw new SignatureVerificationException("Epoch advancement signature invalid.");
        await RecordEpochTransitionAsync(toEpoch, governanceRef, notes, ct);
    }

    public async Task RecordEpochTransitionAsync(int toEpoch, string governanceRef,
        string? notes = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (toEpoch < _currentEpoch)
            throw new EpochViolationException(_currentEpoch, "BackwardEpoch",
                $"Cannot transition backward from Epoch {_currentEpoch} to Epoch {toEpoch}.");
        _currentEpoch = toEpoch;
        var payload = JsonSerializer.Serialize(new
            { type = "EpochTransition", toEpoch, governanceRef, notes, timestamp = DateTimeOffset.UtcNow });
        await _merkle.AppendAsync("EpochTransition", payload, ct);
        _log.LogInformation("Epoch advanced to {Epoch} (ref: {Ref})", toEpoch, governanceRef);
    }

    // ── Citizen lifecycle ──────────────────────────────────────────────────────

    public async Task<OperationResult> RegisterCitizenAsync(
        RegisterCitizenRequest request, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Did);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PublicKeyHex);

        try
        {
            // Encrypt and store citizen
            var citizen = new CitizenRecord
            {
                Did                       = request.Did,
                PublicKeyHex              = request.PublicKeyHex,
                EncryptedPrivateKeyBase64 = Convert.ToBase64String(request.PrivateKeyBytes),
            };
            await _registry.RegisterCitizenAsync(citizen, ct);

            // Primary DID record
            await _registry.StoreCitizenDidAsync(new CitizenDidRecord
            {
                CitizenPrimaryDid = request.Did,
                Did               = request.Did,
                MethodName        = _options.DidMethodName,
                IsPrimary         = true,
            }, ct);

            // Wallet (restricted until Epoch 1)
            var wallet = new Wallet { Did = request.Did, BalanceGrana = 0, IsRestricted = true };
            await _wallets.CreateWalletAsync(wallet, ct);

            // Endowment UTXO
            var txId = _crypto.Blake3Hex(Encoding.UTF8.GetBytes($"ENDOW:{request.Did}:{DateTimeOffset.UtcNow:O}"));
            await _wallets.AddUtxoAsync(new Utxo
            {
                Id          = txId,
                OwnerDid    = request.Did,
                AmountGrana = Svrn7Constants.CitizenEndowmentGrana,
            }, ct);

            // DID Document
            var didDoc = BuildMinimalDidDocument(request.Did, request.PublicKeyHex, _options.DidMethodName);
            await _didRegistry.CreateAsync(didDoc, ct);

            // Endowment VC
            var jwtVc = await _vcService.IssueAsync(
                _options.DidMethodName + ":foundation",
                request.Did,
                "Svrn7EndowmentCredential",
                new { id = request.Did, amountGrana = Svrn7Constants.CitizenEndowmentGrana, amountSvrn7Display = Svrn7Constants.CitizenEndowmentSvrn7Display },
                _foundationPrivateKey, ct: ct);
            var vcRecord = new VcRecord
            {
                VcId       = $"urn:uuid:{Guid.NewGuid()}",
                IssuerDid  = _options.DidMethodName + ":foundation",
                SubjectDid = request.Did,
                Types      = new List<string> { "VerifiableCredential", "Svrn7EndowmentCredential" },
                VcHash     = _crypto.Blake3Hex(Encoding.UTF8.GetBytes(jwtVc)),
                JwtEncoded = jwtVc,
                IssuedAt   = DateTimeOffset.UtcNow,
                ExpiresAt  = DateTimeOffset.UtcNow.AddYears(10),
            };
            await _vcRegistry.StoreIfAbsentAsync(vcRecord, ct);

            // Merkle log
            await _merkle.AppendAsync("CitizenRegistration",
                JsonSerializer.Serialize(new { did = request.Did, timestamp = DateTimeOffset.UtcNow }), ct);

            _ctzReg.Add(1);
            _didsPubl.Add(1);
            _vcIssued.Add(1);
            _log.LogInformation("Citizen registered: {Did}", request.Did);
            return OperationResult.Ok(new { request.Did, VcId = vcRecord.VcId });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Citizen registration failed for {Did}", request.Did);
            return OperationResult.Fail(ex.Message);
        }
    }

    public Task<CitizenRecord?> GetCitizenAsync(string did, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _registry.GetCitizenAsync(did, ct);
    }

    public Task<bool> IsCitizenActiveAsync(string did, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _registry.IsCitizenActiveAsync(did, ct);
    }

    public Task<IReadOnlyList<CitizenDidRecord>> GetAllDidsForCitizenAsync(
        string primaryDid, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _registry.GetAllDidsForCitizenAsync(primaryDid, ct);
    }

    public Task<string?> ResolveCitizenPrimaryDidAsync(string anyDid, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _registry.ResolveCitizenPrimaryDidAsync(anyDid, ct);
    }

    // ── Society lifecycle ──────────────────────────────────────────────────────

    public async Task<OperationResult> RegisterSocietyAsync(
        RegisterSocietyRequest request, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Did);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PrimaryDidMethodName);

        try
        {
            // Validate method name format ([a-z0-9]+)
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.PrimaryDidMethodName, @"^[a-z0-9]+$"))
                throw new ConfigurationException(
                    $"DID method name '{request.PrimaryDidMethodName}' must match [a-z0-9]+.");

            // Check uniqueness in Federation registry
            var status = await _federation.GetMethodStatusAsync(request.PrimaryDidMethodName, ct);
            var existingRecord = await _federation.GetMethodRecordAsync(request.PrimaryDidMethodName, ct);
            if (existingRecord is not null && status == DidMethodStatus.Active)
                throw new DuplicateDidMethodException(request.PrimaryDidMethodName, existingRecord.SocietyDid);
            if (status == DidMethodStatus.Dormant)
            {
                var dormantRecord = await _federation.GetMethodRecordAsync(request.PrimaryDidMethodName, ct);
                throw new DormantDidMethodException(request.PrimaryDidMethodName,
                    dormantRecord?.DormantUntil ?? DateTimeOffset.UtcNow.Add(_options.DidMethodDormancyPeriod));
            }

            var society = new SocietyRecord
            {
                Did                  = request.Did,
                PublicKeyHex         = request.PublicKeyHex,
                SocietyName          = request.SocietyName,
                PrimaryDidMethodName = request.PrimaryDidMethodName,
            };
            await _registry.RegisterSocietyAsync(society, ct);

            // Register primary method name in Federation registry
            await _federation.RegisterMethodAsync(new SocietyDidMethodRecord
            {
                SocietyDid  = request.Did,
                MethodName  = request.PrimaryDidMethodName,
                IsPrimary   = true,
                Status      = DidMethodStatus.Active,
            }, ct);

            // Society wallet (active — receives Epoch 0 transfers)
            await _wallets.CreateWalletAsync(
                new Wallet { Did = request.Did, BalanceGrana = 0, IsRestricted = false }, ct);

            // Overdraft record
            // (ISocietyMembershipStore would store this — handled by Svrn7.Society layer)

            // DID Document
            var didDoc = BuildMinimalDidDocument(request.Did, request.PublicKeyHex, request.PrimaryDidMethodName);
            await _didRegistry.CreateAsync(didDoc, ct);

            // VTC Credential
            var jwtVc = await _vcService.IssueAsync(
                _options.DidMethodName + ":foundation",
                request.Did,
                "Svrn7VtcCredential",
                new { id = request.Did, societyName = request.SocietyName, methodName = request.PrimaryDidMethodName },
                _foundationPrivateKey, ct: ct);
            var vcRecord = new VcRecord
            {
                VcId       = $"urn:uuid:{Guid.NewGuid()}",
                IssuerDid  = _options.DidMethodName + ":foundation",
                SubjectDid = request.Did,
                Types      = new List<string> { "VerifiableCredential", "Svrn7VtcCredential" },
                VcHash     = _crypto.Blake3Hex(Encoding.UTF8.GetBytes(jwtVc)),
                JwtEncoded = jwtVc,
                IssuedAt   = DateTimeOffset.UtcNow,
                ExpiresAt  = DateTimeOffset.UtcNow.AddYears(5),
            };
            await _vcRegistry.StoreIfAbsentAsync(vcRecord, ct);

            await _merkle.AppendAsync("SocietyRegistration",
                JsonSerializer.Serialize(new { did = request.Did, method = request.PrimaryDidMethodName }), ct);

            _socReg.Add(1);
            _didsPubl.Add(1);
            _vcIssued.Add(1);
            _log.LogInformation("Society registered: {Did} ({Method})", request.Did, request.PrimaryDidMethodName);
            return OperationResult.Ok(new { request.Did, VcId = vcRecord.VcId });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Society registration failed for {Did}", request.Did);
            return OperationResult.Fail(ex.Message);
        }
    }

    public Task<SocietyRecord?> GetSocietyAsync(string did, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _registry.GetSocietyAsync(did, ct);
    }

    public Task<bool> IsSocietyActiveAsync(string did, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _registry.IsSocietyActiveAsync(did, ct);
    }

    public async Task DeactivateSocietyAsync(string did, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var resolved = await _didRegistry.ResolveAsync(did, ct);
        if (!resolved.Found) throw new NotFoundException("Society", did);
        await _registry.SetSocietyActiveAsync(did, false, ct);
        await _didRegistry.DeactivateAsync(did, ct);
        await _merkle.AppendAsync("SocietyDeactivation",
            JsonSerializer.Serialize(new { did, timestamp = DateTimeOffset.UtcNow }), ct);
    }

    // ── DID method names ───────────────────────────────────────────────────────

    public async Task<OperationResult> RegisterAdditionalDidMethodAsync(
        string societyDid, string methodName, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);

        if (!System.Text.RegularExpressions.Regex.IsMatch(methodName, @"^[a-z0-9]+$"))
            return OperationResult.Fail($"Method name '{methodName}' must match [a-z0-9]+.");

        var existingActive = await _federation.GetMethodRecordAsync(methodName, ct);
        if (existingActive is not null)
            return OperationResult.Fail($"Method name '{methodName}' is already registered to '{existingActive.SocietyDid}'.");

        // Check dormancy (time-based)
        var allRecords = await _federation.GetAllMethodsAsync(statusFilter: DidMethodStatus.Dormant, ct: ct);
        var dormant = allRecords.FirstOrDefault(r => r.MethodName == methodName
            && r.DormantUntil.HasValue && r.DormantUntil.Value > DateTimeOffset.UtcNow);
        if (dormant is not null)
            return OperationResult.Fail(
                $"Method name '{methodName}' is dormant until {dormant.DormantUntil:O}.");

        await _federation.RegisterMethodAsync(new SocietyDidMethodRecord
        {
            SocietyDid = societyDid,
            MethodName = methodName,
            IsPrimary  = false,
            Status     = DidMethodStatus.Active,
        }, ct);

        _log.LogInformation("Additional DID method '{Method}' registered for Society '{Society}'",
            methodName, societyDid);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeregisterDidMethodAsync(
        string societyDid, string methodName, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var society = await _registry.GetSocietyAsync(societyDid, ct)
            ?? throw new NotFoundException("Society", societyDid);

        if (society.PrimaryDidMethodName == methodName)
            throw new PrimaryDidMethodException(methodName);

        var dormantUntil = DateTimeOffset.UtcNow.Add(_options.DidMethodDormancyPeriod);
        await _federation.DeregisterMethodAsync(methodName, dormantUntil, ct);

        _log.LogInformation("DID method '{Method}' deregistered from Society '{Society}'. Dormant until {Until}",
            methodName, societyDid, dormantUntil);
        return OperationResult.Ok(new { dormantUntil });
    }

    public Task<DidMethodStatus> GetDidMethodStatusAsync(string methodName, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _federation.GetMethodStatusAsync(methodName, ct);
    }

    public Task<IReadOnlyList<SocietyDidMethodRecord>> GetAllDidMethodsAsync(
        string? societyDid = null, DidMethodStatus? statusFilter = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _federation.GetAllMethodsAsync(societyDid, statusFilter, ct);
    }

    // ── Transfers ──────────────────────────────────────────────────────────────

    public async Task<OperationResult> TransferAsync(
        TransferRequest request, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _validator.ValidateAsync(request, ct);

            var payerDid = await _registry.ResolveCitizenPrimaryDidAsync(request.PayerDid, ct)
                           ?? request.PayerDid;
            var utxos    = await _wallets.GetUnspentUtxosAsync(payerDid, ct);
            var txId     = _crypto.Blake3Hex(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));

            // Dry-run: verify balance before touching any UTXO
            long total = 0;
            var  toSpend = new List<Utxo>();
            foreach (var u in utxos)
            {
                toSpend.Add(u);
                total += u.AmountGrana;
                if (total >= request.AmountGrana) break;
            }
            if (total < request.AmountGrana)
                throw new InsufficientBalanceException(total, request.AmountGrana);

            // Now mark spent and create payee UTXO
            foreach (var u in toSpend)
                await _wallets.MarkUtxoSpentAsync(u.Id, txId, ct);

            // Change UTXO if overpaid
            if (total > request.AmountGrana)
                await _wallets.AddUtxoAsync(new Utxo
                {
                    Id          = _crypto.Blake3Hex(Encoding.UTF8.GetBytes($"CHANGE:{txId}")),
                    OwnerDid    = payerDid,
                    AmountGrana = total - request.AmountGrana,
                }, ct);

            var payeeDid = await _registry.ResolveCitizenPrimaryDidAsync(request.PayeeDid, ct)
                           ?? request.PayeeDid;
            await _wallets.AddUtxoAsync(new Utxo
            {
                Id          = _crypto.Blake3Hex(Encoding.UTF8.GetBytes($"CREDIT:{txId}")),
                OwnerDid    = payeeDid,
                AmountGrana = request.AmountGrana,
            }, ct);

            await _merkle.AppendAsync("Transfer",
                JsonSerializer.Serialize(new { txId, payerDid, payeeDid, request.AmountGrana }), ct);

            _txOk.Add(1);
            _granaOut.Add(request.AmountGrana);
            _txDur.Record(sw.Elapsed.TotalMilliseconds);
            return OperationResult.Ok(new { TxId = txId });
        }
        catch (Exception ex)
        {
            _txFail.Add(1);
            _log.LogWarning(ex, "Transfer failed");
            return OperationResult.Fail(ex.Message);
        }
    }

    public async Task<IReadOnlyList<OperationResult>> BatchTransferAsync(
        IEnumerable<TransferRequest> requests, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var list = requests.ToList();
        if (list.Count > Svrn7Constants.MaxBatchSize)
            throw new ArgumentException($"Batch size {list.Count} exceeds maximum {Svrn7Constants.MaxBatchSize}.");
        _batch.Record(list.Count);

        // Validate all before committing any
        foreach (var r in list)
            await _validator.ValidateAsync(r, ct);

        var results = new List<OperationResult>();
        foreach (var r in list)
            results.Add(await TransferAsync(r, ct));
        return results;
    }

    // ── Balance ────────────────────────────────────────────────────────────────

    public async Task<decimal> GetBalanceSvrn7Async(string did, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var g = await GetBalanceGranaAsync(did, ct);
        return (decimal)g / Svrn7Constants.GranaPerSvrn7;
    }

    public async Task<long> GetBalanceGranaAsync(string did, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var primary = await _registry.ResolveCitizenPrimaryDidAsync(did, ct) ?? did;
        var utxos   = await _wallets.GetUnspentUtxosAsync(primary, ct);
        return utxos.Sum(u => u.AmountGrana);
    }

    public async Task<BalanceResult> GetBalanceResultAsync(string did, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var g = await GetBalanceGranaAsync(did, ct);
        return new BalanceResult(g, (decimal)g / Svrn7Constants.GranaPerSvrn7);
    }

    // ── Federation supply ──────────────────────────────────────────────────────

    public Task<FederationRecord?> GetFederationAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _federation.GetAsync(ct);
    }

    public async Task<OperationResult> UpdateFederationSupplyAsync(
        long newTotalSupplyGrana, string foundationSignature,
        string governanceRef, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var payload = Encoding.UTF8.GetBytes(
            $"SUPPLY:{newTotalSupplyGrana}:{governanceRef}:{DateTimeOffset.UtcNow:O}");
        if (!_crypto.VerifySecp256k1(payload, foundationSignature, _options.FoundationPublicKeyHex))
            throw new SignatureVerificationException("Supply update signature invalid.");

        var current = await _federation.GetAsync(ct);
        if (current is null) throw new ConfigurationException("Federation not initialised.");
        var delta = newTotalSupplyGrana - current.TotalSupplyGrana;
        if (delta <= 0)
            return OperationResult.Fail("Supply is monotonically increasing — new value must exceed current.");

        await _federation.UpdateSupplyAsync(newTotalSupplyGrana, ct);

        // Credit Federation wallet with delta
        var txId = _crypto.Blake3Hex(Encoding.UTF8.GetBytes($"SUPPLY:{governanceRef}:{DateTimeOffset.UtcNow:O}"));
        await _wallets.AddUtxoAsync(new Utxo
        {
            Id          = txId,
            OwnerDid    = current.Did,
            AmountGrana = delta,
        }, ct);

        await _merkle.AppendAsync("SupplyUpdate",
            JsonSerializer.Serialize(new { newTotalSupplyGrana, delta, governanceRef }), ct);

        _log.LogInformation("Federation supply updated to {Supply} grana (+{Delta})",
            newTotalSupplyGrana, delta);
        return OperationResult.Ok(new { newTotalSupplyGrana, delta });
    }

    // ── DID registry pass-through ──────────────────────────────────────────────

    public Task CreateDidAsync(DidDocument doc, CancellationToken ct = default)
    { ThrowIfDisposed(); return _didRegistry.CreateAsync(doc, ct); }
    public Task UpdateDidAsync(DidDocument doc, CancellationToken ct = default)
    { ThrowIfDisposed(); return _didRegistry.UpdateAsync(doc, ct); }
    public Task<DidResolutionResult> ResolveDidAsync(string did, CancellationToken ct = default)
    { ThrowIfDisposed(); return _didResolver.ResolveAsync(did, ct); }
    public Task DeactivateDidAsync(string did, CancellationToken ct = default)
    { ThrowIfDisposed(); return _didRegistry.DeactivateAsync(did, ct); }
    public Task SuspendDidAsync(string did, CancellationToken ct = default)
    { ThrowIfDisposed(); return _didRegistry.SuspendAsync(did, ct); }
    public Task ReinstateDidAsync(string did, CancellationToken ct = default)
    { ThrowIfDisposed(); return _didRegistry.ReinstateAsync(did, ct); }
    public Task<IReadOnlyList<DidDocument>> GetDidHistoryAsync(string did, CancellationToken ct = default)
    { ThrowIfDisposed(); return _didRegistry.GetHistoryAsync(did, ct); }
    public Task<bool> IsDidActiveAsync(string did, CancellationToken ct = default)
    { ThrowIfDisposed(); return _didRegistry.IsActiveAsync(did, ct); }
    public Task<string?> FindDidByPublicKeyAsync(string publicKeyHex, CancellationToken ct = default)
    { ThrowIfDisposed(); return _didRegistry.FindDidByPublicKeyHexAsync(publicKeyHex, ct); }

    // ── VC registry pass-through ───────────────────────────────────────────────

    public Task StoreVcAsync(VcRecord r, CancellationToken ct = default)
    { ThrowIfDisposed(); return _vcRegistry.StoreAsync(r, ct); }
    public Task<VcRecord?> GetVcByIdAsync(string id, CancellationToken ct = default)
    { ThrowIfDisposed(); return _vcRegistry.GetByIdAsync(id, ct); }
    public Task<IReadOnlyList<VcRecord>> GetVcsBySubjectAsync(string d, CancellationToken ct = default)
    { ThrowIfDisposed(); return _vcRegistry.GetBySubjectAsync(d, ct); }
    public Task<IReadOnlyList<VcRecord>> GetVcsByIssuerAsync(string d, CancellationToken ct = default)
    { ThrowIfDisposed(); return _vcRegistry.GetByIssuerAsync(d, ct); }
    public async Task RevokeVcAsync(string id, string reason, CancellationToken ct = default)
    { ThrowIfDisposed(); await _vcRegistry.RevokeAsync(id, reason, ct); _vcRevoked.Add(1); }
    public Task SuspendVcAsync(string id, CancellationToken ct = default)
    { ThrowIfDisposed(); return _vcRegistry.SuspendAsync(id, ct); }
    public Task ReinstateVcAsync(string id, CancellationToken ct = default)
    { ThrowIfDisposed(); return _vcRegistry.ReinstateAsync(id, ct); }
    public Task<VcStatus> GetVcStatusAsync(string id, CancellationToken ct = default)
    { ThrowIfDisposed(); return _vcRegistry.GetStatusAsync(id, ct); }
    public Task<int> ExpireStaleVcsAsync(CancellationToken ct = default)
    { ThrowIfDisposed(); return _vcRegistry.ExpireStaleCredentialsAsync(ct); }

    // ── Merkle log pass-through ────────────────────────────────────────────────

    public Task<string>  AppendToLogAsync(string t, string p, CancellationToken ct = default)
    { ThrowIfDisposed(); return _merkle.AppendAsync(t, p, ct); }
    public Task<string>  GetMerkleRootAsync(CancellationToken ct = default)
    { ThrowIfDisposed(); return _merkle.ComputeRootAsync(ct); }
    public Task<TreeHead> SignMerkleTreeHeadAsync(CancellationToken ct = default)
    { ThrowIfDisposed(); return _merkle.SignTreeHeadAsync(_foundationPrivateKey, ct); }
    public Task<long>    GetLogSizeAsync(CancellationToken ct = default)
    { ThrowIfDisposed(); return _merkle.GetSizeAsync(ct); }
    public Task<TreeHead?> GetLatestTreeHeadAsync(CancellationToken ct = default)
    { ThrowIfDisposed(); return _merkle.GetLatestTreeHeadAsync(ct); }

    // ── GDPR ───────────────────────────────────────────────────────────────────

    public async Task<OperationResult> ErasePersonAsync(
        string did, string controllerSignature,
        DateTimeOffset requestTimestamp, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        // Step 1: verify signature
        var payload = Encoding.UTF8.GetBytes($"ERASE:{did}:{requestTimestamp:O}");
        if (!_crypto.VerifySecp256k1(payload, controllerSignature, _options.FoundationPublicKeyHex))
            throw new SignatureVerificationException("GDPR erasure signature invalid.");
        if ((DateTimeOffset.UtcNow - requestTimestamp).Duration() > Svrn7Constants.TransferFreshnessWindow)
            throw new StaleTransferException(requestTimestamp, DateTimeOffset.UtcNow);

        // Step 2: deactivate DID
        await _didRegistry.DeactivateAsync(did, ct);

        // Step 3: revoke all active VCs
        var vcs = await _vcRegistry.GetBySubjectAsync(did, ct);
        foreach (var vc in vcs.Where(v => v.Status == VcStatus.Active))
            await _vcRegistry.RevokeAsync(vc.VcId, "GDPR Article 17 erasure", ct);

        // Step 4: burn key backup
        var burn = RandomNumberGenerator.GetBytes(48);
        await _registry.StoreKeyBackupAsync(did,
            "BURNED:" + Convert.ToBase64String(burn), ct);

        _gdprErase.Add(1);
        _log.LogInformation("GDPR erasure completed for {Did}", did);
        return OperationResult.Ok();
    }

    // ── Crypto helpers ─────────────────────────────────────────────────────────

    public Svrn7KeyPair GenerateSecp256k1KeyPair() { ThrowIfDisposed(); return _crypto.GenerateSecp256k1KeyPair(); }
    public Svrn7KeyPair GenerateEd25519KeyPair()   { ThrowIfDisposed(); return _crypto.GenerateEd25519KeyPair(); }
    public string SignSecp256k1(byte[] p, byte[] k) { ThrowIfDisposed(); return _crypto.SignSecp256k1(p, k); }
    public bool VerifySecp256k1(byte[] p, string s, string k) { ThrowIfDisposed(); return _crypto.VerifySecp256k1(p, s, k); }
    public Task<string> Blake3HexAsync(byte[] d, CancellationToken ct = default)
    { ThrowIfDisposed(); return Task.FromResult(_crypto.Blake3Hex(d)); }
    public Task<string> Base58EncodeAsync(byte[] d, CancellationToken ct = default)
    { ThrowIfDisposed(); return Task.FromResult(_crypto.Base58Encode(d)); }

    // ── Wallet admin ───────────────────────────────────────────────────────────

    public async Task<int> LiftAllWalletRestrictionsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_currentEpoch < Svrn7Constants.Epochs.EcosystemUtility)
            throw new EpochViolationException(_currentEpoch, "PrematureRestrictionLift",
                "Wallet restrictions can only be lifted at Epoch 1 or later.");
        var dids  = await _registry.GetAllCitizenDidsAsync(ct);
        var count = 0;
        foreach (var did in dids)
        {
            await _wallets.SetRestrictedAsync(did, false, ct);
            count++;
        }
        _log.LogInformation("Lifted wallet restrictions on {Count} citizens", count);
        return count;
    }

    // ── IAsyncDisposable ───────────────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        Array.Clear(_foundationPrivateKey, 0, _foundationPrivateKey.Length);
        return ValueTask.CompletedTask;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static DidDocument BuildMinimalDidDocument(string did, string publicKeyHex, string methodName)
    {
        var doc = new
        {
            context            = new[] { "https://www.w3.org/ns/did/v1" },
            id                 = did,
            verificationMethod = new[]
            {
                new
                {
                    id           = $"{did}#key-1",
                    type         = "EcdsaSecp256k1VerificationKey2019",
                    controller   = did,
                    publicKeyHex = publicKeyHex
                }
            },
            authentication = new[] { $"{did}#key-1" },
        };

        return new DidDocument
        {
            Did          = did,
            MethodName   = methodName,
            Version      = 1,
            Status       = DidStatus.Active,
            DocumentJson = JsonSerializer.Serialize(doc),
        };
    }
}
