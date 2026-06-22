namespace MasterSTI.Api.Common.Wysiwys;

/// <summary>
/// Per-page content-stream fingerprint, stored alongside a signed document. Captured from the
/// prepared PDF at embed time; re-computed at validate time. Detects post-signature visual
/// tampering via incremental updates (shadow-attack class — Mainka et al., USENIX 2020) that
/// some PAdES validators miss when <c>SignatureCoversWholeDocument</c> still passes.
/// </summary>
public sealed record PageManifestEntry(int PageNumber, string Sha256Hex);

public sealed record PageManifest(
    string Version,
    string Algorithm,
    int PageCount,
    IReadOnlyList<PageManifestEntry> Entries,
    string OverallSha256Hex);

public sealed record PageManifestComparison(
    bool Matches,
    int StoredPageCount,
    int CurrentPageCount,
    IReadOnlyList<int> MismatchedPages,
    string? StoredOverallSha256,
    string? CurrentOverallSha256);
