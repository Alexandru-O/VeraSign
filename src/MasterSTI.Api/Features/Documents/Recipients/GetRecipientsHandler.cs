using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.Recipients;

public sealed class GetRecipientsHandler : IRequestHandler<GetRecipientsQuery, IReadOnlyList<RecipientDto>>
{
    private readonly AppDbContext _db;

    public GetRecipientsHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<RecipientDto>> Handle(GetRecipientsQuery request, CancellationToken cancellationToken)
    {
        var rows = await _db.Recipients
            .AsNoTracking()
            .Where(r => r.DocumentId == request.DocumentId)
            .OrderBy(r => r.Order)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new RecipientDto(
            r.Id, r.Email, r.Name, r.Order, r.Level,
            r.Status.ToString(), r.NotifiedAt, r.SignedAt)).ToList();
    }
}
