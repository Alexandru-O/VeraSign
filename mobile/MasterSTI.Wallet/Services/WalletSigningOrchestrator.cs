namespace MasterSTI.Wallet.Services;

/// <summary>
/// Testable seam between MAUI pages (ConsentPage, PinPage) and the wallet's
/// API client. Wraps the Prepare → Sign chain so pages stay thin views with
/// no business logic.
/// </summary>
public interface IWalletSigningOrchestrator
{
    /// <summary>
    /// Biometric-attested path. Sends the literal SAD value <c>"bio-attested"</c>
    /// and audit factor <c>"biometric"</c> to the server. See ADR-0007.
    /// </summary>
    Task<OrchestratorResult> SignWithBiometricAsync(
        Guid documentId, Guid recipientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// PIN-fallback path. Sends the user-entered digits as SAD with audit
    /// factor <c>"pin"</c>.
    /// </summary>
    Task<OrchestratorResult> SignWithPinAsync(
        Guid documentId, Guid recipientId, string pin, CancellationToken cancellationToken = default);
}

/// <summary>
/// Combined Prepare + Sign outcome. <see cref="PrepareFailed"/> is true when
/// Prepare returned null (transport / 4xx before SAD was ever sent); in that
/// case <see cref="SignResult"/> is null and the page should re-Prepare on
/// retry rather than reuse a stale SigningRequest id.
/// </summary>
public sealed record OrchestratorResult(
    Guid? SigningRequestId,
    string? DocumentHashHex,
    SignResult? SignResult,
    bool PrepareFailed)
{
    public static OrchestratorResult PrepareError() =>
        new(null, null, null, true);

    public static OrchestratorResult From(PrepareResult prep, SignResult sign) =>
        new(prep.SigningRequestId, prep.HashToSign, sign, false);
}

public sealed class WalletSigningOrchestrator : IWalletSigningOrchestrator
{
    private const string BiometricSadMarker = "bio-attested";
    private const string FactorBiometric = "biometric";
    private const string FactorPin = "pin";

    private readonly IWalletApiClient _api;
    private readonly IRenderCommitmentCarrier? _renderCommitments;

    // Optional carrier so the unit tests can construct an orchestrator without
    // a render-commitment dependency. Production DI registers the singleton.
    public WalletSigningOrchestrator(
        IWalletApiClient api,
        IRenderCommitmentCarrier? renderCommitments = null)
    {
        _api = api;
        _renderCommitments = renderCommitments;
    }

    public Task<OrchestratorResult> SignWithBiometricAsync(
        Guid documentId, Guid recipientId, CancellationToken cancellationToken = default) =>
        RunAsync(documentId, recipientId, BiometricSadMarker, FactorBiometric, cancellationToken);

    public Task<OrchestratorResult> SignWithPinAsync(
        Guid documentId, Guid recipientId, string pin, CancellationToken cancellationToken = default) =>
        RunAsync(documentId, recipientId, pin, FactorPin, cancellationToken);

    private async Task<OrchestratorResult> RunAsync(
        Guid documentId, Guid recipientId, string sad, string factor, CancellationToken cancellationToken)
    {
        // Render Commitment (ADR-0008) is optional: a null carrier or a missing
        // slot means the wallet falls back to plain PAdES-B-LTA without any
        // /VeraSign.Render* dictionary keys. Server validator accepts both shapes.
        var commitment = _renderCommitments?.Get(documentId);

        var prep = await _api.PrepareSigningAsync(documentId, recipientId, commitment, cancellationToken);
        if (prep is null)
            return OrchestratorResult.PrepareError();

        var sign = await _api.SignAsync(prep.SigningRequestId, sad, factor, cancellationToken);
        return OrchestratorResult.From(prep, sign);
    }
}
