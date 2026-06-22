using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// Builds the <c>vp_token</c> string for an OpenID4VP response.
///
/// Algorithm:
/// 1. Load the stored SD-JWT from SecureStorage.
/// 2. Split into issuer-JWT + disclosures.
/// 3. Build the canonical segment: <c>issuerJwt~disc1~disc2~...~discN</c> (no trailing ~).
/// 4. Compute <c>sd_hash = base64url(SHA-256(canonical))</c>.
/// 5. Build a KB-JWT header/payload, sign with the device key (IDeviceKeyService), and
///    produce the final three-part base64url token.
/// 6. Return <c>canonical~kbJwt</c>.
/// </summary>
public sealed class PresentationBuilder
{
    private readonly IDeviceKeyService _deviceKeyService;
    private readonly ILogger<PresentationBuilder> _logger;

    public PresentationBuilder(
        IDeviceKeyService deviceKeyService,
        ILogger<PresentationBuilder> logger)
    {
        _deviceKeyService = deviceKeyService;
        _logger = logger;
    }

    /// <summary>
    /// Builds the VP token. Returns <see langword="null"/> on any error.
    /// </summary>
    public async Task<string?> BuildAsync(OpenId4VpRequest request)
    {
        try
        {
            var storedSdJwt = await SecureStorage.GetAsync("wallet.sdjwt");
            if (string.IsNullOrEmpty(storedSdJwt))
            {
                _logger.LogWarning("No SD-JWT in SecureStorage — wallet not enrolled");
                return null;
            }

            // Split on '~' — first segment is the issuer JWT; rest are disclosures.
            // The stored value may optionally end with '~'; ignore trailing empty segments.
            var segments = storedSdJwt.Split('~', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 1)
            {
                _logger.LogWarning("Stored SD-JWT has no segments");
                return null;
            }

            var issuerJwt = segments[0];
            var allDisclosures = segments.Skip(1).ToArray();

            // Selective disclosure: emit only claims the verifier's allowlist
            // accepts. The verifier rejects unsolicited claims (GDPR data
            // minimisation). For now we hard-drop `birth_date`; the proper
            // long-term path is to parse `presentation_definition` and match
            // requested fields dynamically.
            var disclosures = allDisclosures
                .Where(d => !IsDisclosureClaim(d, "birth_date"))
                .ToArray();

            // Build canonical string: issuerJwt~d1~d2~...~dN  (no trailing ~)
            var sb = new StringBuilder(issuerJwt);
            foreach (var d in disclosures)
                sb.Append('~').Append(d);
            var canonical = sb.ToString();

            // sd_hash
            var hashBytes = SHA256.HashData(Encoding.ASCII.GetBytes(canonical));
            var sdHash = Base64UrlEncode(hashBytes);

            // Build KB-JWT
            var kbJwt = await BuildKbJwtAsync(request, sdHash);
            if (kbJwt is null)
                return null;

            return $"{canonical}~{kbJwt}";
        }
        catch (WalletKeyOrphanedException)
        {
            // Let the caller (WalletApiClient) handle re-enrollment recovery.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PresentationBuilder failed: {Type}", ex.GetType().Name);
            return null;
        }
    }

    private async Task<string?> BuildKbJwtAsync(OpenId4VpRequest request, string sdHash)
    {
        try
        {
            var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Header: {"alg":"ES256","typ":"kb+jwt"}
            var headerJson = "{\"alg\":\"ES256\",\"typ\":\"kb+jwt\"}";
            var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));

            // Payload: {"iat":<now>,"aud":"<clientId>","nonce":"<nonce>","sd_hash":"<hash>"}
            var payloadJson =
                $"{{\"iat\":{iat}," +
                $"\"aud\":\"{JsonStringEscape(request.ClientId)}\"," +
                $"\"nonce\":\"{JsonStringEscape(request.Nonce)}\"," +
                $"\"sd_hash\":\"{sdHash}\"}}";
            var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

            var signingInput = $"{headerB64}.{payloadB64}";
            var signingBytes = Encoding.UTF8.GetBytes(signingInput);

            // Sign — IDeviceKeyService returns raw r||s 64-byte ES256 signature
            var rawSig = await _deviceKeyService.SignAsync(signingBytes);
            var sigB64 = Base64UrlEncode(rawSig);

            return $"{signingInput}.{sigB64}";
        }
        catch (WalletKeyOrphanedException)
        {
            // Propagate so the caller can wipe the stored SD-JWT + session JWT
            // and route the user back through Onboarding.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KB-JWT construction failed: {Type}", ex.GetType().Name);
            return null;
        }
    }

    // ---- Helpers ---------------------------------------------------------------

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>
    /// SD-JWT disclosure = base64url(JSON array [salt, name, value]). Decodes the
    /// claim name and compares to <paramref name="claimName"/>. Returns false on
    /// any parsing error so a malformed disclosure is not silently dropped.
    /// </summary>
    private static bool IsDisclosureClaim(string disclosureB64, string claimName)
    {
        try
        {
            var padded = disclosureB64.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            var bytes = Convert.FromBase64String(padded);
            using var doc = System.Text.Json.JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array || doc.RootElement.GetArrayLength() < 2)
                return false;
            return doc.RootElement[1].GetString() == claimName;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Minimal JSON string escaping for known-safe claim values.</summary>
    private static string JsonStringEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
