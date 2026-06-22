using System.Text.Json;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Email;
using MasterSTI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MasterSTI.Api.Features.Documents.Remind;

/// <summary>
/// Re-sends the signing nudge to the recipient whose turn it currently is —
/// the one in <see cref="RecipientStatus.Notified"/>. Earlier recipients have
/// already signed, later ones have not yet been notified, so a "reminder"
/// targets exactly one row. Only documents in
/// <see cref="DocumentStatus.Awaiting"/> can be reminded; anything else
/// returns Conflict.
/// </summary>
public sealed class RemindDocumentHandler : IRequestHandler<RemindDocumentCommand, RemindDocumentResponse>
{
    private readonly AppDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IEmailSender _email;
    private readonly IHandoffTokenService _handoff;
    private readonly IConfiguration _config;
    private readonly ILogger<RemindDocumentHandler> _logger;

    public RemindDocumentHandler(
        AppDbContext db,
        IAuditWriter audit,
        IEmailSender email,
        IHandoffTokenService handoff,
        IConfiguration config,
        ILogger<RemindDocumentHandler> logger)
    {
        _db = db;
        _audit = audit;
        _email = email;
        _handoff = handoff;
        _config = config;
        _logger = logger;
    }

    public async Task<RemindDocumentResponse> Handle(RemindDocumentCommand request, CancellationToken cancellationToken)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {request.DocumentId} not found");

        if (doc.Status is not DocumentStatus.Awaiting)
            throw new InvalidOperationException(
                $"Document is in status {doc.Status} — only Awaiting documents can be reminded.");

        var current = await _db.Recipients
            .Where(r => r.DocumentId == request.DocumentId && r.Status == RecipientStatus.Notified)
            .OrderBy(r => r.Order)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No recipient is currently awaiting signature.");

        var now = DateTime.UtcNow;
        current.NotifiedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        var token = _handoff.Issue(current.Id, doc.Id);
        var webBase = _config.GetValue<string>("Web:PublicBaseUrl") ?? "https://localhost:7165";
        var deepLink = $"verasign://sign?token={token}";
        var fallback = $"{webBase.TrimEnd('/')}/handoff?t={token}";
        var subject = $"VeraSign · Reamintire: semnează „{doc.FileName}\"";
        var body = $"Salut {current.Name},\n\nTe rugăm să semnezi documentul „{doc.FileName}\".\n\n" +
                   $"Deschide în wallet: {deepLink}\nSau prin browser: {fallback}\n";

        await _email.SendAsync(new EmailMessage(doc.Id, current.Email, subject, body, deepLink), cancellationToken);

        await _audit.WriteAsync(
            request.DocumentId,
            "Reminded",
            JsonSerializer.Serialize(new { recipientId = current.Id, order = current.Order }),
            cancellationToken);

        return new RemindDocumentResponse(request.DocumentId, 1, now);
    }
}
