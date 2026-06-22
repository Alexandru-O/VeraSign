using MasterSTI.Api.Common.Csc;
using Microsoft.Extensions.Options;

namespace MasterSTI.Api.Features.Credentials.Info;

public static class GetCredentialInfoEndpoint
{
    public static IEndpointRouteBuilder MapGetCredentialInfo(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/credentials/{credentialId}/info", async (
            string credentialId,
            ICscApiClient csc,
            IOptionsMonitor<CscApiOptions> options,
            CancellationToken cancellationToken) =>
        {
            var opts = options.CurrentValue;
            if (string.IsNullOrWhiteSpace(opts.Username) || string.IsNullOrWhiteSpace(opts.Password))
                return Results.Problem("CSC credentials are not configured.", statusCode: 500);

            var token = await csc.AuthLoginAsync(opts.Username, opts.Password, cancellationToken);
            var info = await csc.GetCredentialInfoAsync(token, credentialId, cancellationToken);
            return Results.Ok(info);
        })
        .WithName("GetCredentialInfo")
        .WithTags("Credentials")
        .Produces(StatusCodes.Status200OK);

        return app;
    }
}
