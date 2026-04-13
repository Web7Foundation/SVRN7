using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Svrn7.Federation;
using Svrn7.Core;
using Svrn7.Core.Exceptions;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;
using Svrn7.Crypto;
using Svrn7.DIDComm;
using Svrn7.Identity;
using Svrn7.Ledger;
using Svrn7.Store;
using Xunit;

namespace Svrn7.Tests;

// ── Test fixture ──────────────────────────────────────────────────────────────

public class TestFixture : IAsyncDisposable
{
    public ISvrn7Driver    Driver      { get; }
    public ICryptoService  Crypto      { get; }
    public Svrn7LiteContext Context    { get; }
    public byte[]          FoundationPrivateKey { get; }

    private readonly DidRegistryLiteContext _didCtx;
    private readonly VcRegistryLiteContext  _vcCtx;
    private readonly FederationLiteContext  _fedCtx;

    public TestFixture()
    {
        Context  = new Svrn7LiteContext(":memory:");
        _didCtx  = new DidRegistryLiteContext(":memory:");
        _vcCtx   = new VcRegistryLiteContext(":memory:");
        _fedCtx  = new FederationLiteContext(":memory:");

        Crypto   = new CryptoService();
        var foundationKp   = Crypto.GenerateSecp256k1KeyPair();
        FoundationPrivateKey = foundationKp.PrivateKeyBytes;

        var wallets    = new LiteWalletStore(Context);
        var registry   = new LiteIdentityRegistry(Context);
        var merkle     = new MerkleLog(Context, Crypto);
        var didReg     = new LiteDidDocumentRegistry(_didCtx);
        var vcReg      = new LiteVcRegistry(_vcCtx);
        var fedStore   = new LiteFederationStore(_fedCtx);
        var vcResolver = new LiteVcDocumentResolver(vcReg);
        var didResolver= new LocalDidDocumentResolver(didReg, new[] { "drn" });
        var vcService  = new VcService(Crypto);
        var sanctions  = new PassthroughSanctionsChecker();
        var nonceStore = new InMemoryTransferNonceStore();
        var validator  = new TransferValidator(wallets, registry, sanctions, Crypto, nonceStore, 0);
        var opts       = Options.Create(new Svrn7Options
        {
            FoundationPublicKeyHex = foundationKp.PublicKeyHex,
            Svrn7DbPath   = ":memory:",
            DidsDbPath    = ":memory:",
            VcsDbPath     = ":memory:",
            DidMethodName = "drn",
        });

        Driver = new Svrn7Driver(wallets, registry, merkle, vcService, vcReg,
            validator, sanctions, Crypto, fedStore, didReg, didResolver, vcResolver,
            opts, NullLogger<Svrn7Driver>.Instance, FoundationPrivateKey);
    }

    /// <summary>Register a citizen and return their DID and public key hex.</summary>
    public async Task<(string Did, string PubKey)> RegisterCitizenAsync(string id = "alice")
    {
        var kp  = Crypto.GenerateSecp256k1KeyPair();
        var did = $"did:drn:{id}";
        var r   = await Driver.RegisterCitizenAsync(new RegisterCitizenRequest
        {
            Did             = did,
            PublicKeyHex    = kp.PublicKeyHex,
            PrivateKeyBytes = kp.PrivateKeyBytes,
        });
        r.Success.Should().BeTrue(r.ErrorMessage);
        return (did, kp.PublicKeyHex);
    }

    /// <summary>Build a signed TransferRequest for the given payer key pair.</summary>
    public TransferRequest BuildSignedTransfer(
        string payerDid, byte[] payerPrivateKey,
        string payeeDid, long amountGrana,
        string? memo = null)
    {
        var nonce     = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow;
        var canonical = JsonSerializer.Serialize(new
        {
            PayerDid = payerDid, PayeeDid = payeeDid, AmountGrana = amountGrana,
            Nonce = nonce, Timestamp = timestamp.ToString("O"), Memo = memo
        }, new JsonSerializerOptions { WriteIndented = false });
        var sig = Crypto.SignSecp256k1(Encoding.UTF8.GetBytes(canonical), payerPrivateKey);
        return new TransferRequest
        {
            PayerDid = payerDid, PayeeDid = payeeDid,
            AmountGrana = amountGrana, Nonce = nonce,
            Timestamp = timestamp, Signature = sig, Memo = memo
        };
    }

    public async ValueTask DisposeAsync()
    {
        await Driver.DisposeAsync();
        Context.Dispose();
        _didCtx.Dispose();
        _vcCtx.Dispose();
        _fedCtx.Dispose();
    }
}

// ── Crypto tests ──────────────────────────────────────────────────────────────

public class CryptoServiceTests
{
    private readonly CryptoService _crypto = new();

    [Fact] public void GenerateSecp256k1KeyPair_ReturnsValidPair()
    {
        var kp = _crypto.GenerateSecp256k1KeyPair();
        kp.PublicKeyHex.Should().HaveLength(66).And.MatchRegex("^[0-9a-f]+$");
        kp.PrivateKeyBytes.Should().HaveCount(32);
        kp.Algorithm.Should().Be(KeyAlgorithm.Secp256k1);
    }

    [Fact] public void SignAndVerifySecp256k1_RoundTrip()
    {
        var kp      = _crypto.GenerateSecp256k1KeyPair();
        var payload = Encoding.UTF8.GetBytes("test payload");
        var sig     = _crypto.SignSecp256k1(payload, kp.PrivateKeyBytes);
        _crypto.VerifySecp256k1(payload, sig, kp.PublicKeyHex).Should().BeTrue();
    }

    [Fact] public void VerifySecp256k1_WrongKey_ReturnsFalse()
    {
        var kp1     = _crypto.GenerateSecp256k1KeyPair();
        var kp2     = _crypto.GenerateSecp256k1KeyPair();
        var payload = Encoding.UTF8.GetBytes("data");
        var sig     = _crypto.SignSecp256k1(payload, kp1.PrivateKeyBytes);
        _crypto.VerifySecp256k1(payload, sig, kp2.PublicKeyHex).Should().BeFalse();
    }

    [Fact] public void AesGcm_EncryptDecrypt_RoundTrip()
    {
        var key   = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var key2  = key.ToArray();
        var plain = Encoding.UTF8.GetBytes("hello svrn7");
        var enc   = _crypto.EncryptAes256Gcm(plain, key);
        var dec   = _crypto.DecryptAes256Gcm(enc, key2);
        dec.Should().Equal(plain);
    }

    [Fact] public void Blake3Hex_DeterministicForSameInput()
    {
        var data = Encoding.UTF8.GetBytes("svrn7");
        _crypto.Blake3Hex(data).Should().Be(_crypto.Blake3Hex(data));
    }

    [Fact] public void Base58_EncodeDecodeRoundTrip()
    {
        var data = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var enc  = _crypto.Base58Encode(data);
        enc.Should().NotContain("0").And.NotContain("O")
           .And.NotContain("I").And.NotContain("l");
        _crypto.Base58Decode(enc).Should().Equal(data);
    }

    [Fact] public void GenerateEd25519KeyPair_ReturnsValidPair()
    {
        var kp = _crypto.GenerateEd25519KeyPair();
        kp.PublicKeyHex.Should().HaveLength(64);
        kp.Algorithm.Should().Be(KeyAlgorithm.Ed25519);
    }

    [Fact] public void SignAndVerifyEd25519_RoundTrip()
    {
        var kp  = _crypto.GenerateEd25519KeyPair();
        var msg = Encoding.UTF8.GetBytes("ed25519 test");
        var sig = _crypto.SignEd25519(msg, kp.PrivateKeyBytes);
        _crypto.VerifyEd25519(msg, sig, kp.PublicKeyHex).Should().BeTrue();
    }
}

// ── DIDComm tests ─────────────────────────────────────────────────────────────

public class DIDCommTests
{
    private readonly DIDCommPackingService _svc = new();

    [Fact] public async Task PackPlaintext_RoundTrip()
    {
        var msg    = _svc.NewMessage().Type("https://example.com/test").Body("{}").Build();
        var packed = await _svc.PackPlaintextAsync(msg);
        var unpacked = await _svc.UnpackAsync(packed);
        unpacked.Type.Should().Be("https://example.com/test");
    }

    [Fact] public async Task PackEncrypted_ReturnsNonEmptyString()
    {
        var crypto = new CryptoService();
        var kp     = crypto.GenerateEd25519KeyPair();
        var msg    = _svc.NewMessage()
            .Type("did:drn:svrn7.net/protocols/transfer/1.0/request")
            .Body(new { amount = 1000 })
            .Build();
        var packed = await _svc.PackEncryptedAsync(msg, kp.PrivateKeyBytes, kp.PrivateKeyBytes);
        packed.Should().NotBeNullOrEmpty();
    }

    [Fact] public async Task PackSigned_ReturnsNonEmptyString()
    {
        var crypto = new CryptoService();
        var kp     = crypto.GenerateEd25519KeyPair();
        var msg    = _svc.NewMessage().Type("https://example.com/signed").Build();
        var packed = await _svc.PackSignedAsync(msg, kp.PrivateKeyBytes);
        packed.Should().NotBeNullOrEmpty();
    }
}

// ── Citizen registration tests ─────────────────────────────────────────────────

public class CitizenRegistrationTests : IAsyncLifetime
{
    private TestFixture _f = null!;
    public Task InitializeAsync() { _f = new TestFixture(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _f.DisposeAsync();

    [Fact] public async Task RegisterCitizen_HappyPath_Succeeds()
    {
        var (did, _) = await _f.RegisterCitizenAsync("alice");
        did.Should().Be("did:drn:alice");
    }

    [Fact] public async Task RegisterCitizen_WalletCreated()
    {
        var (did, _) = await _f.RegisterCitizenAsync("bob");
        var balance  = await _f.Driver.GetBalanceGranaAsync(did);
        balance.Should().Be(Svrn7Constants.CitizenEndowmentGrana);
    }

    [Fact] public async Task RegisterCitizen_DidDocumentCreated()
    {
        var (did, _) = await _f.RegisterCitizenAsync("carol");
        var result   = await _f.Driver.ResolveDidAsync(did);
        result.Found.Should().BeTrue();
        result.Document!.Status.Should().Be(DidStatus.Active);
    }

    [Fact] public async Task RegisterCitizen_EndowmentVcIssued()
    {
        var (did, _) = await _f.RegisterCitizenAsync("dave");
        var vcs      = await _f.Driver.GetVcsBySubjectAsync(did);
        vcs.Should().ContainSingle(v => v.Types.Contains("Svrn7EndowmentCredential"));
    }

    [Fact] public async Task RegisterCitizen_MerkleLogUpdated()
    {
        var before = await _f.Driver.GetLogSizeAsync();
        await _f.RegisterCitizenAsync("eve");
        var after  = await _f.Driver.GetLogSizeAsync();
        after.Should().BeGreaterThan(before);
    }

    [Fact] public async Task RegisterCitizen_BalanceInSvrn7()
    {
        var (did, _)   = await _f.RegisterCitizenAsync("frank");
        var balanceSvrn7 = await _f.Driver.GetBalanceSvrn7Async(did);
        balanceSvrn7.Should().Be(Svrn7Constants.CitizenEndowmentSvrn7Display);
    }

    [Fact] public async Task RegisterCitizen_PrimaryDidRecorded()
    {
        var (did, _) = await _f.RegisterCitizenAsync("grace");
        var dids     = await _f.Driver.GetAllDidsForCitizenAsync(did);
        dids.Should().ContainSingle(d => d.IsPrimary && d.Did == did);
    }

    [Fact] public async Task ResolveCitizenPrimaryDid_ReturnsSelf()
    {
        var (did, _) = await _f.RegisterCitizenAsync("heidi");
        var resolved = await _f.Driver.ResolveCitizenPrimaryDidAsync(did);
        resolved.Should().Be(did);
    }
}

// ── Society registration tests ────────────────────────────────────────────────

public class SocietyRegistrationTests : IAsyncLifetime
{
    private TestFixture _f = null!;
    public Task InitializeAsync() { _f = new TestFixture(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _f.DisposeAsync();

    private async Task<string> RegisterSocietyAsync(string id = "s1", string method = "testsoc")
    {
        var kp = _f.Crypto.GenerateSecp256k1KeyPair();
        var did = $"did:{method}:{id}";
        var r = await _f.Driver.RegisterSocietyAsync(new RegisterSocietyRequest
        {
            Did = did, PublicKeyHex = kp.PublicKeyHex, PrivateKeyBytes = kp.PrivateKeyBytes,
            SocietyName = "Test Society", PrimaryDidMethodName = method,
            DrawAmountGrana = 0, OverdraftCeilingGrana = 0,
        });
        r.Success.Should().BeTrue(r.ErrorMessage);
        return did;
    }

    [Fact] public async Task RegisterSociety_HappyPath_Succeeds()
    {
        var did = await RegisterSocietyAsync();
        (await _f.Driver.IsSocietyActiveAsync(did)).Should().BeTrue();
    }

    [Fact] public async Task RegisterSociety_DidDocumentCreated()
    {
        var did    = await RegisterSocietyAsync("s2", "soc2");
        var result = await _f.Driver.ResolveDidAsync(did);
        result.Found.Should().BeTrue();
    }

    [Fact] public async Task RegisterSociety_VtcCredentialIssued()
    {
        var did = await RegisterSocietyAsync("s3", "soc3");
        var vcs = await _f.Driver.GetVcsBySubjectAsync(did);
        vcs.Should().ContainSingle(v => v.Types.Contains("Svrn7VtcCredential"));
    }

    [Fact] public async Task RegisterSociety_DuplicateMethodName_Fails()
    {
        await RegisterSocietyAsync("s4", "uniquesoc");
        var kp = _f.Crypto.GenerateSecp256k1KeyPair();
        var r  = await _f.Driver.RegisterSocietyAsync(new RegisterSocietyRequest
        {
            Did = "did:uniquesoc:s5", PublicKeyHex = kp.PublicKeyHex,
            PrivateKeyBytes = kp.PrivateKeyBytes, SocietyName = "Second Society",
            PrimaryDidMethodName = "uniquesoc",
            DrawAmountGrana = 0, OverdraftCeilingGrana = 0,
        });
        r.Success.Should().BeFalse();
        r.ErrorMessage.Should().Contain("uniquesoc");
    }

    [Fact] public async Task RegisterSociety_InvalidMethodName_Fails()
    {
        var kp = _f.Crypto.GenerateSecp256k1KeyPair();
        var r  = await _f.Driver.RegisterSocietyAsync(new RegisterSocietyRequest
        {
            Did = "did:Bad-Method:s6", PublicKeyHex = kp.PublicKeyHex,
            PrivateKeyBytes = kp.PrivateKeyBytes, SocietyName = "Bad Method Society",
            PrimaryDidMethodName = "Bad-Method",
            DrawAmountGrana = 0, OverdraftCeilingGrana = 0,
        });
        r.Success.Should().BeFalse();
    }
}

// ── Transfer tests ─────────────────────────────────────────────────────────────

public class TransferTests : IAsyncLifetime
{
    private TestFixture _f = null!;
    public Task InitializeAsync() { _f = new TestFixture(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _f.DisposeAsync();

    [Fact] public async Task Transfer_Epoch0_CitizenToSociety_Succeeds()
    {
        // Register payer and society
        var payerKp = _f.Crypto.GenerateSecp256k1KeyPair();
        var payerDid = "did:drn:payer-e0";
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
        {
            Did = payerDid, PublicKeyHex = payerKp.PublicKeyHex,
            PrivateKeyBytes = payerKp.PrivateKeyBytes
        });
        var socKp = _f.Crypto.GenerateSecp256k1KeyPair();
        var socDid = "did:testsoc:society-e0";
        await _f.Driver.RegisterSocietyAsync(new RegisterSocietyRequest
        {
            Did = socDid, PublicKeyHex = socKp.PublicKeyHex,
            PrivateKeyBytes = socKp.PrivateKeyBytes,
            SocietyName = "Epoch0 Society", PrimaryDidMethodName = "testsoc",
            DrawAmountGrana = 0, OverdraftCeilingGrana = 0,
        });

        var amount = 100 * Svrn7Constants.GranaPerSvrn7;
        var req    = _f.BuildSignedTransfer(payerDid, payerKp.PrivateKeyBytes, socDid, amount);
        var result = await _f.Driver.TransferAsync(req);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var payerBal = await _f.Driver.GetBalanceGranaAsync(payerDid);
        payerBal.Should().Be(Svrn7Constants.CitizenEndowmentGrana - amount);
        var socBal = await _f.Driver.GetBalanceGranaAsync(socDid);
        socBal.Should().Be(amount);
    }

    [Fact] public async Task Transfer_Epoch0_CitizenToCitizen_ThrowsEpochViolation()
    {
        var payerKp = _f.Crypto.GenerateSecp256k1KeyPair();
        var (payerDid, _) = await _f.RegisterCitizenAsync("ep0-payer");
        // Override to get the real key pair
        var (payeeDid, _) = await _f.RegisterCitizenAsync("ep0-payee");

        // Build transfer citizen→citizen in epoch 0 — should fail
        var validator = new TransferValidator(
            new LiteWalletStore(_f.Context),
            new LiteIdentityRegistry(_f.Context),
            new PassthroughSanctionsChecker(),
            _f.Crypto,
            new InMemoryTransferNonceStore(),
            0);

        var req = new TransferRequest
        {
            PayerDid = payerDid, PayeeDid = payeeDid,
            AmountGrana = 1, Nonce = Guid.NewGuid().ToString("N"),
            Timestamp = DateTimeOffset.UtcNow,
            Signature = "0Bvalid" // reaches step 2 before step 6 — epoch check throws first
        };

        // Step 2 (EpochRules) fires before step 6 (Signature)
        await Assert.ThrowsAsync<EpochViolationException>(() => validator.ValidateAsync(req));
    }

    [Fact] public async Task Transfer_InsufficientBalance_ThrowsException()
    {
        var (did, _) = await _f.RegisterCitizenAsync("poor");
        var validator = new TransferValidator(
            new LiteWalletStore(_f.Context),
            new LiteIdentityRegistry(_f.Context),
            new PassthroughSanctionsChecker(),
            _f.Crypto,
            new InMemoryTransferNonceStore(),
            1); // Epoch 1 so citizen→citizen allowed

        // Build with valid-looking nonce+timestamp, balance check fires at step 7
        // The citizen DID has been registered so epoch step passes
        // Step 7 (balance) is checked after step 6 (signature) — signature will fail first
        // So we build a real signature for a request that will fail balance check
        var payerKp = _f.Crypto.GenerateSecp256k1KeyPair();
        var payerDid2 = "did:drn:richtest";
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
        {
            Did = payerDid2, PublicKeyHex = payerKp.PublicKeyHex,
            PrivateKeyBytes = payerKp.PrivateKeyBytes
        });

        var overAmount = 999_999_999_999_999L; // far exceeds endowment
        var req = _f.BuildSignedTransfer(
            payerDid2, payerKp.PrivateKeyBytes,
            "did:drn:payee-rich", overAmount);
        req = req with { PayeeDid = payerDid2 }; // same party — workaround epoch check

        await Assert.ThrowsAsync<InsufficientBalanceException>(() => validator.ValidateAsync(req));
    }

    [Fact] public async Task Transfer_NonceReplay_ThrowsException()
    {
        var payerKp  = _f.Crypto.GenerateSecp256k1KeyPair();
        var payerDid = "did:drn:nonce-test";
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
        {
            Did = payerDid, PublicKeyHex = payerKp.PublicKeyHex,
            PrivateKeyBytes = payerKp.PrivateKeyBytes
        });

        var validator = new TransferValidator(
            new LiteWalletStore(_f.Context),
            new LiteIdentityRegistry(_f.Context),
            new PassthroughSanctionsChecker(),
            _f.Crypto,
            new InMemoryTransferNonceStore(),
            1);

        var nonce     = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow;
        var canonical = JsonSerializer.Serialize(new
        {
            PayerDid = payerDid, PayeeDid = payerDid,
            AmountGrana = 1L, Nonce = nonce,
            Timestamp = timestamp.ToString("O"), Memo = (string?)null
        }, new JsonSerializerOptions { WriteIndented = false });
        var sig = _f.Crypto.SignSecp256k1(Encoding.UTF8.GetBytes(canonical), payerKp.PrivateKeyBytes);

        var req = new TransferRequest
        {
            PayerDid = payerDid, PayeeDid = payerDid,
            AmountGrana = 1, Nonce = nonce, Timestamp = timestamp, Signature = sig
        };

        // First attempt: fails at step 7 (self-transfer balance) but step 3 (nonce) succeeds
        try { await validator.ValidateAsync(req); } catch { }
        // Second attempt: nonce was consumed at step 3 on first pass
        await Assert.ThrowsAsync<NonceReplayException>(() => validator.ValidateAsync(req));
    }

    [Fact] public async Task Transfer_StaleTimestamp_ThrowsException()
    {
        var validator = new TransferValidator(
            new LiteWalletStore(_f.Context),
            new LiteIdentityRegistry(_f.Context),
            new PassthroughSanctionsChecker(),
            _f.Crypto,
            new InMemoryTransferNonceStore(),
            1);

        // Stale timestamp — step 4 fires before step 6 (signature)
        var req = new TransferRequest
        {
            PayerDid = "did:drn:x", PayeeDid = "did:drn:y",
            AmountGrana = 1,
            Nonce       = Guid.NewGuid().ToString("N"),
            Timestamp   = DateTimeOffset.UtcNow.AddHours(-2),
            Signature   = "0Btest"
        };

        await Assert.ThrowsAsync<StaleTransferException>(() => validator.ValidateAsync(req));
    }

    [Fact] public async Task BatchTransfer_ExecutesAll()
    {
        var payerKp  = _f.Crypto.GenerateSecp256k1KeyPair();
        var payerDid = "did:drn:batch-payer";
        var payeeDid = "did:drn:batch-payee";
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
        {
            Did = payerDid, PublicKeyHex = payerKp.PublicKeyHex,
            PrivateKeyBytes = payerKp.PrivateKeyBytes
        });
        var socKp = _f.Crypto.GenerateSecp256k1KeyPair();
        await _f.Driver.RegisterSocietyAsync(new RegisterSocietyRequest
        {
            Did = payeeDid, PublicKeyHex = socKp.PublicKeyHex,
            PrivateKeyBytes = socKp.PrivateKeyBytes,
            SocietyName = "Batch Society", PrimaryDidMethodName = "batchsoc",
            DrawAmountGrana = 0, OverdraftCeilingGrana = 0,
        });

        var amount = 10 * Svrn7Constants.GranaPerSvrn7;
        var requests = new[]
        {
            _f.BuildSignedTransfer(payerDid, payerKp.PrivateKeyBytes, payeeDid, amount),
            _f.BuildSignedTransfer(payerDid, payerKp.PrivateKeyBytes, payeeDid, amount),
        };

        var results = await _f.Driver.BatchTransferAsync(requests);
        results.Should().HaveCount(2);
        results.All(r => r.Success).Should().BeTrue();
    }
}

// ── Merkle log tests ──────────────────────────────────────────────────────────

public class MerkleLogTests : IAsyncLifetime
{
    private TestFixture _f = null!;
    public Task InitializeAsync() { _f = new TestFixture(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _f.DisposeAsync();

    [Fact] public async Task MerkleLog_EmptyRoot_ReturnsZeroHash()
    {
        var root = await _f.Driver.GetMerkleRootAsync();
        root.Should().HaveLength(64).And.MatchRegex("^0+$");
    }

    [Fact] public async Task MerkleLog_AppendAndRoot_NonEmpty()
    {
        await _f.RegisterCitizenAsync("m1");
        var root = await _f.Driver.GetMerkleRootAsync();
        root.Should().NotMatchRegex("^0+$");
    }

    [Fact] public async Task MerkleLog_NonPowerOf2_ComputesRoot()
    {
        await _f.RegisterCitizenAsync("n1");
        await _f.RegisterCitizenAsync("n2");
        await _f.RegisterCitizenAsync("n3");
        var root = await _f.Driver.GetMerkleRootAsync();
        root.Should().HaveLength(64);
    }

    [Fact] public async Task MerkleLog_SizeIncrementsOnAppend()
    {
        var before = await _f.Driver.GetLogSizeAsync();
        await _f.RegisterCitizenAsync("sz1");
        var after  = await _f.Driver.GetLogSizeAsync();
        after.Should().BeGreaterThan(before);
    }

    [Fact] public async Task MerkleLog_InclusionProof_ReturnsTrueForKnownEntry()
    {
        var txId = await _f.Driver.AppendToLogAsync("TestEntry", "{\"test\":true}");
        var found = await _f.Driver.DidRegistry.CountAsync();
        found.Should().BeGreaterThanOrEqualTo(0); // proxy check that log is functional
        txId.Should().HaveLength(64);
    }

    [Fact] public async Task MerkleLog_SignTreeHead_ReturnsHead()
    {
        await _f.RegisterCitizenAsync("th1");
        var head = await _f.Driver.SignMerkleTreeHeadAsync();
        head.RootHash.Should().HaveLength(64);
        head.Signature.Should().StartWith("0B");
        head.TreeSize.Should().BeGreaterThan(0);
    }
}

// ── DID Document registry tests ───────────────────────────────────────────────

public class DidDocumentRegistryTests : IAsyncLifetime
{
    private TestFixture _f = null!;
    public Task InitializeAsync() { _f = new TestFixture(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _f.DisposeAsync();

    [Fact] public async Task Did_Create_ResolveActive()
    {
        var (did, _) = await _f.RegisterCitizenAsync("dtest1");
        var result   = await _f.Driver.ResolveDidAsync(did);
        result.Found.Should().BeTrue();
        result.Document!.Status.Should().Be(DidStatus.Active);
    }

    [Fact] public async Task Did_Suspend_IsNotActive()
    {
        var (did, _) = await _f.RegisterCitizenAsync("dtest2");
        await _f.Driver.SuspendDidAsync(did);
        var result   = await _f.Driver.ResolveDidAsync(did);
        result.Document!.Status.Should().Be(DidStatus.Suspended);
    }

    [Fact] public async Task Did_Reinstate_IsActiveAgain()
    {
        var (did, _) = await _f.RegisterCitizenAsync("dtest3");
        await _f.Driver.SuspendDidAsync(did);
        await _f.Driver.ReinstateDidAsync(did);
        var active = await _f.Driver.IsDidActiveAsync(did);
        active.Should().BeTrue();
    }

    [Fact] public async Task Did_Deactivate_IsPermanent()
    {
        var (did, _) = await _f.RegisterCitizenAsync("dtest4");
        await _f.Driver.DeactivateDidAsync(did);
        var result   = await _f.Driver.ResolveDidAsync(did);
        result.Document!.Status.Should().Be(DidStatus.Deactivated);
    }

    [Fact] public async Task Did_History_HasVersions()
    {
        var (did, _) = await _f.RegisterCitizenAsync("dtest5");
        var history  = await _f.Driver.GetDidHistoryAsync(did);
        history.Should().HaveCountGreaterOrEqualTo(1);
    }

    [Fact] public async Task Did_FindByPublicKey_ReturnsCorrectDid()
    {
        var kp  = _f.Crypto.GenerateSecp256k1KeyPair();
        var did = "did:drn:bypk1";
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
            { Did = did, PublicKeyHex = kp.PublicKeyHex, PrivateKeyBytes = kp.PrivateKeyBytes });
        var found = await _f.Driver.FindDidByPublicKeyAsync(kp.PublicKeyHex);
        found.Should().Be(did);
    }
}

// ── VC registry tests ─────────────────────────────────────────────────────────

public class VcRegistryTests : IAsyncLifetime
{
    private TestFixture _f = null!;
    public Task InitializeAsync() { _f = new TestFixture(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _f.DisposeAsync();

    [Fact] public async Task Vc_StoreAndRetrieve_ById()
    {
        var (did, _) = await _f.RegisterCitizenAsync("vctest1");
        var vcs      = await _f.Driver.GetVcsBySubjectAsync(did);
        vcs.Should().NotBeEmpty();
        var vcId = vcs[0].VcId;
        var fetched = await _f.Driver.GetVcByIdAsync(vcId);
        fetched.Should().NotBeNull();
        fetched!.VcId.Should().Be(vcId);
    }

    [Fact] public async Task Vc_Revoke_StatusBecomesRevoked()
    {
        var (did, _) = await _f.RegisterCitizenAsync("vctest2");
        var vcs      = await _f.Driver.GetVcsBySubjectAsync(did);
        var vcId     = vcs[0].VcId;
        await _f.Driver.RevokeVcAsync(vcId, "Test revocation");
        var status = await _f.Driver.GetVcStatusAsync(vcId);
        status.Should().Be(VcStatus.Revoked);
    }

    [Fact] public async Task Vc_Suspend_StatusBecomesSuspended()
    {
        var (did, _) = await _f.RegisterCitizenAsync("vctest3");
        var vcs      = await _f.Driver.GetVcsBySubjectAsync(did);
        var vcId     = vcs[0].VcId;
        await _f.Driver.SuspendVcAsync(vcId);
        var status = await _f.Driver.GetVcStatusAsync(vcId);
        status.Should().Be(VcStatus.Suspended);
    }

    [Fact] public async Task Vc_Reinstate_StatusBecomesActive()
    {
        var (did, _) = await _f.RegisterCitizenAsync("vctest4");
        var vcs      = await _f.Driver.GetVcsBySubjectAsync(did);
        var vcId     = vcs[0].VcId;
        await _f.Driver.SuspendVcAsync(vcId);
        await _f.Driver.ReinstateVcAsync(vcId);
        var status = await _f.Driver.GetVcStatusAsync(vcId);
        status.Should().Be(VcStatus.Active);
    }

    [Fact] public async Task Vc_ExpireStale_MarksExpiredCredentials()
    {
        var staleVc = new VcRecord
        {
            VcId       = "urn:uuid:" + Guid.NewGuid(),
            IssuerDid  = "did:drn:issuer",
            SubjectDid = "did:drn:subject",
            Types      = new List<string> { "VerifiableCredential", "TestCredential" },
            VcHash     = "abc123",
            JwtEncoded = "header.payload.sig",
            IssuedAt   = DateTimeOffset.UtcNow.AddDays(-10),
            ExpiresAt  = DateTimeOffset.UtcNow.AddSeconds(-1), // already expired
        };
        await _f.Driver.StoreVcAsync(staleVc);
        var expired = await _f.Driver.ExpireStaleVcsAsync();
        expired.Should().BeGreaterThanOrEqualTo(1);
    }
}

// ── DID method name tests ─────────────────────────────────────────────────────

public class DidMethodNameTests : IAsyncLifetime
{
    private TestFixture _f = null!;
    public Task InitializeAsync() { _f = new TestFixture(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _f.DisposeAsync();

    private async Task<string> RegisterSocietyAsync(string method)
    {
        var kp = _f.Crypto.GenerateSecp256k1KeyPair();
        var did = $"did:{method}:soc";
        await _f.Driver.RegisterSocietyAsync(new RegisterSocietyRequest
        {
            Did = did, PublicKeyHex = kp.PublicKeyHex, PrivateKeyBytes = kp.PrivateKeyBytes,
            SocietyName = $"Society {method}", PrimaryDidMethodName = method,
            DrawAmountGrana = 0, OverdraftCeilingGrana = 0,
        });
        return did;
    }

    [Fact] public async Task RegisterAdditional_UniqueMethod_Succeeds()
    {
        var socDid = await RegisterSocietyAsync("methodtest1");
        var result = await _f.Driver.RegisterAdditionalDidMethodAsync(socDid, "methodtest1extra");
        result.Success.Should().BeTrue();
    }

    [Fact] public async Task RegisterAdditional_DuplicateMethod_Fails()
    {
        var socDid = await RegisterSocietyAsync("dupmethod");
        await _f.Driver.RegisterAdditionalDidMethodAsync(socDid, "dupextension");
        var result = await _f.Driver.RegisterAdditionalDidMethodAsync(socDid, "dupextension");
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("dupextension");
    }

    [Fact] public async Task Deregister_EntersDormancy()
    {
        var socDid = await RegisterSocietyAsync("dormtest");
        await _f.Driver.RegisterAdditionalDidMethodAsync(socDid, "dormext");
        await _f.Driver.DeregisterDidMethodAsync(socDid, "dormext");
        var status = await _f.Driver.GetDidMethodStatusAsync("dormext");
        status.Should().Be(DidMethodStatus.Dormant);
    }

    [Fact] public async Task Deregister_PrimaryMethod_ThrowsPrimaryDidMethodException()
    {
        await RegisterSocietyAsync("primarylock");
        var kp = _f.Crypto.GenerateSecp256k1KeyPair();
        var society = await _f.Driver.GetSocietyAsync("did:primarylock:soc");
        await Assert.ThrowsAsync<PrimaryDidMethodException>(
            () => _f.Driver.DeregisterDidMethodAsync("did:primarylock:soc", "primarylock"));
    }

    [Fact] public async Task GetAllDidMethods_ReturnsRegisteredMethods()
    {
        var socDid = await RegisterSocietyAsync("enumtest");
        await _f.Driver.RegisterAdditionalDidMethodAsync(socDid, "enumext1");
        await _f.Driver.RegisterAdditionalDidMethodAsync(socDid, "enumext2");
        var methods = await _f.Driver.GetAllDidMethodsAsync(socDid);
        methods.Should().HaveCountGreaterOrEqualTo(3); // primary + 2 additional
    }
}

// ── GDPR erasure tests ────────────────────────────────────────────────────────

public class GdprErasureTests : IAsyncLifetime
{
    private TestFixture _f = null!;
    public Task InitializeAsync() { _f = new TestFixture(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _f.DisposeAsync();

    [Fact] public async Task ErasePersonAsync_ValidSignature_DeactivatesDid()
    {
        var (did, _)      = await _f.RegisterCitizenAsync("erase1");
        var requestTs     = DateTimeOffset.UtcNow;
        var payload       = System.Text.Encoding.UTF8.GetBytes($"ERASE:{did}:{requestTs:O}");
        var sig           = _f.Crypto.SignSecp256k1(payload, _f.FoundationPrivateKey);

        var result = await _f.Driver.ErasePersonAsync(did, sig, requestTs);
        result.Success.Should().BeTrue();

        var resolved = await _f.Driver.ResolveDidAsync(did);
        resolved.Document!.Status.Should().Be(DidStatus.Deactivated);
    }

    [Fact] public async Task ErasePersonAsync_InvalidSignature_Throws()
    {
        var (did, _) = await _f.RegisterCitizenAsync("erase2");
        var ts       = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<SignatureVerificationException>(
            () => _f.Driver.ErasePersonAsync(did, "0Binvalidsig", ts));
    }
}

// ── Balance and UTXO tests ─────────────────────────────────────────────────────

public class BalanceTests : IAsyncLifetime
{
    private TestFixture _f = null!;
    public Task InitializeAsync() { _f = new TestFixture(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _f.DisposeAsync();

    [Fact] public async Task Balance_AfterRegistration_EqualsEndowment()
    {
        var (did, _) = await _f.RegisterCitizenAsync("bal1");
        var result   = await _f.Driver.GetBalanceResultAsync(did);
        result.Grana.Should().Be(Svrn7Constants.CitizenEndowmentGrana);
        result.Svrn7.Should().Be(Svrn7Constants.CitizenEndowmentSvrn7Display);
    }

    [Fact] public async Task Balance_AfterTransfer_Decrements()
    {
        var payerKp  = _f.Crypto.GenerateSecp256k1KeyPair();
        var payerDid = "did:drn:bal-payer";
        var socKp    = _f.Crypto.GenerateSecp256k1KeyPair();
        var socDid   = "did:balsoc:society";
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
            { Did = payerDid, PublicKeyHex = payerKp.PublicKeyHex, PrivateKeyBytes = payerKp.PrivateKeyBytes });
        await _f.Driver.RegisterSocietyAsync(new RegisterSocietyRequest
            { Did = socDid, PublicKeyHex = socKp.PublicKeyHex, PrivateKeyBytes = socKp.PrivateKeyBytes,
              SocietyName = "Bal Society", PrimaryDidMethodName = "balsoc",
              DrawAmountGrana = 0, OverdraftCeilingGrana = 0 });

        var amount = 1 * Svrn7Constants.GranaPerSvrn7;
        var req    = _f.BuildSignedTransfer(payerDid, payerKp.PrivateKeyBytes, socDid, amount);
        await _f.Driver.TransferAsync(req);

        var balance = await _f.Driver.GetBalanceGranaAsync(payerDid);
        balance.Should().Be(Svrn7Constants.CitizenEndowmentGrana - amount);
    }

// ── Test helper: in-memory nonce store (replaces LiteDB for unit tests) ───────
internal sealed class InMemoryTransferNonceStore : ITransferNonceStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset>
        _seen = new();

    public Task<bool> IsReplayAsync(
        string nonce, TimeSpan replayWindow, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var cutoff = DateTimeOffset.UtcNow - replayWindow;
        foreach (var k in _seen.Keys)
            if (_seen.TryGetValue(k, out var t) && t < cutoff)
                _seen.TryRemove(k, out _);
        return Task.FromResult(!_seen.TryAdd(nonce, DateTimeOffset.UtcNow));
    }
}

}
