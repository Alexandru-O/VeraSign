namespace MasterSTI.Wallet.Models;

/// <summary>
/// Canonical demo persona for the two-emulator demo. One persona is baked
/// into each APK build via the <c>WalletPersona</c> MSBuild property (Phase 2);
/// the static fields here are the single source of truth. See
/// <c>docs/two-wallet-demo-plan.md</c>.
/// </summary>
public sealed record DemoPersona(
    string GivenName,
    string FamilyName,
    string BirthDate,
    string Email,
    string Serial)
{
    /// <summary>Header / picker label, e.g. "Toma Iliescu · toma.iliescu@verasign.demo".</summary>
    public string DisplayLabel => $"{GivenName} {FamilyName} · {Email}";

    /// <summary>"Locator" persona — initiates signing flows in the demo (landlord / seller role).</summary>
    public static readonly DemoPersona Toma = new(
        GivenName: "Toma",
        FamilyName: "Iliescu",
        BirthDate: "1985-03-04",
        Email: "toma.iliescu@verasign.demo",
        Serial: "7B:1C:3E:A4");

    /// <summary>"Locatar" persona — counter-signs in the demo (tenant / buyer role).</summary>
    public static readonly DemoPersona Thea = new(
        GivenName: "Thea",
        FamilyName: "Popescu",
        BirthDate: "1992-07-19",
        Email: "thea.popescu@verasign.demo",
        Serial: "2A:F8:C1:90");

    public static readonly IReadOnlyList<DemoPersona> All = new[] { Toma, Thea };
}
