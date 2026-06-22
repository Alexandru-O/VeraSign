using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterSTI.Api.Common.Eudiw;
using MasterSTI.UnitTests; // StaticOptionsMonitor<T> from SdJwtValidatorTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace MasterSTI.UnitTests.Features.Eudiw;

/// <summary>
/// Issue-9 KB-JWT iat skew contract:
///   * Default <see cref="EudiwOptions.KbJwtIatSkewSeconds"/> is 60.
///   * iat older than <c>KbJwtIatSkewSeconds</c> is rejected.
///   * iat more than 5s in the future is rejected (tight forward-skew; an
///     iat that drifts into the future widens the replay window, so it is
///     not symmetric with the backward window).
///   * iat between -<c>KbJwtIatSkewSeconds</c>s and +5s is accepted.
/// </summary>
public class KbJwtIatSkewTests : IDisposable
{
    private const string VerifierId = "https://verifier.test";
    private const string Nonce = "iat-skew-nonce";

    private readonly RSA _issuerRsa = RSA.Create(2048);
    private readonly ECDsa _walletEc = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    [Fact]
    public void DefaultKbJwtIatSkewSeconds_Is60()
    {
        var opts = new EudiwOptions();
        Assert.Equal(60, opts.KbJwtIatSkewSeconds);
    }

    [Fact]
    public void Iat_WithinWindow_Accepted()
    {
        var validator = CreateValidator(skewSeconds: 60);
        // iat = now (offset 0) — well inside ±60s.
        var vpToken = BuildVpToken(kbIatOffsetSeconds: 0);

        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.NotNull(claims);
    }

    [Fact]
    public void Iat_OlderThanSkewWindow_Rejected()
    {
        var validator = CreateValidator(skewSeconds: 60);
        // KB-JWT signed 120s ago — outside the 60s window.
        var vpToken = BuildVpToken(kbIatOffsetSeconds: -120);

        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.Null(claims);
    }

    [Fact]
    public void Iat_FurtherThanSkewWindow_InFuture_Rejected()
    {
        var validator = CreateValidator(skewSeconds: 60);
        // KB-JWT iat is +120s in the future — outside the 60s forward window.
        var vpToken = BuildVpToken(kbIatOffsetSeconds: +120);

        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.Null(claims);
    }

    [Fact]
    public void Iat_JustInsideBackwardEdge_Accepted()
    {
        // -30s is well within the 60s window.
        var validator = CreateValidator(skewSeconds: 60);
        var vpToken = BuildVpToken(kbIatOffsetSeconds: -30);

        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.NotNull(claims);
    }

    [Fact]
    public void Iat_ModestForwardSkew_Rejected()
    {
        // Forward skew is intentionally tighter than the backward window.
        // +30s is honest-clock-drift territory under the symmetric rule but
        // outside the post-hardening 5s forward allowance.
        var validator = CreateValidator(skewSeconds: 60);
        var vpToken = BuildVpToken(kbIatOffsetSeconds: +30);

        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.Null(claims);
    }

    private SdJwtValidator CreateValidator(int skewSeconds)
    {
        var opts = new EudiwOptions
        {
            VerifierId = VerifierId,
            IssuerPublicKeyPem = _issuerRsa.ExportSubjectPublicKeyInfoPem(),
            KbJwtIatSkewSeconds = skewSeconds
        };
        return new SdJwtValidator(
            new StaticOptionsMonitor<EudiwOptions>(opts),
            NullLogger<SdJwtValidator>.Instance,
            httpFactory: null);
    }

    private string BuildVpToken(int kbIatOffsetSeconds)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var now = DateTimeOffset.UtcNow;
        var kbNow = now.AddSeconds(kbIatOffsetSeconds);

        var p = _walletEc.ExportParameters(includePrivateParameters: false);
        var cnfJwk = new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64Url(p.Q.X!),
            ["y"] = Base64Url(p.Q.Y!)
        };

        var disc1 = Disclosure("family_name", "Popescu");
        var disc2 = Disclosure("given_name", "Ion");
        var disclosures = new[] { disc1, disc2 };
        var sdDigests = disclosures
            .Select(d => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(d))))
            .ToArray();

        var payload = new Dictionary<string, object>
        {
            ["iss"] = "https://test-issuer",
            ["sub"] = "iat-skew-subject",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
            ["nonce"] = Nonce,
            ["_sd_alg"] = "sha-256",
            ["_sd"] = sdDigests,
            ["cnf"] = new Dictionary<string, object> { ["jwk"] = cnfJwk }
        };
        var headerB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = "test-issuer" }));
        var payloadB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var input = headerB64 + "." + payloadB64;
        var sig = _issuerRsa.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var issuerJwt = input + "." + Base64Url(sig);

        var sdHash = ComputeSdHash(issuerJwt, disclosures);

        var kbCreds = new SigningCredentials(
            new ECDsaSecurityKey(_walletEc) { KeyId = "wallet-ec" },
            SecurityAlgorithms.EcdsaSha256);

        var kbClaims = new List<Claim>
        {
            new("nonce", Nonce),
            new(JwtRegisteredClaimNames.Iat, kbNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("sd_hash", sdHash)
        };

        var kbToken = new JwtSecurityToken(
            issuer: null,
            audience: VerifierId,
            claims: kbClaims,
            notBefore: kbNow.AddMinutes(-10).UtcDateTime,
            expires: kbNow.AddMinutes(10).UtcDateTime,
            signingCredentials: kbCreds);
        kbToken.Header["typ"] = "kb+jwt";

        var kbJwt = handler.WriteToken(kbToken);

        return string.Join('~', new[] { issuerJwt }.Concat(disclosures).Concat(new[] { kbJwt }));
    }

    private static string Disclosure(string name, string value)
    {
        var salt = Base64Url(RandomNumberGenerator.GetBytes(16));
        var arr = JsonSerializer.SerializeToUtf8Bytes(new object[] { salt, name, value });
        return Base64Url(arr);
    }

    private static string ComputeSdHash(string issuerJwt, string[] disclosures)
    {
        var sb = new StringBuilder(issuerJwt);
        foreach (var d in disclosures)
            sb.Append('~').Append(d);
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(sb.ToString())));
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public void Dispose()
    {
        _issuerRsa.Dispose();
        _walletEc.Dispose();
    }
}
