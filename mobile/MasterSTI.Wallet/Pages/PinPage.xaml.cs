using MasterSTI.Wallet.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Pages;

/// <summary>
/// SAD entry — PIN fallback for ConsentPage. Reached when biometric is
/// unavailable / refused (ADR-0007 strict fingerprint check). PIN buffer is
/// local-only and dropped on submit/navigation; never logged. Each submit
/// runs Prepare + Sign through <see cref="IWalletSigningOrchestrator"/>;
/// successful sign forwards to <c>status?SigningRequestId=…</c>.
/// </summary>
[QueryProperty(nameof(DocId), "docId")]
[QueryProperty(nameof(RecipientId), "recipientId")]
[QueryProperty(nameof(DtbsHash), "dtbsHash")]
public sealed partial class PinPage : ContentPage
{
    private const int PinLength = 6;
    private const int LockoutThreshold = 3;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);

    private readonly IWalletSigningOrchestrator _orchestrator;

    // PIN buffer — string builder kept local so the SAD never lands in a
    // bindable property / view-model that could leak through xamarin tooling.
    private readonly System.Text.StringBuilder _pinBuffer = new(PinLength);
    private int _pinRejectedAttempts;
    private bool _locked;
    private bool _busy;

    public string DocId { get; set; } = string.Empty;
    public string RecipientId { get; set; } = string.Empty;
    public string DtbsHash { get; set; } = string.Empty;

    public PinPage(IWalletSigningOrchestrator orchestrator)
    {
        InitializeComponent();
        _orchestrator = orchestrator;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RenderDtbsPrefix();
    }

    private void RenderDtbsPrefix()
    {
        if (string.IsNullOrWhiteSpace(DtbsHash) || DtbsHash.Length < 12)
        {
            DtbsHashLabel.IsVisible = false;
            return;
        }
        var hex = DtbsHash.ToUpperInvariant();
        DtbsHashLabel.Text = $"{hex[..4]} · {hex[4..8]} · {hex[8..12]}";
        DtbsHashLabel.IsVisible = true;
    }

    private void OnKeyTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _locked) return;
        if (sender is not Border border) return;
        if (border.GestureRecognizers.FirstOrDefault() is not TapGestureRecognizer tap) return;
        if (tap.CommandParameter is not string digit) return;
        if (_pinBuffer.Length >= PinLength) return;

        _pinBuffer.Append(digit);
        RenderDots();

        if (_pinBuffer.Length == PinLength)
            _ = SubmitAsync();
    }

    private void OnDeleteTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _locked) return;
        if (_pinBuffer.Length == 0) return;
        _pinBuffer.Length--;
        RenderDots();
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private void RenderDots()
    {
        var dots = new[] { Dot0, Dot1, Dot2, Dot3, Dot4, Dot5 };
        var filled = (Color)Application.Current!.Resources["FgLight"];
        for (var i = 0; i < dots.Length; i++)
        {
            var isFilled = i < _pinBuffer.Length;
            dots[i].BackgroundColor = isFilled ? filled : Colors.Transparent;
        }
    }

    private async Task SubmitAsync()
    {
        if (!Guid.TryParse(DocId, out var docId) || !Guid.TryParse(RecipientId, out var recipientId))
        {
            await DisplayAlertAsync("Eroare", "Lipsesc datele necesare semnării.", "OK");
            ResetBuffer();
            return;
        }

        var pin = _pinBuffer.ToString();
        // Drop the buffer immediately; the local `pin` variable goes out of
        // scope when this method returns. PIN never lands in logs or bindings.
        ResetBuffer();

        SetBusy(true);
        try
        {
            var result = await _orchestrator.SignWithPinAsync(docId, recipientId, pin);
            if (result.PrepareFailed)
            {
                await DisplayAlertAsync("Eroare",
                    "Nu am putut iniția semnarea. Încearcă din nou.", "OK");
                return;
            }

            var sign = result.SignResult!;
            if (sign.Ok && result.SigningRequestId is Guid sigReqId)
            {
                await Shell.Current.GoToAsync($"status?SigningRequestId={sigReqId}");
                return;
            }

            // Only PinRejected burns an attempt — transport / 5xx are free retries.
            // Mock CSC accepts any PIN today, so PinRejected is dead code for the
            // demo; the surface still maps real CSC 401/403 correctly.
            var kind = sign.Error?.Kind ?? SignErrorKind.QtspError;
            if (kind == SignErrorKind.PinRejected)
            {
                _pinRejectedAttempts++;
                if (_pinRejectedAttempts >= LockoutThreshold)
                {
                    await LockoutAsync();
                    return;
                }
                var remaining = LockoutThreshold - _pinRejectedAttempts;
                await DisplayAlertAsync("PIN incorect",
                    $"Mai ai {remaining} încercări înainte de blocare temporară.", "OK");
                return;
            }

            var message = kind switch
            {
                SignErrorKind.Network   => "Eroare de rețea. Verifică conexiunea și reîncearcă.",
                SignErrorKind.Server    => "Server indisponibil. Reîncearcă în câteva momente.",
                SignErrorKind.QtspError => "Eroare la QTSP. Reîncearcă.",
                _                       => "Eroare necunoscută.",
            };
            await DisplayAlertAsync("Semnare eșuată", message, "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PinPage] {ex}");
            await DisplayAlertAsync("Eroare", "Semnarea a eșuat neașteptat.", "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ResetBuffer()
    {
        _pinBuffer.Clear();
        RenderDots();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyOverlay.IsVisible = busy;
    }

    private async Task LockoutAsync()
    {
        _locked = true;
        var remainingSeconds = (int)LockoutDuration.TotalSeconds;
        LockoutLabel.IsVisible = true;

        try
        {
            while (remainingSeconds > 0)
            {
                LockoutLabel.Text = $"PIN blocat. Reîncearcă în {remainingSeconds} s.";
                await Task.Delay(1000);
                remainingSeconds--;
            }
        }
        finally
        {
            LockoutLabel.IsVisible = false;
            _locked = false;
            _pinRejectedAttempts = 0;
        }
    }
}
