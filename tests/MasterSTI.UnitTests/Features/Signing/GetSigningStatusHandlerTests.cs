using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Signing.Status;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.UnitTests.Features.Signing;

public class GetSigningStatusHandlerTests : IDisposable
{
    private readonly AppDbContext _db;

    public GetSigningStatusHandlerTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"SigStatusDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(opts);
    }

    private GetSigningStatusHandler Create() => new(_db);

    private (Guid sigReqId, Guid docId, Guid? signedDocId) Seed(
        SigningRequestStatus status,
        bool includeSignedDocument = false)
    {
        var docId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var sigReqId = Guid.NewGuid();
        Guid? signedDocId = null;

        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "x.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/x",
            Sha256Hash = "abc",
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Signing,
        });
        _db.Recipients.Add(new Recipient
        {
            Id = recipientId,
            DocumentId = docId,
            Email = "andrei@verasign.demo",
            Name = "Andrei",
            Order = 1,
            Level = "QES",
            Status = RecipientStatus.Notified,
        });
        _db.SigningRequests.Add(new SigningRequest
        {
            Id = sigReqId,
            DocumentId = docId,
            RecipientId = recipientId,
            OrderIndex = 1,
            RequestedBy = "andrei@verasign.demo",
            CredentialId = "cred-1",
            DocumentHash = "deadbeef",
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        if (includeSignedDocument)
        {
            signedDocId = Guid.NewGuid();
            _db.SignedDocuments.Add(new SignedDocument
            {
                Id = signedDocId.Value,
                OriginalDocumentId = docId,
                SigningRequestId = sigReqId,
                RecipientId = recipientId,
                StoragePath = $"/tmp/{signedDocId}.pdf",
                SignedAt = DateTime.UtcNow,
                PadesLevel = "PAdES-B-LT",
                IsFinal = true,
            });
        }
        _db.SaveChanges();
        return (sigReqId, docId, signedDocId);
    }

    [Fact]
    public async Task Handle_MissingRequest_ReturnsNull()
    {
        var result = await Create().Handle(
            new GetSigningStatusQuery(Guid.NewGuid()), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_HashPrepared_SignedDocumentIdIsNull()
    {
        var seeded = Seed(SigningRequestStatus.HashPrepared);

        var result = await Create().Handle(
            new GetSigningStatusQuery(seeded.sigReqId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("HashPrepared", result!.Status);
        Assert.Null(result.SignedDocumentId);
    }

    [Fact]
    public async Task Handle_SignedButNotEmbedded_SignedDocumentIdIsNull()
    {
        // Only Embedded should surface the SignedDocumentId. Earlier states
        // are still in-flight — the embed step writes the SignedDocument row.
        var seeded = Seed(SigningRequestStatus.Signed, includeSignedDocument: false);

        var result = await Create().Handle(
            new GetSigningStatusQuery(seeded.sigReqId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Signed", result!.Status);
        Assert.Null(result.SignedDocumentId);
    }

    [Fact]
    public async Task Handle_Embedded_ReturnsSignedDocumentId()
    {
        var seeded = Seed(SigningRequestStatus.Embedded, includeSignedDocument: true);

        var result = await Create().Handle(
            new GetSigningStatusQuery(seeded.sigReqId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Embedded", result!.Status);
        Assert.Equal(seeded.signedDocId, result.SignedDocumentId);
    }

    [Fact]
    public async Task Handle_EmbeddedButNoSignedDocumentRow_DegradesToNull()
    {
        // Defensive: if Status reaches Embedded before SignedDocument row exists
        // (shouldn't happen — embed handler writes both transactionally — but
        // the polling loop must not crash if it ever observes that gap).
        var seeded = Seed(SigningRequestStatus.Embedded, includeSignedDocument: false);

        var result = await Create().Handle(
            new GetSigningStatusQuery(seeded.sigReqId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Embedded", result!.Status);
        Assert.Null(result.SignedDocumentId);
    }

    [Fact]
    public async Task Handle_Failed_PropagatesFailedAtStage()
    {
        var docId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var sigReqId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId, FileName = "y.pdf", ContentType = "application/pdf",
            StoragePath = "/tmp/y", Sha256Hash = "xyz",
            UploadedAt = DateTime.UtcNow, Status = DocumentStatus.Failed,
        });
        _db.Recipients.Add(new Recipient
        {
            Id = recipientId, DocumentId = docId, Email = "a@b.com", Name = "A",
            Order = 1, Level = "QES", Status = RecipientStatus.Notified,
        });
        _db.SigningRequests.Add(new SigningRequest
        {
            Id = sigReqId, DocumentId = docId, RecipientId = recipientId,
            OrderIndex = 1, RequestedBy = "x", CredentialId = "c",
            DocumentHash = "h", Status = SigningRequestStatus.Failed,
            FailedAtStage = 4,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await Create().Handle(new GetSigningStatusQuery(sigReqId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Failed", result!.Status);
        Assert.Equal(4, result.FailedAtStage);
        Assert.Null(result.SignedDocumentId);
    }

    public void Dispose() => _db.Dispose();
}
