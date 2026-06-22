using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iText.Commons.Bouncycastle.Cert;
using iText.Kernel.Pdf;
using iText.Signatures;
using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Rendering;
using MasterSTI.Api.Common.Trust;
using MasterSTI.Api.Common.Wysiwys;
using MasterSTI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.SignedDocuments.Validate;

public sealed class ValidateSignatureHandler : IRequestHandler<ValidateSignatureQuery, ValidationReportResponse?>
{
    // Frozen v1 schema per ADR-0008. Any other profile name in a signed
    // dictionary is treated as NotPresent + Reason: this verifier does not
    // know how to recompute that variant deterministically.
    private const string SupportedProfile = "PdfiumPinned-v1";

    private readonly AppDbContext _db;
    private readonly DocumentStorage _storage;
    private readonly ITrustListProvider _trustList;
    private readonly IPageManifestService _pageManifests;
    private readonly IReferenceRenderer _referenceRenderer;
    private readonly ILogger<ValidateSignatureHandler> _logger;

    public ValidateSignatureHandler(
        AppDbContext db,
        DocumentStorage storage,
        ITrustListProvider trustList,
        IPageManifestService pageManifests,
        IReferenceRenderer referenceRenderer,
        ILogger<ValidateSignatureHandler> logger)
    {
        _db = db;
        _storage = storage;
        _trustList = trustList;
        _pageManifests = pageManifests;
        _referenceRenderer = referenceRenderer;
        _logger = logger;
    }

    public async Task<ValidationReportResponse?> Handle(ValidateSignatureQuery request, CancellationToken cancellationToken)
    {
        var signedDoc = await _db.SignedDocuments
            .FirstOrDefaultAsync(s => s.Id == request.SignedDocumentId, cancellationToken);

        if (signedDoc is null) return null;

        var fullPath = _storage.ResolveSigned(signedDoc.StoragePath);
        if (!File.Exists(fullPath)) return null;

        var pdfBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        var report = await ValidatePdfAsync(request.SignedDocumentId, signedDoc.PadesLevel, pdfBytes, signedDoc.PageManifestJson, cancellationToken);

        signedDoc.ValidationReport = report.RawReport;
        try { await _db.SaveChangesAsync(cancellationToken); } catch { }

        return report;
    }

    private async Task<ValidationReportResponse> ValidatePdfAsync(Guid signedDocId, string declaredLevel, byte[] pdfBytes, string? storedManifestJson, CancellationToken cancellationToken)
    {
        var report = new StringBuilder();
        bool isIntegrityValid = false;
        bool hasTimestamp = false;
        bool hasLtv = false;
        bool hasArchiveTimestamp = false;
        bool coversWhole = false;
        string signerSubject = string.Empty;
        string? signerIssuer = null;
        DateTime? signingTime = null;
        DateTime? timestampTime = null;
        DateTime? certValidFrom = null;
        DateTime? certValidTo = null;
        var certChain = new List<CertificateInfo>();
        int sigCount = 0;
        StoredRenderCommitment? storedCommitment = null;

        try
        {
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var pdfDoc = new PdfDocument(reader);
            var sigUtil = new SignatureUtil(pdfDoc);

            var names = sigUtil.GetSignatureNames();
            sigCount = names.Count;
            report.AppendLine($"Signatures found: {sigCount}");

            foreach (var sigName in names)
            {
                var sig = sigUtil.GetSignature(sigName);
                var subFilter = sig?.GetSubFilter()?.GetValue();
                if (string.Equals(subFilter, "ETSI.RFC3161", StringComparison.Ordinal))
                {
                    hasArchiveTimestamp = true;
                    report.AppendLine($"\nDocument timestamp: {sigName} (RFC 3161)");
                    continue;
                }

                report.AppendLine($"\nSignature: {sigName}");

                // ADR-0008: lift the Pixel-Bound QES commitment off the
                // first non-document-timestamp signature dictionary we see.
                // Multi-signer chains are NotPresent in v1 (the endpoint
                // refuses the precompute for docs that already have a
                // SignedDocuments row) so reading from the first signature
                // is unambiguous here.
                if (storedCommitment is null)
                    storedCommitment = RenderCommitmentReader.Read(sig?.GetPdfObject());

                var pkcs7 = sigUtil.ReadSignatureData(sigName);
                if (pkcs7 is null) continue;

                coversWhole = sigUtil.SignatureCoversWholeDocument(sigName);
                report.AppendLine($"  Covers whole document: {coversWhole}");

                try
                {
                    isIntegrityValid = pkcs7.VerifySignatureIntegrityAndAuthenticity();
                    report.AppendLine($"  Integrity valid: {isIntegrityValid}");
                }
                catch (Exception ex)
                {
                    report.AppendLine($"  Integrity check error: {ex.Message}");
                    isIntegrityValid = false;
                }

                var signerCert = pkcs7.GetSigningCertificate();
                if (signerCert is not null)
                {
                    signerSubject = signerCert.GetSubjectDN()?.ToString() ?? string.Empty;
                    signerIssuer = signerCert.GetIssuerDN()?.ToString();
                    certValidFrom = TryGetDate(signerCert, getNotBefore: true);
                    certValidTo = TryGetDate(signerCert, getNotBefore: false);
                    report.AppendLine($"  Signer:  {signerSubject}");
                    report.AppendLine($"  Issuer:  {signerIssuer}");
                    if (certValidFrom is not null)
                        report.AppendLine($"  Cert valid from: {certValidFrom:u}");
                    if (certValidTo is not null)
                        report.AppendLine($"  Cert valid to:   {certValidTo:u}");
                }

                signingTime = pkcs7.GetSignDate();
                if (signingTime != DateTime.MinValue)
                    report.AppendLine($"  Signing time: {signingTime:u}");

                var tsDate = pkcs7.GetTimeStampDate();
                if (tsDate != DateTime.MinValue)
                {
                    hasTimestamp = true;
                    timestampTime = tsDate;
                    report.AppendLine($"  Signature timestamp: {tsDate:u}");
                }
                else
                {
                    report.AppendLine("  No signature timestamp");
                }

                try
                {
                    var chain = pkcs7.GetCertificates();
                    if (chain is not null)
                    {
                        foreach (var c in chain)
                        {
                            if (c is null) continue;
                            certChain.Add(BuildCertificateInfo(c));
                        }
                    }
                }
                catch (Exception ex)
                {
                    report.AppendLine($"  Cert chain extraction failed: {ex.Message}");
                }
            }

            var dss = pdfDoc.GetCatalog().GetPdfObject().GetAsDictionary(new PdfName("DSS"));
            hasLtv = dss is not null;
            report.AppendLine($"\nDSS dictionary present (LTV): {hasLtv}");
            report.AppendLine($"Archive timestamp (B-LTA): {hasArchiveTimestamp}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF validation failed for {SignedDocId}", signedDocId);
            report.AppendLine($"Validation error: {ex.Message}");
        }

        var level = hasArchiveTimestamp ? "PAdES-B-LTA"
            : hasLtv ? "PAdES-B-LT"
            : hasTimestamp ? "PAdES-B-T"
            : "PAdES-B-B";

        // EU Trust List lookup — match signer's issuing CA (Issuer DN) against the bundled
        // EUTL snapshot. Demo curated subset; production validators walk the live LOTL XML.
        var trustSnapshot = _trustList.Snapshot;
        var trustMatch = _trustList.Match(signerIssuer);
        report.AppendLine($"EU Trust List: {(trustMatch.IsTrusted ? $"MATCH · {trustMatch.TspName} ({trustMatch.Country})" : "no match")} · source={trustSnapshot.Source}");

        // Page-content manifest: re-hash each page and compare with the manifest captured at
        // embed time. Surfaces shadow-attack class tampering that integrity-of-bytes alone misses.
        PageManifestReport? manifestReport = null;
        if (!string.IsNullOrWhiteSpace(storedManifestJson))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<PageManifest>(storedManifestJson);
                var current = _pageManifests.Compute(pdfBytes);
                var cmp = _pageManifests.Compare(stored, current);
                manifestReport = new PageManifestReport(
                    Present: true,
                    Verified: cmp.Matches,
                    Version: stored?.Version,
                    Algorithm: stored?.Algorithm,
                    StoredPageCount: cmp.StoredPageCount,
                    CurrentPageCount: cmp.CurrentPageCount,
                    MismatchedPages: cmp.MismatchedPages,
                    StoredOverallSha256: cmp.StoredOverallSha256,
                    CurrentOverallSha256: cmp.CurrentOverallSha256);
                report.AppendLine($"Page manifest: {(cmp.Matches ? "MATCH" : "DIVERGENCE")} · {stored?.Version ?? "?"} · stored={cmp.StoredPageCount}p current={cmp.CurrentPageCount}p mismatches={string.Join(',', cmp.MismatchedPages)}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stored page manifest JSON unparsable for {SignedDocId}", signedDocId);
                manifestReport = new PageManifestReport(true, false, null, null, 0, 0, Array.Empty<int>(), null, null);
            }
        }
        else
        {
            report.AppendLine("Page manifest: not captured (pre-Day-3 signed document)");
        }

        // ADR-0008 Pixel-Bound QES verification. Recompute R' from the
        // signed PDF's bytes using the same pinned PDFium binary the
        // wallet's commitment endpoint stamps. R == R' -> Verified, R != R'
        // -> Disputed (always with crypto-status reported independently per
        // ADR Mismatch semantics). Anything else -> NotPresent so legacy
        // signatures and unavailable verifier hosts render identically to
        // pre-Pixel-Bound QES output.
        var renderVerification = await BuildRenderVerificationAsync(
            signedDocId, storedCommitment, pdfBytes, report, cancellationToken);

        return new ValidationReportResponse(
            signedDocId,
            isIntegrityValid,
            hasTimestamp,
            hasLtv,
            coversWhole,
            sigCount,
            signerSubject,
            signerIssuer,
            certValidFrom,
            certValidTo,
            level,
            signingTime == DateTime.MinValue ? null : signingTime,
            timestampTime,
            certChain,
            trustMatch.IsTrusted,
            trustMatch.TspName,
            trustMatch.Country,
            trustSnapshot.Source,
            trustSnapshot.SnapshotTakenAt == DateTime.MinValue ? null : trustSnapshot.SnapshotTakenAt,
            manifestReport,
            renderVerification,
            report.ToString());
    }

    private async Task<RenderVerificationReport?> BuildRenderVerificationAsync(
        Guid signedDocId,
        StoredRenderCommitment? stored,
        byte[] pdfBytes,
        StringBuilder report,
        CancellationToken cancellationToken)
    {
        var (verification, log) = await RenderVerificationBuilder.BuildAsync(
            stored, pdfBytes, _referenceRenderer, SupportedProfile, signedDocId, _logger, cancellationToken);
        report.AppendLine(log);
        return verification;
    }

    private static DateTime? TryGetDate(IX509Certificate cert, bool getNotBefore)
    {
        try
        {
            // iText v9 exposes NotBefore/NotAfter via methods on the BC abstraction.
            var dt = getNotBefore ? cert.GetNotBefore() : cert.GetNotAfter();
            return dt == DateTime.MinValue ? null : dt;
        }
        catch
        {
            return null;
        }
    }

    private static CertificateInfo BuildCertificateInfo(IX509Certificate cert)
    {
        var subject = cert.GetSubjectDN()?.ToString() ?? string.Empty;
        var issuer = cert.GetIssuerDN()?.ToString() ?? string.Empty;
        var from = TryGetDate(cert, getNotBefore: true) ?? DateTime.MinValue;
        var to = TryGetDate(cert, getNotBefore: false) ?? DateTime.MinValue;

        string serial = string.Empty;
        try { serial = cert.GetSerialNumber()?.ToString(16)?.ToUpperInvariant() ?? string.Empty; }
        catch { }

        string thumbprint = string.Empty;
        try
        {
            var encoded = cert.GetEncoded();
            if (encoded is not null)
            {
                var hash = SHA256.HashData(encoded);
                thumbprint = Convert.ToHexString(hash);
            }
        }
        catch { }

        return new CertificateInfo(subject, issuer, from, to, serial, thumbprint);
    }
}
