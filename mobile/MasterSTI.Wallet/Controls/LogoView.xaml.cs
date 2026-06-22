using Microsoft.Maui.Controls;

namespace MasterSTI.Wallet.Controls;

/// <summary>
/// VeraSign wordmark v2 — accent dot + "vera" (full weight) + "Sign" (muted).
/// Mirrors prototype/components/Brand.jsx BrandLogo. Theme-aware via AppThemeBinding.
/// </summary>
public partial class LogoView : ContentView
{
    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(
            nameof(Size),
            typeof(double),
            typeof(LogoView),
            18.0,
            propertyChanged: OnSizeChanged);

    /// <summary>
    /// Legacy "full" / "mark" variant (kept for compatibility with existing pages).
    /// "mark" hides the wordmark and shows only the dot. Default = full.
    /// </summary>
    public static readonly BindableProperty VariantProperty =
        BindableProperty.Create(
            nameof(Variant),
            typeof(string),
            typeof(LogoView),
            "full",
            propertyChanged: OnVariantChanged);

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public string Variant
    {
        get => (string)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public LogoView()
    {
        InitializeComponent();
        ApplySize();
        ApplyVariant();
    }

    private static void OnSizeChanged(BindableObject b, object o, object n) => ((LogoView)b).ApplySize();
    private static void OnVariantChanged(BindableObject b, object o, object n) => ((LogoView)b).ApplyVariant();

    private void ApplySize()
    {
        // Dot is 0.9 * text size in the prototype, with inner cut-out at 22% inset.
        var dot = Size * 0.9;
        DotHost.WidthRequest = dot;
        DotHost.HeightRequest = dot;
        var inset = dot * 0.22;
        DotInner.Margin = new Thickness(inset);
        Wordmark.FontSize = Size;
    }

    private void ApplyVariant()
    {
        Wordmark.IsVisible = Variant != "mark";
    }
}
