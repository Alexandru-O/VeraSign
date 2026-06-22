using MasterSTI.Shared.DTOs.Wallet;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace MasterSTI.Api.Features.Wallet.Auth;

public static class WalletAuthEndpoint
{
    public static IEndpointRouteBuilder MapWalletAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/wallet/auth", async (
            WalletAuthInitRequest? body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var purpose = body?.Purpose ?? WalletAuthPurpose.Sign;
            var response = await mediator.Send(new InitiateWalletAuthCommand(purpose), cancellationToken);
            return Results.Ok(response);
        })
        .AllowAnonymous()
        .WithName("InitiateWalletAuth")
        .WithTags("Wallet")
        .Produces<InitiateWalletAuthResponse>(StatusCodes.Status200OK);

        app.MapGet("/api/wallet/auth/{state}", (
            string state,
            IMemoryCache cache) =>
        {
            if (cache.TryGetValue(WalletAuthCacheKeys.ForState(state), out WalletAuthEntry? entry) && entry is not null)
            {
                if (DateTime.UtcNow > entry.ExpiresAtUtc)
                    return Results.Ok(new WalletAuthStatusResponse("expired", null, null));

                return Results.Ok(new WalletAuthStatusResponse(entry.Status, entry.Subject, entry.CompletedAtUtc, entry.Login));
            }

            // Live entry was evicted on Login success for replay protection.
            // Fall through to the short-lived completion marker so the polling
            // client can still pick up the LoginResponse.
            if (cache.TryGetValue(WalletAuthCacheKeys.ForCompletion(state), out WalletAuthEntry? completed) && completed is not null)
            {
                return Results.Ok(new WalletAuthStatusResponse(completed.Status, completed.Subject, completed.CompletedAtUtc, completed.Login));
            }

            return Results.Ok(new WalletAuthStatusResponse("expired", null, null));
        })
        .AllowAnonymous()
        .WithName("GetWalletAuthStatus")
        .WithTags("Wallet")
        .Produces<WalletAuthStatusResponse>(StatusCodes.Status200OK);

        return app;
    }
}
