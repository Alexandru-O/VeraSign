using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Wallet.Status;

public static class GetWalletStatusEndpoint
{
    public static IEndpointRouteBuilder MapGetWalletStatus(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/wallet/status", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var dto = await mediator.Send(new GetWalletStatusQuery(), cancellationToken);
            return Results.Ok(dto);
        })
        .WithName("GetWalletStatus")
        .WithTags("Wallet")
        .Produces<WalletStatusDto>(StatusCodes.Status200OK);

        return app;
    }
}
