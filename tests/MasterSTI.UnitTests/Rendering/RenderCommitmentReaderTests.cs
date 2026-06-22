using iText.Kernel.Pdf;
using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace MasterSTI.UnitTests.Rendering;

/// <summary>
/// Reader half of the Pixel-Bound QES round trip (ADR-0008 step 4). The
/// writer side is covered by PadesServiceTests; these tests start from the
/// signature dictionary PadesService actually produces and prove that
/// RenderCommitmentReader pulls the same six values back out byte-for-byte.
/// </summary>
public class RenderCommitmentReaderTests
{
    private const string SpikeRootHex =
        "1c8255a7d1db21c4e9a140a1d8068dcd02d594977a188e2f38e33b734a6bee96";

    private static byte[] MinimalPdf()
    {
        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using var doc = new PdfDocument(writer);
        doc.AddNewPage();
        doc.Close();
        return ms.ToArray();
    }

    private static PdfDictionary ReadSignatureDictionary(byte[] preparedPdf, string fieldName)
    {
        using var reader = new PdfReader(new MemoryStream(preparedPdf));
        using var pdf = new PdfDocument(reader);
        var acroForm = iText.Forms.PdfAcroForm.GetAcroForm(pdf, false)!;
        var field = acroForm.GetField(fieldName)!.GetPdfObject();
        return field.GetAsDictionary(PdfName.V) ?? field;
    }

    private static PadesRenderCommitment SampleCommitment(int pageCount = 3) => new(
        RootHex: SpikeRootHex,
        Algo: "SHA-256",
        Dpi: 150,
        PageCount: pageCount,
        Locale: "ro-RO",
        Profile: "PdfiumPinned-v1");

    private static byte[] PrepareWithCommitment(PadesRenderCommitment? commitment)
    {
        var svc = new PadesService(NullLogger<PadesService>.Instance);
        return svc.Prepare(MinimalPdf(), "Signature1", placement: null, appearance: null, commitment: commitment)
                  .PreparedPdfBytes;
    }

    [Fact]
    public void Read_RoundTrips_AllSixValues_FromPadesServiceOutput()
    {
        var bytes = PrepareWithCommitment(SampleCommitment(pageCount: 7));
        var sigDict = ReadSignatureDictionary(bytes, "Signature1");

        var stored = RenderCommitmentReader.Read(sigDict);

        Assert.NotNull(stored);
        Assert.Equal(SpikeRootHex, stored!.RootHex);
        Assert.Equal("SHA-256", stored.Algo);
        Assert.Equal(150, stored.Dpi);
        Assert.Equal(7, stored.PageCount);
        Assert.Equal("ro-RO", stored.Locale);
        Assert.Equal("PdfiumPinned-v1", stored.Profile);
    }

    [Fact]
    public void Read_ReturnsNull_WhenNoRenderRootKey()
    {
        var bytes = PrepareWithCommitment(commitment: null);
        var sigDict = ReadSignatureDictionary(bytes, "Signature1");

        Assert.Null(RenderCommitmentReader.Read(sigDict));
    }

    [Fact]
    public void Read_ReturnsNull_OnNullDictionary()
    {
        Assert.Null(RenderCommitmentReader.Read(null));
    }

    [Fact]
    public void Read_ReturnsNull_WhenRootBytesAreWrongLength()
    {
        // Hand-craft a dictionary with a non-32-byte root. v1 schema is
        // frozen at SHA-256 so anything else is treated as malformed.
        var sigDict = new PdfDictionary();
        sigDict.Put(new PdfName("VeraSign.RenderRoot"),
            new PdfString(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).SetHexWriting(true));
        sigDict.Put(new PdfName("VeraSign.RenderAlgo"), new PdfName("SHA-256"));
        sigDict.Put(new PdfName("VeraSign.RenderDpi"), new PdfNumber(150));
        sigDict.Put(new PdfName("VeraSign.RenderPageCount"), new PdfNumber(3));
        sigDict.Put(new PdfName("VeraSign.RenderLocale"), new PdfString("ro-RO"));
        sigDict.Put(new PdfName("VeraSign.RenderProfile"), new PdfName("PdfiumPinned-v1"));

        Assert.Null(RenderCommitmentReader.Read(sigDict));
    }

    [Fact]
    public void Read_ReturnsNull_WhenAlgoMissing_PartialShape()
    {
        var sigDict = new PdfDictionary();
        var rootBytes = Convert.FromHexString(SpikeRootHex);
        sigDict.Put(new PdfName("VeraSign.RenderRoot"),
            new PdfString(rootBytes).SetHexWriting(true));
        // Deliberately omit Algo.
        sigDict.Put(new PdfName("VeraSign.RenderDpi"), new PdfNumber(150));
        sigDict.Put(new PdfName("VeraSign.RenderPageCount"), new PdfNumber(3));
        sigDict.Put(new PdfName("VeraSign.RenderLocale"), new PdfString("ro-RO"));
        sigDict.Put(new PdfName("VeraSign.RenderProfile"), new PdfName("PdfiumPinned-v1"));

        Assert.Null(RenderCommitmentReader.Read(sigDict));
    }
}
