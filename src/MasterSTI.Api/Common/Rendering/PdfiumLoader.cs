using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MasterSTI.Api.Common.Rendering;

// Redirects PDFiumCore's [DllImport("pdfium")] calls to the pinned binary
// under tools/pdfium-v1/<rid>/ instead of the NuGet's bundled copy.
//
// PDFiumCore P/Invokes the native module under the bare name "pdfium". On
// Linux NativeLibrary.Load expects either an absolute path or a name that
// the OS resolver can find via LD_LIBRARY_PATH / DT_RUNPATH. We give it an
// absolute path so the resolver is bypassed entirely; this also prevents a
// system-wide libpdfium from being picked up by accident.
public static class PdfiumLoader
{
    private static string? _pinnedBinaryPath;

    public static string PinnedBinaryPath =>
        _pinnedBinaryPath ?? throw new InvalidOperationException("Install() must run before PinnedBinaryPath is read.");

    public static void Install(string toolsPdfiumV1Root)
    {
        var rid = RuntimeInformation.RuntimeIdentifier switch
        {
            var r when r.StartsWith("linux") => "linux-x64",
            var r when r.StartsWith("win") => "win-x64",
            var r => throw new PlatformNotSupportedException(
                $"RenderCommit.Cli spike is Linux/Windows only. RID '{r}' is out of scope per ADR-0008.")
        };

        var fileName = rid switch
        {
            "linux-x64" => "libpdfium.so",
            "win-x64" => "pdfium.dll",
            _ => throw new UnreachableException()
        };

        var absolutePath = Path.GetFullPath(Path.Combine(toolsPdfiumV1Root, rid, fileName));

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException(
                $"Pinned PDFium binary not found at '{absolutePath}'. " +
                "See tools/pdfium-v1/linux-x64/README.md for acquisition steps.");
        }

        RenderProfiles.Assert(RenderProfiles.CurrentProfile, absolutePath);

        _pinnedBinaryPath = absolutePath;

        // Resolver fires for every [DllImport] in the PDFiumCore assembly.
        // We answer only for "pdfium" to keep the surface tight.
        var pdfiumCore = typeof(PDFiumCore.fpdfview).Assembly;
        NativeLibrary.SetDllImportResolver(pdfiumCore, (libraryName, asm, searchPath) =>
        {
            if (string.Equals(libraryName, "pdfium", StringComparison.Ordinal))
                return NativeLibrary.Load(absolutePath);

            return IntPtr.Zero;
        });
    }
}
