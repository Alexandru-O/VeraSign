using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Templates.Common;
using MasterSTI.Shared.DTOs.Templates;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Templates.List;

public sealed class ListTemplatesHandler : IRequestHandler<ListTemplatesQuery, IReadOnlyList<TemplateDto>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public ListTemplatesHandler(AppDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task<IReadOnlyList<TemplateDto>> Handle(ListTemplatesQuery request, CancellationToken cancellationToken)
    {
        var orgId = _user.OrganizationId;

        var query = _db.Templates.AsNoTracking().Where(t => !t.IsDeleted);
        if (orgId is not null)
            query = query.Where(t => t.OrganizationId == orgId);

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            var parsed = TemplateMapping.ParseCategory(request.Category);
            query = query.Where(t => t.Category == parsed);
        }

        var rows = await query
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(t => t.ToDto()).ToList();
    }
}
