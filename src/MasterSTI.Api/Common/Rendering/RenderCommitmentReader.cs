using iText.Kernel.Pdf;

namespace MasterSTI.Api.Common.Rendering;

/// <summary>
/// The six /VeraSign.Render* values read back out of a PAdES signature
/// dictionary by <see cref="RenderCommitmentReader"/>. Mirror of
/// MasterSTI.Api.Common.PadesRenderCommitment on the writer side, but
/// typed for the verifier so the validation handler stays decoupled from
/// PadesService.
/// </summary>
public sealed record StoredRenderCommitment(
    string RootHex,
    string Algo,
    int Dpi,
    int PageCount,
    string Locale,
    string Profile);

/// <summary>
/// Pure parser that lifts the Pixel-Bound QES commitment (ADR-0008) out of a
/// signed PDF's signature dictionary. Returns <c>null</c> when the
/// /VeraSign.RenderRoot key is absent — that is the documented NotPresent
/// signal for pre-v1, multi-signer, or &gt;50-page documents and MUST NOT
/// throw. Partial-shape dictionaries (root present but algo/dpi missing) are
/// treated as malformed and also return null — the v1 schema is frozen, so a
/// missing required key means a tampered or hand-built signature, not an
/// unsupported variant.
/// </summary>
public static class RenderCommitmentReader
{
    private static readonly PdfName RootKey = new("VeraSign.RenderRoot");
    private static readonly PdfName AlgoKey = new("VeraSign.RenderAlgo");
    private static readonly PdfName DpiKey = new("VeraSign.RenderDpi");
    private static readonly PdfName PageCountKey = new("VeraSign.RenderPageCount");
    private static readonly PdfName LocaleKey = new("VeraSign.RenderLocale");
    private static readonly PdfName ProfileKey = new("VeraSign.RenderProfile");

    public static StoredRenderCommitment? Read(PdfDictionary? signatureDict)
    {
        if (signatureDict is null) return null;

        var rootStr = signatureDict.GetAsString(RootKey);
        if (rootStr is null) return null;

        // PadesService wrote the root via `new PdfString(rootBytes).SetHexWriting(true)`,
        // so GetValueBytes() yields the original 32 raw hash bytes regardless of
        // whether iText emitted them as <hex> or escaped literals.
        var rootBytes = rootStr.GetValueBytes();
        if (rootBytes is null || rootBytes.Length != 32) return null;
        var rootHex = Convert.ToHexStringLower(rootBytes);

        var algo = signatureDict.GetAsName(AlgoKey)?.GetValue();
        var dpiNum = signatureDict.GetAsNumber(DpiKey);
        var pageCountNum = signatureDict.GetAsNumber(PageCountKey);
        var localeStr = signatureDict.GetAsString(LocaleKey)?.GetValue();
        var profile = signatureDict.GetAsName(ProfileKey)?.GetValue();

        if (string.IsNullOrEmpty(algo)
            || dpiNum is null
            || pageCountNum is null
            || string.IsNullOrEmpty(localeStr)
            || string.IsNullOrEmpty(profile))
        {
            return null;
        }

        return new StoredRenderCommitment(
            RootHex: rootHex,
            Algo: algo,
            Dpi: dpiNum.IntValue(),
            PageCount: pageCountNum.IntValue(),
            Locale: localeStr,
            Profile: profile);
    }
}
