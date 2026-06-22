namespace MasterSTI.Api.Common.Csc;

public interface ICscApiClient
{
    Task<string> AuthLoginAsync(string username, string password, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListCredentialsAsync(string accessToken, CancellationToken ct = default);
    Task<CscCredentialInfoResponse> GetCredentialInfoAsync(string accessToken, string credentialId, CancellationToken ct = default, string? signerCn = null);

    /// <summary>
    /// Authorize a credential per CSC API v2 §11.5 and return the SAD.
    /// <paramref name="factorId"/> is the <c>authData[].id</c> value (e.g. <c>PIN</c>, <c>BIO</c>);
    /// <paramref name="factorValue"/> is the corresponding <c>authData[].value</c>.
    /// SAD is ephemeral — never log or persist.
    /// </summary>
    Task<string> AuthorizeCredentialAsync(string accessToken, string credentialId, string[] hashes, string factorId, string factorValue, CancellationToken ct = default);

    /// <summary>Sign hashes using a previously obtained SAD. SAD is consumed here and never stored.</summary>
    Task<string[]> SignHashAsync(string accessToken, string credentialId, string sad, string[] hashesBase64, CancellationToken ct = default, string? signerCn = null);
}
