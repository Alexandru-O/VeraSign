using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MasterSTI.Api.Common.Eudiw;

/// <summary>
/// ADR-0011: signs the OpenID4VP <c>request_object</c> as an ES256 JWT.
/// The wallet validates the signature against a pinned EC P-256 public key
/// (<c>client_id_scheme=pre-registered</c>) before consuming any field from
/// the payload — verifier authentication that the prior plaintext-JSON
/// endpoint did not provide.
/// </summary>
public sealed class RequestObjectSigner
{
    public const string JwtTyp = "oauth-authz-req+jwt";
    public const string ContentType = "application/oauth-authz-req+jwt";

    private readonly RequestObjectSigningOptions _options;
    private readonly ILogger<RequestObjectSigner> _logger;

    public RequestObjectSigner(IOptions<RequestObjectSigningOptions> options, ILogger<RequestObjectSigner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>True iff a private key PEM is configured. Endpoint flips to 503 when false.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.PrivateKeyPem);

    /// <summary>
    /// Builds a JWS Compact-serialised JWT carrying the OID4VP claims plus
    /// <c>iss</c>/<c>iat</c>/<c>exp</c>/<c>aud</c> and <c>client_id_scheme</c>.
    /// </summary>
    public string Sign(AuthorizationRequest authReq, string issuer)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Eudiw:RequestObjectSigning:PrivateKeyPem is not configured. " +
                "Run start-all.ps1 -Publish (auto-injects a demo key) or set the value via user-secrets / env var.");

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(_options.PrivateKeyPem);

        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = iat + _options.ExpiresInSeconds;

        var headerJson = JsonSerializer.Serialize(new
        {
            alg = "ES256",
            typ = JwtTyp,
            kid = _options.Kid,
        });

        // Payload mirrors the OID4VP claims plus standard JWT timestamps.
        // client_id_scheme is the spec hook the wallet uses to pick the
        // verifier-authentication path (ADR-0011: pre-registered).
        var payload = new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["aud"] = "wallet",
            ["iat"] = iat,
            ["exp"] = exp,
            ["client_id"] = authReq.ClientId,
            ["client_id_scheme"] = "pre-registered",
            ["response_type"] = authReq.ResponseType,
            ["response_mode"] = authReq.ResponseMode,
            ["response_uri"] = authReq.ResponseUri,
            ["nonce"] = authReq.Nonce,
            ["state"] = authReq.State,
            ["presentation_definition"] = authReq.PresentationDefinition,
        };
        var payloadJson = JsonSerializer.Serialize(payload);

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = $"{headerB64}.{payloadB64}";

        // JOSE ES256 signatures are raw r||s (each 32 B) — NOT DER. .NET's
        // SignData with IeeeP1363FixedFieldConcatenation emits the JOSE shape
        // directly; using the default (DER) would produce a ~70 B signature
        // that the wallet would reject during verification.
        var sigBytes = ecdsa.SignData(
            Encoding.UTF8.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var sigB64 = Base64UrlEncode(sigBytes);

        _logger.LogDebug("Signed OID4VP request_object kid={Kid} iat={Iat} exp={Exp}",
            _options.Kid, iat, exp);

        return $"{signingInput}.{sigB64}";
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
