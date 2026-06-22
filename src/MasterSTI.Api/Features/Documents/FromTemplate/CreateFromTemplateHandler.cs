using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.FromTemplate;

public sealed class CreateFromTemplateHandler : IRequestHandler<CreateFromTemplateCommand, DocumentFromTemplateResponse>
{
    private readonly AppDbContext _db;
    private readonly DocumentStorage _storage;
    private readonly IAuditWriter _audit;
    private readonly ICurrentUserAccessor _user;
    private readonly ILogger<CreateFromTemplateHandler> _logger;

    public CreateFromTemplateHandler(
        AppDbContext db,
        DocumentStorage storage,
        IAuditWriter audit,
        ICurrentUserAccessor user,
        ILogger<CreateFromTemplateHandler> logger)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _user = user;
        _logger = logger;
    }

    public async Task<DocumentFromTemplateResponse> Handle(CreateFromTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await _db.Templates.FirstOrDefaultAsync(
            t => t.Id == request.TemplateId && !t.IsDeleted,
            cancellationToken);
        if (template is null)
            throw new KeyNotFoundException($"Template {request.TemplateId} not found.");

        if (string.IsNullOrEmpty(template.PdfPath) || !File.Exists(template.PdfPath))
            throw new InvalidOperationException($"Template {template.Id} has no associated PDF on disk.");

        var newId = Guid.NewGuid();
        var destPath = Path.Combine(_storage.UploadsRoot, $"{newId}.pdf");

        await using (var src = File.OpenRead(template.PdfPath))
        await using (var dst = File.Create(destPath))
        {
            await src.CopyToAsync(dst, cancellationToken);
        }

        // Compute SHA-256 of the new copy so the standard validation report works downstream.
        var bytes = await File.ReadAllBytesAsync(destPath, cancellationToken);
        var sha256 = HashingService.ComputeSha256(bytes);

        var newTitle = $"{template.Title} (din șablon)";
        var orgId = _user.OrganizationId ?? template.OrganizationId;

        var document = new Document
        {
            Id = newId,
            FileName = $"{template.Title}.pdf",
            ContentType = "application/pdf",
            StoragePath = destPath,
            Sha256Hash = sha256,
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Uploaded,
            OrganizationId = orgId,
            OwnerUserId = _user.UserId
        };
        _db.Documents.Add(document);

        // Bump usage count on the template so the dashboard reflects real activity.
        template.UsageCount += 1;
        template.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(
            newId,
            "DocumentCreatedFromTemplate",
            $"{{\"templateId\":\"{template.Id}\",\"templateTitle\":\"{Escape(template.Title)}\"}}",
            cancellationToken);

        _logger.LogInformation("Document {DocumentId} created from template {TemplateId}", newId, template.Id);

        return new DocumentFromTemplateResponse(newId, newTitle);
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
