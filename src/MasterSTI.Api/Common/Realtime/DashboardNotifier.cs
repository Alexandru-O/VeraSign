using Microsoft.AspNetCore.SignalR;

namespace MasterSTI.Api.Common.Realtime;

/// <summary>
/// Pushes a single <c>dashboard-changed</c> ping to every connected client in
/// the org group. Web subscribers refetch <c>/api/dashboard/*</c> in response
/// — no payload, no schema drift between server pushes and HTTP responses.
/// Mutation handlers (Upload/Send/Sign/Embed) call <see cref="NotifyOrgAsync"/>
/// after <c>SaveChanges</c> + cache invalidation so the next read recomputes.
/// </summary>
public interface IDashboardNotifier
{
    Task NotifyOrgAsync(Guid? orgId, CancellationToken cancellationToken = default);
}

public sealed class DashboardNotifier : IDashboardNotifier
{
    private readonly IHubContext<DashboardHub> _hub;
    private readonly ILogger<DashboardNotifier> _logger;

    public DashboardNotifier(IHubContext<DashboardHub> hub, ILogger<DashboardNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyOrgAsync(Guid? orgId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients
                .Group(DashboardHub.GroupFor(orgId))
                .SendAsync("dashboard-changed", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast dashboard-changed for org {OrgId}", orgId);
        }
    }
}
