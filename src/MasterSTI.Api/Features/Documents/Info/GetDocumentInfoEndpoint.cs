using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.Info;

public static class GetDocumentInfoEndpoint
{
    public static IEndpointRouteBuilder MapGetDocumentInfo(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/documents/{id:guid}/info", async (
            Guid id,
            AppDbContext db,
            IRecipientAccessGuard guard,
            CancellationToken cancellationToken) =>
        {
            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (doc is null) return Results.NotFound();

            if (!await guard.CanAccessDocumentAsync(id, cancellationToken))
                return Results.Forbid();

            return Results.Ok(new DocumentDto(
                doc.Id,
                doc.FileName,
                doc.ContentType,
                doc.Sha256Hash,
                doc.UploadedAt,
                doc.Status.ToString()));
        })
        .WithName("GetDocumentInfo")
        .WithTags("Documents")
        .Produces<DocumentDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
