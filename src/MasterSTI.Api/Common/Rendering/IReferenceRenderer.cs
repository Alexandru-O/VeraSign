namespace MasterSTI.Api.Common.Rendering;

/// <summary>
/// Verifier-side seam for recomputing the Pixel-Bound QES root R' from a
/// signed PDF's bytes. Named after the ADR-0008 "sidecar binding" so the
/// validation handler reads as the ADR prose does, and so unit tests can
/// substitute a canned-bitmap implementation without booting PDFium.
/// </summary>
/// <remarks>
/// The wallet-side commitment producer is <see cref="IRenderCommitmentService"/>;
/// they share the underlying algorithm so a Verified result on the verifier
/// host is meaningful evidence the wallet's R came from the same pinned
/// PDFium build (the binary sha256 is also stamped into the signed
/// dictionary as a second-line check).
/// </remarks>
public interface IReferenceRenderer
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    string? PinnedBinarySha256 { get; }

    Task<RenderCommitmentResult> RecomputeAsync(
        byte[] pdfBytes, string locale, CancellationToken cancellationToken);
}

/// <summary>
/// Production implementation backed by the same in-process PDFium pin the
/// wallet uses. Reuses <see cref="IRenderCommitmentService"/> so the
/// algorithm has exactly one source of truth — the verifier just calls it
/// with the signed PDF's bytes and the locale read from the dictionary.
/// </summary>
public sealed class PdfiumReferenceRenderer : IReferenceRenderer
{
    private readonly IRenderCommitmentService _inner;

    public PdfiumReferenceRenderer(IRenderCommitmentService inner)
    {
        _inner = inner;
    }

    public bool IsAvailable => _inner.IsAvailable;
    public string? UnavailableReason => _inner.UnavailableReason;
    public string? PinnedBinarySha256 => _inner.PinnedBinarySha256;

    public Task<RenderCommitmentResult> RecomputeAsync(
        byte[] pdfBytes, string locale, CancellationToken cancellationToken) =>
        _inner.ComputeAsync(pdfBytes, locale, cancellationToken);
}
