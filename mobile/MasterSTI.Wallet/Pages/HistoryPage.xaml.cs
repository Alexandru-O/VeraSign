using MasterSTI.Wallet.Controls;
using MasterSTI.Wallet.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Pages;

/// <summary>
/// History page. Reads <c>/api/wallet/history</c> on appear and renders one
/// row per <c>SignedDocument</c> tied to the wallet user's PID email
/// (cross-organisation). Empty list yields the empty-state label.
/// </summary>
public sealed partial class HistoryPage : ContentPage
{
    private readonly IWalletApiClient _api;

    public HistoryPage(IWalletApiClient api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        IReadOnlyList<WalletHistoryItem> items;
        try
        {
            items = await _api.GetHistoryAsync();
        }
        catch (Exception)
        {
            items = Array.Empty<WalletHistoryItem>();
        }

        HistoryList.Children.Clear();

        if (items.Count == 0)
        {
            HistoryList.Children.Add(BuildEmptyState());
            return;
        }

        foreach (var it in items)
        {
            HistoryList.Children.Add(BuildRow(it));
        }
    }

    private static Color Res(string lightKey, string darkKey)
    {
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        var key = theme == AppTheme.Dark ? darkKey : lightKey;
        return (Color)Application.Current!.Resources[key];
    }

    private static View BuildEmptyState()
    {
        return new Label
        {
            Text = "Niciun document semnat încă",
            FontSize = 13,
            TextColor = Res("FgMutedLight", "FgMutedDark"),
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(24, 32),
        };
    }

    private static View BuildRow(WalletHistoryItem item)
    {
        var border = new Border
        {
            BackgroundColor = Res("BgElevLight", "BgElevDark"),
            Stroke = Res("BorderLight", "BorderDark"),
            StrokeThickness = 1,
            Padding = new Thickness(14),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
        };

        var grid = new Grid
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

        // Green status dot (8 px circle, success color)
        var dot = new Ellipse
        {
            WidthRequest = 8,
            HeightRequest = 8,
            Fill = new SolidColorBrush(Res("SuccessLight", "SuccessDark")),
            VerticalOptions = LayoutOptions.Center,
        };
        Grid.SetColumn(dot, 0);
        grid.Children.Add(dot);

        var textStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        textStack.Children.Add(new Label
        {
            Text = item.DocumentName,
            FontSize = 13,
            FontFamily = "GeistSemiBold",
            TextColor = Res("FgLight", "FgDark"),
        });
        textStack.Children.Add(new Label
        {
            Text = FormatSubtitle(item),
            FontSize = 11,
            FontFamily = "GeistMono",
            TextColor = Res("FgSubtleLight", "FgSubtleDark"),
        });
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        var sigLevel = new SigLevelView { Level = item.Level, Size = "sm" };
        Grid.SetColumn(sigLevel, 2);
        grid.Children.Add(sigLevel);

        border.Content = grid;
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await Application.Current!.Windows[0].Page!.DisplayAlertAsync(
                    item.DocumentName,
                    $"{FormatSubtitle(item)}\nNivel: {item.Level}",
                    "Închide");
            })
        });
        return border;
    }

    private static string FormatSubtitle(WalletHistoryItem item)
    {
        var local = item.SignedAtUtc.ToLocalTime();
        return $"{item.SenderName} · {local:dd MMM · HH:mm}";
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("inbox");
    }
}
