using MasterSTI.Api.Common.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MasterSTI.Api.Common.Realtime;

/// <summary>
/// Real-time fan-out for Dashboard widgets. Clients connect with their JWT
/// (Authorization header on HTTP negotiate, <c>?access_token=</c> on the
/// WebSocket upgrade — see <c>JwtBearer.OnMessageReceived</c>). On connect
/// the hub auto-joins the caller to <c>dashboard:org:{orgId}</c>; mutation
/// handlers fan out to that group via <see cref="IDashboardNotifier"/>.
/// </summary>
[Authorize]
public sealed class DashboardHub : Hub
{
    public static string GroupFor(Guid? orgId) =>
        orgId is null ? "dashboard:org:global" : $"dashboard:org:{orgId}";

    public override async Task OnConnectedAsync()
    {
        var orgId = ResolveOrgId();
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(orgId));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var orgId = ResolveOrgId();
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(orgId));
        await base.OnDisconnectedAsync(exception);
    }

    private Guid? ResolveOrgId()
    {
        var raw = Context.User?.FindFirst(JwtTokenService.ClaimOrganizationId)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
