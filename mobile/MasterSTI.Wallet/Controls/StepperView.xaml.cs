using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Controls;

/// <summary>
/// Horizontal stepper. Mirrors prototype Brand.jsx Stepper:
/// 22-px round node + 8 px gap + label + 24x1 connector bar.
/// </summary>
public partial class StepperView : ContentView
{
    public static readonly BindableProperty StepsProperty =
        BindableProperty.Create(nameof(Steps), typeof(IList<string>), typeof(StepperView), null,
            propertyChanged: (b, _, _) => ((StepperView)b).Render());

    public static readonly BindableProperty ActiveProperty =
        BindableProperty.Create(nameof(Active), typeof(int), typeof(StepperView), 0,
            propertyChanged: (b, _, _) => ((StepperView)b).Render());

    /// <summary>Backwards-compat alias for Active.</summary>
    public static readonly BindableProperty CurrentIndexProperty =
        BindableProperty.Create(nameof(CurrentIndex), typeof(int), typeof(StepperView), 0,
            propertyChanged: (b, _, n) =>
            {
                ((StepperView)b).Active = (int)n;
            });

    public IList<string>? Steps
    {
        get => (IList<string>?)GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    public int Active
    {
        get => (int)GetValue(ActiveProperty);
        set => SetValue(ActiveProperty, value);
    }

    public int CurrentIndex
    {
        get => (int)GetValue(CurrentIndexProperty);
        set => SetValue(CurrentIndexProperty, value);
    }

    public StepperView()
    {
        InitializeComponent();
        Render();

        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeChanged += (_, _) => Render();
        }
    }

    private static Color Res(string lightKey, string darkKey)
    {
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        var key = theme == AppTheme.Dark ? darkKey : lightKey;
        return (Color)Application.Current!.Resources[key];
    }

    private void Render()
    {
        StepHost.Children.Clear();

        if (Steps is null || Steps.Count == 0)
        {
            return;
        }

        var accent       = Res("AccentLight",   "AccentDark");
        var accentFg     = Res("AccentFgLight", "AccentFgDark");
        var bgElev       = Res("BgElevLight",   "BgElevDark");
        var bgSunken     = Res("BgSunkenLight", "BgSunkenDark");
        var border       = Res("BorderLight",   "BorderDark");
        var fg           = Res("FgLight",       "FgDark");
        var fgSubtle     = Res("FgSubtleLight", "FgSubtleDark");

        for (var i = 0; i < Steps.Count; i++)
        {
            var done = i < Active;
            var cur  = i == Active;

            // Node background + stroke per state.
            var nodeBg = done ? accent : cur ? bgElev : bgSunken;
            var nodeStroke = done || cur ? accent : border;
            var nodeFg = done ? accentFg : cur ? accent : fgSubtle;

            var node = new Border
            {
                BackgroundColor = nodeBg,
                Stroke = nodeStroke,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 11 },
                WidthRequest = 22,
                HeightRequest = 22,
                Padding = 0,
                VerticalOptions = LayoutOptions.Center,
            };

            if (done)
            {
                node.Content = new IconView
                {
                    Name = "check",
                    Size = 12,
                    TintColor = nodeFg,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                };
            }
            else
            {
                node.Content = new Label
                {
                    Text = (i + 1).ToString(),
                    TextColor = nodeFg,
                    FontSize = 11,
                    FontFamily = "GeistMono",
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                };
            }

            var label = new Label
            {
                Text = Steps[i],
                FontSize = 13,
                FontFamily = cur ? "GeistSemiBold" : "Geist",
                TextColor = cur || done ? fg : fgSubtle,
                VerticalTextAlignment = TextAlignment.Center,
            };

            StepHost.Children.Add(new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                Children = { node, label }
            });

            if (i < Steps.Count - 1)
            {
                var bar = new BoxView
                {
                    HeightRequest = 1,
                    WidthRequest = 24,
                    Color = done ? accent : border,
                    VerticalOptions = LayoutOptions.Center,
                };
                StepHost.Children.Add(bar);
            }
        }
    }
}
