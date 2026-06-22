using System.Text.Json;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// Base URLs for the MasterSTI backend services.
///
/// Values are read on first access from the bundled MAUI raw asset
/// <c>wallet.config.json</c> (shape: <c>{ "apiBaseUrl": string, "issuerBaseUrl": string }</c>).
/// Missing file OR missing key falls back to the compile-time defaults below.
/// On the Android emulator, 10.0.2.2 is the loopback address that routes to the
/// Windows host.
/// </summary>
public sealed class WalletConfig
{
    // Compile-time defaults — used when wallet.config.json is missing
    // entirely OR when a specific key is absent from the file.
    private const string DefaultIssuerBaseUrl = "https://10.0.2.2:7112";
    private const string DefaultApiBaseUrl = "https://10.0.2.2:7001";

    private const string AssetFileName = "wallet.config.json";

    private static readonly Lazy<(string ApiBaseUrl, string IssuerBaseUrl)> _loaded =
        new(LoadFromAsset, isThreadSafe: true);

    /// <summary>Mock EUDIW Issuer base URL (PID issuance).</summary>
    public string IssuerBaseUrl { get; set; } = _loaded.Value.IssuerBaseUrl;

    /// <summary>API base URL (VP response endpoint).</summary>
    public string ApiBaseUrl { get; set; } = _loaded.Value.ApiBaseUrl;

    /// <summary>
    /// ADR-0011: pinned EC P-256 public key of the verifier's <c>request_object</c>
    /// signer (client_id_scheme=pre-registered). Wallet rejects any
    /// <c>/api/eudiw/request-object/{state}</c> JWT whose signature does not
    /// verify against this key.
    /// </summary>
    public string TrustedRequestObjectPublicKeyPem { get; set; } = DefaultRequestObjectPublicKeyPem;

    /// <summary>
    /// ADR-0011: expected JWS header <c>kid</c>. Wallet rejects any kid mismatch
    /// before attempting signature verification.
    /// </summary>
    public string TrustedRequestObjectKid { get; set; } = "verasign-rqo-v1";

    /// <summary>
    /// ADR-0011: expected <c>client_id</c> (and <c>iss</c>) of the verifier inside
    /// the signed request_object. Defaults to <see cref="ApiBaseUrl"/> because the
    /// verifier identity and the API origin are the same in this prototype.
    /// </summary>
    public string ExpectedVerifierClientId { get; set; } = _loaded.Value.ApiBaseUrl;

    // ADR-0011 demo verifier key. Pair lives in publish/api/appsettings.json
    // (private half) and is injected by start-all.ps1 -Publish. Rotation in this
    // dissertation prototype = generate new pair + ship new wallet build (see
    // ADR-0011 §Rotation deferral).
    private const string DefaultRequestObjectPublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEOQeC98pruo7qoY57JTaMdSCqCVM7\n" +
        "YnoK7NJ5pI5JMsCb4J0K7UJ+Lmj19lY+WTNFl+1CkcQq9Z5ARA5U4E4JcA==\n" +
        "-----END PUBLIC KEY-----";

    private static (string ApiBaseUrl, string IssuerBaseUrl) LoadFromAsset()
    {
        try
        {
            // FileSystem.OpenAppPackageFileAsync is the MAUI API for reading
            // bundled MauiAsset files. Block once here so callers stay sync.
            using var stream = FileSystem.OpenAppPackageFileAsync(AssetFileName)
                .GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            var api = TryReadString(root, "apiBaseUrl") ?? DefaultApiBaseUrl;
            var issuer = TryReadString(root, "issuerBaseUrl") ?? DefaultIssuerBaseUrl;
            return (api, issuer);
        }
        catch (FileNotFoundException)
        {
            // No bundled asset — use compile-time defaults.
            return (DefaultApiBaseUrl, DefaultIssuerBaseUrl);
        }
        catch
        {
            // Malformed JSON or any other read failure — degrade to defaults
            // rather than crashing the wallet at startup.
            return (DefaultApiBaseUrl, DefaultIssuerBaseUrl);
        }
    }

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind != JsonValueKind.String) return null;
        var value = prop.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
