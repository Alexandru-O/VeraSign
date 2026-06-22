using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Controls;

/// <summary>
/// Lucide-style stroke icon (1.6 stroke, 24x24 grid).
/// Path data mirrors prototype/components/Brand.jsx Icon set.
/// Stroke colour follows the current theme via AppThemeBinding,
/// or via the explicit TintColor BindableProperty.
/// </summary>
public partial class IconView : ContentView
{
    public static readonly BindableProperty NameProperty =
        BindableProperty.Create(
            nameof(Name),
            typeof(string),
            typeof(IconView),
            "dot",
            propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(
            nameof(Size),
            typeof(double),
            typeof(IconView),
            20.0,
            propertyChanged: OnSizeChanged);

    public static readonly BindableProperty TintColorProperty =
        BindableProperty.Create(
            nameof(TintColor),
            typeof(Color),
            typeof(IconView),
            null,
            propertyChanged: OnVisualPropertyChanged);

    /// <summary>Backwards-compat alias.</summary>
    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(
            nameof(Color),
            typeof(Color),
            typeof(IconView),
            null,
            propertyChanged: (b, _, n) => ((IconView)b).TintColor = (Color?)n);

    public static readonly BindableProperty StrokeWidthProperty =
        BindableProperty.Create(
            nameof(StrokeWidth),
            typeof(double),
            typeof(IconView),
            1.6,
            propertyChanged: OnVisualPropertyChanged);

    public string Name
    {
        get => (string)GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Color? TintColor
    {
        get => (Color?)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    public Color? Color
    {
        get => (Color?)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public IconView()
    {
        InitializeComponent();
        ApplyAll();
    }

    private static void OnSizeChanged(BindableObject b, object o, object n) => ((IconView)b).ApplySize();
    private static void OnVisualPropertyChanged(BindableObject b, object o, object n) => ((IconView)b).ApplyAll();

    private void ApplyAll()
    {
        ApplySize();
        IconPath.StrokeThickness = StrokeWidth;
        if (TintColor is not null)
        {
            IconPath.Stroke = TintColor;
        }

        var data = Paths.TryGetValue(Name, out var d) ? d : Paths["dot"];
        IconPath.Data = (Geometry?)new PathGeometryConverter().ConvertFromInvariantString(data);
    }

    private void ApplySize()
    {
        IconPath.WidthRequest = Size;
        IconPath.HeightRequest = Size;
    }

    /// <summary>
    /// Lucide-style path data (24x24 grid, stroke-only).
    /// Mirrors prototype/components/Brand.jsx Icon set 1:1.
    /// </summary>
    private static readonly Dictionary<string, string> Paths = new()
    {
        // ── Navigation & arrows ──
        ["arrow-right"]    = "M5 12 H 19 M12 5 L 19 12 L 12 19",
        ["arrow-up-right"] = "M7 17 L 17 7 M7 7 H 17 V 17",
        ["arrow-left"]     = "M19 12 H 5 M12 19 L 5 12 L 12 5",
        ["chevron-down"]   = "M6 9 L 12 15 L 18 9",
        ["chevron-right"]  = "M9 6 L 15 12 L 9 18",
        ["chevron-left"]   = "M15 6 L 9 12 L 15 18",

        // ── Core / status ──
        ["check"]          = "M20 6 L 9 17 L 4 12",
        ["check-circle"]   = "M12 2 A 10 10 0 1 0 12 22 A 10 10 0 1 0 12 2 Z M9 12 L 11 14 L 15 10",
        ["x"]              = "M18 6 L 6 18 M6 6 L 18 18",
        ["close"]          = "M18 6 L 6 18 M6 6 L 18 18",
        ["plus"]           = "M12 5 V 19 M5 12 H 19",
        ["minus"]          = "M5 12 H 19",

        // ── Shield / security ──
        ["shield"]         = "M12 22 C 20 18 20 12 20 12 V 5 L 12 2 L 4 5 V 12 C 4 18 12 22 12 22 Z",
        ["shield-check"]   = "M12 22 C 20 18 20 12 20 12 V 5 L 12 2 L 4 5 V 12 C 4 18 12 22 12 22 Z M9 12 L 11 14 L 15 10",
        ["fingerprint"]    = "M12 2 A 10 10 0 0 0 4.5 18.5 M12 6 A 6 6 0 0 0 6 12 C 6 15 7 17 7 17 M12 10 A 2 2 0 0 0 10 12 C 10 16 9 18 9 18 M16 18 C 16 18 17 15 17 12 M19 14 A 7 7 0 0 0 12 7",
        ["lock"]           = "M3 11 H 21 V 22 H 3 Z M7 11 V 7 A 5 5 0 0 1 17 7 V 11",
        ["key"]            = "M8 11 A 4 4 0 1 0 8 19 A 4 4 0 1 0 8 11 Z M10.5 12.5 L 21 2 M17 6 L 20 9 M15 8 L 18 11",

        // ── Documents ──
        ["doc"]            = "M14 2 H 6 A 2 2 0 0 0 4 4 V 20 A 2 2 0 0 0 6 22 H 18 A 2 2 0 0 0 20 20 V 8 L 14 2 Z M14 2 V 8 H 20",
        ["file"]           = "M14 2 H 6 A 2 2 0 0 0 4 4 V 20 A 2 2 0 0 0 6 22 H 18 A 2 2 0 0 0 20 20 V 8 L 14 2 Z M14 2 V 8 H 20",
        ["file-signature"] = "M14 2 H 6 A 2 2 0 0 0 4 4 V 20 A 2 2 0 0 0 6 22 H 18 A 2 2 0 0 0 20 20 V 8 L 14 2 Z M14 2 V 8 H 20 M8 18 C 9.5 16 11 16 12.5 18 S 15.5 20 17 18 M16 14 H 15",
        ["signature"]      = "M14 2 H 6 A 2 2 0 0 0 4 4 V 20 A 2 2 0 0 0 6 22 H 18 A 2 2 0 0 0 20 20 V 8 L 14 2 Z M14 2 V 8 H 20 M8 18 C 9.5 16 11 16 12.5 18 S 15.5 20 17 18",
        ["folder"]         = "M22 19 A 2 2 0 0 1 20 21 H 4 A 2 2 0 0 1 2 19 V 5 A 2 2 0 0 1 4 3 H 9 L 11 6 H 20 A 2 2 0 0 1 22 8 Z",

        // ── QR & wallet ──
        ["qr"]             = "M3 3 H 10 V 10 H 3 Z M14 3 H 21 V 10 H 14 Z M3 14 H 10 V 21 H 3 Z M14 14 H 17 V 17 H 14 Z M21 14 V 17 M14 21 H 21 M21 21",
        ["wallet"]         = "M19 7 V 5 A 2 2 0 0 0 17 3 H 5 A 2 2 0 0 0 3 5 V 19 A 2 2 0 0 0 5 21 H 19 A 2 2 0 0 0 21 19 V 17 M21 9 V 15 H 16 A 3 3 0 0 1 16 9 Z",

        // ── Communication / mail ──
        ["mail"]           = "M2 4 H 22 V 20 H 2 Z M22 7 L 12 13 L 2 7",

        // ── Up/Down ──
        ["upload"]         = "M21 15 V 19 A 2 2 0 0 1 19 21 H 5 A 2 2 0 0 1 3 19 V 15 M7 10 L 12 5 L 17 10 M12 5 V 17",
        ["download"]       = "M21 15 V 19 A 2 2 0 0 1 19 21 H 5 A 2 2 0 0 1 3 19 V 15 M7 14 L 12 19 L 17 14 M12 19 V 5",

        // ── Search / settings ──
        ["search"]         = "M11 3 A 8 8 0 1 0 11 19 A 8 8 0 1 0 11 3 Z M21 21 L 16.7 16.7",
        ["settings"]       = "M12 9 A 3 3 0 1 0 12 15 A 3 3 0 1 0 12 9 Z M12 1 V 4 M12 20 V 23 M4.2 4.2 L 6.3 6.3 M17.7 17.7 L 19.8 19.8 M1 12 H 4 M20 12 H 23 M4.2 19.8 L 6.3 17.7 M17.7 6.3 L 19.8 4.2",

        // ── Theme / time ──
        ["sun"]            = "M12 8 A 4 4 0 1 0 12 16 A 4 4 0 1 0 12 8 Z M12 2 V 4 M12 20 V 22 M4.93 4.93 L 6.34 6.34 M17.66 17.66 L 19.07 19.07 M2 12 H 4 M20 12 H 22 M6.34 17.66 L 4.93 19.07 M19.07 4.93 L 17.66 6.34",
        ["moon"]           = "M21 12.79 A 9 9 0 1 1 11.21 3 A 7 7 0 0 0 21 12.79 Z",
        ["clock"]          = "M12 2 A 10 10 0 1 0 12 22 A 10 10 0 1 0 12 2 Z M12 6 V 12 L 16 14",

        // ── Inbox / activity ──
        ["inbox"]          = "M22 12 H 16 L 14 15 H 10 L 8 12 H 2 M5.45 5.11 L 2 12 V 18 A 2 2 0 0 0 4 20 H 20 A 2 2 0 0 0 22 18 V 12 L 18.55 5.11 A 2 2 0 0 0 16.76 4 H 7.24 A 2 2 0 0 0 5.45 5.11 Z",
        ["history"]        = "M3 12 A 9 9 0 1 0 6 5.3 L 3 8 M3 3 V 8 H 8 M12 7 V 12 L 15 14",
        ["bell"]           = "M6 8 A 6 6 0 0 1 18 8 C 18 15 21 17 21 17 H 3 C 3 17 6 15 6 8 M10.3 21 A 1.94 1.94 0 0 0 13.7 21",

        // ── Eye / send ──
        ["eye"]            = "M2 12 C 5 5 19 5 22 12 C 19 19 5 19 2 12 Z M12 9 A 3 3 0 1 0 12 15 A 3 3 0 1 0 12 9 Z",
        ["send"]           = "M22 2 L 15 22 L 11 13 L 2 9 Z M22 2 L 11 13",

        // ── Layout / menu ──
        ["menu"]           = "M4 6 H 20 M4 12 H 20 M4 18 H 20",

        // ── Globe / EU ──
        ["globe"]          = "M12 2 A 10 10 0 1 0 12 22 A 10 10 0 1 0 12 2 Z M2 12 H 22 M12 2 A 15.3 15.3 0 0 1 16 12 A 15.3 15.3 0 0 1 12 22 A 15.3 15.3 0 0 1 8 12 A 15.3 15.3 0 0 1 12 2 Z",
        ["eu"]             = "M3 12 A 9 9 0 1 0 21 12 A 9 9 0 1 0 3 12 Z",

        // ── Person / users ──
        ["user"]           = "M19 21 V 19 A 4 4 0 0 0 15 15 H 9 A 4 4 0 0 0 5 19 V 21 M12 3 A 4 4 0 1 0 12 11 A 4 4 0 1 0 12 3 Z",
        ["users"]          = "M16 21 V 19 A 4 4 0 0 0 12 15 H 6 A 4 4 0 0 0 2 19 V 21 M9 3 A 4 4 0 1 0 9 11 A 4 4 0 1 0 9 3 Z M22 21 V 19 A 4 4 0 0 0 19 15.13 M16 3.13 A 4 4 0 0 1 16 10.88",

        // ── Star / sparkle ──
        ["star"]           = "M12 2 L 15.09 8.26 L 22 9.27 L 17 14.14 L 18.18 21.02 L 12 17.77 L 5.82 21.02 L 7 14.14 L 2 9.27 L 8.91 8.26 Z",
        ["sparkle"]        = "M12 3 V 6 M12 18 V 21 M3 12 H 6 M18 12 H 21 M5.6 5.6 L 7.7 7.7 M16.3 16.3 L 18.4 18.4 M5.6 18.4 L 7.7 16.3 M16.3 7.7 L 18.4 5.6",

        // ── Misc ──
        ["pen"]            = "M12 20 H 21 M16.5 3.5 A 2.121 2.121 0 0 1 19.5 6.5 L 7 19 L 3 20 L 4 16 Z",
        ["trash"]          = "M3 6 H 21 M19 6 V 20 A 2 2 0 0 1 17 22 H 7 A 2 2 0 0 1 5 20 V 6 M8 6 V 4 A 2 2 0 0 1 10 2 H 14 A 2 2 0 0 1 16 4 V 6",
        ["share"]          = "M15 5 A 3 3 0 1 0 21 5 A 3 3 0 1 0 15 5 Z M3 12 A 3 3 0 1 0 9 12 A 3 3 0 1 0 3 12 Z M15 19 A 3 3 0 1 0 21 19 A 3 3 0 1 0 15 19 Z M8.6 13.5 L 15.4 17.5 M15.4 6.5 L 8.6 10.5",
        ["dot"]            = "M9.5 12 A 2.5 2.5 0 1 0 14.5 12 A 2.5 2.5 0 1 0 9.5 12 Z",
    };
}
