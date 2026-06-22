namespace MasterSTI.Wallet;

public sealed partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register named routes for programmatic navigation. New prototype-driven flow:
        //   onboarding -> qrpair -> biometric -> inbox -> review -> consent -> status -> done
        // Side flows: history, identities. Existing security-critical pages preserved.
        Routing.RegisterRoute("onboarding",  typeof(Pages.OnboardingPage));
        Routing.RegisterRoute("applock",     typeof(Pages.AppLockPage));
        Routing.RegisterRoute("qrpair",      typeof(Pages.QRPairPage));
        Routing.RegisterRoute("biometric",   typeof(Pages.BiometricPage));
        Routing.RegisterRoute("pinsetup",    typeof(Pages.PinSetupPage));
        Routing.RegisterRoute("inbox",       typeof(Pages.InboxPage));
        Routing.RegisterRoute("review",      typeof(Pages.ReviewPage));
        Routing.RegisterRoute("done",        typeof(Pages.DonePage));
        Routing.RegisterRoute("history",     typeof(Pages.HistoryPage));
        Routing.RegisterRoute("identities",  typeof(Pages.IdentitiesPage));

        // Existing security-critical pages with real EUDIW/StrongBox wiring
        Routing.RegisterRoute("enrollment", typeof(Pages.EnrollmentPage));
        Routing.RegisterRoute("scan",       typeof(Pages.ScanPage));
        Routing.RegisterRoute("consent",    typeof(Pages.ConsentPage));
        Routing.RegisterRoute("pin",        typeof(Pages.PinPage));
        Routing.RegisterRoute("status",     typeof(Pages.StatusPage));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await NavigateToStartPageAsync();
    }

    /// <summary>
    /// Cold-start routing: enrolled wallets must pass the AppLock biometric gate
    /// before reaching the Inbox; fresh installs see the onboarding flow (which
    /// is the default ShellContent).
    /// </summary>
    private static async Task NavigateToStartPageAsync()
    {
        var enrolled = await App.IsEnrolledAsync();

        if (enrolled)
        {
            Services.AppLockState.IsLocked = true;
            await Shell.Current.GoToAsync("applock");
        }
        // Otherwise the default ShellContent (OnboardingPage) is already shown.
    }
}
