using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Templates;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Templates.Common;
using MasterSTI.Shared.DTOs.Templates;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Templates.UpdateContent;

public sealed class UpdateTemplateContentHandler : IRequestHandler<UpdateTemplateContentCommand, TemplateDto>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly TemplatePdfRenderer _renderer;
    private readonly TemplateStoragePaths _paths;
    private readonly IAuditWriter? _audit;
    private readonly ILogger<UpdateTemplateContentHandler> _logger;

    public UpdateTemplateContentHandler(
        AppDbContext db,
        ICurrentUserAccessor user,
        TemplatePdfRenderer renderer,
        TemplateStoragePaths paths,
        ILogger<UpdateTemplateContentHandler> logger,
        IAuditWriter? audit = null)
    {
        _db = db;
        _user = user;
        _renderer = renderer;
        _paths = paths;
        _logger = logger;
        _audit = audit;
    }

    public async Task<TemplateDto> Handle(UpdateTemplateContentCommand request, CancellationToken cancellationToken)
    {
        var orgId = _user.OrganizationId;

        var template = await _db.Templates
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Template {request.Id} not found");

        if (orgId is not null && template.OrganizationId != orgId)
            throw new UnauthorizedAccessException("Template does not belong to your organization.");

        // Resolve target path. If the existing path is missing or unsafe, fall back to a canonical {id}.pdf.
        string targetPath;
        if (!string.IsNullOrEmpty(template.PdfPath))
        {
            try
            {
                targetPath = _paths.ValidateInsideTemplatesRoot(template.PdfPath);
            }
            catch (InvalidOperationException)
            {
                _logger.LogWarning("Template {TemplateId} had unsafe PdfPath {Path}; falling back to canonical path.", template.Id, template.PdfPath);
                targetPath = _paths.DefaultPathForId(template.Id);
            }
        }
        else
        {
            targetPath = _paths.DefaultPathForId(template.Id);
        }

        // Atomic write: render to temp file, then File.Move overwrite.
        var tempPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var bytes = _renderer.Render(template.Title, request.BodyMarkdown);
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);

            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* swallow */ }
            }
            throw;
        }

        template.BodyMarkdown = request.BodyMarkdown;
        template.PdfPath = targetPath;
        template.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        if (_audit is not null)
            await _audit.WriteAsync(template.Id, "TemplateContentUpdated",
                $"{{\"length\":{request.BodyMarkdown.Length}}}", cancellationToken);

        _logger.LogInformation("Template content updated: {TemplateId}", template.Id);
        return template.ToDto();
    }
}
