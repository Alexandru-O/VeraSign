using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.Download;

public static class DownloadDocumentEndpoint
{
    public static IEndpointRouteBuilder MapDownloadDocument(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/documents/{id:guid}/download", async (
            Guid id,
            AppDbContext db,
            DocumentStorage storage,
            IRecipientAccessGuard guard,
            ICurrentUserAccessor currentUser,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (document is null)
                return Results.NotFound();

            if (!await guard.CanAccessDocumentAsync(id, cancellationToken))
                return Results.Forbid();

            // Signed Document Chain — a Recipient calling this endpoint (non-owner
            // branch of the access guard) should see the chain-head PDF so the
            // browser viewer renders any prior signers' work. The Sender / org
            // path keeps its existing semantics (raw upload bytes).
            var isOwner = currentUser.UserId is Guid uid && document.OwnerUserId == uid;
            if (!isOwner)
            {
                var chainHeadPath = await db.SignedDocuments.AsNoTracking()
                    .Where(sd => sd.OriginalDocumentId == id && sd.IsFinal)
                    .Select(sd => sd.StoragePath)
                    .FirstOrDefaultAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(chainHeadPath) && File.Exists(chainHeadPath))
                {
                    var chainBytes = await File.ReadAllBytesAsync(chainHeadPath, cancellationToken);
                    logger.LogInformation("Document downloaded (chain head): {DocumentId}", id);
                    return Results.File(chainBytes, "application/pdf", document.FileName);
                }
            }

            var bytes = await storage.ReadAsync(document.StoragePath);
            logger.LogInformation("Document downloaded: {DocumentId}", id);

            return Results.File(bytes, "application/pdf", document.FileName);
        })
        .WithName("DownloadDocument")
        .WithTags("Documents")
        .Produces<byte[]>(StatusCodes.Status200OK, "application/pdf")
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
