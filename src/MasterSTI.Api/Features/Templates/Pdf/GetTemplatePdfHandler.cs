using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Templates;
using MasterSTI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SysPath = System.IO.Path;

namespace MasterSTI.Api.Features.Templates.Pdf;

public sealed class GetTemplatePdfHandler : IRequestHandler<GetTemplatePdfQuery, GetTemplatePdfResult>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly TemplateStoragePaths _paths;
    private readonly ILogger<GetTemplatePdfHandler> _logger;

    public GetTemplatePdfHandler(
        AppDbContext db,
        ICurrentUserAccessor user,
        TemplateStoragePaths paths,
        ILogger<GetTemplatePdfHandler> logger)
    {
        _db = db;
        _user = user;
        _paths = paths;
        _logger = logger;
    }

    public async Task<GetTemplatePdfResult> Handle(GetTemplatePdfQuery request, CancellationToken cancellationToken)
    {
        var orgId = _user.OrganizationId;

        var template = await _db.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Template {request.Id} not found");

        if (orgId is not null && template.OrganizationId != orgId)
            throw new UnauthorizedAccessException("Template does not belong to your organization.");

        if (string.IsNullOrEmpty(template.PdfPath))
            throw new FileNotFoundException("Template has no PDF associated.");

        // Stored path may be stale (seeded with a different ContentRootPath, env moved
        // between CWDs, etc.). Try strict validation first; on failure, fall back to
        // current TemplatesRoot + original filename — matches UpdateTemplateContentHandler.
        string safePath;
        try
        {
            safePath = _paths.ValidateInsideTemplatesRoot(template.PdfPath);
        }
        catch (InvalidOperationException)
        {
            var fallback = SysPath.Combine(_paths.TemplatesRoot, SysPath.GetFileName(template.PdfPath));
            if (!File.Exists(fallback))
                throw new FileNotFoundException(
                    $"Template PDF path '{template.PdfPath}' is outside current templates root and fallback '{fallback}' is missing.");
            _logger.LogWarning("Template {TemplateId} PdfPath '{Stored}' outside current root; using '{Fallback}'.",
                template.Id, template.PdfPath, fallback);
            safePath = fallback;
        }

        if (!File.Exists(safePath))
            throw new FileNotFoundException($"Template PDF file missing on disk: {safePath}");

        var bytes = await File.ReadAllBytesAsync(safePath, cancellationToken);
        var fileName = SysPath.GetFileName(safePath);
        return new GetTemplatePdfResult(bytes, fileName);
    }
}
