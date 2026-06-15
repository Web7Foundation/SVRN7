using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;

namespace Svrn7.Identity;

/// <summary>
/// Wraps IDidDocumentRegistry with OpenTelemetry Activity tracing and structured
/// Debug logging for all DID Document lifecycle operations. Used by the
/// Get-DIDDocument LOBE cmdlet and any C# caller that needs traced DID resolution,
/// creation, update, or deactivation.
/// </summary>
public sealed class DIDDocumentService
{
    public static readonly ActivitySource ActivitySource =
        new("Svrn7.Identity.DIDDocument", "0.8.0");

    private readonly IDidDocumentRegistry      _registry;
    private readonly ILogger<DIDDocumentService> _log;

    public DIDDocumentService(IDidDocumentRegistry registry, ILogger<DIDDocumentService> log)
    {
        _registry = registry;
        _log      = log;
    }

    /// <summary>
    /// Resolves a DID Document by DID. Emits a DIDDocument.Resolve activity span
    /// with found, version, and W3C error code tags. Logs document contents at Debug.
    /// </summary>
    public async Task<DidResolutionResult> ResolveAsync(string did, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("DIDDocument.Resolve");
        activity?.SetTag("did", did);

        var result = await _registry.ResolveAsync(did, ct);

        activity?.SetTag("found", result.Found);
        if (result.Document is not null)
            activity?.SetTag("did.version", result.Document.Version);
        if (result.ErrorCode is not null)
            activity?.SetTag("error.code", result.ErrorCode);

        if (_log.IsEnabled(LogLevel.Debug) && result.Document is not null)
            _log.LogDebug("DID Document retrieved: {Content}", FormatForLog(result.Document));

        return result;
    }

    /// <summary>
    /// Creates a new DID Document and emits a DIDDocument.Create activity span
    /// with DID, method, and role tags. Logs document contents at Debug.
    /// </summary>
    public async Task CreateAsync(DidDocument document, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("DIDDocument.Create");
        activity?.SetTag("did", document.Did);
        activity?.SetTag("did.method", document.MethodName);
        activity?.SetTag("did.role", document.Role?.ToString() ?? "none");
        activity?.SetTag("did.version", document.Version);

        await _registry.CreateAsync(document, ct);

        if (_log.IsEnabled(LogLevel.Debug))
            _log.LogDebug("DID Document created: {Content}", FormatForLog(document));
    }

    /// <summary>
    /// Updates an existing DID Document (version must be current+1) and emits a
    /// DIDDocument.Update activity span with DID and new version tags.
    /// </summary>
    public async Task UpdateAsync(DidDocument document, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("DIDDocument.Update");
        activity?.SetTag("did", document.Did);
        activity?.SetTag("did.version.new", document.Version);

        await _registry.UpdateAsync(document, ct);
    }

    /// <summary>
    /// Permanently deactivates a DID Document and emits a DIDDocument.Deactivate
    /// activity span. Deactivation is irreversible.
    /// </summary>
    public async Task DeactivateAsync(string did, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("DIDDocument.Deactivate");
        activity?.SetTag("did", did);

        await _registry.DeactivateAsync(did, ct);
    }

    /// <summary>
    /// Retrieves the full version history for a DID and emits a DIDDocument.GetHistory
    /// activity span with the total version count.
    /// </summary>
    public async Task<IReadOnlyList<DidDocument>> GetHistoryAsync(
        string did, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("DIDDocument.GetHistory");
        activity?.SetTag("did", did);

        var history = await _registry.GetHistoryAsync(did, ct);

        activity?.SetTag("version.count", history.Count);
        return history;
    }

    /// <summary>
    /// Retrieves a specific version snapshot of a DID Document and emits a
    /// DIDDocument.ResolveVersion activity span.
    /// </summary>
    public async Task<DidDocument?> ResolveVersionAsync(
        string did, int version, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("DIDDocument.ResolveVersion");
        activity?.SetTag("did", did);
        activity?.SetTag("did.version", version);

        var doc = await _registry.ResolveVersionAsync(did, version, ct);

        activity?.SetTag("found", doc is not null);
        return doc;
    }

    /// <summary>
    /// Returns a one-line diagnostic summary of a DidDocument for log output.
    /// Null-safe — returns "(not found)" when the document is null.
    /// </summary>
    public static string Summarize(DidDocument? doc) =>
        doc is null
            ? "(not found)"
            : $"DID={doc.Did} Version={doc.Version} Status={doc.Status} Role={doc.Role} " +
              $"Keys={doc.VerificationMethod.Count} Services={doc.ServiceEndpoints.Count} " +
              $"UpdatedAt={doc.UpdatedAt:O}";

    private static string FormatForLog(DidDocument doc)
    {
        var summary = Summarize(doc);
        try
        {
            var pretty = System.Text.Json.JsonSerializer.Serialize(
                System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(doc.DocumentJson),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            return $"{summary}\n{pretty}";
        }
        catch { return summary; }
    }
}
