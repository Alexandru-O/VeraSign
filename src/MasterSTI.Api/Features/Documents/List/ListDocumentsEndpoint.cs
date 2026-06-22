using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Documents.List;

public static class ListDocumentsEndpoint
{
    public static IEndpointRouteBuilder MapListDocuments(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/documents", async (
            string? status,
            string? q,
            string? level,
            string? period,
            int? page,
            int? pageSize,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var dto = await mediator.Send(
                new ListDocumentsQuery(status, q, level, period, page ?? 1, pageSize ?? 20),
                cancellationToken);
            return Results.Ok(dto);
        })
        .WithName("ListDocuments")
        .WithTags("Documents")
        .Produces<PagedResultDto<DocumentListItemDto>>(StatusCodes.Status200OK);

        return app;
    }
}
