namespace MasterSTI.Shared.DTOs.Auth;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, DateTime ExpiresAt, UserInfo User);

public record UserInfo(
    Guid Id,
    string Email,
    string Name,
    Guid OrganizationId,
    string OrganizationName,
    string Role);
