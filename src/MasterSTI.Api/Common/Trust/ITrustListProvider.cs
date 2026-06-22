namespace MasterSTI.Api.Common.Trust;

public interface ITrustListProvider
{
    /// <summary>Currently loaded snapshot, or an empty snapshot when ingestion failed.</summary>
    TrustListSnapshot Snapshot { get; }

    /// <summary>
    /// Matches an X.509 Issuer DN string against the snapshot. Returns
    /// <c>IsTrusted=false</c> with empty fields when no match.
    /// </summary>
    TrustListMatchResult Match(string? issuerDn);
}
