namespace MasterSTI.Wallet.Services;

public interface IPdfPageRenderer
{
    /// <summary>
    /// Opens a PDF for page-by-page rendering. The returned session keeps the
    /// document open; dispose it to release the native renderer and temp file.
    /// </summary>
    Task<IPdfDocumentSession?> OpenAsync(byte[] pdfBytes, CancellationToken cancellationToken = default);
}

/// <summary>
/// A live PDF render session. <see cref="RenderPageAsync"/> calls are serialized —
/// the underlying native renderer allows only one open page at a time.
/// </summary>
public interface IPdfDocumentSession : IDisposable
{
    int PageCount { get; }

    Task<ImageSource?> RenderPageAsync(int pageIndex, CancellationToken cancellationToken = default);
}

internal sealed class NullPdfPageRenderer : IPdfPageRenderer
{
    public Task<IPdfDocumentSession?> OpenAsync(byte[] pdfBytes, CancellationToken cancellationToken = default)
        => Task.FromResult<IPdfDocumentSession?>(null);
}
