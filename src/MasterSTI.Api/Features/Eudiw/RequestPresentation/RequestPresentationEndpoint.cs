using MediatR;

namespace MasterSTI.Api.Features.Eudiw.RequestPresentation;

public static class RequestPresentationEndpoint
{
    public static IEndpointRouteBuilder MapRequestPresentation(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/eudiw/request/{signingRequestId:guid}", async (
            Guid signingRequestId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await mediator.Send(new RequestPresentationCommand(signingRequestId), cancellationToken);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .WithName("RequestEudiwPresentation")
        .WithTags("EUDIW")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
