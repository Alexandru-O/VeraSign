using MasterSTI.Api.Features.Signing.Prepare;

namespace MasterSTI.UnitTests;

public class PrepareSigningValidatorTests
{
    // 64 lowercase hex chars (one of the actual roots from the
    // 2026-05-26 spike harness run on PdfiumPinned-v1).
    private const string ValidRoot = "1c8255a7d1db21c4e9a140a1d8068dcd02d594977a188e2f38e33b734a6bee96";

    private readonly PrepareSigningValidator _sut = new();

    [Fact]
    public void Passes_when_no_render_commitment_supplied()
    {
        var cmd = BaseCommand() with { RenderCommitment = new RenderCommitmentInputs(null, null, null, null, null, null) };

        var result = _sut.Validate(cmd);

        Assert.True(result.IsValid, FlattenErrors(result));
    }

    [Fact]
    public void Passes_when_render_commitment_null()
    {
        var cmd = BaseCommand() with { RenderCommitment = null };

        var result = _sut.Validate(cmd);

        Assert.True(result.IsValid, FlattenErrors(result));
    }

    [Fact]
    public void Passes_when_full_v1_conformant_commitment_supplied()
    {
        var cmd = WithCommitment(new RenderCommitmentInputs(
            RenderRootHex: ValidRoot,
            RenderAlgo: "SHA-256",
            RenderDpi: 150,
            RenderPageCount: 3,
            RenderLocale: "ro-RO",
            RenderProfile: "PdfiumPinned-v1"));

        var result = _sut.Validate(cmd);

        Assert.True(result.IsValid, FlattenErrors(result));
    }

    [Theory]
    [InlineData("SHA-512")]
    [InlineData("sha-256")]
    [InlineData("MD5")]
    public void Fails_when_algo_not_frozen_value(string algo)
    {
        var cmd = WithCommitment(ValidCommitmentWith(algo: algo));
        Assert.Contains(_sut.Validate(cmd).Errors, e => e.PropertyName.EndsWith("RenderAlgo"));
    }

    [Theory]
    [InlineData(96)]
    [InlineData(151)]
    [InlineData(300)]
    public void Fails_when_dpi_not_150(int dpi)
    {
        var cmd = WithCommitment(ValidCommitmentWith(dpi: dpi));
        Assert.Contains(_sut.Validate(cmd).Errors, e => e.PropertyName.EndsWith("RenderDpi"));
    }

    [Theory]
    [InlineData("PdfiumPinned-v2")]
    [InlineData("pdfiumpinned-v1")]
    [InlineData("")]
    public void Fails_when_profile_not_frozen_value(string profile)
    {
        var cmd = WithCommitment(ValidCommitmentWith(profile: profile));
        Assert.Contains(_sut.Validate(cmd).Errors, e => e.PropertyName.EndsWith("RenderProfile"));
    }

    [Theory]
    [InlineData("ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890")]
    [InlineData("notahash")]
    [InlineData("1c8255a7d1db21c4e9a140a1d8068dcd02d594977a188e2f38e33b734a6bee9")]
    [InlineData("1c8255a7d1db21c4e9a140a1d8068dcd02d594977a188e2f38e33b734a6bee96Z")]
    public void Fails_when_root_not_lowercase_sha256(string root)
    {
        var cmd = WithCommitment(ValidCommitmentWith(root: root));
        Assert.Contains(_sut.Validate(cmd).Errors, e => e.PropertyName.EndsWith("RenderRootHex"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Fails_when_page_count_not_positive(int pageCount)
    {
        var cmd = WithCommitment(ValidCommitmentWith(pageCount: pageCount));
        Assert.Contains(_sut.Validate(cmd).Errors, e => e.PropertyName.EndsWith("RenderPageCount"));
    }

    [Theory]
    [InlineData("english")]
    [InlineData("RO_RO")]
    [InlineData(" ro-RO ")]
    [InlineData("RO-ro")]
    public void Fails_when_locale_not_bcp47_plausible(string locale)
    {
        var cmd = WithCommitment(ValidCommitmentWith(locale: locale));
        Assert.Contains(_sut.Validate(cmd).Errors, e => e.PropertyName.EndsWith("RenderLocale"));
    }

    [Fact]
    public void Fails_when_root_null_but_other_render_fields_present()
    {
        var cmd = WithCommitment(new RenderCommitmentInputs(
            RenderRootHex: null,
            RenderAlgo: "SHA-256",
            RenderDpi: 150,
            RenderPageCount: 1,
            RenderLocale: "ro-RO",
            RenderProfile: "PdfiumPinned-v1"));

        var errors = _sut.Validate(cmd).Errors;
        Assert.Contains(errors, e => e.PropertyName.EndsWith("RenderRootHex") &&
                                     e.ErrorMessage.Contains("required when any other Render", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ro")]
    [InlineData("ro-RO")]
    [InlineData("zh-Hant-TW")]
    public void Accepts_common_bcp47_locale_shapes(string locale)
    {
        var cmd = WithCommitment(ValidCommitmentWith(locale: locale));
        var result = _sut.Validate(cmd);
        Assert.True(result.IsValid, FlattenErrors(result));
    }

    private static PrepareSigningCommand BaseCommand() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "tester", "mock-credential-001");

    private static PrepareSigningCommand WithCommitment(RenderCommitmentInputs rc) =>
        BaseCommand() with { RenderCommitment = rc };

    private static RenderCommitmentInputs ValidCommitmentWith(
        string? root = null,
        string? algo = null,
        int? dpi = null,
        int? pageCount = null,
        string? locale = null,
        string? profile = null) =>
        new(
            RenderRootHex: root ?? ValidRoot,
            RenderAlgo: algo ?? "SHA-256",
            RenderDpi: dpi ?? 150,
            RenderPageCount: pageCount ?? 3,
            RenderLocale: locale ?? "ro-RO",
            RenderProfile: profile ?? "PdfiumPinned-v1");

    private static string FlattenErrors(FluentValidation.Results.ValidationResult r) =>
        string.Join("; ", r.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
}
