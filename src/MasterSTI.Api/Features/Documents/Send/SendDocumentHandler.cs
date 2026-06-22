using System.Text.Json;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Caching;
using MasterSTI.Api.Common.Email;
using MasterSTI.Api.Common.Realtime;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MasterSTI.Api.Features.Documents.Send;

/// <summary>
/// Locks the document's fields + recipients and starts the sequential signing
/// chain. Only the <c>Order=1</c> recipient is flipped to
/// <see cref="RecipientStatus.Notified"/> and given a <see cref="SigningRequest"/>;
/// later recipients stay <see cref="RecipientStatus.Pending"/> until the
/// previous signer embeds (see <c>docs/adr/0001-lazy-signing-request.md</c>).
///
/// When the sender is also the <c>Order=1</c> recipient, the email step is
/// skipped and the response carries <c>AutoStart=true</c> + the
/// <see cref="SigningRequest.Id"/> so the UI can navigate the sender straight
/// into the wallet-auth flow.
/// </summary>
public sealed class SendDocumentHandler : IRequestHandler<SendDocumentCommand, SendDocumentResponse>
{
    private readonly AppDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IEmailSender _email;
    private readonly IHandoffTokenService _handoff;
    private readonly ICurrentUserAccessor _user;
    private readonly IConfiguration _config;
    private readonly ILogger<SendDocumentHandler> _logger;
    private readonly IDashboardCacheInvalidator? _dashCache;
    private readonly IDashboardNotifier? _notifier;

    public SendDocumentHandler(
        AppDbContext db,
        IAuditWriter audit,
        IEmailSender email,
        IHandoffTokenService handoff,
        ICurrentUserAccessor user,
        IConfiguration config,
        ILogger<SendDocumentHandler> logger,
        IDashboardCacheInvalidator? dashCache = null,
        IDashboardNotifier? notifier = null)
    {
        _db = db;
        _audit = audit;
        _email = email;
        _handoff = handoff;
        _user = user;
        _config = config;
        _logger = logger;
        _dashCache = dashCache;
        _notifier = notifier;
    }

    public async Task<SendDocumentResponse> Handle(SendDocumentCommand request, CancellationToken cancellationToken)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {request.DocumentId} not found");

        if (doc.Status is DocumentStatus.Signed or DocumentStatus.Failed or DocumentStatus.Cancelled)
            throw new InvalidOperationException($"Document is in status {doc.Status} — cannot send.");

        var recipients = await _db.Recipients
            .Where(r => r.DocumentId == request.DocumentId)
            .OrderBy(r => r.Order)
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
            throw new InvalidOperationException("Document has no recipients to notify.");

        var fieldCount = await _db.SignatureFields
            .CountAsync(f => f.DocumentId == request.DocumentId, cancellationToken);
        if (fieldCount == 0)
            throw new InvalidOperationException("Document has no signature fields to sign.");

        var first = recipients[0];
        var nowUtc = DateTime.UtcNow;

        first.Status = RecipientStatus.Notified;
        first.NotifiedAt = nowUtc;

        var signingRequest = new SigningRequest
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            RecipientId = first.Id,
            OrderIndex = first.Order,
            RequestedBy = _user.Email ?? "system",
            CredentialId = string.Empty,
            SignatureLevel = "PAdES-B-LT",
            DocumentHash = string.Empty,
            Status = SigningRequestStatus.Pending,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        };
        _db.SigningRequests.Add(signingRequest);

        doc.Status = DocumentStatus.Awaiting;

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(
            request.DocumentId,
            "SequentialSendStarted",
            JsonSerializer.Serialize(new
            {
                recipients = recipients.Count,
                fields = fieldCount,
                order = recipients.Select(r => new { r.Order, r.Email, r.Level }).ToArray()
            }),
            cancellationToken);

        var senderEmail = _user.Email;
        var senderIsFirst = !string.IsNullOrWhiteSpace(senderEmail)
            && string.Equals(senderEmail, first.Email, StringComparison.OrdinalIgnoreCase);

        if (!senderIsFirst)
        {
            var token = _handoff.Issue(first.Id, doc.Id);
            var webBase = _config.GetValue<string>("Web:PublicBaseUrl") ?? "https://localhost:7165";
            var deepLink = $"verasign://sign?token={token}";
            var fallback = $"{webBase.TrimEnd('/')}/handoff?t={token}";
            var subject = $"VeraSign · Trebuie să semnezi documentul „{doc.FileName}\"";
            var body = $"Salut {first.Name},\n\nEști semnatarul {first.Order} pentru documentul „{doc.FileName}\".\n\n" +
                       $"Deschide în wallet: {deepLink}\nSau prin browser: {fallback}\n";

            await _email.SendAsync(new EmailMessage(doc.Id, first.Email, subject, body, deepLink), cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "Sender {Email} is Order=1 recipient on {DocumentId} — skipping email, returning AutoStart",
                senderEmail, doc.Id);
        }

        _dashCache?.InvalidateOrg(doc.OrganizationId);
        if (_notifier is not null)
            await _notifier.NotifyOrgAsync(doc.OrganizationId, cancellationToken);

        return new SendDocumentResponse(
            doc.Id,
            doc.Status.ToString(),
            recipients.Count,
            AutoStart: senderIsFirst,
            SigningRequestId: senderIsFirst ? signingRequest.Id : null);
    }
}
