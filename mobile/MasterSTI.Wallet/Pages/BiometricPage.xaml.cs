using MasterSTI.Wallet.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace MasterSTI.Wallet.Pages;

/// <summary>
/// M3 — Biometric prompt. Used as the "confirm pairing" gate before the
/// EnrollmentService runs. The actual key generation + PID issuance is
/// preserved from the existing flow. The <c>email</c> query parameter selects
/// which seeded recipient identity the wallet enrolls as (#42).
/// </summary>
[QueryProperty(nameof(Email), "email")]
public sealed partial class BiometricPage : ContentPage
{
    private readonly EnrollmentService _enrollmentService;
    private bool _busy;

    /// <summary>Chosen PID email; blank falls back to the canned persona email.</summary>
    public string? Email { get; set; }

    public BiometricPage(EnrollmentService enrollmentService)
    {
        InitializeComponent();
        _enrollmentService = enrollmentService;
    }

    private async void OnScanTapped(object? sender, TappedEventArgs e)
    {
        if (_busy) return;
        _busy = true;

        await SetPhaseScanning();

        try
        {
            // Real biometric gate (skip silently on emulator without enrolment).
            var available = await CrossFingerprint.Current.IsAvailableAsync(allowAlternativeAuthentication: true);
            if (available)
            {
                var req = new AuthenticationRequestConfiguration(
                    "Confirmă asocierea",
                    "Folosește biometria pentru a debloca cheia din enclavă.");
                var result = await CrossFingerprint.Current.AuthenticateAsync(req);
                if (!result.Authenticated)
                {
                    await ResetIdle("Anulat. Apasă pentru a reîncerca.");
                    return;
                }
            }
            else
            {
                // Demo-mode pause so the spinner is visible on simulators
                await Task.Delay(900);
            }

            // Real enrollment — generate StrongBox key, get SD-JWT, store it.
            // Off UI thread: AndroidDeviceKeyService.GenerateKeyPair() is synchronous
            // and StrongBox keygen takes seconds on the emulator. Running on UI thread
            // saturates it and a stray tap during keygen fires the 5s input-dispatcher
            // ANR. Mirrors the PBKDF2 fix in commit e861366.
            var ok = await Task.Run(() => _enrollmentService.EnrollAsync(Email)).ConfigureAwait(true);
            if (!ok)
            {
                await ResetIdle("Asocierea a eșuat. Verifică Mock.Issuer.");
                return;
            }

            await SetPhaseDone();
            await Task.Delay(500);
            // Onboarding next step: set the unlock PIN before reaching the Inbox.
            // Dispatch via BeginInvokeOnMainThread so GoToAsync starts on a fresh
            // main-thread message instead of inheriting this async chain's
            // continuation — works around a MAUI/Android case where GoToAsync hangs
            // immediately after a chained ScaleToAsync animation completes.
            System.Diagnostics.Debug.WriteLine("[BiometricPage] queuing GoToAsync(pinsetup)");
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await Shell.Current.GoToAsync("pinsetup");
                    System.Diagnostics.Debug.WriteLine("[BiometricPage] GoToAsync(pinsetup) done");
                }
                catch (Exception navEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[BiometricPage] nav EX: {navEx}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BiometricPage] {ex}");
            await ResetIdle($"Eroare: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private Task SetPhaseScanning()
    {
        StatusLabel.Text = "Se verifică...";
        ScanCircle.BackgroundColor = (Color)Application.Current!.Resources["RoBlue50"];
        ScanCircle.Stroke = (Color)Application.Current!.Resources["RoBlue500"];
        FpIcon.TintColor = (Color)Application.Current!.Resources["RoBlue500"];
        return Task.CompletedTask;
    }

    private Task SetPhaseDone()
    {
        // Animation removed: chained ScaleToAsync with Easing.SpringOut triggered
        // an Android compositor loop on the emulator (main thread spinning in
        // mono memset/GC for minutes after the animation "completed"). Static
        // appearance change is enough — green check + label tells the user.
        StatusLabel.Text = "Verificat";
        ScanCircle.BackgroundColor = (Color)Application.Current!.Resources["Success500"];
        ScanCircle.Stroke = (Color)Application.Current!.Resources["Success500"];
        FpIcon.IsVisible = false;
        CheckIcon.IsVisible = true;
        return Task.CompletedTask;
    }

    private Task ResetIdle(string message)
    {
        StatusLabel.Text = message;
        ScanCircle.BackgroundColor = (Color)Application.Current!.Resources["BgElev"];
        ScanCircle.Stroke = (Color)Application.Current!.Resources["Border"];
        FpIcon.TintColor = (Color)Application.Current!.Resources["Fg3"];
        FpIcon.IsVisible = true;
        CheckIcon.IsVisible = false;
        return Task.CompletedTask;
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("qrpair");
    }
}
