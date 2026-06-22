using MasterSTI.Wallet.Services;
using ZXing.Net.Maui;

namespace MasterSTI.Wallet.Pages;

public sealed partial class ScanPage : ContentPage
{
    private readonly OpenId4VpParser _parser;
    private bool _isNavigating;

    public ScanPage(OpenId4VpParser parser)
    {
        InitializeComponent();
        _parser = parser;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isNavigating = false;
        BarcodeReader.IsDetecting = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        BarcodeReader.IsDetecting = false;
    }

    private void OnBarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        if (_isNavigating)
            return;

        var barcode = e.Results?.FirstOrDefault();
        if (barcode is null)
            return;

        var raw = barcode.Value;
        System.Diagnostics.Debug.WriteLine($"[ScanPage] Detected: {raw[..Math.Min(80, raw.Length)]}");

        // Must marshal back to main thread for navigation
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_isNavigating)
                return;

            if (!raw.StartsWith("openid4vp://", StringComparison.OrdinalIgnoreCase))
            {
                HintLabel.Text = $"Unrecognised QR: {raw[..Math.Min(60, raw.Length)]}";
                await Task.Delay(2000);
                HintLabel.Text = "Point camera at an openid4vp:// QR code";
                return;
            }

            var request = _parser.Parse(raw);
            if (request is null)
            {
                HintLabel.Text = "Invalid openid4vp:// — missing mandatory fields.";
                await Task.Delay(2500);
                HintLabel.Text = "Point camera at an openid4vp:// QR code";
                return;
            }

            _isNavigating = true;
            BarcodeReader.IsDetecting = false;

            await Shell.Current.GoToAsync("consent",
                new Dictionary<string, object> { ["Request"] = request });
        });
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("inbox");
    }
}
