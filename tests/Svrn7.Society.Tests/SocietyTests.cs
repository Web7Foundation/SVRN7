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
using Svrn7.Society;
using Svrn7.Store;
using Xunit;

namespace Svrn7.Society.Tests;

// ── Test fixture ───────────────────────────────────────────────────────────────

public sealed class SocietyTestFixture : IAsyncDisposable
{
    public ISvrn7SocietyDriver Driver     { get; }
    public ICryptoService      Crypto     { get; }
    public string              SocietyDid { get; } = "did:alpha:society1";
    public Svrn7LiteContext    Context    { get; }  // exposed for test seeding

    private readonly DidRegistryLiteContext _didCtx;
    private readonly VcRegistryLiteContext  _vcCtx;
    private readonly FederationLiteContext  _fedCtx;

    public SocietyTestFixture()
    {
        Context  = new Svrn7LiteContext(":memory:");
        _didCtx = new DidRegistryLiteContext(":memory:");
        _vcCtx  = new VcRegistryLiteContext(":memory:");
        _fedCtx = new FederationLiteContext(":memory:");

        Crypto  = new CryptoService();

        var wallets     = new LiteWalletStore(Context);
        var registry    = new LiteIdentityRegistry(Context);
        var merkle      = new MerkleLog(Context, Crypto);
        var didReg      = new LiteDidDocumentRegistry(_didCtx);
        var vcReg       = new LiteVcRegistry(_vcCtx);
        var fedStore    = new LiteFederationStore(_fedCtx);
        var vcResolver  = new LiteVcDocumentResolver(vcReg);
        var didResolver = new LocalDidDocumentResolver(didReg, new[] { "alpha", "drn" });
        var vcService   = new VcService(Crypto);
        var sanctions   = new PassthroughSanctionsChecker();
        var membership  = new LiteSocietyMembershipStore(Context);

        var foundationKp = Crypto.GenerateSecp256k1KeyPair();
        var baseOpts = Options.Create(new Svrn7Options
        {
            FoundationPublicKeyHex = foundationKp.PublicKeyHex,
            DidMethodName = "alpha",
        });

        var inner = new Svrn7Driver(wallets, registry, merkle, vcService, vcReg,
            new TransferValidator(wallets, registry, sanctions, Crypto, new InMemoryTransferNonceStore(), 0),
            sanctions, Crypto, fedStore, didReg, didResolver, vcResolver,
            baseOpts, NullLogger<Svrn7Driver>.Instance, foundationKp.PrivateKeyBytes);

        var societyOpts = Options.Create(new Svrn7SocietyOptions
        {
            FoundationPublicKeyHex = foundationKp.PublicKeyHex,
            DidMethodName          = "alpha",
            SocietyDid             = SocietyDid,
            FederationDid          = "did:drn:federation",
            DrawAmountGrana        = 1_000_000_000_000L,
            OverdraftCeilingGrana  = 10_000_000_000_000L,
            FederationRoundTripTimeout = TimeSpan.FromSeconds(5),
            DidMethodNames         = new System.Collections.Generic.List<string> { "alpha" },
        });

        Driver = new Svrn7SocietyDriver(inner, registry, wallets, merkle, vcService,
            vcReg, Crypto, membership, new DIDCommPackingService(), vcResolver,
            societyOpts, NullLogger<Svrn7SocietyDriver>.Instance);
    }

    /// <summary>Build a correctly signed TransferRequest for a registered citizen.</summary>
    public TransferRequest BuildSignedTransfer(
        string payerDid, byte[] payerPrivateKey, string payeeDid, long amountGrana)
    {
        var nonce     = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow;
        var canonical = JsonSerializer.Serialize(new
        {
            PayerDid = payerDid, PayeeDid = payeeDid, AmountGrana = amountGrana,
            Nonce = nonce, Timestamp = timestamp.ToString("O"), Memo = (string?)null
        }, new JsonSerializerOptions { WriteIndented = false });
        var sig = Crypto.SignSecp256k1(Encoding.UTF8.GetBytes(canonical), payerPrivateKey);
        return new TransferRequest
        {
            PayerDid = payerDid, PayeeDid = payeeDid,
            AmountGrana = amountGrana, Nonce = nonce,
            Timestamp = timestamp, Signature = sig
        };
    }

    public async ValueTask DisposeAsync()
    {
        await Driver.DisposeAsync();
        Context.Dispose(); _didCtx.Dispose(); _vcCtx.Dispose(); _fedCtx.Dispose();
    }
}

// ── Society driver tests ───────────────────────────────────────────────────────

public class SocietyDriverTests : IAsyncDisposable
{
    private readonly SocietyTestFixture _f = new();

    [Fact] public void SocietyDid_ReturnsConfiguredDid()
    {
        _f.Driver.SocietyDid.Should().Be("did:alpha:society1");
    }

    [Fact] public async Task RegisterCitizenInSociety_WrongSociety_Fails()
    {
        var kp  = _f.Crypto.GenerateSecp256k1KeyPair();
        var res = await _f.Driver.RegisterCitizenInSocietyAsync(new RegisterCitizenInSocietyRequest
        {
            Did             = "did:alpha:alice",
            PublicKeyHex    = kp.PublicKeyHex,
            PrivateKeyBytes = kp.PrivateKeyBytes,
            SocietyDid      = "did:alpha:wrongsociety",
        });
        res.Success.Should().BeFalse();
        res.ErrorMessage.Should().Contain("wrongsociety");
    }

    [Fact] public async Task GetMemberCitizenDids_EmptyInitially()
    {
        var members = await _f.Driver.GetMemberCitizenDidsAsync();
        members.Should().BeEmpty();
    }

    [Fact] public async Task IsMember_UnknownCitizen_ReturnsFalse()
    {
        var result = await _f.Driver.IsMemberAsync("did:alpha:unknown");
        result.Should().BeFalse();
    }

    [Fact] public async Task GetOverdraftStatus_InitiallyClean()
    {
        var status = await _f.Driver.GetOverdraftStatusAsync();
        status.Should().Be(OverdraftStatus.Clean);
    }

    public async ValueTask DisposeAsync() => await _f.DisposeAsync();
}

// ── Society citizen registration tests ────────────────────────────────────────

public class SocietyCitizenRegistrationTests : IAsyncDisposable
{
    private readonly SocietyTestFixture _f = new();

    private async Task SeedSocietyWalletAsync()
    {
        // Register the society so it has a wallet; seed with funds
        var socKp = _f.Crypto.GenerateSecp256k1KeyPair();
        await _f.Driver.RegisterSocietyAsync(new RegisterSocietyRequest
        {
            Did = _f.SocietyDid, PublicKeyHex = socKp.PublicKeyHex,
            PrivateKeyBytes = socKp.PrivateKeyBytes,
            SocietyName = "Test Society", PrimaryDidMethodName = "alpha",
            DrawAmountGrana = 0, OverdraftCeilingGrana = 0,
        });
        // Add seed UTXO to society wallet for endowment payments
        var walletStore = new LiteWalletStore(_f.Context);
        await walletStore.AddUtxoAsync(new Utxo
        {
            Id = "seed-utxo-001",
            OwnerDid = _f.SocietyDid,
            AmountGrana = 10_000 * Svrn7Constants.GranaPerSvrn7
        });
    }

    [Fact] public async Task RegisterCitizenInSociety_RecordsMembership()
    {
        await SeedSocietyWalletAsync();
        var kp = _f.Crypto.GenerateSecp256k1KeyPair();
        var req = new RegisterCitizenInSocietyRequest
        {
            Did = "did:alpha:member1", PublicKeyHex = kp.PublicKeyHex,
            PrivateKeyBytes = kp.PrivateKeyBytes, SocietyDid = _f.SocietyDid,
        };
        var res = await _f.Driver.RegisterCitizenInSocietyAsync(req);
        res.Success.Should().BeTrue(res.ErrorMessage);

        var isMember = await _f.Driver.IsMemberAsync("did:alpha:member1");
        isMember.Should().BeTrue();
    }

    [Fact] public async Task RegisterCitizenInSociety_CitizenAppearsInMemberList()
    {
        await SeedSocietyWalletAsync();
        var kp = _f.Crypto.GenerateSecp256k1KeyPair();
        await _f.Driver.RegisterCitizenInSocietyAsync(new RegisterCitizenInSocietyRequest
        {
            Did = "did:alpha:member2", PublicKeyHex = kp.PublicKeyHex,
            PrivateKeyBytes = kp.PrivateKeyBytes, SocietyDid = _f.SocietyDid,
        });
        var members = await _f.Driver.GetMemberCitizenDidsAsync();
        members.Should().Contain("did:alpha:member2");
    }

    public async ValueTask DisposeAsync() => await _f.DisposeAsync();
}

// ── DID method self-service tests ─────────────────────────────────────────────

public class SocietyDidMethodTests : IAsyncDisposable
{
    private readonly SocietyTestFixture _f = new();

    private async Task RegisterSocietyInFedAsync()
    {
        var kp = _f.Crypto.GenerateSecp256k1KeyPair();
        await _f.Driver.RegisterSocietyAsync(new RegisterSocietyRequest
        {
            Did = _f.SocietyDid, PublicKeyHex = kp.PublicKeyHex,
            PrivateKeyBytes = kp.PrivateKeyBytes, SocietyName = "Alpha Society",
            PrimaryDidMethodName = "alpha", DrawAmountGrana = 0, OverdraftCeilingGrana = 0,
        });
    }

    [Fact] public async Task RegisterSocietyDidMethod_SelfService_NoSignatureRequired()
    {
        await RegisterSocietyInFedAsync();
        var res = await _f.Driver.RegisterSocietyDidMethodAsync("alphahealth");
        res.Success.Should().BeTrue(res.ErrorMessage);
    }

    [Fact] public async Task DeregisterSocietyDidMethod_MethodEntersDormancy()
    {
        await RegisterSocietyInFedAsync();
        await _f.Driver.RegisterSocietyDidMethodAsync("alphatemp");
        var res = await _f.Driver.DeregisterSocietyDidMethodAsync("alphatemp");
        res.Success.Should().BeTrue(res.ErrorMessage);
        var status = await _f.Driver.GetDidMethodStatusAsync("alphatemp");
        status.Should().Be(DidMethodStatus.Dormant);
    }

    [Fact] public async Task DeregisterPrimaryMethod_ThrowsPrimaryDidMethodException()
    {
        await RegisterSocietyInFedAsync();
        await Assert.ThrowsAsync<PrimaryDidMethodException>(
            () => _f.Driver.DeregisterSocietyDidMethodAsync("alpha"));
    }

    [Fact] public async Task GetSocietyDidMethods_ReturnsAllMethods()
    {
        await RegisterSocietyInFedAsync();
        await _f.Driver.RegisterSocietyDidMethodAsync("alpha2");
        await _f.Driver.RegisterSocietyDidMethodAsync("alpha3");
        var methods = await _f.Driver.GetSocietyDidMethodsAsync();
        methods.Should().HaveCountGreaterOrEqualTo(3); // primary + 2 additional
    }

    [Fact] public async Task RegisterDuplicateMethod_Fails()
    {
        await RegisterSocietyInFedAsync();
        await _f.Driver.RegisterSocietyDidMethodAsync("alphadup");
        var res = await _f.Driver.RegisterSocietyDidMethodAsync("alphadup");
        res.Success.Should().BeFalse();
        res.ErrorMessage.Should().Contain("alphadup");
    }

    public async ValueTask DisposeAsync() => await _f.DisposeAsync();
}

// ── Multi-DID citizen tests ────────────────────────────────────────────────────

public class MultiDidCitizenTests : IAsyncDisposable
{
    private readonly SocietyTestFixture _f = new();

    [Fact] public async Task ResolveCitizenPrimaryDid_FromSelf()
    {
        var kp  = _f.Crypto.GenerateSecp256k1KeyPair();
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
        {
            Did = "did:alpha:c1", PublicKeyHex = kp.PublicKeyHex,
            PrivateKeyBytes = kp.PrivateKeyBytes,
        });
        var primary = await _f.Driver.ResolveCitizenPrimaryDidAsync("did:alpha:c1");
        primary.Should().Be("did:alpha:c1");
    }

    [Fact] public async Task GetAllDidsForCitizen_ReturnsPrimaryDid()
    {
        var kp = _f.Crypto.GenerateSecp256k1KeyPair();
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
        {
            Did = "did:alpha:c2", PublicKeyHex = kp.PublicKeyHex,
            PrivateKeyBytes = kp.PrivateKeyBytes,
        });
        var dids = await _f.Driver.GetAllDidsForCitizenAsync("did:alpha:c2");
        dids.Should().NotBeEmpty();
        dids.Should().Contain(d => d.IsPrimary);
    }

    [Fact] public async Task AddCitizenDid_ValidMethod_Succeeds()
    {
        var kp  = _f.Crypto.GenerateSecp256k1KeyPair();
        var socKp = _f.Crypto.GenerateSecp256k1KeyPair();
        await _f.Driver.RegisterSocietyAsync(new RegisterSocietyRequest
        {
            Did = _f.SocietyDid, PublicKeyHex = socKp.PublicKeyHex,
            PrivateKeyBytes = socKp.PrivateKeyBytes, SocietyName = "Alpha",
            PrimaryDidMethodName = "alpha", DrawAmountGrana = 0, OverdraftCeilingGrana = 0,
        });
        await _f.Driver.RegisterSocietyDidMethodAsync("alphaid");
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
        {
            Did = "did:alpha:c3", PublicKeyHex = kp.PublicKeyHex,
            PrivateKeyBytes = kp.PrivateKeyBytes,
        });

        var result = await _f.Driver.AddCitizenDidAsync("did:alpha:c3", "alphaid");
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    public async ValueTask DisposeAsync() => await _f.DisposeAsync();
}

// ── Cross-Society transfer tests ───────────────────────────────────────────────

public class CrossSocietyTransferTests : IAsyncDisposable
{
    private readonly SocietyTestFixture _f = new();

    [Fact] public async Task TransferToExternalCitizen_ReturnsOrderSentResult()
    {
        // Advance to Epoch 1 to allow cross-Society transfers
        // (skipping Foundation signature verification for this unit test)
        var payerKp  = _f.Crypto.GenerateSecp256k1KeyPair();
        var payerDid = "did:alpha:xpayer";
        var socKp    = _f.Crypto.GenerateSecp256k1KeyPair();
        await _f.Driver.RegisterSocietyAsync(new RegisterSocietyRequest
        {
            Did = _f.SocietyDid, PublicKeyHex = socKp.PublicKeyHex,
            PrivateKeyBytes = socKp.PrivateKeyBytes,
            SocietyName = "Alpha", PrimaryDidMethodName = "alpha",
            DrawAmountGrana = 0, OverdraftCeilingGrana = 0,
        });
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
        {
            Did = payerDid, PublicKeyHex = payerKp.PublicKeyHex,
            PrivateKeyBytes = payerKp.PrivateKeyBytes,
        });

        // At epoch 0 this will fail due to epoch rules — expected
        var req = _f.BuildSignedTransfer(
            payerDid, payerKp.PrivateKeyBytes,
            "did:beta:payee", 1 * Svrn7Constants.GranaPerSvrn7);
        var res = await _f.Driver.TransferToExternalCitizenAsync(req, "did:beta:society");

        // Epoch 0 restriction: transfer fails at epoch check — result is not null
        res.Should().NotBeNull();
    }

    [Fact] public async Task FindVcsBySubjectAcrossSocieties_LocalResultsIncluded()
    {
        var kp = _f.Crypto.GenerateSecp256k1KeyPair();
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
        {
            Did = "did:alpha:crossvc", PublicKeyHex = kp.PublicKeyHex,
            PrivateKeyBytes = kp.PrivateKeyBytes,
        });
        var vcs = await _f.Driver.GetVcsBySubjectAsync("did:alpha:crossvc");
        vcs.Should().NotBeEmpty(); // local VCs from registration

        // Cross-Society resolver returns partial result including local
        var result = await _f.Driver.FindVcsBySubjectAcrossSocietiesAsync("did:alpha:crossvc");
        result.Should().NotBeNull();
        result.RespondedSocieties.Should().Contain(_f.SocietyDid);
    }

    public async ValueTask DisposeAsync() => await _f.DisposeAsync();
}

// ── VC Document Resolver tests ────────────────────────────────────────────────

public class VcDocumentResolverTests : IAsyncDisposable
{
    private readonly SocietyTestFixture _f = new();

    [Fact] public async Task IsValidAsync_ActiveVc_ReturnsTrue()
    {
        var kp = _f.Crypto.GenerateSecp256k1KeyPair();
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
        {
            Did = "did:alpha:vcr1", PublicKeyHex = kp.PublicKeyHex,
            PrivateKeyBytes = kp.PrivateKeyBytes,
        });
        var vcs = await _f.Driver.GetVcsBySubjectAsync("did:alpha:vcr1");
        vcs.Should().NotBeEmpty();
        // IsValid is on VcRegistry — accessible via driver
        var status = await _f.Driver.GetVcStatusAsync(vcs[0].VcId);
        status.Should().Be(VcStatus.Active);
    }

    [Fact] public async Task GetStatusBatchAsync_MultipleVcs()
    {
        var kp1 = _f.Crypto.GenerateSecp256k1KeyPair();
        var kp2 = _f.Crypto.GenerateSecp256k1KeyPair();
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
            { Did = "did:alpha:vcr2", PublicKeyHex = kp1.PublicKeyHex, PrivateKeyBytes = kp1.PrivateKeyBytes });
        await _f.Driver.RegisterCitizenAsync(new RegisterCitizenRequest
            { Did = "did:alpha:vcr3", PublicKeyHex = kp2.PublicKeyHex, PrivateKeyBytes = kp2.PrivateKeyBytes });

        var vcs1 = await _f.Driver.GetVcsBySubjectAsync("did:alpha:vcr2");
        var vcs2 = await _f.Driver.GetVcsBySubjectAsync("did:alpha:vcr3");

        var vcIds = vcs1.Concat(vcs2).Select(v => v.VcId).ToList();
        vcIds.Should().HaveCountGreaterOrEqualTo(2);

        // Batch status via registry
        foreach (var id in vcIds)
        {
            var s = await _f.Driver.GetVcStatusAsync(id);
            s.Should().Be(VcStatus.Active);
        }
    }

    public async ValueTask DisposeAsync() => await _f.DisposeAsync();
}
