using System.Text.Json.Serialization;
using MediatR;

namespace MasterSTI.Api.Features.SignedDocuments.Validate;

public record ValidateSignatureQuery(Guid SignedDocumentId) : IRequest<ValidationReportResponse?>;

public record ValidationReportResponse(
    Guid SignedDocumentId,
    bool IsIntegrityValid,
    bool HasTimestamp,
    bool HasLtv,
    bool CoversWholeDocument,
    int SignatureCount,
    string SignerSubject,
    string? SignerIssuer,
    DateTime? CertValidFrom,
    DateTime? CertValidTo,
    string PadesLevel,
    DateTime? SigningTime,
    DateTime? TimestampTime,
    IReadOnlyList<CertificateInfo> CertificateChain,
    bool IsTrustedQtsp,
    string? TrustListTspName,
    string? TrustListCountry,
    string? TrustListSource,
    DateTime? TrustListSnapshotAt,
    PageManifestReport? PageManifest,
    RenderVerificationReport? RenderVerification,
    string RawReport);

// CLAUDE.md: per-type JsonStringEnumConverter (no global converter on Program.cs).
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RenderVerificationStatus
{
    /// <summary>No /VeraSign.RenderRoot in the signature dictionary, the
    /// verifier host has no pinned PDFium, the recompute crashed, or the
    /// commitment carried a profile this verifier does not support. UI hides
    /// the row entirely (per ADR-0008 Mismatch semantics) so legacy
    /// signatures look exactly like they did before Pixel-Bound QES.</summary>
    NotPresent = 0,

    /// <summary>R == R'. Wallet-side render at sign time and verifier-side
    /// recompute at validate time produced byte-identical Merkle roots.</summary>
    Verified = 1,

    /// <summary>R != R'. The PAdES signature itself can still be crypto-valid
    /// (ETSI TS 119 102-1) — render dispute is reported independently. v1
    /// does not store per-page leaves so <c>DisputedPages</c> is always
    /// empty; the row surfaces the root divergence + the
    /// stored-vs-recomputed hashes so a reviewer can audit the dispute.</summary>
    Disputed = 2,
}

public record RenderVerificationReport(
    RenderVerificationStatus Status,
    string? Profile,
    string? Algo,
    int Dpi,
    int PageCount,
    string? Locale,
    string? StoredRootHex,
    string? RecomputedRootHex,
    string? VerifierPdfiumBinarySha256,
    IReadOnlyList<int> DisputedPages,
    string? Reason);

public record PageManifestReport(
    bool Present,
    bool Verified,
    string? Version,
    string? Algorithm,
    int StoredPageCount,
    int CurrentPageCount,
    IReadOnlyList<int> MismatchedPages,
    string? StoredOverallSha256,
    string? CurrentOverallSha256);

public record CertificateInfo(
    string Subject,
    string Issuer,
    DateTime ValidFrom,
    DateTime ValidTo,
    string SerialNumber,
    string Thumbprint);
