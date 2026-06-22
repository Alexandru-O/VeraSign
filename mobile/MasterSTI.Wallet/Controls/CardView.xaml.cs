using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Controls;

/// <summary>
/// Surface card with v2 token-driven hairline border. Mirrors prototype
/// Brand.jsx Card. Padding default 20. Emphasis legacy slot kept for
/// backwards compatibility.
/// </summary>
public partial class CardView : Border
{
    public static readonly BindableProperty EmphasisProperty =
        BindableProperty.Create(nameof(Emphasis), typeof(string), typeof(CardView), null,
            propertyChanged: (b, _, _) => ((CardView)b).ApplyEmphasis());

    public static readonly BindableProperty PaddingValueProperty =
        BindableProperty.Create(nameof(PaddingValue), typeof(double), typeof(CardView), 20.0,
            propertyChanged: (b, _, _) => ((CardView)b).ApplyPadding());

    /// <summary>Legacy "gold" emphasis slot — now resolves to Accent stroke.</summary>
    public string? Emphasis
    {
        get => (string?)GetValue(EmphasisProperty);
        set => SetValue(EmphasisProperty, value);
    }

    public double PaddingValue
    {
        get => (double)GetValue(PaddingValueProperty);
        set => SetValue(PaddingValueProperty, value);
    }

    public CardView()
    {
        InitializeComponent();
        ApplyEmphasis();
        ApplyPadding();

        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeChanged += (_, _) => ApplyEmphasis();
        }
    }

    private static Color Res(string lightKey, string darkKey)
    {
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        var key = theme == AppTheme.Dark ? darkKey : lightKey;
        return (Color)Application.Current!.Resources[key];
    }

    private void ApplyEmphasis()
    {
        Stroke = Emphasis == "gold"
            ? Res("AccentLight", "AccentDark")
            : Res("BorderLight", "BorderDark");
    }

    private void ApplyPadding()
    {
        Padding = new Thickness(PaddingValue);
    }
}
