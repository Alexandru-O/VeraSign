using System.Text;
using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Caching;
using MasterSTI.Api.Common.Realtime;
using MasterSTI.Api.Data;
using MediatR;

namespace MasterSTI.Api.Features.Documents.Upload;

public class UploadDocumentHandler : IRequestHandler<UploadDocumentCommand, UploadDocumentResponse>
{
    private static readonly byte[] PdfMagic = Encoding.ASCII.GetBytes("%PDF-");

    private readonly AppDbContext _db;
    private readonly DocumentStorage _storage;
    private readonly IAuditWriter _audit;
    private readonly ICurrentUserAccessor _user;
    private readonly ILogger<UploadDocumentHandler> _logger;
    private readonly IDashboardCacheInvalidator? _dashCache;
    private readonly IDashboardNotifier? _notifier;

    public UploadDocumentHandler(
        AppDbContext db,
        DocumentStorage storage,
        IAuditWriter audit,
        ICurrentUserAccessor user,
        ILogger<UploadDocumentHandler> logger,
        IDashboardCacheInvalidator? dashCache = null,
        IDashboardNotifier? notifier = null)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _user = user;
        _logger = logger;
        _dashCache = dashCache;
        _notifier = notifier;
    }

    public async Task<UploadDocumentResponse> Handle(
        UploadDocumentCommand request,
        CancellationToken cancellationToken)
    {
        var file = request.File;
        var id = Guid.NewGuid();

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        if (!HasPdfMagic(bytes))
            throw new InvalidFileFormatException("File does not start with %PDF- magic bytes — rejected.");

        var hash = HashingService.ComputeSha256(bytes);

        var path = Path.Combine(_storage.UploadsRoot, $"{id}.pdf");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);

        var document = new Document
        {
            Id = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = "application/pdf",
            StoragePath = path,
            Sha256Hash = hash,
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Uploaded,
            OrganizationId = _user.OrganizationId,
            OwnerUserId = _user.UserId
        };

        _db.Documents.Add(document);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(id, "Uploaded", $"{{\"size\":{bytes.Length},\"sha256\":\"{hash}\"}}", cancellationToken);
        _dashCache?.InvalidateOrg(_user.OrganizationId);
        if (_notifier is not null)
            await _notifier.NotifyOrgAsync(_user.OrganizationId, cancellationToken);

        _logger.LogInformation("Document uploaded: {DocumentId}", id);

        return new UploadDocumentResponse(id, document.FileName, hash);
    }

    private static bool HasPdfMagic(byte[] bytes)
    {
        if (bytes.Length < PdfMagic.Length) return false;
        for (var i = 0; i < PdfMagic.Length; i++)
            if (bytes[i] != PdfMagic[i]) return false;
        return true;
    }
}

public sealed class InvalidFileFormatException : Exception
{
    public InvalidFileFormatException(string message) : base(message) { }
}
