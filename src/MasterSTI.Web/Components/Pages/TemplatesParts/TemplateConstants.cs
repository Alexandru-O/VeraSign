namespace MasterSTI.Web.Components.Pages.TemplatesParts;

/// <summary>
/// Shared lookup data for the Templates UI.
/// Keeps the enum-string round-trip in one place so the modals + filter chips agree.
/// </summary>
public static class TemplateConstants
{
    public sealed record CategoryOption(string Value, string Label);

    /// <summary>Backend enum values mapped to the Romanian display labels used in the UI.</summary>
    public static readonly CategoryOption[] CategoryOptions =
    [
        new("RealEstate", "Imobiliare"),
        new("Legal",      "Legal"),
        new("HR",         "HR"),
        new("Business",   "Business"),
        new("Custom",     "Personalizate"),
    ];

    /// <summary>Display labels used for filter chips ("Toate" + the five categories).</summary>
    public static readonly string[] FilterChips =
    {
        "Toate", "Imobiliare", "Legal", "HR", "Business", "Personalizate"
    };

    public static readonly string[] LevelOptions = { "SES", "AdES", "QES" };

    public static string CategoryLabel(string enumValue) =>
        CategoryOptions.FirstOrDefault(c => c.Value.Equals(enumValue, StringComparison.OrdinalIgnoreCase))?.Label
        ?? "Personalizate";
}
