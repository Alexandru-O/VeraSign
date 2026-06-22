using FluentValidation;
using MediatR;

namespace MasterSTI.Api.Features.Documents.Upload;

public static class UploadDocumentEndpoint
{
    public static IEndpointRouteBuilder MapUploadDocument(this IEndpointRouteBuilder app)
    {
        var group = app.MapPost("/api/documents/upload", async (
            IFormFile file,
            IMediator mediator,
            IValidator<UploadDocumentCommand> validator,
            CancellationToken cancellationToken) =>
        {
            var command = new UploadDocumentCommand(file);

            var validationResult = await validator.ValidateAsync(command, cancellationToken);
            if (!validationResult.IsValid)
                return Results.ValidationProblem(validationResult.ToDictionary());

            try
            {
                var response = await mediator.Send(command, cancellationToken);
                return Results.Created($"/api/documents/{response.DocumentId}", response);
            }
            catch (InvalidFileFormatException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = new[] { ex.Message }
                });
            }
        })
        .WithName("UploadDocument")
        .WithTags("Documents")
        .RequireRateLimiting("upload")
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<UploadDocumentResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        return app;
    }
}
