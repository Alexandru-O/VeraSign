using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.Recipients;

public sealed class SaveRecipientsHandler : IRequestHandler<SaveRecipientsCommand, IReadOnlyList<RecipientDto>>
{
    private readonly AppDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<SaveRecipientsHandler> _logger;

    public SaveRecipientsHandler(
        AppDbContext db,
        IAuditWriter audit,
        ILogger<SaveRecipientsHandler> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RecipientDto>> Handle(SaveRecipientsCommand request, CancellationToken cancellationToken)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {request.DocumentId} not found");

        if (doc.Status is DocumentStatus.Awaiting or DocumentStatus.Signed)
            throw new InvalidOperationException($"Document is in status {doc.Status} — recipients cannot be modified.");

        var existing = await _db.Recipients
            .Where(r => r.DocumentId == request.DocumentId)
            .ToListAsync(cancellationToken);

        var inputIds = request.Recipients
            .Where(r => r.Id is not null && r.Id != Guid.Empty)
            .Select(r => r.Id!.Value)
            .ToHashSet();

        var toRemove = existing.Where(e => !inputIds.Contains(e.Id)).ToList();
        _db.Recipients.RemoveRange(toRemove);

        var saved = new List<Recipient>();
        foreach (var input in request.Recipients)
        {
            Recipient? entity = null;
            if (input.Id is { } existingId && existingId != Guid.Empty)
                entity = existing.FirstOrDefault(e => e.Id == existingId);

            if (entity is null)
            {
                entity = new Recipient
                {
                    Id = Guid.NewGuid(),
                    DocumentId = request.DocumentId,
                    Status = RecipientStatus.Pending
                };
                _db.Recipients.Add(entity);
            }

            entity.Email = input.Email.Trim();
            entity.Name = input.Name.Trim();
            entity.Order = input.Order;
            entity.Level = input.Level;
            saved.Add(entity);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(request.DocumentId, "RecipientsAdded", $"{{\"count\":{saved.Count}}}", cancellationToken);

        _logger.LogInformation("Saved {Count} recipients for document {DocumentId}", saved.Count, request.DocumentId);

        return saved
            .OrderBy(r => r.Order)
            .Select(r => new RecipientDto(
                r.Id, r.Email, r.Name, r.Order, r.Level,
                r.Status.ToString(), r.NotifiedAt, r.SignedAt))
            .ToList();
    }
}
