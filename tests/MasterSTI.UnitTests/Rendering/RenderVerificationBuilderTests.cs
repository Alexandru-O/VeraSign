using MasterSTI.Api.Common.Rendering;
using MasterSTI.Api.Features.SignedDocuments.Validate;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MasterSTI.UnitTests.Rendering;

/// <summary>
/// ADR-0008 step 4 — verifier orchestration. The reader half is covered by
/// <see cref="RenderCommitmentReaderTests"/>; these tests prove the
/// Verified/Disputed/NotPresent state machine reacts correctly to every
/// branch of the IReferenceRenderer contract.
/// </summary>
public class RenderVerificationBuilderTests
{
    private const string SupportedProfile = "PdfiumPinned-v1";
    private const string StoredRoot = "1c8255a7d1db21c4e9a140a1d8068dcd02d594977a188e2f38e33b734a6bee96";
    private const string VerifierBinarySha = "8f67fac92554e4a6ab57f7d4f6a3d6974b1646373e0d314d90694738941c040c";

    private static StoredRenderCommitment Sample(string root = StoredRoot, string profile = SupportedProfile) =>
        new(RootHex: root, Algo: "SHA-256", Dpi: 150, PageCount: 3, Locale: "ro-RO", Profile: profile);

    private static IReferenceRenderer AvailableRenderer(string recomputedRoot)
    {
        var r = Substitute.For<IReferenceRenderer>();
        r.IsAvailable.Returns(true);
        r.PinnedBinarySha256.Returns(VerifierBinarySha);
        r.RecomputeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RenderCommitmentResult(
                Profile: SupportedProfile, Algo: "SHA-256", Dpi: 150,
                PageCount: 3, Locale: "ro-RO", RootHex: recomputedRoot,
                PageLeafHashesHex: Array.Empty<string>()));
        return r;
    }

    private static async Task<RenderVerificationReport> Build(
        StoredRenderCommitment? stored, IReferenceRenderer renderer) =>
        (await RenderVerificationBuilder.BuildAsync(
            stored, pdfBytes: new byte[] { 0x25, 0x50, 0x44, 0x46 },
            renderer, SupportedProfile, Guid.NewGuid(),
            NullLogger.Instance, CancellationToken.None)).Report;

    [Fact]
    public async Task NoStoredCommitment_ReturnsNotPresent()
    {
        var r = AvailableRenderer(recomputedRoot: StoredRoot);
        var v = await Build(stored: null, r);

        Assert.Equal(RenderVerificationStatus.NotPresent, v.Status);
        Assert.Null(v.StoredRootHex);
        Assert.Equal("no /VeraSign.RenderRoot key", v.Reason);
        Assert.Equal(VerifierBinarySha, v.VerifierPdfiumBinarySha256);
        await r.DidNotReceive().RecomputeAsync(
            Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnsupportedProfile_ShortCircuits_AsNotPresent_WithoutRecompute()
    {
        var r = AvailableRenderer(recomputedRoot: StoredRoot);
        var v = await Build(Sample(profile: "PdfiumPinned-v9999"), r);

        Assert.Equal(RenderVerificationStatus.NotPresent, v.Status);
        Assert.Equal("PdfiumPinned-v9999", v.Profile);
        Assert.Contains("PdfiumPinned-v9999", v.Reason);
        await r.DidNotReceive().RecomputeAsync(
            Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifierUnavailable_ShortCircuits_AsNotPresent_WithoutRecompute()
    {
        var r = Substitute.For<IReferenceRenderer>();
        r.IsAvailable.Returns(false);
        r.UnavailableReason.Returns("libpdfium.so missing");
        r.PinnedBinarySha256.Returns((string?)null);

        var v = await Build(Sample(), r);

        Assert.Equal(RenderVerificationStatus.NotPresent, v.Status);
        Assert.Contains("libpdfium.so missing", v.Reason);
        await r.DidNotReceive().RecomputeAsync(
            Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MatchingRoots_ReportsVerified()
    {
        var r = AvailableRenderer(recomputedRoot: StoredRoot);
        var v = await Build(Sample(), r);

        Assert.Equal(RenderVerificationStatus.Verified, v.Status);
        Assert.Equal(StoredRoot, v.StoredRootHex);
        Assert.Equal(StoredRoot, v.RecomputedRootHex);
        Assert.Null(v.Reason);
        Assert.Empty(v.DisputedPages);
    }

    [Fact]
    public async Task DivergingRoots_ReportsDisputed_WithEmptyPageList()
    {
        const string divergentRoot = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";
        var r = AvailableRenderer(recomputedRoot: divergentRoot);
        var v = await Build(Sample(), r);

        Assert.Equal(RenderVerificationStatus.Disputed, v.Status);
        Assert.Equal(StoredRoot, v.StoredRootHex);
        Assert.Equal(divergentRoot, v.RecomputedRootHex);
        Assert.Equal("Merkle root divergence", v.Reason);
        // v1 does not store per-page leaves — disputed pages stay empty by design.
        Assert.Empty(v.DisputedPages);
    }

    [Fact]
    public async Task RecomputeThrows_DegradesTo_NotPresent_WithReason()
    {
        var r = Substitute.For<IReferenceRenderer>();
        r.IsAvailable.Returns(true);
        r.PinnedBinarySha256.Returns(VerifierBinarySha);
        r.RecomputeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<RenderCommitmentResult>>(_ => throw new InvalidOperationException("FPDF_LoadDocument failed"));

        var v = await Build(Sample(), r);

        Assert.Equal(RenderVerificationStatus.NotPresent, v.Status);
        Assert.Contains("FPDF_LoadDocument failed", v.Reason);
        Assert.Null(v.RecomputedRootHex);
    }
}
