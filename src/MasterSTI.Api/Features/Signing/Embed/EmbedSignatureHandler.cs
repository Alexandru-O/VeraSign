using System.Text.Json;
using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Caching;
using MasterSTI.Api.Common.Realtime;
using MasterSTI.Api.Common.Signing;
using MasterSTI.Api.Common.Wysiwys;
using MasterSTI.Api.Data;
using MasterSTI.Shared.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Signing.Embed;

public sealed class EmbedSignatureHandler : IRequestHandler<EmbedSignatureCommand, EmbedSignatureResponse>
{
    private readonly AppDbContext _db;
    private readonly PadesService _pades;
    private readonly ILtvService _ltv;
    private readonly DocumentStorage _storage;
    private readonly IConfiguration _config;
    private readonly IPageManifestService _pageManifests;
    private readonly ISigningLevelDispatcher? _levelDispatcher;
    private readonly IAuditWriter? _audit;
    private readonly ILogger<EmbedSignatureHandler> _logger;
    private readonly IDashboardCacheInvalidator? _dashCache;
    private readonly IDashboardNotifier? _notifier;

    public EmbedSignatureHandler(
        AppDbContext db,
        PadesService pades,
        ILtvService ltv,
        DocumentStorage storage,
        IConfiguration config,
        IPageManifestService pageManifests,
        ILogger<EmbedSignatureHandler> logger,
        ISigningLevelDispatcher? levelDispatcher = null,
        IAuditWriter? audit = null,
        IDashboardCacheInvalidator? dashCache = null,
        IDashboardNotifier? notifier = null)
    {
        _db = db;
        _pades = pades;
        _ltv = ltv;
        _storage = storage;
        _config = config;
        _pageManifests = pageManifests;
        _levelDispatcher = levelDispatcher;
        _audit = audit;
        _logger = logger;
        _dashCache = dashCache;
        _notifier = notifier;
    }

    public async Task<EmbedSignatureResponse> Handle(EmbedSignatureCommand request, CancellationToken cancellationToken)
    {
        var sigReq = await _db.SigningRequests
            .Include(s => s.Document)
            .Include(s => s.Recipient)
            .FirstOrDefaultAsync(s => s.Id == request.SigningRequestId, cancellationToken)
            ?? throw new KeyNotFoundException($"SigningRequest {request.SigningRequestId} not found");

        if (sigReq.Status is SigningRequestStatus.Embedded or SigningRequestStatus.Failed)
            throw new InvalidOperationException($"SigningRequest {sigReq.Id} is in status {sigReq.Status} — cannot embed again.");

        if (sigReq.PreparedStoragePath is null)
            throw new InvalidOperationException("SigningRequest has no prepared PDF. Call /api/signing/prepare first.");

        if (string.IsNullOrEmpty(request.CmsSignatureBase64))
            throw new InvalidOperationException("CMS signature is required. Call /api/signing/{id}/sign first.");

        // Phase 1 routing guard: only QES_CSC has an embed pipeline today.
        // AdES (wallet device key) + SES land in later epics — fail loud now
        // instead of writing a malformed SignedDocument.
        if (sigReq.Recipient is not null && _levelDispatcher is not null)
        {
            var level = ISigningLevelDispatcher.Parse(sigReq.Recipient.Level);
            _levelDispatcher.Resolve(level);
        }

        var preparedPath = _storage.ResolvePrepared(sigReq.PreparedStoragePath);
        var preparedPdf = await File.ReadAllBytesAsync(preparedPath, cancellationToken);

        var cmsBytes = Convert.FromBase64String(request.CmsSignatureBase64);

        var signedPdf = _pades.Embed(preparedPdf, cmsBytes, sigReq.PreparedFieldName);

        var ltvedPdf = await _ltv.AddLtvDataAsync(signedPdf, cancellationToken);
        var hasLtv = HasDssDictionary(ltvedPdf);

        // PAdES-B-LTA upgrade: only attempt when LTV actually landed (DSS dict present) and a
        // TSA URL is configured. ETSI EN 319 142-1 §5.6 — archive TS builds on B-LT.
        var finalPdf = ltvedPdf;
        var tsaUrl = _config["TsaUrl"];
        if (hasLtv && !string.IsNullOrWhiteSpace(tsaUrl))
            finalPdf = await _ltv.AddArchiveTimestampAsync(ltvedPdf, tsaUrl, cancellationToken);

        var hasArchiveTs = HasArchiveTimestamp(finalPdf);
        var padesLevel = hasArchiveTs ? "PAdES-B-LTA"
            : hasLtv ? "PAdES-B-LT"
            : "PAdES-B-T";

        var signedDocId = Guid.NewGuid();
        var signedPath = Path.Combine(_storage.SignedRoot, $"{signedDocId}.pdf");
        await File.WriteAllBytesAsync(signedPath, finalPdf, cancellationToken);

        // Capture page-content manifest from the prepared PDF (pre-LTV/pre-archive-TS) so the
        // baseline reflects what the signer actually signed. Validator re-hashes the current
        // signed PDF — divergence flags post-signature visual tampering (shadow attacks).
        var manifest = _pageManifests.Compute(preparedPdf);
        var manifestJson = manifest is not null
            ? JsonSerializer.Serialize(manifest)
            : null;

        // Sign Order Rule + chain handover live in SignedDocumentChainHandover so
        // the same logic is callable from unit tests without a real PAdES pipeline.
        // See docs/adr/0006-sign-order-rule-in-embed-handler.md.
        var tx = await DbTransactionScope.BeginIfRelationalAsync(_db, cancellationToken);
        try
        {
            await SignedDocumentChainHandover.ApplyAsync(
                _db, sigReq, signedDocId, signedPath, padesLevel, manifestJson, cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            await DbTransactionScope.CommitIfAsync(tx, cancellationToken);
        }
        catch
        {
            await DbTransactionScope.RollbackIfAsync(tx, cancellationToken);
            try { File.Delete(signedPath); } catch { }

            sigReq.Status = SigningRequestStatus.Failed;
            sigReq.FailedAtStage = 5; // PAdES embed
            sigReq.UpdatedAt = DateTime.UtcNow;
            try { await _db.SaveChangesAsync(cancellationToken); } catch { }
            _dashCache?.InvalidateOrg(sigReq.Document?.OrganizationId);
            if (_notifier is not null)
                try { await _notifier.NotifyOrgAsync(sigReq.Document?.OrganizationId, cancellationToken); } catch { }
            throw;
        }

        if (_audit is not null)
            await _audit.WriteAsync(sigReq.DocumentId, "Embedded",
                $"{{\"signedDocumentId\":\"{signedDocId}\",\"level\":\"{padesLevel}\"}}",
                cancellationToken);

        _dashCache?.InvalidateOrg(sigReq.Document?.OrganizationId);
        if (_notifier is not null)
            await _notifier.NotifyOrgAsync(sigReq.Document?.OrganizationId, cancellationToken);
        _logger.LogInformation("Signature embedded: {SignedDocId} ({Level})", signedDocId, padesLevel);

        return new EmbedSignatureResponse(signedDocId, padesLevel);
    }

    private static bool HasDssDictionary(byte[] pdfBytes)
    {
        try
        {
            using var reader = new iText.Kernel.Pdf.PdfReader(new MemoryStream(pdfBytes));
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
            var dss = pdfDoc.GetCatalog().GetPdfObject().GetAsDictionary(new iText.Kernel.Pdf.PdfName("DSS"));
            return dss is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True when the PDF carries at least one document-level RFC 3161 timestamp signature
    /// (subFilter <c>ETSI.RFC3161</c>) on top of the signer signature — the PAdES-B-LTA marker.
    /// </summary>
    private static bool HasArchiveTimestamp(byte[] pdfBytes)
    {
        try
        {
            using var reader = new iText.Kernel.Pdf.PdfReader(new MemoryStream(pdfBytes));
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
            var sigUtil = new iText.Signatures.SignatureUtil(pdfDoc);
            foreach (var name in sigUtil.GetSignatureNames())
            {
                var sig = sigUtil.GetSignature(name);
                var subFilter = sig?.GetSubFilter()?.GetValue();
                if (string.Equals(subFilter, "ETSI.RFC3161", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
