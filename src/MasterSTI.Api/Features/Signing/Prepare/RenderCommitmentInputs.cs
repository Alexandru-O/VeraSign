namespace MasterSTI.Api.Features.Signing.Prepare;

/// <summary>
/// Optional Pixel-Bound QES commitment (ADR-0008) supplied by the wallet
/// at PrepareSigning time. The wallet stamps these fields on its outgoing
/// /api/signing/prepare payload when it has computed a Render Commitment
/// for the current document (single-recipient, ≤ 50 pages, v1 profile).
/// PadesService writes them as /VeraSign.Render* keys on the AcroForm
/// signature dictionary INSIDE the signed ByteRange.
///
/// Nullable as a unit: when <see cref="RenderRootHex"/> is null the
/// request produces a plain PAdES-B-LTA signature with no /VeraSign.Render*
/// keys. When <see cref="RenderRootHex"/> is non-null every other field
/// MUST also be non-null and carry the v1-frozen values -- enforced by
/// PrepareSigningValidator. The validator is the single gatekeeper;
/// downstream code (PadesService) can treat a non-null commitment as
/// fully populated and v1-conformant.
/// </summary>
public sealed record RenderCommitmentInputs(
    string? RenderRootHex,
    string? RenderAlgo,
    int? RenderDpi,
    int? RenderPageCount,
    string? RenderLocale,
    string? RenderProfile)
{
    public bool IsPresent => RenderRootHex is not null;
}
