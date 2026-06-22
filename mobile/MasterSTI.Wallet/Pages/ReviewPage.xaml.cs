using System.Security.Cryptography;
using MasterSTI.Shared.DTOs.Wallet;
using MasterSTI.Wallet.Models;
using MasterSTI.Wallet.Services;

namespace MasterSTI.Wallet.Pages;

public sealed partial class ReviewPage : ContentPage, IQueryAttributable
{
    private const int MaxPreviewPages = 100;

    private readonly IWalletApiClient _api;
    private readonly IPdfPageRenderer _pdfRenderer;
    private readonly IRenderCommitmentCarrier _renderCommitments;

    private Guid? _recipientId;
    private Guid? _docId;
    private string? _handoffToken;
    private bool _loaded;
    private IPdfDocumentSession? _session;
    private string? _expectedHash;
    private string? _verifiedHash;
    private CancellationTokenSource? _pageCts;

    public ReviewPage(
        IWalletApiClient api,
        IPdfPageRenderer pdfRenderer,
        IRenderCommitmentCarrier renderCommitments)
    {
        InitializeComponent();
        _api = api;
        _pdfRenderer = pdfRenderer;
        _renderCommitments = renderCommitments;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("recipientId", out var ridObj) && Guid.TryParse(ridObj?.ToString(), out var rid))
            _recipientId = rid;
        if (query.TryGetValue("docId", out var didObj) && Guid.TryParse(didObj?.ToString(), out var did))
            _docId = did;
        if (query.TryGetValue("token", out var tokObj))
            _handoffToken = tokObj?.ToString();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _pageCts?.Cancel();
        _pageCts = new CancellationTokenSource();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_recipientId is not Guid rid)
        {
            await DisplayAlertAsync("Cerere invalidă", "Lipsește identificatorul destinatarului.", "OK");
            await Shell.Current.GoToAsync("inbox");
            return;
        }

        try
        {
            var meta = await _api.GetReviewMetaAsync(rid);
            if (meta is null)
            {
                await DisplayAlertAsync("Cerere indisponibilă",
                    "Documentul nu mai este disponibil pentru semnare.", "OK");
                await Shell.Current.GoToAsync("inbox");
                return;
            }

            ApplyMeta(meta);
            _loaded = true;

            if (_docId is Guid did)
            {
                _ = LoadPagesAsync(did);
                // ADR-0008: precompute Render Commitment in the background so
                // it's already in the carrier by the time the user taps
                // "Continuă spre semnare". Soft-fail: null result leaves the
                // carrier empty and PrepareSigning ships without /VeraSign.*.
                _ = PrefetchRenderCommitmentAsync(did, _pageCts?.Token ?? CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReviewPage] {ex}");
            await DisplayAlertAsync("Eroare", "Nu am putut încărca detaliile documentului.", "OK");
            await Shell.Current.GoToAsync("inbox");
        }
    }

    private async Task LoadPagesAsync(Guid documentId)
    {
        try
        {
            var pdfBytes = await _api.DownloadDocumentAsync(documentId);
            if (pdfBytes is null || pdfBytes.Length == 0)
            {
                HidePageLoading();
                return;
            }

            var computedHash = Convert.ToHexString(SHA256.HashData(pdfBytes));
            if (!string.Equals(computedHash, _expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                HidePageLoading();
                _session?.Dispose();
                _session = null;
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlertAsync("Documentul a fost modificat",
                        "Reveniți la inbox și încercați din nou.", "OK");
                    await Shell.Current.GoToAsync("inbox");
                });
                return;
            }
            _verifiedHash = computedHash;

            var session = await _pdfRenderer.OpenAsync(pdfBytes);
            if (session is null)
            {
                HidePageLoading();
                return;
            }
            _session = session;

            if (session.PageCount > MaxPreviewPages)
            {
                MainThread.BeginInvokeOnMainThread(() => BigDocBanner.IsVisible = true);
                HidePageLoading();
                return;
            }

            var items = new List<PdfPageItem>(session.PageCount);
            for (var i = 0; i < session.PageCount; i++)
                items.Add(new PdfPageItem(session, i, session.PageCount));

            MainThread.BeginInvokeOnMainThread(() => PagesView.ItemsSource = items);
            HidePageLoading();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReviewPage] PDF render failed: {ex}");
            HidePageLoading();
        }
    }

    private void HidePageLoading() => MainThread.BeginInvokeOnMainThread(() =>
    {
        PageLoadingIndicator.IsRunning = false;
        PageLoadingIndicator.IsVisible = false;
    });

    private void OnPageItemBound(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is PdfPageItem item)
            _ = item.EnsureRenderedAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _pageCts?.Cancel();
        PagesView.ItemsSource = null;
        _session?.Dispose();
        _session = null;
    }

    private async Task PrefetchRenderCommitmentAsync(Guid documentId, CancellationToken ct)
    {
        try
        {
            if (_renderCommitments.Get(documentId) is not null)
                return; // already cached from a prior ReviewPage visit
            var locale = System.Globalization.CultureInfo.CurrentCulture.Name;
            var commitment = await _api.GetRenderCommitmentAsync(documentId, locale, ct);
            if (commitment is not null && !ct.IsCancellationRequested)
                _renderCommitments.Set(documentId, commitment);
        }
        catch (OperationCanceledException) { /* page navigated away */ }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReviewPage] Render commitment prefetch failed: {ex.Message}");
        }
    }

    private void ApplyMeta(WalletInboxItemMetaDto meta)
    {
        _expectedHash = meta.Hash;
        HeaderTitle.Text = string.IsNullOrWhiteSpace(meta.DocumentName) ? "Document" : meta.DocumentName;
        SenderLabel.Text = string.IsNullOrWhiteSpace(meta.SenderName) ? "—" : meta.SenderName;
        LevelView.Level = string.IsNullOrWhiteSpace(meta.Level) ? "QES" : meta.Level;
        PagesLabel.Text = meta.Pages > 0 ? meta.Pages.ToString() : "—";
        HashLabel.Text = FormatHash(meta.Hash);
        SizeLabel.Text = FormatSize(meta.SizeBytes);
    }

    private static string FormatHash(string? hash)
    {
        if (string.IsNullOrEmpty(hash) || hash.Length < 12) return "—";
        return $"{hash[..4]} · {hash[4..8]} · {hash[8..12]}".ToUpperInvariant();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "—";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024):0.##} MB";
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("inbox");
    }

    private async void OnRejectClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("inbox");
    }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        var args = new List<string>();
        if (_recipientId is Guid rid) args.Add($"recipientId={rid}");
        if (_docId is Guid did) args.Add($"docId={did}");
        if (!string.IsNullOrEmpty(_handoffToken)) args.Add($"token={Uri.EscapeDataString(_handoffToken)}");
        if (!string.IsNullOrEmpty(_verifiedHash)) args.Add($"hash={_verifiedHash}");
        var query = args.Count > 0 ? "?" + string.Join("&", args) : string.Empty;
        await Shell.Current.GoToAsync("consent" + query);
    }
}
