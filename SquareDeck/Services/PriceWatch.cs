using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>A game whose Steam price and release date SquareDeck keeps an eye on.</summary>
public sealed class WatchedGame
{
    public int AppId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>"wishlist" (from the Steam wishlist) or "watch" (added in SquareDeck).</summary>
    public string Source { get; set; } = "watch";
    public DateTime Added { get; set; } = DateTime.Now;
    public string Price { get; set; } = "";
    public string RegularPrice { get; set; } = "";
    public int PriceCents { get; set; } = -1;
    public int RegularCents { get; set; } = -1;
    public int LowestCents { get; set; } = -1;
    public string LowestPrice { get; set; } = "";
    public int Discount { get; set; }
    public bool Free { get; set; }
    public string ReleaseText { get; set; } = "";
    public bool ComingSoon { get; set; }
    /// <summary>The exact release day, when Steam gives one.</summary>
    public DateTime? ReleaseDay { get; set; }
    /// <summary>The month (first day of it), when only the month or quarter is known.</summary>
    public DateTime? ReleaseMonth { get; set; }
    public string Header { get; set; } = "";
    public DateTime CheckedAt { get; set; }
    /// <summary>The discount already announced, so a sale is only announced once (and again if it gets deeper).</summary>
    public int AlertedDiscount { get; set; }
    public bool AlertedRelease { get; set; }

    public string HeaderUrl => Header.Length > 0 ? Header : $"https://cdn.akamai.steamstatic.com/steam/apps/{AppId}/header.jpg";
    public bool OnSale => Discount > 0;
}

/// <summary>
/// Your Steam wishlist plus games you choose to watch, with their prices and release dates from the Steam store.
/// Checked every few hours in the background; a sale (or a deeper one) and a release day raise a notification.
/// Kept in pricewatch.json in the data folder.
/// </summary>
public sealed class PriceWatch
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static string FilePath => Path.Combine(Settings.DataDir, "pricewatch.json");

    public List<WatchedGame> Games { get; set; } = new();
    public DateTime CheckedAt { get; set; }
    public string WishlistState { get; set; } = ""; // "", "ok", "private", "none"

    public static PriceWatch Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<PriceWatch>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception) { }
        return new();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception) { }
    }

    public WatchedGame? Find(int appId) => Games.FirstOrDefault(g => g.AppId == appId);

    public WatchedGame Watch(int appId, string name)
    {
        var existing = Find(appId);
        if (existing != null) return existing;
        var game = new WatchedGame { AppId = appId, Name = name, Source = "watch", Added = DateTime.Now };
        Games.Add(game);
        Save();
        return game;
    }

    public void Unwatch(int appId)
    {
        Games.RemoveAll(g => g.AppId == appId && g.Source == "watch");
        Save();
    }

    /// <summary>What a refresh found worth telling you about.</summary>
    public sealed record Alert(WatchedGame Game, string Kind); // "sale" or "released"

    /// <summary>
    /// Brings the list up to date: the Steam wishlist's games (added and removed), then prices and release dates for
    /// the ones checked longest ago. Returns the sales and releases to announce.
    /// </summary>
    public async Task<List<Alert>> RefreshAsync(string apiKey, int minDiscount, HashSet<int> owned, CancellationToken ct, int max = 80)
    {
        var alerts = new List<Alert>();
        var ids = await SteamWishlist.GetIdsAsync(apiKey, ct).ConfigureAwait(false);
        if (ids != null)
        {
            WishlistState = "ok";
            var wished = ids.Select(i => i.AppId).ToHashSet();
            // Off the wishlist (or bought): gone, unless you also chose to watch it here.
            Games.RemoveAll(g => g.Source == "wishlist" && !wished.Contains(g.AppId));
            foreach (var (appId, added) in ids)
            {
                var g = Find(appId);
                if (g == null) Games.Add(new WatchedGame { AppId = appId, Source = "wishlist", Added = added });
                else if (g.Source == "watch") g.Source = "wishlist";
            }
        }
        else WishlistState = SteamLibrary.GetSteamId64() == null ? "none" : "private";
        Games.RemoveAll(g => owned.Contains(g.AppId));

        var due = Games.OrderBy(g => g.Name.Length == 0 ? 0 : 1).ThenBy(g => g.CheckedAt).Take(max).ToList();
        using var gate = new SemaphoreSlim(3);
        await Task.WhenAll(due.Select(async g =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var json = await Http.GetStringAsync(
                    $"https://store.steampowered.com/api/appdetails?appids={g.AppId}&filters=basic,price_overview,release_date&l=english", ct).ConfigureAwait(false);
                Apply(g, json);
                await Task.Delay(250, ct).ConfigureAwait(false); // the store limits how quickly it may be asked
            }
            catch (Exception) when (!ct.IsCancellationRequested) { }
            finally { gate.Release(); }
        })).ConfigureAwait(false);

        foreach (var g in due)
        {
            if (g.Discount <= 0) g.AlertedDiscount = 0;
            else if (g.Discount >= Math.Max(1, minDiscount) && g.Discount > g.AlertedDiscount)
            {
                g.AlertedDiscount = g.Discount;
                alerts.Add(new Alert(g, "sale"));
            }
            if (g.ReleaseDay is { } day && day.Date <= DateTime.Today && day.Date >= DateTime.Today.AddDays(-2) && !g.AlertedRelease)
            {
                g.AlertedRelease = true;
                alerts.Add(new Alert(g, "released"));
            }
        }
        CheckedAt = DateTime.Now;
        Save();
        return alerts;
    }

    /// <summary>Just one game's price and date (after adding it).</summary>
    public async Task RefreshOneAsync(WatchedGame g, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync(
                $"https://store.steampowered.com/api/appdetails?appids={g.AppId}&filters=basic,price_overview,release_date&l=english", ct).ConfigureAwait(false);
            Apply(g, json);
            if (g.Discount > 0) g.AlertedDiscount = g.Discount; // already on sale when added: nothing to announce
            Save();
        }
        catch (Exception) when (!ct.IsCancellationRequested) { }
    }

    private static void Apply(WatchedGame g, string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(g.AppId.ToString(), out var app) || !app.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object) return;
        if (data.TryGetProperty("name", out var n)) g.Name = n.GetString() ?? g.Name;
        if (data.TryGetProperty("header_image", out var h)) g.Header = h.GetString() ?? "";
        g.Free = data.TryGetProperty("is_free", out var free) && free.ValueKind == JsonValueKind.True;
        if (data.TryGetProperty("price_overview", out var p))
        {
            g.Price = p.TryGetProperty("final_formatted", out var f) ? f.GetString() ?? "" : "";
            g.RegularPrice = p.TryGetProperty("initial_formatted", out var i) ? i.GetString() ?? "" : "";
            g.PriceCents = p.TryGetProperty("final", out var fc) && fc.TryGetInt32(out int c1) ? c1 : -1;
            g.RegularCents = p.TryGetProperty("initial", out var ic) && ic.TryGetInt32(out int c2) ? c2 : -1;
            g.Discount = p.TryGetProperty("discount_percent", out var d) && d.TryGetInt32(out int dp) ? dp : 0;
            if (g.RegularPrice.Length == 0) g.RegularPrice = g.Price;
            if (g.PriceCents > 0 && (g.LowestCents < 0 || g.PriceCents < g.LowestCents)) { g.LowestCents = g.PriceCents; g.LowestPrice = g.Price; }
        }
        else
        {
            g.Price = g.Free ? "Free" : "";
            g.Discount = 0;
            g.PriceCents = -1;
        }
        if (data.TryGetProperty("release_date", out var rd))
        {
            g.ComingSoon = rd.TryGetProperty("coming_soon", out var soon) && soon.ValueKind == JsonValueKind.True;
            g.ReleaseText = rd.TryGetProperty("date", out var date) ? date.GetString() ?? "" : "";
            (g.ReleaseDay, g.ReleaseMonth) = ParseRelease(g.ReleaseText);
            if (!g.ComingSoon && g.Price.Length == 0 && !g.Free) g.Price = "";
            if (g.ComingSoon && g.Price.Length == 0) g.Price = "Coming soon";
        }
        g.CheckedAt = DateTime.Now;
    }

    /// <summary>
    /// Steam's release dates come as "Mar 21, 2027", "21 Mar, 2027", "March 2027", "Q2 2027", "2027" or words like
    /// "Coming soon". Gives the exact day when there is one, else the month (a quarter's first month).
    /// </summary>
    public static (DateTime? Day, DateTime? Month) ParseRelease(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var us = CultureInfo.GetCultureInfo("en-US");
        string[] dayFormats = { "MMM d, yyyy", "d MMM, yyyy", "MMMM d, yyyy", "d MMMM, yyyy", "MMM d yyyy", "d MMM yyyy", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(text.Trim(), dayFormats, us, DateTimeStyles.AllowWhiteSpaces, out var day)) return (day.Date, new DateTime(day.Year, day.Month, 1));
        string[] monthFormats = { "MMMM yyyy", "MMM yyyy", "MMMM, yyyy", "MMM, yyyy" };
        if (DateTime.TryParseExact(text.Trim(), monthFormats, us, DateTimeStyles.AllowWhiteSpaces, out var month)) return (null, new DateTime(month.Year, month.Month, 1));
        var q = System.Text.RegularExpressions.Regex.Match(text, @"Q([1-4])\s*(\d{4})");
        if (q.Success) return (null, new DateTime(int.Parse(q.Groups[2].Value), (int.Parse(q.Groups[1].Value) - 1) * 3 + 1, 1));
        return (null, null);
    }
}
