using System.Security.Cryptography;

namespace MasterSTI.Api.Common.Rendering;

// Registry of allowed (profile, sha256) pairs for the pinned PDFium binary.
// Bumping the binary is a visible diff in three places per ADR-0008:
//   1. this dictionary
//   2. tools/pdfium-v1/<rid>/<file>
//   3. docs/render-profiles.md
//
// The Linux libpdfium.so referenced by PdfiumPinned-v1 is acquired manually
// from bblanchon/pdfium-binaries; see tools/pdfium-v1/linux-x64/README.md.
// Marked internal-to-assembly but visible to the spike CLI via source-link.
public static class RenderProfiles
{
    public const string CurrentProfile = "PdfiumPinned-v1";

    // PdfiumPinned-v1 = bblanchon/pdfium-binaries chromium/7678,
    // linux-x64 release asset. Tarball SLSA-attested sha256:
    //   80ff74fda755237de1df2feda6972aafbd82828be23836093c5708063c815af8
    // The value below is sha256(lib/libpdfium.so) extracted from that tarball.
    // Full provenance lives in docs/render-profiles.md.
    private static readonly Dictionary<string, string?> Registry = new(StringComparer.Ordinal)
    {
        [CurrentProfile] = "8f67fac92554e4a6ab57f7d4f6a3d6974b1646373e0d314d90694738941c040c",
    };

    public static void Assert(string profile, string binaryPath)
    {
        if (!Registry.TryGetValue(profile, out var expected))
            throw new InvalidOperationException($"Unknown render profile '{profile}'. Add to RenderProfiles registry.");

        var actual = Sha256Hex(binaryPath);

        if (expected is null)
        {
            throw new InvalidOperationException(
                $"Render profile '{profile}' has no pinned sha256 yet. " +
                $"Observed sha256 of '{binaryPath}': {actual}. " +
                "Drop this value into RenderProfiles.Registry and re-run.");
        }

        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Render profile '{profile}' sha256 mismatch. " +
                $"Expected {expected}, got {actual} for '{binaryPath}'.");
        }
    }

    public static string Sha256Hex(string path)
    {
        using var fs = File.OpenRead(path);
        var hash = SHA256.HashData(fs);
        return Convert.ToHexStringLower(hash);
    }
}
