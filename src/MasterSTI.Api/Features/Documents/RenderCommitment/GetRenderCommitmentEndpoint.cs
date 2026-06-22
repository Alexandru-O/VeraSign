using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Rendering;
using MasterSTI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.RenderCommitment;

/// <summary>
/// POST /api/documents/{id}/render-commitment — wallet asks the server to
/// compute the Pixel-Bound QES Render Commitment over the document bytes,
/// using the pinned PDFium binary as the bit-identity authority
/// (ADR-0008 §"Cross-toolchain caveat"). Wallet then forwards the result on
/// its /api/signing/prepare call so the keys land inside the signed
/// ByteRange.
/// </summary>
public static class GetRenderCommitmentEndpoint
{
    public static IEndpointRouteBuilder MapGetRenderCommitment(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/documents/{id:guid}/render-commitment", async (
            Guid id,
            GetRenderCommitmentRequest body,
            IRecipientAccessGuard guard,
            IRenderCommitmentService service,
            AppDbContext db,
            DocumentStorage storage,
            Microsoft.Extensions.Options.IOptions<RenderCommitmentOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (!await guard.CanAccessDocumentAsync(id, cancellationToken))
                return Results.Json(new { error = "Not authorised for this document." },
                    statusCode: StatusCodes.Status403Forbidden);

            if (!service.IsAvailable)
                return Results.Json(new
                {
                    error = "Render commitment unavailable on this host.",
                    reason = service.UnavailableReason,
                }, statusCode: StatusCodes.Status503ServiceUnavailable);

            var doc = await db.Documents.AsNoTracking()
                .Where(d => d.Id == id)
                .Select(d => new { d.Id, d.StoragePath })
                .FirstOrDefaultAsync(cancellationToken);
            if (doc is null)
                return Results.NotFound();

            // ADR-0008 v1: render commitment only on documents with no
            // prior signer. Multi-signer chain commitments are deferred to
            // a follow-up ADR.
            var alreadySigned = await db.SignedDocuments.AsNoTracking()
                .AnyAsync(sd => sd.OriginalDocumentId == id, cancellationToken);
            if (alreadySigned)
                return Results.Json(new
                {
                    error = "Render commitment v1 does not support multi-signer documents.",
                }, statusCode: StatusCodes.Status409Conflict);

            byte[] pdfBytes;
            try
            {
                pdfBytes = await storage.ReadAsync(doc.StoragePath, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = "Document file missing on disk." });
            }

            var maxPages = options.Value.MaxPageCount;
            var pageCount = service.QuickPageCount(pdfBytes);
            if (pageCount > maxPages)
                return Results.Json(new
                {
                    error = $"Document exceeds the {maxPages}-page render commitment cap.",
                    pageCount,
                    maxPageCount = maxPages,
                }, statusCode: StatusCodes.Status422UnprocessableEntity);

            var locale = string.IsNullOrWhiteSpace(body?.Locale) ? "ro-RO" : body!.Locale!;

            var result = await service.ComputeAsync(pdfBytes, locale, cancellationToken);

            return Results.Ok(new GetRenderCommitmentResponse(
                Profile: result.Profile,
                Algo: result.Algo,
                Dpi: result.Dpi,
                PageCount: result.PageCount,
                Locale: result.Locale,
                RootHex: result.RootHex,
                PdfiumBinarySha256: service.PinnedBinarySha256 ?? ""));
        })
        .WithName("GetRenderCommitment")
        .WithTags("Documents")
        .Produces<GetRenderCommitmentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}

public sealed record GetRenderCommitmentRequest(string? Locale);

public sealed record GetRenderCommitmentResponse(
    string Profile,
    string Algo,
    int Dpi,
    int PageCount,
    string Locale,
    string RootHex,
    string PdfiumBinarySha256);
