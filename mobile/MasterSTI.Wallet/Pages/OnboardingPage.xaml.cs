using MasterSTI.Wallet.Config;
using Microsoft.Maui.Controls;

namespace MasterSTI.Wallet.Pages;

/// <summary>
/// Onboarding — persona-named welcome (Phase 2 of docs/two-wallet-demo-plan.md).
/// Greeting + role pill + hero card reflect the compile-time baked
/// <see cref="WalletPersona"/>. Continue routes to the QR pair flow.
/// </summary>
public sealed partial class OnboardingPage : ContentPage
{
    private const string ShimmerAnimationHandle = "PidShimmer";

    public OnboardingPage()
    {
        InitializeComponent();
        ApplyPersona();
    }

    private void ApplyPersona()
    {
        WelcomeGreetingSpan.Text = $"Bun venit, {WalletPersona.GivenName},";
        WelcomeNameSpan.Text = WalletPersona.FamilyName + ".";
        HeroPosesorLabel.Text = WalletPersona.FullName;
        HeroSerialLabel.Text = $"nr. {WalletPersona.Serial}";

        // Role pill in the top bar — replaces the generic "eIDAS 2.0" chip with
        // the persona's demo role so the operator can spot which APK is which.
        // Manual title-case avoids depending on ICU presence on Android.
        var role = WalletPersona.Role;
        PersonaRolePill.Text = role.Length > 0
            ? char.ToUpperInvariant(role[0]) + role[1..]
            : role;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StartShimmer();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        this.AbortAnimation(ShimmerAnimationHandle);
    }

    private void StartShimmer()
    {
        // Match CSS keyframe `msShine`: translateX -30% → 30%, 4s ease-in-out, infinite.
        // PID card width 256 → ±30% ≈ ±77px.
        // Uses Animation.Commit (driven by platform animator / Android Choreographer)
        // instead of an awaited TranslateToAsync loop — prior implementation pegged the
        // Android UI thread to ~99% and tripped ANR (5 s input-dispatcher timeout).
        const double range = 77;
        const uint periodMs = 4000;

        ShimmerOverlay.TranslationX = -range;
        var parent = new Animation();
        parent.Add(0.0, 0.5, new Animation(v => ShimmerOverlay.TranslationX = v, -range, range, Easing.SinInOut));
        parent.Add(0.5, 1.0, new Animation(v => ShimmerOverlay.TranslationX = v, range, -range, Easing.SinInOut));
        parent.Commit(this, ShimmerAnimationHandle, length: periodMs, repeat: () => true);
    }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        await FinishOnboardingAsync();
    }

    private static async Task FinishOnboardingAsync()
    {
#if ANDROID
        // Skip QRPair on Android emulator: heavy XAML init (QRCoder render +
        // shell page transition + DropdownView template) saturates UI thread
        // beyond the 5s input-dispatcher window. QRPair is demo fluff; the
        // real enrollment happens on BiometricPage. Pass the compile-time
        // persona email so enrollment finds the seeded Recipient.
        var email = Uri.EscapeDataString(Config.WalletPersona.Email);
        await Shell.Current.GoToAsync($"biometric?email={email}");
#else
        await Shell.Current.GoToAsync("qrpair");
#endif
    }
}
