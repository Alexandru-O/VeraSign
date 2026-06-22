namespace MasterSTI.Wallet.Services;

/// <summary>
/// Outcome of <see cref="IWalletApiClient.PrepareSigningAsync"/>. Null when the
/// call failed at the transport level; callers should re-prepare rather than
/// silently retry to keep server <c>SigningRequest</c> state honest.
/// </summary>
public sealed record PrepareResult(Guid SigningRequestId, string HashToSign);

/// <summary>
/// Outcome of <see cref="IWalletApiClient.SignAsync"/>. Always returned (never
/// null) so the caller can distinguish "auth rejected by QTSP" (burns a PIN
/// attempt) from "network jitter" (free retry) via <see cref="Error"/>.
/// </summary>
public sealed record SignResult(
    bool Ok,
    Guid? SignedDocumentId,
    string? PadesLevel,
    SignError? Error)
{
    public static SignResult Success(Guid signedDocumentId, string padesLevel) =>
        new(true, signedDocumentId, padesLevel, null);

    public static SignResult Failure(SignErrorKind kind, string message) =>
        new(false, null, null, new SignError(kind, message));
}

public sealed record SignError(SignErrorKind Kind, string Message);

/// <summary>
/// Error categories that drive wallet-side retry/lockout policy. Only
/// <see cref="PinRejected"/> burns a PIN attempt (3 → 30 s lockout per ADR-0007);
/// the rest are transient.
/// </summary>
public enum SignErrorKind
{
    /// <summary>QTSP rejected the credential authorisation (HTTP 401/403).</summary>
    PinRejected,
    /// <summary>QTSP-side application error (4xx other than auth) — bad state, conflict, etc.</summary>
    QtspError,
    /// <summary>Transport failure — no response, timeout, DNS, etc. Free to retry.</summary>
    Network,
    /// <summary>Server-side fault (5xx) — operator issue, not user error.</summary>
    Server,
}

/// <summary>
/// Wire shape of <c>SignDocumentResponse</c> from <c>POST /api/signing/{id}/sign</c>.
/// Lives wallet-side so tests can build it without referencing the API DTO.
/// </summary>
public sealed record SignedDocResponse(Guid SignedDocumentId, string PadesLevel);
