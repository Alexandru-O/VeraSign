namespace MasterSTI.Wallet;

public sealed partial class App : Application
{
    private const string ThemePrefKey = "wallet.theme";

    public App()
    {
        InitializeComponent();

        // Restore persisted theme choice. 0 = Unspecified (system), 1 = Light, 2 = Dark.
        var stored = Preferences.Get(ThemePrefKey, (int)AppTheme.Unspecified);
        if (Enum.IsDefined(typeof(AppTheme), stored))
        {
            UserAppTheme = (AppTheme)stored;
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());

        // AppLock re-lock policy (#43): every time an enrolled wallet returns
        // from the background it must pass AppLock again before authenticated
        // content is exposed. Cold-start locking is handled by AppShell.
        window.Resumed += OnWindowResumed;
        return window;
    }

    private async void OnWindowResumed(object? sender, EventArgs e)
    {
        // Skip if not enrolled (fresh install → Onboarding) or already locked
        // (cold start, or a prior resume not yet unlocked — don't stack screens).
        if (Services.AppLockState.IsLocked) return;
        if (!await IsEnrolledAsync()) return;

        Services.AppLockState.IsLocked = true;
        try
        {
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("applock");
        }
        catch
        {
            // Shell not ready — AppShell.OnAppearing will lock on the cold path.
        }
    }

    /// <summary>
    /// Cycles theme Light -> Dark -> Light. Persists choice via Preferences.
    /// Used by ThemeToggleView.
    /// </summary>
    public static void ToggleTheme()
    {
        var app = (App)Application.Current!;
        var next = app.UserAppTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        app.UserAppTheme = next;
        Preferences.Set(ThemePrefKey, (int)next);
    }

    /// <summary>
    /// Checks whether the wallet has been enrolled (SD-JWT stored in SecureStorage).
    /// Called from AppShell after the navigation root is ready.
    /// </summary>
    public static async Task<bool> IsEnrolledAsync()
    {
        try
        {
            var sdjwt = await SecureStorage.GetAsync("wallet.sdjwt");
            return !string.IsNullOrEmpty(sdjwt);
        }
        catch
        {
            // SecureStorage can throw on first access on some Android configurations.
            return false;
        }
    }
}
