using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Dashboard.Diagnostics;

public static class GetDiagnosticsEndpoint
{
    public static IEndpointRouteBuilder MapGetDiagnostics(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/diagnostics", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var dto = await mediator.Send(new GetDiagnosticsQuery(), cancellationToken);
            return Results.Ok(dto);
        })
        .WithName("GetDashboardDiagnostics")
        .WithTags("Dashboard")
        .Produces<DiagnosticsDto>(StatusCodes.Status200OK);

        return app;
    }
}
