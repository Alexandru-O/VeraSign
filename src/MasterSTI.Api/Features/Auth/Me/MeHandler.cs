using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Auth.Me;

public sealed class MeHandler : IRequestHandler<MeQuery, UserInfo>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public MeHandler(AppDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task<UserInfo> Handle(MeQuery request, CancellationToken cancellationToken)
    {
        var userId = _user.UserId
            ?? throw new UnauthorizedAccessException("No authenticated user.");

        var user = await _db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("User not found.");

        return new UserInfo(
            user.Id,
            user.Email,
            user.Name,
            user.OrganizationId,
            user.Organization?.Name ?? string.Empty,
            user.Role);
    }
}
