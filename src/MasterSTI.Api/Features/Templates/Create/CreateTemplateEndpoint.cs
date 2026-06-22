using FluentValidation;
using MasterSTI.Shared.DTOs.Templates;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MasterSTI.Api.Features.Templates.Create;

public static class CreateTemplateEndpoint
{
    public static IEndpointRouteBuilder MapCreateTemplate(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/templates", async (
            [FromBody] CreateTemplateRequest body,
            IMediator mediator,
            IValidator<CreateTemplateCommand> validator,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateTemplateCommand(
                body.Title,
                body.Description,
                body.Category,
                body.FromDocumentId,
                body.FieldsJson,
                body.DefaultLevel);

            var validationResult = await validator.ValidateAsync(command, cancellationToken);
            if (!validationResult.IsValid)
                return Results.ValidationProblem(validationResult.ToDictionary());

            try
            {
                var dto = await mediator.Send(command, cancellationToken);
                return Results.Created($"/api/templates/{dto.Id}", dto);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("CreateTemplate")
        .WithTags("Templates")
        .Produces<TemplateDto>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        return app;
    }
}
