using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Controls;

/// <summary>
/// Pill-shaped status tag. Tone maps the bg/fg/dot colours from the v2
/// design tokens. Either a dot OR an icon can sit left of the label.
/// </summary>
public partial class PillView : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(PillView), string.Empty,
            propertyChanged: (b, _, _) => ((PillView)b).ApplyAll());

    public static readonly BindableProperty ToneProperty =
        BindableProperty.Create(nameof(Tone), typeof(string), typeof(PillView), "neutral",
            propertyChanged: (b, _, _) => ((PillView)b).ApplyAll());

    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(PillView), null,
            propertyChanged: (b, _, _) => ((PillView)b).ApplyAll());

    public static readonly BindableProperty DotProperty =
        BindableProperty.Create(nameof(Dot), typeof(bool), typeof(PillView), false,
            propertyChanged: (b, _, _) => ((PillView)b).ApplyAll());

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>"neutral" | "success" | "warning" | "danger" | "info".
    /// Legacy aliases also accepted: blue/gold (-> info), warn/amber (-> warning), red (-> danger).</summary>
    public string Tone
    {
        get => (string)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool Dot
    {
        get => (bool)GetValue(DotProperty);
        set => SetValue(DotProperty, value);
    }

    public PillView()
    {
        InitializeComponent();
        ApplyAll();

        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeChanged += (_, _) => ApplyTone();
        }
    }

    private void ApplyAll()
    {
        PillLabel.Text = Text;
        ApplyTone();
        ApplyIcon();
    }

    private void ApplyIcon()
    {
        var hasIcon = !string.IsNullOrEmpty(Icon);
        PillIcon.IsVisible = hasIcon;
        if (hasIcon) PillIcon.Name = Icon!;

        // Dot only shows when set explicitly AND there's no icon.
        PillDot.IsVisible = Dot && !hasIcon;
    }

    private static Color Res(string lightKey, string darkKey)
    {
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        var key = theme == AppTheme.Dark ? darkKey : lightKey;
        return (Color)Application.Current!.Resources[key];
    }

    private void ApplyTone()
    {
        // Map tone -> (bg, fg, dot) light-key/dark-key pairs.
        // Legacy aliases preserved so existing pages that used "blue", "gold",
        // "warn", "amber", "red" keep rendering.
        var (bgL, bgD, fgL, fgD) = Tone switch
        {
            "success" =>
                ("SuccessBgLight", "SuccessBgDark", "SuccessLight", "SuccessDark"),
            "warning" or "warn" or "amber" or "gold" =>
                ("WarningBgLight", "WarningBgDark", "WarningLight", "WarningDark"),
            "danger"  or "red" =>
                ("DangerBgLight",  "DangerBgDark",  "DangerLight",  "DangerDark"),
            "info"    or "blue" =>
                ("InfoBgLight",    "InfoBgDark",    "InfoLight",    "InfoDark"),
            _ /* neutral */ =>
                ("BgSunkenLight",  "BgSunkenDark",  "FgMutedLight", "FgMutedDark"),
        };

        var bg = Res(bgL, bgD);
        var fg = Res(fgL, fgD);

        PillBorder.BackgroundColor = bg;
        PillLabel.TextColor = fg;
        PillIcon.TintColor = fg;
        PillDot.Fill = new SolidColorBrush(fg);
    }
}
