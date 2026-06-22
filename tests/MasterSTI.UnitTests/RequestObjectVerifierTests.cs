using System.Security.Cryptography;
using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Wallet.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MasterSTI.UnitTests;

/// <summary>
/// ADR-0011 wallet-side: verifier rejects every tamper / mismatch case
/// before exposing payload fields to the caller.
/// </summary>
public class RequestObjectVerifierTests
{
    private const string Kid = "verasign-rqo-v1";
    private const string Issuer = "https://localhost:7001";

    private static (string jwt, string publicPem, RequestObjectSigner signer) BuildSignedJwt(
        int expiresInSeconds = 300,
        string issuer = Issuer,
        string kid = Kid)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privatePem = ec.ExportPkcs8PrivateKeyPem();
        var publicPem = ec.ExportSubjectPublicKeyInfoPem();
        var signer = new RequestObjectSigner(
            Options.Create(new RequestObjectSigningOptions
            {
                PrivateKeyPem = privatePem,
                Kid = kid,
                ExpiresInSeconds = expiresInSeconds,
            }),
            NullLogger<RequestObjectSigner>.Instance);

        var authReq = new AuthorizationRequest(
            ClientId: issuer,
            ResponseType: "vp_token",
            ResponseMode: "direct_post",
            ResponseUri: $"{issuer}/api/eudiw/response",
            Nonce: "nonce-abc",
            State: "state-xyz",
            PresentationDefinition: PresentationDefinition.BuildPid());
        return (signer.Sign(authReq, issuer), publicPem, signer);
    }

    [Fact]
    public void Valid_Roundtrip_ReturnsClaims()
    {
        var (jwt, pem, _) = BuildSignedJwt();
        var claims = RequestObjectVerifier.VerifyAndParse(jwt, pem, Kid, Issuer);
        Assert.Equal(Issuer, claims.ClientId);
    }

    [Fact]
    public void Rejects_Empty()
    {
        Assert.Throws<RequestObjectVerificationException>(() =>
            RequestObjectVerifier.VerifyAndParse("", "pem", Kid, Issuer));
    }

    [Fact]
    public void Rejects_NotThreeParts()
    {
        Assert.Throws<RequestObjectVerificationException>(() =>
            RequestObjectVerifier.VerifyAndParse("only.two", "pem", Kid, Issuer));
    }

    [Fact]
    public void Rejects_TamperedPayload()
    {
        var (jwt, pem, _) = BuildSignedJwt();
        var parts = jwt.Split('.');

        // Re-encode payload with a flipped nonce — signature stays bound to the
        // original payload so ECDSA verification must fail.
        var payloadJson = System.Text.Encoding.UTF8.GetString(B64UrlDecode(parts[1]));
        var tampered = payloadJson.Replace("\"nonce-abc\"", "\"nonce-evil\"");
        var tamperedB64 = B64UrlEncode(System.Text.Encoding.UTF8.GetBytes(tampered));
        var tamperedJwt = $"{parts[0]}.{tamperedB64}.{parts[2]}";

        var ex = Assert.Throws<RequestObjectVerificationException>(() =>
            RequestObjectVerifier.VerifyAndParse(tamperedJwt, pem, Kid, Issuer));
        Assert.Contains("Signature", ex.Message);
    }

    [Fact]
    public void Rejects_WrongKey()
    {
        var (jwt, _, _) = BuildSignedJwt();
        // Pin a completely different key — signature verification must fail.
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wrongPem = otherKey.ExportSubjectPublicKeyInfoPem();
        Assert.Throws<RequestObjectVerificationException>(() =>
            RequestObjectVerifier.VerifyAndParse(jwt, wrongPem, Kid, Issuer));
    }

    [Fact]
    public void Rejects_KidMismatch()
    {
        var (jwt, pem, _) = BuildSignedJwt(kid: "rogue-kid");
        var ex = Assert.Throws<RequestObjectVerificationException>(() =>
            RequestObjectVerifier.VerifyAndParse(jwt, pem, Kid, Issuer));
        Assert.Contains("kid", ex.Message);
    }

    [Fact]
    public void Rejects_ClientIdMismatch()
    {
        var (jwt, pem, _) = BuildSignedJwt(issuer: "https://attacker.example");
        var ex = Assert.Throws<RequestObjectVerificationException>(() =>
            RequestObjectVerifier.VerifyAndParse(jwt, pem, Kid, Issuer));
        Assert.Contains("iss", ex.Message);
    }

    [Fact]
    public void Rejects_Expired()
    {
        var (jwt, pem, _) = BuildSignedJwt(expiresInSeconds: 1);
        // Force the wallet clock 10 minutes into the future — token's exp must
        // have elapsed beyond the default 60 s skew window.
        var future = DateTimeOffset.UtcNow.AddMinutes(10);
        var ex = Assert.Throws<RequestObjectVerificationException>(() =>
            RequestObjectVerifier.VerifyAndParse(jwt, pem, Kid, Issuer, utcNow: future));
        Assert.Contains("exp", ex.Message);
    }

    [Fact]
    public void Rejects_AlgNone_DoesNotBypass()
    {
        // Build a malicious token with alg=none and no signature; verifier must
        // refuse before doing any ECDSA work.
        var headerB64 = B64UrlEncode(System.Text.Encoding.UTF8.GetBytes(
            "{\"alg\":\"none\",\"typ\":\"oauth-authz-req+jwt\",\"kid\":\"verasign-rqo-v1\"}"));
        var payloadB64 = B64UrlEncode(System.Text.Encoding.UTF8.GetBytes("{\"iss\":\"x\"}"));
        var jwt = $"{headerB64}.{payloadB64}.";
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = ec.ExportSubjectPublicKeyInfoPem();
        var ex = Assert.Throws<RequestObjectVerificationException>(() =>
            RequestObjectVerifier.VerifyAndParse(jwt, pem, Kid, Issuer));
        Assert.Contains("alg", ex.Message);
    }

    private static byte[] B64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }

    private static string B64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
