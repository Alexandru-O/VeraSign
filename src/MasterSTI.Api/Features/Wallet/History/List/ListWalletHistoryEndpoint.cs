using MasterSTI.Shared.DTOs.Wallet;
using MediatR;

namespace MasterSTI.Api.Features.Wallet.History.List;

public static class ListWalletHistoryEndpoint
{
    public static IEndpointRouteBuilder MapListWalletHistory(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/wallet/history", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(new ListWalletHistoryQuery(), cancellationToken);
            return Results.Ok(items);
        })
        .WithName("ListWalletHistory")
        .WithTags("Wallet")
        .Produces<List<WalletHistoryItemDto>>(StatusCodes.Status200OK);

        return app;
    }
}
