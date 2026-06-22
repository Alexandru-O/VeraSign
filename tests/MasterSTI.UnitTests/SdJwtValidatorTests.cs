using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MasterSTI.Api.Common.Eudiw;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MasterSTI.UnitTests;

public class SdJwtValidatorTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);

    private SdJwtValidator CreateValidator(string verifierId = "https://verifier.test")
    {
        var opts = new EudiwOptions
        {
            VerifierId = verifierId,
            IssuerPublicKeyPem = _rsa.ExportSubjectPublicKeyInfoPem(),
            // These legacy tests build SD-JWTs without cnf.jwk and verify the KB-JWT against
            // the issuer key. Opt into the now-gated fallback so they keep exercising the
            // pre-cnf code path. Production keeps the default of false.
            AllowIssuerKeyKbFallback = true
        };
        return new SdJwtValidator(
            new StaticOptionsMonitor<EudiwOptions>(opts),
            NullLogger<SdJwtValidator>.Instance,
            httpFactory: null);
    }

    private string BuildSignedSdJwt(string nonce, string aud, string? overrideKbNonce = null)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var key = new RsaSecurityKey(_rsa) { KeyId = "test-issuer" };
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var now = DateTimeOffset.UtcNow;

        var disc1 = DisclosureFor("family_name", "Popescu");
        var disc2 = DisclosureFor("given_name", "Ion");
        var sdDigests = new[] { disc1, disc2 }
            .Select(d => Base64Url(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(d))))
            .ToArray();

        var payload = new Dictionary<string, object>
        {
            ["iss"] = "https://test-issuer",
            ["sub"] = "mock-sub-001",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
            ["nonce"] = nonce,
            ["_sd_alg"] = "sha-256",
            ["_sd"] = sdDigests
        };
        var headerB64 = Base64Url(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = "test-issuer" }));
        var payloadB64 = Base64Url(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload));
        var input = headerB64 + "." + payloadB64;
        var sig = _rsa.SignData(System.Text.Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var issuerJwt = input + "." + Base64Url(sig);

        var sdHash = ComputeSdHash(issuerJwt, new[] { disc1, disc2 });

        var kbNonce = overrideKbNonce ?? nonce;
        var kbToken = new JwtSecurityToken(
            issuer: null,
            audience: aud,
            claims: new[]
            {
                new Claim("nonce", kbNonce),
                new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("sd_hash", sdHash)
            },
            notBefore: now.AddMinutes(-1).UtcDateTime,
            expires: now.AddMinutes(5).UtcDateTime,
            signingCredentials: creds);
        kbToken.Header["typ"] = "kb+jwt";

        var kbJwt = handler.WriteToken(kbToken);

        return $"{issuerJwt}~{disc1}~{disc2}~{kbJwt}";
    }

    private static string ComputeSdHash(string issuerJwt, string[] disclosures)
    {
        var sb = new System.Text.StringBuilder(issuerJwt);
        foreach (var d in disclosures)
            sb.Append('~').Append(d);
        var hash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(sb.ToString()));
        return Base64Url(hash);
    }

    private static string DisclosureFor(string name, string value)
    {
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(8)).TrimEnd('=');
        var arr = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new object[] { salt, name, value });
        return Base64Url(arr);
    }

    private static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    [Fact]
    public void Validate_ValidToken_ExtractsClaims()
    {
        var validator = CreateValidator("https://verifier.test");
        var vpToken = BuildSignedSdJwt("test-nonce-123", "https://verifier.test");

        var claims = validator.ValidateAndExtract(vpToken, "test-nonce-123", "https://verifier.test");

        Assert.NotNull(claims);
        Assert.Equal("Popescu", claims!.FamilyName);
        Assert.Equal("Ion", claims.GivenName);
        Assert.Equal("mock-sub-001", claims.Subject);
    }

    [Fact]
    public void Validate_WrongNonce_ReturnsNull()
    {
        var validator = CreateValidator("https://verifier.test");
        var vpToken = BuildSignedSdJwt("correct-nonce", "https://verifier.test",
            overrideKbNonce: "wrong-nonce");

        var claims = validator.ValidateAndExtract(vpToken, "correct-nonce", "https://verifier.test");

        Assert.Null(claims);
    }

    [Fact]
    public void Validate_WrongAud_ReturnsNull()
    {
        var validator = CreateValidator("https://verifier.test");
        var vpToken = BuildSignedSdJwt("nonce", "https://wrong-aud.test");

        var claims = validator.ValidateAndExtract(vpToken, "nonce", "https://verifier.test");

        Assert.Null(claims);
    }

    [Fact]
    public void Validate_AlgNone_Rejected()
    {
        var validator = CreateValidator("https://verifier.test");

        // Build an alg=none token
        var header = Base64Url(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { alg = "none", typ = "JWT" }));
        var payload = Base64Url(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = "x",
            sub = "x",
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            nonce = "n"
        }));
        var vpToken = $"{header}.{payload}.~";

        var claims = validator.ValidateAndExtract(vpToken, "n", "https://verifier.test");
        Assert.Null(claims);
    }

    [Fact]
    public void Validate_WrongSigningKey_Rejected()
    {
        using var otherRsa = RSA.Create(2048);
        var opts = new EudiwOptions
        {
            VerifierId = "https://verifier.test",
            IssuerPublicKeyPem = otherRsa.ExportSubjectPublicKeyInfoPem()
        };
        var validator = new SdJwtValidator(
            new StaticOptionsMonitor<EudiwOptions>(opts),
            NullLogger<SdJwtValidator>.Instance,
            httpFactory: null);

        var vpToken = BuildSignedSdJwt("n", "https://verifier.test");
        var claims = validator.ValidateAndExtract(vpToken, "n", "https://verifier.test");
        Assert.Null(claims);
    }

    [Fact]
    public void Validate_EmptyToken_ReturnsNull()
    {
        var validator = CreateValidator();
        var claims = validator.ValidateAndExtract("", "nonce", "aud");
        Assert.Null(claims);
    }

    [Fact]
    public void Validate_MalformedToken_ReturnsNull()
    {
        var validator = CreateValidator();
        var claims = validator.ValidateAndExtract("not.a.valid.sdJwt", "nonce", "aud");
        Assert.Null(claims);
    }

    public void Dispose() => _rsa.Dispose();
}

internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string> listener) => null;
}
