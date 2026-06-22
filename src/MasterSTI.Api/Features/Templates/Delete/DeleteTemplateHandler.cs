using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Templates.Delete;

public sealed class DeleteTemplateHandler : IRequestHandler<DeleteTemplateCommand>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly ILogger<DeleteTemplateHandler> _logger;

    public DeleteTemplateHandler(
        AppDbContext db,
        ICurrentUserAccessor user,
        ILogger<DeleteTemplateHandler> logger)
    {
        _db = db;
        _user = user;
        _logger = logger;
    }

    public async Task Handle(DeleteTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await _db.Templates
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Template {request.Id} not found");

        var orgId = _user.OrganizationId;
        if (orgId is not null && template.OrganizationId != orgId)
            throw new UnauthorizedAccessException("Template does not belong to your organization.");

        template.IsDeleted = true;
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Template soft-deleted: {TemplateId}", template.Id);
    }
}
