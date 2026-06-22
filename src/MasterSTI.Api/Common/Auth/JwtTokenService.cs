using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MasterSTI.Api.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MasterSTI.Api.Common.Auth;

public interface IJwtTokenService
{
    (string token, DateTime expiresAt) Issue(User user);
}

public sealed class JwtTokenService : IJwtTokenService
{
    public const string ClaimOrganizationId = "org_id";
    public const string ClaimUserId = "uid";

    private readonly IOptionsMonitor<JwtOptions> _options;

    public JwtTokenService(IOptionsMonitor<JwtOptions> options)
    {
        _options = options;
    }

    public (string token, DateTime expiresAt) Issue(User user)
    {
        var opts = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(opts.Signing.Key) || opts.Signing.Key.Length < 32)
            throw new InvalidOperationException("JWT signing key is missing or shorter than 32 characters. Set 'Jwt:Signing:Key' in dev config or env.");

        var keyBytes = Encoding.UTF8.GetBytes(opts.Signing.Key);
        var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

        var expires = DateTime.UtcNow.AddMinutes(opts.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new(ClaimUserId, user.Id.ToString()),
            new(ClaimOrganizationId, user.OrganizationId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var jwt = new JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: creds);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return (token, expires);
    }
}
