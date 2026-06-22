using MasterSTI.Shared.DTOs.Templates;
using MediatR;

namespace MasterSTI.Api.Features.Templates.List;

public static class ListTemplatesEndpoint
{
    public static IEndpointRouteBuilder MapListTemplates(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/templates", async (
            string? category,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var rows = await mediator.Send(new ListTemplatesQuery(category), cancellationToken);
                return Results.Ok(rows);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("ListTemplates")
        .WithTags("Templates")
        .Produces<IReadOnlyList<TemplateDto>>(StatusCodes.Status200OK);

        return app;
    }
}
