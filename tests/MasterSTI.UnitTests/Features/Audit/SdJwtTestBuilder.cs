using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace MasterSTI.UnitTests.Features.Audit;

/// <summary>
/// Builds key-bound SD-JWT presentations for tests that exercise
/// <see cref="MasterSTI.Api.Features.Eudiw.HandleResponse.HandleVpResponseHandler"/>.
/// Mirrors the helper pattern in <c>SdJwtKeyBindingTests</c> but lives in its
/// own file so the audit tests can build presentations without depending on
/// private statics in that test class.
/// </summary>
internal static class SdJwtTestBuilder
{
    public static string BuildKeyBoundSdJwt(
        RSA issuerRsa,
        ECDsa walletEc,
        string aud,
        string nonce,
        string familyName = "Popescu",
        string givenName = "Ion",
        string? email = null)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        var now = DateTimeOffset.UtcNow;

        // Disclosures must exist before the issuer JWT so we can commit to their
        // digests via `_sd` (SD-JWT §4.2.4). Otherwise the validator rejects.
        var disclosures = new List<string>
        {
            DisclosureFor("family_name", familyName),
            DisclosureFor("given_name", givenName)
        };
        if (!string.IsNullOrEmpty(email))
            disclosures.Add(DisclosureFor("email", email));
        var sdDigests = disclosures
            .Select(d => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(d))))
            .ToArray();

        var p = walletEc.ExportParameters(includePrivateParameters: false);
        var jwk = new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64Url(p.Q.X!),
            ["y"] = Base64Url(p.Q.Y!)
        };

        var payload = new Dictionary<string, object>
        {
            ["iss"] = "https://test-issuer",
            ["sub"] = email ?? "test-subject",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
            ["nonce"] = nonce,
            ["_sd_alg"] = "sha-256",
            ["_sd"] = sdDigests,
            ["cnf"] = new Dictionary<string, object> { ["jwk"] = jwk }
        };
        var issuerJwt = EncodeRsaJwt(issuerRsa, "test-issuer", payload);

        var sdHash = ComputeSdHash(issuerJwt, disclosures);

        var kbCreds = new SigningCredentials(
            new ECDsaSecurityKey(walletEc) { KeyId = "wallet-ec" },
            SecurityAlgorithms.EcdsaSha256);

        var kbClaims = new List<Claim>
        {
            new("nonce", nonce),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("sd_hash", sdHash)
        };

        var kbToken = new JwtSecurityToken(
            issuer: null,
            audience: aud,
            claims: kbClaims,
            notBefore: now.AddMinutes(-1).UtcDateTime,
            expires: now.AddMinutes(5).UtcDateTime,
            signingCredentials: kbCreds);
        kbToken.Header["typ"] = "kb+jwt";

        var kbJwt = handler.WriteToken(kbToken);

        var parts = new List<string> { issuerJwt };
        parts.AddRange(disclosures);
        parts.Add(kbJwt);
        return string.Join('~', parts);
    }

    private static string DisclosureFor(string name, string value)
    {
        var salt = Base64Url(RandomNumberGenerator.GetBytes(16));
        var arr = JsonSerializer.SerializeToUtf8Bytes(new object[] { salt, name, value });
        return Base64Url(arr);
    }

    private static string ComputeSdHash(string issuerJwt, IEnumerable<string> disclosures)
    {
        var sb = new StringBuilder(issuerJwt);
        foreach (var d in disclosures)
            sb.Append('~').Append(d);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(sb.ToString()));
        return Base64Url(hash);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string EncodeRsaJwt(RSA signingKey, string kid, Dictionary<string, object> payload)
    {
        var header = new Dictionary<string, object>
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT",
            ["kid"] = kid
        };
        var headerB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var input = headerB64 + "." + payloadB64;
        var sig = signingKey.SignData(Encoding.ASCII.GetBytes(input),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return input + "." + Base64Url(sig);
    }
}
