using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// POSTs a VP token to the verifier's <c>response_uri</c>.
/// </summary>
public sealed class PresentationClient
{
    private readonly HttpClient _http;
    private readonly ILogger<PresentationClient> _logger;

    public PresentationClient(HttpClient http, ILogger<PresentationClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Sends the VP response.
    /// Returns <c>(Success: true, Error: null)</c> on HTTP 2xx.
    /// Returns <c>(Success: false, Error: &lt;body&gt;)</c> on failure.
    /// </summary>
    public async Task<(bool Success, string? Error)> SendAsync(
        OpenId4VpRequest request,
        string vpToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new VpTokenResponse(
                VpToken: vpToken,
                State: request.State);

            var response = await _http.PostAsJsonAsync(request.ResponseUri, body, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("VP token accepted by verifier");
                return (true, null);
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Verifier rejected VP token: HTTP {Status}", response.StatusCode);
            return (false, $"HTTP {(int)response.StatusCode}: {errorBody}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PresentationClient failed: {Type}", ex.GetType().Name);
            return (false, ex.Message);
        }
    }

    // Matches the server-side VpTokenResponse record shape
    private sealed record VpTokenResponse(
        [property: JsonPropertyName("vp_token")] string VpToken,
        [property: JsonPropertyName("state")] string State);
}
