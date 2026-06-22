using MediatR;

namespace MasterSTI.Api.Features.Documents.Remind;

public static class RemindDocumentEndpoint
{
    public static IEndpointRouteBuilder MapRemindDocument(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/documents/{id:guid}/remind", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var response = await mediator.Send(new RemindDocumentCommand(id), cancellationToken);
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
        .WithName("RemindDocument")
        .WithTags("Documents")
        .Produces<RemindDocumentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return app;
    }
}
