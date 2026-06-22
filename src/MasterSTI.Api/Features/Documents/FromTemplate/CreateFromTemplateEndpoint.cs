using MasterSTI.Shared.DTOs.Documents;
using MediatR;

namespace MasterSTI.Api.Features.Documents.FromTemplate;

public static class CreateFromTemplateEndpoint
{
    public static IEndpointRouteBuilder MapCreateDocumentFromTemplate(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/documents/from-template/{templateId:guid}", async (
            Guid templateId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var response = await mediator.Send(new CreateFromTemplateCommand(templateId), cancellationToken);
                return Results.Created($"/api/documents/{response.DocumentId}", response);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("CreateDocumentFromTemplate")
        .WithTags("Documents")
        .Produces<DocumentFromTemplateResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return app;
    }
}
