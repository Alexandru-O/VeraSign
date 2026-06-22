using System.Text;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Templates;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Templates.Common;
using MasterSTI.Shared.DTOs.Templates;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Templates.ReplacePdf;

public sealed class ReplaceTemplatePdfHandler : IRequestHandler<ReplaceTemplatePdfCommand, TemplateDto>
{
    private static readonly byte[] PdfMagic = Encoding.ASCII.GetBytes("%PDF-");

    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly TemplateStoragePaths _paths;
    private readonly IAuditWriter? _audit;
    private readonly ILogger<ReplaceTemplatePdfHandler> _logger;

    public ReplaceTemplatePdfHandler(
        AppDbContext db,
        ICurrentUserAccessor user,
        TemplateStoragePaths paths,
        ILogger<ReplaceTemplatePdfHandler> logger,
        IAuditWriter? audit = null)
    {
        _db = db;
        _user = user;
        _paths = paths;
        _logger = logger;
        _audit = audit;
    }

    public async Task<TemplateDto> Handle(ReplaceTemplatePdfCommand request, CancellationToken cancellationToken)
    {
        var orgId = _user.OrganizationId;

        var template = await _db.Templates
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Template {request.Id} not found");

        if (orgId is not null && template.OrganizationId != orgId)
            throw new UnauthorizedAccessException("Template does not belong to your organization.");

        // Read + magic-byte check.
        using var ms = new MemoryStream();
        await request.File.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        if (!HasPdfMagic(bytes))
            throw new InvalidTemplatePdfException("File does not start with %PDF- magic bytes — rejected.");

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

        // Atomic write: write to temp file, then File.Move overwrite.
        var tempPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
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

        template.PdfPath = targetPath;
        template.BodyMarkdown = null; // file is now authoritative
        template.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        if (_audit is not null)
            await _audit.WriteAsync(template.Id, "TemplatePdfReplaced",
                $"{{\"size\":{bytes.Length}}}", cancellationToken);

        _logger.LogInformation("Template PDF replaced: {TemplateId} ({Size} bytes)", template.Id, bytes.Length);
        return template.ToDto();
    }

    private static bool HasPdfMagic(byte[] bytes)
    {
        if (bytes.Length < PdfMagic.Length) return false;
        for (var i = 0; i < PdfMagic.Length; i++)
            if (bytes[i] != PdfMagic[i]) return false;
        return true;
    }
}

public sealed class InvalidTemplatePdfException : Exception
{
    public InvalidTemplatePdfException(string message) : base(message) { }
}
