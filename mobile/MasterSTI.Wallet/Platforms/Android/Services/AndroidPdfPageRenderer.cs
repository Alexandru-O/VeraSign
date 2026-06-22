using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using MasterSTI.Wallet.Services;

namespace MasterSTI.Wallet.Platforms.Android.Services;

public sealed class AndroidPdfPageRenderer : IPdfPageRenderer
{
    public Task<IPdfDocumentSession?> OpenAsync(byte[] pdfBytes, CancellationToken cancellationToken = default)
        => Task.Run<IPdfDocumentSession?>(() =>
        {
            // ParcelFileDescriptor requires a file — write to temp; session deletes it on dispose.
            var tmpPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"verasign_preview_{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(tmpPath, pdfBytes);
            try
            {
                var file = new Java.IO.File(tmpPath);
                var pfd = ParcelFileDescriptor.Open(file, ParcelFileMode.ReadOnly);
                if (pfd is null)
                {
                    File.Delete(tmpPath);
                    return null;
                }
                var renderer = new PdfRenderer(pfd);
                return new AndroidPdfDocumentSession(tmpPath, pfd, renderer);
            }
            catch
            {
                try { File.Delete(tmpPath); } catch { /* best-effort */ }
                throw;
            }
        }, cancellationToken);
}

internal sealed class AndroidPdfDocumentSession : IPdfDocumentSession
{
    private const int TargetWidth = 1080;

    private readonly string _tmpPath;
    private readonly ParcelFileDescriptor _pfd;
    private readonly PdfRenderer _renderer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public AndroidPdfDocumentSession(string tmpPath, ParcelFileDescriptor pfd, PdfRenderer renderer)
    {
        _tmpPath = tmpPath;
        _pfd = pfd;
        _renderer = renderer;
        PageCount = renderer.PageCount;
    }

    public int PageCount { get; }

    public async Task<ImageSource?> RenderPageAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        if (pageIndex < 0 || pageIndex >= PageCount) return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return null;
            return await Task.Run(() => RenderSync(pageIndex), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private ImageSource? RenderSync(int pageIndex)
    {
        // Explicit Close() + Dispose() — `using var` on a Java.Lang.Object subclass
        // (PdfRenderer.Page is one) only drops the JNI handle and does NOT invoke
        // Java's close(). Android PdfRenderer allows ONE open page at a time, so a
        // not-yet-closed previous page makes OpenPage throw on the next call and
        // every page after the first never renders.
        var page = _renderer.OpenPage(pageIndex);
        try
        {
            float scale = (float)TargetWidth / page.Width;
            int bmpW = TargetWidth;
            int bmpH = (int)(page.Height * scale);

            using var bitmap = Bitmap.CreateBitmap(bmpW, bmpH, Bitmap.Config.Argb8888!);
            if (bitmap is null) return null;

            bitmap.EraseColor(global::Android.Graphics.Color.White);
            page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);

            using var ms = new MemoryStream();
            bitmap.Compress(Bitmap.CompressFormat.Png!, 90, ms);
            var imageBytes = ms.ToArray();

            return ImageSource.FromStream(() => new MemoryStream(imageBytes));
        }
        finally
        {
            page.Close();
            page.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _gate.Wait();
        try
        {
            _renderer.Close();
            _renderer.Dispose();
            _pfd.Close();
            _pfd.Dispose();
        }
        catch { /* best-effort */ }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }

        try { File.Delete(_tmpPath); } catch { /* best-effort */ }
    }
}
