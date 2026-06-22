using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Templates.Common;
using MasterSTI.Shared.DTOs.Templates;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Templates.Get;

public sealed class GetTemplateHandler : IRequestHandler<GetTemplateQuery, TemplateDto>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public GetTemplateHandler(AppDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task<TemplateDto> Handle(GetTemplateQuery request, CancellationToken cancellationToken)
    {
        var orgId = _user.OrganizationId;

        var template = await _db.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Template {request.Id} not found");

        if (orgId is not null && template.OrganizationId != orgId)
            throw new UnauthorizedAccessException("Template does not belong to your organization.");

        return template.ToDto();
    }
}
