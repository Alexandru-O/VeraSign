using System.Globalization;
using MasterSTI.Wallet.Config;
using MasterSTI.Wallet.Controls;
using MasterSTI.Wallet.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Pages;

/// <summary>
/// Identities ("Wallet") page. Mirrors prototype MWallet — PID card driven by
/// the real disclosed SD-JWT claims (via <see cref="SdJwtParser"/>) + dashed
/// "Adaugă identitate" CTA. Non-PID credentials show "În curând" so the wallet
/// does not lie about credentials it does not hold.
/// </summary>
public sealed partial class IdentitiesPage : ContentPage
{
    public IdentitiesPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await BuildCardsAsync();
    }

    private record IdentityCard(string Title, string Issuer, string Valid, string Glyph, string PillTone, string PillText);

    private async Task BuildCardsAsync()
    {
        IdentitiesHost.Children.Clear();

        string? sdJwt = null;
        try
        {
            sdJwt = await SecureStorage.GetAsync("wallet.sdjwt");
        }
        catch
        {
            // SecureStorage occasionally throws on first access — treat as "no PID".
        }

        ParsedSdJwt? parsed = null;
        if (!string.IsNullOrEmpty(sdJwt))
        {
            try
            {
                parsed = SdJwtParser.Parse(sdJwt);
            }
            catch (SdJwtFormatException)
            {
                // Stored SD-JWT is unparseable — fall back to "neînrolat" so the UI
                // does not assert a credential the wallet cannot present.
                parsed = null;
            }
        }

        var pidTitle = parsed is { FamilyName: not null } or { GivenName: not null }
            ? $"PID · {Trim(parsed.GivenName)} {Trim(parsed.FamilyName)}".TrimEnd()
            : $"PID · {WalletPersona.FullName}";

        string pidValid;
        string pidPillTone;
        string pidPillText;
        if (parsed is not null)
        {
            pidValid = parsed.Exp is { } expValue
                ? $"activ · {expValue.LocalDateTime.ToString("d MMM yyyy", new CultureInfo("ro-RO"))}"
                : "activ";
            pidPillTone = "success";
            pidPillText = "Valid";
        }
        else
        {
            pidValid = "neînrolat";
            pidPillTone = "warning";
            pidPillText = "Inactiv";
        }

        var cards = new List<IdentityCard>
        {
            new(pidTitle, "MAI · DGEPI", pidValid, "user", pidPillTone, pidPillText),
            new("Card sănătate",       "CNAS",   "În curând", "shield", "neutral", "În curând"),
            new("Permis de conducere", "DRPCIV", "În curând", "doc",    "neutral", "În curând"),
        };

        // Pre-enrollment: show the baked-in persona label so the operator can
        // confirm which APK is loaded. Post-enrollment: surface PID active state.
        SubtitleLabel.Text = parsed is not null
            ? $"{cards.Count} identități · PID activ"
            : $"{WalletPersona.DisplayLabel} · PID neînrolat";

        foreach (var c in cards)
        {
            IdentitiesHost.Children.Add(BuildCard(c));
        }
    }

    private static string Trim(string? s) => s?.Trim() ?? string.Empty;

    private static Color Res(string lightKey, string darkKey)
    {
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        var key = theme == AppTheme.Dark ? darkKey : lightKey;
        return (Color)Application.Current!.Resources[key];
    }

    private static View BuildCard(IdentityCard card)
    {
        // Card design from MWallet: row with icon tile + title/issuer + Valid pill,
        // then a mono "Valabil până la …" subtitle.
        var border = new Border
        {
            BackgroundColor = Res("BgElevLight", "BgElevDark"),
            Stroke = Res("BorderLight", "BorderDark"),
            StrokeThickness = 1,
            Padding = new Thickness(16),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
        };

        var outer = new VerticalStackLayout { Spacing = 10 };

        var topRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 12,
            VerticalOptions = LayoutOptions.Center,
        };

        var iconHolder = new Border
        {
            BackgroundColor = Res("BgSunkenLight", "BgSunkenDark"),
            StrokeThickness = 0,
            WidthRequest = 36,
            HeightRequest = 36,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new IconView
            {
                Name = card.Glyph,
                Size = 16,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            },
        };
        Grid.SetColumn(iconHolder, 0);
        topRow.Children.Add(iconHolder);

        var titleStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        titleStack.Children.Add(new Label
        {
            Text = card.Title,
            FontSize = 14,
            FontFamily = "GeistSemiBold",
            TextColor = Res("FgLight", "FgDark"),
        });
        titleStack.Children.Add(new Label
        {
            Text = card.Issuer,
            FontSize = 12,
            TextColor = Res("FgMutedLight", "FgMutedDark"),
        });
        Grid.SetColumn(titleStack, 1);
        topRow.Children.Add(titleStack);

        var validPill = new PillView
        {
            Tone = card.PillTone,
            Dot = string.Equals(card.PillTone, "success", StringComparison.Ordinal),
            Text = card.PillText,
            VerticalOptions = LayoutOptions.Center,
        };
        Grid.SetColumn(validPill, 2);
        topRow.Children.Add(validPill);

        outer.Children.Add(topRow);
        outer.Children.Add(new Label
        {
            Text = card.Valid.StartsWith("activ", StringComparison.Ordinal)
                ? $"Valabil până la {card.Valid}"
                : card.Valid,
            FontSize = 11,
            FontFamily = "GeistMono",
            TextColor = Res("FgSubtleLight", "FgSubtleDark"),
        });

        border.Content = outer;
        return border;
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("inbox");
    }
}
