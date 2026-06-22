using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MasterSTI.Api.Common.Auth;

public sealed class ApiKeyOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public bool Required { get; set; }
    public string? Value { get; set; }
}

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyOptions>
{
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Options.Required)
        {
            var anon = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "dev-anonymous") },
                ApiKeyOptions.Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(anon, ApiKeyOptions.Scheme)));
        }

        if (!Request.Headers.TryGetValue(ApiKeyOptions.HeaderName, out var provided) || provided.Count == 0)
            return Task.FromResult(AuthenticateResult.Fail("Missing API key"));

        if (string.IsNullOrEmpty(Options.Value))
            return Task.FromResult(AuthenticateResult.Fail("Server API key not configured"));

        if (!CryptographicEquals(provided.ToString(), Options.Value))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "api-client") },
            ApiKeyOptions.Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, ApiKeyOptions.Scheme)));
    }

    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
