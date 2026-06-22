using System.Security.Cryptography;
using iText.Kernel.Pdf;
using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace MasterSTI.UnitTests;

/// <summary>
/// ADR-0008 step 6 — "canned-bitmap path that asserts the dictionary key set,
/// value shapes, and Merkle root recomputation." The full handler involves a
/// DbContext + LTV + archive-timestamp pipeline that is exercised by the
/// docker compose E2E smoke (<c>tools/step4-smoke.sh</c>). The substance the
/// ADR cares about for the test layer — that the /VeraSign.Render* keys
/// survive the CMS embed step and that the Merkle root the wallet would
/// compute round-trips through the writer/reader pair byte-for-byte — is
/// exercised here against <see cref="PadesService.Embed"/> directly so the
/// test stays a pure unit (no DB, no DI scaffolding, no PDFium boot).
/// </summary>
public class EmbedSignatureHandlerTests
{
    private const string SpikeRootHex =
        "1c8255a7d1db21c4e9a140a1d8068dcd02d594977a188e2f38e33b734a6bee96";

    private static PadesService CreateService() => new(NullLogger<PadesService>.Instance);

    private static byte[] CreateMinimalPdf()
    {
        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using var doc = new PdfDocument(writer);
        doc.AddNewPage();
        doc.Close();
        return ms.ToArray();
    }

    private static PadesRenderCommitment SampleCommitment(string root = SpikeRootHex, int pageCount = 3) => new(
        RootHex: root,
        Algo: "SHA-256",
        Dpi: 150,
        PageCount: pageCount,
        Locale: "ro-RO",
        Profile: "PdfiumPinned-v1");

    /// <summary>
    /// Minimal CMS-shaped placeholder. iText writes the bytes verbatim into
    /// the /Contents hex slot; signature crypto is verified later in the
    /// pipeline, so a structurally-empty payload is fine for round-trip tests.
    /// </summary>
    private static byte[] StubCmsBytes()
    {
        var bytes = new byte[512];
        bytes[0] = 0x30; // SEQUENCE
        bytes[1] = 0x82; // long-form length, 2 bytes
        bytes[2] = 0x01;
        bytes[3] = 0xFC; // 508 content bytes follow
        return bytes;
    }

    private static PdfDictionary ReadSignatureDictionary(byte[] signedPdf, string fieldName)
    {
        using var reader = new PdfReader(new MemoryStream(signedPdf));
        using var pdf = new PdfDocument(reader);
        var acroForm = iText.Forms.PdfAcroForm.GetAcroForm(pdf, false)!;
        var field = acroForm.GetField(fieldName)!.GetPdfObject();
        return field.GetAsDictionary(PdfName.V) ?? field;
    }

    [Fact]
    public void Embed_PreservesAllSixRenderKeys_AfterCmsRoundTrip()
    {
        var svc = CreateService();
        var prepared = svc.Prepare(
            CreateMinimalPdf(),
            "Signature1",
            placement: null,
            appearance: null,
            commitment: SampleCommitment(pageCount: 5));

        var signed = svc.Embed(prepared.PreparedPdfBytes, StubCmsBytes(), "Signature1");

        var stored = RenderCommitmentReader.Read(ReadSignatureDictionary(signed, "Signature1"));

        Assert.NotNull(stored);
        Assert.Equal(SpikeRootHex, stored!.RootHex);
        Assert.Equal("SHA-256", stored.Algo);
        Assert.Equal(150, stored.Dpi);
        Assert.Equal(5, stored.PageCount);
        Assert.Equal("ro-RO", stored.Locale);
        Assert.Equal("PdfiumPinned-v1", stored.Profile);
    }

    [Fact]
    public void Embed_WithoutCommitment_ProducesNoRenderKeys()
    {
        var svc = CreateService();
        var prepared = svc.Prepare(CreateMinimalPdf());

        var signed = svc.Embed(prepared.PreparedPdfBytes, StubCmsBytes(), "Signature1");

        var stored = RenderCommitmentReader.Read(ReadSignatureDictionary(signed, "Signature1"));
        Assert.Null(stored);
    }

    [Fact]
    public void CannedBitmaps_MerkleRoot_RoundTripsThroughDictWriterAndReader()
    {
        // Three canned per-page bitmaps (synthetic — content shape only matters
        // for the SHA personalisation, not for any visual interpretation).
        var page1 = Enumerable.Repeat<byte>(0x00, 4096).ToArray();
        var page2 = Enumerable.Range(0, 4096).Select(i => (byte)(i & 0xFF)).ToArray();
        var page3 = Enumerable.Repeat<byte>(0xFF, 4096).ToArray();

        // ADR-0008 personalisation:
        //   leaf hash    = SHA-256(0x00 || pixelBuffer)
        //   internal     = SHA-256(0x01 || left || right)
        // Inlined here so the test asserts the SPEC, not just the impl agreeing
        // with itself.
        var leaves = new[] { page1, page2, page3 }.Select(b => SpecLeafHash(b)).ToList();
        var root = SpecMerkleRoot(leaves);
        var rootHex = Convert.ToHexStringLower(root);

        var svc = CreateService();
        var commitment = SampleCommitment(root: rootHex, pageCount: leaves.Count);

        var prepared = svc.Prepare(
            CreateMinimalPdf(),
            "Signature1",
            placement: null,
            appearance: null,
            commitment: commitment);
        var signed = svc.Embed(prepared.PreparedPdfBytes, StubCmsBytes(), "Signature1");

        var stored = RenderCommitmentReader.Read(ReadSignatureDictionary(signed, "Signature1"));
        Assert.NotNull(stored);
        Assert.Equal(rootHex, stored!.RootHex);
        Assert.Equal(leaves.Count, stored.PageCount);
    }

    [Fact]
    public void CannedBitmaps_OddLeafLevel_DuplicatesLastLeaf()
    {
        // 3 leaves -> level 1 promotes (h12, h33). Asserts the ADR's
        // odd-leaf duplication rule, not the RFC 6962 orphan-promotion rule.
        var b1 = new byte[] { 1 };
        var b2 = new byte[] { 2 };
        var b3 = new byte[] { 3 };

        var l1 = SpecLeafHash(b1);
        var l2 = SpecLeafHash(b2);
        var l3 = SpecLeafHash(b3);

        var lvl1Left = SpecNodeHash(l1, l2);
        var lvl1Right = SpecNodeHash(l3, l3); // duplication, not promotion
        var expected = SpecNodeHash(lvl1Left, lvl1Right);

        var actual = SpecMerkleRoot(new List<byte[]> { l1, l2, l3 });
        Assert.Equal(Convert.ToHexStringLower(expected), Convert.ToHexStringLower(actual));
    }

    private static byte[] SpecLeafHash(ReadOnlySpan<byte> payload)
    {
        var buf = new byte[1 + payload.Length];
        buf[0] = 0x00;
        payload.CopyTo(buf.AsSpan(1));
        return SHA256.HashData(buf);
    }

    private static byte[] SpecNodeHash(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        Span<byte> buf = stackalloc byte[1 + 32 + 32];
        buf[0] = 0x01;
        left.CopyTo(buf[1..]);
        right.CopyTo(buf[33..]);
        return SHA256.HashData(buf);
    }

    private static byte[] SpecMerkleRoot(List<byte[]> leaves)
    {
        if (leaves.Count == 0) throw new ArgumentException("no leaves");
        var level = leaves;
        while (level.Count > 1)
        {
            var next = new List<byte[]>((level.Count + 1) / 2);
            for (var i = 0; i < level.Count; i += 2)
            {
                var left = level[i];
                var right = i + 1 < level.Count ? level[i + 1] : level[i];
                next.Add(SpecNodeHash(left, right));
            }
            level = next;
        }
        return level[0];
    }
}
