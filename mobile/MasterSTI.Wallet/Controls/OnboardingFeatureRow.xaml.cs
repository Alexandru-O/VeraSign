using Microsoft.Maui.Controls;

namespace MasterSTI.Wallet.Controls;

public partial class OnboardingFeatureRow : ContentView
{
    public static readonly BindableProperty IconNameProperty =
        BindableProperty.Create(nameof(IconName), typeof(string), typeof(OnboardingFeatureRow), "shield-check",
            propertyChanged: (b, _, _) => ((OnboardingFeatureRow)b).Glyph.Name = ((OnboardingFeatureRow)b).IconName);

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(OnboardingFeatureRow), string.Empty,
            propertyChanged: (b, _, _) => ((OnboardingFeatureRow)b).TitleLabel.Text = ((OnboardingFeatureRow)b).Title);

    public static readonly BindableProperty SubtitleProperty =
        BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(OnboardingFeatureRow), string.Empty,
            propertyChanged: (b, _, _) => ((OnboardingFeatureRow)b).SubtitleLabel.Text = ((OnboardingFeatureRow)b).Subtitle);

    public string IconName
    {
        get => (string)GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
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

    public OnboardingFeatureRow()
    {
        InitializeComponent();
        Glyph.Name = IconName;
        TitleLabel.Text = Title;
        SubtitleLabel.Text = Subtitle;
    }
}
