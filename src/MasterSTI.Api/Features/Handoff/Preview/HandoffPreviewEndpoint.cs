using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Handoff;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Handoff.Preview;

/// <summary>
/// Read-only metadata for the <c>/handoff</c> Razor landing page. The handoff
/// JWT itself is the proof of intent; this endpoint validates the signature
/// + expiry + purpose, then returns enough Document/Recipient data to render
/// the landing UI. No session JWT is minted, no state is mutated — see
/// <c>docs/adr/0003-no-handoff-token-exchange.md</c>.
/// </summary>
public static class HandoffPreviewEndpoint
{
    public static IEndpointRouteBuilder MapHandoffPreview(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/handoff/preview", async (
            HandoffPreviewRequest body,
            IHandoffTokenService tokens,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var claims = tokens.Validate(body.Token);
            if (claims is null)
                return Results.Ok(new HandoffPreviewResponse(
                    Guid.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    "Invalid", "", TokenValid: false, StatusActive: false, ExpiresAt: DateTime.MinValue));

            var data = await db.Recipients.AsNoTracking()
                .Where(r => r.Id == claims.RecipientId && r.DocumentId == claims.DocumentId)
                .Select(r => new
                {
                    r.Id,
                    r.Email,
                    r.Name,
                    r.Order,
                    r.Status,
                    r.Level,
                    DocId = r.Document.Id,
                    DocName = r.Document.FileName,
                    OwnerUserId = r.Document.OwnerUserId
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (data is null)
                return Results.NotFound();

            var senderName = "—";
            if (data.OwnerUserId.HasValue)
            {
                var owner = await db.Users.AsNoTracking()
                    .Where(u => u.Id == data.OwnerUserId.Value)
                    .Select(u => new { u.Name, u.Email })
                    .FirstOrDefaultAsync(cancellationToken);
                if (owner is not null)
                    senderName = string.IsNullOrWhiteSpace(owner.Name) ? owner.Email : owner.Name;
            }

            var statusActive = data.Status == RecipientStatus.Notified;

            return Results.Ok(new HandoffPreviewResponse(
                DocumentId: data.DocId,
                DocumentName: data.DocName,
                SenderName: senderName,
                RecipientName: data.Name,
                RecipientEmail: data.Email,
                RecipientStatus: data.Status.ToString(),
                Level: data.Level,
                TokenValid: true,
                StatusActive: statusActive,
                ExpiresAt: claims.ExpiresAt));
        })
        .AllowAnonymous()
        .WithName("HandoffPreview")
        .WithTags("Handoff")
        .Produces<HandoffPreviewResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
