namespace MasterSTI.Mock.Issuer.Data;

/// <summary>
/// Append-only log: one row per PID SD-JWT minted at <c>/eudiw/issue-pid</c>.
/// Records which registry identity was attested, the validity window baked into
/// the issuer JWT, and the wallet's <c>cnf.jwk</c> thumbprint when key-bound.
/// </summary>
public sealed class IssuedCredential
{
    public Guid Id { get; set; }
    public Guid IdentityId { get; set; }
    public Identity? Identity { get; set; }

    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>RFC 7638 thumbprint of the wallet key bound into <c>cnf.jwk</c>; null if absent.</summary>
    public string? CnfJwkThumbprint { get; set; }
}
