using System.Globalization;
using MasterSTI.Wallet.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Pages;

[QueryProperty(nameof(SignedDocumentId), "signedDocId")]
public sealed partial class DonePage : ContentPage
{
    private readonly IWalletApiClient _api;
    private const string Placeholder = "—";

    public DonePage(IWalletApiClient api)
    {
        _api = api;
        InitializeComponent();
        BuildSummary(info: null);
    }

    /// <summary>
    /// Passed as <c>?signedDocId=…</c> on the route. Empty when the demo flow
    /// reaches Done without a real signed-document handle, in which case the
    /// page renders placeholder rows instead of inventing data.
    /// </summary>
    public string SignedDocumentId { get; set; } = string.Empty;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        CheckCircle.Scale = 0.4;
        CheckCircle.Opacity = 0;
        _ = Task.WhenAll(
            CheckCircle.FadeToAsync(1, 200),
            CheckCircle.ScaleToAsync(1.0, 560, Easing.SpringOut));

        SignedDocInfo? info = null;
        if (Guid.TryParse(SignedDocumentId, out var id) && id != Guid.Empty)
        {
            info = await _api.GetSignedDocInfoAsync(id);
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            IdPillLabel.Text = info is null
                ? "ID: —"
                : $"ID: ms_sig_{info.TxnId[..Math.Min(8, info.TxnId.Length)]}";
            BuildSummary(info);
        });
    }

    private void BuildSummary(SignedDocInfo? info)
    {
        SummaryHost.Children.Clear();
        // "Nivel cerut" is the legal level the sender requested on the Recipient
        // (QES/AdES/SES). "Profil PAdES" is the technical baseline actually
        // embedded (B-T/B-LT/B-LTA) — divergence is meaningful (e.g., requested
        // QES landed as B-T means LTV didn't attach).
        var rows = new (string Label, string Value, string ValueColorKey, bool Mono)[]
        {
            ("Semnat la",             FormatDate(info?.SignedAtUtc),                 "N800",      false),
            ("Nivel cerut",           FormatRequestedLevel(info?.RequestedLevel),    "Gold600",   false),
            ("Profil PAdES",          info?.Level ?? Placeholder,                    "N800",      false),
            ("Serviciu de încredere", info?.TspName ?? Placeholder,                  "N800",      false),
            ("Certificat",            info?.SubjectCn ?? Placeholder,                "N800",      true),
            ("Serie certificat",      FormatSerial(info?.CertificateSerial),         "RoBlue500", true),
            ("Marca temporală",       FormatDate(info?.TsaTime),                     "N800",      false),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            SummaryHost.Children.Add(BuildRow(
                rows[i].Label, rows[i].Value, rows[i].ValueColorKey,
                isFirst: i == 0, useMonoForValue: rows[i].Mono));
        }
    }

    private static string FormatDate(DateTime? value) => value is null
        ? Placeholder
        : value.Value.ToLocalTime().ToString("HH:mm · dd MMM yyyy", CultureInfo.GetCultureInfo("ro-RO"));

    /// <summary>
    /// Highlights QES as ★-prefixed since it's the qualified-electronic-signature
    /// tier (eIDAS Annex IV). AdES/SES passed through unchanged so the user sees
    /// the literal Recipient.Level value.
    /// </summary>
    private static string FormatRequestedLevel(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return Placeholder;
        return string.Equals(requested, "QES", StringComparison.OrdinalIgnoreCase)
            ? "★ Calificată (QES)"
            : requested;
    }

    /// <summary>
    /// X.509 serials are uppercase hex; group bytes with colons for readability
    /// (matches the ValidationReportPage convention). Long serials get
    /// ellipsised at the head so the suffix (most-unique bits) stays visible.
    /// </summary>
    private static string FormatSerial(string? serialHex)
    {
        if (string.IsNullOrWhiteSpace(serialHex)) return Placeholder;
        var clean = serialHex.Replace(":", string.Empty);
        if (clean.Length % 2 == 1) clean = "0" + clean;
        var pairs = new List<string>(clean.Length / 2);
        for (var i = 0; i < clean.Length; i += 2) pairs.Add(clean.Substring(i, 2));
        var joined = string.Join(':', pairs);
        return joined.Length <= 35 ? joined : "…" + joined[^35..];
    }

    private static View BuildRow(string label, string value, string valueColorKey, bool isFirst, bool useMonoForValue)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            Padding = new Thickness(0, 6),
        };

        if (!isFirst)
        {
            var hairline = new BoxView
            {
                Color = (Color)Application.Current!.Resources["N100"],
                HeightRequest = 1,
                VerticalOptions = LayoutOptions.Start,
                HorizontalOptions = LayoutOptions.Fill,
            };
            Grid.SetColumn(hairline, 0);
            Grid.SetColumnSpan(hairline, 2);
            grid.Children.Add(hairline);
        }

        var labelView = new Label
        {
            Text = label,
            FontSize = 12,
            TextColor = (Color)Application.Current!.Resources["Fg3"],
            VerticalTextAlignment = TextAlignment.Center,
        };
        Grid.SetColumn(labelView, 0);
        grid.Children.Add(labelView);

        var valueView = new Label
        {
            Text = value,
            FontSize = useMonoForValue ? 11 : 12,
            FontFamily = useMonoForValue ? "JetBrainsMono" : "Inter",
            TextColor = (Color)Application.Current!.Resources[valueColorKey],
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
        };
        Grid.SetColumn(valueView, 1);
        grid.Children.Add(valueView);

        return grid;
    }

    private async void OnHomeClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("inbox");
    }

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        await Application.Current!.Windows[0].Page!.DisplayAlertAsync(
            "Distribuie",
            "Funcționalitatea de partajare va fi disponibilă în curând.",
            "OK");
    }
}
