using MasterSTI.Api.Common.Eudiw;
using MediatR;

namespace MasterSTI.Api.Features.Eudiw.HandleResponse;

public static class HandleVpResponseEndpoint
{
    public static IEndpointRouteBuilder MapHandleVpResponse(this IEndpointRouteBuilder app)
    {
        // Wallet posts VP token here (direct_post response mode)
        app.MapPost("/api/eudiw/response", async (
            VpTokenResponse body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(
                new HandleVpResponseCommand(body.VpToken, body.State),
                cancellationToken);

            if (!result.Success)
            {
                return Results.BadRequest(new { error = result.Error });
            }

            return Results.Ok(new { signingRequestId = result.SigningRequestId });
        })
        .WithName("HandleVpResponse")
        .WithTags("EUDIW")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        return app;
    }
}
