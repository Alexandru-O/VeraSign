using System.Security.Cryptography;
using iText.Kernel.Pdf;
using MasterSTI.Api.Common.Csc;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Signing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MasterSTI.Api.Features.Signing.GetTechnicalDetail;

/// <summary>
/// Builds the read-only technical-detail payload shown by the wallet's StatusPage
/// "Detaliat" toggle. Combines DB state (hash prefix, planned PAdES level) with
/// best-effort CSC credential lookup (cert fingerprint, TSP name, algorithm).
/// The CSC call is wrapped in try/catch so a transient QTSP outage degrades the
/// detail view rather than failing the status response.
/// </summary>
public sealed class GetTechnicalDetailHandler : IRequestHandler<GetTechnicalDetailQuery, TechnicalDetailDto?>
{
    private readonly AppDbContext _db;
    private readonly ICscApiClient _csc;
    private readonly IOptionsMonitor<CscApiOptions> _options;
    private readonly ILogger<GetTechnicalDetailHandler> _logger;

    public GetTechnicalDetailHandler(
        AppDbContext db,
        ICscApiClient csc,
        IOptionsMonitor<CscApiOptions> options,
        ILogger<GetTechnicalDetailHandler> logger)
    {
        _db = db;
        _csc = csc;
        _options = options;
        _logger = logger;
    }

    public async Task<TechnicalDetailDto?> Handle(GetTechnicalDetailQuery request, CancellationToken cancellationToken)
    {
        var row = await _db.SigningRequests.AsNoTracking()
            .Where(s => s.Id == request.SigningRequestId)
            .Select(s => new
            {
                s.DocumentHash,
                s.SignatureLevel,
                s.CredentialId,
                Document = new
                {
                    s.Document.Id,
                    s.Document.FileName,
                    s.Document.StoragePath,
                },
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;

        var hashPrefix = string.IsNullOrEmpty(row.DocumentHash)
            ? string.Empty
            : row.DocumentHash[..Math.Min(16, row.DocumentHash.Length)];

        var level = string.IsNullOrWhiteSpace(row.SignatureLevel)
            ? "PAdES-B-LT"
            : row.SignatureLevel;

        var (fingerprint, tspName, algorithm) = await TryLoadCredentialMetadataAsync(
            row.CredentialId, cancellationToken);

        // Chain-head PDF mirrors GetInboxItemHandler — multi-signer Documents grow
        // via PAdES increments, so pages + size must come from the chain-head when
        // present, not the original upload. Original Document.StoragePath remains
        // the fallback for the first signer.
        var chainHeadPath = await _db.SignedDocuments.AsNoTracking()
            .Where(sd => sd.OriginalDocumentId == row.Document.Id && sd.IsFinal)
            .Select(sd => sd.StoragePath)
            .FirstOrDefaultAsync(cancellationToken);

        var sourcePath = !string.IsNullOrWhiteSpace(chainHeadPath) && File.Exists(chainHeadPath)
            ? chainHeadPath
            : row.Document.StoragePath;

        var pages = TryGetPageCount(sourcePath);
        var sizeBytes = TryGetFileSize(sourcePath);

        return new TechnicalDetailDto(
            HashPrefix: hashPrefix,
            CertificateFingerprint: fingerprint,
            TspName: tspName,
            Algorithm: algorithm,
            Level: level,
            DocumentName: row.Document.FileName,
            Pages: pages,
            SizeBytes: sizeBytes);
    }

    private static int TryGetPageCount(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return 0;
        try
        {
            using var reader = new PdfReader(path);
            using var pdf = new PdfDocument(reader);
            return pdf.GetNumberOfPages();
        }
        catch
        {
            return 0;
        }
    }

    private static long TryGetFileSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return 0;
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private async Task<(string Fingerprint, string TspName, string Algorithm)> TryLoadCredentialMetadataAsync(
        string? credentialIdFromRequest,
        CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(opts.Username) || string.IsNullOrWhiteSpace(opts.Password))
            return (string.Empty, string.Empty, "SHA-256 + RSA-2048");

        var credentialId = !string.IsNullOrEmpty(credentialIdFromRequest)
            ? credentialIdFromRequest
            : opts.CredentialId;
        if (string.IsNullOrWhiteSpace(credentialId))
            return (string.Empty, string.Empty, "SHA-256 + RSA-2048");

        try
        {
            var token = await _csc.AuthLoginAsync(opts.Username!, opts.Password!, cancellationToken);
            var info = await _csc.GetCredentialInfoAsync(token, credentialId, cancellationToken);

            var certBytes = Convert.FromBase64String(info.cert.certificates[0]);
            var fingerprint = FormatFingerprint(SHA256.HashData(certBytes));
            var tspName = ExtractCommonName(info.cert.issuerDN);
            var algorithm = FormatAlgorithm(info.key);

            return (fingerprint, tspName, algorithm);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load credential info for technical-detail. Returning placeholders. CredentialId={CredentialId}",
                credentialId);
            return (string.Empty, string.Empty, "SHA-256 + RSA-2048");
        }
    }

    private static string FormatFingerprint(byte[] hash)
    {
        var hex = Convert.ToHexString(hash);
        var pairs = new string[hash.Length];
        for (var i = 0; i < hash.Length; i++)
            pairs[i] = hex.Substring(i * 2, 2);
        return string.Join(':', pairs);
    }

    /// <summary>
    /// Pull the <c>CN=</c> attribute out of an RFC 4514-ish DN string. Falls back
    /// to the trimmed DN when no CN is present.
    /// </summary>
    private static string ExtractCommonName(string? dn)
    {
        if (string.IsNullOrWhiteSpace(dn))
            return string.Empty;
        var parts = dn.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return p[3..].Trim();
        }
        return dn.Trim();
    }

    private static string FormatAlgorithm(CscKeyInfo key)
    {
        var family = key.algo is { Length: > 0 } ? MapAlgoOid(key.algo[0]) : "RSA";
        return key.len > 0 ? $"SHA-256 + {family}-{key.len}" : $"SHA-256 + {family}";
    }

    private static string MapAlgoOid(string oid) => oid switch
    {
        // RSA family
        "1.2.840.113549.1.1.1" => "RSA",
        "1.2.840.113549.1.1.11" => "RSA",
        // ECDSA
        "1.2.840.10045.2.1" => "ECDSA",
        "1.2.840.10045.4.3.2" => "ECDSA",
        _ => oid,
    };
}
