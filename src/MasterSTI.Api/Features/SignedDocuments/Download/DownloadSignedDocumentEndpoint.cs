using MasterSTI.Api.Common;
using MasterSTI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.SignedDocuments.Download;

public static class DownloadSignedDocumentEndpoint
{
    public static IEndpointRouteBuilder MapDownloadSignedDocument(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/signed-documents/{id:guid}/download", async (
            Guid id,
            AppDbContext db,
            DocumentStorage storage,
            CancellationToken cancellationToken) =>
        {
            var signedDoc = await db.SignedDocuments
                .Include(s => s.OriginalDocument)
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (signedDoc is null) return Results.NotFound();

            var fullPath = storage.ResolveSigned(signedDoc.StoragePath);
            if (!File.Exists(fullPath)) return Results.NotFound();

            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            var fileName = $"signed_{signedDoc.OriginalDocument.FileName}";

            return Results.File(bytes, "application/pdf", fileName);
        })
        .WithName("DownloadSignedDocument")
        .WithTags("SignedDocuments")
        .Produces<byte[]>(StatusCodes.Status200OK, "application/pdf")
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
