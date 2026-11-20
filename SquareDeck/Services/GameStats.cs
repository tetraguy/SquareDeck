using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquareDeck.Services;

/// <summary>A month of player counts: average and peak.</summary>
public sealed record PlayerMonth(string Month, double Average, int Peak);

/// <summary>A month of user reviews: thumbs up and down.</summary>
public sealed record ReviewMonth(DateTime Month, int Up, int Down);

/// <summary>Everything the game-details "Player stats" section shows. Any part can be missing.</summary>
public sealed class GameStatsData
{
    public int? PlayingNow { get; set; }
    public int? Peak24h { get; set; }
    public int? PeakAllTime { get; set; }
    public string? Owners { get; set; }           // a range, from SteamSpy ("2,000,000 .. 5,000,000")
    public int? PeakYesterday { get; set; }       // SteamSpy's ccu
    public int? AverageForeverMinutes { get; set; }
    public int? MedianForeverMinutes { get; set; }
    public int? Positive { get; set; }
    public int? Negative { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<PlayerMonth> Months { get; set; } = new();
    public List<ReviewMonth> Reviews { get; set; } = new();
}

/// <summary>
/// Player and review statistics for a Steam game, gathered the way SteamDB itself recommends (from Steam directly),
/// plus SteamSpy for owner estimates and SteamCharts for the monthly player history. SteamDB doesn't offer an API
/// and doesn't allow scraping, so its own page (with Twitch stats and more) opens in the built-in browser instead.
/// Results are cached on disk for six hours.
/// </summary>
public static class GameStats
{
    private static readonly HttpClient Http = CreateClient();
    private static string CacheDir => Path.Combine(Settings.DataDir, "stats");

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SquareDeck");
        return c;
    }

    public static string SteamDbUrl(int appId) => $"https://steamdb.info/app/{appId}/charts/";

    public static async Task<GameStatsData> GetAsync(int appId, CancellationToken ct)
    {
        var cache = Path.Combine(CacheDir, appId + ".json");
        try
        {
            if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromHours(6) &&
                JsonSerializer.Deserialize<GameStatsData>(await File.ReadAllTextAsync(cache, ct)) is { } cached)
                return cached;
        }
        catch (Exception) { }

        var data = new GameStatsData();
        await Task.WhenAll(
            Safe(() => CurrentPlayersAsync(appId, data, ct)),
            Safe(() => SteamSpyAsync(appId, data, ct)),
            Safe(() => SteamChartsAsync(appId, data, ct)),
            Safe(() => ReviewHistoryAsync(appId, data, ct)));
        try
        {
            Directory.CreateDirectory(CacheDir);
            await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(data), ct);
        }
        catch (Exception) { }
        return data;
    }

    private static async Task Safe(Func<Task> work)
    {
        try { await work(); }
        catch (Exception) { }
    }

    /// <summary>Players in the game right now (Steam's own count).</summary>
    private static async Task CurrentPlayersAsync(int appId, GameStatsData data, CancellationToken ct)
    {
        var json = await Http.GetStringAsync($"https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={appId}", ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("response", out var r) && r.TryGetProperty("player_count", out var n) && n.TryGetInt32(out int count))
            data.PlayingNow = count;
    }

    /// <summary>Owner estimate, yesterday's peak, average play time, review counts and tags (SteamSpy).</summary>
    private static async Task SteamSpyAsync(int appId, GameStatsData data, CancellationToken ct)
    {
        var json = await Http.GetStringAsync($"https://steamspy.com/api.php?request=appdetails&appid={appId}", ct);
        using var doc = JsonDocument.Parse(json);
        var d = doc.RootElement;
        int? Int(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int x) ? x : null;
        if (d.TryGetProperty("owners", out var o) && o.ValueKind == JsonValueKind.String && o.GetString() is { Length: > 0 } owners && owners != "0 .. 20,000")
            data.Owners = owners.Replace(" .. ", " – ");
        data.PeakYesterday = Int("ccu");
        data.AverageForeverMinutes = Int("average_forever");
        data.MedianForeverMinutes = Int("median_forever");
        data.Positive = Int("positive");
        data.Negative = Int("negative");
        if (d.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
            data.Tags = tags.EnumerateObject().OrderByDescending(t => t.Value.TryGetInt32(out int v) ? v : 0).Take(8).Select(t => t.Name).ToList();
    }

    /// <summary>Now, 24-hour and all-time peaks, and the monthly average / peak table (SteamCharts).</summary>
    private static async Task SteamChartsAsync(int appId, GameStatsData data, CancellationToken ct)
    {
        var html = await Http.GetStringAsync($"https://steamcharts.com/app/{appId}", ct);
        var nums = Regex.Matches(html, @"class=""app-stat"">\s*<span class=""num"">([\d,]+)</span>").Select(m => ParseInt(m.Groups[1].Value)).ToList();
        if (nums.Count >= 3)
        {
            data.PlayingNow ??= nums[0];
            data.Peak24h = nums[1];
            data.PeakAllTime = nums[2];
        }
        var body = Regex.Match(html, @"<tbody>(.*?)</tbody>", RegexOptions.Singleline);
        if (!body.Success) return;
        foreach (Match row in Regex.Matches(body.Groups[1].Value, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline))
        {
            var cells = Regex.Matches(row.Groups[1].Value, @"<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
                .Select(c => Regex.Replace(c.Groups[1].Value, "<.*?>", "").Trim()).ToList();
            if (cells.Count < 5) continue;
            if (!double.TryParse(cells[1].Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double avg)) continue;
            data.Months.Add(new PlayerMonth(cells[0], avg, ParseInt(cells[4]) ?? 0));
            if (data.Months.Count >= 24) break;
        }
    }

    /// <summary>Monthly thumbs up / down from the Steam store's review histogram.</summary>
    private static async Task ReviewHistoryAsync(int appId, GameStatsData data, CancellationToken ct)
    {
        var json = await Http.GetStringAsync($"https://store.steampowered.com/appreviewhistogram/{appId}?l=english&review_score_preference=0", ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || !results.TryGetProperty("rollups", out var rollups)) return;
        foreach (var r in rollups.EnumerateArray())
        {
            if (!r.TryGetProperty("date", out var date) || !date.TryGetInt64(out long unix)) continue;
            int up = r.TryGetProperty("recommendations_up", out var u) && u.TryGetInt32(out int a) ? a : 0;
            int down = r.TryGetProperty("recommendations_down", out var dn) && dn.TryGetInt32(out int b) ? b : 0;
            data.Reviews.Add(new ReviewMonth(DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime, up, down));
        }
    }

    private static int? ParseInt(string s) => int.TryParse(s.Replace(",", "").Trim(), out int n) ? n : null;

    /// <summary>12,345 → "12.3K".</summary>
    public static string Short(double n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000:0.#}M",
        >= 10_000 => $"{n / 1000:0}K",
        >= 1_000 => $"{n / 1000:0.#}K",
        _ => $"{n:0}"
    };
}
