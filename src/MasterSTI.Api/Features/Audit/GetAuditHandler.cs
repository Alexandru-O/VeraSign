using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Audit;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Audit;

public sealed class GetAuditHandler : IRequestHandler<GetAuditQuery, IReadOnlyList<AuditEventDto>>
{
    private readonly AppDbContext _db;

    public GetAuditHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AuditEventDto>> Handle(GetAuditQuery request, CancellationToken cancellationToken)
    {
        var rows = await _db.AuditEvents
            .AsNoTracking()
            .Where(a => a.DocumentId == request.DocumentId)
            .OrderBy(a => a.Timestamp)
            .ToListAsync(cancellationToken);

        return rows.Select(a => new AuditEventDto(
            a.Id, a.DocumentId, a.EventType, a.Actor,
            a.IpAddress, a.UserAgent, a.Timestamp, a.Metadata)).ToList();
    }
}
