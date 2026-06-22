using MediatR;

namespace MasterSTI.Api.Features.Templates.Delete;

public static class DeleteTemplateEndpoint
{
    public static IEndpointRouteBuilder MapDeleteTemplate(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/templates/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await mediator.Send(new DeleteTemplateCommand(id), cancellationToken);
                return Results.NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
        })
        .WithName("DeleteTemplate")
        .WithTags("Templates")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
