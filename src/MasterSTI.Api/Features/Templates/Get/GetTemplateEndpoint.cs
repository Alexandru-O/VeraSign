using MasterSTI.Shared.DTOs.Templates;
using MediatR;

namespace MasterSTI.Api.Features.Templates.Get;

public static class GetTemplateEndpoint
{
    public static IEndpointRouteBuilder MapGetTemplate(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/templates/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var dto = await mediator.Send(new GetTemplateQuery(id), cancellationToken);
                return Results.Ok(dto);
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
        .WithName("GetTemplate")
        .WithTags("Templates")
        .Produces<TemplateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
