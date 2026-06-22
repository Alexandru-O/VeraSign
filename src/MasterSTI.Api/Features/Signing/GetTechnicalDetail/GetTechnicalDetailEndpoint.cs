using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Signing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Signing.GetTechnicalDetail;

public static class GetTechnicalDetailEndpoint
{
    public static IEndpointRouteBuilder MapGetTechnicalDetail(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/signing/{id:guid}/technical-detail", async (
            Guid id,
            AppDbContext db,
            IRecipientAccessGuard guard,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            // Look up the SigningRequest's Document first so the access guard can
            // run against it; the guard is per-Document, not per-SigningRequest.
            var docId = await db.SigningRequests.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => (Guid?)s.DocumentId)
                .FirstOrDefaultAsync(cancellationToken);
            if (docId is null)
                return Results.NotFound();

            if (!await guard.CanAccessDocumentAsync(docId.Value, cancellationToken))
                return Results.Forbid();

            var dto = await mediator.Send(new GetTechnicalDetailQuery(id), cancellationToken);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        })
        .WithName("GetTechnicalDetail")
        .WithTags("Signing")
        .Produces<TechnicalDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
