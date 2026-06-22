using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Templates.Common;
using MasterSTI.Shared.DTOs.Templates;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Templates.Update;

public sealed class UpdateTemplateHandler : IRequestHandler<UpdateTemplateCommand, TemplateDto>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public UpdateTemplateHandler(AppDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task<TemplateDto> Handle(UpdateTemplateCommand request, CancellationToken cancellationToken)
    {
        var orgId = _user.OrganizationId;

        var template = await _db.Templates
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Template {request.Id} not found");

        if (orgId is not null && template.OrganizationId != orgId)
            throw new UnauthorizedAccessException("Template does not belong to your organization.");

        template.Title = request.Title.Trim();
        template.Description = request.Description;
        template.Category = TemplateMapping.ParseCategory(request.Category);
        template.FieldsJson = request.FieldsJson;
        template.DefaultLevel = request.DefaultLevel;
        template.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return template.ToDto();
    }
}
