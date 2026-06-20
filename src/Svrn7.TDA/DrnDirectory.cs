namespace Svrn7.TDA;

/// <summary>
/// Resolves Federation TDA DIDComm service endpoints via the drn.directory DNS zone.
/// This is the only use of DNS in the Web 7.0 Pando architecture.
/// Spec: draft-herman-did-w3c-drn-00 Section 5b.
/// </summary>
public static class DrnDirectory
{
    /// <summary>
    /// Returns the DIDComm service endpoint URL for a Federation TDA by querying the
    /// drn.directory DNS zone. Returns <c>null</c> when no TXT record is found.
    ///
    /// Accepts any of the following input forms:
    ///   did:drn:federation.svrn7.net/agent/1.0/{key}   (full Federation DID)
    ///   federation.svrn7.net                            (DID method-specific id)
    ///   svrn7.net                                       (bare domain)
    /// </summary>
    public static async Task<string?> GetFederationEndpointAsync(
        string federationDidOrDomain, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(federationDidOrDomain);

        var label  = BuildQueryLabel(federationDidOrDomain);
        var record = await DnsTxtHelper.GetTxtRecordsAsync(label, ct).ConfigureAwait(false);
        return record.FirstOrDefault();
    }

    /// <summary>
    /// Builds the drn.directory DNS query label from a Federation DID or domain.
    /// Result form: federation.{domain}.drn.directory
    /// </summary>
    public static string BuildQueryLabel(string input)
    {
        // Strip did:drn: prefix
        if (input.StartsWith("did:drn:", StringComparison.OrdinalIgnoreCase))
            input = input["did:drn:".Length..];

        // Strip path (e.g. /agent/1.0/{key})
        var slashIdx = input.IndexOf('/');
        if (slashIdx >= 0)
            input = input[..slashIdx];

        // Ensure the label starts with "federation."
        if (!input.StartsWith("federation.", StringComparison.OrdinalIgnoreCase))
            input = $"federation.{input}";

        return $"{input}.drn.directory";
    }
}
