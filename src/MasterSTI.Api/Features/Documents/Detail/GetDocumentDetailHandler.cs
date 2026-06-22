using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.Detail;

public sealed class GetDocumentDetailHandler : IRequestHandler<GetDocumentDetailQuery, DocumentDetailDto?>
{
    private readonly AppDbContext _db;

    public GetDocumentDetailHandler(AppDbContext db) => _db = db;

    public async Task<DocumentDetailDto?> Handle(GetDocumentDetailQuery request, CancellationToken cancellationToken)
    {
        var doc = await _db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken);
        if (doc is null) return null;

        string? senderName = null;
        if (doc.OwnerUserId is { } ownerId)
        {
            senderName = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == ownerId)
                .Select(u => u.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var recipients = await _db.Recipients
            .AsNoTracking()
            .Where(r => r.DocumentId == request.DocumentId)
            .OrderBy(r => r.Order)
            .Select(r => new RecipientDto(
                r.Id, r.Email, r.Name, r.Order, r.Level,
                r.Status.ToString(), r.NotifiedAt, r.SignedAt))
            .ToListAsync(cancellationToken);

        var rawSigned = await _db.SignedDocuments
            .AsNoTracking()
            .Where(s => s.OriginalDocumentId == request.DocumentId)
            .Select(s => new ChainRow(
                s.Id,
                s.PreviousSignedDocumentId,
                s.SignedAt,
                s.PadesLevel,
                s.IsFinal,
                s.Recipient != null ? s.Recipient.Name : string.Empty))
            .ToListAsync(cancellationToken);

        var ordered = OrderChain(rawSigned);
        var stages = ordered
            .Select((s, i) => new SignedStageDto(
                s.Id,
                i + 1,
                string.IsNullOrWhiteSpace(s.RecipientName) ? "—" : s.RecipientName,
                s.SignedAt,
                s.PadesLevel,
                s.IsFinal))
            .ToList();

        var tailSignedId = ordered.Count > 0 ? ordered[^1].Id : (Guid?)null;
        var level = recipients.FirstOrDefault()?.Level ?? "AdES";

        return new DocumentDetailDto(
            doc.Id,
            doc.FileName,
            doc.Status.ToString(),
            level,
            doc.UploadedAt,
            senderName,
            tailSignedId,
            recipients,
            stages);
    }

    private static List<ChainRow> OrderChain(IReadOnlyList<ChainRow> rows)
    {
        if (rows.Count == 0) return new List<ChainRow>();
        var byPrev = rows
            .GroupBy(r => r.PreviousSignedDocumentId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.First());
        var ordered = new List<ChainRow>(rows.Count);
        if (!byPrev.TryGetValue(Guid.Empty, out var current))
        {
            // Chain origin missing — fall back to chronological order.
            return rows.OrderBy(r => r.SignedAt).ToList();
        }
        var seen = new HashSet<Guid>();
        while (current is not null && seen.Add(current.Id))
        {
            ordered.Add(current);
            byPrev.TryGetValue(current.Id, out current);
        }
        foreach (var r in rows.Where(r => !seen.Contains(r.Id)).OrderBy(r => r.SignedAt))
            ordered.Add(r);
        return ordered;
    }

    public sealed record ChainRow(
        Guid Id,
        Guid? PreviousSignedDocumentId,
        DateTime SignedAt,
        string PadesLevel,
        bool IsFinal,
        string RecipientName);
}
