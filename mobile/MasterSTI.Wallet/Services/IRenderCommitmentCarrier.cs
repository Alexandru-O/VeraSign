namespace MasterSTI.Wallet.Services;

/// <summary>
/// Per-document slot for the Pixel-Bound QES Render Commitment (ADR-0008)
/// the wallet pulled from <c>POST /api/documents/{id}/render-commitment</c>.
///
/// Lifecycle:
///   1. ReviewPage.OnAppearing kicks off the API call after the PDF loads.
///   2. Server returns <see cref="RenderCommitmentDto"/>; ReviewPage stores
///      it under the document id.
///   3. ConsentPage shows a "render verified" pill (UI polish; not strictly
///      required for correctness since the server is the authority).
///   4. WalletSigningOrchestrator + StatusPage.RunBackgroundSignAsync read
///      the slot in Prepare time and forward the fields on the
///      <c>/api/signing/prepare</c> payload.
///
/// Singleton in DI. State scoped per documentId, not per session, so a
/// retry from ConsentPage finds the same commitment without re-fetching.
/// Cleared when StatusPage exits (Done / Failed) via <see cref="Clear"/>.
/// </summary>
public interface IRenderCommitmentCarrier
{
    void Set(Guid documentId, RenderCommitmentDto commitment);
    RenderCommitmentDto? Get(Guid documentId);
    void Clear(Guid documentId);
}

public sealed record RenderCommitmentDto(
    string Profile,
    string Algo,
    int Dpi,
    int PageCount,
    string Locale,
    string RootHex,
    string PdfiumBinarySha256);

public sealed class RenderCommitmentCarrier : IRenderCommitmentCarrier
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, RenderCommitmentDto> _slots = new();

    public void Set(Guid documentId, RenderCommitmentDto commitment)
    {
        lock (_lock) _slots[documentId] = commitment;
    }

    public RenderCommitmentDto? Get(Guid documentId)
    {
        lock (_lock)
            return _slots.TryGetValue(documentId, out var c) ? c : null;
    }

    public void Clear(Guid documentId)
    {
        lock (_lock) _slots.Remove(documentId);
    }
}
