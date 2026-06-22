using System.Runtime.InteropServices;
using PDFiumCore;

namespace MasterSTI.Api.Common.Rendering;

public sealed record RenderCommitmentResult(
    string Profile,
    string Algo,
    int Dpi,
    int PageCount,
    string Locale,
    string RootHex,
    IReadOnlyList<string> PageLeafHashesHex);

public static class RenderCommitment
{
    private const int CommittedDpi = 150;
    private const float PdfPointsPerInch = 72f;

    public static RenderCommitmentResult Compute(string pdfPath, string locale)
    {
        // PDFium is not internally thread-safe; lock at the wrapper boundary
        // so concurrent invocations of the CLI in the harness do not corrupt
        // shared library state. Process-level lock only -- inter-process
        // isolation is the OS's job.
        lock (PdfiumGate)
        {
            fpdfview.FPDF_InitLibrary();
            try
            {
                var document = fpdfview.FPDF_LoadDocument(pdfPath, null);
                if (document is null)
                {
                    var err = fpdfview.FPDF_GetLastError();
                    throw new InvalidOperationException($"FPDF_LoadDocument failed for '{pdfPath}' (error {err}).");
                }

                try
                {
                    var pageCount = fpdfview.FPDF_GetPageCount(document);
                    if (pageCount <= 0)
                        throw new InvalidOperationException($"Document '{pdfPath}' has no pages.");

                    var leaves = new List<byte[]>(pageCount);
                    var leavesHex = new List<string>(pageCount);

                    for (var i = 0; i < pageCount; i++)
                    {
                        var leaf = RenderPageLeaf(document, i, out var hex);
                        leaves.Add(leaf);
                        leavesHex.Add(hex);
                    }

                    var root = MerkleRoot.Compute(leaves);

                    return new RenderCommitmentResult(
                        Profile: RenderProfiles.CurrentProfile,
                        Algo: "SHA-256",
                        Dpi: CommittedDpi,
                        PageCount: pageCount,
                        Locale: locale,
                        RootHex: Convert.ToHexStringLower(root),
                        PageLeafHashesHex: leavesHex);
                }
                finally
                {
                    fpdfview.FPDF_CloseDocument(document);
                }
            }
            finally
            {
                fpdfview.FPDF_DestroyLibrary();
            }
        }
    }

    private static readonly Lock PdfiumGate = new();

    private static byte[] RenderPageLeaf(FpdfDocumentT document, int index, out string hex)
    {
        var page = fpdfview.FPDF_LoadPage(document, index);
        if (page is null)
            throw new InvalidOperationException($"FPDF_LoadPage failed for index {index}.");

        try
        {
            double widthPts = 0, heightPts = 0;
            fpdfview.FPDF_GetPageSizeByIndex(document, index, ref widthPts, ref heightPts);

            // Convert PDF points -> pixels at committed DPI.
            // Use double precision through the multiply, then round-down to
            // int. Rounding mode is fixed so two runs on the same input
            // produce the same width/height.
            var widthPx = (int)Math.Floor(widthPts * CommittedDpi / PdfPointsPerInch);
            var heightPx = (int)Math.Floor(heightPts * CommittedDpi / PdfPointsPerInch);
            if (widthPx <= 0 || heightPx <= 0)
                throw new InvalidOperationException($"Page {index} has non-positive pixel dims {widthPx}x{heightPx}.");

            var bitmap = fpdfview.FPDFBitmapCreateEx(
                widthPx, heightPx,
                (int)FPDFBitmapFormat.BGRA,
                IntPtr.Zero, 0);
            if (bitmap is null)
                throw new InvalidOperationException($"FPDFBitmapCreateEx failed for page {index}.");

            try
            {
                // White opaque background. 0xFFFFFFFF in PDFium fill APIs is
                // ARGB regardless of the bitmap format -- see PDFium docs.
                fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, widthPx, heightPx, 0xFFFFFFFFu);

                // RenderAnnotations matches what ReviewPage shows the user.
                // OptimizeTextForLcd is deliberately OFF: it introduces
                // platform-dependent subpixel positioning that breaks
                // determinism across hosts.
                fpdfview.FPDF_RenderPageBitmap(
                    bitmap, page,
                    0, 0,
                    widthPx, heightPx,
                    0,
                    (int)RenderFlags.RenderAnnotations);

                var stride = fpdfview.FPDFBitmapGetStride(bitmap);
                var bufferPtr = fpdfview.FPDFBitmapGetBuffer(bitmap);

                var byteLength = stride * heightPx;
                var pixels = new byte[byteLength];
                Marshal.Copy(bufferPtr, pixels, 0, byteLength);

                // ADR-0008 leaf personalisation: SHA-256(0x00 || pixelBuffer).
                // The buffer captured here is BGRA little-endian (PDFium's
                // native layout on x86); cross-RID byte-order parity is
                // explicitly out of scope for the spike per ADR §"Cross-toolchain caveat".
                var leaf = MerkleRoot.LeafHash(pixels);
                hex = Convert.ToHexStringLower(leaf);
                return leaf;
            }
            finally
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }
}
