using Microsoft.IdentityModel.Tokens;

namespace MasterSTI.Api.Common.Eudiw;

/// <summary>
/// Singleton holder for the trusted EUDIW issuer public key. Populated once
/// at startup by <see cref="IssuerPemLoader"/> and read by
/// <see cref="SdJwtValidator"/> on every validation. Keeping the resolved
/// <see cref="SecurityKey"/> off the hot path is what lets the validator stay
/// synchronous and avoid <c>.GetAwaiter().GetResult()</c> on a network call.
/// </summary>
public interface IIssuerKeyHolder
{
    /// <summary>The resolved issuer key, or <c>null</c> if no key was configured / loaded.</summary>
    SecurityKey? Current { get; }

    /// <summary>SHA-256 hex of the PEM bytes that produced <see cref="Current"/>, or <c>null</c>.</summary>
    string? CurrentPemSha256 { get; }

    /// <summary>Replace the current key. Called by the loader at startup.</summary>
    void Set(SecurityKey? key, string? pemSha256);
}

public sealed class IssuerKeyHolder : IIssuerKeyHolder
{
    private SecurityKey? _key;
    private string? _hash;
    private readonly object _gate = new();

    public SecurityKey? Current
    {
        get { lock (_gate) return _key; }
    }

    public string? CurrentPemSha256
    {
        get { lock (_gate) return _hash; }
    }

    public void Set(SecurityKey? key, string? pemSha256)
    {
        lock (_gate)
        {
            _key = key;
            _hash = pemSha256;
        }
    }
}
