namespace MasterSTI.Web.Services;

/// <summary>
/// Minimal in-circuit i18n. Demo scope: sidebar nav + topbar + a handful of high-visibility
/// labels. Full-app string extraction is out of scope for the prototype. Scoped per
/// Blazor circuit so every browser tab can pick its own language.
/// </summary>
public sealed class LanguageService
{
    public const string Romanian = "ro";
    public const string English = "en";

    private string _current = Romanian;

    public event Action? OnChanged;

    public string Current => _current;

    public void Set(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return;
        if (culture != Romanian && culture != English) return;
        if (_current == culture) return;
        _current = culture;
        OnChanged?.Invoke();
    }

    public void Toggle() => Set(_current == Romanian ? English : Romanian);

    /// <summary>
    /// Returns the localized string for the given key. Falls back to the Romanian value
    /// (the existing UI default) when no English translation exists, then to the key itself.
    /// </summary>
    public string T(string key)
    {
        if (_current == English && En.TryGetValue(key, out var en)) return en;
        return Ro.TryGetValue(key, out var ro) ? ro : key;
    }

    private static readonly Dictionary<string, string> Ro = new(StringComparer.Ordinal)
    {
        ["nav.dashboard"]    = "Panou",
        ["nav.documents"]    = "Documente",
        ["nav.templates"]    = "Șabloane",
        ["nav.verify"]       = "Verificare",
        ["nav.settings"]     = "Setări",
        ["topbar.newRequest"]= "Cerere nouă",
        ["topbar.skip"]      = "Sari la conținut",
        ["kpi.sent"]         = "Documente trimise",
        ["kpi.pending"]      = "În așteptare",
        ["kpi.signed"]       = "Semnate",
        ["kpi.rejected"]     = "Respinse",
        ["lang.toggle"]      = "EN",
    };

    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        ["nav.dashboard"]    = "Dashboard",
        ["nav.documents"]    = "Documents",
        ["nav.templates"]    = "Templates",
        ["nav.verify"]       = "Verify",
        ["nav.settings"]     = "Settings",
        ["topbar.newRequest"]= "New request",
        ["topbar.skip"]      = "Skip to content",
        ["kpi.sent"]         = "Documents sent",
        ["kpi.pending"]      = "Pending",
        ["kpi.signed"]       = "Signed",
        ["kpi.rejected"]     = "Rejected",
        ["lang.toggle"]      = "RO",
    };
}
