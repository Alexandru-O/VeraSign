using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterSTI.Wallet.Services;
using Microsoft.IdentityModel.Tokens;

namespace MasterSTI.UnitTests.Wallet;

/// <summary>
/// Wallet-side parser: read-only, no crypto verify. Builds canonical issuer-minted
/// SD-JWTs the same way <see cref="SdJwtKeyBindingTests"/> does, plus a real
/// <c>_sd</c> array so the parser can hash-check disclosures and detect tampering.
/// </summary>
public sealed class SdJwtParserTests : IDisposable
{
    private readonly RSA _issuerRsa = RSA.Create(2048);

    [Fact]
    public void Parse_CanonicalFixture_SurfacesFamilyGivenEmailNbfExp()
    {
        var nbf = DateTimeOffset.UtcNow.AddMinutes(-1);
        var exp = DateTimeOffset.UtcNow.AddHours(1);

        var sdJwt = BuildSdJwt(
            _issuerRsa,
            new[] {
                ("family_name", "Popescu"),
                ("given_name", "Ion"),
                ("email", "ion.popescu@verasign.demo")
            },
            nbf: nbf,
            exp: exp);

        var parsed = SdJwtParser.Parse(sdJwt);

        Assert.Equal("Popescu", parsed.FamilyName);
        Assert.Equal("Ion", parsed.GivenName);
        Assert.Equal("ion.popescu@verasign.demo", parsed.Email);
        Assert.NotNull(parsed.Nbf);
        Assert.NotNull(parsed.Exp);
        Assert.Equal(nbf.ToUnixTimeSeconds(), parsed.Nbf!.Value.ToUnixTimeSeconds());
        Assert.Equal(exp.ToUnixTimeSeconds(), parsed.Exp!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void Parse_TamperedDisclosure_Throws()
    {
        // Build a valid SD-JWT, then splice in a foreign disclosure that does not
        // appear in the issuer JWT's _sd array. Parser must reject.
        var sdJwt = BuildSdJwt(
            _issuerRsa,
            new[] {
                ("family_name", "Popescu"),
                ("given_name", "Ion")
            });

        // Forge an extra "email" disclosure that the issuer never committed to.
        var forged = MakeDisclosure("email", "attacker@example.com");
        var parts = sdJwt.Split('~', StringSplitOptions.RemoveEmptyEntries);
        // parts[0] = issuer JWT; rest = disclosures. Append forged before trailing '~'.
        var tampered = string.Join('~', parts) + "~" + forged + "~";

        Assert.Throws<SdJwtFormatException>(() => SdJwtParser.Parse(tampered));
    }

    [Fact]
    public void Parse_MalformedJwt_Throws()
    {
        // No "." separators — issuer JWT has no payload segment.
        const string malformed = "not-a-jwt~ZmFtaWx5X25hbWU=~";

        Assert.Throws<SdJwtFormatException>(() => SdJwtParser.Parse(malformed));
    }

    [Fact]
    public void Parse_EmptyString_Throws()
    {
        Assert.Throws<SdJwtFormatException>(() => SdJwtParser.Parse(string.Empty));
    }

    // --- fixture builder --------------------------------------------------

    /// <summary>
    /// Builds an SD-JWT presentation with a spec-compliant <c>_sd</c> array on the
    /// issuer JWT payload. Disclosures are appended after the issuer JWT separated
    /// by <c>~</c>, with a trailing <c>~</c> per SD-JWT serialization.
    /// </summary>
    private static string BuildSdJwt(
        RSA issuerRsa,
        IEnumerable<(string name, string value)> claims,
        DateTimeOffset? nbf = null,
        DateTimeOffset? exp = null)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var creds = new SigningCredentials(
            new RsaSecurityKey(issuerRsa) { KeyId = "test-issuer" },
            SecurityAlgorithms.RsaSha256);

        var disclosures = claims.Select(c => MakeDisclosure(c.name, c.value)).ToArray();
        var sdDigests = disclosures.Select(HashDisclosure).ToArray();

        var sdArrayJson = "[" + string.Join(",", sdDigests.Select(d => $"\"{d}\"")) + "]";

        var now = DateTimeOffset.UtcNow;
        var notBefore = (nbf ?? now.AddMinutes(-1)).UtcDateTime;
        var expires = (exp ?? now.AddHours(1)).UtcDateTime;

        var issuerClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "test-subject"),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("_sd_alg", "sha-256"),
            new("_sd", sdArrayJson, JsonClaimValueTypes.JsonArray)
        };

        var token = new JwtSecurityToken(
            issuer: "https://test-issuer",
            audience: null,
            claims: issuerClaims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: creds);

        var issuerJwt = handler.WriteToken(token);

        // <issuer>~<d1>~<d2>~...~<dN>~  (trailing ~ is allowed by parser via RemoveEmptyEntries)
        return string.Join('~', new[] { issuerJwt }.Concat(disclosures)) + "~";
    }

    private static string MakeDisclosure(string name, string value)
    {
        var salt = Base64Url(RandomNumberGenerator.GetBytes(16));
        var arr = JsonSerializer.SerializeToUtf8Bytes(new object[] { salt, name, value });
        return Base64Url(arr);
    }

    private static string HashDisclosure(string disclosure)
        => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(disclosure)));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public void Dispose() => _issuerRsa.Dispose();
}
