using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// Wallet-side read-only SD-JWT parser. Surfaces the PID claims the wallet already
/// trusts (server verified at enrolment) so the UI can render real values instead
/// of a hardcoded placeholder.
///
/// What this DOES:
///   * Splits the SD-JWT presentation (<c>&lt;jwt&gt;~&lt;disc1&gt;~...~&lt;discN&gt;~</c>,
///     optional trailing KB-JWT) into its issuer JWT + disclosures.
///   * Decodes each disclosure and matches its salted hash against an entry in the
///     issuer JWT payload's <c>_sd</c> array. A disclosure whose hash is NOT in
///     <c>_sd</c> is treated as tampered and throws.
///   * Reads <c>nbf</c> / <c>exp</c> from the issuer JWT payload.
///
/// What this does NOT do:
///   * No JWT signature verification (no crypto verify, no network).
///   * No KB-JWT verification.
///   * No issuer trust check.
///
/// Verification already happened server-side at enrolment; this parser is for
/// presenting the disclosures the wallet already trusts.
/// </summary>
public static class SdJwtParser
{
    public static ParsedSdJwt Parse(string sdJwt)
    {
        if (string.IsNullOrWhiteSpace(sdJwt))
            throw new SdJwtFormatException("SD-JWT is empty");

        var segments = sdJwt.Split('~', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 1)
            throw new SdJwtFormatException("SD-JWT has no segments");

        var issuerJwt = segments[0];
        var rest = segments.Skip(1).ToArray();

        // Strip optional trailing KB-JWT (three dot-separated segments).
        if (rest.Length > 0 && IsJwt(rest[^1]))
            rest = rest[..^1];
        var disclosures = rest;

        var jwtParts = issuerJwt.Split('.');
        if (jwtParts.Length < 2)
            throw new SdJwtFormatException("Issuer JWT missing payload");

        JsonElement payload;
        try
        {
            var payloadBytes = Base64UrlDecode(jwtParts[1]);
            using var doc = JsonDocument.Parse(payloadBytes);
            payload = doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new SdJwtFormatException("Issuer JWT payload is malformed", ex);
        }

        var sdHashes = ExtractSdHashes(payload);
        var enforceSdHashes = sdHashes.Count > 0;

        string? familyName = null;
        string? givenName = null;
        string? email = null;

        foreach (var disclosure in disclosures)
        {
            if (enforceSdHashes)
            {
                var hash = HashDisclosure(disclosure);
                if (!sdHashes.Contains(hash))
                    throw new SdJwtFormatException(
                        "Disclosure hash not present in issuer JWT _sd array — tampered or unrelated disclosure");
            }

            var decoded = DecodeDisclosure(disclosure);
            if (decoded is null) continue;

            switch (decoded.Value.claimName)
            {
                case "family_name":
                    familyName = AsString(decoded.Value.claimValue);
                    break;
                case "given_name":
                    givenName = AsString(decoded.Value.claimValue);
                    break;
                case "email":
                    email = AsString(decoded.Value.claimValue);
                    break;
            }
        }

        DateTimeOffset? nbf = ReadUnixTime(payload, "nbf");
        DateTimeOffset? exp = ReadUnixTime(payload, "exp");

        return new ParsedSdJwt(familyName, givenName, email, nbf, exp);
    }

    private static HashSet<string> ExtractSdHashes(JsonElement payload)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (payload.ValueKind != JsonValueKind.Object) return set;
        if (!payload.TryGetProperty("_sd", out var sd) || sd.ValueKind != JsonValueKind.Array)
            return set;

        foreach (var entry in sd.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                var s = entry.GetString();
                if (!string.IsNullOrEmpty(s))
                    set.Add(s);
            }
        }
        return set;
    }

    /// <summary>
    /// SD-JWT disclosure digest is base64url(SHA-256(ASCII(disclosure))).
    /// </summary>
    private static string HashDisclosure(string disclosure)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(disclosure));
        return Base64UrlEncode(hash);
    }

    private static (string? claimName, JsonElement claimValue)? DecodeDisclosure(string disclosure)
    {
        try
        {
            var bytes = Base64UrlDecode(disclosure);
            using var doc = JsonDocument.Parse(bytes);
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

    private static string? AsString(JsonElement el)
        => el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();

    private static DateTimeOffset? ReadUnixTime(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        if (!payload.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind != JsonValueKind.Number) return null;
        if (!el.TryGetInt64(out var unix)) return null;
        return DateTimeOffset.FromUnixTimeSeconds(unix);
    }

    private static bool IsJwt(string s) => s.Split('.').Length == 3;

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

/// <summary>
/// PID claims surfaced from an SD-JWT presentation. Any field may be null when the
/// issuer did not disclose it.
/// </summary>
public sealed record ParsedSdJwt(
    string? FamilyName,
    string? GivenName,
    string? Email,
    DateTimeOffset? Nbf,
    DateTimeOffset? Exp);

public sealed class SdJwtFormatException : Exception
{
    public SdJwtFormatException(string message) : base(message) { }
    public SdJwtFormatException(string message, Exception inner) : base(message, inner) { }
}
