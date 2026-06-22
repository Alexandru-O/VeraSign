using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MasterSTI.Wallet.Config;
using Microsoft.Extensions.Logging;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// Handles first-time wallet enrollment:
/// 1. Generates (or loads) the device-bound EC P-256 key.
/// 2. POSTs the public JWK + email to the Mock.Issuer PID issuance endpoint.
/// 3. Stores the returned SD-JWT in SecureStorage.
/// </summary>
public sealed class EnrollmentService
{
    private readonly IDeviceKeyService _deviceKeyService;
    private readonly HttpClient _http;
    private readonly WalletConfig _config;
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(
        IDeviceKeyService deviceKeyService,
        HttpClient http,
        WalletConfig config,
        ILogger<EnrollmentService> logger)
    {
        _deviceKeyService = deviceKeyService;
        _http = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Enrols the wallet by obtaining a PID SD-JWT from Mock.Issuer.
    /// <paramref name="email"/> selects which seeded registry identity the wallet
    /// represents; when null/blank the compile-time persona email is used (the
    /// canned default — itself a registry-seeded identity). Mock.Issuer's
    /// <c>/eudiw/issue-pid</c> is registry-gated, so the email MUST resolve to a
    /// seeded identity or issuance returns 404.
    /// Returns <see langword="true"/> on success.
    /// </summary>
    public async Task<bool> EnrollAsync(string? email = null)
    {
        try
        {
            var jwk = await _deviceKeyService.GenerateOrLoadPublicJwkAsync();
            _logger.LogInformation("Device key loaded (kid={KeyId})", jwk.KeyId);

            var resolvedEmail = string.IsNullOrWhiteSpace(email)
                ? WalletPersona.Email
                : email.Trim();

            var requestBody = new IssuePidRequest(
                Jwk: new IssuePidJwk(
                    Kty: jwk.Kty,
                    Crv: jwk.Crv,
                    X: jwk.X,
                    Y: jwk.Y),
                Email: resolvedEmail);

            var url = $"{_config.IssuerBaseUrl}/eudiw/issue-pid";
            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsJsonAsync(url, requestBody);
            }
            catch (Exception netEx)
            {
                _logger.LogError(netEx, "PID issuance network failure to {Url}", url);
                throw new InvalidOperationException($"NET {netEx.GetType().Name}: {netEx.Message} @ {url}", netEx);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("PID issuance failed: HTTP {Status} body={Body}", response.StatusCode, body);
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode} @ {url} body={body}");
            }

            var result = await response.Content.ReadFromJsonAsync<IssuePidResponse>();
            if (result is null || string.IsNullOrEmpty(result.Sdjwt))
            {
                _logger.LogWarning("PID issuance returned empty SD-JWT");
                throw new InvalidOperationException($"Empty SD-JWT from {url}");
            }

            await SecureStorage.SetAsync("wallet.sdjwt", result.Sdjwt);
            _logger.LogInformation("PID SD-JWT stored successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enrollment failed: {Type}", ex.GetType().Name);
            throw;
        }
    }

    // ----- Local DTOs matching the Mock.Issuer contract -----------------------
    // Mock.Issuer is registry-gated: only `jwk` + `email` matter; the issuer
    // sources name/birth-date from its seeded registry, not the request.

    private sealed record IssuePidRequest(
        [property: JsonPropertyName("jwk")] IssuePidJwk Jwk,
        [property: JsonPropertyName("email")] string Email);

    private sealed record IssuePidJwk(
        [property: JsonPropertyName("kty")] string Kty,
        [property: JsonPropertyName("crv")] string Crv,
        [property: JsonPropertyName("x")] string X,
        [property: JsonPropertyName("y")] string Y);

    private sealed record IssuePidResponse(
        [property: JsonPropertyName("sdjwt")] string Sdjwt,
        [property: JsonPropertyName("disclosures")] string[]? Disclosures);
}
