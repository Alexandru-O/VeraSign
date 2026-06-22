using iText.Commons.Bouncycastle.Cert;
using iText.Kernel.Pdf;
using iText.Signatures;
using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Signing;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.SignedDocuments.GetInfo;

public sealed class GetSignedDocumentInfoHandler : IRequestHandler<GetSignedDocumentInfoQuery, GetSignedDocumentInfoResult>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly DocumentStorage? _storage;
    private readonly ILogger<GetSignedDocumentInfoHandler> _logger;

    public GetSignedDocumentInfoHandler(
        AppDbContext db,
        ICurrentUserAccessor user,
        ILogger<GetSignedDocumentInfoHandler> logger,
        DocumentStorage? storage = null)
    {
        _db = db;
        _user = user;
        _storage = storage;
        _logger = logger;
    }

    public async Task<GetSignedDocumentInfoResult> Handle(GetSignedDocumentInfoQuery request, CancellationToken cancellationToken)
    {
        var row = await _db.SignedDocuments
            .AsNoTracking()
            .Where(s => s.Id == request.SignedDocumentId)
            .Select(s => new
            {
                s.Id,
                s.OriginalDocumentId,
                s.SignedAt,
                s.PadesLevel,
                s.TimestampToken,
                s.StoragePath,
                OwnerUserId = s.OriginalDocument.OwnerUserId,
                RequestedLevel = (string?)s.Recipient.Level,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return GetSignedDocumentInfoResult.NotFound();

        if (!await IsAllowedAsync(row.OriginalDocumentId, row.OwnerUserId, cancellationToken))
            return GetSignedDocumentInfoResult.Forbidden();

        var tsaTime = RfcTimestampDecoder.TryDecodeGenTime(row.TimestampToken);
        var (subjectCn, tspName, pdfTsaTime, serial) = TryReadSignerInfo(row.StoragePath);

        var info = new SignedDocumentInfoDto(
            row.Id,
            DateTime.SpecifyKind(row.SignedAt, DateTimeKind.Utc),
            row.PadesLevel,
            tspName,
            subjectCn,
            tsaTime ?? pdfTsaTime,
            row.Id.ToString("N"),
            row.RequestedLevel,
            serial);

        return GetSignedDocumentInfoResult.Ok(info);
    }

    private async Task<bool> IsAllowedAsync(Guid documentId, Guid? ownerUserId, CancellationToken cancellationToken)
    {
        var userId = _user.UserId;
        if (userId is null)
            return false;

        if (ownerUserId == userId)
            return true;

        var email = _user.Email;
        if (string.IsNullOrWhiteSpace(email))
            return false;
        var emailLower = email.ToLowerInvariant();

        return await _db.Recipients.AsNoTracking()
            .AnyAsync(r => r.DocumentId == documentId
                        && r.Email.ToLower() == emailLower,
                cancellationToken);
    }

    private (string? SubjectCn, string? TspName, DateTime? TsaTime, string? CertificateSerial) TryReadSignerInfo(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return (null, null, null, null);

        var resolved = ResolvePath(storagePath);
        if (resolved is null || !File.Exists(resolved))
            return (null, null, null, null);

        try
        {
            using var reader = new PdfReader(resolved);
            using var pdf = new PdfDocument(reader);
            var util = new SignatureUtil(pdf);
            foreach (var name in util.GetSignatureNames())
            {
                var sig = util.GetSignature(name);
                if (string.Equals(sig?.GetSubFilter()?.GetValue(), "ETSI.RFC3161", StringComparison.Ordinal))
                    continue;

                var pkcs7 = util.ReadSignatureData(name);
                if (pkcs7 is null) continue;

                var cert = pkcs7.GetSigningCertificate();
                var subjectCn = ExtractDnComponent(cert?.GetSubjectDN()?.ToString(), "CN");
                var tspName = ExtractDnComponent(cert?.GetIssuerDN()?.ToString(), "O")
                              ?? ExtractDnComponent(cert?.GetIssuerDN()?.ToString(), "CN");
                var ts = pkcs7.GetTimeStampDate();
                // iText returns DateTime.MaxValue as the "no embedded TSA timestamp"
                // sentinel (CMS w/o RFC 3161 unsigned-attr id-aa-timeStampToken).
                // MinValue covers the symmetric uninitialised-default case.
                DateTime? tsaTime = (ts == DateTime.MinValue || ts == DateTime.MaxValue)
                    ? null
                    : DateTime.SpecifyKind(ts, DateTimeKind.Utc);
                string? serial = null;
                try { serial = cert?.GetSerialNumber()?.ToString(16)?.ToUpperInvariant(); }
                catch { }
                return (subjectCn, tspName, tsaTime, serial);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse signer certificate from {Path}", resolved);
        }

        return (null, null, null, null);
    }

    private string? ResolvePath(string storagePath)
    {
        if (Path.IsPathRooted(storagePath))
            return storagePath;
        return _storage?.ResolveSigned(storagePath);
    }

    private static string? ExtractDnComponent(string? dn, string componentKey)
    {
        if (string.IsNullOrWhiteSpace(dn))
            return null;
        foreach (var part in dn.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith($"{componentKey}=", StringComparison.OrdinalIgnoreCase))
                return trimmed[(componentKey.Length + 1)..].Trim();
        }
        return null;
    }
}
