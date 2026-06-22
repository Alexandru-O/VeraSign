using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MasterSTI.UnitTests;

public class RecipientAccessGuardTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user = Substitute.For<ICurrentUserAccessor>();

    public RecipientAccessGuardTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"GuardTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
    }

    private RecipientAccessGuard Create() => new(_db, _user);

    private (Guid docId, Guid senderId, Guid recipientUserId) Seed(
        RecipientStatus status,
        string recipientEmail = "andrei@verasign.demo")
    {
        var senderId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        _db.Users.Add(new User { Id = senderId, Email = "sender@verasign.demo", Name = "S", Role = "Admin", PasswordHash = "", CreatedAt = DateTime.UtcNow });
        _db.Users.Add(new User { Id = recipientUserId, Email = recipientEmail, Name = "R", Role = "User", PasswordHash = "", CreatedAt = DateTime.UtcNow });
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "x.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/x",
            Sha256Hash = "",
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Awaiting,
            OwnerUserId = senderId
        });
        _db.Recipients.Add(new Recipient
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Email = recipientEmail,
            Name = "R",
            Order = 1,
            Level = "QES",
            Status = status
        });
        _db.SaveChanges();
        return (docId, senderId, recipientUserId);
    }

    [Fact]
    public async Task AnonymousUser_Denied()
    {
        var (docId, _, _) = Seed(RecipientStatus.Notified);
        _user.UserId.Returns((Guid?)null);

        var ok = await Create().CanAccessDocumentAsync(docId);

        Assert.False(ok);
    }

    [Fact]
    public async Task Sender_Allowed()
    {
        var (docId, senderId, _) = Seed(RecipientStatus.Notified);
        _user.UserId.Returns(senderId);
        _user.Email.Returns("sender@verasign.demo");

        var ok = await Create().CanAccessDocumentAsync(docId);

        Assert.True(ok);
    }

    [Fact]
    public async Task NotifiedRecipient_Allowed()
    {
        var (docId, _, recipientUserId) = Seed(RecipientStatus.Notified);
        _user.UserId.Returns(recipientUserId);
        _user.Email.Returns("andrei@verasign.demo");

        var ok = await Create().CanAccessDocumentAsync(docId);

        Assert.True(ok);
    }

    [Fact]
    public async Task PendingRecipient_Denied()
    {
        var (docId, _, recipientUserId) = Seed(RecipientStatus.Pending);
        _user.UserId.Returns(recipientUserId);
        _user.Email.Returns("andrei@verasign.demo");

        var ok = await Create().CanAccessDocumentAsync(docId);

        Assert.False(ok);
    }

    [Fact]
    public async Task SignedRecipient_Denied()
    {
        var (docId, _, recipientUserId) = Seed(RecipientStatus.Signed);
        _user.UserId.Returns(recipientUserId);
        _user.Email.Returns("andrei@verasign.demo");

        var ok = await Create().CanAccessDocumentAsync(docId);

        Assert.False(ok);
    }

    [Fact]
    public async Task UnrelatedUser_Denied()
    {
        var (docId, _, _) = Seed(RecipientStatus.Notified);
        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns("stranger@example.com");

        var ok = await Create().CanAccessDocumentAsync(docId);

        Assert.False(ok);
    }

    [Fact]
    public async Task EmailCaseMismatch_StillAllowed()
    {
        var (docId, _, recipientUserId) = Seed(RecipientStatus.Notified, "andrei@verasign.demo");
        _user.UserId.Returns(recipientUserId);
        _user.Email.Returns("ANDREI@VERASIGN.DEMO");

        var ok = await Create().CanAccessDocumentAsync(docId);

        Assert.True(ok);
    }

    [Fact]
    public async Task MissingDocument_Denied()
    {
        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns("x@y.com");

        var ok = await Create().CanAccessDocumentAsync(Guid.NewGuid());

        Assert.False(ok);
    }

    public void Dispose() => _db.Dispose();
}
