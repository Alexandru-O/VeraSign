using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Dashboard.PipelineStats;

public static class GetPipelineStatsEndpoint
{
    public static IEndpointRouteBuilder MapGetPipelineStats(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/pipeline-stats", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var dto = await mediator.Send(new GetPipelineStatsQuery(), cancellationToken);
            return Results.Ok(dto);
        })
        .WithName("GetPipelineStats")
        .WithTags("Dashboard")
        .Produces<PipelineStatsDto>(StatusCodes.Status200OK);

        return app;
    }
}
