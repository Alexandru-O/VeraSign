using System.Text.Json;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Email;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Documents.Send;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MasterSTI.UnitTests;

public class SendDocumentHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RecordingAuditWriter _audit = new();
    private readonly RecordingEmailSender _email = new();
    private readonly StubHandoffTokenService _handoff = new();
    private readonly ICurrentUserAccessor _user = Substitute.For<ICurrentUserAccessor>();
    private readonly IConfiguration _config;

    public SendDocumentHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"SendDocTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Web:PublicBaseUrl"] = "https://verasign.test"
            })
            .Build();
    }

    private SendDocumentHandler CreateHandler() =>
        new(_db, _audit, _email, _handoff, _user, _config, NullLogger<SendDocumentHandler>.Instance);

    private async Task<Guid> SeedDocAsync(params (string email, string name, int order, string level)[] recipients)
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "test-doc.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/x.pdf",
            Sha256Hash = "deadbeef",
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Uploaded
        });
        foreach (var r in recipients)
        {
            _db.Recipients.Add(new Recipient
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                Email = r.email,
                Name = r.name,
                Order = r.order,
                Level = r.level,
                Status = RecipientStatus.Pending
            });
        }
        _db.SignatureFields.Add(new SignatureField
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Page = 1,
            X = 10, Y = 10, Width = 100, Height = 30
        });
        await _db.SaveChangesAsync();
        return docId;
    }

    [Fact]
    public async Task Handle_ThreeRecipients_OnlyOrderOneNotified()
    {
        _user.Email.Returns("sender@verasign.demo");
        var docId = await SeedDocAsync(
            ("toma.iliescu@verasign.demo", "Toma Iliescu", 1, "QES"),
            ("thea.popescu@verasign.demo", "Thea Popescu", 2, "AdES"),
            ("admin@verasign.demo",        "VeraSign Demo Admin", 3, "SES"));

        var response = await CreateHandler().Handle(new SendDocumentCommand(docId), CancellationToken.None);

        var recipients = await _db.Recipients.Where(r => r.DocumentId == docId).OrderBy(r => r.Order).ToListAsync();
        Assert.Equal(RecipientStatus.Notified, recipients[0].Status);
        Assert.NotNull(recipients[0].NotifiedAt);
        Assert.Equal(RecipientStatus.Pending, recipients[1].Status);
        Assert.Null(recipients[1].NotifiedAt);
        Assert.Equal(RecipientStatus.Pending, recipients[2].Status);

        Assert.False(response.AutoStart);
        Assert.Null(response.SigningRequestId);
        Assert.Equal(3, response.RecipientCount);
        Assert.Equal(nameof(DocumentStatus.Awaiting), response.Status);
    }

    [Fact]
    public async Task Handle_CreatesExactlyOneSigningRequest_ForOrderOne()
    {
        _user.Email.Returns("sender@verasign.demo");
        var docId = await SeedDocAsync(
            ("a@x.test", "A", 1, "QES"),
            ("b@x.test", "B", 2, "QES"),
            ("c@x.test", "C", 3, "QES"));

        await CreateHandler().Handle(new SendDocumentCommand(docId), CancellationToken.None);

        var sigReqs = await _db.SigningRequests.Where(sr => sr.DocumentId == docId).ToListAsync();
        Assert.Single(sigReqs);
        Assert.Equal(SigningRequestStatus.Pending, sigReqs[0].Status);
        Assert.Equal(1, sigReqs[0].OrderIndex);

        var firstRecipient = await _db.Recipients.Where(r => r.DocumentId == docId).OrderBy(r => r.Order).FirstAsync();
        Assert.Equal(firstRecipient.Id, sigReqs[0].RecipientId);
    }

    [Fact]
    public async Task Handle_SenderIsOrderOne_AutoStartsAndSkipsEmail()
    {
        _user.Email.Returns("self@verasign.demo");
        var docId = await SeedDocAsync(
            ("Self@VeraSign.Demo", "Self", 1, "QES"));

        var response = await CreateHandler().Handle(new SendDocumentCommand(docId), CancellationToken.None);

        Assert.True(response.AutoStart);
        Assert.NotNull(response.SigningRequestId);
        Assert.Empty(_email.Sent);
        Assert.DoesNotContain(_audit.Events, e => e.EventType == "EmailSent");
        Assert.Contains(_audit.Events, e => e.EventType == "SequentialSendStarted");
    }

    [Fact]
    public async Task Handle_SenderNotOrderOne_EmailsFirstRecipient_WithDeepLink()
    {
        _user.Email.Returns("sender@verasign.demo");
        var docId = await SeedDocAsync(
            ("toma.iliescu@verasign.demo", "Toma Iliescu", 1, "QES"),
            ("thea.popescu@verasign.demo", "Thea Popescu", 2, "QES"));

        await CreateHandler().Handle(new SendDocumentCommand(docId), CancellationToken.None);

        var sent = Assert.Single(_email.Sent);
        Assert.Equal("toma.iliescu@verasign.demo", sent.To);
        Assert.StartsWith($"verasign://sign?token=stub-token-", sent.DeepLink);
        Assert.Equal(docId, sent.DocumentId);
        Assert.Contains("Toma Iliescu", sent.BodyMarkdown);
        Assert.Contains(_audit.Events, e => e.EventType == "SequentialSendStarted");
    }

    [Fact]
    public async Task Handle_NoRecipients_ThrowsInvalidOperation()
    {
        _user.Email.Returns("sender@verasign.demo");
        var docId = await SeedDocAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(new SendDocumentCommand(docId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoFields_ThrowsInvalidOperation()
    {
        _user.Email.Returns("sender@verasign.demo");
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "x.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/x.pdf",
            Sha256Hash = "deadbeef",
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Uploaded
        });
        _db.Recipients.Add(new Recipient { Id = Guid.NewGuid(), DocumentId = docId, Email = "a@x", Name = "A", Order = 1, Level = "QES" });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(new SendDocumentCommand(docId), CancellationToken.None));
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
        public string Issue(Guid recipientId, Guid documentId) => $"stub-token-{recipientId:N}-{documentId:N}";
        public HandoffClaims? Validate(string token) => null;
    }
}
