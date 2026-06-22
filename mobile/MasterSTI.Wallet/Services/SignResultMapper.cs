using System.Net;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// Pure HTTP-status / exception → <see cref="SignResult"/> mapping. Lives in its
/// own file (no MAUI deps) so unit tests can link the source directly without
/// pulling in the full Wallet csproj. Single source of truth for the contract
/// that PinPage's lockout counter and the wallet error overlays rely on.
/// </summary>
public static class SignResultMapper
{
    public static SignResult MapHttp(HttpStatusCode status, SignedDocResponse? body)
    {
        if ((int)status is >= 200 and < 300)
        {
            return body is null
                ? SignResult.Failure(SignErrorKind.Server, "Server returned empty body.")
                : SignResult.Success(body.SignedDocumentId, body.PadesLevel);
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return SignResult.Failure(SignErrorKind.PinRejected, "PIN respins de QTSP.");

        if ((int)status >= 500)
            return SignResult.Failure(SignErrorKind.Server, $"Server error (HTTP {(int)status}).");

        return SignResult.Failure(SignErrorKind.QtspError, $"Eroare QTSP (HTTP {(int)status}).");
    }

    public static SignResult MapException(Exception ex) => ex switch
    {
        HttpRequestException => SignResult.Failure(SignErrorKind.Network, "Eroare de rețea."),
        TaskCanceledException => SignResult.Failure(SignErrorKind.Network, "Conexiunea a expirat."),
        _ => SignResult.Failure(SignErrorKind.QtspError, ex.Message),
    };
}
