using MediatR;

namespace MasterSTI.Api.Features.Documents.Delete;

public static class DeleteDocumentEndpoint
{
    public static IEndpointRouteBuilder MapDeleteDocument(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/documents/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("DeleteDocumentEndpoint");
            try
            {
                var response = await mediator.Send(new DeleteDocumentCommand(id), cancellationToken);
                return Results.Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "Delete blocked for document {DocumentId}: {Reason}", id, ex.Message);
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("DeleteDocument")
        .WithTags("Documents")
        .Produces<DeleteDocumentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return app;
    }
}
