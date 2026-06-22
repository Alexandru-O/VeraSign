using MasterSTI.Wallet.Controls;
using MasterSTI.Wallet.Models;
using Microsoft.Maui.Controls;

namespace MasterSTI.Wallet.Pages;

/// <summary>
/// M2 — QR pair screen. The real camera scan is wired in <see cref="ScanPage"/>;
/// this page covers the dissertation demo's first-time pairing UX, including
/// picking which seeded recipient identity this wallet enrolls as (#42).
/// </summary>
public sealed partial class QRPairPage : ContentPage
{
    private const string CustomValue = "__custom__";

    private bool _scanning;

    /// <summary>
    /// PID email passed to the enrollment service. Defaults to the compile-time
    /// persona (the canned, registry-seeded identity) so the prior behaviour
    /// holds when the operator picks nothing.
    /// </summary>
    private string _selectedEmail = Config.WalletPersona.Email;

    public QRPairPage()
    {
        InitializeComponent();
        BuildPersonaOptions();
    }

    private void BuildPersonaOptions()
    {
        var options = new List<DropdownOption>();
        foreach (var p in DemoPersona.All)
        {
            options.Add(new DropdownOption
            {
                Value = p.Email,
                Label = $"{p.GivenName} {p.FamilyName}",
                Sub = p.Email
            });
        }
        options.Add(new DropdownOption
        {
            Value = CustomValue,
            Label = "Alt e-mail…",
            Sub = "Introdu manual o adresă din registru"
        });

        PersonaDropdown.Items = options;
        // Pre-select the canned default so enrollment never 404s on an empty pick.
        PersonaDropdown.Value = _selectedEmail;
    }

    private async void OnPersonaChanged(object? sender, string? value)
    {
        if (value == CustomValue)
        {
            var email = await DisplayPromptAsync(
                "E-mail semnatar",
                "Introdu adresa de e-mail a identității din registru:",
                "OK", "Anulează",
                placeholder: "nume@verasign.demo",
                keyboard: Keyboard.Email);

            if (!string.IsNullOrWhiteSpace(email))
            {
                _selectedEmail = email.Trim();
            }
            // Revert the dropdown caption to the resolved persona/email.
            var match = (PersonaDropdown.Items ?? new List<DropdownOption>())
                .FirstOrDefault(o => o.Value == _selectedEmail);
            PersonaDropdown.Value = match?.Value ?? Config.WalletPersona.Email;
        }
        else if (!string.IsNullOrWhiteSpace(value))
        {
            _selectedEmail = value!;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Scan-line animation disabled on Android: TranslateToAsync infinite loop
        // saturates the UI thread on the emulator and triggers Android ANR (5s
        // input-dispatcher timeout). Decorative only — static scan-line still
        // renders, just doesn't sweep.
#if !ANDROID
        _ = AnimateScanLineAsync();
#endif
    }

    private async Task AnimateScanLineAsync()
    {
        ScanLine.IsVisible = true;
        // Sweep up/down forever while the page is visible. Margins limit travel
        // to the inner code area (52..208 within a 260-tall viewport).
        while (this.IsVisible)
        {
            await ScanLine.TranslateToAsync(0, 156, 1100, Easing.CubicInOut);
            await ScanLine.TranslateToAsync(0, 0, 1100, Easing.CubicInOut);
        }
    }

    private async void OnFoundCodeClicked(object? sender, EventArgs e)
    {
        if (_scanning) return;
        _scanning = true;

        FoundCodeBtn.IsBusy = true;

        // Brief pause to let the scan animation finish a sweep, then route to
        // biometric confirmation (matches prototype's onPaired handler).
        await Task.Delay(900);
        await NavigateToBiometricAsync();
    }

    /// <summary>Carries the chosen PID email to BiometricPage, which runs enrollment.</summary>
    private Task NavigateToBiometricAsync()
        => Shell.Current.GoToAsync($"biometric?email={Uri.EscapeDataString(_selectedEmail)}");

    private async void OnManualEntryTapped(object? sender, TappedEventArgs e)
    {
        // Fallback: a simple modal prompt for the 12-char pairing code.
        var code = await DisplayPromptAsync(
            "Cod manual",
            "Introdu codul de asociere afișat pe desktop:",
            "OK", "Anulează",
            placeholder: "ABCD-EFGH-IJKL",
            maxLength: 14);

        if (!string.IsNullOrWhiteSpace(code))
        {
            await NavigateToBiometricAsync();
        }
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("///onboarding");
    }
}
