using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Controls;

public partial class OnboardingIssueOption : ContentView
{
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(OnboardingIssueOption), "user",
            propertyChanged: (b, _, _) => ((OnboardingIssueOption)b).Apply());

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(OnboardingIssueOption), string.Empty,
            propertyChanged: (b, _, _) => ((OnboardingIssueOption)b).Apply());

    public static readonly BindableProperty SubtitleProperty =
        BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(OnboardingIssueOption), string.Empty,
            propertyChanged: (b, _, _) => ((OnboardingIssueOption)b).Apply());

    public static readonly BindableProperty RecommendedProperty =
        BindableProperty.Create(nameof(Recommended), typeof(bool), typeof(OnboardingIssueOption), false,
            propertyChanged: (b, _, _) => ((OnboardingIssueOption)b).Apply());

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public bool Recommended
    {
        get => (bool)GetValue(RecommendedProperty);
        set => SetValue(RecommendedProperty, value);
    }

    public event EventHandler? Tapped;

    public OnboardingIssueOption()
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
        GlyphIcon.Name = Glyph;
        TitleLabel.Text = Title;
        SubtitleLabel.Text = Subtitle;
        RecPill.IsVisible = Recommended;

        var accent       = Res("AccentLight",   "AccentDark");
        var border       = Res("BorderLight",   "BorderDark");
        var bgSunken     = Res("BgSunkenLight", "BgSunkenDark");

        Outer.Stroke = Recommended ? accent : border;
        GlyphHolder.BackgroundColor = bgSunken;
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        Tapped?.Invoke(this, EventArgs.Empty);
    }
}
