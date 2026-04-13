using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Svrn7.Federation;
using Svrn7.Core;
using Svrn7.Core.Exceptions;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;
using Svrn7.DIDComm;

namespace Svrn7.Society;

/// <summary>
/// ISvrn7SocietyDriver implementation for SOVRONA (SVRN7) —
/// the Shared Reserve Currency (SRC) for the Web 7.0 digital ecosystem.
/// Wraps the Federation-level ISvrn7Driver via _inner and adds Society-scoped operations.
/// Cross-Society transfers use DIDComm SignThenEncrypt (TransferOrderCredential).
/// Overdraft draws use a synchronous DIDComm round-trip to the Federation.
/// </summary>
public sealed class Svrn7SocietyDriver : ISvrn7SocietyDriver
{
    private readonly ISvrn7Driver          _inner;
    private readonly IIdentityRegistry     _registry;
    private readonly IWalletStore          _wallets;
    private readonly IMerkleLog            _merkle;
    private readonly IVcService            _vcService;
    private readonly IVcRegistry           _vcRegistry;
    private readonly ICryptoService        _crypto;
    private readonly ISocietyMembershipStore _membershipStore;
    private readonly IDIDCommService       _didComm;
    private readonly IVcDocumentResolver   _vcResolver;
    private readonly Svrn7SocietyOptions   _opts;
    private readonly ILogger<Svrn7SocietyDriver> _log;
    private int _disposed;

    private void ThrowIfDisposed()
    {
        if (_disposed != 0)
            throw new ObjectDisposedException(nameof(Svrn7SocietyDriver));
    }

    public string SocietyDid => _opts.SocietyDid;

    // Delegate all ISvrn7Driver members to _inner
    public IDidDocumentRegistry DidRegistry => _inner.DidRegistry;
    public IVcRegistry          VcRegistry  => _inner.VcRegistry;

    public Svrn7SocietyDriver(
        ISvrn7Driver inner,
        IIdentityRegistry registry,
        IWalletStore wallets,
        IMerkleLog merkle,
        IVcService vcService,
        IVcRegistry vcRegistry,
        ICryptoService crypto,
        ISocietyMembershipStore membershipStore,
        IDIDCommService didComm,
        IVcDocumentResolver vcResolver,
        IOptions<Svrn7SocietyOptions> opts,
        ILogger<Svrn7SocietyDriver> log)
    {
        _inner           = inner;
        _registry        = registry;
        _wallets         = wallets;
        _merkle          = merkle;
        _vcService       = vcService;
        _vcRegistry      = vcRegistry;
        _crypto          = crypto;
        _membershipStore = membershipStore;
        _didComm         = didComm;
        _vcResolver      = vcResolver;
        _opts            = opts.Value;
        _log             = log;
    }

    // ── Society identity ──────────────────────────────────────────────────────

    public Task<SocietyRecord?> GetOwnSocietyAsync(CancellationToken ct = default)
        => _inner.GetSocietyAsync(_opts.SocietyDid, ct);

    // ── Society-scoped citizen registration ───────────────────────────────────

    public async Task<OperationResult> RegisterCitizenInSocietyAsync(
        RegisterCitizenInSocietyRequest request, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Did);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SocietyDid);

        if (request.SocietyDid != _opts.SocietyDid)
            return OperationResult.Fail(
                $"This driver manages Society '{_opts.SocietyDid}', not '{request.SocietyDid}'.");

        // Determine DID method name to use
        var methodName = request.PreferredMethodName ?? _opts.DidMethodName;

        // Validate method name is active for this Society
        var methodStatus = await _inner.GetDidMethodStatusAsync(methodName, ct);
        if (methodStatus != DidMethodStatus.Active)
            throw new DeregisteredDidMethodException(methodName, DateTimeOffset.UtcNow);

        // Check Society wallet balance — trigger overdraft draw if needed
        await EnsureSocietyHasSufficientFundsAsync(ct);

        // Transfer endowment from Society wallet to citizen wallet (will be created by RegisterCitizenAsync)
        var baseRequest = new RegisterCitizenRequest
        {
            Did             = request.Did,
            PublicKeyHex    = request.PublicKeyHex,
            PrivateKeyBytes = request.PrivateKeyBytes,
        };
        var result = await _inner.RegisterCitizenAsync(baseRequest, ct);
        if (!result.Success) return result;

        // Record society membership
        await _registry.StoreMembershipAsync(new SocietyMembershipRecord
        {
            CitizenPrimaryDid = request.Did,
            SocietyDid        = _opts.SocietyDid,
        }, ct);

        // If preferred method differs from primary, add an additional DID record
        if (request.PreferredMethodName is not null &&
            request.PreferredMethodName != _opts.DidMethodName)
        {
            var additionalDid = $"did:{request.PreferredMethodName}:{request.Did.Split(':')[^1]}";
            await _registry.StoreCitizenDidAsync(new CitizenDidRecord
            {
                CitizenPrimaryDid = request.Did,
                Did               = additionalDid,
                MethodName        = request.PreferredMethodName,
                IsPrimary         = false,
            }, ct);
        }

        _log.LogInformation("Citizen {Did} registered in Society {Society}", request.Did, _opts.SocietyDid);
        return result;
    }

    // ── Overdraft management ──────────────────────────────────────────────────

    private async Task EnsureSocietyHasSufficientFundsAsync(CancellationToken ct)
    {
        var balance = await _inner.GetBalanceGranaAsync(_opts.SocietyDid, ct);
        if (balance >= Svrn7Constants.CitizenEndowmentGrana) return;

        // Check overdraft ceiling
        var overdraft = await _membershipStore.GetOverdraftAsync(_opts.SocietyDid, ct);
        if (overdraft is null)
        {
            overdraft = new SocietyOverdraftRecord
            {
                SocietyDid           = _opts.SocietyDid,
                DrawAmountGrana      = _opts.DrawAmountGrana,
                OverdraftCeilingGrana= _opts.OverdraftCeilingGrana,
            };
            await _membershipStore.StoreOverdraftAsync(overdraft, ct);
        }

        if (overdraft.TotalOverdrawnGrana >= overdraft.OverdraftCeilingGrana)
            throw new SocietyEndowmentDepletedException(
                _opts.SocietyDid, overdraft.TotalOverdrawnGrana, overdraft.OverdraftCeilingGrana);

        // Send DIDComm OverdraftDrawRequest to Federation
        await RequestOverdraftDrawAsync(overdraft, ct);
    }

    private async Task RequestOverdraftDrawAsync(SocietyOverdraftRecord overdraft, CancellationToken ct)
    {
        var drawRequest = new OverdraftDrawRequest
        {
            SocietyDid      = _opts.SocietyDid,
            DrawAmountGrana = overdraft.DrawAmountGrana,
            DrawCount       = overdraft.DrawCount + 1,
            Reason          = "CitizenRegistration",
            RequestedAt     = DateTimeOffset.UtcNow,
        };

        var msg = _didComm.NewMessage()
            .Type(Svrn7Constants.Protocols.OverdraftDrawRequest)
            .To(_opts.FederationDid)
            .Body(drawRequest)
            .Build();

        // In production this sends via DIDComm transport and awaits receipt
        // For v1: log the request — transport adapter is a future enhancement
        _log.LogInformation(
            "Overdraft draw requested: {Amount} grana from Federation {Federation}",
            overdraft.DrawAmountGrana, _opts.FederationDid);

        // Update overdraft record
        overdraft.TotalOverdrawnGrana += overdraft.DrawAmountGrana;
        overdraft.LifetimeDrawsGrana  += overdraft.DrawAmountGrana;
        overdraft.DrawCount           += 1;
        overdraft.LastDrawAt           = DateTimeOffset.UtcNow;
        overdraft.Status               = overdraft.TotalOverdrawnGrana >= overdraft.OverdraftCeilingGrana
            ? OverdraftStatus.Ceiling
            : OverdraftStatus.Overdrawn;
        await _membershipStore.UpdateOverdraftAsync(overdraft, ct);
    }

    public async Task<OverdraftStatus> GetOverdraftStatusAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var rec = await _membershipStore.GetOverdraftAsync(_opts.SocietyDid, ct);
        return rec?.Status ?? OverdraftStatus.Clean;
    }

    public Task<SocietyOverdraftRecord?> GetOverdraftRecordAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _membershipStore.GetOverdraftAsync(_opts.SocietyDid, ct);
    }

    // ── Multi-DID citizen management ──────────────────────────────────────────

    public async Task<OperationResult> AddCitizenDidAsync(
        string citizenPrimaryDid, string methodName, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(citizenPrimaryDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);

        var status = await _inner.GetDidMethodStatusAsync(methodName, ct);
        if (status != DidMethodStatus.Active)
            throw new DeregisteredDidMethodException(methodName, DateTimeOffset.UtcNow);

        var identifier = citizenPrimaryDid.Split(':')[^1];
        var additionalDid = $"did:{methodName}:{identifier}";

        await _registry.StoreCitizenDidAsync(new CitizenDidRecord
        {
            CitizenPrimaryDid = citizenPrimaryDid,
            Did               = additionalDid,
            MethodName        = methodName,
            IsPrimary         = false,
        }, ct);

        _log.LogInformation("Additional DID {Did} added for citizen {Primary}", additionalDid, citizenPrimaryDid);
        return OperationResult.Ok(new { AdditionalDid = additionalDid });
    }

    // ── DIDComm transfer entry point ──────────────────────────────────────────

    public async Task<string> HandleIncomingTransferMessageAsync(
        string packedDIDCommMessage, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(packedDIDCommMessage);

        var unpacked = await _didComm.UnpackAsync(packedDIDCommMessage, ct);
        var type     = unpacked.Type;

        string responseJson;
        if (type == Svrn7Constants.Protocols.TransferRequest)
        {
            var request = JsonSerializer.Deserialize<TransferRequest>(unpacked.Body)
                ?? throw new InvalidOperationException("Invalid transfer request body.");
            var result = await _inner.TransferAsync(request, ct);
            responseJson = JsonSerializer.Serialize(result);
        }
        else if (type == Svrn7Constants.Protocols.TransferOrder)
        {
            var order = JsonSerializer.Deserialize<TransferOrderCredential>(unpacked.Body)
                ?? throw new InvalidOperationException("Invalid transfer order body.");
            responseJson = await ProcessIncomingTransferOrderAsync(order, ct);
        }
        else
        {
            _log.LogWarning("Unknown DIDComm message type: {Type}", type);
            responseJson = JsonSerializer.Serialize(OperationResult.Fail($"Unknown message type: {type}"));
        }

        // Pack and return response
        var response = _didComm.NewMessage()
            .Type(Svrn7Constants.Protocols.TransferReceipt)
            .To(unpacked.From ?? string.Empty)
            .Body(responseJson)
            .Build();

        return await _didComm.PackEncryptedAsync(response,
            Array.Empty<byte>(), Array.Empty<byte>(), DIDCommPackMode.Anoncrypt, ct);
    }

    private async Task<string> ProcessIncomingTransferOrderAsync(
        TransferOrderCredential order, CancellationToken ct)
    {
        // Idempotency check — TransferId is the Blake3 of the original transfer JSON
        var existingVc = await _vcRegistry.GetByIdAsync(order.TransferId, ct);
        if (existingVc is not null)
        {
            _log.LogInformation("Duplicate TransferOrder received (TransferId={Id}) — ignoring", order.TransferId);
            return JsonSerializer.Serialize(OperationResult.Ok(new { duplicate = true, order.TransferId }));
        }

        // Credit payee
        await _wallets.AddUtxoAsync(new Utxo
        {
            Id          = _crypto.Blake3Hex(Encoding.UTF8.GetBytes($"CREDIT:{order.TransferId}")),
            OwnerDid    = order.PayeeDid,
            AmountGrana = order.AmountGrana,
        }, ct);

        // Merkle log
        await _merkle.AppendAsync("TransferCredit",
            JsonSerializer.Serialize(new
            {
                transferId       = order.TransferId,
                payeeDid         = order.PayeeDid,
                amountGrana      = order.AmountGrana,
                originSocietyDid = order.OriginSocietyDid,
                creditedAt       = DateTimeOffset.UtcNow,
            }), ct);

        // Store receipt VC as proof of settlement
        var receipt = new TransferReceiptCredential
        {
            TransferId        = order.TransferId,
            PayeeDid          = order.PayeeDid,
            CreditedGrana     = order.AmountGrana,
            TargetSocietyDid  = _opts.SocietyDid,
            CreditedAt        = DateTimeOffset.UtcNow,
        };
        var receiptVc = new VcRecord
        {
            VcId       = order.TransferId, // use TransferId as VcId for idempotency
            IssuerDid  = _opts.SocietyDid,
            SubjectDid = order.PayeeDid,
            Types      = new List<string> { "VerifiableCredential", "TransferReceiptCredential" },
            VcHash     = _crypto.Blake3Hex(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(receipt))),
            JwtEncoded = JsonSerializer.Serialize(receipt),
            IssuedAt   = DateTimeOffset.UtcNow,
        };
        await _vcRegistry.StoreIfAbsentAsync(receiptVc, ct);

        _log.LogInformation("Cross-Society credit processed: {Amount} grana → {Payee}",
            order.AmountGrana, order.PayeeDid);
        return JsonSerializer.Serialize(OperationResult.Ok(receipt));
    }

    // ── Cross-Society transfers ────────────────────────────────────────────────

    public async Task<OperationResult> TransferToExternalCitizenAsync(
        TransferRequest request, string targetSocietyDid, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSocietyDid);

        // Validate that payer is an active citizen before attempting cross-Society transfer
        if (!await _inner.IsCitizenActiveAsync(request.PayerDid, ct))
            return OperationResult.Fail($"Payer '{request.PayerDid}' is not an active citizen.");

        // Execute local debit through the full 8-step validator
        var result = await _inner.TransferAsync(request, ct);
        if (!result.Success) return result;

        var txId = _crypto.Blake3Hex(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));

        // Build TransferOrder VC
        var order = new TransferOrderCredential
        {
            TransferId        = txId,
            PayerDid          = request.PayerDid,
            PayeeDid          = request.PayeeDid,
            AmountGrana       = request.AmountGrana,
            OriginSocietyDid  = _opts.SocietyDid,
            TargetSocietyDid  = targetSocietyDid,
            Epoch             = _inner.GetCurrentEpoch(),
            Nonce             = request.Nonce,
            Timestamp         = request.Timestamp,
            ExpiresAt         = DateTimeOffset.UtcNow.AddHours(24),
        };

        // Send via DIDComm to target Society
        var msg = _didComm.NewMessage()
            .Type(Svrn7Constants.Protocols.TransferOrder)
            .To(targetSocietyDid)
            .Body(order)
            .Build();

        _log.LogInformation(
            "TransferOrder {TxId} sent to Society {Target} for {Amount} grana",
            txId, targetSocietyDid, request.AmountGrana);

        return OperationResult.Ok(new { TxId = txId, Status = "OrderSent", TargetSociety = targetSocietyDid });
    }

    public async Task<OperationResult> TransferToFederationAsync(
        string payerDid, long amountGrana, string nonce, string signature,
        string? memo = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var request = new TransferRequest
        {
            PayerDid    = payerDid,
            PayeeDid    = _opts.FederationDid,
            AmountGrana = amountGrana,
            Nonce       = nonce,
            Timestamp   = DateTimeOffset.UtcNow,
            Signature   = signature,
            Memo        = memo,
        };
        return await _inner.TransferAsync(request, ct);
    }

    // ── Society membership ────────────────────────────────────────────────────

    public Task<IReadOnlyList<string>> GetMemberCitizenDidsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _registry.GetMemberCitizenDidsAsync(_opts.SocietyDid, ct);
    }

    public async Task<bool> IsMemberAsync(string citizenDid, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var primary    = await _registry.ResolveCitizenPrimaryDidAsync(citizenDid, ct) ?? citizenDid;
        var membership = await _registry.GetMembershipAsync(primary, ct);
        return membership?.SocietyDid == _opts.SocietyDid;
    }

    // ── DID method name management (self-service) ─────────────────────────────

    public Task<OperationResult> RegisterSocietyDidMethodAsync(
        string methodName, CancellationToken ct = default)
        => _inner.RegisterAdditionalDidMethodAsync(_opts.SocietyDid, methodName, ct);

    public Task<OperationResult> DeregisterSocietyDidMethodAsync(
        string methodName, CancellationToken ct = default)
        => _inner.DeregisterDidMethodAsync(_opts.SocietyDid, methodName, ct);

    public Task<IReadOnlyList<SocietyDidMethodRecord>> GetSocietyDidMethodsAsync(
        CancellationToken ct = default)
        => _inner.GetAllDidMethodsAsync(_opts.SocietyDid, ct: ct);

    // ── Cross-Society VC Document Resolution ─────────────────────────────────

    public Task<CrossSocietyVcQueryResult> FindVcsBySubjectAcrossSocietiesAsync(
        string subjectDid, TimeSpan? timeout = null, CancellationToken ct = default)
        => _vcResolver.FindBySubjectAcrossSocietiesAsync(subjectDid, timeout, ct);

    // ── ISvrn7Driver delegation ───────────────────────────────────────────────

    public int  GetCurrentEpoch()                             => _inner.GetCurrentEpoch();
    public Task AdvanceEpochAuthorisedAsync(int e, string r, string s, string? n, CancellationToken ct)
        => _inner.AdvanceEpochAuthorisedAsync(e, r, s, n, ct);
    public Task RecordEpochTransitionAsync(int e, string r, string? n, CancellationToken ct)
        => _inner.RecordEpochTransitionAsync(e, r, n, ct);
    public Task<OperationResult>  RegisterCitizenAsync(RegisterCitizenRequest r, CancellationToken ct) => _inner.RegisterCitizenAsync(r, ct);
    public Task<CitizenRecord?>   GetCitizenAsync(string d, CancellationToken ct)    => _inner.GetCitizenAsync(d, ct);
    public Task<bool>             IsCitizenActiveAsync(string d, CancellationToken ct)=> _inner.IsCitizenActiveAsync(d, ct);
    public Task<IReadOnlyList<CitizenDidRecord>> GetAllDidsForCitizenAsync(string d, CancellationToken ct) => _inner.GetAllDidsForCitizenAsync(d, ct);
    public Task<string?> ResolveCitizenPrimaryDidAsync(string d, CancellationToken ct) => _inner.ResolveCitizenPrimaryDidAsync(d, ct);
    public Task<OperationResult>  RegisterSocietyAsync(RegisterSocietyRequest r, CancellationToken ct) => _inner.RegisterSocietyAsync(r, ct);
    public Task<SocietyRecord?>   GetSocietyAsync(string d, CancellationToken ct)    => _inner.GetSocietyAsync(d, ct);
    public Task<bool>             IsSocietyActiveAsync(string d, CancellationToken ct)=> _inner.IsSocietyActiveAsync(d, ct);
    public Task DeactivateSocietyAsync(string d, CancellationToken ct)               => _inner.DeactivateSocietyAsync(d, ct);
    public Task<OperationResult>  RegisterAdditionalDidMethodAsync(string s, string m, CancellationToken ct) => _inner.RegisterAdditionalDidMethodAsync(s, m, ct);
    public Task<OperationResult>  DeregisterDidMethodAsync(string s, string m, CancellationToken ct) => _inner.DeregisterDidMethodAsync(s, m, ct);
    public Task<DidMethodStatus>  GetDidMethodStatusAsync(string m, CancellationToken ct)            => _inner.GetDidMethodStatusAsync(m, ct);
    public Task<IReadOnlyList<SocietyDidMethodRecord>> GetAllDidMethodsAsync(string? s, DidMethodStatus? f, CancellationToken ct) => _inner.GetAllDidMethodsAsync(s, f, ct);
    public Task<OperationResult>  TransferAsync(TransferRequest r, CancellationToken ct)             => _inner.TransferAsync(r, ct);
    public Task<IReadOnlyList<OperationResult>> BatchTransferAsync(IEnumerable<TransferRequest> r, CancellationToken ct) => _inner.BatchTransferAsync(r, ct);
    public Task<decimal>          GetBalanceSvrn7Async(string d, CancellationToken ct)  => _inner.GetBalanceSvrn7Async(d, ct);
    public Task<long>             GetBalanceGranaAsync(string d, CancellationToken ct)  => _inner.GetBalanceGranaAsync(d, ct);
    public Task<BalanceResult>    GetBalanceResultAsync(string d, CancellationToken ct) => _inner.GetBalanceResultAsync(d, ct);
    public Task<FederationRecord?> GetFederationAsync(CancellationToken ct)             => _inner.GetFederationAsync(ct);
    public Task<OperationResult>  UpdateFederationSupplyAsync(long n, string s, string r, CancellationToken ct) => _inner.UpdateFederationSupplyAsync(n, s, r, ct);
    public Task CreateDidAsync(DidDocument d, CancellationToken ct)                    => _inner.CreateDidAsync(d, ct);
    public Task UpdateDidAsync(DidDocument d, CancellationToken ct)                    => _inner.UpdateDidAsync(d, ct);
    public Task<DidResolutionResult> ResolveDidAsync(string d, CancellationToken ct)   => _inner.ResolveDidAsync(d, ct);
    public Task DeactivateDidAsync(string d, CancellationToken ct)                     => _inner.DeactivateDidAsync(d, ct);
    public Task SuspendDidAsync(string d, CancellationToken ct)                        => _inner.SuspendDidAsync(d, ct);
    public Task ReinstateDidAsync(string d, CancellationToken ct)                      => _inner.ReinstateDidAsync(d, ct);
    public Task<IReadOnlyList<DidDocument>> GetDidHistoryAsync(string d, CancellationToken ct) => _inner.GetDidHistoryAsync(d, ct);
    public Task<bool> IsDidActiveAsync(string d, CancellationToken ct)                 => _inner.IsDidActiveAsync(d, ct);
    public Task<string?> FindDidByPublicKeyAsync(string k, CancellationToken ct)       => _inner.FindDidByPublicKeyAsync(k, ct);
    public Task StoreVcAsync(VcRecord r, CancellationToken ct)                         => _inner.StoreVcAsync(r, ct);
    public Task<VcRecord?> GetVcByIdAsync(string id, CancellationToken ct)             => _inner.GetVcByIdAsync(id, ct);
    public Task<IReadOnlyList<VcRecord>> GetVcsBySubjectAsync(string d, CancellationToken ct) => _inner.GetVcsBySubjectAsync(d, ct);
    public Task<IReadOnlyList<VcRecord>> GetVcsByIssuerAsync(string d, CancellationToken ct)  => _inner.GetVcsByIssuerAsync(d, ct);
    public Task RevokeVcAsync(string id, string r, CancellationToken ct)               => _inner.RevokeVcAsync(id, r, ct);
    public Task SuspendVcAsync(string id, CancellationToken ct)                        => _inner.SuspendVcAsync(id, ct);
    public Task ReinstateVcAsync(string id, CancellationToken ct)                      => _inner.ReinstateVcAsync(id, ct);
    public Task<VcStatus> GetVcStatusAsync(string id, CancellationToken ct)            => _inner.GetVcStatusAsync(id, ct);
    public Task<int> ExpireStaleVcsAsync(CancellationToken ct)                         => _inner.ExpireStaleVcsAsync(ct);
    public Task<string>   AppendToLogAsync(string t, string p, CancellationToken ct)   => _inner.AppendToLogAsync(t, p, ct);
    public Task<string>   GetMerkleRootAsync(CancellationToken ct)                     => _inner.GetMerkleRootAsync(ct);
    public Task<TreeHead> SignMerkleTreeHeadAsync(CancellationToken ct)                 => _inner.SignMerkleTreeHeadAsync(ct);
    public Task<long>     GetLogSizeAsync(CancellationToken ct)                        => _inner.GetLogSizeAsync(ct);
    public Task<TreeHead?> GetLatestTreeHeadAsync(CancellationToken ct)                => _inner.GetLatestTreeHeadAsync(ct);
    public Task<OperationResult> ErasePersonAsync(string d, string s, DateTimeOffset t, CancellationToken ct) => _inner.ErasePersonAsync(d, s, t, ct);
    public Svrn7KeyPair GenerateSecp256k1KeyPair()                                     => _inner.GenerateSecp256k1KeyPair();
    public Svrn7KeyPair GenerateEd25519KeyPair()                                       => _inner.GenerateEd25519KeyPair();
    public string SignSecp256k1(byte[] p, byte[] k)                                    => _inner.SignSecp256k1(p, k);
    public bool   VerifySecp256k1(byte[] p, string s, string k)                       => _inner.VerifySecp256k1(p, s, k);
    public Task<string> Blake3HexAsync(byte[] d, CancellationToken ct)                => _inner.Blake3HexAsync(d, ct);
    public Task<string> Base58EncodeAsync(byte[] d, CancellationToken ct)             => _inner.Base58EncodeAsync(d, ct);
    public Task<int>    LiftAllWalletRestrictionsAsync(CancellationToken ct)           => _inner.LiftAllWalletRestrictionsAsync(ct);

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return _inner.DisposeAsync();
    }
}
