using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.Fields;

public sealed class GetFieldsHandler : IRequestHandler<GetFieldsQuery, IReadOnlyList<SignatureFieldDto>>
{
    private readonly AppDbContext _db;

    public GetFieldsHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SignatureFieldDto>> Handle(GetFieldsQuery request, CancellationToken cancellationToken)
    {
        var rows = await _db.SignatureFields
            .AsNoTracking()
            .Where(f => f.DocumentId == request.DocumentId)
            .OrderBy(f => f.Page).ThenBy(f => f.Y).ThenBy(f => f.X)
            .ToListAsync(cancellationToken);

        var orderByRecipient = await _db.Recipients
            .AsNoTracking()
            .Where(r => r.DocumentId == request.DocumentId)
            .Select(r => new { r.Id, r.Order })
            .ToDictionaryAsync(r => r.Id, r => r.Order, cancellationToken);

        return rows.Select(f => new SignatureFieldDto(
            f.Id, f.Type, f.Page, f.X, f.Y, f.Width, f.Height, f.RecipientId,
            f.RecipientId.HasValue && orderByRecipient.TryGetValue(f.RecipientId.Value, out var ord) ? ord : null)).ToList();
    }
}
