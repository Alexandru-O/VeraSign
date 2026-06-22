using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Audit;
using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Audit;

/// <summary>
/// Cross-document audit feed, scoped to the caller's organization. Joins AuditEvents to
/// Document for filename context and applies a sliding time window so the Settings · Audit
/// tab can render the last N days without loading everything. Read-only — write path stays
/// in <see cref="MasterSTI.Api.Common.Audit.IAuditWriter"/>.
/// </summary>
public sealed class ListAuditHandler : IRequestHandler<ListAuditQuery, PagedResultDto<AuditEventListItemDto>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public ListAuditHandler(AppDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task<PagedResultDto<AuditEventListItemDto>> Handle(ListAuditQuery request, CancellationToken cancellationToken)
    {
        var orgId = _user.OrganizationId;

        var since = request.Period switch
        {
            "today" => DateTime.UtcNow.Date,
            "7d"    => DateTime.UtcNow.AddDays(-7),
            "30d"   => DateTime.UtcNow.AddDays(-30),
            "all"   => DateTime.MinValue,
            _       => DateTime.UtcNow.AddDays(-7)
        };

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 10, 200);

        var q = _db.AuditEvents
            .AsNoTracking()
            .Include(a => a.Document)
            .Where(a => a.Timestamp >= since);

        if (orgId is not null)
            q = q.Where(a => a.Document == null || a.Document.OrganizationId == orgId);

        if (!string.IsNullOrWhiteSpace(request.EventType))
            q = q.Where(a => a.EventType == request.EventType);

        var total = await q.CountAsync(cancellationToken);

        var rows = await q
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditEventListItemDto(
                a.Id,
                a.DocumentId,
                a.Document != null ? a.Document.FileName : null,
                a.EventType,
                a.Actor,
                a.Timestamp,
                a.Metadata))
            .ToListAsync(cancellationToken);

        return new PagedResultDto<AuditEventListItemDto>(rows, page, pageSize, total);
    }
}
