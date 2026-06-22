using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Templates;

namespace MasterSTI.Api.Features.Templates.Common;

internal static class TemplateMapping
{
    public static TemplateDto ToDto(this Template t) => new(
        t.Id,
        t.OrganizationId,
        t.Title,
        t.Description,
        t.Category.ToString(),
        t.PdfPath,
        t.FieldsJson,
        t.BodyMarkdown,
        t.DefaultLevel,
        t.UsageCount,
        t.CreatedAt,
        t.UpdatedAt);

    public static TemplateCategory ParseCategory(string value)
    {
        if (Enum.TryParse<TemplateCategory>(value, ignoreCase: true, out var cat))
            return cat;
        // Allow hyphenated input like "Real-Estate"
        var normalized = value?.Replace("-", "").Replace(" ", "") ?? string.Empty;
        if (Enum.TryParse<TemplateCategory>(normalized, ignoreCase: true, out var cat2))
            return cat2;
        throw new ArgumentException($"Unknown template category '{value}'.");
    }
}
