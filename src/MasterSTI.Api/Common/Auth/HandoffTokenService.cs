using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MasterSTI.Api.Common.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MasterSTI.Api.Common.Auth;

public interface IHandoffTokenService
{
    string Issue(Guid recipientId, Guid documentId);

    /// <summary>
    /// Validates a handoff JWT (signature, exp, purpose). Returns the claims on
    /// success, <c>null</c> on any validation failure. Does not consult
    /// <c>Recipient.Status</c> — callers must gate on Status themselves
    /// (see <c>docs/adr/0002-handoff-token-status-gate.md</c>).
    /// </summary>
    HandoffClaims? Validate(string token);
}

public sealed record HandoffClaims(Guid RecipientId, Guid DocumentId, DateTime ExpiresAt);

public sealed class HandoffTokenService : IHandoffTokenService
{
    public const string PurposeClaim = "purpose";
    public const string PurposeValue = "handoff";
    public const string DocumentClaim = "doc";

    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(30);

    private readonly IOptionsMonitor<JwtOptions> _options;

    public HandoffTokenService(IOptionsMonitor<JwtOptions> options) => _options = options;

    public string Issue(Guid recipientId, Guid documentId)
    {
        var opts = _options.CurrentValue;
        var key = RequireSigningKey(opts);

        var creds = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, recipientId.ToString()),
                new Claim(DocumentClaim, documentId.ToString()),
                new Claim(PurposeClaim, PurposeValue),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            },
            notBefore: now,
            expires: now.Add(DefaultLifetime),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public HandoffClaims? Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var opts = _options.CurrentValue;
        byte[] key;
        try { key = RequireSigningKey(opts); }
        catch (InvalidOperationException) { return null; }

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = opts.Issuer,
            ValidateAudience = true,
            ValidAudience = opts.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, parameters, out var validated);

            var purpose = principal.FindFirst(PurposeClaim)?.Value;
            if (!string.Equals(purpose, PurposeValue, StringComparison.Ordinal))
                return null;

            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var doc = principal.FindFirst(DocumentClaim)?.Value;
            if (!Guid.TryParse(sub, out var recipientId)) return null;
            if (!Guid.TryParse(doc, out var documentId)) return null;

            return new HandoffClaims(recipientId, documentId, validated.ValidTo);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] RequireSigningKey(JwtOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.Signing.Key) || opts.Signing.Key.Length < 32)
            throw new InvalidOperationException(
                "JWT signing key is missing or shorter than 32 characters. Set 'Jwt:Signing:Key'.");
        return Encoding.UTF8.GetBytes(opts.Signing.Key);
    }
}
