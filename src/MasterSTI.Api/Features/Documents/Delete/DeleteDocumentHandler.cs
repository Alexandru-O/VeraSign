using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Caching;
using MasterSTI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.Delete;

/// <summary>
/// Hard-deletes a Document plus its children (Recipients, SignatureFields,
/// AuditEvents, SigningRequests, SignedDocument) and removes the on-disk
/// PDF artefacts. Blocked while the document is mid-flight
/// (<see cref="DocumentStatus.Preparing"/> / <see cref="DocumentStatus.Signing"/>)
/// to avoid yanking storage out from under an active CSC/PAdES round-trip.
/// </summary>
public sealed class DeleteDocumentHandler : IRequestHandler<DeleteDocumentCommand, DeleteDocumentResponse>
{
    private readonly AppDbContext _db;
    private readonly ILogger<DeleteDocumentHandler> _logger;
    private readonly IDashboardCacheInvalidator? _dashCache;
    private readonly IAuditWriter? _audit;

    public DeleteDocumentHandler(
        AppDbContext db,
        ILogger<DeleteDocumentHandler> logger,
        IDashboardCacheInvalidator? dashCache = null,
        IAuditWriter? audit = null)
    {
        _db = db;
        _logger = logger;
        _dashCache = dashCache;
        _audit = audit;
    }

    public async Task<DeleteDocumentResponse> Handle(DeleteDocumentCommand request, CancellationToken cancellationToken)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {request.DocumentId} not found");

        if (doc.Status is DocumentStatus.Preparing or DocumentStatus.Signing)
            throw new InvalidOperationException(
                $"Document is in status {doc.Status} — cannot delete a document mid-flight.");

        var signedPaths = await _db.SignedDocuments
            .Where(s => s.OriginalDocumentId == doc.Id)
            .Select(s => s.StoragePath)
            .ToListAsync(cancellationToken);

        var preparedPaths = await _db.SigningRequests
            .Where(r => r.DocumentId == doc.Id)
            .Select(r => r.PreparedStoragePath)
            .ToListAsync(cancellationToken);

        var isInMemory = _db.Database.ProviderName?.Contains("InMemory") == true;

        if (isInMemory)
        {
            await DeleteViaChangeTrackerAsync(doc, cancellationToken);
        }
        else
        {
            await DeleteViaExecuteAsync(doc.Id, cancellationToken);
        }

        TryDelete(doc.StoragePath);
        foreach (var p in signedPaths) TryDelete(p);
        foreach (var p in preparedPaths) TryDelete(p);

        _dashCache?.InvalidateOrg(doc.OrganizationId);

        if (_audit is not null)
        {
            var metadata =
                $"{{\"fileName\":{System.Text.Json.JsonSerializer.Serialize(doc.FileName)}," +
                $"\"status\":\"{doc.Status}\"," +
                $"\"signedRows\":{signedPaths.Count}," +
                $"\"requestRows\":{preparedPaths.Count}}}";
            await _audit.WriteAsync(doc.Id, "Deleted", metadata, cancellationToken);
        }

        _logger.LogInformation(
            "Document deleted: {DocumentId} (status={Status}, fileName={FileName}, signedRows={Signed}, requestRows={Requests})",
            doc.Id, doc.Status, doc.FileName, signedPaths.Count, preparedPaths.Count);

        return new DeleteDocumentResponse(doc.Id);
    }

    /// <summary>
    /// Relational path. Uses <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> to bypass EF
    /// Core's change tracker, which otherwise raises "severed required relationship"
    /// for the NOT NULL + Restrict FK <c>SigningRequest.RecipientId → Recipient</c>
    /// when both sides are removed in one SaveChanges. Ordering reflects FK
    /// dependencies: SignedDocument → SigningRequest → Recipient. The self-ref
    /// <c>SignedDocument.PreviousSignedDocumentId</c> is nulled first so the
    /// row-set can be deleted in a single statement without violating Restrict.
    /// </summary>
    private async Task DeleteViaExecuteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        await _db.SignedDocuments
            .Where(s => s.OriginalDocumentId == documentId && s.PreviousSignedDocumentId != null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.PreviousSignedDocumentId, (Guid?)null),
                cancellationToken);

        await _db.SignedDocuments
            .Where(s => s.OriginalDocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.SigningRequests
            .Where(r => r.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.Recipients
            .Where(r => r.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.SignatureFields
            .Where(f => f.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.AuditEvents
            .Where(a => a.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.Documents
            .Where(d => d.Id == documentId)
            .ExecuteDeleteAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// In-memory provider path. <c>ExecuteDelete</c> is not supported there, so
    /// we fall back to change-tracker removal. The required-relationship trap
    /// does not bite InMemory because it does not enforce FK constraints.
    /// </summary>
    private async Task DeleteViaChangeTrackerAsync(Document doc, CancellationToken cancellationToken)
    {
        var signed     = await _db.SignedDocuments.Where(s => s.OriginalDocumentId == doc.Id).ToListAsync(cancellationToken);
        var requests   = await _db.SigningRequests.Where(r => r.DocumentId == doc.Id).ToListAsync(cancellationToken);
        var recipients = await _db.Recipients.Where(r => r.DocumentId == doc.Id).ToListAsync(cancellationToken);
        var fields     = await _db.SignatureFields.Where(f => f.DocumentId == doc.Id).ToListAsync(cancellationToken);
        var audits     = await _db.AuditEvents.Where(a => a.DocumentId == doc.Id).ToListAsync(cancellationToken);

        _db.SignedDocuments.RemoveRange(signed);
        _db.SigningRequests.RemoveRange(requests);
        _db.Recipients.RemoveRange(recipients);
        _db.SignatureFields.RemoveRange(fields);
        _db.AuditEvents.RemoveRange(audits);
        _db.Documents.Remove(doc);

        await _db.SaveChangesAsync(cancellationToken);
    }

    private void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete storage artefact at {Path}", path);
        }
    }
}
