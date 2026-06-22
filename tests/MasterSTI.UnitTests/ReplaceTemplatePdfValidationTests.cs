using System.Text;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Templates;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Templates.ReplacePdf;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MasterSTI.UnitTests;

/// <summary>
/// Issue-#61 ingest-path validator surface for <c>POST /api/templates/{id}/replace-pdf</c>.
/// Shares the same .pdf extension / 50 MB / %PDF- magic-byte invariants as the upload
/// path -- the dissertation's value claim that template-replace cannot be used to plant
/// arbitrary content depends on these three checks being enforced.
/// </summary>
public class ReplaceTemplatePdfValidationTests : IDisposable
{
    private const long MaxFileSize = 50L * 1024 * 1024;

    private readonly ReplaceTemplatePdfValidator _validator = new();
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly TemplateStoragePaths _paths;
    private readonly string _tempDir;

    public ReplaceTemplatePdfValidationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ReplacePdfValidationTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);

        _tempDir = Path.Combine(Path.GetTempPath(), $"replace_pdf_validation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _env = Substitute.For<IWebHostEnvironment>();
        _env.ContentRootPath.Returns(_tempDir);
        _env.WebRootPath.Returns(_tempDir);
        _paths = new TemplateStoragePaths(_env);
    }

    // ---- Validator tier (extension + size) -----------------------------------------------

    [Fact]
    public void Validator_AcceptsPdfExtension()
    {
        var cmd = new ReplaceTemplatePdfCommand(Guid.NewGuid(), MakeFile("template.pdf", length: 1024));

        var result = _validator.Validate(cmd);

        Assert.True(result.IsValid, FlattenErrors(result));
    }

    [Fact]
    public void Validator_AcceptsUppercasePdfExtension()
    {
        var cmd = new ReplaceTemplatePdfCommand(Guid.NewGuid(), MakeFile("TEMPLATE.PDF", length: 1024));

        var result = _validator.Validate(cmd);

        Assert.True(result.IsValid, FlattenErrors(result));
    }

    [Theory]
    [InlineData("template.docx")]
    [InlineData("template.txt")]
    [InlineData("template")]
    [InlineData("template.pdf.exe")]
    public void Validator_RejectsNonPdfExtension(string fileName)
    {
        var cmd = new ReplaceTemplatePdfCommand(Guid.NewGuid(), MakeFile(fileName, length: 1024));

        var result = _validator.Validate(cmd);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("FileName", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AcceptsExactly50MB()
    {
        var cmd = new ReplaceTemplatePdfCommand(Guid.NewGuid(), MakeFile("template.pdf", length: MaxFileSize));

        var result = _validator.Validate(cmd);

        Assert.True(result.IsValid, FlattenErrors(result));
    }

    [Fact]
    public void Validator_RejectsOver50MB()
    {
        var cmd = new ReplaceTemplatePdfCommand(Guid.NewGuid(), MakeFile("template.pdf", length: MaxFileSize + 1));

        var result = _validator.Validate(cmd);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Length", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsZeroLength()
    {
        var cmd = new ReplaceTemplatePdfCommand(Guid.NewGuid(), MakeFile("template.pdf", length: 0));

        var result = _validator.Validate(cmd);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Length", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsEmptyGuid()
    {
        var cmd = new ReplaceTemplatePdfCommand(Guid.Empty, MakeFile("template.pdf", length: 1024));

        var result = _validator.Validate(cmd);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Id", StringComparison.Ordinal));
    }

    // ---- Handler tier (%PDF- magic bytes) ------------------------------------------------

    [Fact]
    public async Task Handler_AcceptsPayloadWithPdfMagicBytes()
    {
        var orgId = Guid.NewGuid();
        var templateId = await SeedTemplateAsync(orgId);

        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n%dummy\n");
        var cmd = new ReplaceTemplatePdfCommand(templateId, MakeFile("template.pdf", bytes));

        var handler = CreateHandler(orgId);
        var dto = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal(templateId, dto.Id);
        var refreshed = await _db.Templates.FirstAsync(t => t.Id == templateId);
        Assert.NotNull(refreshed.PdfPath);
        Assert.True(File.Exists(refreshed.PdfPath));
    }

    [Fact]
    public async Task Handler_RejectsPayloadWithoutPdfMagicBytes()
    {
        var orgId = Guid.NewGuid();
        var templateId = await SeedTemplateAsync(orgId);

        // PNG header masquerading as a .pdf.
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var cmd = new ReplaceTemplatePdfCommand(templateId, MakeFile("template.pdf", bytes));

        var handler = CreateHandler(orgId);

        await Assert.ThrowsAsync<InvalidTemplatePdfException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handler_RejectsPayloadShorterThanMagicBytes()
    {
        var orgId = Guid.NewGuid();
        var templateId = await SeedTemplateAsync(orgId);

        var bytes = new byte[] { 0x25, 0x50, 0x44 };
        var cmd = new ReplaceTemplatePdfCommand(templateId, MakeFile("template.pdf", bytes));

        var handler = CreateHandler(orgId);

        await Assert.ThrowsAsync<InvalidTemplatePdfException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    // ---- Helpers --------------------------------------------------------------------------

    private async Task<Guid> SeedTemplateAsync(Guid orgId)
    {
        var templateId = Guid.NewGuid();
        _db.Templates.Add(new Template
        {
            Id = templateId,
            OrganizationId = orgId,
            Title = "Test Template",
            Category = TemplateCategory.Custom,
            DefaultLevel = "AdES",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        return templateId;
    }

    private ReplaceTemplatePdfHandler CreateHandler(Guid orgId)
    {
        var user = Substitute.For<ICurrentUserAccessor>();
        user.OrganizationId.Returns(orgId);
        return new ReplaceTemplatePdfHandler(
            _db,
            user,
            _paths,
            NullLogger<ReplaceTemplatePdfHandler>.Instance);
    }

    private static IFormFile MakeFile(string fileName, long length)
    {
        var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D });
        return new FormFile(stream, baseStreamOffset: 0, length: length, name: "file", fileName: fileName);
    }

    private static IFormFile MakeFile(string fileName, byte[] contents)
    {
        var stream = new MemoryStream(contents);
        return new FormFile(stream, baseStreamOffset: 0, length: contents.Length, name: "file", fileName: fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private static string FlattenErrors(FluentValidation.Results.ValidationResult r) =>
        string.Join("; ", r.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
