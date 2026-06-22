using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Auth;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Auth.Login;

public sealed class LoginHandler : IRequestHandler<LoginCommand, LoginResponse>
{
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _tokens;
    private readonly ILogger<LoginHandler> _logger;
    private readonly PasswordHasher<User> _hasher = new();

    public LoginHandler(AppDbContext db, IJwtTokenService tokens, ILogger<LoginHandler> logger)
    {
        _db = db;
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await _db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email, cancellationToken);

        if (user is null)
        {
            _logger.LogInformation("Login failed: unknown email");
            throw new InvalidCredentialsException();
        }

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verify == PasswordVerificationResult.Failed)
        {
            _logger.LogInformation("Login failed for user {UserId}: bad password", user.Id);
            throw new InvalidCredentialsException();
        }

        var (token, expiresAt) = _tokens.Issue(user);

        _logger.LogInformation("Login success for user {UserId}", user.Id);

        return new LoginResponse(
            token,
            expiresAt,
            new UserInfo(
                user.Id,
                user.Email,
                user.Name,
                user.OrganizationId,
                user.Organization?.Name ?? string.Empty,
                user.Role));
    }
}

public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException() : base("Invalid email or password.") { }
}
