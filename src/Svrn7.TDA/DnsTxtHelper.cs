using DnsClient;
using DnsClient.Protocol;

namespace Svrn7.TDA;

/// <summary>
/// DNS TXT record lookup utility. Used for drn.directory federation endpoint discovery
/// and any other DNS TXT queries in the Web 7.0 Pando architecture.
/// </summary>
internal static class DnsTxtHelper
{
    private static readonly LookupClient _client = new();

    /// <summary>
    /// Returns all TXT record strings for the given hostname or URL.
    /// If <paramref name="query"/> is a full URL, the host portion is extracted automatically.
    /// Returns an empty list when the name does not exist or has no TXT records.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetTxtRecordsAsync(
        string query, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (Uri.TryCreate(query, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            query = uri.Host;

        var result = await _client.QueryAsync(query, QueryType.TXT, cancellationToken: ct)
                                  .ConfigureAwait(false);

        return result.Answers
                     .OfType<TxtRecord>()
                     .SelectMany(r => r.Text)
                     .Where(s => !string.IsNullOrEmpty(s))
                     .ToList();
    }
}
