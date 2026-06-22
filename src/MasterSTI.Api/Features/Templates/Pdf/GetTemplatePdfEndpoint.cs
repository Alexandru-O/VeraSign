using MediatR;

namespace MasterSTI.Api.Features.Templates.Pdf;

public static class GetTemplatePdfEndpoint
{
    public static IEndpointRouteBuilder MapGetTemplatePdf(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/templates/{id:guid}/pdf", async (
            Guid id,
            IMediator mediator,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await mediator.Send(new GetTemplatePdfQuery(id), cancellationToken);

                // inline disposition so iframes render the PDF instead of downloading it.
                var safeName = string.IsNullOrEmpty(result.FileName) ? $"{id}.pdf" : result.FileName;
                http.Response.Headers.ContentDisposition = $"inline; filename=\"{safeName}\"";
                http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

                return Results.File(result.Bytes, "application/pdf");
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .WithName("GetTemplatePdf")
        .WithTags("Templates")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
