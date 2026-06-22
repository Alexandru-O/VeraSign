using System.Text;
using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Documents.Upload;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MasterSTI.UnitTests;

/// <summary>
/// Issue-#61 ingest-path validator surface for <c>POST /api/documents/upload</c>:
///   * .pdf extension enforced (FluentValidation)
///   * length > 0 and length &lt;= 50 MB enforced (FluentValidation)
///   * payload must start with %PDF- magic bytes (handler-tier defence-in-depth)
/// Validator runs first at runtime; handler magic-byte check is the last line of
/// defence against a misleading .pdf extension wrapped around non-PDF content.
/// </summary>
public class UploadDocumentValidationTests : IDisposable
{
    private const long MaxFileSize = 50L * 1024 * 1024;

    private readonly UploadDocumentValidator _validator = new();
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly DocumentStorage _storage;
    private readonly string _tempDir;

    public UploadDocumentValidationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"UploadValidationTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);

        _tempDir = Path.Combine(Path.GetTempPath(), $"upload_validation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _env = Substitute.For<IWebHostEnvironment>();
        _env.ContentRootPath.Returns(_tempDir);
        _env.WebRootPath.Returns(_tempDir);
        _storage = new DocumentStorage(_env);
    }

    // ---- Validator tier (extension + size) -----------------------------------------------

    [Fact]
    public void Validator_AcceptsPdfExtension()
    {
        var cmd = new UploadDocumentCommand(MakeFile("contract.pdf", length: 1024));

        var result = _validator.Validate(cmd);

        Assert.True(result.IsValid, FlattenErrors(result));
    }

    [Fact]
    public void Validator_AcceptsUppercasePdfExtension()
    {
        var cmd = new UploadDocumentCommand(MakeFile("CONTRACT.PDF", length: 1024));

        var result = _validator.Validate(cmd);

        Assert.True(result.IsValid, FlattenErrors(result));
    }

    [Theory]
    [InlineData("contract.docx")]
    [InlineData("contract.txt")]
    [InlineData("contract")]
    [InlineData("contract.pdf.exe")]
    public void Validator_RejectsNonPdfExtension(string fileName)
    {
        var cmd = new UploadDocumentCommand(MakeFile(fileName, length: 1024));

        var result = _validator.Validate(cmd);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("FileName", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AcceptsExactly50MB()
    {
        var cmd = new UploadDocumentCommand(MakeFile("contract.pdf", length: MaxFileSize));

        var result = _validator.Validate(cmd);

        Assert.True(result.IsValid, FlattenErrors(result));
    }

    [Fact]
    public void Validator_RejectsOver50MB()
    {
        var cmd = new UploadDocumentCommand(MakeFile("contract.pdf", length: MaxFileSize + 1));

        var result = _validator.Validate(cmd);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Length", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsZeroLength()
    {
        var cmd = new UploadDocumentCommand(MakeFile("contract.pdf", length: 0));

        var result = _validator.Validate(cmd);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Length", StringComparison.Ordinal));
    }

    // ---- Handler tier (%PDF- magic bytes) ------------------------------------------------

    [Fact]
    public async Task Handler_AcceptsPayloadWithPdfMagicBytes()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n%dummy\n");
        var cmd = new UploadDocumentCommand(MakeFile("contract.pdf", bytes));

        var handler = CreateHandler();
        var response = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.DocumentId);
        Assert.True(File.Exists(Path.Combine(_storage.UploadsRoot, $"{response.DocumentId}.pdf")));
    }

    [Fact]
    public async Task Handler_RejectsPayloadWithoutPdfMagicBytes()
    {
        // PNG header masquerading as a .pdf — exactly the attack the magic-byte check
        // exists to catch.
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var cmd = new UploadDocumentCommand(MakeFile("contract.pdf", bytes));

        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidFileFormatException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handler_RejectsPayloadShorterThanMagicBytes()
    {
        // 3 bytes — less than the 5-byte %PDF- prefix. Must not under-read.
        var bytes = new byte[] { 0x25, 0x50, 0x44 };
        var cmd = new UploadDocumentCommand(MakeFile("contract.pdf", bytes));

        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidFileFormatException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    // ---- Helpers --------------------------------------------------------------------------

    private UploadDocumentHandler CreateHandler()
    {
        var user = Substitute.For<ICurrentUserAccessor>();
        user.OrganizationId.Returns(Guid.NewGuid());
        user.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditWriter>();
        return new UploadDocumentHandler(
            _db,
            _storage,
            audit,
            user,
            NullLogger<UploadDocumentHandler>.Instance);
    }

    /// <summary>Builds an IFormFile whose <c>Length</c> is asserted by the validator
    /// without allocating that many bytes in memory. Content is irrelevant to the validator.</summary>
    private static IFormFile MakeFile(string fileName, long length)
    {
        var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D });
        return new FormFile(stream, baseStreamOffset: 0, length: length, name: "file", fileName: fileName);
    }

    /// <summary>Builds an IFormFile whose actual bytes drive the handler-tier magic-byte check.</summary>
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
