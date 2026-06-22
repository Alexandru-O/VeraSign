using iText.Kernel.Pdf;
using Microsoft.Extensions.Options;

namespace MasterSTI.Api.Common.Rendering;

/// <summary>
/// Server-side facade over the static <see cref="RenderCommitment"/>
/// algorithm. Owns the one-shot <see cref="PdfiumLoader"/> install + the
/// per-request CPU offload, and reports availability so the HTTP endpoint
/// can surface a clean 503 when the pinned PDFium binary is missing on the
/// host (Windows dev box without a win-x64 slot, container without the
/// volume-mount, etc.) instead of crashing on every call.
/// </summary>
public interface IRenderCommitmentService
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    string? PinnedBinarySha256 { get; }

    int QuickPageCount(byte[] pdfBytes);

    Task<RenderCommitmentResult> ComputeAsync(byte[] pdfBytes, string locale, CancellationToken cancellationToken);
}

public sealed class RenderCommitmentOptions
{
    /// <summary>
    /// Root directory holding pinned PDFium binaries per RID:
    /// <c>{root}/linux-x64/libpdfium.so</c>, <c>{root}/win-x64/pdfium.dll</c>.
    /// Default expects the repo-layout path when the API runs from source;
    /// containers override via env var <c>RenderCommitment__PdfiumRoot</c>.
    /// </summary>
    public string PdfiumRoot { get; set; } = "tools/pdfium-v1";

    /// <summary>
    /// 50 in v1 per ADR-0008 §"Consequences" / 50-page cap. Documents over
    /// the cap are refused by the endpoint with a structured 422 so the
    /// wallet can show the BigDocBanner gracefully instead of waiting on a
    /// render that will never matter for the commitment.
    /// </summary>
    public int MaxPageCount { get; set; } = 50;
}

public sealed class RenderCommitmentService : IRenderCommitmentService
{
    private readonly ILogger<RenderCommitmentService> _logger;
    private readonly RenderCommitmentOptions _options;

    public bool IsAvailable { get; }
    public string? UnavailableReason { get; }
    public string? PinnedBinarySha256 { get; }

    public RenderCommitmentService(
        IOptions<RenderCommitmentOptions> options,
        ILogger<RenderCommitmentService> logger)
    {
        _logger = logger;
        _options = options.Value;

        try
        {
            PdfiumLoader.Install(_options.PdfiumRoot);
            PinnedBinarySha256 = RenderProfiles.Sha256Hex(PdfiumLoader.PinnedBinaryPath);
            IsAvailable = true;
            _logger.LogInformation(
                "Render commitment service ready. Profile={Profile} Binary={Path} Sha256={Sha}",
                RenderProfiles.CurrentProfile, PdfiumLoader.PinnedBinaryPath, PinnedBinarySha256);
        }
        catch (Exception ex)
        {
            UnavailableReason = ex.Message;
            IsAvailable = false;
            _logger.LogWarning(
                "Render commitment service unavailable: {Reason}. /api/documents/{{id}}/render-commitment will return 503.",
                ex.Message);
        }
    }

    public int QuickPageCount(byte[] pdfBytes)
    {
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdf = new PdfDocument(reader);
        return pdf.GetNumberOfPages();
    }

    public async Task<RenderCommitmentResult> ComputeAsync(
        byte[] pdfBytes, string locale, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
            throw new InvalidOperationException(
                $"Render commitment service unavailable: {UnavailableReason}");

        // PDFium's managed wrapper expects a file path; round-trip via temp
        // file rather than pinning the byte[] across an unmanaged call so
        // the GCHandle bookkeeping stays out of the request hot path. Worst
        // case is a few ms of file I/O against a CPU job that runs for
        // hundreds of ms per page.
        var tmpPath = Path.Combine(Path.GetTempPath(), $"render-commit-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tmpPath, pdfBytes, cancellationToken);
            return await Task.Run(() => RenderCommitment.Compute(tmpPath, locale), cancellationToken);
        }
        finally
        {
            try { File.Delete(tmpPath); } catch { /* best-effort */ }
        }
    }
}
