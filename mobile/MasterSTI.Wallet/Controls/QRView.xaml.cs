using System.IO;
using Microsoft.Maui.Controls;
using QRCoder;

namespace MasterSTI.Wallet.Controls;

/// <summary>
/// Renders a QR code via QRCoder.PngByteQRCode and shows it in an Image.
/// Suitable for self-pair / handoff payloads.
/// </summary>
public partial class QRView : ContentView
{
    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(string), typeof(QRView), null,
            propertyChanged: (b, _, _) => ((QRView)b).Render());

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(QRView), 180.0,
            propertyChanged: (b, _, _) => ((QRView)b).ApplySize());

    public string? Value
    {
        get => (string?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public QRView()
    {
        InitializeComponent();
        ApplySize();
        Render();
    }

    private void ApplySize()
    {
        QrImage.WidthRequest = Size;
        QrImage.HeightRequest = Size;
    }

    private void Render()
    {
        if (string.IsNullOrEmpty(Value))
        {
            QrImage.Source = null;
            return;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(Value, QRCodeGenerator.ECCLevel.Q);
        using var qr = new PngByteQRCode(data);
        var bytes = qr.GetGraphic(pixelsPerModule: 8);

        QrImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
    }
}
