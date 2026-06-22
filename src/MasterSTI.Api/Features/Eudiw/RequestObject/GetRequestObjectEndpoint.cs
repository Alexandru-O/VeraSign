using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Api.Features.Wallet.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MasterSTI.Api.Features.Eudiw.RequestObject;

/// <summary>
/// Serves the OpenID4VP authorization request as a signed JWT (ADR-0011) so the
/// wallet can cryptographically authenticate the verifier identity before
/// releasing PID claims. The QR carries only <c>client_id</c> + <c>request_uri</c>;
/// the wallet GETs this endpoint, validates the JWS against its pinned EC P-256
/// public key (<c>client_id_scheme=pre-registered</c>), then issues the VP
/// response to <c>/api/eudiw/response</c>.
/// </summary>
public static class GetRequestObjectEndpoint
{
    public static IEndpointRouteBuilder MapGetRequestObject(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/eudiw/request-object/{state}", (
            string state,
            OpenId4VpService openId4Vp,
            RequestObjectSigner signer,
            IOptions<EudiwOptions> eudiwOptions,
            IConfiguration config,
            IMemoryCache cache) =>
        {
            if (!cache.TryGetValue(WalletAuthCacheKeys.ForState(state), out WalletAuthEntry? entry) || entry is null)
                return Results.NotFound(new { error = "Unknown or expired state" });

            if (DateTime.UtcNow > entry.ExpiresAtUtc)
                return Results.NotFound(new { error = "State expired" });

            // Reconstruct the same authorization request the QR would have inlined.
            var authReq = openId4Vp.CreateAuthorizationRequest(entry.Nonce, state);

            // ADR-0011: signed JWT replaces plaintext JSON. Fail-closed when the
            // signing key is not configured rather than silently downgrading to
            // the prior unsigned shape.
            if (!signer.IsConfigured)
                return Results.Problem(
                    title: "Request object signing key not configured",
                    detail: "Eudiw:RequestObjectSigning:PrivateKeyPem is missing. Run start-all.ps1 -Publish or set the value via user-secrets / env var.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            // iss == effective verifier identity. PublicBaseUrl wins when set
            // (Android emulator routing); falls back to VerifierId. Mirrors the
            // logic in OpenId4VpService so iss and client_id always match.
            var publicBase = config["Eudiw:PublicBaseUrl"];
            var issuer = !string.IsNullOrWhiteSpace(publicBase)
                ? publicBase.TrimEnd('/')
                : eudiwOptions.Value.VerifierId;

            var jwt = signer.Sign(authReq, issuer);
            return Results.Text(jwt, RequestObjectSigner.ContentType);
        })
        .AllowAnonymous()
        .WithName("GetEudiwRequestObject")
        .WithTags("Eudiw");

        return app;
    }
}
