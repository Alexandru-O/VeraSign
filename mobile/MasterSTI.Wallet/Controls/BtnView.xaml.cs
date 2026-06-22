using System;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Controls;

/// <summary>
/// Token-styled tap target. Mirrors prototype/components/Brand.jsx Btn.
/// Variant maps to background/foreground/border colour pairs that flip
/// via AppThemeBinding-resolved Application resources. Size controls
/// height + padding + label/icon size.
/// </summary>
public partial class BtnView : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(BtnView), string.Empty,
            propertyChanged: (b, _, _) => ((BtnView)b).ApplyAll());

    public static readonly BindableProperty VariantProperty =
        BindableProperty.Create(nameof(Variant), typeof(string), typeof(BtnView), "primary",
            propertyChanged: (b, _, _) => ((BtnView)b).ApplyAll());

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(string), typeof(BtnView), "md",
            propertyChanged: (b, _, _) => ((BtnView)b).ApplyAll());

    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(BtnView), null,
            propertyChanged: (b, _, _) => ((BtnView)b).ApplyAll());

    public static readonly BindableProperty IconRightProperty =
        BindableProperty.Create(nameof(IconRight), typeof(string), typeof(BtnView), null,
            propertyChanged: (b, _, _) => ((BtnView)b).ApplyAll());

    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(nameof(IsBusy), typeof(bool), typeof(BtnView), false,
            propertyChanged: (b, _, _) => ((BtnView)b).ApplyAll());

    public static readonly BindableProperty FullProperty =
        BindableProperty.Create(nameof(Full), typeof(bool), typeof(BtnView), false,
            propertyChanged: (b, _, _) => ((BtnView)b).ApplyAll());

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(BtnView));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(BtnView));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>"primary" | "secondary" | "ghost" | "danger" | "gold" | "primaryDark".</summary>
    public string Variant
    {
        get => (string)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    /// <summary>"sm" | "md" | "lg".</summary>
    public string Size
    {
        get => (string)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? IconRight
    {
        get => (string?)GetValue(IconRightProperty);
        set => SetValue(IconRightProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public bool Full
    {
        get => (bool)GetValue(FullProperty);
        set => SetValue(FullProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public event EventHandler? Clicked;

    public BtnView()
    {
        InitializeComponent();
        ApplyAll();

        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeChanged += OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => ApplyVariant();

    private void ApplyAll()
    {
        ApplyVariant();
        ApplySize();
        ApplyText();
        ApplyIcons();
        ApplyFull();
        ApplyBusy();
        IsEnabled = !IsBusy;
    }

    private void ApplyText() => ButtonLabel.Text = Text;

    private void ApplyFull()
    {
        ButtonBorder.HorizontalOptions = Full ? LayoutOptions.Fill : LayoutOptions.Start;
    }

    private void ApplyBusy()
    {
        BusySpinner.IsVisible = IsBusy;
        BusySpinner.IsRunning = IsBusy;
        ContentStack.IsVisible = !IsBusy;
        Opacity = IsBusy ? 0.7 : 1.0;
        BusySpinner.Color = ButtonLabel.TextColor;
    }

    private void ApplyIcons()
    {
        if (!string.IsNullOrEmpty(Icon))
        {
            LeftIcon.Name = Icon!;
            LeftIcon.IsVisible = true;
        }
        else
        {
            LeftIcon.IsVisible = false;
        }

        if (!string.IsNullOrEmpty(IconRight))
        {
            RightIcon.Name = IconRight!;
            RightIcon.IsVisible = true;
        }
        else
        {
            RightIcon.IsVisible = false;
        }
    }

    private static Color Res(string lightKey, string darkKey)
    {
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        var key = theme == AppTheme.Dark ? darkKey : lightKey;
        return (Color)Application.Current!.Resources[key];
    }

    private void ApplyVariant()
    {
        Color bg, fg, stroke;
        double border = 0;

        switch (Variant)
        {
            case "secondary":
                bg     = Res("BgElevLight",  "BgElevDark");
                fg     = Res("FgLight",      "FgDark");
                stroke = Res("BorderLight",  "BorderDark");
                border = 1;
                break;

            case "ghost":
                bg     = Colors.Transparent;
                fg     = Res("FgLight", "FgDark");
                stroke = Colors.Transparent;
                break;

            case "danger":
                bg     = Colors.Transparent;
                fg     = Res("DangerLight",  "DangerDark");
                stroke = Res("BorderLight",  "BorderDark");
                border = 1;
                break;

            case "gold":
                // Legacy variant — accent-tinted button used in dark hero pages.
                // Mapped to high-contrast accent for v2.
                bg     = Res("AccentLight",   "AccentDark");
                fg     = Res("AccentFgLight", "AccentFgDark");
                stroke = bg;
                break;

            case "primaryDark":
                bg     = Res("FgLight",        "FgDark");
                fg     = Res("FgInverseLight", "FgInverseDark");
                stroke = bg;
                break;

            case "primary":
            default:
                bg     = Res("AccentLight",   "AccentDark");
                fg     = Res("AccentFgLight", "AccentFgDark");
                stroke = bg;
                break;
        }

        ButtonBorder.BackgroundColor = bg;
        ButtonBorder.Stroke = stroke;
        ButtonBorder.StrokeThickness = border;
        ButtonLabel.TextColor = fg;
        LeftIcon.TintColor = fg;
        RightIcon.TintColor = fg;
        BusySpinner.Color = fg;
    }

    private void ApplySize()
    {
        // Brand.jsx sizes (height/padding/font):  sm 32/12/13 — md 40/16/14 — lg 48/20/15
        double h, ph, fz, isz;
        switch (Size)
        {
            case "sm":
                h = 32; ph = 12; fz = 13; isz = 13;
                break;
            case "lg":
                h = 48; ph = 20; fz = 15; isz = 15;
                break;
            case "md":
            default:
                h = 40; ph = 16; fz = 14; isz = 14;
                break;
        }

        ButtonBorder.HeightRequest = h;
        ButtonBorder.Padding = new Thickness(ph, 0);
        ButtonBorder.StrokeShape = new RoundRectangle { CornerRadius = 8 };
        ButtonLabel.FontSize = fz;
        LeftIcon.Size = isz;
        RightIcon.Size = isz;
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (IsBusy || !IsEnabled)
        {
            return;
        }

        // Quick visual press feedback
        VisualStateManager.GoToState(ButtonBorder, "Pressed");
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120),
            () => VisualStateManager.GoToState(ButtonBorder, "Normal"));

        Clicked?.Invoke(this, EventArgs.Empty);

        if (Command is { } cmd && cmd.CanExecute(CommandParameter))
        {
            cmd.Execute(CommandParameter);
        }
    }
}
