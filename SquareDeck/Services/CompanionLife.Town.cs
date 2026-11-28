namespace SquareDeck.Services;

/// <summary>
/// The playground's neighborhood and goals (9.0.6): the neighbors and how much they like you, their side missions,
/// three daily quests, and the fish you've caught.
/// </summary>
public static partial class CompanionLife
{
    // ---------------------------------------------------------------- daily quests

    /// <summary>A daily goal: what to do (an event and how many times), and the star bits it's worth.</summary>
    public sealed record Quest(string Id, string Text, string Event, int Target, int Stars);

    private static readonly Quest[] QuestPool =
    {
        new("feed", "Feed {0} twice", "feed", 2, 10),
        new("pet", "Pet {0} 5 times", "pet", 5, 8),
        new("stars", "Play Star Catch", "starcatch", 1, 10),
        new("fish", "Catch 3 fish at the pond", "fish", 3, 12),
        new("memory", "Finish a game of Memory Match", "memory", 1, 10),
        new("beat", "Play Beat Hop on the stage", "beat", 1, 10),
        new("hide", "Win hide-and-seek", "hide", 1, 10),
        new("talk", "Chat with 3 neighbors", "talk", 3, 10),
        new("gift", "Give a neighbor a gift", "gift", 1, 12),
        new("toy", "Play with toys twice", "toy", 2, 8),
        new("nap", "Let {0} take a nap", "nap", 1, 6),
        new("rps", "Win rock, paper, scissors", "rps", 1, 10),
        new("joke", "Hear 2 of {0}'s jokes", "joke", 2, 6),
        new("mail", "Open today's letter", "mail", 1, 6),
    };

    /// <summary>Today's three quests (the same all day, new ones tomorrow).</summary>
    public static List<Quest> TodaysQuests()
    {
        var day = DateTime.Today;
        var rng = new Random(day.Year * 1000 + day.DayOfYear);
        return QuestPool.OrderBy(_ => rng.Next()).Take(3).ToList();
    }

    private static string QuestKey(Quest q) => $"pg.q.{DateTime.Today:yyyyMMdd}.{q.Id}";

    public static int QuestProgress(Quest q) => int.TryParse(Labs.Get(QuestKey(q), "0"), out var n) ? n : 0;

    public static bool QuestDone(Quest q) => QuestProgress(q) >= q.Target;

    /// <summary>Something happened: counts toward today's quests. Returns the quests it just completed (their stars already added).</summary>
    public static List<Quest> Track(string evt, int amount = 1)
    {
        var done = new List<Quest>();
        foreach (var q in TodaysQuests().Where(q => q.Event == evt))
        {
            int before = QuestProgress(q);
            if (before >= q.Target) continue;
            int now = Math.Min(q.Target, before + amount);
            Labs.Put(QuestKey(q), now.ToString());
            if (now >= q.Target) { Stars += q.Stars; AddXp(3); done.Add(q); }
        }
        // All three done: a bonus.
        if (done.Count > 0 && TodaysQuests().All(QuestDone) && Labs.Get("pg.qbonus", "") != DateTime.Today.ToString("yyyyMMdd"))
        {
            Labs.Put("pg.qbonus", DateTime.Today.ToString("yyyyMMdd"));
            Stars += 15;
            done.Add(new Quest("bonus", "All of today's quests", "", 1, 15));
        }
        return done;
    }

    // ---------------------------------------------------------------- fish

    public sealed record Fish(string Id, string Name, string Rarity, int Stars, string Color, bool NightOnly = false, bool DayOnly = false);

    public static readonly Fish[] FishList =
    {
        new("minnow", "Minnow", "common", 1, "#B8C0CC"),
        new("bluegill", "Bluegill", "common", 2, "#4A7BD9"),
        new("carp", "Carp", "common", 2, "#C8A060"),
        new("trout", "Rainbow trout", "uncommon", 3, "#FF8FB1"),
        new("catfish", "Catfish", "uncommon", 3, "#6D7684"),
        new("puffer", "Pufferfish", "uncommon", 4, "#FFD84D"),
        new("sunfish", "Sunfish", "uncommon", 4, "#FF9F43", DayOnly: true),
        new("koi", "Koi", "rare", 6, "#FF6B4A"),
        new("eel", "Night eel", "rare", 6, "#3E8E48", NightOnly: true),
        new("goldkoi", "Golden koi", "legendary", 15, "#F2C14E"),
        new("moonfish", "Moonfish", "legendary", 15, "#C9D3E8", NightOnly: true),
        new("boot", "Old boot", "junk", 0, "#6B4229"),
    };

    /// <summary>A fish for this cast: mostly common ones, rarely a legend, now and then a boot.</summary>
    public static Fish RollFish(bool night)
    {
        var pool = FishList.Where(f => !(f.NightOnly && !night) && !(f.DayOnly && night)).ToList();
        int roll = Random.Shared.Next(1000);
        string rarity = roll < 50 ? "junk" : roll < 560 ? "common" : roll < 860 ? "uncommon" : roll < 975 ? "rare" : "legendary";
        var choices = pool.Where(f => f.Rarity == rarity).ToList();
        if (choices.Count == 0) choices = pool.Where(f => f.Rarity == "common").ToList();
        return choices[Random.Shared.Next(choices.Count)];
    }

    private static Dictionary<string, int> Caught() =>
        Labs.Get("pg.fish", "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(':'))
            .Where(p => p.Length == 2 && int.TryParse(p[1], out _)).ToDictionary(p => p[0], p => int.Parse(p[1]));

    public static int CaughtCount(string fish) => Caught().GetValueOrDefault(fish);

    /// <summary>Adds a fish to the collection; true when it's the first of its kind.</summary>
    public static bool AddFish(string fish)
    {
        var caught = Caught();
        bool first = !caught.ContainsKey(fish);
        caught[fish] = caught.GetValueOrDefault(fish) + 1;
        Labs.Put("pg.fish", string.Join(",", caught.Select(kv => kv.Key + ":" + kv.Value)));
        return first;
    }

    public static int SpeciesCaught => Caught().Count;

    // ---------------------------------------------------------------- neighbors

    public static int Hearts(string neighbor) => Math.Clamp(int.TryParse(Labs.Get("pg.n." + neighbor, "0"), out var n) ? n : 0, 0, 10);

    /// <summary>Adds hearts (up to 10); true if a heart was actually gained.</summary>
    public static bool AddHearts(string neighbor, int amount)
    {
        int before = Hearts(neighbor);
        Labs.Put("pg.n." + neighbor, Math.Clamp(before + amount, 0, 10).ToString());
        return Hearts(neighbor) > before;
    }

    /// <summary>True the first time today (for a once-a-day thing with a neighbor, by key).</summary>
    public static bool FirstToday(string key)
    {
        string today = DateTime.Today.ToString("yyyyMMdd");
        if (Labs.Get("pg.day." + key, "") == today) return false;
        Labs.Put("pg.day." + key, today);
        return true;
    }

    // Side missions: how far along each neighbor's story is, whether the current one is taken and done, and where a
    // lost thing is hidden.
    public static int StoryStage(string neighbor) => int.TryParse(Labs.Get("pg.story." + neighbor, "0"), out var n) ? n : 0;
    public static void NextStory(string neighbor)
    {
        Labs.Put("pg.story." + neighbor, (StoryStage(neighbor) + 1).ToString());
        Labs.Put("pg.story." + neighbor + ".on", "");
        Labs.Put("pg.story." + neighbor + ".ready", "");
        Labs.Put("pg.story." + neighbor + ".spot", "");
    }
    public static bool StoryOn(string neighbor) => Labs.Get("pg.story." + neighbor + ".on", "") == "1";
    public static void TakeStory(string neighbor, string? spot = null)
    {
        Labs.Put("pg.story." + neighbor + ".on", "1");
        if (spot != null) Labs.Put("pg.story." + neighbor + ".spot", spot);
    }
    public static bool StoryReady(string neighbor) => Labs.Get("pg.story." + neighbor + ".ready", "") == "1";
    public static void SetStoryReady(string neighbor) => Labs.Put("pg.story." + neighbor + ".ready", "1");
    public static string StorySpot(string neighbor) => Labs.Get("pg.story." + neighbor + ".spot", "");

    public static int Best(string game) => int.TryParse(Labs.Get("pg.best." + game, "0"), out var n) ? n : 0;
    public static void SetBest(string game, int value) => Labs.Put("pg.best." + game, value.ToString());
}
