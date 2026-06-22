using MediatR;

namespace MasterSTI.Api.Features.Documents.Cancel;

public sealed record CancelDocumentRequest(string? Reason);

public static class CancelDocumentEndpoint
{
    public static IEndpointRouteBuilder MapCancelDocument(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/documents/{id:guid}/cancel", async (
            Guid id,
            CancelDocumentRequest? body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var response = await mediator.Send(
                    new CancelDocumentCommand(id, body?.Reason),
                    cancellationToken);
                return Results.Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("CancelDocument")
        .WithTags("Documents")
        .Produces<CancelDocumentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return app;
    }
}
