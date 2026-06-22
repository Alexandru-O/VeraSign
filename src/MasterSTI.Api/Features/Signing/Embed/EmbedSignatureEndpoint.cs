using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Signing.Embed;

public static class EmbedSignatureEndpoint
{
    public static IEndpointRouteBuilder MapEmbedSignature(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/signing/{id:guid}/embed", async (
            Guid id,
            EmbedSignatureRequest body,
            AppDbContext db,
            IRecipientAccessGuard guard,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body?.CmsSignatureBase64))
                return Results.BadRequest(new { error = "cmsSignatureBase64 is required" });

            var documentId = await db.SigningRequests
                .Where(s => s.Id == id)
                .Select(s => (Guid?)s.DocumentId)
                .FirstOrDefaultAsync(cancellationToken);
            if (documentId is null)
                return Results.NotFound(new { error = $"SigningRequest {id} not found" });
            if (!await guard.CanAccessDocumentAsync(documentId.Value, cancellationToken))
                return Results.Forbid();

            try
            {
                var result = await mediator.Send(
                    new EmbedSignatureCommand(id, body.CmsSignatureBase64),
                    cancellationToken);

                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("EmbedSignature")
        .WithTags("Signing")
        .Produces<EmbedSignatureResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);

        return app;
    }
}

public record EmbedSignatureRequest(string CmsSignatureBase64);
