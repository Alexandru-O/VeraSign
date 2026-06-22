using Microsoft.Extensions.Logging;
using MasterSTI.Wallet.Services;
using ZXing.Net.Maui.Controls;

#if ANDROID
using MasterSTI.Wallet.Platforms.Android.Services;
#endif

namespace MasterSTI.Wallet;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseBarcodeReader()   // ZXing.Net.Maui initialisation
            .ConfigureFonts(fonts =>
            {
                // VeraSign brand fonts v2 — Geist + Geist Mono.
                // Drop matching TTFs into Resources/Fonts/ (see README.md).
                fonts.AddFont("Geist-Regular.ttf",  "Geist");
                fonts.AddFont("Geist-Medium.ttf",   "GeistMedium");
                fonts.AddFont("Geist-SemiBold.ttf", "GeistSemiBold");
                fonts.AddFont("Geist-Bold.ttf",     "GeistBold");

                fonts.AddFont("GeistMono-Regular.ttf", "GeistMono");
                fonts.AddFont("GeistMono-Medium.ttf",  "GeistMonoMedium");

                // Default-template fallback aliases.
                fonts.AddFont("Geist-Regular.ttf",  "OpenSansRegular");
                fonts.AddFont("Geist-SemiBold.ttf", "OpenSansSemibold");
            });

        // Platform device-key service
#if ANDROID
        builder.Services.AddSingleton<IDeviceKeyService, AndroidDeviceKeyService>();
        builder.Services.AddSingleton<IPdfPageRenderer, AndroidPdfPageRenderer>();
#else
        builder.Services.AddSingleton<IPdfPageRenderer, NullPdfPageRenderer>();
#endif

        // Config singleton — defaults point at Android emulator loopback (10.0.2.2)
        builder.Services.AddSingleton<WalletConfig>();

        // Shared HttpClient with dev-cert bypass in DEBUG builds.
        // Registered as singleton so all services share one connection pool.
        // Release path: see docs/adr/0009-wallet-tls-pinning.md — pinning is
        // deferred for the prototype; cleartext is blocked by
        // Platforms/Android/Resources/xml/network_security_config.xml.
        builder.Services.AddSingleton<HttpClient>(_ =>
        {
#if DEBUG
            var handler = new HttpClientHandler
            {
                // EMULATOR-ONLY: bypass dev certificate validation.
                // Remove or gate on a build flag before deploying to a real device.
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
#else
#warning ADR-0009: wallet TLS pinning deferred; Release relies on Android system CA store. Revisit before production deploy (see docs/adr/0009-wallet-tls-pinning.md).
            return new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
#endif
        });

        // Business-logic services
        builder.Services.AddSingleton<EnrollmentService>();
        builder.Services.AddSingleton<IPinService, PinService>();
        builder.Services.AddSingleton<OpenId4VpParser>();
        builder.Services.AddSingleton<PresentationBuilder>();
        builder.Services.AddSingleton<PresentationClient>();
        builder.Services.AddSingleton<IWalletApiClient, WalletApiClient>();
        builder.Services.AddTransient<IWalletSigningOrchestrator, WalletSigningOrchestrator>();
        builder.Services.AddSingleton<IPendingSignContext, PendingSignContext>();
        builder.Services.AddSingleton<IRenderCommitmentCarrier, RenderCommitmentCarrier>();
        builder.Services.AddSingleton<IDeepLinkRouter, DeepLinkRouter>();

        // Pages
        builder.Services.AddTransient<Pages.EnrollmentPage>();
        builder.Services.AddTransient<Pages.ScanPage>();
        builder.Services.AddTransient<Pages.ConsentPage>();
        builder.Services.AddTransient<Pages.PinPage>();
        builder.Services.AddTransient<Pages.StatusPage>();
        builder.Services.AddTransient<Pages.OnboardingPage>();
        builder.Services.AddTransient<Pages.AppLockPage>();
        builder.Services.AddTransient<Pages.QRPairPage>();
        builder.Services.AddTransient<Pages.BiometricPage>();
        builder.Services.AddTransient<Pages.PinSetupPage>();
        builder.Services.AddTransient<Pages.InboxPage>();
        builder.Services.AddTransient<Pages.ReviewPage>();
        builder.Services.AddTransient<Pages.DonePage>();
        builder.Services.AddTransient<Pages.HistoryPage>();
        builder.Services.AddTransient<Pages.IdentitiesPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
