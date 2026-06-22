using MasterSTI.Shared.DTOs.Documents;
using MediatR;

namespace MasterSTI.Api.Features.Documents.Send;

public static class SendDocumentEndpoint
{
    public static IEndpointRouteBuilder MapSendDocument(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/documents/{id:guid}/send", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var response = await mediator.Send(new SendDocumentCommand(id), cancellationToken);
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
        .WithName("SendDocument")
        .WithTags("Documents")
        .Produces<SendDocumentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return app;
    }
}
