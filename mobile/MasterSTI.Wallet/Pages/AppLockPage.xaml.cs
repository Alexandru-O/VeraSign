using MasterSTI.Wallet.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace MasterSTI.Wallet.Pages;

/// <summary>
/// AppLock — biometric (or PIN fallback) gate shown to an enrolled wallet on
/// cold start and on every resume from background. Unlocking routes to the
/// Inbox; a pending deep link is consumed by InboxPage afterwards.
/// </summary>
public sealed partial class AppLockPage : ContentPage
{
    private readonly IPinService _pinService;
    private bool _busy;
    private bool _unlocked;

    public AppLockPage(IPinService pinService)
    {
        InitializeComponent();
        _pinService = pinService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Auto-prompt biometric the moment the lock screen shows.
        await TryBiometricUnlockAsync();
    }

    private async void OnUnlockTapped(object? sender, TappedEventArgs e)
        => await TryBiometricUnlockAsync();

    private async void OnRetryClicked(object? sender, EventArgs e)
        => await TryBiometricUnlockAsync();

    private async Task TryBiometricUnlockAsync()
    {
        if (_busy || _unlocked) return;
        _busy = true;

        try
        {
            SetScanning();

            var available = await CrossFingerprint.Current.IsAvailableAsync(allowAlternativeAuthentication: true);
            if (!available)
            {
                // No biometric on this device — PIN is the only unlock path (#44).
                SetIdle("Biometria nu este disponibilă. Folosește codul PIN.");
                RetryBtn.IsVisible = false;
                return;
            }

            var req = new AuthenticationRequestConfiguration(
                "Deblochează portofelul",
                "Folosește biometria pentru a accesa portofelul.");
            var result = await CrossFingerprint.Current.AuthenticateAsync(req);
            if (result.Authenticated)
            {
                await UnlockAsync();
                return;
            }

            SetIdle("Deblocare anulată. Încearcă din nou sau folosește PIN-ul.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppLockPage] {ex}");
            SetIdle("Eroare la deblocare. Încearcă din nou.");
        }
        finally
        {
            _busy = false;
        }
    }

    private async void OnUsePinClicked(object? sender, EventArgs e)
    {
        if (_busy || _unlocked) return;
        _busy = true;
        try
        {
            // An enrolled wallet from before PIN setup existed (#40) has no
            // stored hash — offer a one-time setup from the lock screen (#44).
            if (!await _pinService.HasPinAsync())
            {
                if (await SetUpPinFromLockScreenAsync())
                    await UnlockAsync();
                return;
            }

            var pin = await DisplayPromptAsync(
                "Cod PIN",
                "Introdu codul PIN pentru a debloca portofelul:",
                "Deblochează", "Anulează",
                keyboard: Keyboard.Numeric,
                maxLength: 12);
            if (string.IsNullOrEmpty(pin))
                return;

            if (await _pinService.VerifyAsync(pin))
            {
                await UnlockAsync();
            }
            else
            {
                SetIdle("Cod PIN incorect.");
                await DisplayAlertAsync("Cod PIN incorect", "Codul introdus nu este corect.", "OK");
            }
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>One-time PIN setup offered from the lock screen. Returns true on success.</summary>
    private async Task<bool> SetUpPinFromLockScreenAsync()
    {
        var min = _pinService.MinLength;
        var pin = await DisplayPromptAsync(
            "Setează un cod PIN",
            $"Acest portofel nu are încă un cod PIN. Alege un cod de minim {min} cifre:",
            "Continuă", "Anulează",
            keyboard: Keyboard.Numeric, maxLength: 12);
        if (string.IsNullOrEmpty(pin))
            return false;
        if (pin.Length < min || !pin.All(char.IsDigit))
        {
            await DisplayAlertAsync("Cod invalid", $"Codul PIN trebuie să aibă minim {min} cifre.", "OK");
            return false;
        }

        var confirm = await DisplayPromptAsync(
            "Confirmă codul PIN",
            "Introdu din nou codul pentru confirmare:",
            "Setează", "Anulează",
            keyboard: Keyboard.Numeric, maxLength: 12);
        if (confirm != pin)
        {
            await DisplayAlertAsync("Coduri diferite", "Codurile introduse nu coincid.", "OK");
            return false;
        }

        await _pinService.SetPinAsync(pin);
        return true;
    }

    private async Task UnlockAsync()
    {
        _unlocked = true;
        AppLockState.IsLocked = false;
        await Shell.Current.GoToAsync("inbox");
    }

    private void SetScanning()
    {
        StatusLabel.Text = "Se verifică...";
        LockCircle.BackgroundColor = (Color)Application.Current!.Resources["RoBlue50"];
        LockCircle.Stroke = (Color)Application.Current!.Resources["RoBlue500"];
        FpIcon.TintColor = (Color)Application.Current!.Resources["RoBlue500"];
    }

    private void SetIdle(string message)
    {
        StatusLabel.Text = message;
        LockCircle.BackgroundColor = (Color)Application.Current!.Resources["BgElev"];
        LockCircle.Stroke = (Color)Application.Current!.Resources["Border"];
        FpIcon.TintColor = (Color)Application.Current!.Resources["Fg3"];
    }

    // Block hardware back — the wallet stays locked until unlocked.
    protected override bool OnBackButtonPressed() => true;
}
