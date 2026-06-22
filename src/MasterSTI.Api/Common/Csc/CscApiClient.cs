using System.Net.Http.Json;

namespace MasterSTI.Api.Common.Csc;

public sealed class CscApiClient : ICscApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<CscApiClient> _logger;

    public CscApiClient(HttpClient http, ILogger<CscApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string> AuthLoginAsync(string username, string password, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/csc/v2/auth/login",
            new CscLoginRequest(username, password), ct);
        await EnsureSuccessOrThrow(resp, "auth/login", ct);
        var result = await resp.Content.ReadFromJsonAsync<CscLoginResponse>(ct)
            ?? throw new InvalidOperationException("Empty auth/login response");
        return result.access_token;
    }

    public async Task<IReadOnlyList<string>> ListCredentialsAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/csc/v2/credentials/list");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = JsonContent.Create(new CscCredentialsListRequest());
        var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessOrThrow(resp, "credentials/list", ct);
        var result = await resp.Content.ReadFromJsonAsync<CscCredentialsListResponse>(ct)
            ?? throw new InvalidOperationException("Empty credentials/list response");
        return result.credentialIDs;
    }

    public async Task<CscCredentialInfoResponse> GetCredentialInfoAsync(string accessToken, string credentialId, CancellationToken ct = default, string? signerCn = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/csc/v2/credentials/info");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        if (!string.IsNullOrWhiteSpace(signerCn))
            req.Headers.TryAddWithoutValidation("X-Mock-Signer-Cn", signerCn);
        req.Content = JsonContent.Create(new CscCredentialInfoRequest(credentialId));
        var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessOrThrow(resp, "credentials/info", ct);
        return await resp.Content.ReadFromJsonAsync<CscCredentialInfoResponse>(ct)
            ?? throw new InvalidOperationException("Empty credentials/info response");
    }

    public async Task<string> AuthorizeCredentialAsync(string accessToken, string credentialId, string[] hashes, string factorId, string factorValue, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/csc/v2/credentials/authorize");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var body = new CscAuthorizeRequest(
            credentialID: credentialId,
            numSignatures: hashes.Length,
            hash: hashes,
            hashAlgorithmOID: "2.16.840.1.101.3.4.2.1",
            authData: new[] { new CscAuthData(factorId, factorValue) },
            description: "Authorization for signing");
        req.Content = JsonContent.Create(body);
        var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessOrThrow(resp, "credentials/authorize", ct);
        var result = await resp.Content.ReadFromJsonAsync<CscAuthorizeResponse>(ct)
            ?? throw new InvalidOperationException("Empty credentials/authorize response");
        return result.SAD;
    }

    public async Task<string[]> SignHashAsync(string accessToken, string credentialId, string sad, string[] hashesBase64, CancellationToken ct = default, string? signerCn = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/csc/v2/signatures/signHash");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        if (!string.IsNullOrWhiteSpace(signerCn))
            req.Headers.TryAddWithoutValidation("X-Mock-Signer-Cn", signerCn);
        req.Content = JsonContent.Create(new CscSignHashRequest(credentialId, sad, hashesBase64));
        var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessOrThrow(resp, "signatures/signHash", ct);
        var result = await resp.Content.ReadFromJsonAsync<CscSignHashResponse>(ct)
            ?? throw new InvalidOperationException("Empty signatures/signHash response");
        return result.signatures;
    }

    private async Task EnsureSuccessOrThrow(HttpResponseMessage resp, string op, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        string upstream = string.Empty;
        try { upstream = await resp.Content.ReadAsStringAsync(ct); } catch { }
        _logger.LogWarning("CSC {Op} failed with {Status}. Upstream: {Body}", op, (int)resp.StatusCode, upstream);
        throw new CscApiException($"CSC {op} returned {(int)resp.StatusCode}", (int)resp.StatusCode);
    }
}

public sealed class CscApiException : Exception
{
    public int StatusCode { get; }
    public CscApiException(string message, int statusCode) : base(message) => StatusCode = statusCode;
}
