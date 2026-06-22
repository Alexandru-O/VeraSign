using Microsoft.Maui.Controls;

namespace MasterSTI.Wallet.Controls;

/// <summary>
/// Theme toggle. Tapping calls App.ToggleTheme() which persists via Preferences.
/// The glyph reflects the destination theme (sun in dark, moon in light) — same
/// behaviour as prototype Brand.jsx ThemeToggle.
/// </summary>
public partial class ThemeToggleView : ContentView
{
    public ThemeToggleView()
    {
        InitializeComponent();
        UpdateGlyph();

        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeChanged += (_, _) => UpdateGlyph();
        }
    }

    private void UpdateGlyph()
    {
        var dark = (Application.Current?.RequestedTheme ?? AppTheme.Light) == AppTheme.Dark;
        Glyph.Name = dark ? "sun" : "moon";
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        App.ToggleTheme();
        UpdateGlyph();
    }
}
