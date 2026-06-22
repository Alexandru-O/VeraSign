using System.Text;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using ItDocument = iText.Layout.Document;

namespace MasterSTI.Api.Common.Templates;

/// <summary>
/// Renders a one-page-or-more A4 PDF for a template using a tiny inline
/// markdown subset. Used by the DB seeder and by the
/// /api/templates/{id}/content + /api/templates/{id}/replace-pdf flows.
/// Pure layout — no signing — iText7 v9 layout API.
/// </summary>
/// <remarks>
/// Supported markdown:
/// <list type="bullet">
///   <item><c># text</c> — h1</item>
///   <item><c>## text</c> — h2</item>
///   <item><c>- text</c> — bullet list item</item>
///   <item><c>&gt; text</c> — eyebrow / quote</item>
///   <item><c>---</c> — horizontal rule</item>
///   <item><c>[SIGNATURE]</c> — signature placeholder rectangle</item>
///   <item>blank line — paragraph break</item>
///   <item>anything else — body paragraph (11pt)</item>
/// </list>
/// Romanian diacritics: rendered via a TTF embedded with IDENTITY_H so ăâîșț
/// reach the PDF as Unicode points rather than degrading under WinAnsi. The
/// font is resolved at startup via <see cref="UnicodeFonts"/> from a fallback
/// chain (DejaVu Sans → Segoe UI → Helvetica) — the last entry is the
/// built-in StandardFonts fallback when no system TTF is reachable.
/// </remarks>
public sealed class TemplatePdfRenderer
{
    public byte[] Render(string title, string? bodyMarkdown)
    {
        using var ms = new MemoryStream();
        Render(ms, title, bodyMarkdown);
        return ms.ToArray();
    }

    public void Render(Stream output, string title, string? bodyMarkdown)
    {
        var ink = new DeviceRgb(24, 29, 40);
        var muted = new DeviceRgb(96, 104, 120);
        var accent = new DeviceRgb(15, 62, 147);
        var rule = new DeviceRgb(223, 228, 236);

        // Use leaveOpen so the caller controls disposal of `output`.
        using var writer = new PdfWriter(output, new WriterProperties());
        writer.SetCloseStream(false);
        var pdf = new PdfDocument(writer);

        // Fonts must be created per PdfDocument — iText binds PdfFont to the
        // first document that adds it, so a static cache breaks on the second
        // close ("PdfPages tree could be generated only once").
        var (helvetica, helveticaBold, helveticaItalic) = UnicodeFonts.Create();

        var doc = new ItDocument(pdf, PageSize.A4);
        doc.SetMargins(56, 56, 56, 56);

        // ---- Header (eyebrow + title + sub) -----------------------------
        var (eyebrow, body) = ExtractEyebrow(bodyMarkdown);

        var eyebrowText = string.IsNullOrWhiteSpace(eyebrow)
            ? $"VERASIGN · ȘABLON"
            : $"VERASIGN · ȘABLON · {eyebrow.ToUpperInvariant()}";

        doc.Add(new Paragraph(eyebrowText)
            .SetFont(helvetica)
            .SetFontSize(9)
            .SetFontColor(accent)
            .SetCharacterSpacing(1.4f)
            .SetMarginBottom(8));

        doc.Add(new Paragraph(title)
            .SetFont(helveticaBold)
            .SetFontSize(22)
            .SetFontColor(ink)
            .SetMarginBottom(4));

        doc.Add(new Paragraph("Document model · pregătit pentru semnare electronică")
            .SetFont(helvetica)
            .SetFontSize(11)
            .SetFontColor(muted)
            .SetMarginBottom(18));

        doc.Add(new Paragraph(" ")
            .SetBorderTop(new SolidBorder(rule, 0.6f))
            .SetMarginBottom(18)
            .SetFontSize(0.1f));

        // ---- Body -------------------------------------------------------
        if (string.IsNullOrWhiteSpace(body))
        {
            doc.Add(new Paragraph("Acest șablon nu are conținut. Editează-l pentru a adăuga text.")
                .SetFont(helveticaItalic)
                .SetFontSize(11)
                .SetFontColor(muted));
        }
        else
        {
            RenderMarkdown(doc, body, helvetica, helveticaBold, helveticaItalic, ink, muted, accent, rule);
        }

        // ---- Footer -----------------------------------------------------
        doc.Add(new Paragraph("Document model · masterSTI")
            .SetFont(helvetica)
            .SetFontSize(9)
            .SetFontColor(muted)
            .SetTextAlignment(TextAlignment.CENTER)
            .SetMarginTop(28));

        doc.Close();
    }

    /// <summary>
    /// If the first non-empty line is "&gt; eyebrow", strip it and return as eyebrow.
    /// </summary>
    private static (string? eyebrow, string body) ExtractEyebrow(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return (null, string.Empty);

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var ln = lines[i];
            if (string.IsNullOrWhiteSpace(ln)) continue;

            if (ln.StartsWith("> ", StringComparison.Ordinal))
            {
                var eyebrow = ln.Substring(2).Trim();
                var rest = string.Join('\n', lines.Skip(i + 1));
                return (eyebrow, rest);
            }
            return (null, markdown);
        }
        return (null, markdown);
    }

    private static void RenderMarkdown(
        ItDocument doc,
        string markdown,
        PdfFont regular,
        PdfFont bold,
        PdfFont italic,
        DeviceRgb ink,
        DeviceRgb muted,
        DeviceRgb accent,
        DeviceRgb rule)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        // Buffer consecutive bullet items into a single List for proper layout.
        List? currentList = null;

        void FlushList()
        {
            if (currentList is null) return;
            doc.Add(currentList);
            currentList = null;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushList();
                // blank line = paragraph break (already implicit via paragraph margins)
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                FlushList();
                var h1 = new Paragraph()
                    .SetFont(bold)
                    .SetFontSize(18)
                    .SetFontColor(ink)
                    .SetMarginTop(12)
                    .SetMarginBottom(6);
                AppendInline(h1, line.Substring(2).Trim(), bold, bold, italic, ink);
                doc.Add(h1);
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushList();
                var h2 = new Paragraph()
                    .SetFont(bold)
                    .SetFontSize(13)
                    .SetFontColor(ink)
                    .SetMarginTop(10)
                    .SetMarginBottom(4);
                AppendInline(h2, line.Substring(3).Trim(), bold, bold, italic, ink);
                doc.Add(h2);
                continue;
            }

            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushList();
                doc.Add(new Paragraph(line.Substring(2).Trim().ToUpperInvariant())
                    .SetFont(regular)
                    .SetFontSize(9)
                    .SetFontColor(accent)
                    .SetCharacterSpacing(1.4f)
                    .SetMarginTop(8)
                    .SetMarginBottom(6));
                continue;
            }

            if (line == "---")
            {
                FlushList();
                doc.Add(new Paragraph(" ")
                    .SetBorderTop(new SolidBorder(rule, 0.6f))
                    .SetMarginTop(8)
                    .SetMarginBottom(12)
                    .SetFontSize(0.1f));
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                currentList ??= new List()
                    .SetSymbolIndent(12)
                    .SetListSymbol("•")
                    .SetFont(regular)
                    .SetFontSize(11)
                    .SetFontColor(ink)
                    .SetMarginBottom(8);

                var itemPara = new Paragraph().SetFont(regular).SetFontColor(ink);
                AppendInline(itemPara, line.Substring(2).Trim(), regular, bold, italic, ink);
                var listItem = new ListItem();
                listItem.Add(itemPara);
                currentList.Add(listItem);
                continue;
            }

            if (string.Equals(line.Trim(), "[SIGNATURE]", StringComparison.OrdinalIgnoreCase))
            {
                FlushList();
                var placeholder = new Div()
                    .SetWidth(UnitValue.CreatePercentValue(60))
                    .SetHeight(72)
                    .SetPadding(10)
                    .SetMarginTop(18)
                    .SetMarginBottom(18)
                    .SetBorder(new DashedBorder(rule, 0.8f))
                    .Add(new Paragraph("Semnătura părții")
                        .SetFont(regular)
                        .SetFontSize(10)
                        .SetFontColor(muted));
                doc.Add(placeholder);
                continue;
            }

            // Default: body paragraph.
            FlushList();
            var body = new Paragraph()
                .SetFont(regular)
                .SetFontSize(11)
                .SetFontColor(ink)
                .SetMarginBottom(8)
                .SetMultipliedLeading(1.45f)
                .SetTextAlignment(TextAlignment.JUSTIFIED);
            AppendInline(body, line, regular, bold, italic, ink);
            doc.Add(body);
        }

        FlushList();
    }

    /// <summary>
    /// Tokenise inline markdown into Text runs: <c>**bold**</c>, <c>*italic*</c>,
    /// <c>_italic_</c>. Unmatched markers fall through as literal characters so a
    /// stray <c>*</c> renders as itself rather than swallowing the rest of the line.
    /// </summary>
    private static void AppendInline(Paragraph p, string text, PdfFont regular, PdfFont bold, PdfFont italic, DeviceRgb color)
    {
        if (string.IsNullOrEmpty(text)) return;

        var buffer = new StringBuilder();

        void FlushPlain()
        {
            if (buffer.Length == 0) return;
            p.Add(new Text(buffer.ToString()).SetFont(regular).SetFontColor(color));
            buffer.Clear();
        }

        var i = 0;
        var n = text.Length;
        while (i < n)
        {
            // **bold**
            if (i + 1 < n && text[i] == '*' && text[i + 1] == '*')
            {
                var close = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (close > i + 2)
                {
                    FlushPlain();
                    p.Add(new Text(text.Substring(i + 2, close - i - 2)).SetFont(bold).SetFontColor(color));
                    i = close + 2;
                    continue;
                }
            }

            // *italic* (single star, not part of **)
            if (text[i] == '*' && (i + 1 >= n || text[i + 1] != '*'))
            {
                var close = text.IndexOf('*', i + 1);
                if (close > i + 1 && (close + 1 >= n || text[close + 1] != '*'))
                {
                    FlushPlain();
                    p.Add(new Text(text.Substring(i + 1, close - i - 1)).SetFont(italic).SetFontColor(color));
                    i = close + 1;
                    continue;
                }
            }

            // _italic_
            if (text[i] == '_')
            {
                var close = text.IndexOf('_', i + 1);
                if (close > i + 1)
                {
                    FlushPlain();
                    p.Add(new Text(text.Substring(i + 1, close - i - 1)).SetFont(italic).SetFontColor(color));
                    i = close + 1;
                    continue;
                }
            }

            buffer.Append(text[i]);
            i++;
        }

        FlushPlain();
    }
}
