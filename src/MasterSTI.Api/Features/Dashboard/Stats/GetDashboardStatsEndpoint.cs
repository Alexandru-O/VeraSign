using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Dashboard.Stats;

public static class GetDashboardStatsEndpoint
{
    public static IEndpointRouteBuilder MapGetDashboardStats(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/stats", async (
            string? range,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var dto = await mediator.Send(new GetDashboardStatsQuery(range ?? "30d"), cancellationToken);
            return Results.Ok(dto);
        })
        .WithName("GetDashboardStats")
        .WithTags("Dashboard")
        .Produces<DashboardStatsDto>(StatusCodes.Status200OK);

        return app;
    }
}
