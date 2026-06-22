using iText.Kernel.Pdf;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Wallet.InboxItem.Get;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MasterSTI.UnitTests;

public class GetInboxItemHandlerTests : IDisposable
{
    private const string SignerEmail = "andrei@verasign.demo";

    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user = Substitute.For<ICurrentUserAccessor>();
    private readonly string _tempDir;

    public GetInboxItemHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"InboxItemTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);

        _tempDir = Path.Combine(Path.GetTempPath(), $"inboxitem_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private GetInboxItemHandler CreateHandler() => new(_db, new RecipientAccessGuard(_db, _user));

    private static byte[] CreateMinimalPdf(int pages = 1)
    {
        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using var doc = new PdfDocument(writer);
        for (var i = 0; i < pages; i++) doc.AddNewPage();
        doc.Close();
        return ms.ToArray();
    }

    private async Task<(Guid recipientId, byte[] pdfBytes, string pdfPath)> SeedDocAsync(
        RecipientStatus recipientStatus,
        string recipientEmail = SignerEmail,
        int pages = 3,
        string level = "QES",
        string fileName = "contract.pdf",
        Guid? ownerUserId = null)
    {
        var pdfBytes = CreateMinimalPdf(pages);
        var docId = Guid.NewGuid();
        var path = Path.Combine(_tempDir, $"{docId}.pdf");
        await File.WriteAllBytesAsync(path, pdfBytes);

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pdfBytes)).ToLowerInvariant();

        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = fileName,
            ContentType = "application/pdf",
            StoragePath = path,
            Sha256Hash = hash,
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Awaiting,
            OwnerUserId = ownerUserId
        });
        var recipient = new Recipient
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Email = recipientEmail,
            Name = "—",
            Order = 1,
            Level = level,
            Status = recipientStatus,
            NotifiedAt = recipientStatus == RecipientStatus.Notified ? DateTime.UtcNow : null
        };
        _db.Recipients.Add(recipient);
        await _db.SaveChangesAsync();
        return (recipient.Id, pdfBytes, path);
    }

    private Guid SeedSender(string name = "Toma Iliescu", string email = "toma@verasign.demo")
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User
        {
            Id = id,
            Email = email,
            Name = name,
            Role = "Sender",
            PasswordHash = "",
            CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task HappyPath_ReturnsRealMetadata()
    {
        var senderId = SeedSender();
        var (recipientId, pdfBytes, _) = await SeedDocAsync(
            RecipientStatus.Notified,
            pages: 3,
            level: "QES",
            fileName: "lease.pdf",
            ownerUserId: senderId);

        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns(SignerEmail);

        var dto = await CreateHandler().Handle(new GetInboxItemQuery(recipientId), CancellationToken.None);

        Assert.Equal("lease.pdf", dto.DocumentName);
        Assert.Equal("Toma Iliescu", dto.SenderName);
        Assert.Equal(3, dto.Pages);
        Assert.Equal("QES", dto.Level);
        Assert.Equal(64, dto.Hash.Length);
        Assert.Equal(pdfBytes.Length, dto.SizeBytes);
    }

    [Fact]
    public async Task HappyPath_EmailCaseInsensitive()
    {
        var senderId = SeedSender();
        var (recipientId, _, _) = await SeedDocAsync(RecipientStatus.Notified, ownerUserId: senderId);

        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns(SignerEmail.ToUpperInvariant());

        var dto = await CreateHandler().Handle(new GetInboxItemQuery(recipientId), CancellationToken.None);

        Assert.NotNull(dto);
    }

    [Fact]
    public async Task RecipientNotFound_ThrowsKeyNotFound()
    {
        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns(SignerEmail);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            CreateHandler().Handle(new GetInboxItemQuery(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task EmptyRecipientId_ThrowsKeyNotFound()
    {
        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns(SignerEmail);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            CreateHandler().Handle(new GetInboxItemQuery(Guid.Empty), CancellationToken.None));
    }

    [Fact]
    public async Task RecipientNotNotified_ThrowsUnauthorized()
    {
        var (recipientId, _, _) = await SeedDocAsync(RecipientStatus.Pending);

        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns(SignerEmail);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateHandler().Handle(new GetInboxItemQuery(recipientId), CancellationToken.None));
    }

    [Fact]
    public async Task CallerEmailMismatch_ThrowsUnauthorized()
    {
        var (recipientId, _, _) = await SeedDocAsync(RecipientStatus.Notified);

        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns("other.user@verasign.demo");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateHandler().Handle(new GetInboxItemQuery(recipientId), CancellationToken.None));
    }

    [Fact]
    public async Task NoCallerUser_ThrowsUnauthorized()
    {
        var (recipientId, _, _) = await SeedDocAsync(RecipientStatus.Notified);

        _user.UserId.Returns((Guid?)null);
        _user.Email.Returns((string?)null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateHandler().Handle(new GetInboxItemQuery(recipientId), CancellationToken.None));
    }

    [Fact]
    public async Task MissingPdfFile_PagesZeroSizeZeroHashStillReturned()
    {
        var senderId = SeedSender();
        var (recipientId, _, pdfPath) = await SeedDocAsync(RecipientStatus.Notified, ownerUserId: senderId);
        File.Delete(pdfPath);

        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns(SignerEmail);

        var dto = await CreateHandler().Handle(new GetInboxItemQuery(recipientId), CancellationToken.None);

        Assert.Equal(0, dto.Pages);
        Assert.Equal(0, dto.SizeBytes);
        Assert.Equal(64, dto.Hash.Length);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
