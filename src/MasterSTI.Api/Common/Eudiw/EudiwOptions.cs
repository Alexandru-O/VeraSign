namespace MasterSTI.Api.Common.Eudiw;

public sealed class EudiwOptions
{
    public const string Section = "Eudiw";

    public string VerifierId { get; set; } = "https://localhost:7001";
    public string ResponseUri { get; set; } = "https://localhost:7001/api/eudiw/response";
    public int NonceCacheMinutes { get; set; } = 5;

    /// <summary>PEM-encoded trusted issuer public key (inline, e.g. for tests).</summary>
    public string? IssuerPublicKeyPem { get; set; }

    /// <summary>URL the validator can GET to retrieve the trusted issuer public key in PEM form.</summary>
    public string? IssuerPublicKeyPemUrl { get; set; }

    /// <summary>
    /// SHA-256 (hex, lowercase) of the trusted issuer PEM, used as a startup pin.
    /// When set, <see cref="IssuerPemLoader"/> aborts the host if the loaded PEM
    /// hashes to a different value. When unset, the loader logs a Critical entry
    /// containing the observed hash so an operator can copy it back into config —
    /// loud TOFU rather than silent. MUST be set in any non-Development deployment.
    /// </summary>
    public string? IssuerPublicKeyPemSha256 { get; set; }

    /// <summary>Allow tokens with alg=none. MUST remain false in production. Used only for legacy unit tests.</summary>
    public bool AllowUnsignedJwt { get; set; }

    /// <summary>
    /// When the SD-JWT carries no <c>cnf.jwk</c>, fall back to verifying the KB-JWT against the
    /// trusted issuer key. Required by the legacy HTML simulator flow. MUST remain <c>false</c>
    /// in production: a real wallet always embeds its cnf.jwk and the fallback weakens key
    /// binding to "anyone with the issuer key can present".
    /// </summary>
    public bool AllowIssuerKeyKbFallback { get; set; }

    /// <summary>
    /// Maximum age (seconds) of a KB-JWT <c>iat</c> claim relative to server clock. Default 60s,
    /// aligned with ARF §6.5.3 OID4VP profile upper bound. Future <c>iat</c> values are also
    /// rejected beyond the same window. Lower values tighten replay; raise carefully if mobile
    /// clock skew becomes a field issue.
    /// </summary>
    public int KbJwtIatSkewSeconds { get; set; } = 60;

    /// <summary>
    /// When set, overrides <see cref="VerifierId"/> in QR codes AND as the
    /// expected KB-JWT <c>aud</c> claim. Used so Android emulator wallets can
    /// reach the API via <c>10.0.2.2</c> while still being able to validate
    /// against the same identity the QR/request-object exposed.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}
