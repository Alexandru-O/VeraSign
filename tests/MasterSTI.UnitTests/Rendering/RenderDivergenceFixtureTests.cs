using System.Text.Json;
using iText.Kernel.Pdf;
using MasterSTI.Api.Common.Rendering;
using MasterSTI.Api.Features.SignedDocuments.Validate;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MasterSTI.UnitTests.Rendering;

/// <summary>
/// ADR-0008 step 6 — corpus-level smoke over tests/Fixtures/RenderDivergence.
/// The real Verified-vs-Disputed proof is the docker compose path in
/// tools/step4-smoke.sh (linux-x64 PdfiumPinned-v1 binary). These tests pin
/// the corpus contract that downstream work depends on:
///
///   - the three attack-class PDFs are present and parse
///   - each ships a sibling .expected.json with the documented schema
///   - the verifier seam (RenderVerificationBuilder + IReferenceRenderer)
///     wires through the corpus the same way it would for a real signed PDF
///
/// CI hosts without the pinned binary still run every test here; the real
/// PDFium recompute is exercised by the docker E2E and is intentionally not
/// duplicated as a Windows-side unit.
/// </summary>
public class RenderDivergenceFixtureTests
{
    private static readonly string CorpusDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "RenderDivergence");

    private static readonly string[] ExpectedFixtures =
    {
        "01-romanian-diacritics.pdf",
        "02-ocg-hidden-amount.pdf",
        "03-transparent-overlay.pdf",
    };

    private static readonly string[] AllowedAttackClasses =
    {
        "font-fallback",
        "OCG-toggle",
        "transparent-overlay",
    };

    public sealed record ExpectedFixture(
        string Fixture,
        string AttackClass,
        int PageCount,
        string Locale,
        string Summary,
        IReadOnlyList<DisputeScenario> DisputeScenarios,
        IReadOnlyList<VerifiedScenario> VerifiedScenarios,
        string Notes);

    public sealed record DisputeScenario(
        string Mutation,
        IReadOnlyList<int> DisputedPages,
        string ExpectedResult);

    public sealed record VerifiedScenario(
        string? Mutation,
        string ExpectedResult);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static ExpectedFixture LoadExpected(string pdfName)
    {
        var jsonPath = Path.Combine(
            CorpusDir,
            Path.GetFileNameWithoutExtension(pdfName) + ".expected.json");
        Assert.True(File.Exists(jsonPath), $"missing sidecar: {jsonPath}");
        var json = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<ExpectedFixture>(json, JsonOpts)!;
    }

    [Fact]
    public void Corpus_DirectoryExists_WithAllThreeAttackClasses()
    {
        Assert.True(Directory.Exists(CorpusDir),
            $"corpus directory missing — did the csproj <None Include> copy step run? Looked for {CorpusDir}");
        foreach (var fx in ExpectedFixtures)
            Assert.True(File.Exists(Path.Combine(CorpusDir, fx)), $"fixture missing: {fx}");
    }

    [Theory]
    [InlineData("01-romanian-diacritics.pdf")]
    [InlineData("02-ocg-hidden-amount.pdf")]
    [InlineData("03-transparent-overlay.pdf")]
    public void Fixture_ParsesAsPdf_AndPageCountMatchesExpected(string pdfName)
    {
        var expected = LoadExpected(pdfName);
        var pdfPath = Path.Combine(CorpusDir, pdfName);

        using var reader = new PdfReader(pdfPath);
        using var pdf = new PdfDocument(reader);

        Assert.Equal(expected.PageCount, pdf.GetNumberOfPages());
    }

    [Theory]
    [InlineData("01-romanian-diacritics.pdf")]
    [InlineData("02-ocg-hidden-amount.pdf")]
    [InlineData("03-transparent-overlay.pdf")]
    public void ExpectedJson_ShapeConformsToCorpusSchema(string pdfName)
    {
        var expected = LoadExpected(pdfName);

        Assert.Equal(pdfName, expected.Fixture);
        Assert.Contains(expected.AttackClass, AllowedAttackClasses);
        Assert.True(expected.PageCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(expected.Locale));
        Assert.False(string.IsNullOrWhiteSpace(expected.Summary));
        Assert.NotEmpty(expected.DisputeScenarios);
        foreach (var scenario in expected.DisputeScenarios)
        {
            Assert.False(string.IsNullOrWhiteSpace(scenario.Mutation));
            Assert.NotEmpty(scenario.DisputedPages);
            Assert.All(scenario.DisputedPages, p =>
                Assert.InRange(p, 1, expected.PageCount));
        }
        Assert.NotEmpty(expected.VerifiedScenarios);
    }

    [Theory]
    [InlineData("01-romanian-diacritics.pdf")]
    [InlineData("02-ocg-hidden-amount.pdf")]
    [InlineData("03-transparent-overlay.pdf")]
    public async Task Fixture_VerifierSeam_ReportsVerified_WhenStoredRootMatchesRecompute(string pdfName)
    {
        // Wire the same builder the validation handler uses, but inject a
        // canned IReferenceRenderer so the test runs without booting PDFium
        // (Windows dev box has no linux-x64 libpdfium.so). We assert that
        // when the stored root matches the recomputed root, the builder
        // emits Verified — corpus-level proof the seam composes correctly
        // for every attack-class fixture.
        var pdfBytes = await File.ReadAllBytesAsync(Path.Combine(CorpusDir, pdfName));
        var expected = LoadExpected(pdfName);
        const string fakeRoot = "1c8255a7d1db21c4e9a140a1d8068dcd02d594977a188e2f38e33b734a6bee96";

        var stored = new StoredRenderCommitment(
            RootHex: fakeRoot, Algo: "SHA-256", Dpi: 150,
            PageCount: expected.PageCount, Locale: expected.Locale,
            Profile: "PdfiumPinned-v1");

        var renderer = Substitute.For<IReferenceRenderer>();
        renderer.IsAvailable.Returns(true);
        renderer.PinnedBinarySha256.Returns("test-pinned-sha");
        renderer.RecomputeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RenderCommitmentResult(
                Profile: "PdfiumPinned-v1", Algo: "SHA-256", Dpi: 150,
                PageCount: expected.PageCount, Locale: expected.Locale,
                RootHex: fakeRoot, PageLeafHashesHex: Array.Empty<string>()));

        var report = (await RenderVerificationBuilder.BuildAsync(
            stored, pdfBytes, renderer,
            supportedProfile: "PdfiumPinned-v1",
            signedDocId: Guid.NewGuid(),
            logger: NullLogger.Instance,
            cancellationToken: CancellationToken.None)).Report;

        Assert.Equal(RenderVerificationStatus.Verified, report.Status);
    }

    [Theory]
    [InlineData("01-romanian-diacritics.pdf")]
    [InlineData("02-ocg-hidden-amount.pdf")]
    [InlineData("03-transparent-overlay.pdf")]
    public async Task Fixture_VerifierSeam_ReportsDisputed_AndNamesNoPages_WhenRootDiverges(string pdfName)
    {
        // Pixel-Bound QES v1 commits only the root — per-page leaves are not
        // stored in the dict (ADR-0008 §"Alternatives considered"). The
        // .expected.json names disputed pages for the dissertation eval
        // narrative; the verifier itself MUST emit an empty DisputedPages
        // array on root divergence, because it has no leaves to attribute
        // the divergence to. Both rules are corpus contracts.
        var pdfBytes = await File.ReadAllBytesAsync(Path.Combine(CorpusDir, pdfName));
        var expected = LoadExpected(pdfName);
        const string committedRoot = "1c8255a7d1db21c4e9a140a1d8068dcd02d594977a188e2f38e33b734a6bee96";
        const string divergentRoot = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";

        var stored = new StoredRenderCommitment(
            RootHex: committedRoot, Algo: "SHA-256", Dpi: 150,
            PageCount: expected.PageCount, Locale: expected.Locale,
            Profile: "PdfiumPinned-v1");

        var renderer = Substitute.For<IReferenceRenderer>();
        renderer.IsAvailable.Returns(true);
        renderer.PinnedBinarySha256.Returns("test-pinned-sha");
        renderer.RecomputeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RenderCommitmentResult(
                Profile: "PdfiumPinned-v1", Algo: "SHA-256", Dpi: 150,
                PageCount: expected.PageCount, Locale: expected.Locale,
                RootHex: divergentRoot, PageLeafHashesHex: Array.Empty<string>()));

        var report = (await RenderVerificationBuilder.BuildAsync(
            stored, pdfBytes, renderer,
            supportedProfile: "PdfiumPinned-v1",
            signedDocId: Guid.NewGuid(),
            logger: NullLogger.Instance,
            cancellationToken: CancellationToken.None)).Report;

        Assert.Equal(RenderVerificationStatus.Disputed, report.Status);
        Assert.Equal("Merkle root divergence", report.Reason);
        Assert.Empty(report.DisputedPages);
    }
}
