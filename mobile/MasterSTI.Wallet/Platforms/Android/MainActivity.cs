using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using MasterSTI.Wallet.Services;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Plugin.Fingerprint;

namespace MasterSTI.Wallet.Platforms.Android;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges =
        ConfigChanges.ScreenSize |
        ConfigChanges.Orientation |
        ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "verasign",
    DataHost = "sign")]
public sealed class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CrossFingerprint.SetCurrentActivityResolver(() => this);
        CaptureIfDeepLink(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        CaptureIfDeepLink(intent);
    }

    private static void CaptureIfDeepLink(Intent? intent)
    {
        var data = intent?.Data?.ToString();
        if (string.IsNullOrEmpty(data)) return;
        if (!data.StartsWith("verasign://", StringComparison.OrdinalIgnoreCase)) return;

        var router = IPlatformApplication.Current?.Services.GetService<IDeepLinkRouter>();
        router?.Capture(data);

        // Re-route to Inbox so its OnAppearing consumes the pending token.
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
        {
            // While AppLock owns the foreground (#43), don't fight it — unlock
            // routes to //inbox itself and the token is consumed there.
            if (AppLockState.IsLocked) return;
            try
            {
                if (Shell.Current is not null)
                    await Shell.Current.GoToAsync("inbox");
            }
            catch { /* shell not ready yet — OnCreate path picks it up via NavigateToStartPageAsync */ }
        });
    }
}
