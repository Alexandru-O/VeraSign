using MasterSTI.Shared.DTOs.Signing;
using MasterSTI.Shared.DTOs.Wallet;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// HTTP client surface for the MasterSTI API, scoped to the wallet's own session.
/// Lives in its own file (no MAUI deps) so the testing project can link the
/// interface + pure DTO records and exercise consumers (e.g. <see cref="WalletSigningOrchestrator"/>)
/// without pulling in the full MAUI csproj.
/// </summary>
public interface IWalletApiClient
{
    Task<bool> EnsureLoggedInAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs login (if needed) + the inbox fetch and reports the outcome so the
    /// UI can distinguish a genuinely empty inbox from a login/network failure.
    /// </summary>
    Task<InboxResult> GetInboxAsync(CancellationToken cancellationToken = default);

    Task<WalletInboxItemMetaDto?> GetReviewMetaAsync(Guid recipientId, CancellationToken cancellationToken = default);

    Task<SignedDocInfo?> GetSignedDocInfoAsync(Guid signedDocumentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WalletHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls <c>GET /api/signing/{id}/status</c>. Returns <c>null</c> on network
    /// error or when the wallet is not authenticated.
    /// </summary>
    Task<SigningStatusSnapshot?> GetSigningStatusAsync(Guid signingRequestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the technical detail (hash prefix, certificate fingerprint, TSP name,
    /// algorithm, level) for the in-flight signing request. Returns <c>null</c> on
    /// failure so the caller can render placeholders.
    /// </summary>
    Task<TechnicalDetailDto?> GetTechnicalDetailAsync(Guid signingRequestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads raw PDF bytes from <c>GET /api/documents/{id}/download</c>.
    /// Bearer-authed via Wallet Session; guarded server-side by <c>IRecipientAccessGuard</c>.
    /// Returns <c>null</c> on auth failure or HTTP error.
    /// </summary>
    Task<byte[]?> DownloadDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// POSTs <c>/api/signing/prepare</c>. Returns <c>{ SigningRequestId, HashToSign }</c>
    /// or <c>null</c> on transport / non-2xx response so the caller can re-Prepare
    /// on retry rather than reuse a stale id. When <paramref name="renderCommitment"/>
    /// is non-null the six Render* fields ride on the payload and land in the
    /// signature dictionary inside the signed ByteRange (ADR-0008).
    /// </summary>
    Task<PrepareResult?> PrepareSigningAsync(
        Guid documentId,
        Guid recipientId,
        RenderCommitmentDto? renderCommitment = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// POSTs <c>/api/documents/{id}/render-commitment</c>. Server invokes the
    /// pinned PDFium binary in-process, computes the Merkle root over per-page
    /// bitmap hashes, returns the fields the wallet then stamps onto the next
    /// <see cref="PrepareSigningAsync"/> call. Returns <c>null</c> on transport
    /// error or any non-2xx (e.g. 503 when server has no pinned binary, 422 when
    /// document exceeds 50 pages, 409 when the document already has a prior
    /// signer). Null is a soft-degrade: the wallet proceeds with plain
    /// PAdES-B-LTA (no <c>/VeraSign.Render*</c> keys) and the document still signs.
    /// </summary>
    Task<RenderCommitmentDto?> GetRenderCommitmentAsync(
        Guid documentId, string locale, CancellationToken cancellationToken = default);

    /// <summary>
    /// POSTs <c>/api/signing/{id}/sign</c>. <paramref name="pin"/> is either the
    /// user-entered digits or the literal SAD marker <c>"bio-attested"</c>
    /// (biometric path); <paramref name="factor"/> is <c>"pin"</c> or
    /// <c>"biometric"</c> and lands in the audit trail. PIN value is never
    /// logged. See <see cref="SignResultMapper"/> for status → error-kind contract.
    /// </summary>
    Task<SignResult> SignAsync(
        Guid signingRequestId, string pin, string factor, CancellationToken cancellationToken = default);

    Task SignOutAsync();
}

public sealed record WalletInboxItem(
    Guid DocumentId,
    Guid RecipientId,
    string DocumentName,
    string SenderName,
    DateTime NotifiedAt,
    string Level,
    string DeepLink);

/// <summary>
/// Outcome of an inbox refresh. <see cref="Ok"/> false means login or the HTTP
/// fetch failed — distinct from <see cref="Ok"/> true with zero
/// <see cref="Items"/>, which is a genuinely empty inbox.
/// </summary>
public sealed record InboxResult(bool Ok, IReadOnlyList<WalletInboxItem> Items)
{
    public static readonly InboxResult Failed = new(false, Array.Empty<WalletInboxItem>());
    public static InboxResult Success(IReadOnlyList<WalletInboxItem> items) => new(true, items);
}

public sealed record SignedDocInfo(
    Guid Id,
    DateTime SignedAtUtc,
    string Level,
    string? TspName,
    string? SubjectCn,
    DateTime? TsaTime,
    string TxnId,
    string? RequestedLevel = null,
    string? CertificateSerial = null);

public sealed record WalletHistoryItem(
    Guid DocumentId,
    string DocumentName,
    string SenderName,
    DateTime SignedAtUtc,
    string Level,
    Guid SignedDocumentId);

/// <summary>
/// Wallet-local view of <c>SigningStatusResponse</c> + the wallet-relevant
/// extension columns the API exposes via <c>SigningRequest</c>. Kept here
/// instead of <c>MasterSTI.Shared</c> because the existing
/// <c>SigningStatusResponse</c> is owned by the API project and the API/Wallet
/// projects evolve at different cadences.
/// </summary>
public sealed record SigningStatusSnapshot(
    Guid SigningRequestId,
    string Status,
    string DocumentHash,
    Guid DocumentId,
    int? FailedAtStage,
    Guid? SignedDocumentId = null);
