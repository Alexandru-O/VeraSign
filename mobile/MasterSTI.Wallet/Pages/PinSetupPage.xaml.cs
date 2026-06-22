using MasterSTI.Wallet.Services;
using Microsoft.Maui.Controls;

namespace MasterSTI.Wallet.Pages;

/// <summary>
/// Onboarding PIN setup — runs after biometric pairing and before the Inbox.
/// The PIN (entered twice) is hashed by <see cref="IPinService"/>; the plaintext
/// is never persisted or logged.
/// </summary>
public sealed partial class PinSetupPage : ContentPage
{
    private readonly IPinService _pinService;
    private bool _busy;

    public PinSetupPage(IPinService pinService)
    {
        InitializeComponent();
        _pinService = pinService;
    }

    private void OnPinChanged(object? sender, TextChangedEventArgs e)
    {
        // Clear stale error as the user retypes.
        if (ErrorLabel.IsVisible)
            ErrorLabel.IsVisible = false;
    }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        if (_busy) return;

        var pin = PinEntry.Text ?? string.Empty;
        var confirm = ConfirmEntry.Text ?? string.Empty;

        if (pin.Length < _pinService.MinLength || !pin.All(char.IsDigit))
        {
            ShowError($"Codul PIN trebuie să aibă minim {_pinService.MinLength} cifre.");
            return;
        }
        if (pin != confirm)
        {
            ShowError("Codurile PIN introduse nu coincid.");
            return;
        }

        _busy = true;
        ContinueBtn.IsBusy = true;
        try
        {
            await _pinService.SetPinAsync(pin);
            await Shell.Current.GoToAsync("inbox");
        }
        finally
        {
            _busy = false;
            ContinueBtn.IsBusy = false;
        }
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    // PIN setup is a required onboarding step — block hardware back.
    protected override bool OnBackButtonPressed() => true;
}
