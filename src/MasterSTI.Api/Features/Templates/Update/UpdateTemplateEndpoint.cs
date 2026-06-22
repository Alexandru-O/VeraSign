using FluentValidation;
using MasterSTI.Shared.DTOs.Templates;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MasterSTI.Api.Features.Templates.Update;

public static class UpdateTemplateEndpoint
{
    public static IEndpointRouteBuilder MapUpdateTemplate(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/templates/{id:guid}", async (
            Guid id,
            [FromBody] UpdateTemplateRequest body,
            IMediator mediator,
            IValidator<UpdateTemplateCommand> validator,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateTemplateCommand(
                id,
                body.Title,
                body.Description,
                body.Category,
                body.FieldsJson,
                body.DefaultLevel);

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
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("UpdateTemplate")
        .WithTags("Templates")
        .Produces<TemplateDto>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        return app;
    }
}
