using System.Text.Json;

namespace MasterSTI.Api.Common.Eudiw;

public sealed class OpenId4VpService
{
    private readonly IConfiguration _config;
    private readonly ILogger<OpenId4VpService> _logger;

    public OpenId4VpService(IConfiguration config, ILogger<OpenId4VpService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Creates an OpenID4VP authorization request and returns the QR payload (deep link).
    ///
    /// When <c>Eudiw:PublicBaseUrl</c> is configured (e.g. <c>https://10.0.2.2:7001</c>),
    /// <c>client_id</c> and <c>response_uri</c> in the QR code use that base URL so that
    /// Android emulator wallets can resolve them. Falls back to <c>Eudiw:VerifierId</c>
    /// and <c>Eudiw:ResponseUri</c> when not set.
    /// </summary>
    public AuthorizationRequest CreateAuthorizationRequest(string nonce, string state)
    {
        var publicBase = _config["Eudiw:PublicBaseUrl"];
        var verifierId = _config["Eudiw:VerifierId"] ?? "https://localhost:7001";
        var responseUri = _config["Eudiw:ResponseUri"] ?? "https://localhost:7001/api/eudiw/response";

        string effectiveClientId;
        string effectiveResponseUri;

        if (!string.IsNullOrWhiteSpace(publicBase))
        {
            effectiveClientId = publicBase.TrimEnd('/');
            effectiveResponseUri = $"{publicBase.TrimEnd('/')}/api/eudiw/response";
            _logger.LogDebug("QR uses PublicBaseUrl {PublicBase}", publicBase);
        }
        else
        {
            effectiveClientId = verifierId;
            effectiveResponseUri = responseUri;
        }

        return new AuthorizationRequest(
            ClientId: effectiveClientId,
            ResponseType: "vp_token",
            ResponseMode: "direct_post",
            ResponseUri: effectiveResponseUri,
            Nonce: nonce,
            State: state,
            PresentationDefinition: PresentationDefinition.BuildPid());
    }

    /// <summary>
    /// Builds the openid4vp:// deep link / QR payload from the authorization request.
    /// </summary>
    public string BuildQrPayload(AuthorizationRequest request)
    {
        var presentationDefJson = Uri.EscapeDataString(JsonSerializer.Serialize(request.PresentationDefinition));

        return $"openid4vp://?response_type=vp_token" +
               $"&client_id={Uri.EscapeDataString(request.ClientId)}" +
               $"&response_mode=direct_post" +
               $"&response_uri={Uri.EscapeDataString(request.ResponseUri)}" +
               $"&nonce={Uri.EscapeDataString(request.Nonce)}" +
               $"&state={Uri.EscapeDataString(request.State)}" +
               $"&presentation_definition={presentationDefJson}";
    }

    /// <summary>
    /// Builds a SHORT openid4vp:// payload that uses <c>request_uri</c> indirection.
    /// The wallet fetches the full authorization request from
    /// <c>{base}/api/eudiw/request-object/{state}</c>. Reduces QR payload from ~600 to ~120 bytes
    /// — much easier to scan on screens / phone cameras at modal sizes.
    /// </summary>
    public string BuildQrPayloadShort(string state)
    {
        var publicBase = _config["Eudiw:PublicBaseUrl"];
        var verifierId = _config["Eudiw:VerifierId"] ?? "https://localhost:7001";
        var clientId = !string.IsNullOrWhiteSpace(publicBase) ? publicBase.TrimEnd('/') : verifierId;
        var requestUri = $"{clientId}/api/eudiw/request-object/{Uri.EscapeDataString(state)}";

        return $"openid4vp://?client_id={Uri.EscapeDataString(clientId)}" +
               $"&request_uri={Uri.EscapeDataString(requestUri)}";
    }
}
