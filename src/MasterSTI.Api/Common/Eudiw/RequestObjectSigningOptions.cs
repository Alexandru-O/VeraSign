namespace MasterSTI.Api.Common.Eudiw;

/// <summary>
/// ADR-0011: ES256 signing material for the OpenID4VP <c>request_object</c> JWT.
/// </summary>
public sealed class RequestObjectSigningOptions
{
    public const string Section = "Eudiw:RequestObjectSigning";

    /// <summary>
    /// PEM-encoded EC P-256 private key (PKCS#8). Loaded once at startup via
    /// <see cref="System.Security.Cryptography.ECDsa.ImportFromPem(System.ReadOnlySpan{char})"/>.
    /// Source-controlled value MUST stay blank — populated by user-secrets / env var /
    /// <c>publish/api/appsettings.json</c> patched by <c>start-all.ps1 -Publish</c>,
    /// same hygiene as <c>Jwt:Signing:Key</c>.
    /// </summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>
    /// JWS header <c>kid</c>. Wallet pins this string and rejects any other kid.
    /// </summary>
    public string Kid { get; set; } = "verasign-rqo-v1";

    /// <summary>
    /// Lifetime of the issued JWT in seconds. Defaults to 300 s, aligned with
    /// <c>WalletAuthCacheKeys.TtlSeconds</c> (5 min) — the state entry's expiry
    /// already bounds the wallet's window; the JWT <c>exp</c> just makes it
    /// cryptographically expressed.
    /// </summary>
    public int ExpiresInSeconds { get; set; } = 300;
}
