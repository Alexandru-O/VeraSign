using iText.Kernel.Pdf;
using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Signing.Prepare;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MasterSTI.UnitTests;

public class PrepareSigningHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PadesService _pades;
    private readonly IWebHostEnvironment _env;
    private readonly DocumentStorage _storage;
    private readonly string _tempDir;

    public PrepareSigningHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);

        _tempDir = Path.Combine(Path.GetTempPath(), $"pades_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _pades = new PadesService(NullLogger<PadesService>.Instance);

        _env = Substitute.For<IWebHostEnvironment>();
        _env.ContentRootPath.Returns(_tempDir);
        _env.WebRootPath.Returns(_tempDir);

        _storage = new DocumentStorage(_env);
    }

    private static byte[] CreateMinimalPdf()
    {
        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using var doc = new PdfDocument(writer);
        doc.AddNewPage();
        doc.Close();
        return ms.ToArray();
    }

    private async Task<(Guid docId, Guid recipientId, string absoluteStoragePath)> SeedDocumentAsync(byte[] pdfBytes, int recipientOrder = 1)
    {
        var hash = HashingService.ComputeSha256(pdfBytes);
        var docId = Guid.NewGuid();

        var absolutePath = Path.Combine(_storage.UploadsRoot, $"{docId}.pdf");
        await File.WriteAllBytesAsync(absolutePath, pdfBytes);

        var doc = new Document
        {
            Id = docId,
            FileName = "test.pdf",
            ContentType = "application/pdf",
            StoragePath = absolutePath,
            Sha256Hash = hash,
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Uploaded
        };
        _db.Documents.Add(doc);

        var recipient = new Recipient
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Email = "signer@example.test",
            Name = "Test Signer",
            Order = recipientOrder,
            Level = "QES",
            Status = RecipientStatus.Pending
        };
        _db.Recipients.Add(recipient);
        await _db.SaveChangesAsync();
        return (docId, recipient.Id, absolutePath);
    }

    private PrepareSigningHandler CreateHandler() =>
        new PrepareSigningHandler(_db, _storage, _pades, NullLogger<PrepareSigningHandler>.Instance);

    private PrepareSigningHandler CreateHandlerAs(string callerEmail)
    {
        var user = Substitute.For<ICurrentUserAccessor>();
        user.Email.Returns(callerEmail);
        return new PrepareSigningHandler(
            _db, _storage, _pades, NullLogger<PrepareSigningHandler>.Instance,
            audit: null, currentUser: user);
    }

    private async Task SetRecipientStatusAsync(Guid recipientId, RecipientStatus status)
    {
        var r = await _db.Recipients.FindAsync(recipientId);
        r!.Status = status;
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_ValidDocument_CreatesSigningRequest()
    {
        var pdfBytes = CreateMinimalPdf();
        var (docId, recipientId, _) = await SeedDocumentAsync(pdfBytes);

        var handler = CreateHandler();
        var cmd = new PrepareSigningCommand(docId, recipientId, "test-user", "mock-credential-001");
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.SigningRequestId);
        Assert.Equal(64, result.DocumentHash.Length);
    }

    [Fact]
    public async Task Handle_ValidDocument_StatusSetToHashPrepared()
    {
        var pdfBytes = CreateMinimalPdf();
        var (docId, recipientId, _) = await SeedDocumentAsync(pdfBytes, recipientOrder: 2);

        var handler = CreateHandler();
        var cmd = new PrepareSigningCommand(docId, recipientId, "test-user", "mock-credential-001");
        var result = await handler.Handle(cmd, CancellationToken.None);

        var sigReq = await _db.SigningRequests.FindAsync(result.SigningRequestId);
        Assert.NotNull(sigReq);
        Assert.Equal(SigningRequestStatus.HashPrepared, sigReq!.Status);
        Assert.NotNull(sigReq.PreparedStoragePath);
        Assert.True(File.Exists(sigReq.PreparedStoragePath));
        Assert.Equal(result.DocumentHash, sigReq.DocumentHash);
        Assert.Equal(recipientId, sigReq.RecipientId);
        Assert.Equal(2, sigReq.OrderIndex);
    }

    [Fact]
    public async Task Handle_TamperedDocument_ThrowsInvalidOperation()
    {
        var pdfBytes = CreateMinimalPdf();
        var (docId, recipientId, absolutePath) = await SeedDocumentAsync(pdfBytes);

        var tamperedBytes = CreateMinimalPdf();
        await File.WriteAllBytesAsync(absolutePath, tamperedBytes);

        var handler = CreateHandler();
        var cmd = new PrepareSigningCommand(docId, recipientId, "test-user", "mock-credential-001");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NonExistentDocument_ThrowsKeyNotFound()
    {
        var handler = CreateHandler();
        var cmd = new PrepareSigningCommand(Guid.NewGuid(), Guid.NewGuid(), "test-user", "mock-credential-001");
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_EmptyRecipientId_Throws()
    {
        var pdfBytes = CreateMinimalPdf();
        var (docId, _, _) = await SeedDocumentAsync(pdfBytes);

        var handler = CreateHandler();
        var cmd = new PrepareSigningCommand(docId, Guid.Empty, "test-user", "mock-credential-001");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_RecipientFromOtherDocument_ThrowsInvalidOperation()
    {
        var pdfBytes = CreateMinimalPdf();
        var (docId, _, _) = await SeedDocumentAsync(pdfBytes);
        var (_, foreignRecipientId, _) = await SeedDocumentAsync(CreateMinimalPdf());

        var handler = CreateHandler();
        var cmd = new PrepareSigningCommand(docId, foreignRecipientId, "test-user", "mock-credential-001");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_CallerIsNotRecipient_ThrowsUnauthorized()
    {
        var pdfBytes = CreateMinimalPdf();
        var (docId, recipientId, _) = await SeedDocumentAsync(pdfBytes);
        await SetRecipientStatusAsync(recipientId, RecipientStatus.Notified);

        var handler = CreateHandlerAs("admin@verasign.demo");
        var cmd = new PrepareSigningCommand(docId, recipientId, "admin", "mock-credential-001");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_CallerIsRecipient_RecipientNotified_Succeeds()
    {
        var pdfBytes = CreateMinimalPdf();
        var (docId, recipientId, _) = await SeedDocumentAsync(pdfBytes);
        await SetRecipientStatusAsync(recipientId, RecipientStatus.Notified);

        var handler = CreateHandlerAs("SIGNER@example.test");
        var cmd = new PrepareSigningCommand(docId, recipientId, "signer", "mock-credential-001");
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.SigningRequestId);
    }

    [Fact]
    public async Task Handle_CallerIsRecipient_StatusNotNotified_ThrowsUnauthorized()
    {
        var pdfBytes = CreateMinimalPdf();
        var (docId, recipientId, _) = await SeedDocumentAsync(pdfBytes);
        // Recipient seeded with Pending (default), not yet Notified.

        var handler = CreateHandlerAs("signer@example.test");
        var cmd = new PrepareSigningCommand(docId, recipientId, "signer", "mock-credential-001");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
