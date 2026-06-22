using MasterSTI.Api.Common.Auth;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MasterSTI.UnitTests;

public class HandoffTokenServiceTests
{
    private const string TestKey = "phase-2-handoff-key-at-least-32-chars-long";

    private static IOptionsMonitor<JwtOptions> Monitor(string key = TestKey, string issuer = "test-iss", string audience = "test-aud")
    {
        var opts = new JwtOptions
        {
            Issuer = issuer,
            Audience = audience,
            Signing = new JwtSigningOptions { Key = key }
        };
        var monitor = Substitute.For<IOptionsMonitor<JwtOptions>>();
        monitor.CurrentValue.Returns(opts);
        return monitor;
    }

    [Fact]
    public void IssueAndValidate_RoundTripsClaims()
    {
        var svc = new HandoffTokenService(Monitor());
        var recipientId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var token = svc.Issue(recipientId, documentId);
        var claims = svc.Validate(token);

        Assert.NotNull(claims);
        Assert.Equal(recipientId, claims!.RecipientId);
        Assert.Equal(documentId, claims.DocumentId);
        Assert.True(claims.ExpiresAt > DateTime.UtcNow.AddDays(29));
    }

    [Fact]
    public void Validate_TamperedToken_ReturnsNull()
    {
        var svc = new HandoffTokenService(Monitor());
        var token = svc.Issue(Guid.NewGuid(), Guid.NewGuid());

        // Flip the last character of the signature segment.
        var lastChar = token[^1];
        var swap = lastChar == 'A' ? 'B' : 'A';
        var tampered = token[..^1] + swap;

        Assert.Null(svc.Validate(tampered));
    }

    [Fact]
    public void Validate_WrongIssuer_ReturnsNull()
    {
        var issuerA = new HandoffTokenService(Monitor(issuer: "issuer-a"));
        var token = issuerA.Issue(Guid.NewGuid(), Guid.NewGuid());

        var issuerB = new HandoffTokenService(Monitor(issuer: "issuer-b"));
        Assert.Null(issuerB.Validate(token));
    }

    [Fact]
    public void Validate_GarbageInput_ReturnsNull()
    {
        var svc = new HandoffTokenService(Monitor());
        Assert.Null(svc.Validate("not.a.jwt"));
        Assert.Null(svc.Validate(""));
        Assert.Null(svc.Validate("   "));
    }

    [Fact]
    public void Validate_TokenWithoutPurpose_ReturnsNull()
    {
        // Sign a JWT with the same key but no purpose claim using the lower-level handler.
        var opts = new JwtOptions
        {
            Issuer = "test-iss",
            Audience = "test-aud",
            Signing = new JwtSigningOptions { Key = TestKey }
        };
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(opts.Signing.Key);
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: new[]
            {
                new System.Security.Claims.Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new System.Security.Claims.Claim("doc", Guid.NewGuid().ToString())
            },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt);

        var svc = new HandoffTokenService(Monitor());
        Assert.Null(svc.Validate(token));
    }
}
