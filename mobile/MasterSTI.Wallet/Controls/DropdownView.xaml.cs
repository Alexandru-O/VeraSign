using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Controls;

/// <summary>One option entry for <see cref="DropdownView"/>.</summary>
public sealed class DropdownOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? Sub { get; init; }
    public bool HasSub => !string.IsNullOrEmpty(Sub);
}

/// <summary>
/// Tap-to-open dropdown. Mirrors prototype Brand.jsx Dropdown.
/// Options are an IList&lt;DropdownOption&gt;. The selected Value is exposed
/// via the Value bindable property; Changed fires when the user picks one.
/// </summary>
public partial class DropdownView : ContentView
{
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.Create(nameof(Items), typeof(IList<DropdownOption>), typeof(DropdownView), null,
            propertyChanged: (b, _, _) => ((DropdownView)b).Apply());

    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(string), typeof(DropdownView), null,
            propertyChanged: (b, _, _) => ((DropdownView)b).Apply());

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(DropdownView), "Selectează",
            propertyChanged: (b, _, _) => ((DropdownView)b).Apply());

    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(DropdownView), null,
            propertyChanged: (b, _, _) => ((DropdownView)b).Apply());

    public IList<DropdownOption>? Items
    {
        get => (IList<DropdownOption>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string? Value
    {
        get => (string?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public event EventHandler<string?>? Changed;

    private bool _open;

    public DropdownView()
    {
        InitializeComponent();
        Apply();

        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeChanged += (_, _) => ApplyTriggerStroke();
        }
    }

    private void Apply()
    {
        OptionsList.ItemsSource = Items;

        var current = Items?.FirstOrDefault(o => o.Value == Value);
        if (current is not null)
        {
            ValueLabel.Text = current.Label;
            ValueLabel.TextColor = ResolveFg();
        }
        else
        {
            ValueLabel.Text = Placeholder;
            ValueLabel.TextColor = ResolveSubtle();
        }

        if (!string.IsNullOrEmpty(Icon))
        {
            LeftIcon.Name = Icon!;
            LeftIcon.IsVisible = true;
        }
        else
        {
            LeftIcon.IsVisible = false;
        }

        ApplyTriggerStroke();
    }

    private static Color Res(string lightKey, string darkKey)
    {
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        var key = theme == AppTheme.Dark ? darkKey : lightKey;
        return (Color)Application.Current!.Resources[key];
    }

    private static Color ResolveFg() => Res("FgLight", "FgDark");
    private static Color ResolveSubtle() => Res("FgSubtleLight", "FgSubtleDark");

    private void ApplyTriggerStroke()
    {
        TriggerBorder.Stroke = _open
            ? Res("AccentLight", "AccentDark")
            : Res("BorderLight", "BorderDark");
    }

    private void OnTriggerTapped(object? sender, TappedEventArgs e)
    {
        _open = !_open;
        Popup.IsVisible = _open;
        ApplyTriggerStroke();
    }

    private void OnOptionTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element el || el.BindingContext is not DropdownOption opt) return;
        Value = opt.Value;
        _open = false;
        Popup.IsVisible = false;
        ApplyTriggerStroke();
        Changed?.Invoke(this, opt.Value);
    }
}
