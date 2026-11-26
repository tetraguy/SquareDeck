using System.Globalization;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>
/// Data for the Switch 2 style Passport, Backlog Boss, Mood picker, Leaderboard and Free games (experimental
/// "Switch 2 style play pages"): more stores and deals, Epic's next free games, more stamps, and the little logs the
/// pages keep (the boss's health by day, your place on the leaderboard by day, games you've claimed).
/// </summary>
public static partial class Labs
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ================================================================ free games, everywhere

    /// <summary>
    /// A free offer: a game to keep, or loot / DLC. <paramref name="Kind"/> is "game" or "loot"; <paramref name="Starts"/>
    /// is set for ones that haven't started yet (Epic's next free games).
    /// </summary>
    public sealed record Offer(string Id, string Title, string Store, string Kind, string Worth, string Url, DateTime? Ends, string Image,
                               string Description, DateTime? Starts = null, DateTime? Added = null, int Claimers = 0);

    /// <summary>The stores free games come from, in the order they're listed, with a short name for the left side.</summary>
    public static readonly (string Store, string Short, string Glyph)[] OfferStores =
    {
        ("Epic Games Store", "Epic Games", "\uE7FC"), ("Steam", "Steam", "\uE7FC"), ("Amazon Prime Gaming", "Amazon Prime", "\uE719"),
        ("GOG", "GOG", "\uE8F1"), ("Ubisoft Connect", "Ubisoft", "\uE7FC"), ("EA app", "EA", "\uE7FC"), ("Battle.net", "Battle.net", "\uE7FC"),
        ("itch.io", "itch.io", "\uE8F1"), ("DRM-free", "DRM-free", "\uE8F1"), ("Other", "Other stores", "\uE719"),
    };

    private static string StoreOf(string platforms)
    {
        bool Has(string s) => platforms.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (Has("Epic")) return "Epic Games Store";
        if (Has("Prime") || Has("Amazon") || Has("Luna")) return "Amazon Prime Gaming";
        if (Has("GOG")) return "GOG";
        if (Has("Steam")) return "Steam";
        if (Has("Ubisoft") || Has("Uplay")) return "Ubisoft Connect";
        if (Has("Origin") || Has("EA ")) return "EA app";
        if (Has("Battle")) return "Battle.net";
        if (Has("itch")) return "itch.io";
        if (Has("DRM")) return "DRM-free";
        return "Other";
    }

    /// <summary>
    /// Every free game and free loot on PC stores (Epic, Steam, Amazon Prime Gaming, GOG, Ubisoft, EA, Battle.net,
    /// itch.io, DRM-free), from GamerPower's public list, plus Epic's own list of this week's and next week's free games.
    /// Null if nothing could be loaded at all.
    /// </summary>
    public static async Task<List<Offer>?> AllFreeAsync(CancellationToken ct)
    {
        var gamer = GamerPowerAsync(ct);
        var epic = EpicFreeAsync(ct);
        await Task.WhenAll(gamer, epic);
        if (gamer.Result == null && epic.Result == null) return null;
        var list = new List<Offer>(gamer.Result ?? new());
        // Epic's own list adds next week's games and fills in any GamerPower hasn't picked up yet.
        foreach (var e in epic.Result ?? new())
        {
            bool known = list.Any(o => o.Store == e.Store && SameTitle(o.Title, e.Title));
            if (e.Starts != null || !known) list.Add(e);
        }
        return list;
    }

    private static async Task<List<Offer>?> GamerPowerAsync(CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync("https://www.gamerpower.com/api/filter?platform=epic-games-store.steam.prime-gaming.gog.ubisoft.origin.battlenet.itchio.drm-free&type=game.loot", ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<Offer>();
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var g in doc.RootElement.EnumerateArray())
            {
                string S(string name) => g.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : "" : "";
                if (S("status").Length > 0 && !S("status").Equals("Active", StringComparison.OrdinalIgnoreCase)) continue;
                string type = S("type").ToLowerInvariant();
                string kind = type.Contains("game") ? "game" : "loot";
                DateTime? ends = DateTime.TryParse(S("end_date"), Inv, DateTimeStyles.None, out var e) ? e : null;
                DateTime? added = DateTime.TryParse(S("published_date"), Inv, DateTimeStyles.None, out var a) ? a : null;
                string title = S("title");
                foreach (var cut in new[] { " Giveaway", " (Epic Games)", " (Steam)", " (GOG)", " (Prime Gaming)", " (Amazon Prime)", " (IndieGala)", " (itch.io)" })
                    title = title.Replace(cut, "", StringComparison.OrdinalIgnoreCase);
                int users = int.TryParse(S("users"), NumberStyles.Integer, Inv, out var u) ? u : 0;
                string worth = S("worth");
                if (worth.Equals("N/A", StringComparison.OrdinalIgnoreCase)) worth = "";
                list.Add(new Offer("gp:" + S("id"), title.Trim(), StoreOf(S("platforms")), kind, worth,
                    S("open_giveaway_url").Length > 0 ? S("open_giveaway_url") : S("gamerpower_url"), ends, S("image").Length > 0 ? S("image") : S("thumbnail"),
                    S("description"), null, added, users));
            }
            return list;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Epic's free games: this week's, and next week's (with the day they start).</summary>
    private static async Task<List<Offer>?> EpicFreeAsync(CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync("https://store-site-backend-static-ipv4.ak.epicgames.com/freeGamesPromotions?locale=en-US&country=US&allowCountries=US", ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<Offer>();
            if (!doc.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("Catalog", out var cat) ||
                !cat.TryGetProperty("searchStore", out var store) || !store.TryGetProperty("elements", out var elements)) return list;
            foreach (var el in elements.EnumerateArray())
            {
                string S(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                if (!el.TryGetProperty("promotions", out var promos) || promos.ValueKind != JsonValueKind.Object) continue;
                // A free one: a promotional offer at 0% of the price (a 100% discount).
                (DateTime Start, DateTime End)? Free(string which)
                {
                    if (!promos.TryGetProperty(which, out var groups) || groups.ValueKind != JsonValueKind.Array) return null;
                    foreach (var grp in groups.EnumerateArray())
                        if (grp.TryGetProperty("promotionalOffers", out var offers))
                            foreach (var o in offers.EnumerateArray())
                            {
                                int pct = o.TryGetProperty("discountSetting", out var d) && d.TryGetProperty("discountPercentage", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : -1;
                                if (pct != 0) continue;
                                if (DateTime.TryParse(S(o, "startDate"), Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var st) &&
                                    DateTime.TryParse(S(o, "endDate"), Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var en))
                                    return (st.ToLocalTime(), en.ToLocalTime());
                            }
                    return null;
                }
                var now = Free("promotionalOffers");
                var next = Free("upcomingPromotionalOffers");
                if (now == null && next == null) continue;
                string slug = "";
                if (el.TryGetProperty("catalogNs", out var ns) && ns.TryGetProperty("mappings", out var maps) && maps.ValueKind == JsonValueKind.Array)
                    foreach (var m in maps.EnumerateArray()) { slug = S(m, "pageSlug"); if (slug.Length > 0) break; }
                if (slug.Length == 0 && el.TryGetProperty("offerMappings", out var om) && om.ValueKind == JsonValueKind.Array)
                    foreach (var m in om.EnumerateArray()) { slug = S(m, "pageSlug"); if (slug.Length > 0) break; }
                if (slug.Length == 0) slug = S(el, "productSlug").Split('/')[0];
                string image = "";
                if (el.TryGetProperty("keyImages", out var imgs))
                    foreach (var want in new[] { "OfferImageWide", "DieselStoreFrontWide", "Thumbnail", "OfferImageTall" })
                    {
                        foreach (var i in imgs.EnumerateArray()) if (S(i, "type") == want) { image = S(i, "url"); break; }
                        if (image.Length > 0) break;
                    }
                string worth = "";
                if (el.TryGetProperty("price", out var price) && price.TryGetProperty("totalPrice", out var tp) && tp.TryGetProperty("fmtPrice", out var fmt))
                    worth = S(fmt, "originalPrice");
                if (worth is "0" or "$0.00") worth = "";
                string url = slug.Length > 0 ? "https://store.epicgames.com/en-US/p/" + slug : "https://store.epicgames.com/en-US/free-games";
                var when = now ?? next!.Value;
                list.Add(new Offer("epic:" + S(el, "id"), S(el, "title"), "Epic Games Store", "game", worth, url, when.End, image, S(el, "description"),
                    now == null ? when.Start : null, now?.Start));
            }
            return list;
        }
        catch (Exception) { return null; }
    }

    private static string SimpleTitle(string title) =>
        new string(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static bool SameTitle(string a, string b) => SimpleTitle(a).Length > 0 && SimpleTitle(a) == SimpleTitle(b);

    /// <summary>A game in the library with the same name as an offer (to say "You already have it").</summary>
    public static Game? Owned(string title, IEnumerable<Game> library)
    {
        var s = SimpleTitle(title);
        if (s.Length < 3) return null;
        return library.FirstOrDefault(g => g.IsGame && SimpleTitle(g.Name) == s);
    }

    public static string WhenStarts(DateTime starts)
    {
        var d = starts - DateTime.Now;
        if (d <= TimeSpan.Zero) return "Free now";
        return d.TotalDays >= 2 ? $"Free from {starts:dddd d MMM}" : d.TotalHours >= 2 ? $"Free in {Math.Floor(d.TotalHours):0} hours" : "Free very soon";
    }

    /// <summary>Total worth of what's free right now, from the prices the offers give (in dollars).</summary>
    public static double WorthOf(IEnumerable<Offer> offers) =>
        offers.Sum(o => double.TryParse(o.Worth.Replace("$", "").Replace(",", "").Trim(), NumberStyles.Float, Inv, out var v) ? v : 0);

    // Claimed games and offers already seen, kept as lists of ids.
    private static HashSet<string> IdSet(string key) => Get(key).Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    private static void PutIdSet(string key, IEnumerable<string> ids) => Put(key, string.Join("|", ids.TakeLast(400)));

    public static bool IsClaimed(Offer o) => IdSet("free.claimed").Contains(o.Id);

    public static void SetClaimed(Offer o, bool claimed)
    {
        var set = IdSet("free.claimed");
        if (claimed) set.Add(o.Id); else set.Remove(o.Id);
        PutIdSet("free.claimed", set);
    }

    /// <summary>Which offers are new since the page was last opened (and remembers this visit's).</summary>
    public static HashSet<string> TakeNewOffers(IEnumerable<Offer> offers)
    {
        var seen = IdSet("free.seen");
        bool first = seen.Count == 0;
        var fresh = offers.Where(o => !seen.Contains(o.Id)).Select(o => o.Id).ToHashSet();
        PutIdSet("free.seen", seen.Concat(fresh));
        return first ? new() : fresh;
    }

    // ================================================================ deals (CheapShark)

    public sealed record Deal(string Id, string Title, string StoreId, double Sale, double Normal, int Savings, double Rating, string Thumb,
                              int SteamAppId, int Metacritic, string SteamRating, int SteamPercent, DateTime? Released)
    {
        public string Url => "https://www.cheapshark.com/redirect?dealID=" + Uri.EscapeDataString(Uri.UnescapeDataString(Id));
    }

    public sealed record DealStore(string Id, string Name, string Icon);

    private static List<DealStore>? _dealStores;

    /// <summary>The stores CheapShark follows (Steam, GOG, Humble, Fanatical, Green Man Gaming, Epic and more).</summary>
    public static async Task<List<DealStore>> DealStoresAsync(CancellationToken ct)
    {
        if (_dealStores != null) return _dealStores;
        try
        {
            var json = await Http.GetStringAsync("https://www.cheapshark.com/api/1.0/stores", ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<DealStore>();
            foreach (var s in doc.RootElement.EnumerateArray())
            {
                bool active = s.TryGetProperty("isActive", out var a) && (a.ValueKind == JsonValueKind.Number ? a.GetInt32() == 1 : a.ValueKind == JsonValueKind.True);
                if (!active) continue;
                string icon = s.TryGetProperty("images", out var im) && im.TryGetProperty("icon", out var ic) ? "https://www.cheapshark.com" + ic.GetString() : "";
                list.Add(new DealStore(s.GetProperty("storeID").GetString() ?? "", s.GetProperty("storeName").GetString() ?? "", icon));
            }
            return _dealStores = list;
        }
        catch (Exception) { return new(); }
    }

    /// <summary>
    /// Game deals on PC stores from CheapShark's public list. <paramref name="query"/> is added to the address
    /// (sortBy, storeID, upperPrice, AAA, steamAppID…). Null when it couldn't be loaded.
    /// </summary>
    public static async Task<List<Deal>?> DealsAsync(string query, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync("https://www.cheapshark.com/api/1.0/deals?onSale=1&pageSize=60&" + query, ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<Deal>();
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var d in doc.RootElement.EnumerateArray())
            {
                string S(string name) => d.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : "" : "";
                double N(string name) => double.TryParse(S(name), NumberStyles.Float, Inv, out var x) ? x : 0;
                long released = (long)N("releaseDate");
                list.Add(new Deal(S("dealID"), S("title"), S("storeID"), N("salePrice"), N("normalPrice"), (int)Math.Round(N("savings")), N("dealRating"),
                    S("thumb"), (int)N("steamAppID"), (int)N("metacriticScore"), S("steamRatingText"), (int)N("steamRatingPercent"),
                    released > 0 ? DateTimeOffset.FromUnixTimeSeconds(released).LocalDateTime : null));
            }
            // The same game on several stores: the cheapest only.
            return list.GroupBy(x => SimpleTitle(x.Title)).Select(g => g.OrderBy(x => x.Sale).First()).ToList();
        }
        catch (Exception) { return null; }
    }

    public static string Money(double dollars) => dollars <= 0 ? "Free" : "$" + dollars.ToString("0.00", Inv);

    // ================================================================ daily logs

    /// <summary>A small log of one number a day (the boss's health, your leaderboard place), the last 120 days.</summary>
    public static List<(DateTime Day, double Value, double Extra)> DailyLog(string key)
    {
        var list = new List<(DateTime, double, double)>();
        foreach (var part in Get(key).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split(',');
            if (bits.Length >= 2 && DateTime.TryParseExact(bits[0], "yyyy-MM-dd", Inv, DateTimeStyles.None, out var day) &&
                double.TryParse(bits[1], NumberStyles.Float, Inv, out var v))
                list.Add((day, v, bits.Length > 2 && double.TryParse(bits[2], NumberStyles.Float, Inv, out var x) ? x : 0));
        }
        return list.OrderBy(x => x.Item1).ToList();
    }

    /// <summary>Writes today's value (replacing an earlier one today).</summary>
    public static void LogToday(string key, double value, double extra = 0)
    {
        var list = DailyLog(key).Where(x => x.Day != DateTime.Today).ToList();
        list.Add((DateTime.Today, value, extra));
        Put(key, string.Join(";", list.TakeLast(120).Select(x => $"{x.Day:yyyy-MM-dd},{x.Value.ToString("0.##", Inv)},{x.Extra.ToString("0.##", Inv)}")));
    }

    // ================================================================ play pace

    /// <summary>Hours a week you've played lately (the last four weeks, from SquareDeck's play time).</summary>
    public static double HoursPerWeek(PlaytimeStore playtime)
    {
        double secs = 0;
        for (int d = 0; d < 28; d++) secs += playtime.SecondsOn(DateTime.Today.AddDays(-d));
        return secs / 3600 / 4;
    }

    public static double HoursOf(Game g, PlaytimeStore playtime) =>
        Math.Max(playtime.TotalSeconds(g.Key) / 3600, (g.IsSteam ? SteamLibrary.GetPlaytimeMinutes(g.AppId) ?? 0 : 0) / 60.0);

    // ================================================================ more stamps

    /// <summary>A stamp with how close you are to it (0 to 1) and an icon.</summary>
    public sealed record StampPlus(string Group, string Name, bool Earned, string Detail, double Progress, string Glyph, Game? Art = null);

    /// <summary>
    /// The passport's stamps: genres, decades, stores and firsts as before, plus hours, finished games, achievements,
    /// streaks, variety, library size, and the time of day and week you play.
    /// </summary>
    public static List<StampPlus> PassportPlus(IEnumerable<Game> library, PlaytimeStore playtime, AchievementStore achievements, LibraryStore notes)
    {
        var lib = library.ToList();
        var stamps = Passport(lib, playtime, achievements, notes)
            .Select(s => new StampPlus(s.Group, s.Name, s.Earned, s.Detail, s.Earned ? 1 : 0,
                s.Group switch { "Genres" => "\uE7FC", "Decades" => "\uE787", "Stores" => "\uE719", _ => "\uE734" }, s.Art)).ToList();
        var games = lib.Where(g => g.IsGame).ToList();
        double totalHours = playtime.Seconds.Values.Sum(d => d.Values.Sum()) / 3600;
        int finished = games.Count(g => notes.Find(g.Key)?.Status == "Finished");
        int unlocked = achievements.Feed.Count;
        int streak = playtime.LongestStreak().Days;
        int variety = games.Count(g => HoursOf(g, playtime) > 0.5);

        void Tiered(string group, string glyph, double have, string unit, params (int Need, string Name)[] tiers)
        {
            foreach (var (need, name) in tiers)
                stamps.Add(new StampPlus(group, name, have >= need, have >= need ? $"{need:N0} {unit}" : $"{Math.Floor(have):N0} of {need:N0} {unit}", Math.Clamp(have / need, 0, 1), glyph));
        }
        Tiered("Hours", "\uE916", totalHours, "hours with SquareDeck", (10, "Getting started"), (50, "Regular"), (100, "Centurion"), (250, "Devoted"), (500, "Veteran"), (1000, "Legend"));
        Tiered("Finisher", "\uE73E", finished, "games finished", (1, "First credits"), (5, "Closer"), (10, "Completionist"), (25, "Credits collector"), (50, "Backlog slayer"));
        Tiered("Achievements", "\uE7EF", unlocked, "achievements", (10, "Bronze"), (50, "Silver"), (250, "Gold"), (1000, "Platinum"));
        Tiered("Streaks", "\uE945", streak, "days in a row", (3, "Hat trick"), (7, "A whole week"), (14, "Fortnight"), (30, "Month-long"));
        Tiered("Variety", "\uE8B9", variety, "games played", (5, "Sampler"), (10, "Explorer"), (25, "Wanderer"), (50, "World traveler"));
        Tiered("Collector", "\uE8F1", games.Count, "games in your library", (25, "Shelf"), (50, "Cabinet"), (100, "Collection"), (250, "Archive"), (500, "Museum"));

        // The time of day and week.
        var sessions = playtime.Sessions;
        SessionEntry? First(Func<SessionEntry, bool> when) => sessions.FirstOrDefault(when);
        void Moment(string name, string how, Func<SessionEntry, bool> when, string glyph)
        {
            var s = First(when);
            stamps.Add(new StampPlus("Moments", name, s != null, s != null ? s.Name : how, s != null ? 1 : 0, glyph, s == null ? null : games.FirstOrDefault(g => g.Key == s.Key)));
        }
        Moment("Early bird", "Play before 7 in the morning", s => s.Start.Hour is >= 4 and < 7, "\uE706");
        Moment("Lunch break", "A session around noon", s => s.Start.Hour is >= 12 and < 14, "\uE799");
        Moment("Weekend warrior", "Three hours on a Saturday or Sunday", s => s.Start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday && s.Seconds >= 3 * 3600, "\uE787");
        Moment("New year's play", "Play on January 1st", s => s.Start.Month == 1 && s.Start.Day == 1, "\uE734");
        Moment("Holiday gamer", "Play on December 24th or 25th", s => s.Start.Month == 12 && s.Start.Day is 24 or 25, "\uE734");
        Moment("Quick fix", "A session of 10 minutes or less", s => s.Seconds is > 60 and <= 600, "\uE916");
        return stamps;
    }

    /// <summary>A traveler's rank, from how many stamps there are.</summary>
    public static (string Rank, int Next) PassportRank(int earned)
    {
        var ranks = new[] { (0, "Tourist"), (10, "Traveler"), (25, "Explorer"), (45, "Globetrotter"), (70, "Voyager"), (100, "World citizen") };
        var rank = ranks.Last(r => earned >= r.Item1);
        var next = ranks.FirstOrDefault(r => r.Item1 > earned);
        return (rank.Item2, next.Item2 == null ? 0 : next.Item1);
    }

    // ================================================================ moods, with more to choose from

    public sealed record MoodDef(string Id, string Name, string Glyph, string Line, string[] Genres);

    public static readonly MoodDef[] MoodsPlus =
    {
        new("chill", "Chill", "\uE706", "Something relaxed, nothing to prove", new[] { "simulation", "casual", "puzzle", "indie", "farming" }),
        new("adrenaline", "Adrenaline", "\uE945", "Fast, loud, heart racing", new[] { "action", "racing", "shooter", "fighting", "sports" }),
        new("story", "Story time", "\uE736", "A world to get lost in", new[] { "rpg", "adventure", "role" }),
        new("laugh", "Quick laugh", "\uE76E", "Short, silly, fun right away", new[] { "casual", "indie", "party", "platform" }),
        new("brain", "Brain workout", "\uE7BE", "Plan, think, outsmart", new[] { "strategy", "puzzle", "simulation" }),
        new("spooky", "Spooky", "\uE708", "Lights off, sound up", new[] { "horror", "survival" }),
        new("unfinished", "Unfinished business", "\uE768", "Pick up where you left off", Array.Empty<string>()),
        new("rediscover", "Rediscover", "\uE81C", "Old friends you haven't seen in a while", Array.Empty<string>()),
        new("nostalgia", "Nostalgia", "\uE823", "Games from years gone by", Array.Empty<string>()),
        new("new", "Something new", "\uE7C1", "Games you've barely touched", Array.Empty<string>()),
        new("surprise", "Surprise me", "\uE8B1", "Anything at all, chosen by chance", Array.Empty<string>()),
    };

    /// <summary>
    /// Games for a mood, best first: by genre, how far along you are, when you last played, and how long it takes
    /// (with <paramref name="maxHours"/> &gt; 0, games that don't fit the time you have score lower). Recent picks are
    /// moved down so the list changes.
    /// </summary>
    public static List<Game> PickForMoodPlus(string mood, IEnumerable<Game> library, PlaytimeStore playtime, LibraryStore notes, double maxHours, bool installedOnly, int count = 12)
    {
        var m = MoodsPlus.FirstOrDefault(x => x.Id == mood) ?? MoodsPlus[0];
        var rand = new Random();
        var recent = Get("mood.recent").Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var picks = library.Where(g => g.IsGame && (!installedOnly || g.IsInstalled) && notes.Find(g.Key)?.Status != "Dropped")
            .Select(g =>
            {
                var genre = (GenreCache.Of(g) ?? "").ToLowerInvariant();
                double played = HoursOf(g, playtime);
                var last = g.LastPlayed ?? g.RecentAt;
                double daysAway = last > DateTime.MinValue.AddYears(1) ? (DateTime.Now - last).TotalDays : 9999;
                var status = notes.Find(g.Key)?.Status;
                var times = HowLongToBeat.Cached(g).Times;
                double score = mood switch
                {
                    "unfinished" => (status == "Playing" ? 14 : 0) + (played > 1 && status != "Finished" ? 6 : -10) + (times is { Main: > 0 } t1 && played < t1.Main ? 4 : 0) + (daysAway < 60 ? 3 : 0),
                    "rediscover" => played > 2 && daysAway > 120 ? 10 + Math.Min(daysAway / 90, 6) : -10,
                    "nostalgia" => g.ReleaseYear is int y && y > 1970 ? Math.Clamp((2018 - y) / 2.0, -10, 14) : -10,
                    "new" => played < 1 ? 10 : played < 3 ? 4 : -10,
                    "surprise" => 6,
                    _ => (m.Genres.Any(genre.Contains) ? 10 : 0) - (status == "Finished" ? 3 : 0),
                };
                if (maxHours > 0 && times is { Main: > 0 } t)
                {
                    double left = Math.Max(0.3, t.Main - played);
                    score += t.Main <= maxHours * 3 || left <= maxHours ? 3 : -2; // short games, or nearly done, fit a short evening
                }
                if (recent.Contains(g.Key)) score -= 5;
                return (g, score: score + rand.NextDouble() * (mood == "surprise" ? 10 : 4));
            })
            .Where(x => x.score >= 4).OrderByDescending(x => x.score).Take(count).Select(x => x.g).ToList();
        Put("mood.recent", string.Join("|", picks.Take(3).Select(g => g.Key).Concat(recent).Take(9)));
        return picks;
    }
}
