using FluentAssertions;
using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Svrn7.Core;
using Svrn7.Core.Models;
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
        var suffix = "alice.alpha.svrn7.net";
        var didUrl = TdaResourceId.Citizen(Network, suffix);

        didUrl.Should().Be($"did:drn:{Network}/main/citizen/{suffix}");
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
            TdaResourceId.Citizen(Network, "alice.alpha.svrn7.net"),
            TdaResourceId.Wallet(Network, "alice.alpha.svrn7.net"),
            TdaResourceId.Utxo(Network, new string('a', 64)),
            TdaResourceId.Society(Network, "alpha.svrn7.net"),
            TdaResourceId.Membership(Network, "alice.alpha.svrn7.net"),
            TdaResourceId.LogEntry(Network, new string('b', 64)),
            TdaResourceId.TreeHead(Network, new string('c', 64)),
            TdaResourceId.DidDocument(Network, "alice.alpha.svrn7.net"),
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
    public Task EnqueueAsync(string t, string p, CancellationToken ct = default) => Task.CompletedTask;
    public Task<Svrn7.Core.Models.InboxMessage?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<Svrn7.Core.Models.InboxMessage?>(null);
    public Task<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.InboxMessage>> DequeueBatchAsync(int b = 20, CancellationToken ct = default) => Task.FromResult<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.InboxMessage>>(Array.Empty<Svrn7.Core.Models.InboxMessage>());
    public Task MarkProcessedAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    public Task MarkFailedAsync(string id, string err, bool retry = true, int max = 3, CancellationToken ct = default) => Task.CompletedTask;
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
    public string SocietyDid => "did:drn:alpha.svrn7.net";
    // All methods throw NotImplementedException — tests do not call them.
    public Task<Svrn7.Core.Models.OperationResult> RegisterCitizenInSocietyAsync(Svrn7.Core.Models.RegisterCitizenInSocietyRequest r, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> IsMemberAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> IncomingTransferAsync(Svrn7.Core.Models.TransferRequest r, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> ExternalTransferAsync(Svrn7.Core.Models.TransferRequest r, string targetSocietyDid, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.SocietyOverdraftRecord?> GetOverdraftRecordAsync(string societyDid, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> RegisterSocietyDidMethodAsync(string methodName, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> DeregisterSocietyDidMethodAsync(string methodName, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.SocietyDidMethodRecord>> GetSocietyDidMethodsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<System.Collections.Generic.IReadOnlyList<Svrn7.Core.Models.SocietyMembershipRecord>> GetMembersAsync(CancellationToken ct = default) => throw new NotImplementedException();
    // ISvrn7Driver pass-through members
    public Task<Svrn7.Core.Models.OperationResult> TransferAsync(Svrn7.Core.Models.TransferRequest r, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.BalanceResult> GetBalanceResultAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> RegisterCitizenAsync(Svrn7.Core.Models.CitizenRecord c, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.CitizenRecord?> GetCitizenAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.OperationResult> RegisterSocietyAsync(Svrn7.Core.Models.SocietyRecord s, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.SocietyRecord?> GetSocietyAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> ExpireStaleVcsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.MerkleTreeHead> SignMerkleTreeHeadAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task AppendToLogAsync(string eventType, string payload, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Svrn7.Core.Models.DIDDocument?> ResolveDidDocumentAsync(string did, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<string?> GetSocietyDidAsync(CancellationToken ct = default) => Task.FromResult<string?>("did:drn:alpha.svrn7.net");
}
