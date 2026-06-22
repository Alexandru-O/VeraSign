namespace MasterSTI.Api.Common.Wysiwys;

public interface IPageManifestService
{
    /// <summary>
    /// Computes a deterministic per-page manifest from a PDF byte buffer. Returns
    /// <c>null</c> when the PDF cannot be opened.
    /// </summary>
    PageManifest? Compute(byte[] pdfBytes);

    /// <summary>Compares two manifests entry-by-entry. Null inputs are treated as empty.</summary>
    PageManifestComparison Compare(PageManifest? stored, PageManifest? current);
}
