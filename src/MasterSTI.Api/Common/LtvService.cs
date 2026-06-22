using iText.Kernel.Pdf;
using iText.Signatures;

namespace MasterSTI.Api.Common;

public interface ILtvService
{
    /// <summary>
    /// Attempts to add LTV (Long-Term Validation) data (OCSP/CRL) to the DSS dictionary.
    /// Degrades gracefully if certificate has no AIA/CDP extensions (e.g. self-signed mock cert).
    /// </summary>
    Task<byte[]> AddLtvDataAsync(byte[] signedPdfBytes, CancellationToken ct = default);

    /// <summary>
    /// Adds an RFC 3161 document timestamp on top of a B-LT signed PDF, producing PAdES-B-LTA
    /// (ETSI EN 319 142-1 §5.6). Caller is expected to have run <see cref="AddLtvDataAsync"/>
    /// first; this method does not check. Returns the original bytes on TSA failure.
    /// </summary>
    Task<byte[]> AddArchiveTimestampAsync(byte[] ltvPdfBytes, string tsaUrl, CancellationToken ct = default);
}

public sealed class LtvService : ILtvService
{
    private readonly ILogger<LtvService> _logger;

    public LtvService(ILogger<LtvService> logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> AddLtvDataAsync(byte[] signedPdfBytes, CancellationToken ct = default)
    {
        await Task.Yield(); // make truly async

        try
        {
            using var inputMs = new MemoryStream(signedPdfBytes);
            using var outputMs = new MemoryStream();

            using var reader = new PdfReader(inputMs);
            using var writer = new PdfWriter(outputMs);
            using var pdfDoc = new PdfDocument(reader, writer, new StampingProperties().UseAppendMode());

            var ltvVerification = new LtvVerification(pdfDoc);

            var sigUtil = new SignatureUtil(pdfDoc);
            var signatureNames = sigUtil.GetSignatureNames();

            if (signatureNames.Count == 0)
            {
                _logger.LogWarning("No signatures found in PDF for LTV processing");
                return signedPdfBytes;
            }

            var anyLtvAdded = false;
            foreach (var sigName in signatureNames)
            {
                try
                {
                    var added = ltvVerification.AddVerification(
                        sigName,
                        null,   // IOcspClient — null triggers auto-fetch from AIA
                        null,   // ICrlClient — null triggers auto-fetch from CDP
                        LtvVerification.CertificateOption.WHOLE_CHAIN,
                        LtvVerification.Level.OCSP_CRL,
                        LtvVerification.CertificateInclusion.NO);

                    if (added) anyLtvAdded = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "LTV data could not be added for signature '{SigName}' — certificate may lack AIA/CDP (e.g. self-signed mock cert). Degrading to PAdES-B-T.",
                        sigName);
                }
            }

            if (anyLtvAdded)
            {
                ltvVerification.Merge();
                _logger.LogInformation("LTV data added — signature level: PAdES-B-LT");
                pdfDoc.Close();
                return outputMs.ToArray();
            }
            else
            {
                _logger.LogWarning("No LTV data could be fetched — signature remains at PAdES-B-T");
                return signedPdfBytes;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LTV processing failed — returning PDF without LTV data");
            return signedPdfBytes;
        }
    }

    public async Task<byte[]> AddArchiveTimestampAsync(byte[] ltvPdfBytes, string tsaUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tsaUrl))
        {
            _logger.LogWarning("Archive timestamp skipped — no TSA URL configured");
            return ltvPdfBytes;
        }

        // Off-thread to keep the request thread free; iText I/O is synchronous.
        return await Task.Run(() =>
        {
            try
            {
                using var inputMs = new MemoryStream(ltvPdfBytes);
                using var outputMs = new MemoryStream();
                using var reader = new PdfReader(inputMs);

                var signer = new PdfSigner(reader, outputMs, new StampingProperties().UseAppendMode());
                var tsa = new TSAClientBouncyCastle(tsaUrl);
                var fieldName = $"ArchiveTimestamp_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                signer.Timestamp(tsa, fieldName);

                _logger.LogInformation("Archive timestamp added (field '{Field}', TSA {Tsa}) — signature level: PAdES-B-LTA",
                    fieldName, tsaUrl);
                return outputMs.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Archive timestamp failed for TSA {Tsa} — returning PDF without B-LTA upgrade", tsaUrl);
                return ltvPdfBytes;
            }
        }, ct);
    }
}
