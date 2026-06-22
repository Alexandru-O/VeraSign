using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Dashboard.Upcoming;

public static class GetUpcomingEndpoint
{
    public static IEndpointRouteBuilder MapGetUpcoming(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/upcoming", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var rows = await mediator.Send(new GetUpcomingQuery(), cancellationToken);
            return Results.Ok(rows);
        })
        .WithName("GetDashboardUpcoming")
        .WithTags("Dashboard")
        .Produces<IReadOnlyList<UpcomingItemDto>>(StatusCodes.Status200OK);

        return app;
    }
}
