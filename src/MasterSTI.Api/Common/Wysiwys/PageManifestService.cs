using System.Security.Cryptography;
using System.Text;
using iText.Kernel.Pdf;

namespace MasterSTI.Api.Common.Wysiwys;

/// <summary>
/// Hashes each page's content streams (concatenated in document order) and an overall digest
/// over the per-page hashes. v1 covers only PDF drawing instructions — enough to defeat the
/// Shadow Attack class where attackers append an incremental update that visually rewrites a
/// page without invalidating the PAdES byte-range hash. A future v2 would add raster hashes
/// captured at the signing client, defending font-substitution-only render divergence.
/// </summary>
public sealed class PageManifestService : IPageManifestService
{
    public const string CurrentVersion = "v1-content-streams";
    public const string Algorithm = "SHA-256";

    private readonly ILogger<PageManifestService> _logger;

    public PageManifestService(ILogger<PageManifestService> logger)
    {
        _logger = logger;
    }

    public PageManifest? Compute(byte[] pdfBytes)
    {
        if (pdfBytes is null || pdfBytes.Length == 0)
            return null;

        try
        {
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var pdfDoc = new PdfDocument(reader);

            var pageCount = pdfDoc.GetNumberOfPages();
            var entries = new List<PageManifestEntry>(pageCount);

            for (var i = 1; i <= pageCount; i++)
            {
                var page = pdfDoc.GetPage(i);
                var streamCount = page.GetContentStreamCount();

                using var pageBuf = new MemoryStream();
                for (var s = 0; s < streamCount; s++)
                {
                    var stream = page.GetContentStream(s);
                    var bytes = stream?.GetBytes();
                    if (bytes is { Length: > 0 })
                        pageBuf.Write(bytes, 0, bytes.Length);
                }

                var hash = SHA256.HashData(pageBuf.ToArray());
                entries.Add(new PageManifestEntry(i, Convert.ToHexString(hash).ToLowerInvariant()));
            }

            var overall = ComputeOverall(entries);
            return new PageManifest(CurrentVersion, Algorithm, pageCount, entries, overall);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Page manifest computation failed; returning null");
            return null;
        }
    }

    public PageManifestComparison Compare(PageManifest? stored, PageManifest? current)
    {
        var storedEntries = stored?.Entries ?? Array.Empty<PageManifestEntry>();
        var currentEntries = current?.Entries ?? Array.Empty<PageManifestEntry>();

        var mismatches = new List<int>();
        var checkUntil = Math.Min(storedEntries.Count, currentEntries.Count);
        for (var i = 0; i < checkUntil; i++)
        {
            if (!string.Equals(storedEntries[i].Sha256Hex, currentEntries[i].Sha256Hex, StringComparison.OrdinalIgnoreCase))
                mismatches.Add(storedEntries[i].PageNumber);
        }

        // Surplus / missing pages count as mismatches at their page numbers.
        if (storedEntries.Count != currentEntries.Count)
        {
            for (var i = checkUntil; i < Math.Max(storedEntries.Count, currentEntries.Count); i++)
                mismatches.Add(i + 1);
        }

        var matches = mismatches.Count == 0
                      && storedEntries.Count == currentEntries.Count
                      && storedEntries.Count > 0;

        return new PageManifestComparison(
            matches,
            storedEntries.Count,
            currentEntries.Count,
            mismatches,
            stored?.OverallSha256Hex,
            current?.OverallSha256Hex);
    }

    private static string ComputeOverall(IReadOnlyList<PageManifestEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
            sb.Append(e.PageNumber).Append(':').Append(e.Sha256Hex).Append('|');
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
