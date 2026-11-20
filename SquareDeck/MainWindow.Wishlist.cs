using System.Windows.Threading;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>Wishlist prices and release dates: checked every few hours, with a notification for sales and release days.</summary>
public partial class MainWindow
{
    public PriceWatch PriceWatch { get; } = PriceWatch.Load();
    private DispatcherTimer? _priceTimer;
    private bool _checkingPrices;

    /// <summary>Raised after prices have been checked (the wishlist page refreshes).</summary>
    public event Action? PricesChecked;

    private void StartPriceWatch()
    {
        _priceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        _priceTimer.Tick += async (_, _) =>
        {
            _priceTimer.Interval = TimeSpan.FromHours(6);
            if (Settings.SaleAlerts || Settings.ReleaseAlerts) await CheckPricesAsync(manual: false);
        };
        _priceTimer.Start();
    }

    /// <summary>Checks the wishlist's prices now. <paramref name="manual"/>: everything, not just the oldest few.</summary>
    public async Task CheckPricesAsync(bool manual)
    {
        if (_checkingPrices) return;
        _checkingPrices = true;
        try
        {
            var owned = AllGames.Where(g => g.IsSteam && !g.OnWishlist).Select(g => g.AppId)
                .Concat(NotInstalledGames.Select(g => g.AppId)).ToHashSet();
            string key = Settings.SteamWebApiKey;
            int min = Settings.SaleAlertMin;
            var alerts = await Task.Run(() => PriceWatch.RefreshAsync(key, min, owned, CancellationToken.None, manual ? 200 : 80));
            foreach (var alert in alerts.Take(4))
            {
                var g = alert.Game;
                if (alert.Kind == "sale" && Settings.SaleAlerts)
                    ShowToast("On sale", $"{g.Name} is {g.Discount}% off",
                        $"{g.Price}" + (g.RegularPrice.Length > 0 && g.RegularPrice != g.Price ? $" (was {g.RegularPrice})" : "") +
                        (g.LowestCents > 0 && g.PriceCents <= g.LowestCents ? "  ·  the lowest price seen" : ""), g.HeaderUrl, "info");
                else if (alert.Kind == "released" && Settings.ReleaseAlerts)
                    ShowToast("Out now", g.Name + " is out", "It's on your wishlist." + (g.Price.Length > 0 ? " " + g.Price + "." : ""), g.HeaderUrl, "info");
            }
            if (alerts.Count > 4 && Settings.SaleAlerts)
                ShowToast("On sale", $"{alerts.Count - 4} more on your wishlist", "See them in Stores > Wishlist and sales.", "", "info");
        }
        catch (Exception) { }
        finally
        {
            _checkingPrices = false;
            PricesChecked?.Invoke();
        }
    }
}
