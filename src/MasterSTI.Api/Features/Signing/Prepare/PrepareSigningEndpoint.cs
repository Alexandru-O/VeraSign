using MasterSTI.Api.Features.Signing.Prepare;
using MediatR;

namespace MasterSTI.Api.Features.Signing.Prepare;

public static class PrepareSigningEndpoint
{
    public static IEndpointRouteBuilder MapPrepareSigning(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/signing/prepare", async (
            PrepareSigningRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                // Render commitment fields are optional. Wallet milestone 1 wires
                // the field set into the request without populating values;
                // milestone 2 starts populating once RenderCommitmentService ships.
                // PrepareSigningValidator enforces all-or-nothing shape and
                // v1-frozen values when any render field is present.
                var renderCommitment = new RenderCommitmentInputs(
                    RenderRootHex: body.RenderRootHex,
                    RenderAlgo: body.RenderAlgo,
                    RenderDpi: body.RenderDpi,
                    RenderPageCount: body.RenderPageCount,
                    RenderLocale: body.RenderLocale,
                    RenderProfile: body.RenderProfile);

                var result = await mediator.Send(
                    new PrepareSigningCommand(
                        body.DocumentId,
                        body.RecipientId,
                        body.RequestedBy ?? "anonymous",
                        body.CredentialId ?? "mock-credential-001",
                        renderCommitment),
                    cancellationToken);

                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("PrepareSigning")
        .WithTags("Signing")
        .Produces<PrepareSigningResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return app;
    }
}

public record PrepareSigningRequest(
    Guid DocumentId,
    Guid RecipientId,
    string? RequestedBy,
    string? CredentialId,
    string? RenderRootHex = null,
    string? RenderAlgo = null,
    int? RenderDpi = null,
    int? RenderPageCount = null,
    string? RenderLocale = null,
    string? RenderProfile = null);
