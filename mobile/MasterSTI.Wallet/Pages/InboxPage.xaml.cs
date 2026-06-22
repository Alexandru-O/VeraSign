using MasterSTI.Wallet.Config;
using MasterSTI.Wallet.Services;
using Microsoft.Maui.Controls;

namespace MasterSTI.Wallet.Pages;

public sealed partial class InboxPage : ContentPage
{
    private const int PollIntervalSeconds = 30;

    private readonly IWalletApiClient _api;
    private readonly IDeepLinkRouter _deepLinks;
    private CancellationTokenSource? _pollCts;

    public InboxPage(IWalletApiClient api, IDeepLinkRouter deepLinks)
    {
        InitializeComponent();
        _api = api;
        _deepLinks = deepLinks;
        AvatarLabel.Text = ResolveAvatarInitial();
    }

    /// <summary>
    /// Prefer the disclosed PID GivenName when the wallet is enrolled; fall back
    /// to the compile-time persona (always present, since the APK refuses to build
    /// without one). Uppercased so casing of the underlying name does not leak.
    /// </summary>
    private static string ResolveAvatarInitial()
    {
        string? sdJwt = null;
        try { sdJwt = SecureStorage.GetAsync("wallet.sdjwt").GetAwaiter().GetResult(); }
        catch { /* SecureStorage may throw on first access — treat as no PID. */ }

        if (!string.IsNullOrEmpty(sdJwt))
        {
            try
            {
                var parsed = SdJwtParser.Parse(sdJwt);
                var given = parsed.GivenName?.Trim();
                if (!string.IsNullOrEmpty(given))
                    return given[..1].ToUpperInvariant();
            }
            catch (SdJwtFormatException) { /* fall through */ }
        }

        var personaGiven = WalletPersona.GivenName?.Trim();
        return string.IsNullOrEmpty(personaGiven)
            ? "?"
            : personaGiven[..1].ToUpperInvariant();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // A deep link captured while the app was backgrounded routes us straight
        // to ReviewPage. Inbox refresh still runs underneath so it's up to date
        // when the user returns.
        if (_deepLinks.PendingDocumentId is { } docId)
        {
            var token = _deepLinks.PendingHandoffToken ?? string.Empty;
            var rid = _deepLinks.PendingRecipientId;
            _deepLinks.Consume();
            var query = $"review?docId={docId}&token={Uri.EscapeDataString(token)}";
            if (rid is Guid r) query += $"&recipientId={r}";
            await Shell.Current.GoToAsync(query);
            return;
        }

        _pollCts = new CancellationTokenSource();
        _ = RunPollLoopAsync(_pollCts.Token);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _pollCts?.Cancel();
        _pollCts = null;
    }

    private async Task RunPollLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await RefreshAsync(cancellationToken);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), cancellationToken);
                }
                catch (TaskCanceledException) { return; }
            }
        }
        catch (Exception)
        {
            // Polling failure is non-fatal; the next OnAppearing will try again.
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var result = await _api.GetInboxAsync(cancellationToken);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!result.Ok)
            {
                // Login or fetch failed — distinct error state, NOT an empty inbox.
                ErrorPanel.IsVisible = true;
                InboxList.IsVisible = false;
                StraplineLabel.Text = "Conexiune eșuată";
                return;
            }

            ErrorPanel.IsVisible = false;
            InboxList.IsVisible = true;
            InboxList.ItemsSource = result.Items;
            StraplineLabel.Text = result.Items.Count switch
            {
                0 => "Nicio cerere activă",
                1 => "1 document în așteptare",
                _ => $"{result.Items.Count} documente în așteptare"
            };
        });
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        // Retry re-runs login + fetch. The poll loop continues underneath.
        try
        {
            await RefreshAsync(_pollCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException) { /* page left — ignore */ }
    }

    private async void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is WalletInboxItem item)
        {
            await Shell.Current.GoToAsync($"review?docId={item.DocumentId}&recipientId={item.RecipientId}");
        }
    }

    private async void OnIdentitiesTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("identities");
    }

    private async void OnSeeAllHistoryTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("history");
    }
}
