using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Documents.Detail;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.UnitTests;

public class GetDocumentDetailHandlerTests : IDisposable
{
    private readonly AppDbContext _db;

    public GetDocumentDetailHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DetailTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
    }

    private GetDocumentDetailHandler CreateHandler() => new(_db);

    private async Task<(Guid docId, Guid senderId, Recipient r1, Recipient r2)> SeedTwoRecipientsAsync()
    {
        var senderId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        _db.Users.Add(new User { Id = senderId, Email = "sender@verasign.demo", Name = "Sender X", Role = "Admin", PasswordHash = "", CreatedAt = DateTime.UtcNow });
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "contract.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/x",
            Sha256Hash = "abc",
            UploadedAt = DateTime.UtcNow.AddHours(-2),
            Status = DocumentStatus.Awaiting,
            OwnerUserId = senderId
        });
        var r1 = new Recipient
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Email = "thea@verasign.demo",
            Name = "Thea Popescu",
            Order = 1,
            Level = "QES",
            Status = RecipientStatus.Signed,
            NotifiedAt = DateTime.UtcNow.AddHours(-2),
            SignedAt = DateTime.UtcNow.AddHours(-1),
        };
        var r2 = new Recipient
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Email = "toma@verasign.demo",
            Name = "Toma Iliescu",
            Order = 2,
            Level = "QES",
            Status = RecipientStatus.Notified,
            NotifiedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        _db.Recipients.AddRange(r1, r2);
        await _db.SaveChangesAsync();
        return (docId, senderId, r1, r2);
    }

    [Fact]
    public async Task Handle_MissingDocument_ReturnsNull()
    {
        var result = await CreateHandler().Handle(
            new GetDocumentDetailQuery(Guid.NewGuid()), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_TwoRecipients_OneSigned_TimelineReflectsStatus()
    {
        var seeded = await SeedTwoRecipientsAsync();
        var req = new SigningRequest
        {
            Id = Guid.NewGuid(),
            DocumentId = seeded.docId,
            RecipientId = seeded.r1.Id,
            OrderIndex = 1,
            RequestedBy = "sender",
            CredentialId = "cred-1",
            DocumentHash = "h",
            Status = SigningRequestStatus.Embedded,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var signed = new SignedDocument
        {
            Id = Guid.NewGuid(),
            OriginalDocumentId = seeded.docId,
            SigningRequestId = req.Id,
            RecipientId = seeded.r1.Id,
            PreviousSignedDocumentId = null,
            IsFinal = false,
            StoragePath = "signed/contract-stage1.pdf",
            SignedAt = DateTime.UtcNow.AddHours(-1),
            PadesLevel = "PAdES-B-T",
        };
        _db.SigningRequests.Add(req);
        _db.SignedDocuments.Add(signed);
        await _db.SaveChangesAsync();

        var dto = await CreateHandler().Handle(
            new GetDocumentDetailQuery(seeded.docId), CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal("contract.pdf", dto!.FileName);
        Assert.Equal("Awaiting", dto.Status);
        Assert.Equal("QES", dto.Level);
        Assert.Equal("Sender X", dto.SenderName);

        Assert.Equal(2, dto.Recipients.Count);
        Assert.Equal("Thea Popescu", dto.Recipients[0].Name);
        Assert.Equal("Signed", dto.Recipients[0].Status);
        Assert.Equal("Toma Iliescu", dto.Recipients[1].Name);
        Assert.Equal("Notified", dto.Recipients[1].Status);

        Assert.Single(dto.Stages);
        Assert.Equal(1, dto.Stages[0].Stage);
        Assert.Equal("Thea Popescu", dto.Stages[0].SignerName);
        Assert.Equal("PAdES-B-T", dto.Stages[0].PadesLevel);
        Assert.False(dto.Stages[0].IsFinal);
        Assert.Equal(signed.Id, dto.SignedDocumentId);
    }

    [Fact]
    public async Task Handle_TwoStageChain_OrdersByPreviousLink()
    {
        var seeded = await SeedTwoRecipientsAsync();
        var req1Id = Guid.NewGuid();
        var req2Id = Guid.NewGuid();
        _db.SigningRequests.AddRange(
            new SigningRequest { Id = req1Id, DocumentId = seeded.docId, RecipientId = seeded.r1.Id, OrderIndex = 1, RequestedBy = "x", CredentialId = "c", DocumentHash = "h", Status = SigningRequestStatus.Embedded, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new SigningRequest { Id = req2Id, DocumentId = seeded.docId, RecipientId = seeded.r2.Id, OrderIndex = 2, RequestedBy = "x", CredentialId = "c", DocumentHash = "h", Status = SigningRequestStatus.Embedded, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        var first = new SignedDocument
        {
            Id = Guid.NewGuid(),
            OriginalDocumentId = seeded.docId,
            SigningRequestId = req1Id,
            RecipientId = seeded.r1.Id,
            PreviousSignedDocumentId = null,
            IsFinal = false,
            StoragePath = "s1.pdf",
            SignedAt = DateTime.UtcNow.AddHours(-2),
            PadesLevel = "PAdES-B-T",
        };
        var last = new SignedDocument
        {
            Id = Guid.NewGuid(),
            OriginalDocumentId = seeded.docId,
            SigningRequestId = req2Id,
            RecipientId = seeded.r2.Id,
            PreviousSignedDocumentId = first.Id,
            IsFinal = true,
            StoragePath = "s2.pdf",
            SignedAt = DateTime.UtcNow.AddHours(-1),
            PadesLevel = "PAdES-B-LTA",
        };
        // Insert in reverse order to verify ordering is by chain link, not insert order.
        _db.SignedDocuments.AddRange(last, first);
        await _db.SaveChangesAsync();

        var dto = await CreateHandler().Handle(
            new GetDocumentDetailQuery(seeded.docId), CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal(2, dto!.Stages.Count);
        Assert.Equal(first.Id, dto.Stages[0].Id);
        Assert.Equal(last.Id, dto.Stages[1].Id);
        Assert.Equal("Thea Popescu", dto.Stages[0].SignerName);
        Assert.Equal("Toma Iliescu", dto.Stages[1].SignerName);
        Assert.True(dto.Stages[1].IsFinal);
        Assert.Equal("PAdES-B-LTA", dto.Stages[1].PadesLevel);
        Assert.Equal(last.Id, dto.SignedDocumentId);
    }

    public void Dispose() => _db.Dispose();
}
