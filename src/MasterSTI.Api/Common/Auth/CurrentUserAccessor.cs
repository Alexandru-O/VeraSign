using System.Security.Claims;

namespace MasterSTI.Api.Common.Auth;

public interface ICurrentUserAccessor
{
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    string? Email { get; }
    string? Name { get; }
    string? Role { get; }
    string DisplayActor { get; }
}

public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserAccessor(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var v = Principal?.FindFirst(JwtTokenService.ClaimUserId)?.Value
                    ?? Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(v, out var id) ? id : null;
        }
    }

    public Guid? OrganizationId
    {
        get
        {
            var v = Principal?.FindFirst(JwtTokenService.ClaimOrganizationId)?.Value;
            return Guid.TryParse(v, out var id) ? id : null;
        }
    }

    public string? Email => Principal?.FindFirst(ClaimTypes.Email)?.Value;
    public string? Name => Principal?.FindFirst(ClaimTypes.Name)?.Value;
    public string? Role => Principal?.FindFirst(ClaimTypes.Role)?.Value;

    public string DisplayActor
    {
        get
        {
            var uid = UserId;
            if (uid is not null) return uid.Value.ToString();
            var name = Name;
            return string.IsNullOrEmpty(name) ? "system" : name;
        }
    }
}
