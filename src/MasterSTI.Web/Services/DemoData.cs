namespace MasterSTI.Web.Services;

/// <summary>
/// Static demo / sample data used by Dashboard, Templates, Recipients, etc.
/// Verbatim copies of the JSX prototype's hardcoded arrays (WebScreens.jsx) so the
/// dissertation demo looks identical to the design.
/// </summary>
public static class DemoData
{
    public sealed record KpiCard(string Label, string Value, string Trend, string Color, string Icon);

    public sealed record RecentDoc(string Name, string Recipient, string Level, string Status, string SentAt);

    public sealed record UpcomingSignatory(string Name, string Pill);

    public sealed record TemplateCard(string Title, string Description, string Used, string Category);

    public sealed record Signatory(string Name, string Email, string Role, string Level, int Order, bool Me);

    public static readonly IReadOnlyList<KpiCard> Kpis = new[]
    {
        new KpiCard("Documente trimise", "247", "+12 săptămâna asta", "#0F3E93", "upload"),
        new KpiCard("În așteptare",      "18",  "3 urgente",          "#D97706", "clock"),
        new KpiCard("Semnate",           "221", "rată 94%",           "#159855", "check-circle"),
        new KpiCard("Respinse",          "8",   "—",                  "#606878", "x"),
    };

    public static readonly IReadOnlyList<RecentDoc> RecentDocs = new[]
    {
        new RecentDoc("Contract închiriere · Apt 4B",  "Thea Popescu",     "QES",  "pending",  "azi, 14:02"),
        new RecentDoc("NDA proiect Orizont",           "Alexandru Stan",   "AdES", "signed",   "ieri"),
        new RecentDoc("Contract muncă · Senior Eng",   "Ioana Dragu",      "QES",  "sent",     "ieri"),
        new RecentDoc("Acord GDPR · Banca T",          "Radu Miron",       "AdES", "signed",   "2 zile"),
        new RecentDoc("Procură notarială",             "Elena Varga",      "QES",  "pending",  "3 zile"),
        new RecentDoc("Anexă servicii",                "Mihai Iancu",      "SES",  "rejected", "acum o săpt"),
    };

    public static readonly int[] WeekChart = { 32, 45, 28, 67, 55, 78, 94 };
    public static readonly string[] WeekLabels = { "L", "M", "M", "J", "V", "S", "D" };

    public static readonly IReadOnlyList<UpcomingSignatory> Upcoming = new[]
    {
        new UpcomingSignatory("Thea Popescu", "Azi"),
        new UpcomingSignatory("Ioana Dragu",  "Azi"),
        new UpcomingSignatory("Elena Varga",  "Azi"),
    };

    public static readonly IReadOnlyList<TemplateCard> Templates = new[]
    {
        new TemplateCard("Contract închiriere",            "Template complet · 12 pagini",       "28 folosit", "Imobiliare"),
        new TemplateCard("NDA standard",                   "Confidențialitate bilaterală",       "45 folosit", "Legal"),
        new TemplateCard("Contract individual de muncă",   "Conform codului muncii RO",          "14 folosit", "HR"),
        new TemplateCard("Acord GDPR",                     "Prelucrare date personale",          "87 folosit", "Legal"),
        new TemplateCard("Procură notarială",              "Model BNP · necesar QES",            "3 folosit",  "Legal"),
        new TemplateCard("Contract prestări servicii",     "Model simplu B2B",                   "62 folosit", "Business"),
    };

    public static readonly string[] TemplateCategories = { "Toate", "Imobiliare", "Legal", "HR", "Business", "Personalizate" };

    public static readonly IReadOnlyList<Signatory> Signatories = new[]
    {
        new Signatory("Toma Iliescu", "toma.iliescu@verasign.demo", "Locator", "QES", 1, false),
        new Signatory("Thea Popescu", "thea.popescu@verasign.demo", "Locatar", "QES", 2, false),
    };

    public sealed record QuickAddPersona(string Name, string Email, string Role, string Level);

    /// <summary>
    /// Canonical demo personas surfaced as chips on Recipients screen.
    /// "Eu (admin)" injected at runtime from <see cref="IAuthService"/> — not listed here.
    /// </summary>
    public static readonly IReadOnlyList<QuickAddPersona> QuickAddPersonas = new[]
    {
        new QuickAddPersona("Toma Iliescu", "toma.iliescu@verasign.demo", "Locator", "QES"),
        new QuickAddPersona("Thea Popescu", "thea.popescu@verasign.demo", "Locatar", "QES"),
    };

    public sealed record SettingsKv(string Key, string Value);
    public static readonly IReadOnlyList<SettingsKv> SigningDefaults = new[]
    {
        new SettingsKv("Nivel implicit",   "QES pentru valori > 5.000 €"),
        new SettingsKv("Marcă temporală",  "Activă (RFC 3161)"),
        new SettingsKv("Arhivare LTV",     "10 ani conform legislației RO"),
        new SettingsKv("Hash algorithm",   "SHA-256"),
    };

    public sealed record Tsp(string Name, string Status, string CountryCode);
    public static readonly IReadOnlyList<Tsp> Tsps = new[]
    {
        new Tsp("certSIGN RO", "QES · activ",    "RO"),
        new Tsp("Trans Sped",  "AdES · activ",   "RO"),
        new Tsp("D-Trust",     "QES · backup",   "DE"),
    };
}
