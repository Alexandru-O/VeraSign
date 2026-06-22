using MasterSTI.Wallet.Services;
using Microsoft.Maui.Graphics;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace MasterSTI.Wallet.Pages;

public sealed partial class EnrollmentPage : ContentPage
{
    private readonly EnrollmentService _enrollmentService;

    public EnrollmentPage(EnrollmentService enrollmentService)
    {
        InitializeComponent();
        _enrollmentService = enrollmentService;
    }

    private async void OnEnrollTapped(object? sender, EventArgs e)
    {
        EnrollBtn.IsBusy = true;
        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        StatusLabel.IsVisible = false;

        try
        {
            var available = await CrossFingerprint.Current.IsAvailableAsync(allowAlternativeAuthentication: true);
            if (available)
            {
                var authRequest = new AuthenticationRequestConfiguration(
                    "Înrolează portofelul",
                    "Confirmă pentru a genera cheia de semnare a dispozitivului.");

                var result = await CrossFingerprint.Current.AuthenticateAsync(authRequest);
                if (!result.Authenticated)
                {
                    ShowError("Autentificarea biometrică a fost anulată sau a eșuat.");
                    return;
                }
            }

            var success = await _enrollmentService.EnrollAsync();

            if (!success)
            {
                ShowError("Înrolarea a eșuat. Verifică dacă Mock QTSP este pornit.");
                return;
            }

            StatusLabel.Text = "Portofel înrolat cu succes.";
            StatusLabel.TextColor = (Color)Application.Current!.Resources["Success500"];
            StatusLabel.IsVisible = true;

            await Task.Delay(900);
            await Shell.Current.GoToAsync("inbox");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnrollmentPage] Error: {ex}");
            ShowError($"Eroare la înrolare: {ex.Message}");
        }
        finally
        {
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            EnrollBtn.IsBusy = false;
        }
    }

    private void ShowError(string message)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = (Color)Application.Current!.Resources["Danger500"];
        StatusLabel.IsVisible = true;
    }
}
