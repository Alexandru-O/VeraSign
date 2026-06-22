using FluentValidation;
using MasterSTI.Shared.DTOs.Documents;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MasterSTI.Api.Features.Documents.Recipients;

public static class RecipientsEndpoint
{
    public static IEndpointRouteBuilder MapDocumentRecipients(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/documents/{id:guid}/recipients", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var rows = await mediator.Send(new GetRecipientsQuery(id), cancellationToken);
            return Results.Ok(rows);
        })
        .WithName("GetDocumentRecipients")
        .WithTags("Documents")
        .Produces<IReadOnlyList<RecipientDto>>(StatusCodes.Status200OK);

        app.MapPost("/api/documents/{id:guid}/recipients", async (
            Guid id,
            [FromBody] SaveRecipientsRequest body,
            IMediator mediator,
            IValidator<SaveRecipientsCommand> validator,
            CancellationToken cancellationToken) =>
        {
            var command = new SaveRecipientsCommand(id, body.Recipients ?? Array.Empty<RecipientInput>());

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
        .WithName("SaveDocumentRecipients")
        .WithTags("Documents")
        .Produces<IReadOnlyList<RecipientDto>>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        return app;
    }
}
