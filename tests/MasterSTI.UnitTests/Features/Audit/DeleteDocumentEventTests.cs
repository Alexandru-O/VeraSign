using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Documents.Delete;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MasterSTI.UnitTests.Features.Audit;

/// <summary>
/// Locks in that <see cref="DeleteDocumentHandler"/> emits a <c>Deleted</c> audit
/// event on the successful hard-delete path, and never on the 409 reject path
/// for in-flight documents (<see cref="DocumentStatus.Preparing"/> /
/// <see cref="DocumentStatus.Signing"/>).
/// </summary>
public class DeleteDocumentEventTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _tempDir;

    public DeleteDocumentEventTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DeleteAuditTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);

        _tempDir = Path.Combine(Path.GetTempPath(), $"delete_audit_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private async Task<Guid> SeedDocumentAsync(DocumentStatus status)
    {
        var docId = Guid.NewGuid();
        var uploadPath = Path.Combine(_tempDir, $"{docId}.pdf");
        await File.WriteAllBytesAsync(uploadPath, [0x25, 0x50, 0x44, 0x46]);

        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "audit-doc.pdf",
            ContentType = "application/pdf",
            StoragePath = uploadPath,
            Sha256Hash = "deadbeef",
            UploadedAt = DateTime.UtcNow,
            Status = status
        });
        await _db.SaveChangesAsync();
        return docId;
    }

    [Fact]
    public async Task Handle_OnSuccess_EmitsDeletedEvent()
    {
        var docId = await SeedDocumentAsync(DocumentStatus.Signed);
        var audit = Substitute.For<IAuditWriter>();

        var handler = new DeleteDocumentHandler(
            _db,
            NullLogger<DeleteDocumentHandler>.Instance,
            dashCache: null,
            audit: audit);

        await handler.Handle(new DeleteDocumentCommand(docId), CancellationToken.None);

        await audit.Received(1).WriteAsync(
            docId,
            "Deleted",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(DocumentStatus.Preparing)]
    [InlineData(DocumentStatus.Signing)]
    public async Task Handle_InFlight_DoesNotEmitDeletedEvent(DocumentStatus status)
    {
        var docId = await SeedDocumentAsync(status);
        var audit = Substitute.For<IAuditWriter>();

        var handler = new DeleteDocumentHandler(
            _db,
            NullLogger<DeleteDocumentHandler>.Instance,
            dashCache: null,
            audit: audit);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteDocumentCommand(docId), CancellationToken.None));

        await audit.DidNotReceive().WriteAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingDocument_DoesNotEmitDeletedEvent()
    {
        var audit = Substitute.For<IAuditWriter>();

        var handler = new DeleteDocumentHandler(
            _db,
            NullLogger<DeleteDocumentHandler>.Instance,
            dashCache: null,
            audit: audit);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new DeleteDocumentCommand(Guid.NewGuid()), CancellationToken.None));

        await audit.DidNotReceive().WriteAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Existing two-arg ctor (db, logger) must keep compiling — proves the
    /// optional ctor pattern is preserved per CLAUDE.md and mirrors
    /// <see cref="PrepareSigningHandler"/>'s 4-arg test seam.
    /// </summary>
    [Fact]
    public void Ctor_TwoArg_StillCompiles()
    {
        _ = new DeleteDocumentHandler(_db, NullLogger<DeleteDocumentHandler>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
