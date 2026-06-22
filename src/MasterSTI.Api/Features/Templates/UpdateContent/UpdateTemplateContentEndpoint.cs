using FluentValidation;
using MasterSTI.Shared.DTOs.Templates;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MasterSTI.Api.Features.Templates.UpdateContent;

public static class UpdateTemplateContentEndpoint
{
    public static IEndpointRouteBuilder MapUpdateTemplateContent(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/templates/{id:guid}/content", async (
            Guid id,
            [FromBody] UpdateTemplateContentRequest body,
            IMediator mediator,
            IValidator<UpdateTemplateContentCommand> validator,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateTemplateContentCommand(id, body.BodyMarkdown ?? string.Empty);

            var validationResult = await validator.ValidateAsync(command, cancellationToken);
            if (!validationResult.IsValid)
                return Results.ValidationProblem(validationResult.ToDictionary());

            try
            {
                var dto = await mediator.Send(command, cancellationToken);
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
        .WithName("UpdateTemplateContent")
        .WithTags("Templates")
        .Produces<TemplateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesValidationProblem();

        return app;
    }
}
