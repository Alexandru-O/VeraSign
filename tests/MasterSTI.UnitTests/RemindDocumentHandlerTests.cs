using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Email;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Documents.Remind;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MasterSTI.UnitTests;

public class RemindDocumentHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RecordingAuditWriter _audit = new();
    private readonly RecordingEmailSender _email = new();
    private readonly StubHandoffTokenService _handoff = new();
    private readonly IConfiguration _config;

    public RemindDocumentHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"RemindTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Web:PublicBaseUrl"] = "https://verasign.test"
            })
            .Build();
    }

    private RemindDocumentHandler CreateHandler() =>
        new(_db, _audit, _email, _handoff, _config, NullLogger<RemindDocumentHandler>.Instance);

    [Fact]
    public async Task Handle_RemindsOnlyTheCurrentlyNotifiedRecipient()
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "x.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/x.pdf",
            Sha256Hash = "h",
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Awaiting
        });
        var signed = new Recipient { Id = Guid.NewGuid(), DocumentId = docId, Email = "a@x", Name = "A", Order = 1, Level = "QES", Status = RecipientStatus.Signed };
        var current = new Recipient { Id = Guid.NewGuid(), DocumentId = docId, Email = "b@x", Name = "B", Order = 2, Level = "QES", Status = RecipientStatus.Notified };
        var pending = new Recipient { Id = Guid.NewGuid(), DocumentId = docId, Email = "c@x", Name = "C", Order = 3, Level = "QES", Status = RecipientStatus.Pending };
        _db.Recipients.AddRange(signed, current, pending);
        await _db.SaveChangesAsync();

        var response = await CreateHandler().Handle(new RemindDocumentCommand(docId), CancellationToken.None);

        Assert.Equal(1, response.RecipientsNudged);

        var fresh = await _db.Recipients.Where(r => r.DocumentId == docId).ToDictionaryAsync(r => r.Order);
        Assert.Equal(RecipientStatus.Signed, fresh[1].Status);
        Assert.Equal(RecipientStatus.Notified, fresh[2].Status);
        Assert.Equal(RecipientStatus.Pending, fresh[3].Status);

        var sent = Assert.Single(_email.Sent);
        Assert.Equal("b@x", sent.To);
        Assert.StartsWith("verasign://sign?token=stub-", sent.DeepLink);
        Assert.Contains(_audit.Events, e => e.EventType == "Reminded");
    }

    [Fact]
    public async Task Handle_NoNotifiedRecipient_Throws()
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "x.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/x.pdf",
            Sha256Hash = "h",
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Awaiting
        });
        _db.Recipients.Add(new Recipient { Id = Guid.NewGuid(), DocumentId = docId, Email = "a@x", Name = "A", Order = 1, Level = "QES", Status = RecipientStatus.Pending });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(new RemindDocumentCommand(docId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NonAwaitingDocument_Throws()
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "x.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/x.pdf",
            Sha256Hash = "h",
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Signed
        });
        _db.Recipients.Add(new Recipient { Id = Guid.NewGuid(), DocumentId = docId, Email = "a@x", Name = "A", Order = 1, Level = "QES", Status = RecipientStatus.Notified });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(new RemindDocumentCommand(docId), CancellationToken.None));
    }

    public void Dispose() => _db.Dispose();

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
                Timestamp = DateTime.UtcNow
            });
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHandoffTokenService : IHandoffTokenService
    {
        public string Issue(Guid recipientId, Guid documentId) => $"stub-{recipientId:N}";
        public HandoffClaims? Validate(string token) => null;
    }
}
