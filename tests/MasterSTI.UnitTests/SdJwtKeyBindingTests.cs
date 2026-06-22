using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterSTI.Api.Common.Eudiw;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace MasterSTI.UnitTests;

/// <summary>
/// Verifies the hardened SD-JWT validator honours RFC 7800 key binding: KB-JWT signature
/// must match the <c>cnf.jwk</c> on the issuer SD-JWT, the <c>sd_hash</c> claim must
/// cover the canonical disclosures segment, and the <c>typ=kb+jwt</c> header is required.
/// Also locks in the legacy-simulator fallback where no cnf.jwk exists and the KB-JWT is
/// verified against the issuer RSA key.
/// </summary>
public class SdJwtKeyBindingTests : IDisposable
{
    private const string VerifierId = "https://verifier.test";
    private const string Nonce = "nonce-abc-123";

    private readonly RSA _issuerRsa = RSA.Create(2048);
    private readonly ECDsa _walletEc = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private SdJwtValidator CreateValidator(
        bool allowUnsigned = false,
        bool allowIssuerKeyKbFallback = false,
        ILogger<SdJwtValidator>? logger = null)
    {
        var opts = new EudiwOptions
        {
            VerifierId = VerifierId,
            IssuerPublicKeyPem = _issuerRsa.ExportSubjectPublicKeyInfoPem(),
            AllowUnsignedJwt = allowUnsigned,
            AllowIssuerKeyKbFallback = allowIssuerKeyKbFallback
        };
        return new SdJwtValidator(
            new StaticOptionsMonitor<EudiwOptions>(opts),
            logger ?? NullLogger<SdJwtValidator>.Instance,
            httpFactory: null);
    }

    [Fact]
    public void KbJwt_ValidSignature_MatchingSdHash_Passes()
    {
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _walletEc,
            cnfJwk: WalletJwk(_walletEc),
            aud: VerifierId,
            nonce: Nonce);

        var validator = CreateValidator();
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.NotNull(claims);
        Assert.Equal("Popescu", claims!.FamilyName);
        Assert.Equal("Ion", claims.GivenName);
        Assert.NotNull(claims.CnfJwkThumbprint);
        Assert.NotEmpty(claims.CnfJwkThumbprint!);
    }

    [Fact]
    public void CnfJwkThumbprint_IsRfc7638_AndDeterministic()
    {
        // Same wallet key across two SD-JWTs must yield the same thumbprint.
        var vp1 = BuildKeyBoundSdJwt(_issuerRsa, _walletEc, WalletJwk(_walletEc), VerifierId, Nonce);
        var vp2 = BuildKeyBoundSdJwt(_issuerRsa, _walletEc, WalletJwk(_walletEc), VerifierId, Nonce);

        var validator = CreateValidator();
        var c1 = validator.ValidateAndExtract(vp1, Nonce, VerifierId);
        var c2 = validator.ValidateAndExtract(vp2, Nonce, VerifierId);

        Assert.NotNull(c1?.CnfJwkThumbprint);
        Assert.Equal(c1!.CnfJwkThumbprint, c2!.CnfJwkThumbprint);

        // RFC 7638 thumbprint of canonical {"crv":"P-256","kty":"EC","x":"...","y":"..."} JSON.
        var p = _walletEc.ExportParameters(includePrivateParameters: false);
        var canonical = "{\"crv\":\"P-256\",\"kty\":\"EC\""
            + ",\"x\":\"" + Base64Url(p.Q.X!) + "\""
            + ",\"y\":\"" + Base64Url(p.Q.Y!) + "\"}";
        var expected = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        Assert.Equal(expected, c1.CnfJwkThumbprint);
    }

    [Fact]
    public void LegacySimulator_NoCnfJwk_ThumbprintIsNull()
    {
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _issuerRsa,
            cnfJwk: null,
            aud: VerifierId,
            nonce: Nonce);

        // Opt into the legacy issuer-key fallback (production default is false).
        var validator = CreateValidator(allowIssuerKeyKbFallback: true);
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.NotNull(claims);
        Assert.Null(claims!.CnfJwkThumbprint);
    }

    [Fact]
    public void KbJwt_NoCnfJwk_RejectedByDefault_WhenFallbackDisabled()
    {
        // Production posture: SD-JWT without a cnf.jwk MUST be rejected outright. The verifier
        // has no proof of key binding and the fallback to the issuer key is opt-in only.
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _issuerRsa,
            cnfJwk: null,
            aud: VerifierId,
            nonce: Nonce);

        // Default constructor: AllowIssuerKeyKbFallback = false.
        var validator = CreateValidator();
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.Null(claims);
    }

    [Fact]
    public void KbJwt_SignedByWrongKey_Rejected()
    {
        // Attacker signs the KB-JWT with a different EC key, but the SD-JWT still declares
        // the legitimate wallet's public key in cnf.jwk.
        using var attackerEc = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: attackerEc,
            cnfJwk: WalletJwk(_walletEc),
            aud: VerifierId,
            nonce: Nonce);

        var validator = CreateValidator();
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.Null(claims);
    }

    [Fact]
    public void KbJwt_SdHashMismatch_Rejected()
    {
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _walletEc,
            cnfJwk: WalletJwk(_walletEc),
            aud: VerifierId,
            nonce: Nonce,
            forceSdHash: Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes("tampered"))));

        var validator = CreateValidator();
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.Null(claims);
    }

    [Fact]
    public void KbJwt_MissingTypHeader_Rejected()
    {
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _walletEc,
            cnfJwk: WalletJwk(_walletEc),
            aud: VerifierId,
            nonce: Nonce,
            omitTypHeader: true);

        var validator = CreateValidator();
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.Null(claims);
    }

    [Fact]
    public void KbJwt_MissingSdHashClaim_Rejected()
    {
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _walletEc,
            cnfJwk: WalletJwk(_walletEc),
            aud: VerifierId,
            nonce: Nonce,
            omitSdHash: true);

        var validator = CreateValidator();
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.Null(claims);
    }

    [Fact]
    public void LegacySimulator_NoCnfJwk_KbSignedByIssuerKey_Passes_AndLogsWarning()
    {
        // Legacy flow: no cnf.jwk on the SD-JWT, KB-JWT signed by the same RSA issuer key.
        // With AllowIssuerKeyKbFallback opted in, the validator MUST still verify the KB-JWT
        // signature (against the issuer key), and MUST emit a WARN explaining the fallback.
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _issuerRsa,
            cnfJwk: null,
            aud: VerifierId,
            nonce: Nonce);

        var capturing = new CapturingLogger<SdJwtValidator>();
        var validator = CreateValidator(allowIssuerKeyKbFallback: true, logger: capturing);

        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.NotNull(claims);
        Assert.Contains(
            capturing.Entries,
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains("no cnf.jwk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void KbJwt_MissingEntirely_Rejected_WhenAllowUnsignedFalse()
    {
        // A bare SD-JWT presentation (issuer JWT + disclosures, no KB-JWT) must be rejected
        // in production mode — the verifier has no proof the holder is present.
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _walletEc,
            cnfJwk: WalletJwk(_walletEc),
            aud: VerifierId,
            nonce: Nonce,
            omitKbJwt: true);

        var validator = CreateValidator(allowUnsigned: false);
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.Null(claims);
    }

    [Fact]
    public void KbJwt_MissingEntirely_Accepted_WhenAllowUnsignedTrue()
    {
        // AllowUnsignedJwt=true is the documented test-mode escape hatch. When set, the
        // validator must accept a bare SD-JWT without a KB-JWT.
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _walletEc,
            cnfJwk: WalletJwk(_walletEc),
            aud: VerifierId,
            nonce: Nonce,
            omitKbJwt: true);

        var validator = CreateValidator(allowUnsigned: true);
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.NotNull(claims);
    }

    [Fact]
    public void KbJwt_IatOlderThanConfiguredWindow_Rejected()
    {
        // KB-JWT signed 5 minutes ago. Default KbJwtIatSkewSeconds is 120s, so this MUST fail.
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _walletEc,
            cnfJwk: WalletJwk(_walletEc),
            aud: VerifierId,
            nonce: Nonce,
            kbIatOffsetSeconds: -300);

        var validator = CreateValidator();
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.Null(claims);
    }

    [Fact]
    public void Disclosure_OutsideAllowlist_Rejected()
    {
        // Wallet sends an extra "email" disclosure but the verifier only asked for the name claims.
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _walletEc,
            cnfJwk: WalletJwk(_walletEc),
            aud: VerifierId,
            nonce: Nonce,
            extraDisclosures: new[] { ("email", "leak@example.com") });

        var validator = CreateValidator();
        var allow = new HashSet<string>(StringComparer.Ordinal) { "family_name", "given_name" };
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId, allow);

        Assert.Null(claims);
    }

    [Fact]
    public void Disclosure_InsideAllowlist_Accepted()
    {
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _walletEc,
            cnfJwk: WalletJwk(_walletEc),
            aud: VerifierId,
            nonce: Nonce);

        var validator = CreateValidator();
        var allow = new HashSet<string>(StringComparer.Ordinal) { "family_name", "given_name", "email" };
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId, allow);

        Assert.NotNull(claims);
        Assert.Equal("Popescu", claims!.FamilyName);
    }

    [Fact]
    public void Disclosure_Email_SurvivesKbJwtVerify()
    {
        // Phase 1 contract (two-wallet demo): the Mock QTSP now mints PIDs with an
        // `email` disclosure. The disclosure must round-trip through the full
        // KB-JWT verification path and surface on PidClaims.Email so the wallet
        // enrollment writer can populate WalletEnrollment.PidEmail.
        const string expectedEmail = "toma.iliescu@verasign.demo";

        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _walletEc,
            cnfJwk: WalletJwk(_walletEc),
            aud: VerifierId,
            nonce: Nonce,
            extraDisclosures: new[] { ("email", expectedEmail) });

        var validator = CreateValidator();
        var allow = new HashSet<string>(StringComparer.Ordinal) { "family_name", "given_name", "email" };
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId, allow);

        Assert.NotNull(claims);
        Assert.Equal(expectedEmail, claims!.Email);
    }

    [Fact]
    public void CnfPresent_ButKbSignedByDifferentKey_Rejected()
    {
        // SD-JWT declares the wallet's EC key in cnf.jwk, but the KB-JWT is signed by
        // the issuer RSA key instead. Fallback MUST NOT apply when cnf.jwk is present —
        // verification is strictly against cnf.jwk.
        var vpToken = BuildKeyBoundSdJwt(
            issuerRsa: _issuerRsa,
            walletKey: _issuerRsa,
            cnfJwk: WalletJwk(_walletEc),
            aud: VerifierId,
            nonce: Nonce);

        var validator = CreateValidator();
        var claims = validator.ValidateAndExtract(vpToken, Nonce, VerifierId);

        Assert.Null(claims);
    }

    // --- helpers ----------------------------------------------------------

    private static object WalletJwk(ECDsa ec)
    {
        var p = ec.ExportParameters(includePrivateParameters: false);
        return new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64Url(p.Q.X!),
            ["y"] = Base64Url(p.Q.Y!)
        };
    }

    /// <summary>
    /// Builds an SD-JWT presentation. <paramref name="walletKey"/> can be an <see cref="RSA"/>
    /// or an <see cref="ECDsa"/> — the KB-JWT is signed with whichever is provided.
    /// </summary>
    private static string BuildKeyBoundSdJwt(
        RSA issuerRsa,
        object walletKey,
        object? cnfJwk,
        string aud,
        string nonce,
        bool omitTypHeader = false,
        bool omitSdHash = false,
        string? forceSdHash = null,
        bool omitKbJwt = false,
        int kbIatOffsetSeconds = 0,
        (string name, string value)[]? extraDisclosures = null)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        var now = DateTimeOffset.UtcNow;
        var kbNow = now.AddSeconds(kbIatOffsetSeconds);

        // Build disclosures first so the issuer can commit to their digests
        // via the `_sd` array (SD-JWT §4.2.4).
        var disc1 = DisclosureFor("family_name", "Popescu");
        var disc2 = DisclosureFor("given_name", "Ion");
        var extra = (extraDisclosures ?? Array.Empty<(string, string)>())
            .Select(t => DisclosureFor(t.name, t.value))
            .ToArray();
        var disclosures = new[] { disc1, disc2 }.Concat(extra).ToArray();
        var sdDigests = disclosures
            .Select(d => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(d))))
            .ToArray();

        // Hand-craft the issuer JWT payload: JwtSecurityToken's claim-list ctor
        // flattens JsonArray claims back into individual string claims, which
        // would emit "_sd":"<digest>" instead of "_sd":[...]. Building the JSON
        // payload directly avoids that quirk and matches what the Mock QTSP
        // emits in production.
        var payload = new Dictionary<string, object>
        {
            ["iss"] = "https://test-issuer",
            ["sub"] = "test-subject",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
            ["nonce"] = nonce,
            ["_sd_alg"] = "sha-256",
            ["_sd"] = sdDigests
        };
        if (cnfJwk is not null)
            payload["cnf"] = new Dictionary<string, object> { ["jwk"] = cnfJwk };

        var header = new Dictionary<string, object>
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT",
            ["kid"] = "test-issuer"
        };
        var headerB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = headerB64 + "." + payloadB64;
        var sigBytes = issuerRsa.SignData(Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var issuerJwt = signingInput + "." + Base64Url(sigBytes);

        if (omitKbJwt)
        {
            // Bare SD-JWT without a KB-JWT — used to exercise the AllowUnsignedJwt escape hatch.
            return string.Join('~', new[] { issuerJwt }.Concat(disclosures));
        }

        var sdHash = forceSdHash ?? ComputeSdHash(issuerJwt, disclosures);

        // Choose KB signing credentials based on key type.
        SigningCredentials kbCreds = walletKey switch
        {
            ECDsa ec => new SigningCredentials(
                new ECDsaSecurityKey(ec) { KeyId = "wallet-ec" },
                SecurityAlgorithms.EcdsaSha256),
            RSA rsa => new SigningCredentials(
                new RsaSecurityKey(rsa) { KeyId = "wallet-rsa" },
                SecurityAlgorithms.RsaSha256),
            _ => throw new ArgumentException("walletKey must be RSA or ECDsa")
        };

        var kbClaims = new List<Claim>
        {
            new("nonce", nonce),
            new(JwtRegisteredClaimNames.Iat, kbNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        if (!omitSdHash)
            kbClaims.Add(new Claim("sd_hash", sdHash));

        var kbToken = new JwtSecurityToken(
            issuer: null,
            audience: aud,
            claims: kbClaims,
            notBefore: kbNow.AddMinutes(-1).UtcDateTime,
            expires: kbNow.AddMinutes(5).UtcDateTime,
            signingCredentials: kbCreds);

        if (!omitTypHeader)
            kbToken.Header["typ"] = "kb+jwt";

        var kbJwt = handler.WriteToken(kbToken);

        return string.Join('~', new[] { issuerJwt }.Concat(disclosures).Concat(new[] { kbJwt }));
    }

    private static string DisclosureFor(string name, string value)
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
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(sb.ToString()));
        return Base64Url(hash);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public void Dispose()
    {
        _issuerRsa.Dispose();
        _walletEc.Dispose();
    }
}

/// <summary>
/// In-memory logger that records every entry so tests can assert that specific
/// warnings were emitted (or not). Not thread-safe by design — each test uses
/// its own instance.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }
}
