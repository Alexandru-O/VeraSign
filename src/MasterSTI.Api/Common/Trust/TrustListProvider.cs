using System.Text.Json;

namespace MasterSTI.Api.Common.Trust;

/// <summary>
/// Loads a curated EU Trust List slice from JSON at construction. Bundled file
/// <c>Common/Trust/trust-list.json</c> is copied to the output directory by the
/// csproj — see <c>MasterSTI.Api.csproj</c>. Production validators would ingest
/// the full ETSI TS 119 612 LOTL XML from https://ec.europa.eu/tools/lotl/eu-lotl.xml
/// and walk the per-country TSL pointers; this prototype trades freshness for
/// auditability and ships a reviewable JSON of well-known QTSPs.
/// </summary>
public sealed class TrustListProvider : ITrustListProvider
{
    private readonly TrustListSnapshot _snapshot;
    private readonly ILogger<TrustListProvider> _logger;

    public TrustListProvider(ILogger<TrustListProvider> logger)
    {
        _logger = logger;
        _snapshot = LoadOrEmpty();
    }

    public TrustListSnapshot Snapshot => _snapshot;

    public TrustListMatchResult Match(string? issuerDn)
    {
        if (string.IsNullOrWhiteSpace(issuerDn))
            return new TrustListMatchResult(false, null, null, null);

        foreach (var tsp in _snapshot.Tsps)
        {
            foreach (var matcher in tsp.SubjectMatchers)
            {
                if (issuerDn.Contains(matcher, StringComparison.OrdinalIgnoreCase))
                    return new TrustListMatchResult(true, tsp.TspName, tsp.Country, tsp.ServiceTypeIdentifier);
            }
        }

        return new TrustListMatchResult(false, null, null, null);
    }

    private TrustListSnapshot LoadOrEmpty()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Common", "Trust", "trust-list.json");
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("Trust list not found at {Path} — running with empty snapshot", path);
                return EmptySnapshot();
            }

            var json = File.ReadAllText(path);
            var snapshot = JsonSerializer.Deserialize<TrustListSnapshot>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (snapshot is null || snapshot.Tsps.Count == 0)
            {
                _logger.LogWarning("Trust list JSON parsed to empty snapshot");
                return EmptySnapshot();
            }

            _logger.LogInformation("EU Trust List loaded: {Count} TSPs, snapshot {Snapshot:u}, source {Source}",
                snapshot.Tsps.Count, snapshot.SnapshotTakenAt, snapshot.Source);
            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load trust list from {Path} — running with empty snapshot", path);
            return EmptySnapshot();
        }
    }

    private static TrustListSnapshot EmptySnapshot() => new(
        Source: "n/a",
        SnapshotTakenAt: DateTime.MinValue,
        Scheme: "empty",
        Tsps: Array.Empty<TrustListEntry>());
}
