using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Wallet.Inbox;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MasterSTI.UnitTests;

public class ListInboxHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user = Substitute.For<ICurrentUserAccessor>();
    private readonly IHandoffTokenService _handoff = Substitute.For<IHandoffTokenService>();

    public ListInboxHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"InboxTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
        _handoff.Issue(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns("fake-token");
    }

    private ListInboxHandler CreateHandler() => new(_db, _user, _handoff);

    private Guid SeedWalletUser(string pidEmail, string userEmail = "andrei@verasign.demo")
    {
        var userId = Guid.NewGuid();
        _db.Users.Add(new User
        {
            Id = userId,
            Email = userEmail,
            Name = "Andrei Test",
            Role = "User",
            PasswordHash = "",
            CreatedAt = DateTime.UtcNow
        });
        _db.WalletEnrollments.Add(new WalletEnrollment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CnfJwkThumbprint = "thumb-" + Guid.NewGuid(),
            PidClaimsJson = "{}",
            PidEmail = pidEmail,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
        return userId;
    }

    private Guid SeedDoc(string recipientEmail, RecipientStatus recStatus, DocumentStatus docStatus, Guid? orgId = null)
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "contract.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/x.pdf",
            Sha256Hash = "",
            UploadedAt = DateTime.UtcNow,
            Status = docStatus,
            OrganizationId = orgId
        });
        _db.Recipients.Add(new Recipient
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Email = recipientEmail,
            Name = "—",
            Order = 1,
            Level = "QES",
            Status = recStatus,
            NotifiedAt = recStatus == RecipientStatus.Notified ? DateTime.UtcNow : null
        });
        _db.SaveChanges();
        return docId;
    }

    [Fact]
    public async Task NoUser_ReturnsEmpty()
    {
        _user.UserId.Returns((Guid?)null);
        var result = await CreateHandler().Handle(new ListInboxQuery(), CancellationToken.None);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task NoEnrollment_ReturnsEmpty()
    {
        _user.UserId.Returns(Guid.NewGuid());
        var result = await CreateHandler().Handle(new ListInboxQuery(), CancellationToken.None);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task PidEmailMatchesNotifiedAwaiting_ReturnsItem()
    {
        var userId = SeedWalletUser("andrei@verasign.demo");
        _user.UserId.Returns(userId);
        SeedDoc("andrei@verasign.demo", RecipientStatus.Notified, DocumentStatus.Awaiting);

        var result = await CreateHandler().Handle(new ListInboxQuery(), CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("contract.pdf", result.Items[0].DocumentName);
        Assert.StartsWith("verasign://sign?token=", result.Items[0].DeepLink);
    }

    [Fact]
    public async Task RecipientStatusNotNotified_Excluded()
    {
        var userId = SeedWalletUser("andrei@verasign.demo");
        _user.UserId.Returns(userId);
        SeedDoc("andrei@verasign.demo", RecipientStatus.Pending, DocumentStatus.Awaiting);
        SeedDoc("andrei@verasign.demo", RecipientStatus.Signed, DocumentStatus.Signed);

        var result = await CreateHandler().Handle(new ListInboxQuery(), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task DocumentNotAwaiting_Excluded()
    {
        var userId = SeedWalletUser("andrei@verasign.demo");
        _user.UserId.Returns(userId);
        SeedDoc("andrei@verasign.demo", RecipientStatus.Notified, DocumentStatus.Signed);

        var result = await CreateHandler().Handle(new ListInboxQuery(), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task EmailCaseInsensitive_Match()
    {
        var userId = SeedWalletUser("andrei@verasign.demo");
        _user.UserId.Returns(userId);
        SeedDoc("ANDREI@VeraSign.Demo", RecipientStatus.Notified, DocumentStatus.Awaiting);

        var result = await CreateHandler().Handle(new ListInboxQuery(), CancellationToken.None);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task CrossOrg_StillVisible()
    {
        var userId = SeedWalletUser("andrei@verasign.demo");
        _user.UserId.Returns(userId);
        var otherOrg = Guid.NewGuid();
        SeedDoc("andrei@verasign.demo", RecipientStatus.Notified, DocumentStatus.Awaiting, otherOrg);

        var result = await CreateHandler().Handle(new ListInboxQuery(), CancellationToken.None);

        Assert.Single(result.Items);
    }

    public void Dispose() => _db.Dispose();
}
