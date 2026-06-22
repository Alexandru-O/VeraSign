using MediatR;

namespace MasterSTI.Api.Features.Signing.Status;

public static class GetSigningStatusEndpoint
{
    public static IEndpointRouteBuilder MapGetSigningStatus(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/signing/{id:guid}/status", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new GetSigningStatusQuery(id), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetSigningStatus")
        .WithTags("Signing")
        .Produces<SigningStatusResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
