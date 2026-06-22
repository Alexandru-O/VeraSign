using Microsoft.Maui.Controls;

namespace MasterSTI.Wallet.Controls;

/// <summary>
/// Form-field wrapper. Mirrors prototype Brand.jsx Field.
/// </summary>
[ContentProperty(nameof(Content))]
public partial class FieldView : ContentView
{
    public static readonly BindableProperty LabelProperty =
        BindableProperty.Create(nameof(Label), typeof(string), typeof(FieldView), string.Empty,
            propertyChanged: (b, _, _) => ((FieldView)b).Apply());

    public static readonly BindableProperty HintProperty =
        BindableProperty.Create(nameof(Hint), typeof(string), typeof(FieldView), null,
            propertyChanged: (b, _, _) => ((FieldView)b).Apply());

    public static readonly BindableProperty ErrorProperty =
        BindableProperty.Create(nameof(Error), typeof(string), typeof(FieldView), null,
            propertyChanged: (b, _, _) => ((FieldView)b).Apply());

    public static readonly BindableProperty RequiredProperty =
        BindableProperty.Create(nameof(Required), typeof(bool), typeof(FieldView), false,
            propertyChanged: (b, _, _) => ((FieldView)b).Apply());

    public new static readonly BindableProperty ContentProperty =
        BindableProperty.Create(nameof(Content), typeof(View), typeof(FieldView));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Hint
    {
        get => (string?)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public string? Error
    {
        get => (string?)GetValue(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    public bool Required
    {
        get => (bool)GetValue(RequiredProperty);
        set => SetValue(RequiredProperty, value);
    }

    public new View? Content
    {
        get => (View?)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public FieldView()
    {
        InitializeComponent();
        Apply();
    }

    private void Apply()
    {
        LabelSpan.Text = Label ?? string.Empty;
        RequiredSpan.Text = Required ? " *" : string.Empty;
        LabelText.IsVisible = !string.IsNullOrEmpty(Label);

        if (!string.IsNullOrEmpty(Error))
        {
            ErrorLabel.Text = Error;
            ErrorLabel.IsVisible = true;
            HintLabel.IsVisible = false;
        }
        else if (!string.IsNullOrEmpty(Hint))
        {
            HintLabel.Text = Hint;
            HintLabel.IsVisible = true;
            ErrorLabel.IsVisible = false;
        }
        else
        {
            HintLabel.IsVisible = false;
            ErrorLabel.IsVisible = false;
        }
    }
}
