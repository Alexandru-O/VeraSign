using FluentValidation;
using MasterSTI.Shared.DTOs.Documents;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MasterSTI.Api.Features.Documents.Fields;

public static class FieldsEndpoint
{
    public static IEndpointRouteBuilder MapDocumentFields(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/documents/{id:guid}/fields", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var rows = await mediator.Send(new GetFieldsQuery(id), cancellationToken);
            return Results.Ok(rows);
        })
        .WithName("GetDocumentFields")
        .WithTags("Documents")
        .Produces<IReadOnlyList<SignatureFieldDto>>(StatusCodes.Status200OK);

        app.MapPatch("/api/documents/{id:guid}/fields", async (
            Guid id,
            [FromBody] SaveFieldsRequest body,
            IMediator mediator,
            IValidator<SaveFieldsCommand> validator,
            CancellationToken cancellationToken) =>
        {
            var command = new SaveFieldsCommand(id, body.Fields ?? Array.Empty<SignatureFieldDto>());

            var validationResult = await validator.ValidateAsync(command, cancellationToken);
            if (!validationResult.IsValid)
                return Results.ValidationProblem(validationResult.ToDictionary());

            try
            {
                var saved = await mediator.Send(command, cancellationToken);
                return Results.Ok(saved);
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
        .WithName("SaveDocumentFields")
        .WithTags("Documents")
        .Produces<IReadOnlyList<SignatureFieldDto>>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        return app;
    }
}
