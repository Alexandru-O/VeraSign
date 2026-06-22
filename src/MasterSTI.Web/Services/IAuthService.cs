using System.Net.Http.Json;
using MasterSTI.Shared.DTOs.Auth;

namespace MasterSTI.Web.Services;

public interface IAuthService
{
    bool IsAuthenticated { get; }
    string? UserDisplayName { get; }
    string? UserEmail { get; }
    string? UserInitials { get; }
    string? Organization { get; }
    string? Token { get; }

    Task<bool> SignInAsync(string email, string password, CancellationToken ct = default);
    Task<bool> SignInWithWalletAsync(string token, UserInfo user, CancellationToken ct = default);
    void SignOut();
}

public sealed class ApiAuthService(IHttpClientFactory httpFactory, ILogger<ApiAuthService> logger) : IAuthService
{
    private bool _authenticated;
    private string? _displayName;
    private string? _email;
    private string? _initials;
    private string? _organization;
    private string? _token;

    public bool IsAuthenticated => _authenticated;
    public string? UserDisplayName => _displayName;
    public string? UserEmail => _email;
    public string? UserInitials => _initials;
    public string? Organization => _organization;
    public string? Token => _token;

    public async Task<bool> SignInAsync(string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return false;

        try
        {
            var client = httpFactory.CreateClient("MasterSTI.Api");
            var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password), ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Login failed for {Email}: {Status}", email, resp.StatusCode);
                return false;
            }

            var body = await resp.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
            if (body is null) return false;

            AdoptSession(body.Token, body.User);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login API call failed");
            return false;
        }
    }

    public Task<bool> SignInWithWalletAsync(string token, UserInfo user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult(false);

        AdoptSession(token, user);
        return Task.FromResult(true);
    }

    public void SignOut()
    {
        _authenticated = false;
        _displayName = null;
        _email = null;
        _initials = null;
        _organization = null;
        _token = null;
    }

    private void AdoptSession(string token, UserInfo user)
    {
        _authenticated = true;
        _token = token;
        _displayName = user.Name;
        _email = user.Email;
        _initials = BuildInitials(user.Name);
        _organization = user.OrganizationName;
    }

    private static string BuildInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant()
        };
    }
}
