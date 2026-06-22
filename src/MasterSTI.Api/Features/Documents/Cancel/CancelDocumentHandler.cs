using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Caching;
using MasterSTI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.Cancel;

/// <summary>
/// Cancels an in-flight signing request. Only documents in
/// <see cref="DocumentStatus.Awaiting"/> or <see cref="DocumentStatus.Uploaded"/>
/// can be cancelled — already-signed or already-failed flows are immutable.
/// Pending recipients are marked <see cref="RecipientStatus.Declined"/> so the
/// dashboard pipelines reflect the cancellation. The document itself moves to
/// <see cref="DocumentStatus.Cancelled"/>.
/// </summary>
public sealed class CancelDocumentHandler : IRequestHandler<CancelDocumentCommand, CancelDocumentResponse>
{
    private readonly AppDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<CancelDocumentHandler> _logger;
    private readonly IDashboardCacheInvalidator? _dashCache;

    public CancelDocumentHandler(
        AppDbContext db,
        IAuditWriter audit,
        ILogger<CancelDocumentHandler> logger,
        IDashboardCacheInvalidator? dashCache = null)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
        _dashCache = dashCache;
    }

    public async Task<CancelDocumentResponse> Handle(CancelDocumentCommand request, CancellationToken cancellationToken)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {request.DocumentId} not found");

        if (doc.Status is not (DocumentStatus.Awaiting or DocumentStatus.Uploaded))
            throw new InvalidOperationException(
                $"Document is in status {doc.Status} — cannot cancel a {doc.Status} document.");

        var pending = await _db.Recipients
            .Where(r => r.DocumentId == request.DocumentId
                     && (r.Status == RecipientStatus.Pending || r.Status == RecipientStatus.Notified))
            .ToListAsync(cancellationToken);

        foreach (var r in pending)
            r.Status = RecipientStatus.Declined;

        doc.Status = DocumentStatus.Cancelled;
        await _db.SaveChangesAsync(cancellationToken);

        var reasonPayload = string.IsNullOrWhiteSpace(request.Reason)
            ? "{}"
            : $"{{\"reason\":{System.Text.Json.JsonSerializer.Serialize(request.Reason)}}}";
        await _audit.WriteAsync(request.DocumentId, "Cancelled", reasonPayload, cancellationToken);
        _dashCache?.InvalidateOrg(doc.OrganizationId);

        _logger.LogInformation("Document cancelled: {DocumentId} (recipients declined: {Count})",
            request.DocumentId, pending.Count);

        return new CancelDocumentResponse(request.DocumentId, doc.Status.ToString());
    }
}
