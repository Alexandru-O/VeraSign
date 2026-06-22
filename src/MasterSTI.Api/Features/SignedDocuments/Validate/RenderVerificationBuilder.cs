using MasterSTI.Api.Common.Rendering;

namespace MasterSTI.Api.Features.SignedDocuments.Validate;

/// <summary>
/// Pure orchestrator over <see cref="IReferenceRenderer"/> that turns a
/// <see cref="StoredRenderCommitment"/> + the signed PDF bytes into a
/// <see cref="RenderVerificationReport"/>. Lives outside ValidateSignatureHandler
/// so tests can drive the verifier seam directly without standing up an
/// InMemory DbContext, on-disk PDF, or HTTP pipeline.
/// </summary>
public static class RenderVerificationBuilder
{
    public static async Task<(RenderVerificationReport Report, string LogLine)> BuildAsync(
        StoredRenderCommitment? stored,
        byte[] pdfBytes,
        IReferenceRenderer renderer,
        string supportedProfile,
        Guid signedDocId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (stored is null)
        {
            return (new RenderVerificationReport(
                Status: RenderVerificationStatus.NotPresent,
                Profile: null, Algo: null, Dpi: 0, PageCount: 0, Locale: null,
                StoredRootHex: null, RecomputedRootHex: null,
                VerifierPdfiumBinarySha256: renderer.PinnedBinarySha256,
                DisputedPages: Array.Empty<int>(),
                Reason: "no /VeraSign.RenderRoot key"),
                "Pixel-Bound QES: not present in signature dictionary");
        }

        if (!string.Equals(stored.Profile, supportedProfile, StringComparison.Ordinal))
        {
            return (new RenderVerificationReport(
                Status: RenderVerificationStatus.NotPresent,
                Profile: stored.Profile, Algo: stored.Algo, Dpi: stored.Dpi,
                PageCount: stored.PageCount, Locale: stored.Locale,
                StoredRootHex: stored.RootHex, RecomputedRootHex: null,
                VerifierPdfiumBinarySha256: renderer.PinnedBinarySha256,
                DisputedPages: Array.Empty<int>(),
                Reason: $"verifier does not support profile '{stored.Profile}'"),
                $"Pixel-Bound QES: profile '{stored.Profile}' not supported by this verifier");
        }

        if (!renderer.IsAvailable)
        {
            return (new RenderVerificationReport(
                Status: RenderVerificationStatus.NotPresent,
                Profile: stored.Profile, Algo: stored.Algo, Dpi: stored.Dpi,
                PageCount: stored.PageCount, Locale: stored.Locale,
                StoredRootHex: stored.RootHex, RecomputedRootHex: null,
                VerifierPdfiumBinarySha256: renderer.PinnedBinarySha256,
                DisputedPages: Array.Empty<int>(),
                Reason: $"verifier unavailable: {renderer.UnavailableReason}"),
                $"Pixel-Bound QES: verifier unavailable ({renderer.UnavailableReason})");
        }

        try
        {
            var locale = string.IsNullOrWhiteSpace(stored.Locale) ? "ro-RO" : stored.Locale;
            var recomputed = await renderer.RecomputeAsync(pdfBytes, locale, cancellationToken);

            var matches = string.Equals(recomputed.RootHex, stored.RootHex, StringComparison.OrdinalIgnoreCase);
            var status = matches ? RenderVerificationStatus.Verified : RenderVerificationStatus.Disputed;

            // v1 deliberately does NOT store per-page leaves in the
            // signature dictionary (ADR-0008 §"Alternatives considered") so
            // a root divergence cannot be attributed to specific pages.
            // DisputedPages stays empty; the row surfaces the root mismatch
            // and the reviewer audits the dispute via the raw-report block.
            return (new RenderVerificationReport(
                Status: status,
                Profile: stored.Profile, Algo: stored.Algo, Dpi: stored.Dpi,
                PageCount: stored.PageCount, Locale: stored.Locale,
                StoredRootHex: stored.RootHex,
                RecomputedRootHex: recomputed.RootHex,
                VerifierPdfiumBinarySha256: renderer.PinnedBinarySha256,
                DisputedPages: Array.Empty<int>(),
                Reason: matches ? null : "Merkle root divergence"),
                $"Pixel-Bound QES: {(matches ? "MATCH" : "DIVERGENCE")} · stored={stored.RootHex[..8]}… recomputed={recomputed.RootHex[..8]}… profile={stored.Profile}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Render commitment recompute failed for {SignedDocId}", signedDocId);
            return (new RenderVerificationReport(
                Status: RenderVerificationStatus.NotPresent,
                Profile: stored.Profile, Algo: stored.Algo, Dpi: stored.Dpi,
                PageCount: stored.PageCount, Locale: stored.Locale,
                StoredRootHex: stored.RootHex, RecomputedRootHex: null,
                VerifierPdfiumBinarySha256: renderer.PinnedBinarySha256,
                DisputedPages: Array.Empty<int>(),
                Reason: $"recompute failed: {ex.Message}"),
                $"Pixel-Bound QES: recompute failed ({ex.Message})");
        }
    }
}
