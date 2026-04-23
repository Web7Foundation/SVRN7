using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Svrn7.Core.Interfaces;
using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Svrn7.Core;
using Svrn7.Core.Models;
using Svrn7.DIDComm;
using Svrn7.Society;
using Svrn7.Store;
using Xunit;

namespace Svrn7.TDA.Tests;

// ── TdaResourceId Tests ───────────────────────────────────────────────────────

public class TdaResourceIdTests
{
    private const string Network = "alpha.svrn7.net";

    // ── Build / parse round-trips ─────────────────────────────────────────────

    [Fact]
    public void InboxMessage_RoundTrip()
    {
        var objectId = ObjectId.NewObjectId().ToString();
        var didUrl   = TdaResourceId.InboxMessage(Network, objectId);

        didUrl.Should().Be($"did:drn:{Network}/inbox/msg/{objectId}");
        TdaResourceId.ParseKey(didUrl).Should().Be(objectId);
        TdaResourceId.ParseNetworkId(didUrl).Should().Be(Network);
    }

    [Fact]
    public void Citizen_RoundTrip()
    {
        var suffix = "alice";
        var didUrl = TdaResourceId.Citizen(Network, suffix);

        didUrl.Should().Be($"did:drn:{Network}/citizen/{suffix}");
        TdaResourceId.ParseKey(didUrl).Should().Be(suffix);
    }

    [Fact]
    public void LogEntry_RoundTrip()
    {
        var hash   = new string('a', 64); // 64-char Blake3 hex
        var didUrl = TdaResourceId.LogEntry(Network, hash);

        didUrl.Should().Be($"did:drn:{Network}/main/logentry/{hash}");
        TdaResourceId.ParseKey(didUrl).Should().Be(hash);
    }

    [Fact]
    public void Schema_RoundTrip()
    {
        var name   = "CitizenEndowmentCredential";
        var didUrl = TdaResourceId.Schema(Network, name);

        didUrl.Should().Be($"did:drn:{Network}/schemas/schema/{name}");
        TdaResourceId.ParseKey(didUrl).Should().Be(name);
    }

    [Fact]
    public void Utxo_RoundTrip()
    {
        var hash   = new string('f', 64);
        var didUrl = TdaResourceId.Utxo(Network, hash);
        didUrl.Should().Be($"did:drn:{Network}/main/utxo/{hash}");
    }

    [Fact]
    public void Vc_RoundTrip()
    {
        var vcId   = Guid.NewGuid().ToString();
        var didUrl = TdaResourceId.Vc(Network, vcId);
        didUrl.Should().Be($"did:drn:{Network}/vcs/vc/{vcId}");
    }

    // ── NetworkIdFromDid ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("did:drn:alpha.svrn7.net",      "alpha.svrn7.net")]
    [InlineData("did:drn:foundation.svrn7.net", "foundation.svrn7.net")]
    [InlineData("alpha.svrn7.net",              "alpha.svrn7.net")]   // passthrough
    public void NetworkIdFromDid_Strips_Prefix(string input, string expected)
    {
        TdaResourceId.NetworkIdFromDid(input).Should().Be(expected);
    }

    // ── ParseKey ──────────────────────────────────────────────────────────────

    [Fact]
    public void ParseKey_Returns_Null_For_Bare_Did()
    {
        TdaResourceId.ParseKey("did:drn:alpha.svrn7.net").Should().BeNull();
    }

    [Fact]
    public void ParseKey_Returns_Null_For_Empty()
    {
        TdaResourceId.ParseKey("").Should().BeNull();
    }

    // ── ParseNetworkId ────────────────────────────────────────────────────────

    [Fact]
    public void ParseNetworkId_Returns_Null_For_Non_DRN()
    {
        TdaResourceId.ParseNetworkId("did:key:abc123").Should().BeNull();
    }

    [Fact]
    public void ParseNetworkId_Returns_Null_For_Bare_Did()
    {
        // Bare DID has no slash — no path
        TdaResourceId.ParseNetworkId("did:drn:alpha.svrn7.net").Should().BeNull();
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_Produces_Correct_Form()
    {
        var result = TdaResourceId.Build("alpha.svrn7.net", "inbox", "msg", "abc123");
        result.Should().Be("did:drn:alpha.svrn7.net/inbox/msg/abc123");
    }

    [Fact]
    public void AllBuilders_Produce_Did_Url_Starting_With_Did_Drn()
    {
        var builders = new[]
        {
            TdaResourceId.InboxMessage(Network, "abc"),
            TdaResourceId.Citizen(Network, "alice"),
            TdaResourceId.Wallet(Network, "alice"),
            TdaResourceId.Utxo(Network, new string('a', 64)),
            TdaResourceId.Society(Network, "alpha.svrn7.net"),
            TdaResourceId.Membership(Network, "alice"),
            TdaResourceId.LogEntry(Network, new string('b', 64)),
            TdaResourceId.TreeHead(Network, new string('c', 64)),
            TdaResourceId.DidDocument(Network, "sovronia.svrn7.net"),
            TdaResourceId.Vc(Network, Guid.NewGuid().ToString()),
            TdaResourceId.Schema(Network, "TestSchema"),
            TdaResourceId.ProcessedOrder(Network, "abc"),
        };

        foreach (var url in builders)
            url.Should().StartWith("did:drn:");
    }
}

// ── LiteInboxStore DID URL Tests ──────────────────────────────────────────────

public class LiteInboxStoreDIDUrlTests : IDisposable
{
    private readonly InboxLiteContext   _ctx;
    private readonly LiteInboxStore     _store;
    private const string                SocietyDid = "did:drn:alpha.svrn7.net";

    public LiteInboxStoreDIDUrlTests()
    {
        _ctx   = new InboxLiteContext(":memory:");
        var opts = Options.Create(new Svrn7SocietyOptions { SocietyDid = SocietyDid });
        _store = new LiteInboxStore(_ctx, opts, NullLogger<LiteInboxStore>.Instance);
    }

    [Fact]
    public async Task EnqueueAsync_Generates_DID_URL_As_Id()
    {
        await _store.EnqueueAsync("test/1.0/msg", "payload");

        var counts = await _store.GetStatusCountsAsync();
        counts[InboxMessageStatus.Pending].Should().Be(1);

        var batch = await _store.DequeueBatchAsync(1);
        batch.Should().HaveCount(1);

        var id = batch[0].Id;
        id.Should().StartWith("did:drn:alpha.svrn7.net/inbox/msg/");
        TdaResourceId.ParseKey(id).Should().HaveLength(24); // ObjectId hex
        TdaResourceId.ParseNetworkId(id).Should().Be("alpha.svrn7.net");
    }

    [Fact]
    public async Task GetByIdAsync_Resolves_By_DID_URL()
    {
        await _store.EnqueueAsync("test/1.0/msg", "hello world");
        var batch  = await _store.DequeueBatchAsync(1);
        var didUrl = batch[0].Id;

        var found = await _store.GetByIdAsync(didUrl);
        found.Should().NotBeNull();
        found!.PackedPayload.Should().Be("hello world");
        found.MessageType.Should().Be("test/1.0/msg");
    }

    [Fact]
    public async Task GetByIdAsync_Returns_Null_For_Unknown_DID_URL()
    {
        var unknown = TdaResourceId.InboxMessage("alpha.svrn7.net",
            ObjectId.NewObjectId().ToString());
        var result  = await _store.GetByIdAsync(unknown);
        result.Should().BeNull();
    }

    [Fact]
    public async Task MarkProcessedAsync_Works_With_DID_URL()
    {
        await _store.EnqueueAsync("test/1.0/msg", "payload");
        var batch  = await _store.DequeueBatchAsync(1);
        var didUrl = batch[0].Id;

        await _store.MarkProcessedAsync(didUrl);

        var counts = await _store.GetStatusCountsAsync();
        counts.ContainsKey(InboxMessageStatus.Processed).Should().BeTrue();
        counts[InboxMessageStatus.Processed].Should().Be(1);
    }

    [Fact]
    public async Task Multiple_Messages_All_Have_Unique_DID_URL_Ids()
    {
        for (int i = 0; i < 10; i++)
            await _store.EnqueueAsync("test/1.0/msg", $"payload-{i}");

        var batch = await _store.DequeueBatchAsync(10);
        batch.Should().HaveCount(10);

        var ids = batch.Select(m => m.Id).ToHashSet();
        ids.Should().HaveCount(10, "all IDs must be unique");
        ids.Should().OnlyContain(id => id.StartsWith("did:drn:"));
    }

    public void Dispose() => _ctx.Dispose();
}

// ── SchemaRegistry Tests ──────────────────────────────────────────────────────

public class SchemaRegistryTests : IDisposable
{
    private readonly SchemaLiteContext  _ctx;
    private readonly LiteSchemaRegistry _registry;
    private readonly LiteSchemaResolver _resolver;

    public SchemaRegistryTests()
    {
        _ctx      = new SchemaLiteContext(":memory:");
        _registry = new LiteSchemaRegistry(_ctx, NullLogger<LiteSchemaRegistry>.Instance);
        _resolver = new LiteSchemaResolver(_registry);
    }

    [Fact]
    public async Task Register_And_GetByName_RoundTrip()
    {
        var schema = new SchemaRecord
        {
            Name       = "CitizenEndowmentCredential",
            SchemaJson = """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object"}""",
        };

        await _registry.RegisterAsync(schema);
        var found = await _registry.GetByNameAsync("CitizenEndowmentCredential");

        found.Should().NotBeNull();
        found!.Name.Should().Be("CitizenEndowmentCredential");
        found.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetByName_Returns_Null_For_Unknown()
    {
        var result = await _registry.GetByNameAsync("NonExistentSchema");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Deactivate_Makes_Schema_Invisible()
    {
        await _registry.RegisterAsync(new SchemaRecord
        {
            Name = "TransferReceiptCredential",
            SchemaJson = "{}",
        });

        await _registry.DeactivateAsync("TransferReceiptCredential");
        var found = await _registry.GetByNameAsync("TransferReceiptCredential");
        found.Should().BeNull(); // deactivated — not returned
    }

    [Fact]
    public async Task GetAllActive_Returns_Only_Active_Schemas()
    {
        await _registry.RegisterAsync(new SchemaRecord { Name = "SchemaA", SchemaJson = "{}" });
        await _registry.RegisterAsync(new SchemaRecord { Name = "SchemaB", SchemaJson = "{}" });
        await _registry.RegisterAsync(new SchemaRecord { Name = "SchemaC", SchemaJson = "{}" });
        await _registry.DeactivateAsync("SchemaB");

        var active = await _registry.GetAllActiveAsync();
        active.Should().HaveCount(2);
        active.Select(s => s.Name).Should().Contain("SchemaA").And.Contain("SchemaC");
        active.Select(s => s.Name).Should().NotContain("SchemaB");
    }

    [Fact]
    public async Task ResolveByName_Returns_SchemaJson()
    {
        var json = """{"type":"object","properties":{"did":{"type":"string"}}}""";
        await _registry.RegisterAsync(new SchemaRecord
        {
            Name = "TestCredential",
            SchemaJson = json,
        });

        var result = await _resolver.ResolveByNameAsync("TestCredential");
        result.Should().Be(json);
    }

    [Fact]
    public async Task ResolveByDidUrl_Extracts_Name_And_Resolves()
    {
        await _registry.RegisterAsync(new SchemaRecord
        {
            Name = "CitizenEndowmentCredential",
            SchemaJson = """{"type":"object"}""",
        });

        var didUrl = "did:drn:alpha.svrn7.net/schemas/schema/CitizenEndowmentCredential";
        var result = await _resolver.ResolveByDidUrlAsync(didUrl);
        result.Should().Be("""{"type":"object"}""");
    }

    [Fact]
    public async Task ResolveByDidUrl_Returns_Null_For_Unknown()
    {
        var didUrl = "did:drn:alpha.svrn7.net/schemas/schema/NonExistent";
        var result = await _resolver.ResolveByDidUrlAsync(didUrl);
        result.Should().BeNull();
    }

    [Fact]
    public void Register_Rejects_Invalid_Schema_Name()
    {
        var act = async () => await _registry.RegisterAsync(new SchemaRecord
        {
            Name       = "Invalid Name With Spaces",
            SchemaJson = "{}",
        });
        act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Register_Rejects_Schema_Name_With_Slash()
    {
        var act = async () => await _registry.RegisterAsync(new SchemaRecord
        {
            Name       = "Invalid/Name",
            SchemaJson = "{}",
        });
        act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Valid_Schema_Names_Are_Accepted()
    {
        var validNames = new[]
        {
            "CitizenEndowmentCredential",
            "TransferReceiptCredential-v2",
            "Svrn7.VtcCredential",
            "simple",
        };

        foreach (var name in validNames)
        {
            await _registry.RegisterAsync(new SchemaRecord { Name = name, SchemaJson = "{}" });
            var found = await _registry.GetByNameAsync(name);
            found.Should().NotBeNull($"'{name}' should be a valid schema name");
        }
    }

    public void Dispose() => _ctx.Dispose();
}

// ── LobeDescriptor Tests ──────────────────────────────────────────────────────

public class LobeDescriptorTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "fixtures");

    // ── LoadFromFile ──────────────────────────────────────────────────────────

    [Fact]
    public void LoadFromFile_Parses_All_TopLevel_Fields()
    {
        var path = Path.Combine(FixtureDir, "Test.Email.lobe.json");
        var d    = LobeDescriptor.LoadFromFile(path);

        d.Should().NotBeNull();
        d!.Lobe.Id.Should().Be("test.email");
        d.Lobe.Name.Should().Be("Test.Email");
        d.Lobe.Title.Should().Be("Test Email LOBE");
        d.Lobe.Version.Should().Be("1.0.0");
        d.Lobe.Author.Should().Be("Test");
        d.Lobe.Organization.Should().Be("Test Org");
        d.Lobe.EpochRequired.Should().Be(0);
        d.Lobe.Module.Should().Be("Test.Email.psm1");
    }

    [Fact]
    public void LoadFromFile_Parses_Protocols()
    {
        var path = Path.Combine(FixtureDir, "Test.Email.lobe.json");
        var d    = LobeDescriptor.LoadFromFile(path)!;

        d.Protocols.Should().HaveCount(2);

        var prefix = d.Protocols[0];
        prefix.Uri.Should().Be("https://test.example/protocols/email/1.0/message");
        prefix.Match.Should().Be("prefix");
        prefix.Entrypoint.Should().Be("Receive-TestEmail");
        prefix.Direction.Should().Be("inbound");
        prefix.EpochRequired.Should().Be(0);

        var exact = d.Protocols[1];
        exact.Uri.Should().Be("https://test.example/protocols/email/1.0/receipt");
        exact.Match.Should().Be("exact");
        exact.Entrypoint.Should().Be("Receive-TestEmailReceipt");
    }

    [Fact]
    public void LoadFromFile_Parses_Cmdlets_With_Schemas_And_Annotations()
    {
        var path = Path.Combine(FixtureDir, "Test.Email.lobe.json");
        var d    = LobeDescriptor.LoadFromFile(path)!;

        d.Cmdlets.Should().HaveCount(2);

        var c = d.Cmdlets[0];
        c.Name.Should().Be("Receive-TestEmail");
        c.Title.Should().Be("Receive Test Email");
        c.InputSchema.Should().NotBeNull();
        c.OutputSchema.Should().NotBeNull();

        c.Annotations.Idempotent.Should().BeTrue();
        c.Annotations.ModifiesState.Should().BeFalse();
        c.Annotations.Destructive.Should().BeFalse();
        c.Annotations.PipelinePosition.Should().Be("source");
        c.Annotations.RequiresEpoch.Should().Be(0);
    }

    [Fact]
    public void LoadFromFile_Parses_Cmdlet_Without_Schema()
    {
        var path = Path.Combine(FixtureDir, "Test.Email.lobe.json");
        var d    = LobeDescriptor.LoadFromFile(path)!;

        var c = d.Cmdlets[1]; // Receive-TestEmailReceipt has no inputSchema/outputSchema
        c.Name.Should().Be("Receive-TestEmailReceipt");
        c.InputSchema.Should().BeNull();
        c.OutputSchema.Should().BeNull();
    }

    [Fact]
    public void LoadFromFile_Parses_Ai_Note()
    {
        var path = Path.Combine(FixtureDir, "Test.Email.lobe.json");
        var d    = LobeDescriptor.LoadFromFile(path)!;

        d.Ai.Note.Should().NotBeNullOrEmpty();
        d.Ai.Summary.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void LoadFromFile_Returns_Null_For_Missing_File()
    {
        var result = LobeDescriptor.LoadFromFile("/nonexistent/path.lobe.json");
        result.Should().BeNull();
    }

    [Fact]
    public void LoadFromFile_Parses_FutureEpoch_Descriptor()
    {
        var path = Path.Combine(FixtureDir, "Test.FutureEpoch.lobe.json");
        var d    = LobeDescriptor.LoadFromFile(path)!;

        d.Lobe.EpochRequired.Should().Be(1);
        d.Protocols[0].EpochRequired.Should().Be(1);
    }

    [Fact]
    public void LoadFromFile_Parses_Dependency_List()
    {
        var path = Path.Combine(FixtureDir, "Test.WithDep.lobe.json");
        var d    = LobeDescriptor.LoadFromFile(path)!;

        d.Dependencies.Lobes.Should().ContainSingle()
            .Which.Should().Be("Test.Email");
    }

    // ── Real descriptor files ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Svrn7.Email.lobe.json")]
    [InlineData("Svrn7.Calendar.lobe.json")]
    [InlineData("Svrn7.Presence.lobe.json")]
    [InlineData("Svrn7.Notifications.lobe.json")]
    [InlineData("Svrn7.Onboarding.lobe.json")]
    [InlineData("Svrn7.Invoicing.lobe.json")]
    [InlineData("Svrn7.Society.lobe.json")]
    [InlineData("Svrn7.Federation.lobe.json")]
    [InlineData("Svrn7.Common.lobe.json")]
    public void Real_Descriptor_Parses_And_Has_Required_Fields(string filename)
    {
        // The real .lobe.json files are copied next to the test binary via .csproj.
        var path = Path.Combine(AppContext.BaseDirectory, "lobes", filename);
        if (!File.Exists(path))
        {
            // Skip if files not copied — marks as skipped in CI rather than failing.
            return;
        }

        var d = LobeDescriptor.LoadFromFile(path);
        d.Should().NotBeNull($"{filename} should parse successfully");
        d!.Lobe.Id.Should().NotBeNullOrEmpty($"{filename} lobe.id must be present");
        d.Lobe.Name.Should().NotBeNullOrEmpty();
        d.Lobe.Module.Should().NotBeNullOrEmpty();
        d.Ai.Note.Should().NotBeNullOrEmpty($"{filename} ai._note must be present");
    }
}

// ── LobeManager Protocol Registry Tests ───────────────────────────────────────

public class LobeManagerRegistryTests : IDisposable
{
    private readonly string             _tmpDir;
    private readonly LobeManager        _manager;
    private readonly TdaOptions         _tdaOpts;

    public LobeManagerRegistryTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"lobe-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);

        // Copy test fixtures into temp dir so LobeManager can find them
        var fixtureDir = Path.Combine(AppContext.BaseDirectory, "fixtures");
        if (Directory.Exists(fixtureDir))
            foreach (var f in Directory.GetFiles(fixtureDir, "*.lobe.json"))
                File.Copy(f, Path.Combine(_tmpDir, Path.GetFileName(f)), overwrite: true);

        _tdaOpts = new TdaOptions
        {
            SocietyDid = "did:drn:alpha.svrn7.net",
            SocietyMessagingPrivateKeyEd25519 = Array.Empty<byte>(),
            LobesConfigPath = Path.Combine(_tmpDir, "lobes.config.json"),
        };

        // Write a minimal lobes.config.json (no eager, no jit — we use descriptors only)
        File.WriteAllText(_tdaOpts.LobesConfigPath, """{"eager":[],"jit":[]}""");

        // Create a minimal Svrn7RunspaceContext stub
        var ctx = CreateMinimalContext();

        _manager = new LobeManager(
            Options.Create(_tdaOpts),
            ctx,
            NullLogger<LobeManager>.Instance);
    }

    // ── RegisterFromDescriptor ────────────────────────────────────────────────

    [Fact]
    public void RegisterFromDescriptor_Registers_Prefix_Protocol()
    {
        var path = Path.Combine(_tmpDir, "Test.Email.lobe.json");
        _manager.RegisterFromDescriptor(path);

        // Prefix match: "https://test.example/protocols/email/1.0/message" prefix
        var reg = _manager.TryResolveProtocol(
            "https://test.example/protocols/email/1.0/message");
        reg.Should().NotBeNull();
        reg!.Entrypoint.Should().Be("Receive-TestEmail");
        reg.LobeName.Should().Be("Test.Email");
        reg.Match.Should().Be("prefix");
    }

    [Fact]
    public void RegisterFromDescriptor_Registers_Exact_Protocol()
    {
        var path = Path.Combine(_tmpDir, "Test.Email.lobe.json");
        _manager.RegisterFromDescriptor(path);

        var reg = _manager.TryResolveProtocol(
            "https://test.example/protocols/email/1.0/receipt");
        reg.Should().NotBeNull();
        reg!.Entrypoint.Should().Be("Receive-TestEmailReceipt");
        reg.Match.Should().Be("exact");
    }

    [Fact]
    public void RegisterFromDescriptor_Skips_FutureEpoch_LOBE()
    {
        // LobeManager is at Epoch 0 — future LOBE should be silently skipped
        var path = Path.Combine(_tmpDir, "Test.FutureEpoch.lobe.json");
        _manager.RegisterFromDescriptor(path);

        var reg = _manager.TryResolveProtocol(
            "https://test.example/protocols/future/1.0/request");
        reg.Should().BeNull("Epoch 1+ LOBE should not register in Epoch 0");
    }

    [Fact]
    public void RegisterFromDescriptor_IsIdempotent()
    {
        var path = Path.Combine(_tmpDir, "Test.Email.lobe.json");
        _manager.RegisterFromDescriptor(path);
        _manager.RegisterFromDescriptor(path); // second call — no exception, no duplicate

        // Registry should still have exactly one entry per URI
        _manager.ExactRegistrations.Keys
            .Count(k => k == "https://test.example/protocols/email/1.0/receipt")
            .Should().Be(1);
    }

    [Fact]
    public void RegisterFromDescriptor_Returns_Without_Error_For_Missing_File()
    {
        var act = () => _manager.RegisterFromDescriptor("/nonexistent/path.lobe.json");
        act.Should().NotThrow();
    }

    [Fact]
    public void IsRegistered_Returns_True_After_Registration()
    {
        var path = Path.Combine(_tmpDir, "Test.Email.lobe.json");
        _manager.RegisterFromDescriptor(path);

        _manager.IsRegistered("Test.Email").Should().BeTrue();
    }

    [Fact]
    public void IsRegistered_Returns_False_Before_Registration()
    {
        _manager.IsRegistered("Test.Email").Should().BeFalse();
    }

    // ── TryResolveProtocol ────────────────────────────────────────────────────

    [Fact]
    public void TryResolveProtocol_Exact_Beats_Prefix()
    {
        // Register the email LOBE which has both exact and prefix registrations
        _manager.RegisterFromDescriptor(Path.Combine(_tmpDir, "Test.Email.lobe.json"));

        // The receipt URI matches both:
        //   prefix: "https://test.example/protocols/email/1.0/message" (prefix of receipt? no)
        //   exact:  "https://test.example/protocols/email/1.0/receipt"
        // Exact should win
        var reg = _manager.TryResolveProtocol(
            "https://test.example/protocols/email/1.0/receipt");
        reg!.Entrypoint.Should().Be("Receive-TestEmailReceipt");
    }

    [Fact]
    public void TryResolveProtocol_Prefix_Matches_Subtype()
    {
        _manager.RegisterFromDescriptor(Path.Combine(_tmpDir, "Test.Email.lobe.json"));

        // The prefix "https://test.example/protocols/email/1.0/message" should match
        // any URI starting with that string
        var reg = _manager.TryResolveProtocol(
            "https://test.example/protocols/email/1.0/message/extended");
        reg.Should().NotBeNull();
        reg!.Entrypoint.Should().Be("Receive-TestEmail");
    }

    [Fact]
    public void TryResolveProtocol_Returns_Null_For_Unknown_Type()
    {
        _manager.RegisterFromDescriptor(Path.Combine(_tmpDir, "Test.Email.lobe.json"));

        var reg = _manager.TryResolveProtocol(
            "https://totally.unknown.example/protocols/xyz/1.0/msg");
        reg.Should().BeNull();
    }

    [Fact]
    public void TryResolveProtocol_LongestPrefix_Wins()
    {
        // Write two descriptors — one with a shorter prefix, one with a longer one
        var shortDesc = """
        {
          "lobe": { "id": "test.short", "name": "Test.Short", "title": "Short",
                    "description": "Short prefix.", "version": "1.0.0",
                    "author": "T", "organization": "T", "website": "https://t.example",
                    "license": "MIT", "epochRequired": 0, "module": "Test.Short.psm1" },
          "protocols": [{
            "uri": "https://test.example/protocols/",
            "title": "Short", "description": "Short.", "direction": "inbound",
            "match": "prefix", "entrypoint": "Invoke-Short", "epochRequired": 0
          }],
          "cmdlets": [],
          "dependencies": { "lobes": [], "packages": [] },
          "ai": { "_note": "t", "summary": "t", "useCases": [], "compositionHints": [], "limitations": [] }
        }
        """;
        var longDesc = """
        {
          "lobe": { "id": "test.long", "name": "Test.Long", "title": "Long",
                    "description": "Long prefix.", "version": "1.0.0",
                    "author": "T", "organization": "T", "website": "https://t.example",
                    "license": "MIT", "epochRequired": 0, "module": "Test.Long.psm1" },
          "protocols": [{
            "uri": "https://test.example/protocols/specific/",
            "title": "Long", "description": "Long.", "direction": "inbound",
            "match": "prefix", "entrypoint": "Invoke-Long", "epochRequired": 0
          }],
          "cmdlets": [],
          "dependencies": { "lobes": [], "packages": [] },
          "ai": { "_note": "t", "summary": "t", "useCases": [], "compositionHints": [], "limitations": [] }
        }
        """;

        File.WriteAllText(Path.Combine(_tmpDir, "Test.Short.lobe.json"), shortDesc);
        File.WriteAllText(Path.Combine(_tmpDir, "Test.Long.lobe.json"),  longDesc);
        _manager.RegisterFromDescriptor(Path.Combine(_tmpDir, "Test.Short.lobe.json"));
        _manager.RegisterFromDescriptor(Path.Combine(_tmpDir, "Test.Long.lobe.json"));

        // A message matching both — the longer prefix should win
        var reg = _manager.TryResolveProtocol(
            "https://test.example/protocols/specific/1.0/msg");
        reg.Should().NotBeNull();
        reg!.Entrypoint.Should().Be("Invoke-Long");
    }

    // ── FileSystemWatcher ─────────────────────────────────────────────────────

    [Fact]
    public async Task FileSystemWatcher_Detects_New_Descriptor_And_Registers()
    {
        // Build ISS to start the FileSystemWatcher
        _manager.BuildInitialSessionState();

        // Drop a new descriptor into the watched directory
        var newDesc = """
        {
          "lobe": { "id": "test.dynamic", "name": "Test.Dynamic", "title": "Dynamic",
                    "description": "Hot-loaded LOBE.", "version": "1.0.0",
                    "author": "T", "organization": "T", "website": "https://t.example",
                    "license": "MIT", "epochRequired": 0, "module": "Test.Dynamic.psm1" },
          "protocols": [{
            "uri": "https://test.example/protocols/dynamic/1.0/event",
            "title": "Dynamic Event", "description": "Hot-loaded event.",
            "direction": "inbound", "match": "exact",
            "entrypoint": "Invoke-DynamicHandler", "epochRequired": 0
          }],
          "cmdlets": [],
          "dependencies": { "lobes": [], "packages": [] },
          "ai": { "_note": "t", "summary": "t", "useCases": [], "compositionHints": [], "limitations": [] }
        }
        """;

        File.WriteAllText(
            Path.Combine(_tmpDir, "Test.Dynamic.lobe.json"), newDesc);

        // Wait for FileSystemWatcher to fire and LobeManager to process
        await Task.Delay(500);

        var reg = _manager.TryResolveProtocol(
            "https://test.example/protocols/dynamic/1.0/event");
        reg.Should().NotBeNull("hot-loaded LOBE should register within 500ms");
        reg!.Entrypoint.Should().Be("Invoke-DynamicHandler");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Svrn7RunspaceContext CreateMinimalContext()
    {
        // Create a minimal context with NullObject stubs.
        // We only need CurrentEpoch for registry epoch gating in these tests.
        var cache  = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());

        // Use a mock-by-hand approach — NullInboxStore and NullSocietyDriver
        return new Svrn7RunspaceContext(
            driver:          new NullSocietyDriver(),
            inbox:           new NullInboxStore(),
            cache:           cache,
            processedOrders: new NullProcessedOrderStore(),
            initialEpoch:    0);
    }

    public void Dispose()
    {
        _manager.Dispose();
        try { Directory.Delete(_tmpDir, recursive: true); } catch { }
    }
}

// ── Null stubs for test isolation ─────────────────────────────────────────────

internal sealed class NullInboxStore : Svrn7.Core.Interfaces.IInboxStore
{
    public Task EnqueueAsync(string t, string p, string? fromDid = null, string? wireId = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task<Svrn7.Core.Models.InboxMessage?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<Svrn7.Core.Models.InboxMessage?>(null);
    public Task<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.InboxMessage>> DequeueBatchAsync(int b = 20, CancellationToken ct = default) => Task.FromResult<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.InboxMessage>>(Array.Empty<Svrn7.Core.Models.InboxMessage>());
    public Task MarkProcessedAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    public Task MarkFailedAsync(string id, string err, bool retry = true, int max = Svrn7Constants.InboxMaxAttempts, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResetStuckMessagesAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<System.Collections.Generic.IReadOnlyDictionary<Svrn7.Core.Models.InboxMessageStatus, int>> GetStatusCountsAsync(CancellationToken ct = default) => Task.FromResult<System.Collections.Generic.IReadOnlyDictionary<Svrn7.Core.Models.InboxMessageStatus, int>>(new System.Collections.Generic.Dictionary<Svrn7.Core.Models.InboxMessageStatus, int>());
}

internal sealed class NullProcessedOrderStore : Svrn7.Core.Interfaces.IProcessedOrderStore
{
    public Task<string?> GetReceiptAsync(string transferId, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task StoreReceiptAsync(string transferId, string packed, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class NullSocietyDriver : Svrn7.Society.ISvrn7SocietyDriver
{
    // All methods throw NotImplementedException — tests do not call them.

    // ── ISvrn7SocietyDriver members ────────────────────────────────────────────
    public string SocietyDid => "did:drn:alpha.svrn7.net";
    public Task<Svrn7.Core.Models.OperationResult> RegisterCitizenInSocietyAsync(Svrn7.Core.Models.RegisterCitizenInSocietyRequest r, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> IsMemberAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<System.Collections.Generic.IReadOnlyList<string>> GetMemberCitizenDidsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> AddCitizenDidAsync(string citizenPrimaryDid, string methodName, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> TransferToExternalCitizenAsync(Svrn7.Core.Models.TransferRequest r, string targetSocietyDid, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> TransferToFederationAsync(string payerDid, long amountGrana, string nonce, string signature, string? memo = null, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<string> HandleIncomingTransferMessageAsync(string packedDIDCommMessage, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> RegisterSocietyDidMethodAsync(string methodName, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> DeregisterSocietyDidMethodAsync(string methodName, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.SocietyDidMethodRecord>> GetSocietyDidMethodsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.SocietyRecord?> GetOwnSocietyAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OverdraftStatus> GetOverdraftStatusAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.SocietyOverdraftRecord?> GetOverdraftRecordAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Interfaces.CrossSocietyVcQueryResult> FindVcsBySubjectAcrossSocietiesAsync(string subjectDid, TimeSpan? timeout = null, CancellationToken ct = default) => throw new NotImplementedException();

    // ── ISvrn7Driver members ───────────────────────────────────────────────────
    public Svrn7.Core.Interfaces.IDidDocumentRegistry DidRegistry => throw new NotImplementedException();
    public Svrn7.Core.Interfaces.IVcRegistry VcRegistry => throw new NotImplementedException();
    public int GetCurrentEpoch() => throw new NotImplementedException();
    public Task AdvanceEpochAuthorisedAsync(int toEpoch, string governanceRef, string foundationSignature, string? notes = null, CancellationToken ct = default) => throw new NotImplementedException();
    public Task RecordEpochTransitionAsync(int toEpoch, string governanceRef, string? notes = null, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> RegisterCitizenAsync(Svrn7.Core.Models.RegisterCitizenRequest r, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.CitizenRecord?> GetCitizenAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> IsCitizenActiveAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.CitizenDidRecord>> GetAllDidsForCitizenAsync(string primaryDid, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<string?> ResolveCitizenPrimaryDidAsync(string anyDid, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> RegisterSocietyAsync(Svrn7.Core.Models.RegisterSocietyRequest r, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.SocietyRecord?> GetSocietyAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.SocietyRecord>> GetAllSocietiesAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> IsSocietyActiveAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeactivateSocietyAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> RegisterAdditionalDidMethodAsync(string societyDid, string methodName, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> DeregisterDidMethodAsync(string societyDid, string methodName, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.DidMethodStatus> GetDidMethodStatusAsync(string methodName, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.SocietyDidMethodRecord>> GetAllDidMethodsAsync(string? societyDid = null, Svrn7.Core.Models.DidMethodStatus? statusFilter = null, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> TransferAsync(Svrn7.Core.Models.TransferRequest r, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.OperationResult>> BatchTransferAsync(System.Collections.Generic.IEnumerable<Svrn7.Core.Models.TransferRequest> requests, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<decimal> GetBalanceSvrn7Async(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<long> GetBalanceGranaAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.BalanceResult> GetBalanceResultAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.FederationRecord?> GetFederationAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> UpdateFederationSupplyAsync(long newTotalSupplyGrana, string foundationSignature, string governanceRef, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> InitialiseFederationAsync(string federationDid, string federationName, string publicKeyHex, string primaryDidMethodName, CancellationToken ct = default) => throw new NotImplementedException();
    public Task CreateDidAsync(Svrn7.Core.Models.DidDocument document, CancellationToken ct = default) => throw new NotImplementedException();
    public Task UpdateDidAsync(Svrn7.Core.Models.DidDocument document, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.DidResolutionResult> ResolveDidAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeactivateDidAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SuspendDidAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task ReinstateDidAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.DidDocument>> GetDidHistoryAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> IsDidActiveAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<string?> FindDidByPublicKeyAsync(string publicKeyHex, CancellationToken ct = default) => throw new NotImplementedException();
    public Task StoreVcAsync(Svrn7.Core.Models.VcRecord record, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.VcRecord?> GetVcByIdAsync(string vcId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.VcRecord>> GetVcsBySubjectAsync(string subjectDid, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.VcRecord>> GetVcsByIssuerAsync(string issuerDid, CancellationToken ct = default) => throw new NotImplementedException();
    public Task RevokeVcAsync(string vcId, string reason, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SuspendVcAsync(string vcId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task ReinstateVcAsync(string vcId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.VcStatus> GetVcStatusAsync(string vcId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> ExpireStaleVcsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<string> AppendToLogAsync(string entryType, string payloadJson, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<string> GetMerkleRootAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.TreeHead> SignMerkleTreeHeadAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<long> GetLogSizeAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.TreeHead?> GetLatestTreeHeadAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> ErasePersonAsync(string did, string controllerSignature, DateTimeOffset requestTimestamp, CancellationToken ct = default) => throw new NotImplementedException();
    public Svrn7.Core.Models.Svrn7KeyPair GenerateSecp256k1KeyPair() => throw new NotImplementedException();
    public Svrn7.Core.Models.Svrn7KeyPair GenerateEd25519KeyPair() => throw new NotImplementedException();
    public string SignSecp256k1(byte[] payload, byte[] privateKeyBytes) => throw new NotImplementedException();
    public bool VerifySecp256k1(byte[] payload, string cesrSig, string publicKeyHex) => throw new NotImplementedException();
    public Task<string> Blake3HexAsync(byte[] data, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<string> Base58EncodeAsync(byte[] data, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> LiftAllWalletRestrictionsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// ── KestrelListenerService Integration Tests ──────────────────────────────────
//
// Starts a real Kestrel server in cleartext HTTP/2 dev mode (no TLS cert).
// Uses stub IDIDCommService and a recording IInboxStore to verify the
// POST /didcomm inbound pipeline end-to-end.

public sealed class KestrelListenerServiceIntegrationTests : IAsyncLifetime
{
    private readonly KestrelListenerService _listener;
    private readonly RecordingInboxStore   _inbox;
    private readonly int                   _port;

    public KestrelListenerServiceIntegrationTests()
    {
        _port  = FindFreePort();
        _inbox = new RecordingInboxStore();

        var opts = Options.Create(new TdaOptions
        {
            SocietyDid                        = "did:drn:test.svrn7.net",
            SocietyMessagingPrivateKeyEd25519 = Array.Empty<byte>(),
            ListenPort                        = _port,
            TlsCertificatePath                = null,   // cleartext dev mode — no mTLS
            RequireMutualTls                  = false,
        });

        _listener = new KestrelListenerService(
            opts,
            new StubDIDCommService("test/1.0/msg", """{"amount":500}"""),
            _inbox,
            NullLogger<KestrelListenerService>.Instance);
    }

    public Task InitializeAsync() => _listener.StartAsync(CancellationToken.None);

    public async Task DisposeAsync()
    {
        await _listener.StopAsync(CancellationToken.None);
        await _listener.DisposeAsync();
    }

    // ── 202 Accepted: valid body ──────────────────────────────────────────────

    [Fact]
    public async Task Post_ValidDIDCommBody_Returns202_AndEnqueues()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        using var handler = new SocketsHttpHandler();
        using var client  = new HttpClient(handler)
        {
            BaseAddress           = new Uri($"http://localhost:{_port}"),
            DefaultRequestVersion = new Version(2, 0),
            DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionOrHigher,
        };

        var content  = new StringContent("packed-didcomm-body",
            System.Text.Encoding.UTF8, "application/didcomm+json");
        var response = await client.PostAsync("/didcomm", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _inbox.Messages.Should().ContainSingle(
            because: "a valid unpacked message must be enqueued");
        _inbox.Messages[0].Type.Should().Be("test/1.0/msg");
    }

    // ── 400 Bad Request: empty body ───────────────────────────────────────────

    [Fact]
    public async Task Post_EmptyBody_Returns400_DoesNotEnqueue()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        using var handler = new SocketsHttpHandler();
        using var client  = new HttpClient(handler)
        {
            BaseAddress           = new Uri($"http://localhost:{_port}"),
            DefaultRequestVersion = new Version(2, 0),
            DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionOrHigher,
        };

        var response = await client.PostAsync("/didcomm", new StringContent(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _inbox.Messages.Should().BeEmpty();
    }

    // ── 400 Bad Request: unpack failure ──────────────────────────────────────

    [Fact]
    public async Task Post_UnpackFailure_Returns400_DoesNotEnqueue()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        var port        = FindFreePort();
        var inbox       = new RecordingInboxStore();
        var badListener = new KestrelListenerService(
            Options.Create(new TdaOptions
            {
                SocietyDid                        = "did:drn:test.svrn7.net",
                SocietyMessagingPrivateKeyEd25519 = Array.Empty<byte>(),
                ListenPort                        = port,
                TlsCertificatePath                = null,
                RequireMutualTls                  = false,
            }),
            new ThrowingDIDCommService(),
            inbox,
            NullLogger<KestrelListenerService>.Instance);

        await badListener.StartAsync(CancellationToken.None);
        try
        {
            using var handler = new SocketsHttpHandler();
            using var client  = new HttpClient(handler)
            {
                BaseAddress           = new Uri($"http://localhost:{port}"),
                DefaultRequestVersion = new Version(2, 0),
                DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionOrHigher,
            };

            var response = await client.PostAsync("/didcomm",
                new StringContent("invalid-packed-body",
                    System.Text.Encoding.UTF8, "application/didcomm+json"));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            inbox.Messages.Should().BeEmpty(
                because: "a message that fails unpack must not be enqueued");
        }
        finally
        {
            await badListener.StopAsync(CancellationToken.None);
            await badListener.DisposeAsync();
        }
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

// ── Stubs for KestrelListenerService integration tests ────────────────────────

internal sealed class RecordingInboxStore : IInboxStore
{
    public List<(string Type, string Payload)> Messages { get; } = new();

    public Task EnqueueAsync(string messageType, string packedPayload, string? fromDid = null, string? wireId = null, CancellationToken ct = default)
    {
        Messages.Add((messageType, packedPayload));
        return Task.CompletedTask;
    }

    public Task<InboxMessage?> GetByIdAsync(string objectId, CancellationToken ct = default) =>
        Task.FromResult<InboxMessage?>(null);
    public Task<IReadOnlyList<InboxMessage>> DequeueBatchAsync(int batchSize = 20, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<InboxMessage>>(Array.Empty<InboxMessage>());
    public Task MarkProcessedAsync(string messageId, CancellationToken ct = default) => Task.CompletedTask;
    public Task MarkFailedAsync(string messageId, string error, bool retry = true, int maxAttempts = Svrn7Constants.InboxMaxAttempts, CancellationToken ct = default) =>
        Task.CompletedTask;
    public Task ResetStuckMessagesAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyDictionary<InboxMessageStatus, int>> GetStatusCountsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<InboxMessageStatus, int>>(
            new Dictionary<InboxMessageStatus, int>());
}

/// <summary>Stub IDIDCommService: UnpackAsync always returns a pre-configured message.</summary>
internal sealed class StubDIDCommService(string type, string body) : IDIDCommService
{
    public DIDCommMessageBuilder NewMessage() => new();
    public Task<string> PackPlaintextAsync(DIDCommMessage m, CancellationToken ct = default) =>
        Task.FromResult("{}");
    public Task<string> PackSignedAsync(DIDCommMessage m, byte[] key, CancellationToken ct = default) =>
        Task.FromResult("{}");
    public Task<string> PackEncryptedAsync(DIDCommMessage m, byte[] recipKey, byte[] sendKey,
        DIDCommPackMode mode = DIDCommPackMode.SignThenEncrypt, CancellationToken ct = default) =>
        Task.FromResult("{}");
    public Task<string> PackSignedAndEncryptedAsync(DIDCommMessage m, byte[] recipKey, byte[] sendKey,
        CancellationToken ct = default) => Task.FromResult("{}");
    public Task<DIDCommUnpackedMessage> UnpackAsync(string packed,
        byte[]? recipientPrivateKey = null, CancellationToken ct = default) =>
        Task.FromResult(new DIDCommUnpackedMessage
            { Type = type, Body = body, Mode = DIDCommPackMode.Plaintext });
}

/// <summary>Stub IDIDCommService: UnpackAsync always throws — simulates a malformed message.</summary>
internal sealed class ThrowingDIDCommService : IDIDCommService
{
    public DIDCommMessageBuilder NewMessage() => new();
    public Task<string> PackPlaintextAsync(DIDCommMessage m, CancellationToken ct = default) =>
        Task.FromResult("{}");
    public Task<string> PackSignedAsync(DIDCommMessage m, byte[] key, CancellationToken ct = default) =>
        Task.FromResult("{}");
    public Task<string> PackEncryptedAsync(DIDCommMessage m, byte[] recipKey, byte[] sendKey,
        DIDCommPackMode mode = DIDCommPackMode.SignThenEncrypt, CancellationToken ct = default) =>
        Task.FromResult("{}");
    public Task<string> PackSignedAndEncryptedAsync(DIDCommMessage m, byte[] recipKey, byte[] sendKey,
        CancellationToken ct = default) => Task.FromResult("{}");
    public Task<DIDCommUnpackedMessage> UnpackAsync(string packed,
        byte[]? recipientPrivateKey = null, CancellationToken ct = default) =>
        throw new InvalidOperationException("Simulated DIDComm unpack failure.");
}

// ── Svrn7Constants Inbox Reliability Tests ────────────────────────────────────

public class Svrn7ConstantsInboxReliabilityTests
{
    [Fact]
    public void InboxMaxAttempts_Is_1()
    {
        Svrn7Constants.InboxMaxAttempts.Should().Be(1,
            because: "transactional messages must never retry — double-spend risk");
    }

    [Fact]
    public void InboxNonTransactionalMaxAttempts_Is_3()
    {
        Svrn7Constants.InboxNonTransactionalMaxAttempts.Should().Be(3,
            because: "non-financial messages may safely retry on transient failure");
    }

    [Theory]
    [InlineData(Svrn7Constants.Protocols.TransferRequest)]
    [InlineData(Svrn7Constants.Protocols.TransferReceipt)]
    [InlineData(Svrn7Constants.Protocols.TransferOrder)]
    [InlineData(Svrn7Constants.Protocols.TransferOrderReceipt)]
    [InlineData(Svrn7Constants.Protocols.OverdraftDrawRequest)]
    [InlineData(Svrn7Constants.Protocols.OverdraftDrawReceipt)]
    [InlineData(Svrn7Constants.Protocols.EndowmentTopUp)]
    [InlineData(Svrn7Constants.Protocols.SupplyUpdate)]
    [InlineData(Svrn7Constants.Protocols.InvoiceRequest)]
    [InlineData(Svrn7Constants.Protocols.InvoiceReceipt)]
    public void TransactionalProtocols_Contains_Financial_Protocol(string protocol)
    {
        Svrn7Constants.TransactionalProtocols.Should().Contain(protocol,
            because: $"{protocol} carries financial state and must never retry");
    }

    [Theory]
    [InlineData(Svrn7Constants.Protocols.DidResolveRequest)]
    [InlineData(Svrn7Constants.Protocols.DidResolveResponse)]
    [InlineData(Svrn7Constants.Protocols.OnboardRequest)]
    [InlineData(Svrn7Constants.Protocols.OnboardReceipt)]
    public void TransactionalProtocols_Does_Not_Contain_Query_Protocol(string protocol)
    {
        Svrn7Constants.TransactionalProtocols.Should().NotContain(protocol,
            because: $"{protocol} carries no financial state and may safely retry");
    }

    [Fact]
    public void TransactionalProtocols_Lookup_Is_Case_Insensitive()
    {
        var upper = Svrn7Constants.Protocols.TransferOrder.ToUpperInvariant();
        Svrn7Constants.TransactionalProtocols.Should().Contain(upper,
            because: "case-insensitive comparison is required for @type URI matching");
    }
}

// ── LiteInboxStore Stuck-Message Recovery Tests ───────────────────────────────

public class LiteInboxStoreStuckMessageTests : IDisposable
{
    private readonly InboxLiteContext   _ctx;
    private readonly LiteInboxStore     _store;
    private const string                SocietyDid = "did:drn:alpha.svrn7.net";

    public LiteInboxStoreStuckMessageTests()
    {
        _ctx   = new InboxLiteContext(":memory:");
        var opts = Options.Create(new Svrn7SocietyOptions { SocietyDid = SocietyDid });
        _store = new LiteInboxStore(_ctx, opts, NullLogger<LiteInboxStore>.Instance);
    }

    [Fact]
    public async Task ResetStuckMessages_Restores_Processing_To_Pending()
    {
        // Enqueue two messages and dequeue them — they move to Processing.
        await _store.EnqueueAsync("test/1.0/msg", "payload-a");
        await _store.EnqueueAsync("test/1.0/msg", "payload-b");
        var batch = await _store.DequeueBatchAsync(10);
        batch.Should().HaveCount(2);

        var counts = await _store.GetStatusCountsAsync();
        counts[InboxMessageStatus.Processing].Should().Be(2,
            because: "DequeueBatchAsync moves messages to Processing");

        // Simulate an unclean shutdown recovery.
        await _store.ResetStuckMessagesAsync();

        counts = await _store.GetStatusCountsAsync();
        counts.TryGetValue(InboxMessageStatus.Processing, out var processing);
        processing.Should().Be(0, because: "ResetStuckMessagesAsync clears all Processing messages");
        counts[InboxMessageStatus.Pending].Should().Be(2,
            because: "stuck messages are restored to Pending for re-processing");
    }

    [Fact]
    public async Task ResetStuckMessages_Does_Not_Affect_Processed_Messages()
    {
        await _store.EnqueueAsync("test/1.0/msg", "payload");
        var batch = await _store.DequeueBatchAsync(1);
        await _store.MarkProcessedAsync(batch[0].Id);

        await _store.ResetStuckMessagesAsync();

        var counts = await _store.GetStatusCountsAsync();
        counts[InboxMessageStatus.Processed].Should().Be(1);
        counts.TryGetValue(InboxMessageStatus.Pending, out var pending);
        pending.Should().Be(0);
    }

    [Fact]
    public async Task ResetStuckMessages_Does_Not_Affect_Failed_Messages()
    {
        await _store.EnqueueAsync("test/1.0/msg", "payload");
        var batch = await _store.DequeueBatchAsync(1);
        await _store.MarkFailedAsync(batch[0].Id, "simulated error",
            retry: false, maxAttempts: Svrn7Constants.InboxMaxAttempts);

        await _store.ResetStuckMessagesAsync();

        var counts = await _store.GetStatusCountsAsync();
        counts[InboxMessageStatus.Failed].Should().Be(1,
            because: "Failed messages are permanently dead-lettered and must not be reset");
        counts.TryGetValue(InboxMessageStatus.Pending, out var pending);
        pending.Should().Be(0);
    }

    [Fact]
    public async Task ResetStuckMessages_On_Empty_Inbox_Does_Not_Throw()
    {
        var act = async () => await _store.ResetStuckMessagesAsync();
        await act.Should().NotThrowAsync();
    }

    public void Dispose() => _ctx.Dispose();
}

// ── DIDCommMessageSwitchboard Startup Recovery Tests ─────────────────────────

public class SwitchboardStartupTests : IDisposable
{
    private readonly string _lobeDir;

    public SwitchboardStartupTests()
    {
        _lobeDir = Path.Combine(Path.GetTempPath(), $"sb-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_lobeDir);
        File.WriteAllText(Path.Combine(_lobeDir, "lobes.config.json"), """{"eager":[],"jit":[]}""");
    }

    [Fact]
    public async Task StartupAsync_Calls_ResetStuckMessages()
    {
        var inbox      = new TrackingInboxStore();
        var outbox     = new TrackingOutboxStore(Array.Empty<Svrn7.Core.Models.OutboxRecord>());
        var switchboard = CreateSwitchboard(inbox, outbox);

        await switchboard.StartupAsync(CancellationToken.None);

        inbox.ResetStuckCalled.Should().BeTrue(
            because: "stuck inbox messages from a prior unclean shutdown must be recovered on startup");
    }

    [Fact]
    public async Task StartupAsync_Calls_GetPendingOutbox()
    {
        var inbox      = new TrackingInboxStore();
        var outbox     = new TrackingOutboxStore(Array.Empty<Svrn7.Core.Models.OutboxRecord>());
        var switchboard = CreateSwitchboard(inbox, outbox);

        await switchboard.StartupAsync(CancellationToken.None);

        outbox.GetPendingCalled.Should().BeTrue(
            because: "dead-lettered outbound messages from the prior session must be re-enqueued");
    }

    [Fact]
    public async Task StartupAsync_ReEnqueues_PendingOutbox_Into_OutboundQueue()
    {
        var inbox = new TrackingInboxStore();
        var pending = new[]
        {
            new Svrn7.Core.Models.OutboxRecord
            {
                Id            = TdaResourceId.Build("test.svrn7.net", "inbox", "outbox",
                                    LiteDB.ObjectId.NewObjectId().ToString()),
                PeerEndpoint  = "https://peer.example",
                PackedMessage = "packed-1",
                MessageType   = "outbound",
                AttemptCount  = 3,
            },
            new Svrn7.Core.Models.OutboxRecord
            {
                Id            = TdaResourceId.Build("test.svrn7.net", "inbox", "outbox",
                                    LiteDB.ObjectId.NewObjectId().ToString()),
                PeerEndpoint  = "https://peer2.example",
                PackedMessage = "packed-2",
                MessageType   = "outbound",
                AttemptCount  = 3,
            },
        };
        var outbox      = new TrackingOutboxStore(pending);
        var switchboard = CreateSwitchboard(inbox, outbox);

        await switchboard.StartupAsync(CancellationToken.None);

        switchboard.PendingOutboundCount.Should().Be(2,
            because: "both dead-lettered outbound records must be re-enqueued for delivery");
    }

    [Fact]
    public async Task StartupAsync_Continues_If_InboxStore_Throws()
    {
        var inbox      = new ThrowingResetInboxStore();
        var outbox     = new TrackingOutboxStore(Array.Empty<Svrn7.Core.Models.OutboxRecord>());
        var switchboard = CreateSwitchboard(inbox, outbox);

        // StartupAsync must not propagate the exception — it is best-effort.
        var act = async () => await switchboard.StartupAsync(CancellationToken.None);
        await act.Should().NotThrowAsync(
            because: "a failing inbox store must not prevent the TDA from starting");

        // The outbox recovery should still have been attempted.
        outbox.GetPendingCalled.Should().BeTrue();
    }

    [Fact]
    public async Task StartupAsync_Empty_Outbox_Does_Not_Enqueue_Anything()
    {
        var inbox      = new TrackingInboxStore();
        var outbox     = new TrackingOutboxStore(Array.Empty<Svrn7.Core.Models.OutboxRecord>());
        var switchboard = CreateSwitchboard(inbox, outbox);

        await switchboard.StartupAsync(CancellationToken.None);

        switchboard.PendingOutboundCount.Should().Be(0);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private DIDCommMessageSwitchboard CreateSwitchboard(
        IInboxStore inbox,
        Svrn7.Core.Interfaces.IOutboxStore outbox,
        TdaOptions? overrideOpts = null)
    {
        var tdaOpts = overrideOpts ?? new TdaOptions
        {
            SocietyDid                        = "did:drn:test.svrn7.net",
            SocietyMessagingPrivateKeyEd25519 = Array.Empty<byte>(),
            LobesConfigPath                   = Path.Combine(_lobeDir, "lobes.config.json"),
            MaxMessageAgeSeconds              = 3600,
            LobeInvocationTimeoutSeconds      = 30,
        };

        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var ctx = new Svrn7RunspaceContext(
            new NullSocietyDriver(), inbox, cache, new NullProcessedOrderStore(), 0);
        var lobes = new LobeManager(
            Options.Create(tdaOpts), ctx, NullLogger<LobeManager>.Instance);
        var pool = new RunspacePoolManager(
            Options.Create(tdaOpts), lobes, ctx, NullLogger<RunspacePoolManager>.Instance);

        return new DIDCommMessageSwitchboard(
            ctx, pool, inbox, outbox, lobes,
            new NullHttpClientFactory(),
            Options.Create(tdaOpts),
            NullLogger<DIDCommMessageSwitchboard>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_lobeDir, recursive: true); } catch { }
    }
}

// ── KestrelListenerService Rate Limit Tests ───────────────────────────────────

public sealed class KestrelListenerRateLimitTests : IAsyncLifetime
{
    private readonly KestrelListenerService _listener;
    private readonly int                   _port;

    public KestrelListenerRateLimitTests()
    {
        _port = FindFreePort();

        _listener = new KestrelListenerService(
            Options.Create(new TdaOptions
            {
                SocietyDid                        = "did:drn:test.svrn7.net",
                SocietyMessagingPrivateKeyEd25519 = Array.Empty<byte>(),
                ListenPort                        = _port,
                TlsCertificatePath                = null,
                RequireMutualTls                  = false,
                RateLimitRequestsPerSecond        = 1, // only 1 request per second allowed
            }),
            new StubDIDCommService("test/1.0/msg", "{}"),
            new RecordingInboxStore(),
            NullLogger<KestrelListenerService>.Instance);
    }

    public Task InitializeAsync() => _listener.StartAsync(CancellationToken.None);

    public async Task DisposeAsync()
    {
        await _listener.StopAsync(CancellationToken.None);
        await _listener.DisposeAsync();
    }

    [Fact]
    public async Task Second_Request_In_Same_Window_Returns_429()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        using var handler = new SocketsHttpHandler();
        using var client  = new HttpClient(handler)
        {
            BaseAddress           = new Uri($"http://localhost:{_port}"),
            DefaultRequestVersion = new Version(2, 0),
            DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionOrHigher,
        };

        // First request — within the 1-req/sec permit limit.
        var first = await client.PostAsync("/didcomm",
            new StringContent("packed-body", System.Text.Encoding.UTF8, "application/didcomm+json"));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted,
            because: "the first request in the rate-limit window should be accepted");

        // Second request — immediately after, same window, limit exceeded.
        var second = await client.PostAsync("/didcomm",
            new StringContent("packed-body", System.Text.Encoding.UTF8, "application/didcomm+json"));
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            because: "a second request within the same 1-second window should be rejected");
    }

    [Fact]
    public async Task RateLimit_Disabled_When_Zero_Allows_Many_Requests()
    {
        // Spin up a separate listener with rate limiting disabled.
        var port      = FindFreePort();
        var noLimit   = new KestrelListenerService(
            Options.Create(new TdaOptions
            {
                SocietyDid                        = "did:drn:test.svrn7.net",
                SocietyMessagingPrivateKeyEd25519 = Array.Empty<byte>(),
                ListenPort                        = port,
                TlsCertificatePath                = null,
                RequireMutualTls                  = false,
                RateLimitRequestsPerSecond        = 0, // disabled
            }),
            new StubDIDCommService("test/1.0/msg", "{}"),
            new RecordingInboxStore(),
            NullLogger<KestrelListenerService>.Instance);

        await noLimit.StartAsync(CancellationToken.None);
        try
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            using var handler = new SocketsHttpHandler();
            using var client  = new HttpClient(handler)
            {
                BaseAddress           = new Uri($"http://localhost:{port}"),
                DefaultRequestVersion = new Version(2, 0),
                DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionOrHigher,
            };

            // Send 5 requests — all should succeed.
            for (int i = 0; i < 5; i++)
            {
                var resp = await client.PostAsync("/didcomm",
                    new StringContent("packed-body",
                        System.Text.Encoding.UTF8, "application/didcomm+json"));
                resp.StatusCode.Should().Be(HttpStatusCode.Accepted,
                    because: $"request {i + 1} should succeed when rate limiting is disabled");
            }
        }
        finally
        {
            await noLimit.StopAsync(CancellationToken.None);
            await noLimit.DisposeAsync();
        }
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

// ── Additional stubs ──────────────────────────────────────────────────────────

/// <summary>IInboxStore stub that tracks whether ResetStuckMessagesAsync was called.</summary>
internal sealed class TrackingInboxStore : IInboxStore
{
    public bool ResetStuckCalled { get; private set; }

    public Task EnqueueAsync(string t, string p, string? fromDid = null, string? wireId = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task<InboxMessage?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<InboxMessage?>(null);
    public Task<IReadOnlyList<InboxMessage>> DequeueBatchAsync(int b = 20, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InboxMessage>>(Array.Empty<InboxMessage>());
    public Task MarkProcessedAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    public Task MarkFailedAsync(string id, string err, bool retry = true, int maxAttempts = Svrn7Constants.InboxMaxAttempts, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyDictionary<InboxMessageStatus, int>> GetStatusCountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<InboxMessageStatus, int>>(new Dictionary<InboxMessageStatus, int>());
    public Task ResetStuckMessagesAsync(CancellationToken ct = default)
    {
        ResetStuckCalled = true;
        return Task.CompletedTask;
    }
}

/// <summary>IOutboxStore stub: returns a pre-configured list of pending records; tracks whether GetPendingAsync was called.</summary>
internal sealed class TrackingOutboxStore : Svrn7.Core.Interfaces.IOutboxStore
{
    private readonly IReadOnlyList<Svrn7.Core.Models.OutboxRecord> _pending;
    public bool GetPendingCalled { get; private set; }

    public TrackingOutboxStore(IEnumerable<Svrn7.Core.Models.OutboxRecord> pending)
        => _pending = pending.ToList();

    public Task EnqueueAsync(Svrn7.Core.Models.OutboxRecord record, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<Svrn7.Core.Models.OutboxRecord>> GetPendingAsync(CancellationToken ct = default)
    {
        GetPendingCalled = true;
        return Task.FromResult(_pending);
    }

    public Task MarkRetriedAsync(string id, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>IInboxStore stub: ResetStuckMessagesAsync throws to simulate a failed store.</summary>
internal sealed class ThrowingResetInboxStore : IInboxStore
{
    public Task EnqueueAsync(string t, string p, string? fromDid = null, string? wireId = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task<InboxMessage?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<InboxMessage?>(null);
    public Task<IReadOnlyList<InboxMessage>> DequeueBatchAsync(int b = 20, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InboxMessage>>(Array.Empty<InboxMessage>());
    public Task MarkProcessedAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    public Task MarkFailedAsync(string id, string err, bool retry = true, int maxAttempts = Svrn7Constants.InboxMaxAttempts, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyDictionary<InboxMessageStatus, int>> GetStatusCountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<InboxMessageStatus, int>>(new Dictionary<InboxMessageStatus, int>());
    public Task ResetStuckMessagesAsync(CancellationToken ct = default)
        => throw new InvalidOperationException("Simulated inbox store failure on reset.");
}

/// <summary>IHttpClientFactory stub: returns a plain HttpClient (no real connections in tests that use it).</summary>
internal sealed class NullHttpClientFactory : System.Net.Http.IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new HttpClient();
}
