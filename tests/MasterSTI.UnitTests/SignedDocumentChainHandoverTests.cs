using MasterSTI.Api.Common.Signing;
using MasterSTI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.UnitTests;

public class SignedDocumentChainHandoverTests : IDisposable
{
    private readonly AppDbContext _db;

    public SignedDocumentChainHandoverTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ChainHandover_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
    }

    private async Task<Guid> SeedDocAsync(params (string email, int order)[] recipients)
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "lease.pdf",
            ContentType = "application/pdf",
            StoragePath = $"/tmp/{docId}.pdf",
            Sha256Hash = "deadbeef",
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Awaiting
        });
        foreach (var (email, order) in recipients)
        {
            _db.Recipients.Add(new Recipient
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                Email = email,
                Name = email,
                Order = order,
                Level = "QES",
                Status = order == 1 ? RecipientStatus.Notified : RecipientStatus.Pending,
                NotifiedAt = order == 1 ? DateTime.UtcNow : null
            });
        }
        await _db.SaveChangesAsync();
        return docId;
    }

    private async Task<SigningRequest> SeedSigningRequestAsync(Guid docId, int order)
    {
        var recipient = await _db.Recipients
            .Where(r => r.DocumentId == docId && r.Order == order)
            .FirstAsync();

        var sr = new SigningRequest
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            RecipientId = recipient.Id,
            OrderIndex = recipient.Order,
            RequestedBy = "system",
            CredentialId = "cred-001",
            SignatureLevel = "PAdES-B-LT",
            DocumentHash = "abc",
            PreparedStoragePath = "/tmp/prep.pdf",
            Status = SigningRequestStatus.CredentialAuthorized,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        sr.Recipient = recipient;
        sr.Document = await _db.Documents.FirstAsync(d => d.Id == docId);
        _db.SigningRequests.Add(sr);
        await _db.SaveChangesAsync();
        return sr;
    }

    [Fact]
    public async Task FirstSigner_OnTwoRecipientDoc_CreatesChainAndNotifiesNext()
    {
        var docId = await SeedDocAsync(("toma@x", 1), ("thea@x", 2));
        var sigReq = await SeedSigningRequestAsync(docId, order: 1);

        var newSignedId = Guid.NewGuid();
        await SignedDocumentChainHandover.ApplyAsync(
            _db, sigReq, newSignedId, "/tmp/signed-1.pdf", "PAdES-B-LT", manifestJson: null);
        await _db.SaveChangesAsync();

        var signed = await _db.SignedDocuments.FirstAsync(s => s.Id == newSignedId);
        Assert.Null(signed.PreviousSignedDocumentId);
        Assert.False(signed.IsFinal);
        Assert.Equal(sigReq.RecipientId, signed.RecipientId);

        var toma = await _db.Recipients.FirstAsync(r => r.DocumentId == docId && r.Order == 1);
        Assert.Equal(RecipientStatus.Signed, toma.Status);
        Assert.NotNull(toma.SignedAt);

        var thea = await _db.Recipients.FirstAsync(r => r.DocumentId == docId && r.Order == 2);
        Assert.Equal(RecipientStatus.Notified, thea.Status);
        Assert.NotNull(thea.NotifiedAt);

        var nextSigReq = await _db.SigningRequests
            .Where(s => s.DocumentId == docId && s.RecipientId == thea.Id)
            .SingleAsync();
        Assert.Equal(SigningRequestStatus.Pending, nextSigReq.Status);
        Assert.Equal(2, nextSigReq.OrderIndex);

        var doc = await _db.Documents.FirstAsync(d => d.Id == docId);
        Assert.Equal(DocumentStatus.Awaiting, doc.Status);

        Assert.Equal(SigningRequestStatus.Embedded, sigReq.Status);
    }

    [Fact]
    public async Task SecondSigner_OnTwoRecipientDoc_ClosesChainAndMarksDocSigned()
    {
        var docId = await SeedDocAsync(("toma@x", 1), ("thea@x", 2));

        // Pretend Toma already signed: create his SignedDocument as the existing chain tail.
        var toma = await _db.Recipients.FirstAsync(r => r.DocumentId == docId && r.Order == 1);
        toma.Status = RecipientStatus.Signed;
        toma.SignedAt = DateTime.UtcNow.AddMinutes(-5);
        var tomaSigReq = await SeedSigningRequestAsync(docId, order: 1);
        tomaSigReq.Status = SigningRequestStatus.Embedded;
        var tomaSignedId = Guid.NewGuid();
        _db.SignedDocuments.Add(new SignedDocument
        {
            Id = tomaSignedId,
            OriginalDocumentId = docId,
            SigningRequestId = tomaSigReq.Id,
            RecipientId = toma.Id,
            PreviousSignedDocumentId = null,
            IsFinal = true,
            StoragePath = "/tmp/toma-signed.pdf",
            SignedAt = DateTime.UtcNow.AddMinutes(-4),
            PadesLevel = "PAdES-B-LT"
        });
        // Thea is now Notified for her turn.
        var thea = await _db.Recipients.FirstAsync(r => r.DocumentId == docId && r.Order == 2);
        thea.Status = RecipientStatus.Notified;
        thea.NotifiedAt = DateTime.UtcNow.AddMinutes(-3);
        await _db.SaveChangesAsync();

        var theaSigReq = await SeedSigningRequestAsync(docId, order: 2);
        var theaSignedId = Guid.NewGuid();
        await SignedDocumentChainHandover.ApplyAsync(
            _db, theaSigReq, theaSignedId, "/tmp/thea-signed.pdf", "PAdES-B-LT", manifestJson: null);
        await _db.SaveChangesAsync();

        var theaSigned = await _db.SignedDocuments.FirstAsync(s => s.Id == theaSignedId);
        Assert.Equal(tomaSignedId, theaSigned.PreviousSignedDocumentId);
        Assert.True(theaSigned.IsFinal);

        var tomaSigned = await _db.SignedDocuments.FirstAsync(s => s.Id == tomaSignedId);
        Assert.False(tomaSigned.IsFinal);

        var doc = await _db.Documents.FirstAsync(d => d.Id == docId);
        Assert.Equal(DocumentStatus.Signed, doc.Status);

        var theaRefreshed = await _db.Recipients.FirstAsync(r => r.Id == thea.Id);
        Assert.Equal(RecipientStatus.Signed, theaRefreshed.Status);
        Assert.NotNull(theaRefreshed.SignedAt);
    }

    [Fact]
    public async Task MidChainSigner_OnThreeRecipientDoc_LinksAndAdvances()
    {
        var docId = await SeedDocAsync(("a@x", 1), ("b@x", 2), ("c@x", 3));

        // Seed signer 1 as already embedded with an existing chain tail.
        var a = await _db.Recipients.FirstAsync(r => r.DocumentId == docId && r.Order == 1);
        a.Status = RecipientStatus.Signed;
        a.SignedAt = DateTime.UtcNow.AddMinutes(-10);
        var aSigReq = await SeedSigningRequestAsync(docId, order: 1);
        aSigReq.Status = SigningRequestStatus.Embedded;
        var aSignedId = Guid.NewGuid();
        _db.SignedDocuments.Add(new SignedDocument
        {
            Id = aSignedId,
            OriginalDocumentId = docId,
            SigningRequestId = aSigReq.Id,
            RecipientId = a.Id,
            PreviousSignedDocumentId = null,
            IsFinal = true,
            StoragePath = "/tmp/a-signed.pdf",
            SignedAt = DateTime.UtcNow.AddMinutes(-9),
            PadesLevel = "PAdES-B-LT"
        });
        var b = await _db.Recipients.FirstAsync(r => r.DocumentId == docId && r.Order == 2);
        b.Status = RecipientStatus.Notified;
        b.NotifiedAt = DateTime.UtcNow.AddMinutes(-8);
        await _db.SaveChangesAsync();

        var bSigReq = await SeedSigningRequestAsync(docId, order: 2);
        var bSignedId = Guid.NewGuid();
        await SignedDocumentChainHandover.ApplyAsync(
            _db, bSigReq, bSignedId, "/tmp/b-signed.pdf", "PAdES-B-LT", manifestJson: null);
        await _db.SaveChangesAsync();

        var bSigned = await _db.SignedDocuments.FirstAsync(s => s.Id == bSignedId);
        Assert.Equal(aSignedId, bSigned.PreviousSignedDocumentId);
        Assert.False(bSigned.IsFinal);

        var aSignedRefreshed = await _db.SignedDocuments.FirstAsync(s => s.Id == aSignedId);
        Assert.False(aSignedRefreshed.IsFinal);

        var c = await _db.Recipients.FirstAsync(r => r.DocumentId == docId && r.Order == 3);
        Assert.Equal(RecipientStatus.Notified, c.Status);
        Assert.NotNull(c.NotifiedAt);

        var cSigReq = await _db.SigningRequests
            .Where(s => s.DocumentId == docId && s.RecipientId == c.Id)
            .SingleAsync();
        Assert.Equal(SigningRequestStatus.Pending, cSigReq.Status);
        Assert.Equal(3, cSigReq.OrderIndex);

        var doc = await _db.Documents.FirstAsync(d => d.Id == docId);
        Assert.Equal(DocumentStatus.Awaiting, doc.Status);

        var bRefreshed = await _db.Recipients.FirstAsync(r => r.Id == b.Id);
        Assert.Equal(RecipientStatus.Signed, bRefreshed.Status);
    }

    public void Dispose() => _db.Dispose();
}
