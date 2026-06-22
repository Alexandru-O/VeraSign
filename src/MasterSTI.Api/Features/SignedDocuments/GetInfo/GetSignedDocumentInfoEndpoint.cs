using MasterSTI.Shared.DTOs.Documents;
using MediatR;

namespace MasterSTI.Api.Features.SignedDocuments.GetInfo;

public static class GetSignedDocumentInfoEndpoint
{
    public static IEndpointRouteBuilder MapGetSignedDocumentInfo(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/signed-documents/{id:guid}/info", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new GetSignedDocumentInfoQuery(id), cancellationToken);
            return result.Status switch
            {
                GetSignedDocumentInfoStatus.NotFound => Results.NotFound(),
                GetSignedDocumentInfoStatus.Forbidden => Results.Forbid(),
                _ => Results.Ok(result.Info)
            };
        })
        .WithName("GetSignedDocumentInfo")
        .WithTags("SignedDocuments")
        .Produces<SignedDocumentInfoDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
