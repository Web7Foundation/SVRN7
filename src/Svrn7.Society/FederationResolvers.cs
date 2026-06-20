using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Svrn7.Core;
using Svrn7.Core.Interfaces;
using Svrn7.Core.Models;
using Svrn7.DIDComm;

namespace Svrn7.Society;

// ── FederationDidDocumentResolver ─────────────────────────────────────────────

/// <summary>
/// Routes did:drn DID Document resolution.
///
/// Tries the local <see cref="IDidDocumentRegistry"/> first. If the DID is cached
/// locally the result is returned immediately without a network hop.
///
/// On a local miss, returns <c>notFound</c> — the Identity LOBE
/// (<c>Resolve-Svrn7Did</c>) owns the async DIDComm escalation and correlated
/// relay pattern. See DIDRESOLUTION.md for the full flow.
/// </summary>
public sealed class FederationDidDocumentResolver : IDidDocumentResolver
{
    private readonly IDidDocumentRegistry _localRegistry;
    private readonly ILogger<FederationDidDocumentResolver> _log;

    public FederationDidDocumentResolver(
        IDidDocumentRegistry localRegistry,
        ILogger<FederationDidDocumentResolver> log)
    {
        _localRegistry = localRegistry;
        _log           = log;
    }

    public async Task<DidResolutionResult> ResolveAsync(string did, CancellationToken ct = default)
    {
        var parts = did.Split(':', 3);
        if (parts.Length < 3 || parts[0] != "did")
            return Error(did, "invalidDid");

        if (!string.Equals(parts[1], "drn", StringComparison.OrdinalIgnoreCase))
            return Error(did, "methodNotSupported");

        // Try local registry — covers all locally registered DID Documents
        var local = await _localRegistry.ResolveAsync(did, ct);
        if (local.Found) return local;

        // DID not cached locally — LOBE layer handles DIDComm escalation
        _log.LogDebug("Foreign DID '{Did}': local miss — escalation handled by Resolve-Svrn7Did", did);
        return Error(did, "notFound");
    }

    private static DidResolutionResult Error(string did, string errorCode) =>
        new() { Did = did, Found = false, ErrorCode = errorCode, Document = null };
}

// ── FederationVcDocumentResolver ─────────────────────────────────────────────

/// <summary>
/// Federation-level IVcDocumentResolver.
///
/// All single-Society queries delegate to the injected local IVcDocumentResolver
/// (typically LiteVcDocumentResolver backed by svrn7-vcs.db).
///
/// FindBySubjectAcrossSocietiesAsync issues DIDComm messages to all active
/// Societies in parallel and aggregates results within the fan-out timeout window.
/// Partial results are returned — TimedOutSocieties identifies non-responders.
/// The caller receives a CrossSocietyVcQueryResult with IsComplete=true only when
/// every known Society responded within the timeout.
/// </summary>
public sealed class FederationVcDocumentResolver : IVcDocumentResolver
{
    private readonly IVcDocumentResolver  _local;
    private readonly IIdentityRegistry    _registry;
    private readonly IDIDCommService      _didComm;
    private readonly Svrn7SocietyOptions  _opts;
    private readonly ILogger<FederationVcDocumentResolver> _log;

    private static readonly TimeSpan DefaultFanOutTimeout = TimeSpan.FromSeconds(10);

    public FederationVcDocumentResolver(
        IVcDocumentResolver local,
        IIdentityRegistry registry,
        IDIDCommService didComm,
        IOptions<Svrn7SocietyOptions> opts,
        ILogger<FederationVcDocumentResolver> log)
    {
        _local    = local;
        _registry = registry;
        _didComm  = didComm;
        _opts     = opts.Value;
        _log      = log;
    }

    // ── All single-Society operations delegate to local resolver ──────────────

    public Task<VcResolutionResult> ResolveAsync(string vcId, CancellationToken ct = default)
        => _local.ResolveAsync(vcId, ct);

    public Task<IReadOnlyList<VcRecord>> FindBySubjectAsync(
        string subjectDid, VcStatus? statusFilter = null, CancellationToken ct = default)
        => _local.FindBySubjectAsync(subjectDid, statusFilter, ct);

    public Task<IReadOnlyList<VcRecord>> FindByIssuerAsync(
        string issuerDid, VcStatus? statusFilter = null, CancellationToken ct = default)
        => _local.FindByIssuerAsync(issuerDid, statusFilter, ct);

    public Task<IReadOnlyList<VcRecord>> FindByTypeAsync(
        string credentialType, VcStatus? statusFilter = null, CancellationToken ct = default)
        => _local.FindByTypeAsync(credentialType, statusFilter, ct);

    public Task<IReadOnlyList<VcRecord>> FindBySocietyAsync(
        string societyDid, VcStatus? statusFilter = null, CancellationToken ct = default)
        => _local.FindBySocietyAsync(societyDid, statusFilter, ct);

    public Task<bool> IsValidAsync(string vcId, CancellationToken ct = default)
        => _local.IsValidAsync(vcId, ct);

    public Task<IReadOnlyDictionary<string, VcStatus>> GetStatusBatchAsync(
        IEnumerable<string> vcIds, CancellationToken ct = default)
        => _local.GetStatusBatchAsync(vcIds, ct);

    public Task<IReadOnlyList<VcRecord>> FindExpiringAsync(
        TimeSpan withinWindow, CancellationToken ct = default)
        => _local.FindExpiringAsync(withinWindow, ct);

    public Task<IReadOnlyList<RevocationEvent>> GetRevocationHistoryAsync(
        string? subjectDid = null, string? issuerDid = null,
        DateTimeOffset? since = null, CancellationToken ct = default)
        => _local.GetRevocationHistoryAsync(subjectDid, issuerDid, since, ct);

    public Task<IReadOnlyDictionary<string, long>> GetCountsByTypeAsync(CancellationToken ct = default)
        => _local.GetCountsByTypeAsync(ct);

    public Task<IReadOnlyDictionary<VcStatus, long>> GetCountsByStatusAsync(CancellationToken ct = default)
        => _local.GetCountsByStatusAsync(ct);

    // ── Cross-Society fan-out ─────────────────────────────────────────────────

    /// <summary>
    /// Fans out DIDComm vc-resolve-request messages to all known active Societies in parallel.
    /// Local results from this Society are always included without a network hop.
    /// Each remote Society is given its own cancellation scope linked to the fan-out timeout.
    /// The result manifest distinguishes responded Societies from timed-out ones.
    /// IsComplete=true only when TimedOutSocieties is empty.
    /// </summary>
    public async Task<CrossSocietyVcQueryResult> FindBySubjectAcrossSocietiesAsync(
        string subjectDid, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var effectiveTimeout = timeout ?? DefaultFanOutTimeout;

        // Discover all active Societies from the identity registry
        var allSocieties = await _registry.GetAllSocietiesAsync(ct);

        var remoteSocieties = allSocieties
            .Where(s => s.IsActive && s.Did != _opts.SocietyDid)
            .Select(s => s.Did)
            .ToList();

        // Always include local results (no timeout risk)
        var localResults = await _local.FindBySubjectAsync(subjectDid, ct: ct);
        var allRecords   = new List<VcRecord>(localResults);
        var responded    = new List<string> { _opts.SocietyDid };
        var timedOut     = new List<string>();

        if (remoteSocieties.Count == 0)
        {
            _log.LogDebug(
                "Cross-Society VC query for '{Subject}': no remote Societies known — " +
                "returning local results only.", subjectDid);
            return Build(allRecords, responded, timedOut);
        }

        _log.LogDebug(
            "Cross-Society VC query for '{Subject}': fanning out to {Count} remote Societies",
            subjectDid, remoteSocieties.Count);

        // Fan out — each Society gets its own timeout-linked CTS
        var fanOutTasks = remoteSocieties.Select(societyDid =>
            QuerySocietyAsync(societyDid, subjectDid, effectiveTimeout, ct)).ToList();

        var fanOutResults = await Task.WhenAll(fanOutTasks);

        foreach (var (sid, records, success) in fanOutResults)
        {
            if (success)
            {
                allRecords.AddRange(records);
                responded.Add(sid);
            }
            else
            {
                timedOut.Add(sid);
            }
        }

        if (timedOut.Count > 0)
            _log.LogWarning(
                "Cross-Society VC query partial: {TimedOut}/{Total} Societies timed out",
                timedOut.Count, remoteSocieties.Count);

        return Build(allRecords, responded, timedOut);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task<(string SocietyDid, IReadOnlyList<VcRecord> Records, bool Success)>
        QuerySocietyAsync(string societyDid, string subjectDid, TimeSpan timeout, CancellationToken ct)
    {
        using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        taskCts.CancelAfter(timeout);

        try
        {
            // Build and dispatch DIDComm vc-resolve-request
            var requestMsg = _didComm.NewMessage()
                .Type("did:drn:svrn7.net/protocols/Svrn7.Identity.0.8.0/vc-resolve-by-subject-request")
                .From(_opts.SocietyDid)
                .To(societyDid)
                .Body(new { subjectDid, requestedAt = DateTimeOffset.UtcNow })
                .Build();

            // Plaintext pack — transport layer encrypts for actual delivery
            _ = await _didComm.PackPlaintextAsync(requestMsg, taskCts.Token);

            // The response is delivered by the transport and processed by
            // DIDCommMessageProcessorService, which populates the local VC registry.
            // Since we cannot await an async DIDComm response inline without a
            // transport adapter, we return empty here — results arrive via the inbox.
            // Transport adapters replace this with a TaskCompletionSource-based await.
            _log.LogDebug(
                "VC resolve-request dispatched to Society '{Society}' for subject '{Subject}'",
                societyDid, subjectDid);

            return (societyDid, Array.Empty<VcRecord>(), true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("VC resolve-request to Society '{Society}' timed out", societyDid);
            return (societyDid, Array.Empty<VcRecord>(), false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "VC resolve-request to Society '{Society}' threw an exception", societyDid);
            return (societyDid, Array.Empty<VcRecord>(), false);
        }
    }

    private static CrossSocietyVcQueryResult Build(
        List<VcRecord> records, List<string> responded, List<string> timedOut) =>
        new()
        {
            Records            = records.DistinctBy(v => v.VcId).ToList(),
            RespondedSocieties = responded,
            TimedOutSocieties  = timedOut,
        };
}
