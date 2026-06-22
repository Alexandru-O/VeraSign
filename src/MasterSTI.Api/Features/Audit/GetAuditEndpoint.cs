using MasterSTI.Shared.DTOs.Audit;
using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Audit;

public static class GetAuditEndpoint
{
    public static IEndpointRouteBuilder MapGetAudit(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit/{documentId:guid}", async (
            Guid documentId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var rows = await mediator.Send(new GetAuditQuery(documentId), cancellationToken);
            return Results.Ok(rows);
        })
        .WithName("GetAuditTrail")
        .WithTags("Audit")
        .Produces<IReadOnlyList<AuditEventDto>>(StatusCodes.Status200OK);

        app.MapGet("/api/audit", async (
            string? period,
            string? type,
            int? page,
            int? pageSize,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(
                new ListAuditQuery(period, type, page ?? 1, pageSize ?? 50),
                cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ListAuditTrail")
        .WithTags("Audit")
        .Produces<PagedResultDto<AuditEventListItemDto>>(StatusCodes.Status200OK);

        return app;
    }
}
