using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MasterSTI.Api.Common.Csc;
using Microsoft.Extensions.Logging.Abstractions;

namespace MasterSTI.UnitTests;

public class CscApiClientTests
{
    private static (CscApiClient client, MockHttpMessageHandler handler) CreateClient()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://mock-qtsp.test") };
        var client = new CscApiClient(httpClient, NullLogger<CscApiClient>.Instance);
        return (client, handler);
    }

    [Fact]
    public async Task AuthLoginAsync_ReturnsAccessToken()
    {
        var (client, handler) = CreateClient();
        handler.SetResponse("/csc/v2/auth/login", new
        {
            access_token = "test-token-123",
            token_type = "Bearer",
            expires_in = 3600
        });

        var token = await client.AuthLoginAsync("user", "pass");

        Assert.Equal("test-token-123", token);
    }

    [Fact]
    public async Task ListCredentialsAsync_ReturnsCredentialIds()
    {
        var (client, handler) = CreateClient();
        handler.SetResponse("/csc/v2/credentials/list", new
        {
            credentialIDs = new[] { "cred-001", "cred-002" }
        });

        var creds = await client.ListCredentialsAsync("token");

        Assert.Equal(2, creds.Count);
        Assert.Contains("cred-001", creds);
    }

    [Fact]
    public async Task AuthorizeCredentialAsync_ReturnsSad()
    {
        var (client, handler) = CreateClient();
        handler.SetResponse("/csc/v2/credentials/authorize", new
        {
            SAD = "test-sad-ephemeral",
            expiresIn = 3600
        });

        var sad = await client.AuthorizeCredentialAsync("token", "cred-001", new[] { "hash1" }, "PIN", "1234");

        Assert.Equal("test-sad-ephemeral", sad);
    }

    // ADR-0010: assert the CSC v2 §11.5 wire shape — singular `hash`,
    // explicit `hashAlgorithmOID`, `authData[]` with the factor pair.
    [Fact]
    public async Task AuthorizeCredentialAsync_PostsCscV2WireShape()
    {
        var (client, handler) = CreateClient();
        handler.SetResponse("/csc/v2/credentials/authorize", new { SAD = "x", expiresIn = 60 });

        await client.AuthorizeCredentialAsync("token", "cred-001", new[] { "hashBase64==" }, "BIO", "bio-attested");

        var body = handler.LastRequestBody("/csc/v2/credentials/authorize");
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("cred-001", root.GetProperty("credentialID").GetString());
        Assert.Equal(1, root.GetProperty("numSignatures").GetInt32());
        Assert.Equal("hashBase64==", root.GetProperty("hash")[0].GetString());
        Assert.Equal("2.16.840.1.101.3.4.2.1", root.GetProperty("hashAlgorithmOID").GetString());

        var authData = root.GetProperty("authData");
        Assert.Equal(1, authData.GetArrayLength());
        Assert.Equal("BIO", authData[0].GetProperty("id").GetString());
        Assert.Equal("bio-attested", authData[0].GetProperty("value").GetString());

        // Legacy scalar PIN must be absent on the wire.
        Assert.False(root.TryGetProperty("PIN", out _));
        Assert.False(root.TryGetProperty("hashes", out _));
    }

    [Fact]
    public async Task SignHashAsync_ReturnsSignatures()
    {
        var (client, handler) = CreateClient();
        handler.SetResponse("/csc/v2/signatures/signHash", new
        {
            signatures = new[] { "base64sigvalue==" }
        });

        var sigs = await client.SignHashAsync("token", "cred-001", "sad", new[] { "hashBase64==" });

        Assert.Single(sigs);
        Assert.Equal("base64sigvalue==", sigs[0]);
    }

    [Fact]
    public async Task SignHashAsync_PostsCscV2WireShape()
    {
        var (client, handler) = CreateClient();
        handler.SetResponse("/csc/v2/signatures/signHash", new { signatures = new[] { "sigBase64==" } });

        await client.SignHashAsync("token", "cred-001", "sad-value", new[] { "hashBase64==" });

        var body = handler.LastRequestBody("/csc/v2/signatures/signHash");
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("hashBase64==", root.GetProperty("hash")[0].GetString());
        Assert.Equal("2.16.840.1.101.3.4.2.1", root.GetProperty("hashAlgorithmOID").GetString());
        Assert.False(root.TryGetProperty("hashes", out _));
    }

    [Fact]
    public async Task GetCredentialInfoAsync_ReturnsCertInfo()
    {
        var (client, handler) = CreateClient();
        handler.SetResponse("/csc/v2/credentials/info", new
        {
            description = "Test cert",
            key = new { status = "enabled", algo = new[] { "1.2.840.113549.1.1.11" }, len = 2048 },
            cert = new { status = "valid", certificates = new[] { "certBase64==" }, issuerDN = "CN=Test", serialNumber = "01", subjectDN = "CN=Signer", validFrom = "20240101000000Z", validTo = "20260101000000Z" },
            authMode = "explicit",
            multisign = 1,
            lang = "en-US"
        });

        var info = await client.GetCredentialInfoAsync("token", "cred-001");

        Assert.Equal("Test cert", info.description);
        Assert.Equal("certBase64==", info.cert.certificates[0]);
    }
}

// Simple mock HTTP handler for tests
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, object> _responses = new();
    private readonly Dictionary<string, string> _lastBodies = new();

    public void SetResponse(string path, object responseBody)
    {
        _responses[path] = responseBody;
    }

    public string LastRequestBody(string path) =>
        _lastBodies.TryGetValue(path, out var b)
            ? b
            : throw new InvalidOperationException($"No captured body for {path}");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.PathAndQuery ?? "";

        // Match by prefix
        var key = _responses.Keys.FirstOrDefault(k => path.StartsWith(k));
        if (key is null)
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        if (request.Content is not null)
            _lastBodies[key] = await request.Content.ReadAsStringAsync(cancellationToken);

        var json = JsonSerializer.Serialize(_responses[key]);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
