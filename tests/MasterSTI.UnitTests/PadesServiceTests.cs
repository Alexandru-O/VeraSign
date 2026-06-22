using iText.Kernel.Pdf;
using MasterSTI.Api.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace MasterSTI.UnitTests;

public class PadesServiceTests
{
    private static PadesService CreateService() =>
        new PadesService(NullLogger<PadesService>.Instance);

    private static byte[] CreateMinimalPdf()
    {
        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using var doc = new PdfDocument(writer);
        doc.AddNewPage();
        doc.Close();
        return ms.ToArray();
    }

    [Fact]
    public void Prepare_ReturnsNonEmptyBytes()
    {
        var svc = CreateService();
        var result = svc.Prepare(CreateMinimalPdf());
        Assert.NotEmpty(result.PreparedPdfBytes);
        Assert.Equal("Signature1", result.SignatureFieldName);
    }

    [Fact]
    public void Prepare_ByteRangeHashIsValidHex()
    {
        var svc = CreateService();
        var result = svc.Prepare(CreateMinimalPdf());
        Assert.Equal(64, result.ByteRangeHashHex.Length);
        Assert.All(result.ByteRangeHashHex, c => Assert.Contains(c, "0123456789abcdef"));
    }

    [Fact]
    public void Prepare_ByteRangeHasFourElements()
    {
        var svc = CreateService();
        var result = svc.Prepare(CreateMinimalPdf());
        Assert.Equal(4, result.ByteRange.Length);
        Assert.All(result.ByteRange, v => Assert.True(v >= 0));
    }

    [Fact]
    public void Prepare_PreparedPdfContainsSignatureField()
    {
        var svc = CreateService();
        var result = svc.Prepare(CreateMinimalPdf());

        using var reader = new PdfReader(new MemoryStream(result.PreparedPdfBytes));
        using var pdfDoc = new PdfDocument(reader);
        var acroForm = iText.Forms.PdfAcroForm.GetAcroForm(pdfDoc, false);

        Assert.NotNull(acroForm);
        Assert.NotNull(acroForm!.GetField("Signature1"));
    }

    [Fact]
    public void Prepare_HashCorrespondsToByteRange()
    {
        var svc = CreateService();
        var result = svc.Prepare(CreateMinimalPdf());

        var bytes = result.PreparedPdfBytes;
        var br = result.ByteRange;

        using var ms = new MemoryStream();
        ms.Write(bytes, (int)br[0], (int)br[1]);
        ms.Write(bytes, (int)br[2], (int)br[3]);
        var recomputed = HashingService.ComputeSha256(ms.ToArray());

        Assert.Equal(result.ByteRangeHashHex, recomputed);
    }

    [Fact]
    public void Prepare_PreparedPdfLargerThanOriginal()
    {
        var svc = CreateService();
        var pdf = CreateMinimalPdf();
        var result = svc.Prepare(pdf);
        Assert.True(result.PreparedPdfBytes.Length > pdf.Length);
    }

    // ----- ADR-0008 Pixel-Bound QES dictionary writer -----

    private const string SpikeRootHex = "1c8255a7d1db21c4e9a140a1d8068dcd02d594977a188e2f38e33b734a6bee96";

    private static PadesRenderCommitment SampleCommitment(int pageCount = 3) => new(
        RootHex: SpikeRootHex,
        Algo: "SHA-256",
        Dpi: 150,
        PageCount: pageCount,
        Locale: "ro-RO",
        Profile: "PdfiumPinned-v1");

    private static PdfDictionary ReadSignatureDictionary(byte[] preparedPdf, string fieldName)
    {
        using var reader = new PdfReader(new MemoryStream(preparedPdf));
        using var pdf = new PdfDocument(reader);
        var acroForm = iText.Forms.PdfAcroForm.GetAcroForm(pdf, false)!;
        var field = acroForm.GetField(fieldName)!.GetPdfObject();
        return field.GetAsDictionary(PdfName.V) ?? field;
    }

    [Fact]
    public void Prepare_WithCommitment_WritesAllSixRenderKeys()
    {
        var svc = CreateService();
        var result = svc.Prepare(
            CreateMinimalPdf(),
            "Signature1",
            placement: null,
            appearance: null,
            commitment: SampleCommitment());

        var sigDict = ReadSignatureDictionary(result.PreparedPdfBytes, "Signature1");

        Assert.NotNull(sigDict.GetAsString(new PdfName("VeraSign.RenderRoot")));
        Assert.NotNull(sigDict.GetAsName(new PdfName("VeraSign.RenderAlgo")));
        Assert.NotNull(sigDict.GetAsNumber(new PdfName("VeraSign.RenderDpi")));
        Assert.NotNull(sigDict.GetAsNumber(new PdfName("VeraSign.RenderPageCount")));
        Assert.NotNull(sigDict.GetAsString(new PdfName("VeraSign.RenderLocale")));
        Assert.NotNull(sigDict.GetAsName(new PdfName("VeraSign.RenderProfile")));
    }

    [Fact]
    public void Prepare_WithCommitment_RootRoundTripsAsRawHashBytes()
    {
        var svc = CreateService();
        var result = svc.Prepare(
            CreateMinimalPdf(),
            "Signature1",
            placement: null,
            appearance: null,
            commitment: SampleCommitment());

        var sigDict = ReadSignatureDictionary(result.PreparedPdfBytes, "Signature1");
        var rootStr = sigDict.GetAsString(new PdfName("VeraSign.RenderRoot"));
        var rawBytes = rootStr.GetValueBytes();

        Assert.Equal(32, rawBytes.Length);
        Assert.Equal(SpikeRootHex, Convert.ToHexStringLower(rawBytes));
    }

    [Fact]
    public void Prepare_WithCommitment_StampsFrozenV1Values()
    {
        var svc = CreateService();
        var commitment = SampleCommitment(pageCount: 7);

        var result = svc.Prepare(
            CreateMinimalPdf(),
            "Signature1",
            placement: null,
            appearance: null,
            commitment: commitment);

        var sigDict = ReadSignatureDictionary(result.PreparedPdfBytes, "Signature1");

        Assert.Equal("SHA-256", sigDict.GetAsName(new PdfName("VeraSign.RenderAlgo")).GetValue());
        Assert.Equal(150, sigDict.GetAsNumber(new PdfName("VeraSign.RenderDpi")).IntValue());
        Assert.Equal(7, sigDict.GetAsNumber(new PdfName("VeraSign.RenderPageCount")).IntValue());
        Assert.Equal("ro-RO", sigDict.GetAsString(new PdfName("VeraSign.RenderLocale")).GetValue());
        Assert.Equal("PdfiumPinned-v1", sigDict.GetAsName(new PdfName("VeraSign.RenderProfile")).GetValue());
    }

    [Fact]
    public void Prepare_WithoutCommitment_WritesNoRenderKeys()
    {
        var svc = CreateService();
        var result = svc.Prepare(CreateMinimalPdf());

        var sigDict = ReadSignatureDictionary(result.PreparedPdfBytes, "Signature1");

        Assert.Null(sigDict.GetAsString(new PdfName("VeraSign.RenderRoot")));
        Assert.Null(sigDict.GetAsName(new PdfName("VeraSign.RenderAlgo")));
        Assert.Null(sigDict.GetAsNumber(new PdfName("VeraSign.RenderDpi")));
        Assert.Null(sigDict.GetAsNumber(new PdfName("VeraSign.RenderPageCount")));
        Assert.Null(sigDict.GetAsString(new PdfName("VeraSign.RenderLocale")));
        Assert.Null(sigDict.GetAsName(new PdfName("VeraSign.RenderProfile")));
    }

    [Fact]
    public void Prepare_WithCommitment_KeysAreInsideSignedByteRange()
    {
        // The whole point of writing the keys via ModifySigningDictionary is
        // that they participate in the /ByteRange. This test asserts that
        // the dictionary key NAMES appear in the byte range PdfSigner
        // computed -- a key written AFTER the placeholder would land in the
        // "skip" gap and never be hashed, defeating the commitment.
        var svc = CreateService();
        var result = svc.Prepare(
            CreateMinimalPdf(),
            "Signature1",
            placement: null,
            appearance: null,
            commitment: SampleCommitment());

        var bytes = result.PreparedPdfBytes;
        var br = result.ByteRange;

        using var ms = new MemoryStream();
        ms.Write(bytes, (int)br[0], (int)br[1]);
        ms.Write(bytes, (int)br[2], (int)br[3]);
        var hashedRegion = ms.ToArray();
        var asAscii = System.Text.Encoding.ASCII.GetString(hashedRegion);

        Assert.Contains("VeraSign.RenderRoot", asAscii, StringComparison.Ordinal);
        Assert.Contains("VeraSign.RenderProfile", asAscii, StringComparison.Ordinal);
    }
}
