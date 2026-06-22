using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Wallet.History.List;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MasterSTI.UnitTests.Features.Wallet;

public class ListWalletHistoryHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user = Substitute.For<ICurrentUserAccessor>();

    public ListWalletHistoryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"HistoryTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
    }

    private ListWalletHistoryHandler CreateHandler() => new(_db, _user);

    private Guid SeedWalletUser(string pidEmail, Guid orgId, string userEmail = "andrei@verasign.demo")
    {
        var userId = Guid.NewGuid();
        _db.Users.Add(new User
        {
            Id = userId,
            Email = userEmail,
            Name = "Andrei Test",
            Role = "User",
            PasswordHash = "",
            OrganizationId = orgId,
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

    private Guid SeedSenderUser(string name, Guid orgId)
    {
        var senderId = Guid.NewGuid();
        _db.Users.Add(new User
        {
            Id = senderId,
            Email = $"sender-{senderId:N}@verasign.demo",
            Name = name,
            Role = "User",
            PasswordHash = "",
            OrganizationId = orgId,
            CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
        return senderId;
    }

    private (Guid documentId, Guid signedDocumentId) SeedSignedDoc(
        string recipientEmail,
        Guid orgId,
        Guid senderUserId,
        string fileName = "contract.pdf",
        string level = "QES",
        DateTime? signedAt = null)
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = fileName,
            ContentType = "application/pdf",
            StoragePath = "/tmp/x.pdf",
            Sha256Hash = "",
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Signed,
            OrganizationId = orgId,
            OwnerUserId = senderUserId
        });
        var recipientId = Guid.NewGuid();
        _db.Recipients.Add(new Recipient
        {
            Id = recipientId,
            DocumentId = docId,
            Email = recipientEmail,
            Name = "—",
            Order = 1,
            Level = level,
            Status = RecipientStatus.Signed,
            SignedAt = signedAt ?? DateTime.UtcNow
        });
        var signingRequestId = Guid.NewGuid();
        _db.SigningRequests.Add(new SigningRequest
        {
            Id = signingRequestId,
            DocumentId = docId,
            RecipientId = recipientId,
            OrderIndex = 1,
            RequestedBy = "test",
            CredentialId = "cred",
            SignatureLevel = "PAdES-B-LT",
            DocumentHash = "",
            Status = SigningRequestStatus.Embedded,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        var signedDocId = Guid.NewGuid();
        _db.SignedDocuments.Add(new SignedDocument
        {
            Id = signedDocId,
            OriginalDocumentId = docId,
            SigningRequestId = signingRequestId,
            RecipientId = recipientId,
            IsFinal = true,
            StoragePath = "/tmp/signed.pdf",
            SignedAt = signedAt ?? DateTime.UtcNow,
            PadesLevel = "PAdES-B-LT"
        });
        _db.SaveChanges();
        return (docId, signedDocId);
    }

    [Fact]
    public async Task NoUser_ReturnsEmpty()
    {
        _user.UserId.Returns((Guid?)null);
        var result = await CreateHandler().Handle(new ListWalletHistoryQuery(), CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task NoEnrollment_ReturnsEmpty()
    {
        _user.UserId.Returns(Guid.NewGuid());
        var result = await CreateHandler().Handle(new ListWalletHistoryQuery(), CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task FreshUser_NoSignedDocs_ReturnsEmpty()
    {
        var orgId = Guid.NewGuid();
        var userId = SeedWalletUser("andrei@verasign.demo", orgId);
        _user.UserId.Returns(userId);

        var result = await CreateHandler().Handle(new ListWalletHistoryQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task PidEmailMatches_ReturnsSignedDocument()
    {
        var orgId = Guid.NewGuid();
        var userId = SeedWalletUser("andrei@verasign.demo", orgId);
        _user.UserId.Returns(userId);
        var senderId = SeedSenderUser("Maria Pop", orgId);
        var (docId, signedDocId) = SeedSignedDoc("andrei@verasign.demo", orgId, senderId);

        var result = await CreateHandler().Handle(new ListWalletHistoryQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(docId, result[0].DocumentId);
        Assert.Equal(signedDocId, result[0].SignedDocumentId);
        Assert.Equal("contract.pdf", result[0].DocumentName);
        Assert.Equal("Maria Pop", result[0].SenderName);
        Assert.Equal("QES", result[0].Level);
    }

    [Fact]
    public async Task CrossOrg_StillVisible()
    {
        // Wallet user belongs to org A, signed document lives in org B —
        // join is by PID email only, never by OrganizationId.
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var userId = SeedWalletUser("andrei@verasign.demo", orgA);
        _user.UserId.Returns(userId);
        var senderId = SeedSenderUser("Other Org Sender", orgB);
        SeedSignedDoc("andrei@verasign.demo", orgB, senderId);

        var result = await CreateHandler().Handle(new ListWalletHistoryQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Other Org Sender", result[0].SenderName);
    }

    [Fact]
    public async Task EmailCaseInsensitive_Match()
    {
        var orgId = Guid.NewGuid();
        var userId = SeedWalletUser("andrei@verasign.demo", orgId);
        _user.UserId.Returns(userId);
        var senderId = SeedSenderUser("Maria Pop", orgId);
        SeedSignedDoc("ANDREI@VeraSign.Demo", orgId, senderId);

        var result = await CreateHandler().Handle(new ListWalletHistoryQuery(), CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task OtherRecipientEmail_Excluded()
    {
        var orgId = Guid.NewGuid();
        var userId = SeedWalletUser("andrei@verasign.demo", orgId);
        _user.UserId.Returns(userId);
        var senderId = SeedSenderUser("Maria Pop", orgId);
        SeedSignedDoc("someone-else@verasign.demo", orgId, senderId);

        var result = await CreateHandler().Handle(new ListWalletHistoryQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task OrderedByMostRecentFirst()
    {
        var orgId = Guid.NewGuid();
        var userId = SeedWalletUser("andrei@verasign.demo", orgId);
        _user.UserId.Returns(userId);
        var senderId = SeedSenderUser("Maria Pop", orgId);
        SeedSignedDoc("andrei@verasign.demo", orgId, senderId, fileName: "older.pdf", signedAt: DateTime.UtcNow.AddDays(-5));
        SeedSignedDoc("andrei@verasign.demo", orgId, senderId, fileName: "newer.pdf", signedAt: DateTime.UtcNow);

        var result = await CreateHandler().Handle(new ListWalletHistoryQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("newer.pdf", result[0].DocumentName);
        Assert.Equal("older.pdf", result[1].DocumentName);
    }

    public void Dispose() => _db.Dispose();
}
