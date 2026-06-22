using MasterSTI.Wallet.Models;

namespace MasterSTI.Wallet.Config;

/// <summary>
/// Compile-time persona resolution for the two-emulator demo. Exactly one of
/// <c>WALLET_PERSONA_THEA</c> / <c>WALLET_PERSONA_TOMA</c> MUST be defined via
/// the MSBuild <c>WalletPersona</c> property. A bare build fails loudly so
/// the APK can never ship without an identity baked in.
/// Source of truth for the persona record stays <see cref="DemoPersona"/>.
/// </summary>
public static class WalletPersona
{
#if WALLET_PERSONA_THEA
    public static readonly DemoPersona Identity = DemoPersona.Thea;

    /// <summary>"locatar" (tenant / buyer) — counter-signs in the demo.</summary>
    public const string Role = "locatar";
#elif WALLET_PERSONA_TOMA
    public static readonly DemoPersona Identity = DemoPersona.Toma;

    /// <summary>"locator" (landlord / seller) — initiates signing flows in the demo.</summary>
    public const string Role = "locator";
#else
#error WalletPersona property not set — pass -p:WalletPersona=Thea or -p:WalletPersona=Toma when building the wallet APK.
#endif

    public static string GivenName => Identity.GivenName;
    public static string FamilyName => Identity.FamilyName;
    public static string BirthDate => Identity.BirthDate;
    public static string Email => Identity.Email;
    public static string Serial => Identity.Serial;
    public static string DisplayLabel => Identity.DisplayLabel;

    /// <summary>"Toma Iliescu" / "Thea Popescu" — for UI labels that don't need email.</summary>
    public static string FullName => $"{Identity.GivenName} {Identity.FamilyName}";
}
