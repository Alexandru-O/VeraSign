using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// ADR-0011: validates the signed OpenID4VP <c>request_object</c> JWT served by
/// <c>/api/eudiw/request-object/{state}</c>. The wallet pins the verifier's EC P-256
/// public key (<c>client_id_scheme=pre-registered</c>) and refuses to consume any
/// claim from a token whose signature, kid, alg, iat/exp window, aud, or client_id
/// does not match expectations. Pure <c>System.Security.Cryptography</c> — no MAUI
/// deps — so the test project can link this source directly via the same pattern
/// used for <c>SdJwtParser</c>.
/// </summary>
public static class RequestObjectVerifier
{
    private const string ExpectedAlg = "ES256";
    private const string ExpectedTyp = "oauth-authz-req+jwt";
    private const string ExpectedAud = "wallet";
    private const string ExpectedClientIdScheme = "pre-registered";

    /// <summary>
    /// Verifies the JWT and returns the parsed claims. Throws
    /// <see cref="RequestObjectVerificationException"/> on any failure
    /// (caller maps to a login-failure path).
    /// </summary>
    /// <param name="jwt">JWS Compact-serialised token from the request-object endpoint.</param>
    /// <param name="pinnedPublicKeyPem">PEM-encoded EC P-256 public key (SubjectPublicKeyInfo).</param>
    /// <param name="expectedKid">Pinned <c>kid</c> header value.</param>
    /// <param name="expectedClientId">Pinned verifier identity (<c>iss</c> and <c>client_id</c> must both equal this).</param>
    /// <param name="iatSkewSeconds">Acceptable clock skew either side of the wallet clock. Default 60 s.</param>
    /// <param name="utcNow">Override for tests; defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
    public static RequestObjectClaims VerifyAndParse(
        string jwt,
        string pinnedPublicKeyPem,
        string expectedKid,
        string expectedClientId,
        int iatSkewSeconds = 60,
        DateTimeOffset? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(jwt))
            throw new RequestObjectVerificationException("Empty request_object JWT");
        var parts = jwt.Split('.');
        if (parts.Length != 3)
            throw new RequestObjectVerificationException($"Malformed JWS (expected 3 parts, got {parts.Length})");

        // 1. Header: alg=ES256 (literal — no algorithm negotiation), typ, kid pin.
        JsonElement header;
        try
        {
            header = JsonDocument.Parse(Base64UrlDecode(parts[0])).RootElement;
        }
        catch (Exception ex)
        {
            throw new RequestObjectVerificationException("Header JSON parse failed", ex);
        }

        var alg = TryGetString(header, "alg");
        if (!string.Equals(alg, ExpectedAlg, StringComparison.Ordinal))
            throw new RequestObjectVerificationException($"Unexpected alg '{alg}' (require ES256)");

        var typ = TryGetString(header, "typ");
        if (!string.Equals(typ, ExpectedTyp, StringComparison.Ordinal))
            throw new RequestObjectVerificationException($"Unexpected typ '{typ}'");

        var kid = TryGetString(header, "kid");
        if (!string.Equals(kid, expectedKid, StringComparison.Ordinal))
            throw new RequestObjectVerificationException($"kid '{kid}' does not match pinned '{expectedKid}'");

        // 2. Signature: verify against pinned public key BEFORE parsing payload,
        // so we never read attacker-controlled fields off an untrusted token.
        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        byte[] sig;
        try
        {
            sig = Base64UrlDecode(parts[2]);
        }
        catch (Exception ex)
        {
            throw new RequestObjectVerificationException("Signature base64 decode failed", ex);
        }

        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(pinnedPublicKeyPem);
        }
        catch (Exception ex)
        {
            throw new RequestObjectVerificationException("Pinned public key PEM import failed", ex);
        }

        // JOSE ES256 signature is raw r||s — must verify with IeeeP1363, not DER.
        var ok = ecdsa.VerifyData(
            signingInput, sig,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (!ok)
            throw new RequestObjectVerificationException("Signature verification failed");

        // 3. Payload claims: iss, aud, iat/exp window, client_id, client_id_scheme.
        JsonElement payload;
        try
        {
            payload = JsonDocument.Parse(Base64UrlDecode(parts[1])).RootElement;
        }
        catch (Exception ex)
        {
            throw new RequestObjectVerificationException("Payload JSON parse failed", ex);
        }

        var aud = TryGetString(payload, "aud");
        if (!string.Equals(aud, ExpectedAud, StringComparison.Ordinal))
            throw new RequestObjectVerificationException($"Unexpected aud '{aud}'");

        var scheme = TryGetString(payload, "client_id_scheme");
        if (!string.Equals(scheme, ExpectedClientIdScheme, StringComparison.Ordinal))
            throw new RequestObjectVerificationException($"Unexpected client_id_scheme '{scheme}'");

        var iss = TryGetString(payload, "iss");
        if (!string.Equals(iss, expectedClientId, StringComparison.Ordinal))
            throw new RequestObjectVerificationException($"iss '{iss}' does not match pinned '{expectedClientId}'");

        var clientId = TryGetString(payload, "client_id");
        if (!string.Equals(clientId, expectedClientId, StringComparison.Ordinal))
            throw new RequestObjectVerificationException($"client_id '{clientId}' does not match pinned '{expectedClientId}'");

        var iat = TryGetInt64(payload, "iat") ?? throw new RequestObjectVerificationException("Missing iat");
        var exp = TryGetInt64(payload, "exp") ?? throw new RequestObjectVerificationException("Missing exp");
        var now = (utcNow ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        if (iat > now + iatSkewSeconds)
            throw new RequestObjectVerificationException($"iat {iat} is future-skewed beyond {iatSkewSeconds}s of wallet clock {now}");
        if (exp <= now - iatSkewSeconds)
            throw new RequestObjectVerificationException($"exp {exp} elapsed (wallet clock {now})");

        var nonce = TryGetString(payload, "nonce") ?? throw new RequestObjectVerificationException("Missing nonce");
        var state = TryGetString(payload, "state") ?? throw new RequestObjectVerificationException("Missing state");
        var responseType = TryGetString(payload, "response_type") ?? throw new RequestObjectVerificationException("Missing response_type");
        var responseMode = TryGetString(payload, "response_mode") ?? throw new RequestObjectVerificationException("Missing response_mode");
        var responseUri = TryGetString(payload, "response_uri") ?? throw new RequestObjectVerificationException("Missing response_uri");

        // presentation_definition stays as a raw JSON snippet — the wallet
        // hands it straight to PresentationBuilder which already parses it.
        if (!payload.TryGetProperty("presentation_definition", out var pd))
            throw new RequestObjectVerificationException("Missing presentation_definition");
        var presentationDefinitionJson = pd.GetRawText();

        return new RequestObjectClaims(
            ClientId: clientId!,
            ResponseUri: responseUri,
            Nonce: nonce,
            State: state,
            ResponseType: responseType,
            ResponseMode: responseMode,
            PresentationDefinitionJson: presentationDefinitionJson);
    }

    private static string? TryGetString(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static long? TryGetInt64(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var v) ? v : null;
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}

public sealed record RequestObjectClaims(
    string ClientId,
    string ResponseUri,
    string Nonce,
    string State,
    string ResponseType,
    string ResponseMode,
    string PresentationDefinitionJson);

public sealed class RequestObjectVerificationException : Exception
{
    public RequestObjectVerificationException(string message) : base(message) { }
    public RequestObjectVerificationException(string message, Exception inner) : base(message, inner) { }
}
