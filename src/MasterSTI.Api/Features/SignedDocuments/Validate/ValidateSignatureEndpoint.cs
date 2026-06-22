using MediatR;

namespace MasterSTI.Api.Features.SignedDocuments.Validate;

public static class ValidateSignatureEndpoint
{
    public static IEndpointRouteBuilder MapValidateSignature(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/signed-documents/{id:guid}/validate", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new ValidateSignatureQuery(id), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ValidateSignature")
        .WithTags("SignedDocuments")
        .Produces<ValidationReportResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
