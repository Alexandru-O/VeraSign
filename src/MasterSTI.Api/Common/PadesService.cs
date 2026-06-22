using System.Security.Cryptography;
using iText.Bouncycastle.X509;
using iText.Commons.Bouncycastle.Cert;
using iText.Forms.Form.Element;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Signatures;
using Org.BouncyCastle.X509;

namespace MasterSTI.Api.Common;

public record PrepareResult(
    byte[] PreparedPdfBytes,
    string ByteRangeHashHex,
    long[] ByteRange,
    string SignatureFieldName);

/// <summary>
/// Placement of a signature widget on a specific page. Coordinates use the
/// editor convention: fractions of the page box (0-1) with origin at the
/// top-left corner. <see cref="PadesService"/> converts them to PDF
/// user-space (origin bottom-left) when stamping the widget.
/// </summary>
public sealed record PadesFieldPlacement(
    int Page,
    double XFraction,
    double YFraction,
    double WidthFraction,
    double HeightFraction);

/// <summary>
/// Identity + context drawn inside the signature widget. The CMS container
/// is still authoritative — this is just the visible artifact a reader sees.
/// </summary>
public sealed record PadesAppearance(
    string? SignerName,
    string? SignerEmail,
    DateTime? SignedAtUtc,
    string Reason = "Qualified Electronic Signature",
    string Location = "Romania");

/// <summary>
/// Pixel-Bound QES commitment (ADR-0008) passed verbatim into the
/// AcroForm signature dictionary under the /VeraSign.Render* namespace.
/// PadesService does not interpret the values; PrepareSigningValidator is
/// the authoritative gatekeeper for v1 schema conformance.
/// </summary>
public sealed record PadesRenderCommitment(
    string RootHex,
    string Algo,
    int Dpi,
    int PageCount,
    string Locale,
    string Profile);

public sealed class PadesService
{
    private const string DefaultSigFieldName = "Signature1";
    private const int SignaturePlaceholderSize = 32768;

    // Default appearance rectangle used when the caller does not supply a
    // placement (legacy tests + the older self-sign path). Top-left of page 1.
    private static readonly Rectangle DefaultRect = new(36, 748, 200, 48);

    private readonly ILogger<PadesService> _logger;

    public PadesService(ILogger<PadesService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Inserts an empty signature placeholder and computes SHA-256 over the
    /// ByteRange. Legacy single-arg overload — preserved so unit tests and the
    /// older self-sign path keep working.
    /// </summary>
    public PrepareResult Prepare(byte[] originalPdf) =>
        Prepare(originalPdf, DefaultSigFieldName, placement: null, appearance: null, commitment: null);

    /// <summary>
    /// Multi-signer overload. Forwards to the full overload with no
    /// commitment so existing callers keep working unchanged.
    /// </summary>
    public PrepareResult Prepare(
        byte[] originalPdf,
        string fieldName,
        PadesFieldPlacement? placement,
        PadesAppearance? appearance) =>
        Prepare(originalPdf, fieldName, placement, appearance, commitment: null);

    /// <summary>
    /// Multi-signer overload with optional Pixel-Bound QES commitment.
    /// <paramref name="fieldName"/> MUST be unique across the chain (each
    /// recipient gets their own widget). When <paramref name="placement"/> is
    /// null the widget falls back to the top-left of page 1 — caller should
    /// always pass real coordinates so stacked signatures do not overlap.
    /// When <paramref name="commitment"/> is non-null the six
    /// /VeraSign.Render* keys are written inside the AcroForm signature
    /// dictionary BEFORE the placeholder is sealed, so the keys land inside
    /// the signed ByteRange and any post-sign tamper invalidates the
    /// signature in the standard PAdES way.
    /// </summary>
    public PrepareResult Prepare(
        byte[] originalPdf,
        string fieldName,
        PadesFieldPlacement? placement,
        PadesAppearance? appearance,
        PadesRenderCommitment? commitment)
    {
        var safeFieldName = SanitiseFieldName(fieldName);

        using var reader = new PdfReader(new MemoryStream(originalPdf));
        using var outputMs = new MemoryStream();

        // Resolve placement against the actual page size — editor stores % so
        // the widget tracks the layout even if the PDF is A4 vs Letter. We
        // need a transient PdfDocument for that lookup; PdfSigner opens its
        // own copy from the byte stream below.
        var (pageNumber, rect) = ResolveRectangle(originalPdf, placement);

        var reason = appearance?.Reason ?? "Qualified Electronic Signature";
        var location = appearance?.Location ?? "Romania";

        var signerProps = new SignerProperties()
            .SetFieldName(safeFieldName)
            .SetCertificationLevel(AccessPermissions.UNSPECIFIED)
            .SetReason(reason)
            .SetLocation(location)
            .SetPageNumber(pageNumber)
            .SetPageRect(rect);

        // Visible appearance — when we have signer identity, render a layered
        // block ("Semnat de NAME / email / timestamp"). Without it, fall back
        // to iText's default text appearance (reason/location only).
        if (appearance is not null && !string.IsNullOrWhiteSpace(appearance.SignerName))
        {
            var content = BuildAppearanceText(appearance);
            var sigAppearance = new SignatureFieldAppearance(safeFieldName)
                .SetContent(content);
            signerProps.SetSignatureAppearance(sigAppearance);
        }

        // Append-mode is mandatory for stacked signatures: it appends an
        // incremental update instead of rewriting the file, so any prior
        // signer's CMS + widget survive untouched. iText auto-detects this
        // case but we make it explicit so the behavior is identical on the
        // first signer too — incremental output is harmless when the source
        // has no signatures yet.
        var signer = new PdfSigner(reader, outputMs, new StampingProperties().UseAppendMode());
        signer.SetSignerProperties(signerProps);

        var blankContainer = new BlankWithRenderCommitmentContainer(
            PdfName.Adobe_PPKLite,
            PdfName.Adbe_pkcs7_detached,
            commitment);

        signer.SignExternalContainer(blankContainer, SignaturePlaceholderSize);

        var preparedBytes = outputMs.ToArray();
        var (byteRange, docHash) = ExtractByteRangeHash(preparedBytes, safeFieldName);

        _logger.LogInformation(
            "PDF prepared for signing. Field: {Field}, Page: {Page}, Rect: ({X},{Y},{W},{H})",
            safeFieldName, pageNumber, rect.GetX(), rect.GetY(), rect.GetWidth(), rect.GetHeight());

        return new PrepareResult(preparedBytes, docHash, byteRange, safeFieldName);
    }

    public byte[] Embed(byte[] preparedPdf, byte[] cmsSignatureBytes, string? fieldName = null)
    {
        var field = string.IsNullOrWhiteSpace(fieldName) ? DefaultSigFieldName : SanitiseFieldName(fieldName);
        using var outputMs = new MemoryStream();

        using var reader = new PdfReader(new MemoryStream(preparedPdf));
        PdfSigner.SignDeferred(reader, field, outputMs, new PreComputedSignatureContainer(cmsSignatureBytes));

        _logger.LogInformation("CMS ({Bytes} bytes) embedded into PDF field '{Field}'",
            cmsSignatureBytes.Length, field);

        return outputMs.ToArray();
    }

    public byte[] GetSignedAttributesBytes(byte[] docHashBytes, byte[] certDerBytes)
    {
        var cert = ParseCertificate(certDerBytes);
        var pdfPkcs7 = new PdfPKCS7(null, new[] { cert }, "SHA256", false);
        return pdfPkcs7.GetAuthenticatedAttributeBytes(
            docHashBytes,
            PdfSigner.CryptoStandard.CMS,
            null,
            null);
    }

    public byte[] BuildCms(
        byte[] docHashBytes,
        byte[] certDerBytes,
        byte[] rawSignatureBytes,
        string? tsaUrl = null)
    {
        var cert = ParseCertificate(certDerBytes);
        var pdfPkcs7 = new PdfPKCS7(null, new[] { cert }, "SHA256", false);

        pdfPkcs7.SetExternalSignatureValue(rawSignatureBytes, null, "RSA");

        ITSAClient? tsaClient = null;
        if (!string.IsNullOrEmpty(tsaUrl))
        {
            try { tsaClient = new TSAClientBouncyCastle(tsaUrl); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not create TSA client for {Url}", tsaUrl); }
        }

        return pdfPkcs7.GetEncodedPKCS7(
            docHashBytes,
            PdfSigner.CryptoStandard.CMS,
            tsaClient,
            null,
            null);
    }

    private static (int pageNumber, Rectangle rect) ResolveRectangle(byte[] originalPdf, PadesFieldPlacement? placement)
    {
        if (placement is null)
            return (1, DefaultRect);

        using var pdfDoc = new PdfDocument(new PdfReader(new MemoryStream(originalPdf)));
        var totalPages = pdfDoc.GetNumberOfPages();
        var page = Math.Clamp(placement.Page <= 0 ? 1 : placement.Page, 1, totalPages);

        var size = pdfDoc.GetPage(page).GetPageSize();
        var pageW = size.GetWidth();
        var pageH = size.GetHeight();

        // Editor coordinates: origin top-left, fractions of the page box (0-1).
        // PDF user space: origin bottom-left.
        var xFrac = ClampFraction(placement.XFraction);
        var yFrac = ClampFraction(placement.YFraction);
        var wFrac = Math.Max(0.02, placement.WidthFraction);  // never narrower than 2% so the widget stays visible
        var hFrac = Math.Max(0.02, placement.HeightFraction);

        var x = (float)(xFrac * pageW);
        var w = (float)(wFrac * pageW);
        var h = (float)(hFrac * pageH);

        // Convert top-edge Y to bottom-edge Y.
        var topEdgeY = (float)(yFrac * pageH);
        var y = pageH - topEdgeY - h;

        // Keep the widget inside the page box if the editor rounded over the edge.
        if (x < 0) x = 0;
        if (y < 0) y = 0;
        if (x + w > pageW) w = pageW - x;
        if (y + h > pageH) h = pageH - y;

        return (page, new Rectangle(x, y, w, h));
    }

    private static double ClampFraction(double v) =>
        double.IsFinite(v) ? Math.Clamp(v, 0.0, 1.0) : 0.0;

    private static string BuildAppearanceText(PadesAppearance a)
    {
        var lines = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(a.SignerName))
            lines.Add($"Semnat de {a.SignerName}");
        if (!string.IsNullOrWhiteSpace(a.SignerEmail))
            lines.Add(a.SignerEmail!);
        var when = (a.SignedAtUtc ?? DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm 'UTC'");
        lines.Add(when);
        return string.Join('\n', lines);
    }

    private static string SanitiseFieldName(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return DefaultSigFieldName;

        // AcroForm names use '.' as path separator. Strip anything that would
        // confuse iText's name resolver; keep ASCII letters/digits + '-' + '_'.
        Span<char> buffer = stackalloc char[Math.Min(fieldName.Length, 64)];
        var i = 0;
        foreach (var ch in fieldName)
        {
            if (i >= buffer.Length) break;
            var ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')
                  || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_';
            buffer[i++] = ok ? ch : '_';
        }
        var name = new string(buffer[..i]);
        return string.IsNullOrEmpty(name) ? DefaultSigFieldName : name;
    }

    private (long[] byteRange, string docHashHex) ExtractByteRangeHash(byte[] preparedBytes, string fieldName)
    {
        using var pdfDoc = new PdfDocument(new PdfReader(new MemoryStream(preparedBytes)));
        var acroForm = iText.Forms.PdfAcroForm.GetAcroForm(pdfDoc, false)
            ?? throw new InvalidOperationException("PDF has no AcroForm after prepare");

        var field = acroForm.GetField(fieldName)
            ?? throw new InvalidOperationException($"Signature field '{fieldName}' not found after prepare");

        var fieldDict = field.GetPdfObject();
        var sigDict = fieldDict.GetAsDictionary(PdfName.V) ?? fieldDict;
        var byteRangeArray = sigDict.GetAsArray(PdfName.ByteRange)
            ?? throw new InvalidOperationException("No /ByteRange in prepared signature field.");

        var offset0 = byteRangeArray.GetAsNumber(0).LongValue();
        var length0 = byteRangeArray.GetAsNumber(1).LongValue();
        var offset1 = byteRangeArray.GetAsNumber(2).LongValue();
        var length1 = byteRangeArray.GetAsNumber(3).LongValue();

        using var hashInput = new MemoryStream();
        hashInput.Write(preparedBytes, (int)offset0, (int)length0);
        hashInput.Write(preparedBytes, (int)offset1, (int)length1);

        var docHash = SHA256.HashData(hashInput.ToArray());
        var docHashHex = Convert.ToHexString(docHash).ToLowerInvariant();

        return (new[] { offset0, length0, offset1, length1 }, docHashHex);
    }

    private static IX509Certificate ParseCertificate(byte[] certDerBytes)
    {
        var bcCert = new X509CertificateParser().ReadCertificate(certDerBytes);
        return new X509CertificateBC(bcCert);
    }

    private sealed class PreComputedSignatureContainer : IExternalSignatureContainer
    {
        private readonly byte[] _cmsBytes;
        public PreComputedSignatureContainer(byte[] cmsBytes) => _cmsBytes = cmsBytes;
        public byte[] Sign(Stream data) => _cmsBytes;
        public void ModifySigningDictionary(PdfDictionary signDic) { }
    }

    // Same role as iText's ExternalBlankSignatureContainer (write a
    // placeholder /Filter + /SubFilter, no actual signature bytes) plus
    // the Pixel-Bound QES dictionary keys when a commitment is supplied.
    // Writing the keys here -- inside ModifySigningDictionary -- guarantees
    // they participate in the /ByteRange that PdfSigner is about to compute,
    // which is the whole point of the scheme per ADR-0008.
    private sealed class BlankWithRenderCommitmentContainer : IExternalSignatureContainer
    {
        private readonly PdfName _filter;
        private readonly PdfName _subFilter;
        private readonly PadesRenderCommitment? _commitment;

        public BlankWithRenderCommitmentContainer(
            PdfName filter,
            PdfName subFilter,
            PadesRenderCommitment? commitment)
        {
            _filter = filter;
            _subFilter = subFilter;
            _commitment = commitment;
        }

        public byte[] Sign(Stream data) => Array.Empty<byte>();

        public void ModifySigningDictionary(PdfDictionary signDic)
        {
            signDic.Put(PdfName.Filter, _filter);
            signDic.Put(PdfName.SubFilter, _subFilter);

            if (_commitment is null)
                return;

            // Hex-string representation: PDF body emits the 32 raw hash
            // bytes as <hex...> rather than the literal ASCII text form.
            // Verifier reads back the same 32 bytes regardless of which
            // casing iText emits internally.
            var rootBytes = Convert.FromHexString(_commitment.RootHex);
            var rootPdfString = new PdfString(rootBytes).SetHexWriting(true);

            signDic.Put(new PdfName("VeraSign.RenderRoot"), rootPdfString);
            signDic.Put(new PdfName("VeraSign.RenderAlgo"), new PdfName(_commitment.Algo));
            signDic.Put(new PdfName("VeraSign.RenderDpi"), new PdfNumber(_commitment.Dpi));
            signDic.Put(new PdfName("VeraSign.RenderPageCount"), new PdfNumber(_commitment.PageCount));
            signDic.Put(new PdfName("VeraSign.RenderLocale"), new PdfString(_commitment.Locale));
            signDic.Put(new PdfName("VeraSign.RenderProfile"), new PdfName(_commitment.Profile));
        }
    }
}
