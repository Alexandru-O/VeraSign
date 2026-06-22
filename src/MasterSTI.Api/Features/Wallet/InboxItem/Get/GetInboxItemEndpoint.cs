using MasterSTI.Shared.DTOs.Wallet;
using MediatR;

namespace MasterSTI.Api.Features.Wallet.InboxItem.Get;

public static class GetInboxItemEndpoint
{
    public static IEndpointRouteBuilder MapGetInboxItem(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/wallet/inbox/{recipientId:guid}", async (
            Guid recipientId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var dto = await mediator.Send(new GetInboxItemQuery(recipientId), cancellationToken);
                return Results.Ok(dto);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
        })
        .WithName("GetWalletInboxItem")
        .WithTags("Wallet")
        .Produces<WalletInboxItemMetaDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status403Forbidden);

        return app;
    }
}
