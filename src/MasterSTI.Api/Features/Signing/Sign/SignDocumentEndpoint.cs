using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Signing.Sign;

public static class SignDocumentEndpoint
{
    public static IEndpointRouteBuilder MapSignDocument(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/signing/{id:guid}/sign", async (
            Guid id,
            SignDocumentRequest body,
            AppDbContext db,
            IRecipientAccessGuard guard,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            // Per-document authz: only sender or currently-Notified Recipient
            // for the underlying Document may invoke /sign. Without this any
            // authenticated user could drive someone else's signing request.
            var documentId = await db.SigningRequests
                .Where(s => s.Id == id)
                .Select(s => (Guid?)s.DocumentId)
                .FirstOrDefaultAsync(cancellationToken);
            if (documentId is null)
                return Results.NotFound(new { error = $"SigningRequest {id} not found" });
            if (!await guard.CanAccessDocumentAsync(documentId.Value, cancellationToken))
                return Results.Forbid();

            try
            {
                // Factor: "biometric" when the wallet authorised via fingerprint,
                // "pin" otherwise. Optional and defaults to "pin" so legacy callers
                // (e.g. /signing/{id}/credentials Razor flow) keep working.
                var factor = string.IsNullOrWhiteSpace(body.Factor) ? "pin" : body.Factor;

                var result = await mediator.Send(
                    new SignDocumentCommand(id, body.Pin ?? string.Empty, factor),
                    cancellationToken);

                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("SignDocument")
        .WithTags("Signing")
        .Produces<SignDocumentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);

        return app;
    }
}

public record SignDocumentRequest(string? Pin, string? Factor = null);
