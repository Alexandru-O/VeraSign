using System.Security.Cryptography;
using System.Text.Json;
using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Wallet.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MasterSTI.UnitTests;

/// <summary>
/// ADR-0011 server-side: signer emits a JWS that the wallet's pinned-key
/// verifier accepts. Roundtrip + wire-shape assertions.
/// </summary>
public class RequestObjectSignerTests
{
    private const string Kid = "verasign-rqo-v1";
    private const string Issuer = "https://localhost:7001";

    private static (RequestObjectSigner signer, string publicPem) CreateSigner()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privatePem = ec.ExportPkcs8PrivateKeyPem();
        var publicPem = ec.ExportSubjectPublicKeyInfoPem();
        var options = Options.Create(new RequestObjectSigningOptions
        {
            PrivateKeyPem = privatePem,
            Kid = Kid,
            ExpiresInSeconds = 300,
        });
        var signer = new RequestObjectSigner(options, NullLogger<RequestObjectSigner>.Instance);
        return (signer, publicPem);
    }

    private static AuthorizationRequest SampleAuthReq() => new(
        ClientId: Issuer,
        ResponseType: "vp_token",
        ResponseMode: "direct_post",
        ResponseUri: $"{Issuer}/api/eudiw/response",
        Nonce: "nonce-abc",
        State: "state-xyz",
        PresentationDefinition: PresentationDefinition.BuildPid());

    [Fact]
    public void IsConfigured_FalseWhenPemBlank()
    {
        var signer = new RequestObjectSigner(
            Options.Create(new RequestObjectSigningOptions { PrivateKeyPem = null }),
            NullLogger<RequestObjectSigner>.Instance);
        Assert.False(signer.IsConfigured);
    }

    [Fact]
    public void Sign_EmitsThreePartJwsWithExpectedHeader()
    {
        var (signer, _) = CreateSigner();
        var jwt = signer.Sign(SampleAuthReq(), Issuer);

        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        var headerJson = System.Text.Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
        using var hdr = JsonDocument.Parse(headerJson);
        Assert.Equal("ES256", hdr.RootElement.GetProperty("alg").GetString());
        Assert.Equal("oauth-authz-req+jwt", hdr.RootElement.GetProperty("typ").GetString());
        Assert.Equal(Kid, hdr.RootElement.GetProperty("kid").GetString());
    }

    [Fact]
    public void Sign_PayloadCarriesOid4VpAndJwtClaims()
    {
        var (signer, _) = CreateSigner();
        var jwt = signer.Sign(SampleAuthReq(), Issuer);

        var parts = jwt.Split('.');
        var payloadJson = System.Text.Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var p = JsonDocument.Parse(payloadJson);
        var root = p.RootElement;

        Assert.Equal(Issuer, root.GetProperty("iss").GetString());
        Assert.Equal("wallet", root.GetProperty("aud").GetString());
        Assert.Equal(Issuer, root.GetProperty("client_id").GetString());
        Assert.Equal("pre-registered", root.GetProperty("client_id_scheme").GetString());
        Assert.Equal("vp_token", root.GetProperty("response_type").GetString());
        Assert.Equal("direct_post", root.GetProperty("response_mode").GetString());
        Assert.Equal("nonce-abc", root.GetProperty("nonce").GetString());
        Assert.Equal("state-xyz", root.GetProperty("state").GetString());
        Assert.True(root.TryGetProperty("iat", out _));
        Assert.True(root.TryGetProperty("exp", out _));
        Assert.True(root.TryGetProperty("presentation_definition", out _));
    }

    [Fact]
    public void Sign_VerifiesAgainstWalletPinnedKey()
    {
        var (signer, publicPem) = CreateSigner();
        var jwt = signer.Sign(SampleAuthReq(), Issuer);

        // Cross-component roundtrip: server-emitted JWT must validate cleanly
        // against the wallet's pure-crypto verifier under the same pinned key.
        var claims = RequestObjectVerifier.VerifyAndParse(jwt, publicPem, Kid, Issuer);

        Assert.Equal(Issuer, claims.ClientId);
        Assert.Equal($"{Issuer}/api/eudiw/response", claims.ResponseUri);
        Assert.Equal("nonce-abc", claims.Nonce);
        Assert.Equal("state-xyz", claims.State);
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
