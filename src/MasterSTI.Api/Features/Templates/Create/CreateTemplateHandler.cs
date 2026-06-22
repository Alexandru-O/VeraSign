using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Templates.Common;
using MasterSTI.Shared.DTOs.Templates;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Templates.Create;

public sealed class CreateTemplateHandler : IRequestHandler<CreateTemplateCommand, TemplateDto>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly ILogger<CreateTemplateHandler> _logger;

    public CreateTemplateHandler(
        AppDbContext db,
        ICurrentUserAccessor user,
        ILogger<CreateTemplateHandler> logger)
    {
        _db = db;
        _user = user;
        _logger = logger;
    }

    public async Task<TemplateDto> Handle(CreateTemplateCommand request, CancellationToken cancellationToken)
    {
        var orgId = _user.OrganizationId
            ?? DbInitializer.SeedOrganizationId; // dev/anonymous fallback

        string? pdfPath = null;
        if (request.FromDocumentId is { } docId)
        {
            var doc = await _db.Documents.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == docId, cancellationToken)
                ?? throw new KeyNotFoundException($"Document {docId} not found");
            pdfPath = doc.StoragePath;
        }

        var template = new Template
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Title = request.Title.Trim(),
            Description = request.Description,
            Category = TemplateMapping.ParseCategory(request.Category),
            PdfPath = pdfPath,
            FieldsJson = request.FieldsJson,
            DefaultLevel = request.DefaultLevel,
            UsageCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        _db.Templates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Template created: {TemplateId} for Org {OrgId}", template.Id, orgId);

        return template.ToDto();
    }
}
