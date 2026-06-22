namespace MasterSTI.Api.Common.Trust;

/// <summary>
/// One Trust Service Provider record as surfaced by an EU Trust List (ETSI TS 119 612).
/// <see cref="SubjectMatchers"/> are case-insensitive substrings checked against a signer
/// certificate's Issuer DN (the issuing QTSP). A match counts as a trust-list hit.
/// </summary>
public sealed record TrustListEntry(
    string Country,
    string TspName,
    string ServiceTypeIdentifier,
    IReadOnlyList<string> SubjectMatchers);

/// <summary>
/// Snapshot of a curated EU Trust List slice — what was loaded, when, and from where.
/// </summary>
public sealed record TrustListSnapshot(
    string Source,
    DateTime SnapshotTakenAt,
    string Scheme,
    IReadOnlyList<TrustListEntry> Tsps);

/// <summary>Result of matching a certificate Issuer DN against the trust list.</summary>
public sealed record TrustListMatchResult(
    bool IsTrusted,
    string? TspName,
    string? Country,
    string? ServiceTypeIdentifier);
