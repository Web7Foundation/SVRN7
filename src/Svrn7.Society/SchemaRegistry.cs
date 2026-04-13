using LiteDB;
using Microsoft.Extensions.Logging;
using Svrn7.Core.Models;

namespace Svrn7.Society;

// ── Schema Registry ───────────────────────────────────────────────────────────
//
// Derived from: "Schema Registry (LiteDB)" + "Schema Resolver" (Data Access)
//               inside "Society TDA Only" Conditional Components — DSA 0.24 Epoch 0.
//
// PPML Conditional Components rule: these artefacts are instantiated ONLY when the
// Host is configured as a Society TDA (i.e., AddSvrn7Society() is called, not just
// AddSvrn7Federation()).
//
// DID URL addressing (draft-herman-drn-resource-addressing-00):
//   did:drn:alpha.svrn7.net/schemas/schema/{schemaName}
//   Key type: Named (common name) — e.g., "CitizenEndowmentCredential"
//   Schema names: alphanumeric, hyphens, dots only. PascalCase or kebab-case.

// ── Models ────────────────────────────────────────────────────────────────────

/// <summary>
/// A JSON Schema document registered in the Schema Registry.
/// Keyed by a human-meaningful common name (e.g., "CitizenEndowmentCredential").
/// Derived from: Schema Registry (LiteDB) — DSA 0.24 Epoch 0 (PPML).
/// </summary>
public sealed class SchemaRecord
{
    [BsonId]
    public ObjectId     Id          { get; init; } = ObjectId.NewObjectId();

    /// <summary>
    /// Human-meaningful common name — the natural key and DID URL segment.
    /// Must be unique within the registry. Alphanumeric, hyphens, dots only.
    /// Examples: "CitizenEndowmentCredential", "TransferReceiptCredential-v2".
    /// </summary>
    public required string Name        { get; init; }

    /// <summary>JSON Schema document (RFC 8259 JSON string).</summary>
    public required string SchemaJson  { get; init; }

    /// <summary>MIME type of the schema (default: application/schema+json).</summary>
    public string ContentType          { get; init; } = "application/schema+json";

    /// <summary>Optional human-readable description of the schema's purpose.</summary>
    public string? Description         { get; init; }

    /// <summary>When this schema was registered.</summary>
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Whether this schema version is still active.</summary>
    public bool IsActive               { get; set; } = true;
}

// ── ISchemaRegistry ───────────────────────────────────────────────────────────

/// <summary>
/// Stores and retrieves JSON Schema documents by common name.
/// Derived from: Schema Registry (LiteDB) — DSA 0.24 Epoch 0 (PPML).
/// Conditional: Society TDA Only.
/// </summary>
public interface ISchemaRegistry
{
    /// <summary>Registers a new schema. Name must be unique.</summary>
    Task RegisterAsync(SchemaRecord schema, CancellationToken ct = default);

    /// <summary>Returns a schema by its common name, or null if not found.</summary>
    Task<SchemaRecord?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>Returns all active schemas.</summary>
    Task<IReadOnlyList<SchemaRecord>> GetAllActiveAsync(CancellationToken ct = default);

    /// <summary>Deactivates a schema by name. Does not delete — schemas are immutable once registered.</summary>
    Task DeactivateAsync(string name, CancellationToken ct = default);
}

// ── ISchemaResolver ───────────────────────────────────────────────────────────

/// <summary>
/// Resolves a JSON Schema by its common name or DID URL.
/// Derived from: Schema Resolver (Data Access) — DSA 0.24 Epoch 0 (PPML).
/// Conditional: Society TDA Only.
/// </summary>
public interface ISchemaResolver
{
    /// <summary>
    /// Resolves a schema by its common name (e.g., "CitizenEndowmentCredential").
    /// Returns the JSON Schema string, or null if not found.
    /// </summary>
    Task<string?> ResolveByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Resolves a schema by its DID URL
    /// (e.g., "did:drn:alpha.svrn7.net/schemas/schema/CitizenEndowmentCredential").
    /// Parses the name segment and delegates to <see cref="ResolveByNameAsync"/>.
    /// </summary>
    Task<string?> ResolveByDidUrlAsync(string didUrl, CancellationToken ct = default);
}

// ── SchemaLiteContext ─────────────────────────────────────────────────────────

/// <summary>
/// LiteDB context for svrn7-schemas.db.
/// One collection: Schemas (keyed by Name, unique).
/// Derived from: Schema Registry (LiteDB) — DSA 0.24 Epoch 0 (PPML).
/// Conditional: Society TDA Only.
/// </summary>
public sealed class SchemaLiteContext : IDisposable
{
    private readonly LiteDatabase _db;

    public const string ColSchemas = "Schemas";

    public SchemaLiteContext(string dbPath)
    {
        _db = new LiteDatabase(dbPath);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        var col = _db.GetCollection<SchemaRecord>(ColSchemas);
        col.EnsureIndex(s => s.Name, unique: true);
        col.EnsureIndex(s => s.IsActive);
        col.EnsureIndex(s => s.RegisteredAt);
    }

    public ILiteCollection<SchemaRecord> Schemas
        => _db.GetCollection<SchemaRecord>(ColSchemas);

    public void Dispose() => _db.Dispose();
}

// ── LiteSchemaRegistry ────────────────────────────────────────────────────────

/// <summary>
/// <see cref="ISchemaRegistry"/> implementation backed by <see cref="SchemaLiteContext"/>.
/// Derived from: Schema Registry (LiteDB) — DSA 0.24 Epoch 0 (PPML).
/// </summary>
public sealed class LiteSchemaRegistry : ISchemaRegistry
{
    private readonly SchemaLiteContext              _ctx;
    private readonly ILogger<LiteSchemaRegistry>   _log;

    public LiteSchemaRegistry(SchemaLiteContext ctx, ILogger<LiteSchemaRegistry> log)
    {
        _ctx = ctx;
        _log = log;
    }

    public Task RegisterAsync(SchemaRecord schema, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidateName(schema.Name);
        _ctx.Schemas.Insert(schema);
        _log.LogInformation("SchemaRegistry: registered schema '{Name}'.", schema.Name);
        return Task.CompletedTask;
    }

    public Task<SchemaRecord?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var schema = _ctx.Schemas.FindOne(s => s.Name == name && s.IsActive);
        return Task.FromResult<SchemaRecord?>(schema);
    }

    public Task<IReadOnlyList<SchemaRecord>> GetAllActiveAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var schemas = _ctx.Schemas.Find(s => s.IsActive).ToList();
        return Task.FromResult<IReadOnlyList<SchemaRecord>>(schemas);
    }

    public Task DeactivateAsync(string name, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var schema = _ctx.Schemas.FindOne(s => s.Name == name);
        if (schema is null)
        {
            _log.LogWarning("SchemaRegistry: deactivate called for unknown schema '{Name}'.", name);
            return Task.CompletedTask;
        }

        schema.IsActive = false;
        _ctx.Schemas.Update(schema);
        _log.LogInformation("SchemaRegistry: deactivated schema '{Name}'.", name);
        return Task.CompletedTask;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Schema name must not be empty.", nameof(name));
        foreach (var ch in name)
            if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '.')
                throw new ArgumentException(
                    $"Schema name '{name}' contains invalid character '{ch}'. " +
                    "Only alphanumeric characters, hyphens, and dots are permitted.",
                    nameof(name));
    }
}

// ── LiteSchemaResolver ────────────────────────────────────────────────────────

/// <summary>
/// <see cref="ISchemaResolver"/> implementation backed by <see cref="ISchemaRegistry"/>.
/// Derived from: Schema Resolver (Data Access) — DSA 0.24 Epoch 0 (PPML).
/// </summary>
public sealed class LiteSchemaResolver : ISchemaResolver
{
    private readonly ISchemaRegistry _registry;

    public LiteSchemaResolver(ISchemaRegistry registry) => _registry = registry;

    public async Task<string?> ResolveByNameAsync(string name, CancellationToken ct = default)
    {
        var schema = await _registry.GetByNameAsync(name, ct);
        return schema?.SchemaJson;
    }

    public async Task<string?> ResolveByDidUrlAsync(string didUrl, CancellationToken ct = default)
    {
        // Parse: did:drn:{networkId}/schemas/schema/{name}
        // Extract the name segment — the final path component.
        if (string.IsNullOrWhiteSpace(didUrl)) return null;

        var slashIdx = didUrl.LastIndexOf('/');
        if (slashIdx < 0) return null;

        var name = didUrl[(slashIdx + 1)..];
        if (string.IsNullOrWhiteSpace(name)) return null;

        return await ResolveByNameAsync(name, ct);
    }
}
