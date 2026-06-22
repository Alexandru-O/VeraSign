using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MasterSTI.Api.Common.Eudiw;

/// <summary>
/// Validates an SD-JWT vp_token from an EUDIW wallet.
///
/// Security model (what is verified against what):
///   * Issuer JWT signature: verified against the trusted issuer public key configured via
///     <see cref="EudiwOptions.IssuerPublicKeyPem"/> / <see cref="EudiwOptions.IssuerPublicKeyPemUrl"/>.
///     alg=none is rejected unless <see cref="EudiwOptions.AllowUnsignedJwt"/> is true (tests only).
///   * KB-JWT signature:
///       - Preferred: verified against the <c>cnf.jwk</c> embedded in the issuer JWT payload
///         (key binding, per SD-JWT spec). Supports EC P-256 and RSA JWKs.
///       - Fallback: if the issuer JWT has no <c>cnf.jwk</c>, the KB-JWT is verified against
///         the issuer key. This preserves the legacy HTML-simulator flow where both JWTs are
///         signed with the same RSA key. A WARN is logged in that mode. The signature is
///         NEVER skipped.
///   * KB-JWT header: must carry <c>typ=kb+jwt</c>.
///   * KB-JWT claims: <c>nonce</c>, <c>aud</c>, recent <c>iat</c>, and <c>sd_hash</c> that
///     equals base64url(SHA-256("&lt;issuer-jwt&gt;~&lt;d1&gt;~...~&lt;dN&gt;")) over the
///     canonical disclosures segment (no trailing ~).
/// </summary>
public sealed class SdJwtValidator
{
    private static readonly HashSet<string> AllowedAlgorithms = new(StringComparer.Ordinal)
    {
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.RsaSha384,
        SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.EcdsaSha256,
        SecurityAlgorithms.EcdsaSha384,
        SecurityAlgorithms.EcdsaSha512
    };

    private readonly IOptionsMonitor<EudiwOptions> _options;
    private readonly IIssuerKeyHolder? _issuerKeyHolder;
    private readonly ILogger<SdJwtValidator> _logger;

    // Inline-PEM-only synchronous cache. The async URL-fetch path was moved out
    // of the validator into IssuerPemLoader (hosted service); this keeps the hot
    // path off any I/O and lets us drop the legacy .GetAwaiter().GetResult().
    private SecurityKey? _cachedInlineKey;
    private string? _cachedInlinePemFingerprint;
    private readonly object _cacheLock = new();

    public SdJwtValidator(
        IOptionsMonitor<EudiwOptions> options,
        ILogger<SdJwtValidator> logger,
        IIssuerKeyHolder? issuerKeyHolder = null,
        IHttpClientFactory? httpFactory = null)
    {
        _ = httpFactory; // retained for ctor back-compat; URL fetch lives in IssuerPemLoader now.
        _options = options;
        _issuerKeyHolder = issuerKeyHolder;
        _logger = logger;
    }

    public PidClaims? ValidateAndExtract(string vpToken, string expectedNonce, string expectedAud)
        => ValidateAndExtract(vpToken, expectedNonce, expectedAud, allowedDisclosureNames: null);

    /// <summary>
    /// Validate an SD-JWT presentation. When <paramref name="allowedDisclosureNames"/> is non-null,
    /// the validator additionally rejects the presentation if it carries any disclosure whose claim
    /// name is not in the allowlist (GDPR data minimisation, SD-JWT VC §6.2). When null, every
    /// disclosure is accepted (back-compat for tests).
    /// </summary>
    public PidClaims? ValidateAndExtract(
        string vpToken,
        string expectedNonce,
        string expectedAud,
        IReadOnlyCollection<string>? allowedDisclosureNames)
    {
        try
        {
            var opts = _options.CurrentValue;
            var parts = vpToken.Split('~', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1)
                return Fail("Invalid SD-JWT: no parts");

            var issuerJwt = parts[0];
            var disclosures = parts.Skip(1).ToArray();

            string? kbJwt = null;
            if (disclosures.Length > 0 && IsJwt(disclosures[^1]))
            {
                kbJwt = disclosures[^1];
                disclosures = disclosures[..^1];
            }

            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            if (!handler.CanReadToken(issuerJwt))
                return Fail("Issuer token not readable");

            var header = handler.ReadJwtToken(issuerJwt).Header;
            var alg = header.Alg ?? string.Empty;

            SecurityKey? issuerKey = null;
            if (string.Equals(alg, SecurityAlgorithms.None, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(alg))
            {
                if (!opts.AllowUnsignedJwt)
                    return Fail("Issuer JWT uses alg=none — rejected");
                _logger.LogWarning("alg=none accepted because AllowUnsignedJwt=true — test mode only");
            }
            else
            {
                if (!AllowedAlgorithms.Contains(alg))
                    return Fail($"Issuer JWT algorithm '{alg}' is not permitted");

                issuerKey = ResolveIssuerKey();
                if (issuerKey is null)
                    return Fail("No trusted issuer key configured");

                var parameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                    IssuerSigningKey = issuerKey,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = AllowedAlgorithms.ToArray()
                };

                try
                {
                    handler.ValidateToken(issuerJwt, parameters, out _);
                }
                catch (SecurityTokenException ex)
                {
                    return Fail($"Issuer JWT signature validation failed: {ex.GetType().Name}");
                }
            }

            var jwt = handler.ReadJwtToken(issuerJwt);

            // Extract cnf.jwk (key binding) if present — RFC 7800 / SD-JWT key binding.
            var cnf = TryExtractCnfJwk(jwt);
            var cnfKey = cnf?.Key;
            var cnfThumbprint = cnf?.Thumbprint;

            if (kbJwt is null)
            {
                if (!opts.AllowUnsignedJwt)
                    return Fail("KB-JWT missing — rejected");
            }
            else if (!ValidateKbJwt(kbJwt, expectedNonce, expectedAud, issuerJwt, disclosures, cnfKey, issuerKey, opts))
            {
                return null;
            }

            // SD-JWT §4: every disclosure presented by the holder MUST hash to a
            // digest the issuer committed to in the `_sd` array(s) of the issuer
            // JWT. Without this check a tampered claim ("admin=true") still passes
            // because the KB-JWT `sd_hash` only protects transit, not issuer
            // commitment. `_sd_alg` selects the hash (default sha-256).
            var (issuerSdDigests, sdAlg) = ExtractIssuerCommitment(jwt);
            var disclosedClaims = new Dictionary<string, JsonElement>();
            foreach (var disclosure in disclosures)
            {
                var digest = ComputeDisclosureDigest(disclosure, sdAlg);
                if (digest is null)
                    return Fail($"Disclosure digest unsupported _sd_alg '{sdAlg}'");
                if (!issuerSdDigests.Contains(digest))
                    return Fail("Disclosure digest not in issuer _sd[] — disclosure not authentic");

                var decoded = DecodeDisclosure(disclosure);
                if (decoded is { claimName: not null })
                    disclosedClaims[decoded.Value.claimName] = decoded.Value.claimValue;
            }

            if (allowedDisclosureNames is { Count: > 0 })
            {
                foreach (var name in disclosedClaims.Keys)
                {
                    if (!allowedDisclosureNames.Contains(name))
                        return Fail($"Disclosure '{name}' not in verifier allowlist (GDPR data minimisation)");
                }
            }

            var familyName = GetStringClaim(disclosedClaims, "family_name");
            var givenName = GetStringClaim(disclosedClaims, "given_name");
            var birthDateStr = GetStringClaim(disclosedClaims, "birth_date");
            var email = GetStringClaim(disclosedClaims, "email");

            DateOnly? birthDate = null;
            if (birthDateStr is not null && DateOnly.TryParse(birthDateStr, out var bd))
                birthDate = bd;

            var subject = jwt.Subject;

            _logger.LogInformation("SD-JWT validated for subject hash {SubHash}", HashForLog(subject));

            DateTime? issuedAt = jwt.ValidFrom == DateTime.MinValue ? null : jwt.ValidFrom;
            DateTime? expiresAt = jwt.ValidTo == DateTime.MinValue ? null : jwt.ValidTo;

            return new PidClaims(
                FamilyName: familyName ?? string.Empty,
                GivenName: givenName ?? string.Empty,
                BirthDate: birthDate,
                Subject: subject,
                Email: email,
                CnfJwkThumbprint: cnfThumbprint,
                IssuedAt: issuedAt,
                ExpiresAt: expiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SD-JWT validation threw");
            return null;
        }
    }

    private PidClaims? Fail(string reason)
    {
        _logger.LogWarning("SD-JWT rejected: {Reason}", reason);
        return null;
    }

    private SecurityKey? ResolveIssuerKey()
    {
        // Preferred path: key pre-resolved + pinned at startup by IssuerPemLoader.
        var holderKey = _issuerKeyHolder?.Current;
        if (holderKey is not null)
            return holderKey;

        // Back-compat path for tests / inline configs: build the key from the
        // inline PEM synchronously. No HTTP, no .GetAwaiter().GetResult().
        var opts = _options.CurrentValue;
        var pem = opts.IssuerPublicKeyPem;
        if (string.IsNullOrWhiteSpace(pem))
            return null;

        lock (_cacheLock)
        {
            if (_cachedInlineKey is not null
                && string.Equals(_cachedInlinePemFingerprint, pem, StringComparison.Ordinal))
                return _cachedInlineKey;
        }

        try
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            var key = new RsaSecurityKey(rsa) { KeyId = "eudiw-issuer" };

            lock (_cacheLock)
            {
                _cachedInlineKey = key;
                _cachedInlinePemFingerprint = pem;
            }
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid issuer public key PEM");
            return null;
        }
    }

    private bool ValidateKbJwt(
        string kbJwt,
        string expectedNonce,
        string expectedAud,
        string issuerJwt,
        string[] disclosures,
        SecurityKey? cnfKey,
        SecurityKey? issuerKey,
        EudiwOptions opts)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            if (!handler.CanReadToken(kbJwt))
                return FailBool("KB-JWT not readable");

            var kb = handler.ReadJwtToken(kbJwt);

            // typ=kb+jwt header is mandatory per SD-JWT spec.
            var typ = kb.Header.Typ;
            if (!string.Equals(typ, "kb+jwt", StringComparison.Ordinal))
                return FailBool($"KB-JWT typ header is '{typ ?? "<null>"}' — expected 'kb+jwt'");

            var kbAlg = kb.Header.Alg ?? string.Empty;
            var isAlgNone = string.Equals(kbAlg, SecurityAlgorithms.None, StringComparison.OrdinalIgnoreCase)
                            || string.IsNullOrEmpty(kbAlg);

            if (isAlgNone)
            {
                if (!opts.AllowUnsignedJwt)
                    return FailBool("KB-JWT uses alg=none — rejected");
                _logger.LogWarning("KB-JWT alg=none accepted because AllowUnsignedJwt=true — test mode only");
            }
            else
            {
                if (!AllowedAlgorithms.Contains(kbAlg))
                    return FailBool($"KB-JWT algorithm '{kbAlg}' is not permitted");

                // Pick the key to verify against. Prefer cnf.jwk (real key binding).
                // Fallback: no cnf present -> issuer key (legacy simulator flow). Gated behind
                // EudiwOptions.AllowIssuerKeyKbFallback (default false) — production MUST reject.
                SecurityKey? verifyKey;
                if (cnfKey is not null)
                {
                    verifyKey = cnfKey;
                }
                else
                {
                    if (!opts.AllowIssuerKeyKbFallback)
                        return FailBool("KB-JWT has no cnf.jwk and AllowIssuerKeyKbFallback=false — rejected (real wallets MUST embed cnf.jwk)");

                    if (issuerKey is null)
                        return FailBool("KB-JWT has no cnf.jwk and no issuer key available for fallback verification");

                    _logger.LogWarning(
                        "KB-JWT is being verified against the issuer key (no cnf.jwk on SD-JWT). " +
                        "This is the legacy simulator flow — real wallets MUST embed cnf.jwk.");
                    verifyKey = issuerKey;
                }

                var parameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = false, // iat window enforced below
                    IssuerSigningKey = verifyKey,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = AllowedAlgorithms.ToArray()
                };

                try
                {
                    handler.ValidateToken(kbJwt, parameters, out _);
                }
                catch (SecurityTokenException ex)
                {
                    return FailBool($"KB-JWT signature validation failed: {ex.GetType().Name}");
                }
            }

            var nonceClaim = kb.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
            if (nonceClaim != expectedNonce)
                return FailBool("KB-JWT nonce mismatch");

            var audClaim = kb.Audiences.FirstOrDefault();
            if (audClaim != expectedAud)
                return FailBool("KB-JWT aud mismatch");

            var iat = kb.IssuedAt;
            var iatMaxAge = TimeSpan.FromSeconds(Math.Max(1, opts.KbJwtIatSkewSeconds));
            // Allow up to 5s of forward skew for honest client-clock drift; everything
            // beyond that is suspicious (an iat in the future widens the replay window).
            var futureSkew = TimeSpan.FromSeconds(5);
            if (iat == DateTime.MinValue)
                return FailBool("KB-JWT iat claim is missing");
            var delta = DateTime.UtcNow - iat;
            if (delta > iatMaxAge)
                return FailBool($"KB-JWT iat older than {iatMaxAge.TotalSeconds:F0}s — replay or stale");
            if (delta < -futureSkew)
                return FailBool($"KB-JWT iat more than {futureSkew.TotalSeconds:F0}s in the future");

            // sd_hash claim is required. It covers the issuer JWT + disclosures joined by ~ (no trailing ~).
            var sdHashClaim = kb.Claims.FirstOrDefault(c => c.Type == "sd_hash")?.Value;
            if (string.IsNullOrEmpty(sdHashClaim))
                return FailBool("KB-JWT sd_hash claim is missing");

            var expectedSdHash = ComputeSdHash(issuerJwt, disclosures);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(sdHashClaim),
                    Encoding.ASCII.GetBytes(expectedSdHash)))
            {
                return FailBool("KB-JWT sd_hash mismatch");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KB-JWT validation threw");
            return false;
        }
    }

    private bool FailBool(string reason)
    {
        _logger.LogWarning("KB-JWT rejected: {Reason}", reason);
        return false;
    }

    /// <summary>
    /// base64url(SHA-256(issuerJwt + "~" + disc1 + "~" + ... + "~" + discN)) — no trailing "~".
    /// When there are no disclosures, hashes just the issuer JWT.
    /// </summary>
    private static string ComputeSdHash(string issuerJwt, string[] disclosures)
    {
        var sb = new StringBuilder(issuerJwt.Length + disclosures.Sum(d => d.Length + 1));
        sb.Append(issuerJwt);
        foreach (var d in disclosures)
        {
            sb.Append('~').Append(d);
        }
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(sb.ToString()));
        return Base64UrlEncode(hash);
    }

    internal sealed record CnfJwkResult(SecurityKey Key, string Thumbprint);

    /// <summary>
    /// Extracts the cnf.jwk SecurityKey + RFC 7638 thumbprint from an issuer JWT payload.
    /// Returns null when the claim is absent or malformed. Supports EC P-256 and RSA JWKs.
    /// </summary>
    private CnfJwkResult? TryExtractCnfJwk(JwtSecurityToken issuerJwt)
    {
        try
        {
            // Claim "cnf" is a JSON object; System.IdentityModel surfaces it as a string.
            var cnfClaim = issuerJwt.Claims.FirstOrDefault(c => c.Type == "cnf")?.Value;
            if (string.IsNullOrWhiteSpace(cnfClaim))
                return null;

            using var doc = JsonDocument.Parse(cnfClaim);
            if (!doc.RootElement.TryGetProperty("jwk", out var jwkEl)
                || jwkEl.ValueKind != JsonValueKind.Object)
                return null;

            var key = BuildKeyFromJwk(jwkEl);
            if (key is null) return null;

            var thumbprint = ComputeJwkThumbprint(jwkEl);
            if (thumbprint is null) return null;

            return new CnfJwkResult(key, thumbprint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse cnf.jwk from issuer JWT");
            return null;
        }
    }

    private static SecurityKey? BuildKeyFromJwk(JsonElement jwk)
    {
        var kty = jwk.TryGetProperty("kty", out var ktyEl) ? ktyEl.GetString() : null;
        if (string.IsNullOrEmpty(kty))
            return null;

        if (string.Equals(kty, "EC", StringComparison.Ordinal))
        {
            var crv = jwk.TryGetProperty("crv", out var crvEl) ? crvEl.GetString() : null;
            var x = jwk.TryGetProperty("x", out var xEl) ? xEl.GetString() : null;
            var y = jwk.TryGetProperty("y", out var yEl) ? yEl.GetString() : null;
            if (string.IsNullOrEmpty(crv) || string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y))
                return null;

            ECCurve curve = crv switch
            {
                "P-256" => ECCurve.NamedCurves.nistP256,
                "P-384" => ECCurve.NamedCurves.nistP384,
                "P-521" => ECCurve.NamedCurves.nistP521,
                _ => default
            };
            if (curve.Oid is null)
                return null;

            var ecParams = new ECParameters
            {
                Curve = curve,
                Q = new ECPoint
                {
                    X = Base64UrlDecode(x),
                    Y = Base64UrlDecode(y)
                }
            };
            var ecdsa = ECDsa.Create(ecParams);
            return new ECDsaSecurityKey(ecdsa) { KeyId = "cnf-jwk-ec" };
        }

        if (string.Equals(kty, "RSA", StringComparison.Ordinal))
        {
            var n = jwk.TryGetProperty("n", out var nEl) ? nEl.GetString() : null;
            var e = jwk.TryGetProperty("e", out var eEl) ? eEl.GetString() : null;
            if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(e))
                return null;

            var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Base64UrlDecode(n),
                Exponent = Base64UrlDecode(e)
            });
            return new RsaSecurityKey(rsa) { KeyId = "cnf-jwk-rsa" };
        }

        return null;
    }

    /// <summary>
    /// RFC 7638 JWK thumbprint — SHA-256 of canonical UTF-8 JSON of the required
    /// JWK members (lex-ordered, no whitespace), base64url-encoded.
    /// EC: {"crv","kty","x","y"}; RSA: {"e","kty","n"}.
    /// </summary>
    private static string? ComputeJwkThumbprint(JsonElement jwk)
    {
        var kty = jwk.TryGetProperty("kty", out var ktyEl) ? ktyEl.GetString() : null;
        if (string.IsNullOrEmpty(kty)) return null;

        string canonical;
        if (string.Equals(kty, "EC", StringComparison.Ordinal))
        {
            var crv = jwk.GetProperty("crv").GetString();
            var x = jwk.GetProperty("x").GetString();
            var y = jwk.GetProperty("y").GetString();
            canonical = $"{{\"crv\":\"{crv}\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
        }
        else if (string.Equals(kty, "RSA", StringComparison.Ordinal))
        {
            var e = jwk.GetProperty("e").GetString();
            var n = jwk.GetProperty("n").GetString();
            canonical = $"{{\"e\":\"{e}\",\"kty\":\"RSA\",\"n\":\"{n}\"}}";
        }
        else
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Base64UrlEncode(hash);
    }

    /// <summary>
    /// Returns the set of digests the issuer committed to via `_sd` arrays
    /// anywhere in the issuer JWT payload, plus the algorithm under `_sd_alg`
    /// (default `sha-256`). Walks the JSON tree so nested object `_sd`
    /// (SD-JWT VC `address` style) is covered too.
    /// </summary>
    private static (HashSet<string> digests, string sdAlg) ExtractIssuerCommitment(JwtSecurityToken issuerJwt)
    {
        var digests = new HashSet<string>(StringComparer.Ordinal);
        var sdAlg = "sha-256";
        try
        {
            // JwtSecurityToken.Payload.SerializeToJson() materializes the
            // payload as JSON regardless of how each claim was originally
            // shaped — preserves `_sd` as an array even when the legacy
            // claim-list ctor would have flattened it.
            var json = issuerJwt.Payload.SerializeToJson();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("_sd_alg", out var algEl)
                && algEl.ValueKind == JsonValueKind.String)
            {
                var a = algEl.GetString();
                if (!string.IsNullOrWhiteSpace(a)) sdAlg = a;
            }
            CollectSdDigests(doc.RootElement, digests);
        }
        catch
        {
            // malformed payload -> empty commitment, every disclosure rejected
        }
        return (digests, sdAlg);
    }

    private static void CollectSdDigests(JsonElement el, HashSet<string> bag)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.NameEquals("_sd") && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in prop.Value.EnumerateArray())
                            if (d.ValueKind == JsonValueKind.String) bag.Add(d.GetString()!);
                    }
                    else
                    {
                        CollectSdDigests(prop.Value, bag);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    CollectSdDigests(item, bag);
                break;
        }
    }

    /// <summary>
    /// Per SD-JWT §4.2.4.1, the disclosure digest is hash(ASCII bytes of the
    /// untouched base64url-encoded disclosure string), then base64url-encoded.
    /// </summary>
    private static string? ComputeDisclosureDigest(string disclosure, string sdAlg)
    {
        HashAlgorithm? hash = sdAlg switch
        {
            "sha-256" => SHA256.Create(),
            "sha-384" => SHA384.Create(),
            "sha-512" => SHA512.Create(),
            _ => null
        };
        if (hash is null) return null;
        using (hash)
        {
            var bytes = hash.ComputeHash(Encoding.ASCII.GetBytes(disclosure));
            return Base64UrlEncode(bytes);
        }
    }

    private static (string? claimName, JsonElement claimValue)? DecodeDisclosure(string disclosure)
    {
        try
        {
            var bytes = Base64UrlDecode(disclosure);
            var json = Encoding.UTF8.GetString(bytes);
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 3)
                return null;
            return (arr[1].GetString(), arr[2].Clone());
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStringClaim(Dictionary<string, JsonElement> claims, string name)
    {
        if (claims.TryGetValue(name, out var el))
            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        return null;
    }

    private static bool IsJwt(string s) => s.Split('.').Length == 3;

    private static string HashForLog(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "-";
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(h.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 1: throw new FormatException("Invalid base64url length");
        }
        return Convert.FromBase64String(padded);
    }
}
