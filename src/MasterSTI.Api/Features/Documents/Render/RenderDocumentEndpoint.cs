using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.Render;

public static class RenderDocumentEndpoint
{
    public static IEndpointRouteBuilder MapRenderDocument(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/documents/{id:guid}/render", async (
            Guid id,
            AppDbContext db,
            DocumentStorage storage,
            IRecipientAccessGuard guard,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (document is null)
                return Results.NotFound();

            if (!await guard.CanAccessDocumentAsync(id, cancellationToken))
                return Results.Forbid();

            var bytes = await storage.ReadAsync(document.StoragePath);
            logger.LogInformation("Document rendered: {DocumentId}", id);

            // Inline display (WYSIWYS use case)
            return Results.File(bytes, "application/pdf");
        })
        .WithName("RenderDocument")
        .WithTags("Documents")
        .Produces<byte[]>(StatusCodes.Status200OK, "application/pdf")
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
