using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Controls;

/// <summary>
/// Bottom tab bar for the wallet home flows. Mirrors prototype's MobileTabBar
/// (MobileScreens.jsx). Routes are //inbox, //identities (wallet),
/// //history, settings (modal placeholder).
/// </summary>
public partial class MobileTabBarView : ContentView
{
    public static readonly BindableProperty ActiveProperty =
        BindableProperty.Create(nameof(Active), typeof(string), typeof(MobileTabBarView), "inbox",
            propertyChanged: (b, _, _) => ((MobileTabBarView)b).Apply());

    public string Active
    {
        get => (string)GetValue(ActiveProperty);
        set => SetValue(ActiveProperty, value);
    }

    public MobileTabBarView()
    {
        InitializeComponent();
        Apply();

        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeChanged += (_, _) => Apply();
        }
    }

    private static Color Res(string lightKey, string darkKey)
    {
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        var key = theme == AppTheme.Dark ? darkKey : lightKey;
        return (Color)Application.Current!.Resources[key];
    }

    private void Apply()
    {
        var fg       = Res("FgLight",       "FgDark");
        var fgSubtle = Res("FgSubtleLight", "FgSubtleDark");

        // Accept legacy "identities" alias for "wallet".
        var active = Active == "identities" ? "wallet" : Active;

        ApplyTab(InboxIcon,    InboxLabel,    active == "inbox",    fg, fgSubtle);
        ApplyTab(WalletIcon,   WalletLabel,   active == "wallet",   fg, fgSubtle);
        ApplyTab(HistoryIcon,  HistoryLabel,  active == "history",  fg, fgSubtle);
        ApplyTab(SettingsIcon, SettingsLabel, active == "settings", fg, fgSubtle);
    }

    private static void ApplyTab(IconView icon, Label label, bool isActive, Color active, Color idle)
    {
        icon.TintColor = isActive ? active : idle;
        label.TextColor = isActive ? active : idle;
        label.FontFamily = isActive ? "GeistSemiBold" : "Geist";
    }

    private async void OnInboxTapped(object? sender, TappedEventArgs e)
    {
        if (Active == "inbox") return;
        await Shell.Current.GoToAsync("inbox");
    }

    private async void OnWalletTapped(object? sender, TappedEventArgs e)
    {
        if (Active == "wallet" || Active == "identities") return;
        await Shell.Current.GoToAsync("identities");
    }

    private async void OnHistoryTapped(object? sender, TappedEventArgs e)
    {
        if (Active == "history") return;
        await Shell.Current.GoToAsync("history");
    }

    private async void OnSettingsTapped(object? sender, TappedEventArgs e)
    {
        // No dedicated settings page yet; keep the existing placeholder dialog.
        var page = Application.Current?.Windows[0].Page;
        if (page is not null)
        {
            await page.DisplayAlertAsync(
                "Setări",
                "Setările contului vor fi disponibile în curând.",
                "OK");
        }
    }
}
