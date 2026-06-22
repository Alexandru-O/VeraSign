using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Eudiw.Status;

public static class GetEudiwStatusEndpoint
{
    public static IEndpointRouteBuilder MapGetEudiwStatus(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/eudiw/status/{signingRequestId:guid}", async (
            Guid signingRequestId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var sigReq = await db.SigningRequests.FirstOrDefaultAsync(s => s.Id == signingRequestId, cancellationToken);
            if (sigReq is null) return Results.NotFound();

            return Results.Ok(new EudiwStatusResult(
                sigReq.Id,
                sigReq.Status.ToString(),
                sigReq.EudiwSubject));
        })
        .WithName("GetEudiwStatus")
        .WithTags("EUDIW")
        .Produces<EudiwStatusResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
