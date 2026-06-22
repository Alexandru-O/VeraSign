using MasterSTI.Api.Common.Csc;
using Microsoft.Extensions.Options;

namespace MasterSTI.Api.Features.Credentials.List;

public static class ListCredentialsEndpoint
{
    public static IEndpointRouteBuilder MapListCredentials(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/credentials/list", async (
            ICscApiClient csc,
            IOptionsMonitor<CscApiOptions> options,
            CancellationToken cancellationToken) =>
        {
            var opts = options.CurrentValue;
            if (string.IsNullOrWhiteSpace(opts.Username) || string.IsNullOrWhiteSpace(opts.Password))
                return Results.Problem("CSC credentials are not configured.", statusCode: 500);

            var token = await csc.AuthLoginAsync(opts.Username, opts.Password, cancellationToken);
            var credentials = await csc.ListCredentialsAsync(token, cancellationToken);
            return Results.Ok(new { credentialIDs = credentials });
        })
        .WithName("ListCredentials")
        .WithTags("Credentials")
        .Produces(StatusCodes.Status200OK);

        return app;
    }
}
