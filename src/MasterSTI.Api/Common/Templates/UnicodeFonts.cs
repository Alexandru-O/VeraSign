using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Font;

namespace MasterSTI.Api.Common.Templates;

/// <summary>
/// Resolves a Unicode-capable font triple (Regular/Bold/Italic) for the PDF
/// renderer so Romanian diacritics survive the round-trip. iText's built-in
/// Helvetica is bound to WinAnsi which drops ăâîșțĂÂÎȘȚ; embedding a TTF
/// with IDENTITY_H fixes it.
///
/// Resolution order (first hit wins):
///   1. Linux DejaVu Sans       (apt-get install fonts-dejavu-core; Docker default)
///   2. Windows Arial           (fsType=Installable — embeddable)
///   3. Windows Segoe UI        (fsType=Restricted — only loads if PREFER_NOT_EMBEDDED works)
///   4. iText StandardFonts.HELVETICA (graceful fallback — diacritics WILL degrade)
///
/// Font BYTES + chosen embedding strategy are resolved once at startup. Call
/// <see cref="Create"/> to mint a per-document <see cref="PdfFont"/> triple —
/// iText binds each PdfFont to the PdfDocument it was first added to, so
/// sharing one PdfFont across multiple documents throws on the second close.
/// </summary>
internal static class UnicodeFonts
{
    private static readonly byte[]? _regularBytes;
    private static readonly byte[]? _boldBytes;
    private static readonly byte[]? _italicBytes;
    private static readonly PdfFontFactory.EmbeddingStrategy _strategy;
    private static readonly bool _useStandard14;

    private sealed record FontTriple(string Regular, string Bold, string Italic, PdfFontFactory.EmbeddingStrategy Strategy);

    private static readonly FontTriple[] _candidates =
    {
        // Linux (Docker fonts-dejavu-core) — fsType=Installable
        new("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Oblique.ttf",
            PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED),

        // Windows Arial — fsType=Installable, embeds cleanly with IDENTITY_H
        new(@"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\arialbd.ttf",
            @"C:\Windows\Fonts\ariali.ttf",
            PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED),

        // Windows Segoe UI — fsType=Restricted, force NOT_EMBEDDED (cmap still resolves diacritics).
        new(@"C:\Windows\Fonts\segoeui.ttf",
            @"C:\Windows\Fonts\segoeuib.ttf",
            @"C:\Windows\Fonts\segoeuii.ttf",
            PdfFontFactory.EmbeddingStrategy.PREFER_NOT_EMBEDDED),

        // macOS (developers running on Apple Silicon)
        new("/Library/Fonts/Arial Unicode.ttf",
            "/Library/Fonts/Arial Unicode.ttf",
            "/Library/Fonts/Arial Unicode.ttf",
            PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED),
    };

    static UnicodeFonts()
    {
        foreach (var t in _candidates)
        {
            if (!File.Exists(t.Regular)) continue;

            byte[] reg;
            try { reg = File.ReadAllBytes(t.Regular); }
            catch { continue; }

            _regularBytes = reg;
            _boldBytes    = File.Exists(t.Bold)   ? File.ReadAllBytes(t.Bold)   : null;
            _italicBytes  = File.Exists(t.Italic) ? File.ReadAllBytes(t.Italic) : null;
            _strategy     = t.Strategy;
            _useStandard14 = false;
            return;
        }

        _useStandard14 = true;
    }

    /// <summary>
    /// Mints a fresh PdfFont triple for a single PdfDocument. Call per-render —
    /// PdfFont instances must not be shared across PdfDocument instances.
    /// </summary>
    public static (PdfFont Regular, PdfFont Bold, PdfFont Italic) Create()
    {
        if (_useStandard14 || _regularBytes is null)
        {
            return (
                PdfFontFactory.CreateFont(StandardFonts.HELVETICA),
                PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD),
                PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE));
        }

        var reg = PdfFontFactory.CreateFont(_regularBytes, PdfEncodings.IDENTITY_H, _strategy);
        var bld = _boldBytes   is { Length: > 0 } ? PdfFontFactory.CreateFont(_boldBytes,   PdfEncodings.IDENTITY_H, _strategy) : reg;
        var itl = _italicBytes is { Length: > 0 } ? PdfFontFactory.CreateFont(_italicBytes, PdfEncodings.IDENTITY_H, _strategy) : reg;
        return (reg, bld, itl);
    }

}
