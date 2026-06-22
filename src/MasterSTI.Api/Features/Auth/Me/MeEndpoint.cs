using MasterSTI.Shared.DTOs.Auth;
using MediatR;

namespace MasterSTI.Api.Features.Auth.Me;

public static class MeEndpoint
{
    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var info = await mediator.Send(new MeQuery(), cancellationToken);
                return Results.Ok(info);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(
                    title: "Unauthorized",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status401Unauthorized);
            }
        })
        .WithName("Me")
        .WithTags("Auth")
        .Produces<UserInfo>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
