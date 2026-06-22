using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Documents.Cancel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MasterSTI.UnitTests;

/// <summary>
/// Issue-#63 — symmetrically tests <see cref="CancelDocumentHandler"/> against
/// the matrix of pre-existing document statuses, mirroring how
/// <see cref="SendDocumentHandlerTests"/> and <see cref="DeleteDocumentHandlerTests"/>
/// cover their respective transitions. The cancel handler is the only path that
/// flips an in-flight document into <see cref="DocumentStatus.Cancelled"/> and
/// fans pending recipients to <see cref="RecipientStatus.Declined"/>; without
/// this coverage the dissertation pipeline can mis-report a cancellation as a
/// failure or leave recipients in Notified state.
/// </summary>
public class CancelDocumentHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RecordingAuditWriter _audit = new();

    public CancelDocumentHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"CancelDocTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
    }

    private CancelDocumentHandler CreateHandler() =>
        new(_db, _audit, NullLogger<CancelDocumentHandler>.Instance);

    private async Task<Guid> SeedAsync(DocumentStatus status,
        params (string email, RecipientStatus rStatus)[] recipients)
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/x.pdf",
            Sha256Hash = "deadbeef",
            UploadedAt = DateTime.UtcNow,
            Status = status,
            OrganizationId = Guid.NewGuid(),
        });
        var order = 1;
        foreach (var (email, rStatus) in recipients)
        {
            _db.Recipients.Add(new Recipient
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                Email = email,
                Name = email.Split('@')[0],
                Order = order++,
                Level = "QES",
                Status = rStatus,
            });
        }
        await _db.SaveChangesAsync();
        return docId;
    }

    [Fact]
    public async Task Handle_AwaitingDocument_TransitionsToCancelledAndDeclinesPendingRecipients()
    {
        var docId = await SeedAsync(DocumentStatus.Awaiting,
            ("first@verasign.demo", RecipientStatus.Notified),
            ("second@verasign.demo", RecipientStatus.Pending),
            ("third@verasign.demo", RecipientStatus.Signed));

        var response = await CreateHandler()
            .Handle(new CancelDocumentCommand(docId, Reason: "no longer needed"), CancellationToken.None);

        var doc = await _db.Documents.FirstAsync(d => d.Id == docId);
        Assert.Equal(DocumentStatus.Cancelled, doc.Status);
        Assert.Equal(docId, response.DocumentId);
        Assert.Equal(nameof(DocumentStatus.Cancelled), response.Status);

        // Notified + Pending recipients flipped to Declined; already-signed left alone.
        var recipients = await _db.Recipients.Where(r => r.DocumentId == docId).OrderBy(r => r.Order).ToListAsync();
        Assert.Equal(RecipientStatus.Declined, recipients[0].Status);
        Assert.Equal(RecipientStatus.Declined, recipients[1].Status);
        Assert.Equal(RecipientStatus.Signed,   recipients[2].Status);
    }

    [Fact]
    public async Task Handle_UploadedDocument_CancelsEvenWithNoRecipients()
    {
        // Documents in Uploaded never went through Send -- they have no
        // recipients yet, but the user can still abandon them via the cancel
        // endpoint without going through delete.
        var docId = await SeedAsync(DocumentStatus.Uploaded);

        var response = await CreateHandler()
            .Handle(new CancelDocumentCommand(docId, Reason: null), CancellationToken.None);

        var doc = await _db.Documents.FirstAsync(d => d.Id == docId);
        Assert.Equal(DocumentStatus.Cancelled, doc.Status);
        Assert.Equal(nameof(DocumentStatus.Cancelled), response.Status);
    }

    [Theory]
    [InlineData(DocumentStatus.Preparing)]
    [InlineData(DocumentStatus.Signing)]
    [InlineData(DocumentStatus.Signed)]
    [InlineData(DocumentStatus.Failed)]
    [InlineData(DocumentStatus.Cancelled)]
    public async Task Handle_NonCancellableStatus_ThrowsInvalidOperationException(DocumentStatus status)
    {
        var docId = await SeedAsync(status,
            ("victim@verasign.demo", RecipientStatus.Notified));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(new CancelDocumentCommand(docId, Reason: null), CancellationToken.None));

        // Endpoint maps this to 409 Conflict — message must reference the live status.
        Assert.Contains(status.ToString(), ex.Message, StringComparison.Ordinal);

        // Document untouched.
        var doc = await _db.Documents.FirstAsync(d => d.Id == docId);
        Assert.Equal(status, doc.Status);
        var recipient = await _db.Recipients.FirstAsync(r => r.DocumentId == docId);
        Assert.Equal(RecipientStatus.Notified, recipient.Status);
        Assert.Empty(_audit.Events);
    }

    [Fact]
    public async Task Handle_MissingDocument_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            CreateHandler().Handle(new CancelDocumentCommand(Guid.NewGuid(), null), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AwaitingDocument_WritesCancelledAuditEvent_WithReasonPayload()
    {
        var docId = await SeedAsync(DocumentStatus.Awaiting);

        await CreateHandler().Handle(
            new CancelDocumentCommand(docId, Reason: "client asked for revisions"),
            CancellationToken.None);

        var ev = Assert.Single(_audit.Events);
        Assert.Equal("Cancelled", ev.EventType);
        Assert.Equal(docId, ev.DocumentId);
        Assert.NotNull(ev.Metadata);
        Assert.Contains("client asked for revisions", ev.Metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_AwaitingDocument_WithoutReason_WritesEmptyJsonPayload()
    {
        var docId = await SeedAsync(DocumentStatus.Awaiting);

        await CreateHandler().Handle(
            new CancelDocumentCommand(docId, Reason: null),
            CancellationToken.None);

        var ev = Assert.Single(_audit.Events);
        Assert.Equal("Cancelled", ev.EventType);
        Assert.Equal("{}", ev.Metadata);
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = new();
        public Task WriteAsync(Guid? documentId, string eventType, string? metadata = null, CancellationToken cancellationToken = default)
        {
            Events.Add(new AuditEvent
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                EventType = eventType,
                Metadata = metadata,
                Timestamp = DateTime.UtcNow,
            });
            return Task.CompletedTask;
        }
    }

    public void Dispose() => _db.Dispose();
}
