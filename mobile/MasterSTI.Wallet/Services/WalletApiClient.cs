using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MasterSTI.Shared.DTOs.Signing;
using MasterSTI.Shared.DTOs.Wallet;
using Microsoft.Extensions.Logging;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// HTTP client for the MasterSTI API, scoped to the wallet's own session.
/// The Wallet Session JWT is obtained via <see cref="EnsureLoggedInAsync"/> —
/// a closed-loop OID4VP "self-presentation" against the API's Login flow —
/// and persisted in <see cref="SecureStorage"/>. The same JWT is replayed as
/// a Bearer header on every subsequent call.
/// Interface + DTO records live in <c>IWalletApiClient.cs</c> so the test
/// project can link those without a MAUI ProjectReference.
/// </summary>
public sealed class WalletApiClient : IWalletApiClient
{
    private const string TokenKey = "wallet.session.jwt";

    private readonly HttpClient _http;
    private readonly WalletConfig _config;
    private readonly PresentationBuilder _builder;
    private readonly ILogger<WalletApiClient> _logger;
    private string? _cachedToken;

    public WalletApiClient(
        HttpClient http,
        WalletConfig config,
        PresentationBuilder builder,
        ILogger<WalletApiClient> logger)
    {
        _http = http;
        _config = config;
        _builder = builder;
        _logger = logger;
    }

    public async Task<bool> EnsureLoggedInAsync(CancellationToken cancellationToken = default)
    {
        var existing = await GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(existing) && !IsExpired(existing))
            return true;

        try
        {
            return await DoLoginAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Wallet login failed");
            return false;
        }
    }

    public async Task<InboxResult> GetInboxAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureLoggedInAsync(cancellationToken))
            return InboxResult.Failed;

        var token = await GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return InboxResult.Failed;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_config.ApiBaseUrl.TrimEnd('/')}/api/wallet/inbox");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // 401 ends the session — surface as a failure, not a silent empty.
                await SignOutAsync();
                return InboxResult.Failed;
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Inbox fetch failed: HTTP {Status}", response.StatusCode);
                return InboxResult.Failed;
            }

            var payload = await response.Content.ReadFromJsonAsync<InboxResponse>(cancellationToken: cancellationToken);
            return InboxResult.Success(
                (IReadOnlyList<WalletInboxItem>?)payload?.Items ?? Array.Empty<WalletInboxItem>());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inbox fetch network error");
            return InboxResult.Failed;
        }
    }

    public async Task<WalletInboxItemMetaDto?> GetReviewMetaAsync(Guid recipientId, CancellationToken cancellationToken = default)
    {
        if (!await EnsureLoggedInAsync(cancellationToken))
            return null;

        var token = await GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return null;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_config.ApiBaseUrl.TrimEnd('/')}/api/wallet/inbox/{recipientId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await SignOutAsync();
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Review meta fetch failed for {RecipientId}: HTTP {Status}", recipientId, response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<WalletInboxItemMetaDto>(cancellationToken: cancellationToken);
    }

    public async Task<SignedDocInfo?> GetSignedDocInfoAsync(Guid signedDocumentId, CancellationToken cancellationToken = default)
    {
        if (!await EnsureLoggedInAsync(cancellationToken))
            return null;

        var token = await GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_config.ApiBaseUrl.TrimEnd('/')}/api/signed-documents/{signedDocumentId}/info");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await SignOutAsync();
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("SignedDocInfo fetch failed: HTTP {Status}", response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<SignedDocInfo>(cancellationToken: cancellationToken);
    }

    public async Task<byte[]?> DownloadDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (!await EnsureLoggedInAsync(cancellationToken))
            return null;

        var token = await GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return null;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_config.ApiBaseUrl.TrimEnd('/')}/api/documents/{documentId}/download");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await SignOutAsync();
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Document download failed {DocumentId}: HTTP {Status}", documentId, response.StatusCode);
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WalletHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureLoggedInAsync(cancellationToken))
            return Array.Empty<WalletHistoryItem>();

        var token = await GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return Array.Empty<WalletHistoryItem>();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_config.ApiBaseUrl.TrimEnd('/')}/api/wallet/history");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await SignOutAsync();
            return Array.Empty<WalletHistoryItem>();
        }
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("History fetch failed: HTTP {Status}", response.StatusCode);
            return Array.Empty<WalletHistoryItem>();
        }

        var items = await response.Content.ReadFromJsonAsync<List<WalletHistoryItem>>(cancellationToken: cancellationToken);
        return (IReadOnlyList<WalletHistoryItem>?)items ?? Array.Empty<WalletHistoryItem>();
    }

    public async Task<SigningStatusSnapshot?> GetSigningStatusAsync(Guid signingRequestId, CancellationToken cancellationToken = default)
    {
        var token = await EnsureTokenAsync(cancellationToken);
        if (token is null) return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_config.ApiBaseUrl.TrimEnd('/')}/api/signing/{signingRequestId}/status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await SignOutAsync();
                return null;
            }
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<SigningStatusSnapshot>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetSigningStatus failed for {Id}", signingRequestId);
            return null;
        }
    }

    public async Task<TechnicalDetailDto?> GetTechnicalDetailAsync(Guid signingRequestId, CancellationToken cancellationToken = default)
    {
        var token = await EnsureTokenAsync(cancellationToken);
        if (token is null) return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_config.ApiBaseUrl.TrimEnd('/')}/api/signing/{signingRequestId}/technical-detail");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await SignOutAsync();
                return null;
            }
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<TechnicalDetailDto>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetTechnicalDetail failed for {Id}", signingRequestId);
            return null;
        }
    }

    public async Task<PrepareResult?> PrepareSigningAsync(
        Guid documentId,
        Guid recipientId,
        RenderCommitmentDto? renderCommitment = null,
        CancellationToken cancellationToken = default)
    {
        var token = await EnsureTokenAsync(cancellationToken);
        if (token is null) return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.ApiBaseUrl.TrimEnd('/')}/api/signing/prepare")
            {
                Content = JsonContent.Create(new PrepareSigningRequestBody(
                    documentId,
                    recipientId,
                    RequestedBy: "wallet",
                    CredentialId: null,
                    RenderRootHex: renderCommitment?.RootHex,
                    RenderAlgo: renderCommitment?.Algo,
                    RenderDpi: renderCommitment?.Dpi,
                    RenderPageCount: renderCommitment?.PageCount,
                    RenderLocale: renderCommitment?.Locale,
                    RenderProfile: renderCommitment?.Profile)),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(req, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await SignOutAsync();
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PrepareSigning failed: HTTP {Status}", response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<PrepareSigningResponseBody>(cancellationToken: cancellationToken);
            if (body is null) return null;
            return new PrepareResult(body.SigningRequestId, body.HashToSign);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PrepareSigning crashed");
            return null;
        }
    }

    public async Task<RenderCommitmentDto?> GetRenderCommitmentAsync(
        Guid documentId, string locale, CancellationToken cancellationToken = default)
    {
        var token = await EnsureTokenAsync(cancellationToken);
        if (token is null) return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.ApiBaseUrl.TrimEnd('/')}/api/documents/{documentId}/render-commitment")
            {
                Content = JsonContent.Create(new { locale }),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(req, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await SignOutAsync();
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                // 503 / 422 / 409 are documented soft-fail cases (no pinned binary,
                // > 50 pages, prior signer). Wallet proceeds without commitment.
                _logger.LogInformation(
                    "Render commitment unavailable for {DocId}: HTTP {Status}",
                    documentId, response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<RenderCommitmentResponseBody>(
                cancellationToken: cancellationToken);
            if (body is null) return null;

            return new RenderCommitmentDto(
                Profile: body.Profile,
                Algo: body.Algo,
                Dpi: body.Dpi,
                PageCount: body.PageCount,
                Locale: body.Locale,
                RootHex: body.RootHex,
                PdfiumBinarySha256: body.PdfiumBinarySha256);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetRenderCommitment crashed for {DocId}", documentId);
            return null;
        }
    }

    public async Task<SignResult> SignAsync(
        Guid signingRequestId, string pin, string factor, CancellationToken cancellationToken = default)
    {
        var token = await EnsureTokenAsync(cancellationToken);
        if (token is null)
            return SignResult.Failure(SignErrorKind.PinRejected, "Sesiunea portofelului a expirat.");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.ApiBaseUrl.TrimEnd('/')}/api/signing/{signingRequestId}/sign")
            {
                // PIN body intentionally never logged — Serilog destructure on
                // SignDocumentCommand projects only SigningRequestId + Factor.
                Content = JsonContent.Create(new SignRequestBody(pin, factor)),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(req, cancellationToken);
            SignedDocResponse? body = null;
            if (response.IsSuccessStatusCode)
            {
                try { body = await response.Content.ReadFromJsonAsync<SignedDocResponse>(cancellationToken: cancellationToken); }
                catch (Exception ex) { _logger.LogWarning(ex, "Sign response body parse failed"); }
            }
            return SignResultMapper.MapHttp(response.StatusCode, body);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return SignResultMapper.MapException(ex);
        }
    }

    public Task SignOutAsync()
    {
        _cachedToken = null;
        SecureStorage.Remove(TokenKey);
        return Task.CompletedTask;
    }

    private async Task<string?> EnsureTokenAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureLoggedInAsync(cancellationToken))
            return null;
        var token = await GetTokenAsync();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    // ---- internals ----

    private async Task<bool> DoLoginAsync(CancellationToken cancellationToken)
    {
        var apiBase = _config.ApiBaseUrl.TrimEnd('/');

        // Pre-flight: detect stored SD-JWTs the server can no longer validate and
        // route the wallet back through Onboarding instead of looping on
        // "Conexiune eșuată". Two failure shapes we catch locally:
        //   1. exp passed — HandleVpResponseHandler raises SecurityTokenExpired.
        //   2. Issuer JWT has no `_sd[]` claim — minted before commit b35108c
        //      tightened SD-JWT §4 digest commitment. Server rejects with
        //      "Disclosure digest not in issuer _sd[]".
        try
        {
            var stored = await SecureStorage.GetAsync("wallet.sdjwt");
            if (!string.IsNullOrEmpty(stored))
            {
                string? staleReason = null;

                try
                {
                    var parsed = SdJwtParser.Parse(stored);
                    if (parsed.Exp is { } exp && exp <= DateTimeOffset.UtcNow.AddSeconds(30))
                        staleReason = $"expired at {exp:u}";
                }
                catch (SdJwtFormatException) { /* fall through to _sd[] check */ }

                if (staleReason is null && !IssuerJwtHasSdCommitment(stored))
                    staleReason = "issuer JWT missing _sd[] — pre-hardening shape";

                if (staleReason is not null)
                {
                    _logger.LogWarning("Stored SD-JWT stale ({Reason}) — clearing for re-enrollment", staleReason);
                    SecureStorage.Remove("wallet.sdjwt");
                    await SignOutAsync();
                    try { _ = MainThread.InvokeOnMainThreadAsync(() => Shell.Current?.GoToAsync("//onboarding") ?? Task.CompletedTask); }
                    catch { }
                    return false;
                }
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "SD-JWT pre-flight check threw — proceeding with login attempt"); }

        HttpResponseMessage init;
        try
        {
            init = await _http.PostAsJsonAsync(
                $"{apiBase}/api/wallet/auth",
                new { Purpose = "Login" },
                cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }
        if (!init.IsSuccessStatusCode)
        {
            _logger.LogWarning("Wallet auth init failed: HTTP {Status}", init.StatusCode);
            return false;
        }

        InitiateAuthResponse? initResp;
        try
        {
            initResp = await init.Content.ReadFromJsonAsync<InitiateAuthResponse>(cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }
        if (initResp is null || string.IsNullOrEmpty(initResp.State))
        {
            return false;
        }

        // ADR-0011: request_object is a signed JWT (ES256, client_id_scheme=pre-registered).
        // Fetch raw, verify against pinned EC P-256 public key BEFORE consuming any field.
        var reqObjUrl = $"{apiBase}/api/eudiw/request-object/{initResp.State}";
        string rawJwt;
        try
        {
            rawJwt = await _http.GetStringAsync(reqObjUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "request_object fetch failed for state {State}", initResp.State);
            return false;
        }

        RequestObjectClaims claims;
        try
        {
            claims = RequestObjectVerifier.VerifyAndParse(
                rawJwt,
                _config.TrustedRequestObjectPublicKeyPem,
                _config.TrustedRequestObjectKid,
                _config.ExpectedVerifierClientId);
        }
        catch (RequestObjectVerificationException ex)
        {
            _logger.LogWarning(ex, "request_object verification rejected — refusing to present PID");
            return false;
        }

        // Cross-check the wallet-side nonce (returned by InitiateWalletAuth) against the
        // signed token's nonce. Both should match — divergence means either the cache
        // entry was tampered with or the wrong state was bound at signing time.
        if (!string.Equals(claims.Nonce, initResp.Nonce, StringComparison.Ordinal))
        {
            _logger.LogWarning("request_object nonce mismatch — init={Init} signed={Signed}",
                initResp.Nonce, claims.Nonce);
            return false;
        }

        var vpRequest = new OpenId4VpRequest(
            ClientId: claims.ClientId,
            ResponseUri: claims.ResponseUri,
            Nonce: claims.Nonce,
            State: claims.State,
            PresentationDefinitionJson: claims.PresentationDefinitionJson);

        string? vpToken;
        try
        {
            vpToken = await _builder.BuildAsync(vpRequest);
        }
        catch (WalletKeyOrphanedException ex)
        {
            // AndroidKeyStore lost the private key while the alias persisted —
            // recover by wiping the stored PID and session JWT so the next app
            // launch routes back through Onboarding for a fresh enrollment.
            _logger.LogWarning(ex, "Wallet key orphaned — clearing SD-JWT + session JWT to force re-enrollment");
            SecureStorage.Remove("wallet.sdjwt");
            await SignOutAsync();
            try { _ = MainThread.InvokeOnMainThreadAsync(() => Shell.Current?.GoToAsync("//onboarding") ?? Task.CompletedTask); }
            catch { /* Shell may not be ready; next app launch will route via AppShell.OnAppearing */ }
            return false;
        }
        if (string.IsNullOrEmpty(vpToken))
        {
            _logger.LogWarning("PresentationBuilder returned null vp_token");
            return false;
        }

        HttpResponseMessage post;
        try
        {
            post = await _http.PostAsJsonAsync(
                claims.ResponseUri,
                new { vp_token = vpToken, state = claims.State },
                cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }
        if (!post.IsSuccessStatusCode)
        {
            _logger.LogWarning("VP response rejected: HTTP {Status}", post.StatusCode);
            return false;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(500, cancellationToken);
            var status = await _http.GetFromJsonAsync<AuthStatusResponse>(
                $"{apiBase}/api/wallet/auth/{initResp.State}",
                cancellationToken);
            if (status?.Login is { Token: { } token })
            {
                await SetTokenAsync(token);
                _logger.LogInformation("Wallet login complete");
                return true;
            }
            if (string.Equals(status?.Status, "failed", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        _logger.LogWarning("Wallet login polling timed out");
        return false;
    }

    private async Task<string?> GetTokenAsync()
    {
        if (!string.IsNullOrEmpty(_cachedToken))
            return _cachedToken;
        _cachedToken = await SecureStorage.GetAsync(TokenKey);
        return _cachedToken;
    }

    private Task SetTokenAsync(string token)
    {
        _cachedToken = token;
        return SecureStorage.SetAsync(TokenKey, token);
    }

    // Walk the issuer JWT payload looking for a top-level `_sd` array. SD-JWTs
    // minted before commit b35108c had no `_sd[]` claim and the API now rejects
    // them. No signature check — this just spots the wire shape.
    private static bool IssuerJwtHasSdCommitment(string sdJwt)
    {
        try
        {
            var firstTilde = sdJwt.IndexOf('~');
            var issuerJwt = firstTilde < 0 ? sdJwt : sdJwt[..firstTilde];
            var parts = issuerJwt.Split('.');
            if (parts.Length != 3) return false;
            var payloadJson = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("_sd", out var sd)
                && sd.ValueKind == JsonValueKind.Array
                && sd.GetArrayLength() > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Cheap client-side JWT exp check (no signature verification). Server is
    /// the authority; this just lets us pre-empt a 401 round-trip when the
    /// cached token is obviously stale.
    /// </summary>
    private static bool IsExpired(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return true;
            var payloadJson = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("exp", out var expProp)) return false;
            var exp = expProp.GetInt64();
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return exp <= nowUnix + 30;
        }
        catch
        {
            return true;
        }
    }

    private static string Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    // ---- wire shapes ----

    private sealed record InitiateAuthResponse(
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("requestUri")] string RequestUri,
        [property: JsonPropertyName("qrCode")] string QrCode,
        [property: JsonPropertyName("expiresInSeconds")] int ExpiresInSeconds,
        [property: JsonPropertyName("nonce")] string Nonce);

    private sealed record AuthStatusResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("login")] LoginPayload? Login);

    private sealed record LoginPayload(
        [property: JsonPropertyName("token")] string Token);

    private sealed record InboxResponse(
        [property: JsonPropertyName("items")] List<WalletInboxItem>? Items);

    // Prepare/Sign wire shapes — kept private to the client since callers see
    // PrepareResult / SignResult instead. Render* fields are camelCase to match
    // the server-side PrepareSigningRequest record (ASP.NET model binding).
    private sealed record PrepareSigningRequestBody(
        [property: JsonPropertyName("documentId")] Guid DocumentId,
        [property: JsonPropertyName("recipientId")] Guid RecipientId,
        [property: JsonPropertyName("requestedBy")] string RequestedBy,
        [property: JsonPropertyName("credentialId")] string? CredentialId,
        [property: JsonPropertyName("renderRootHex")] string? RenderRootHex = null,
        [property: JsonPropertyName("renderAlgo")] string? RenderAlgo = null,
        [property: JsonPropertyName("renderDpi")] int? RenderDpi = null,
        [property: JsonPropertyName("renderPageCount")] int? RenderPageCount = null,
        [property: JsonPropertyName("renderLocale")] string? RenderLocale = null,
        [property: JsonPropertyName("renderProfile")] string? RenderProfile = null);

    private sealed record RenderCommitmentResponseBody(
        [property: JsonPropertyName("profile")] string Profile,
        [property: JsonPropertyName("algo")] string Algo,
        [property: JsonPropertyName("dpi")] int Dpi,
        [property: JsonPropertyName("pageCount")] int PageCount,
        [property: JsonPropertyName("locale")] string Locale,
        [property: JsonPropertyName("rootHex")] string RootHex,
        [property: JsonPropertyName("pdfiumBinarySha256")] string PdfiumBinarySha256);

    private sealed record PrepareSigningResponseBody(
        [property: JsonPropertyName("signingRequestId")] Guid SigningRequestId,
        // Server's PrepareSigningResponse names this field DocumentHash
        // (camelCase on the wire). Wallet surfaces it as HashToSign in
        // PrepareResult since the wallet caller cares about the DTBS hash,
        // not the document hash. Same byte string, different perspective.
        [property: JsonPropertyName("documentHash")] string HashToSign);

    private sealed record SignRequestBody(
        [property: JsonPropertyName("pin")] string Pin,
        [property: JsonPropertyName("factor")] string Factor);
}
