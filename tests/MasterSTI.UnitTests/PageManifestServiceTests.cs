using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using MasterSTI.Api.Common.Wysiwys;
using Microsoft.Extensions.Logging.Abstractions;

namespace MasterSTI.UnitTests;

/// <summary>
/// Locks in the page-content manifest behaviour: deterministic per-PDF, sensitive to per-page
/// content changes, and stable across recomputation. Backs the "page-level integrity manifest"
/// feature that catches the shadow-attack class (PAdES byte-range intact, page content mutated
/// via incremental update).
/// </summary>
public class PageManifestServiceTests
{
    private static PageManifestService NewService() =>
        new(NullLogger<PageManifestService>.Instance);

    [Fact]
    public void Compute_TwoPagePdf_ProducesTwoEntries()
    {
        var pdf = BuildPdf("Page A", "Page B");
        var svc = NewService();

        var manifest = svc.Compute(pdf);

        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.PageCount);
        Assert.Equal(2, manifest.Entries.Count);
        Assert.Equal(1, manifest.Entries[0].PageNumber);
        Assert.Equal(2, manifest.Entries[1].PageNumber);
        Assert.NotEmpty(manifest.OverallSha256Hex);
        Assert.Equal(PageManifestService.CurrentVersion, manifest.Version);
        Assert.Equal(PageManifestService.Algorithm, manifest.Algorithm);
    }

    [Fact]
    public void Compute_IsDeterministic_ForIdenticalBytes()
    {
        var pdf = BuildPdf("hello world", "another page");
        var svc = NewService();

        var m1 = svc.Compute(pdf);
        var m2 = svc.Compute(pdf);

        Assert.NotNull(m1);
        Assert.NotNull(m2);
        Assert.Equal(m1!.OverallSha256Hex, m2!.OverallSha256Hex);
        for (var i = 0; i < m1.Entries.Count; i++)
            Assert.Equal(m1.Entries[i].Sha256Hex, m2.Entries[i].Sha256Hex);
    }

    [Fact]
    public void Compare_DivergentPage_ListsMismatch()
    {
        var pristine = BuildPdf("Original page 1", "Original page 2");
        var tampered = BuildPdf("Original page 1", "TAMPERED page 2");
        var svc = NewService();

        var stored = svc.Compute(pristine);
        var current = svc.Compute(tampered);

        var cmp = svc.Compare(stored, current);

        Assert.False(cmp.Matches);
        Assert.Equal(2, cmp.StoredPageCount);
        Assert.Equal(2, cmp.CurrentPageCount);
        Assert.Single(cmp.MismatchedPages);
        Assert.Equal(2, cmp.MismatchedPages[0]);
    }

    [Fact]
    public void Compare_DifferentPageCount_FlagsSurplusPages()
    {
        var twoPage = BuildPdf("A", "B");
        var threePage = BuildPdf("A", "B", "C");
        var svc = NewService();

        var stored = svc.Compute(twoPage);
        var current = svc.Compute(threePage);

        var cmp = svc.Compare(stored, current);

        Assert.False(cmp.Matches);
        Assert.Equal(2, cmp.StoredPageCount);
        Assert.Equal(3, cmp.CurrentPageCount);
        Assert.Contains(3, cmp.MismatchedPages);
    }

    [Fact]
    public void Compare_IdenticalManifest_Matches()
    {
        var pdf = BuildPdf("p1", "p2");
        var svc = NewService();

        var m = svc.Compute(pdf);

        var cmp = svc.Compare(m, m);

        Assert.True(cmp.Matches);
        Assert.Empty(cmp.MismatchedPages);
        Assert.Equal(m!.OverallSha256Hex, cmp.StoredOverallSha256);
    }

    [Fact]
    public void Compute_EmptyInput_ReturnsNull()
    {
        var svc = NewService();
        Assert.Null(svc.Compute(Array.Empty<byte>()));
        Assert.Null(svc.Compute(null!));
    }

    [Fact]
    public void Compare_NullStored_DoesNotMatch()
    {
        var pdf = BuildPdf("only-page");
        var svc = NewService();
        var current = svc.Compute(pdf);

        var cmp = svc.Compare(null, current);

        Assert.False(cmp.Matches);
        Assert.Equal(0, cmp.StoredPageCount);
        Assert.Equal(1, cmp.CurrentPageCount);
    }

    private static byte[] BuildPdf(params string[] pages)
    {
        using var ms = new MemoryStream();
        using (var writer = new PdfWriter(ms))
        using (var pdfDoc = new PdfDocument(writer))
        using (var doc = new Document(pdfDoc))
        {
            for (var i = 0; i < pages.Length; i++)
            {
                if (i > 0)
                    doc.Add(new AreaBreak(iText.Layout.Properties.AreaBreakType.NEXT_PAGE));
                doc.Add(new Paragraph(pages[i]));
            }
        }
        return ms.ToArray();
    }
}
