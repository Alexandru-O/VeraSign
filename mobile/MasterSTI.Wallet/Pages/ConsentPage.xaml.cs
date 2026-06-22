using MasterSTI.Shared.DTOs.Wallet;
using MasterSTI.Wallet.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace MasterSTI.Wallet.Pages;

/// <summary>
/// M6 — Consent screen for an incoming openid4vp:// request, plus the static
/// "review then sign" flow used in the dissertation demo. Existing OID4VP
/// wiring (PresentationBuilder + PresentationClient) is preserved.
/// </summary>
public sealed partial class ConsentPage : ContentPage, IQueryAttributable
{
    private readonly OpenId4VpParser _parser;
    private readonly PresentationBuilder _presentationBuilder;
    private readonly PresentationClient _presentationClient;
    private readonly IWalletApiClient _api;
    private readonly IPendingSignContext _pendingSign;

    private OpenId4VpRequest? _request;
    private Guid? _recipientId;
    private Guid? _documentId;
    private string? _pinnedHash;

    public ConsentPage(
        OpenId4VpParser parser,
        PresentationBuilder presentationBuilder,
        PresentationClient presentationClient,
        IWalletApiClient api,
        IPendingSignContext pendingSign)
    {
        InitializeComponent();
        _parser = parser;
        _presentationBuilder = presentationBuilder;
        _presentationClient = presentationClient;
        _api = api;
        _pendingSign = pendingSign;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Request", out var reqObj) && reqObj is OpenId4VpRequest request)
        {
            _request = request;
            MainThread.BeginInvokeOnMainThread(() => PopulateFromOid4VpRequest(request));
        }

        if (query.TryGetValue("recipientId", out var ridObj) && Guid.TryParse(ridObj?.ToString(), out var rid))
            _recipientId = rid;
        if (query.TryGetValue("docId", out var didObj) && Guid.TryParse(didObj?.ToString(), out var did))
            _documentId = did;
        if (query.TryGetValue("hash", out var hashObj))
            _pinnedHash = hashObj?.ToString();

        // PID disclosure happens at Login; QES_CSC sign releases SAD only —
        // no new claims for the verifier. Card is OID4VP-scan only.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (ClaimsCard is not null) ClaimsCard.IsVisible = _request is not null;
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // OID4VP scan path populates labels via PopulateFromOid4VpRequest.
        if (_request is not null || _recipientId is not Guid rid) return;

        var meta = await _api.GetReviewMetaAsync(rid);
        if (meta is null)
        {
            await Task.Delay(500);
            meta = await _api.GetReviewMetaAsync(rid);
        }
        if (meta is null) return; // "—" placeholders stay visible.
        ApplyMeta(meta);
    }

    private void ApplyMeta(WalletInboxItemMetaDto meta)
    {
        // "Locație de păstrare" row stays as XAML literal "QSCD calificat (HSM)"
        // — only QES_CSC is reachable today (AdES_Wallet / SES throw NotImpl in
        // SigningLevelDispatcher.Resolve). Wire a Level→location switch when
        // those branches land.
        var name = string.IsNullOrWhiteSpace(meta.DocumentName) ? "—" : meta.DocumentName;
        DocBodySpan.Text = name;
        DocumentLabel.Text = name;
        RequesterLabel.Text = string.IsNullOrWhiteSpace(meta.SenderName) ? "—" : meta.SenderName;
        LevelView.Level = string.IsNullOrWhiteSpace(meta.Level) ? "QES" : meta.Level;
    }

    private void PopulateFromOid4VpRequest(OpenId4VpRequest request)
    {
        RequesterLabel.Text = request.ClientId;

        // Inject real claim list — fall back to the placeholder if the parser
        // can't extract paths.
        var claims = _parser.ExtractClaimPaths(request.PresentationDefinitionJson);
        ClaimsHost.Children.Clear();
        if (claims.Count == 0)
        {
            ClaimsHost.Children.Add(BuildClaimRow(
                "Atribute determinate la prezentare", "—", true, true));
            return;
        }

        for (var i = 0; i < claims.Count; i++)
        {
            ClaimsHost.Children.Add(BuildClaimRow(claims[i], "—", true, i == 0));
        }
    }

    private static View BuildClaimRow(string label, string value, bool on, bool isFirst)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 10,
            Padding = new Thickness(0, 8),
        };

        if (!isFirst)
        {
            grid.Margin = new Thickness(0, 0, 0, 0);
            // Top hairline
            grid.Children.Add(new BoxView
            {
                Color = (Color)Application.Current!.Resources["N100"],
                HeightRequest = 1,
                VerticalOptions = LayoutOptions.Start,
                HorizontalOptions = LayoutOptions.Fill,
            });
            Grid.SetColumn((BindableObject)grid.Children[^1], 0);
            Grid.SetColumnSpan((BindableObject)grid.Children[^1], 3);
        }

        var icon = new Controls.IconView
        {
            Name = on ? "check" : "x",
            Size = 14,
            TintColor = (Color)Application.Current!.Resources[on ? "Success500" : "Fg4"],
            VerticalOptions = LayoutOptions.Center,
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var labelView = new Label
        {
            Text = label,
            FontSize = 13,
            TextColor = (Color)Application.Current!.Resources["Fg2"],
            VerticalTextAlignment = TextAlignment.Center,
        };
        Grid.SetColumn(labelView, 1);
        grid.Children.Add(labelView);

        var valueView = new Label
        {
            Text = value,
            FontSize = 13,
            FontFamily = "InterMedium",
            TextColor = (Color)Application.Current!.Resources[on ? "N800" : "Fg4"],
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.End,
        };
        Grid.SetColumn(valueView, 2);
        grid.Children.Add(valueView);

        return grid;
    }

    private async void OnApproveClicked(object? sender, EventArgs e)
    {
        ApproveButton.IsBusy = true;
        RejectButton.IsBusy = true;

        try
        {
            // WYSIWYS — re-verify the Document Hash pinned on ReviewPage against
            // a fresh inbox-meta fetch BEFORE the biometric prompt and any CSC
            // call. Mismatch means the document changed after review: abort.
            // Intentionally Document Hash, not DTBS Hash — see ADR-0004.
            if (!string.IsNullOrEmpty(_pinnedHash) && _recipientId is Guid rid0)
            {
                var meta = await _api.GetReviewMetaAsync(rid0);
                if (meta is null || !string.Equals(meta.Hash, _pinnedHash, StringComparison.OrdinalIgnoreCase))
                {
                    await DisplayAlertAsync("Document modificat",
                        "Documentul a fost modificat înainte de semnare.", "OK");
                    await Shell.Current.GoToAsync("inbox");
                    return;
                }
            }

            // Real OID4VP path (QR-scan flow) — preserved as-is. Uses Plugin.Fingerprint
            // with allowAlternativeAuthentication=true since OID4VP presentation is
            // not the qualified signing flow.
            if (_request is not null)
            {
                var available = await CrossFingerprint.Current.IsAvailableAsync(allowAlternativeAuthentication: true);
                if (available)
                {
                    var authResult = await CrossFingerprint.Current.AuthenticateAsync(
                        new AuthenticationRequestConfiguration(
                            "Confirmă semnarea",
                            $"Confirmă partajarea credențialului cu {_request.ClientId}"));
                    if (!authResult.Authenticated)
                        return;
                }

                var vpToken = await _presentationBuilder.BuildAsync(_request);
                if (vpToken is null)
                {
                    await DisplayAlertAsync("Eroare", "Construirea prezentării a eșuat. Portofelul este înrolat?", "OK");
                    return;
                }

                var (success, error) = await _presentationClient.SendAsync(_request, vpToken);
                if (!success)
                {
                    await DisplayAlertAsync("Eroare", $"Verificatorul a respins prezentarea:\n{error}", "OK");
                    return;
                }

                await Shell.Current.GoToAsync("status");
                return;
            }

            // Inbox path (real signing). Requires docId + recipientId so the
            // orchestrator can drive Prepare → Sign against the API.
            if (_documentId is not Guid docId || _recipientId is not Guid rid)
            {
                await DisplayAlertAsync("Eroare", "Lipsesc datele necesare semnării.", "OK");
                return;
            }

            // Strict biometric gate per ADR-0007: only Fingerprint factor qualifies
            // for the bio-attested SAD path. Face / Device-PIN / None all route to
            // PinPage fallback so the user enters PIN explicitly. CSC v2 §11.5
            // expects an explicit credential-authorisation factor; we don't treat
            // weaker biometrics as equivalent.
            var authType = await CrossFingerprint.Current.GetAuthenticationTypeAsync();
            if (authType != AuthenticationType.Fingerprint)
            {
                var goToPin = await DisplayActionSheetAsync(
                    "Amprentă neactivată",
                    "Anulează",
                    null,
                    "Folosește PIN");
                if (goToPin == "Folosește PIN")
                    await NavigateToPinAsync(docId, rid);
                return;
            }

            var bioResult = await CrossFingerprint.Current.AuthenticateAsync(
                new AuthenticationRequestConfiguration(
                    "Confirmă semnarea",
                    "Confirmă semnarea documentului cu amprenta."));
            if (!bioResult.Authenticated)
            {
                // User declined biometric → offer PIN fallback. Same screen path
                // as "no fingerprint hardware" so the recovery story is uniform.
                var goToPin = await DisplayActionSheetAsync(
                    "Biometric refuzat",
                    "Anulează",
                    null,
                    "Folosește PIN");
                if (goToPin == "Folosește PIN")
                    await NavigateToPinAsync(docId, rid);
                return;
            }

            // Hand off to StatusPage immediately so the user sees the progress
            // ring while Prepare + Sign run in the background. Keeps the
            // ConsentPage from blocking on the slow CSC + PAdES embed roundtrip.
            _pendingSign.SetBiometric(docId, rid);
            await Shell.Current.GoToAsync("status");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ConsentPage] {ex}");
            await DisplayAlertAsync("Eroare", ex.Message, "OK");
        }
        finally
        {
            ApproveButton.IsBusy = false;
            RejectButton.IsBusy = false;
        }
    }

    private static Task NavigateToPinAsync(Guid docId, Guid recipientId)
    {
        // PinPage owns its own Prepare → Sign round-trip on submit; orchestrator
        // accepts the docId + recipientId again so a retry creates a fresh
        // SigningRequest if the previous one is Failed.
        return Shell.Current.GoToAsync($"pin?docId={docId}&recipientId={recipientId}");
    }

    private async void OnRejectClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("inbox");
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
