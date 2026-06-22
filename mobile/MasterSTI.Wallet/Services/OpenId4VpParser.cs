using System.Web;
using Microsoft.Extensions.Logging;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// Typed result of parsing an openid4vp:// authorization request URI.
/// </summary>
public sealed record OpenId4VpRequest(
    string ClientId,
    string ResponseUri,
    string Nonce,
    string State,
    string PresentationDefinitionJson);

/// <summary>
/// Parses an <c>openid4vp://</c> deep-link URI into a strongly-typed
/// <see cref="OpenId4VpRequest"/>. Rejects URIs that are missing mandatory fields.
/// </summary>
public sealed class OpenId4VpParser
{
    private readonly ILogger<OpenId4VpParser> _logger;

    public OpenId4VpParser(ILogger<OpenId4VpParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses the raw URI. Returns <see langword="null"/> if any mandatory field is absent.
    /// </summary>
    public OpenId4VpRequest? Parse(string rawUri)
    {
        try
        {
            var uri = new Uri(rawUri);
            var qs = HttpUtility.ParseQueryString(uri.Query);

            var clientId = qs["client_id"];
            var responseUri = qs["response_uri"];
            var nonce = qs["nonce"];
            var state = qs["state"];
            // presentation_definition is already URL-decoded by ParseQueryString
            var presentationDefinitionJson = qs["presentation_definition"];

            if (string.IsNullOrEmpty(clientId))
            {
                _logger.LogWarning("openid4vp:// missing client_id");
                return null;
            }
            if (string.IsNullOrEmpty(responseUri))
            {
                _logger.LogWarning("openid4vp:// missing response_uri");
                return null;
            }
            if (string.IsNullOrEmpty(nonce))
            {
                _logger.LogWarning("openid4vp:// missing nonce");
                return null;
            }
            if (string.IsNullOrEmpty(state))
            {
                _logger.LogWarning("openid4vp:// missing state");
                return null;
            }
            if (string.IsNullOrEmpty(presentationDefinitionJson))
            {
                _logger.LogWarning("openid4vp:// missing presentation_definition");
                return null;
            }

            return new OpenId4VpRequest(
                ClientId: Uri.UnescapeDataString(clientId),
                ResponseUri: Uri.UnescapeDataString(responseUri),
                Nonce: Uri.UnescapeDataString(nonce),
                State: Uri.UnescapeDataString(state),
                PresentationDefinitionJson: presentationDefinitionJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse openid4vp:// URI");
            return null;
        }
    }

    /// <summary>
    /// Extracts the human-readable field paths from a presentation_definition JSON string.
    /// Returns a flattened list of <c>$.claim_name</c> path strings extracted from
    /// <c>input_descriptors[].constraints.fields[].path[0]</c>.
    /// Falls back gracefully on any parse error.
    /// </summary>
    public IReadOnlyList<string> ExtractClaimPaths(string presentationDefinitionJson)
    {
        var paths = new List<string>();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(presentationDefinitionJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("input_descriptors", out var descriptors))
                return paths;

            foreach (var descriptor in descriptors.EnumerateArray())
            {
                if (!descriptor.TryGetProperty("constraints", out var constraints))
                    continue;
                if (!constraints.TryGetProperty("fields", out var fields))
                    continue;

                foreach (var field in fields.EnumerateArray())
                {
                    if (!field.TryGetProperty("path", out var pathArr))
                        continue;

                    var first = pathArr.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var raw = first.GetString() ?? string.Empty;
                        // Strip JsonPath prefix "$."; surface the bare claim name
                        var display = raw.StartsWith("$.", StringComparison.Ordinal)
                            ? raw[2..]
                            : raw;
                        if (!string.IsNullOrEmpty(display))
                            paths.Add(display);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract claim paths from presentation_definition");
        }

        return paths;
    }
}
