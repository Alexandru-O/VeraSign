using MasterSTI.Shared.DTOs.Wallet;
using MediatR;

namespace MasterSTI.Api.Features.Wallet.Inbox;

public static class ListInboxEndpoint
{
    public static IEndpointRouteBuilder MapListInbox(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/wallet/inbox", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ListInboxQuery(), cancellationToken);
            return Results.Ok(response);
        })
        .WithName("ListWalletInbox")
        .WithTags("Wallet")
        .Produces<WalletInboxResponse>(StatusCodes.Status200OK);

        return app;
    }
}
