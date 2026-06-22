using FluentValidation;
using MasterSTI.Shared.DTOs.Templates;
using MediatR;

namespace MasterSTI.Api.Features.Templates.ReplacePdf;

public static class ReplaceTemplatePdfEndpoint
{
    public static IEndpointRouteBuilder MapReplaceTemplatePdf(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/templates/{id:guid}/replace-pdf", async (
            Guid id,
            IFormFile file,
            IMediator mediator,
            IValidator<ReplaceTemplatePdfCommand> validator,
            CancellationToken cancellationToken) =>
        {
            var command = new ReplaceTemplatePdfCommand(id, file);

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
            catch (InvalidTemplatePdfException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = new[] { ex.Message }
                });
            }
        })
        .WithName("ReplaceTemplatePdf")
        .WithTags("Templates")
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<TemplateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesValidationProblem();

        return app;
    }
}
