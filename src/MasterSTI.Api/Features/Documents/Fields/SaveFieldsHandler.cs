using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.Fields;

public sealed class SaveFieldsHandler : IRequestHandler<SaveFieldsCommand, IReadOnlyList<SignatureFieldDto>>
{
    private readonly AppDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<SaveFieldsHandler> _logger;

    public SaveFieldsHandler(
        AppDbContext db,
        IAuditWriter audit,
        ILogger<SaveFieldsHandler> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SignatureFieldDto>> Handle(SaveFieldsCommand request, CancellationToken cancellationToken)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {request.DocumentId} not found");

        if (doc.Status is DocumentStatus.Signed or DocumentStatus.Awaiting)
            throw new InvalidOperationException($"Document is in status {doc.Status} — fields cannot be modified.");

        var existing = await _db.SignatureFields
            .Where(f => f.DocumentId == request.DocumentId)
            .ToListAsync(cancellationToken);
        _db.SignatureFields.RemoveRange(existing);

        var newFields = request.Fields.Select(f => new SignatureField
        {
            Id = f.Id == Guid.Empty ? Guid.NewGuid() : f.Id,
            DocumentId = request.DocumentId,
            Type = f.Type,
            Page = f.Page,
            X = f.X,
            Y = f.Y,
            Width = f.Width,
            Height = f.Height,
            RecipientId = f.RecipientId
        }).ToList();

        _db.SignatureFields.AddRange(newFields);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(request.DocumentId, "FieldsAdded", $"{{\"count\":{newFields.Count}}}", cancellationToken);

        _logger.LogInformation("Saved {Count} fields for document {DocumentId}", newFields.Count, request.DocumentId);

        var orderByRecipient = await _db.Recipients
            .AsNoTracking()
            .Where(r => r.DocumentId == request.DocumentId)
            .Select(r => new { r.Id, r.Order })
            .ToDictionaryAsync(r => r.Id, r => r.Order, cancellationToken);

        return newFields.Select(f => new SignatureFieldDto(
            f.Id, f.Type, f.Page, f.X, f.Y, f.Width, f.Height, f.RecipientId,
            f.RecipientId.HasValue && orderByRecipient.TryGetValue(f.RecipientId.Value, out var ord) ? ord : null)).ToList();
    }
}
