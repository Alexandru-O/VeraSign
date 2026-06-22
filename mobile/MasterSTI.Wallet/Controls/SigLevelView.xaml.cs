using Microsoft.Maui.Controls;

namespace MasterSTI.Wallet.Controls;

/// <summary>
/// Signature-level chip (SES / AdES / QES). Mirrors prototype Brand.jsx SigLevel.
/// Size "sm" -> PillView with the level's tone. Size "md" -> mono label + Romanian sub.
/// </summary>
public partial class SigLevelView : ContentView
{
    public static readonly BindableProperty LevelProperty =
        BindableProperty.Create(nameof(Level), typeof(string), typeof(SigLevelView), "QES",
            propertyChanged: (b, _, _) => ((SigLevelView)b).Apply());

    /// <summary>"sm" = small pill, "md" = mono label with Romanian description.</summary>
    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(string), typeof(SigLevelView), "md",
            propertyChanged: (b, _, _) => ((SigLevelView)b).Apply());

    /// <summary>Backwards-compat: legacy ShowSubtitle flag.</summary>
    public static readonly BindableProperty ShowSubtitleProperty =
        BindableProperty.Create(nameof(ShowSubtitle), typeof(bool), typeof(SigLevelView), true,
            propertyChanged: (b, _, _) => ((SigLevelView)b).Apply());

    public string Level
    {
        get => (string)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public string Size
    {
        get => (string)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public bool ShowSubtitle
    {
        get => (bool)GetValue(ShowSubtitleProperty);
        set => SetValue(ShowSubtitleProperty, value);
    }

    public SigLevelView()
    {
        InitializeComponent();
        Apply();
    }

    private void Apply()
    {
        var (label, desc, tone) = Level switch
        {
            "SES"  => ("SES",  "Simplă",     "neutral"),
            "AdES" => ("AdES", "Avansată",   "info"),
            _      => ("QES",  "Calificată", "success"),
        };

        // Small variant: pill.
        SmallPill.Text = label;
        SmallPill.Tone = tone;
        SmallPill.Dot = true;

        // Medium variant: mono code + sub.
        LevelLabel.Text = label;
        DescLabel.Text = $"· {desc}";

        var isSm = Size == "sm";
        SmallPill.IsVisible = isSm;
        MediumRow.IsVisible = !isSm;
        DescLabel.IsVisible = !isSm && ShowSubtitle;
    }
}
