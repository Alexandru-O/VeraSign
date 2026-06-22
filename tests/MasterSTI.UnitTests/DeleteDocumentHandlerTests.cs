using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Documents.Delete;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MasterSTI.UnitTests;

public class DeleteDocumentHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _tempDir;

    public DeleteDocumentHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DeleteDocTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);

        _tempDir = Path.Combine(Path.GetTempPath(), $"delete_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private DeleteDocumentHandler CreateHandler() =>
        new(_db, NullLogger<DeleteDocumentHandler>.Instance);

    private async Task<(Document doc, SigningRequest req, SignedDocument signed, string uploadPath, string preparedPath, string signedPath)> SeedSignedDocAsync(DocumentStatus status = DocumentStatus.Signed)
    {
        var docId = Guid.NewGuid();
        var uploadPath = Path.Combine(_tempDir, $"{docId}-upload.pdf");
        var preparedPath = Path.Combine(_tempDir, $"{docId}-prepared.pdf");
        var signedPath = Path.Combine(_tempDir, $"{docId}-signed.pdf");
        await File.WriteAllBytesAsync(uploadPath, [0x25, 0x50, 0x44, 0x46]);
        await File.WriteAllBytesAsync(preparedPath, [0x25, 0x50, 0x44, 0x46]);
        await File.WriteAllBytesAsync(signedPath, [0x25, 0x50, 0x44, 0x46]);

        var doc = new Document
        {
            Id = docId,
            FileName = "test.pdf",
            ContentType = "application/pdf",
            StoragePath = uploadPath,
            Sha256Hash = "deadbeef",
            UploadedAt = DateTime.UtcNow,
            Status = status,
        };
        var req = new SigningRequest
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            RequestedBy = "test",
            CredentialId = "cred-1",
            DocumentHash = "deadbeef",
            PreparedStoragePath = preparedPath,
            Status = SigningRequestStatus.Signed,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var signed = new SignedDocument
        {
            Id = Guid.NewGuid(),
            OriginalDocumentId = docId,
            SigningRequestId = req.Id,
            StoragePath = signedPath,
            SignedAt = DateTime.UtcNow,
            PadesLevel = "PAdES-B-LT",
        };
        _db.Documents.Add(doc);
        _db.SigningRequests.Add(req);
        _db.SignedDocuments.Add(signed);
        _db.Recipients.Add(new Recipient { Id = Guid.NewGuid(), DocumentId = docId, Email = "a@b.c", Name = "A" });
        _db.SignatureFields.Add(new SignatureField { Id = Guid.NewGuid(), DocumentId = docId, Page = 1, X = 10, Y = 10, Width = 100, Height = 30 });
        _db.AuditEvents.Add(new AuditEvent { Id = Guid.NewGuid(), DocumentId = docId, EventType = "Uploaded", Timestamp = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        return (doc, req, signed, uploadPath, preparedPath, signedPath);
    }

    [Fact]
    public async Task Handle_RemovesDocumentChildrenAndFiles()
    {
        var seeded = await SeedSignedDocAsync();
        var handler = CreateHandler();

        var resp = await handler.Handle(new DeleteDocumentCommand(seeded.doc.Id), CancellationToken.None);

        Assert.Equal(seeded.doc.Id, resp.DocumentId);
        Assert.Empty(_db.Documents);
        Assert.Empty(_db.SigningRequests);
        Assert.Empty(_db.SignedDocuments);
        Assert.Empty(_db.Recipients);
        Assert.Empty(_db.SignatureFields);
        Assert.Empty(_db.AuditEvents);
        Assert.False(File.Exists(seeded.uploadPath));
        Assert.False(File.Exists(seeded.preparedPath));
        Assert.False(File.Exists(seeded.signedPath));
    }

    [Theory]
    [InlineData(DocumentStatus.Preparing)]
    [InlineData(DocumentStatus.Signing)]
    public async Task Handle_RejectsInFlightStatus(DocumentStatus status)
    {
        var seeded = await SeedSignedDocAsync(status);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteDocumentCommand(seeded.doc.Id), CancellationToken.None));

        Assert.Single(_db.Documents);
        Assert.True(File.Exists(seeded.uploadPath));
    }

    [Fact]
    public async Task Handle_MissingDocument_ThrowsKeyNotFound()
    {
        var handler = CreateHandler();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new DeleteDocumentCommand(Guid.NewGuid()), CancellationToken.None));
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
