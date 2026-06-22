using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.Detail;

public static class GetDocumentDetailEndpoint
{
    public static IEndpointRouteBuilder MapGetDocumentDetail(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/documents/{id:guid}/detail", async (
            Guid id,
            IMediator mediator,
            AppDbContext db,
            IRecipientAccessGuard guard,
            CancellationToken cancellationToken) =>
        {
            var exists = await db.Documents
                .AsNoTracking()
                .AnyAsync(d => d.Id == id, cancellationToken);
            if (!exists) return Results.NotFound();

            if (!await guard.CanAccessDocumentAsync(id, cancellationToken))
                return Results.Forbid();

            var dto = await mediator.Send(new GetDocumentDetailQuery(id), cancellationToken);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        })
        .WithName("GetDocumentDetail")
        .WithTags("Documents")
        .Produces<DocumentDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
