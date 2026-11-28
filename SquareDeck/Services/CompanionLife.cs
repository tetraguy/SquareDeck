using System.Globalization;

namespace SquareDeck.Services;

/// <summary>
/// The companion's life outside Home (9.0.4, "Companion playground", out of Experimental in 9.0.20): how fed, rested and entertained it
/// is, how close the two of you are, the star bits it has saved, and what it owns. Needs drift down slowly in real
/// time (never to nothing: it's never neglected, only a bit grumpy), and come back up with food, naps and play.
/// Star bits come from the daily letter in the mailbox, the playground's games, petting, and from playing your games.
/// </summary>
public static partial class CompanionLife
{
    public const string Lab = "companionplay";
    public static bool On => Labs.On(Lab);

    // ---------------------------------------------------------------- the catalog

    /// <summary>Something to buy: a snack (eaten), a toy, a piece of furniture or decoration, a wallpaper, or an accessory.</summary>
    public sealed record Item(string Id, string Name, string Kind, int Price, string About, double Food = 0, double Fun = 0, double Energy = 0);

    public static readonly Item[] Catalog =
    {
        // Snacks: bought by the piece, kept in the fridge.
        new("apple", "Apple", "snack", 0, "From the tree in the yard. Crunchy!", Food: 18),
        new("cookie", "Cookie", "snack", 4, "A warm chocolate chip cookie.", Food: 14, Fun: 6),
        new("onigiri", "Rice ball", "snack", 6, "Simple, filling, perfect.", Food: 26),
        new("icecream", "Ice cream", "snack", 7, "Three scoops. Brain freeze included.", Food: 14, Fun: 14),
        new("pizza", "Pizza slice", "snack", 9, "Extra cheese, obviously.", Food: 34, Fun: 6),
        new("sushi", "Sushi", "snack", 10, "Fancy. Pinky out.", Food: 30, Fun: 8),
        new("cake", "Birthday cake", "snack", 14, "Every day is somebody's birthday.", Food: 30, Fun: 24),
        new("cocoa", "Hot cocoa", "snack", 5, "Warm hands, warm heart. A little sleepy after.", Food: 10, Fun: 4, Energy: 12),
        new("energy", "Star soda", "snack", 8, "Fizzy and full of zip.", Food: 6, Energy: 30),
        // Toys: kept, played with in the yard.
        new("ball", "Bouncy ball", "toy", 0, "The classic. Throw it, it brings it back (mostly)."),
        new("kite", "Kite", "toy", 25, "Flies it high on windy days."),
        new("bubbles", "Bubble wand", "toy", 18, "Blows bubbles to chase."),
        new("frisbee", "Frisbee", "toy", 22, "Throw it far; it zooms after it."),
        // Furniture and decorations for the house.
        new("plant", "Potted plant", "decor", 20, "A little green in the corner."),
        new("fishtank", "Fish tank", "decor", 60, "Three fish, all named Bubbles."),
        new("disco", "Disco ball", "decor", 80, "Turns the house into a dance floor."),
        new("trophies", "Trophy shelf", "decor", 45, "Shows off your achievements."),
        new("poster", "Game poster", "decor", 30, "Your favorite game, framed on the wall."),
        new("lamp", "Lava lamp", "decor", 35, "Blobs drifting up and down. Mesmerizing."),
        new("rug", "Cozy rug", "decor", 25, "Soft under tiny feet."),
        new("telescope", "Telescope", "decor", 70, "For counting stars at night, out in the yard."),
        // Wallpapers.
        new("wall:mint", "Mint wallpaper", "wall", 0, "Fresh and calm."),
        new("wall:peach", "Peach wallpaper", "wall", 30, "Warm and sunny."),
        new("wall:night", "Starry wallpaper", "wall", 40, "Little stars all over."),
        new("wall:lilac", "Lilac wallpaper", "wall", 30, "Soft purple stripes."),
        new("wall:wood", "Cabin wallpaper", "wall", 40, "Wooden planks, very cozy."),
    };

    /// <summary>Accessory prices (free ones cost 0).</summary>
    public static int AccessoryPrice(string id) => id switch
    {
        "bowtie" or "glasses" or "freckles" or "blush" or "bandaid" or "beautymark" => 0,
        "sparkles" or "clownnose" or "whiskers" => 10,
        "earring" or "gem" or "bubble" or "bandananeck" => 20,
        "goggles" or "lei" => 30,
        "medal" => 45,
        "star" or "heart" or "sweatband" => 15,
        "mustache" or "necktie" or "collar" => 25,
        "eyepatch" or "pearls" => 35,
        "monocle" => 50,
        _ => 20,
    };

    public static Item? Find(string id) => Catalog.FirstOrDefault(i => i.Id == id);

    // ---------------------------------------------------------------- state

    private static double D(string key, double fallback) =>
        double.TryParse(Labs.Get(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    private static void Set(string key, double v) => Labs.Put(key, v.ToString("0.###", CultureInfo.InvariantCulture));
    private static int I(string key, int fallback) => int.TryParse(Labs.Get(key, ""), out var v) ? v : fallback;

    public static double Food { get => D("pg.food", 80); set => Set("pg.food", Math.Clamp(value, 0, 100)); }
    public static double Energy { get => D("pg.energy", 80); set => Set("pg.energy", Math.Clamp(value, 0, 100)); }
    public static double Fun { get => D("pg.fun", 70); set => Set("pg.fun", Math.Clamp(value, 0, 100)); }
    public static int Stars { get => I("pg.stars", 20); set => Labs.Put("pg.stars", Math.Max(0, value).ToString()); }
    public static int Xp { get => I("pg.xp", 0); set => Labs.Put("pg.xp", Math.Max(0, value).ToString()); }

    /// <summary>Friendship level, 1 to 10.</summary>
    public static int Level => Math.Clamp(1 + (int)Math.Floor(Math.Sqrt(Xp / 25.0)), 1, 10);

    /// <summary>How far through the current level, 0 to 1.</summary>
    public static double LevelProgress
    {
        get
        {
            int l = Level;
            if (l >= 10) return 1;
            double from = 25.0 * (l - 1) * (l - 1), to = 25.0 * l * l;
            return Math.Clamp((Xp - from) / (to - from), 0, 1);
        }
    }

    public static readonly string[] LevelNames =
    {
        "New friends", "Getting to know you", "Pals", "Good buddies", "Partners in play", "Close friends", "Trusty sidekicks",
        "Best friends", "Inseparable", "Kindred spirits",
    };

    /// <summary>Adds friendship; true when that was a new level.</summary>
    public static bool AddXp(int amount)
    {
        int before = Level;
        Xp += amount;
        return Level > before;
    }

    // ---------------------------------------------------------------- owned things

    private static HashSet<string> Owned()
    {
        var set = Labs.Get("pg.owned", "ball,wall:mint").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return set;
    }

    public static bool Owns(string id) => id.StartsWith("acc:", StringComparison.Ordinal)
        ? AccessoryPrice(id[4..]) == 0 || Owned().Contains(id)
        : Find(id) is { Price: 0, Kind: not "snack" } || Owned().Contains(id);

    private static void AddOwned(string id)
    {
        var set = Owned();
        set.Add(id);
        Labs.Put("pg.owned", string.Join(",", set));
    }

    /// <summary>How many of a snack are in the fridge.</summary>
    public static int Count(string snack) => Bag().GetValueOrDefault(snack);

    private static Dictionary<string, int> Bag()
    {
        var bag = new Dictionary<string, int>();
        foreach (var part in Labs.Get("pg.bag", "apple:2,cookie:1").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            if (kv.Length == 2 && int.TryParse(kv[1], out var n) && n > 0) bag[kv[0]] = n;
        }
        return bag;
    }

    private static void SaveBag(Dictionary<string, int> bag) =>
        Labs.Put("pg.bag", string.Join(",", bag.Where(kv => kv.Value > 0).Select(kv => kv.Key + ":" + kv.Value)));

    public static void AddSnack(string id, int n = 1)
    {
        var bag = Bag();
        bag[id] = bag.GetValueOrDefault(id) + n;
        SaveBag(bag);
    }

    /// <summary>Takes one snack from the fridge; false if there's none.</summary>
    public static bool TakeSnack(string id)
    {
        var bag = Bag();
        if (bag.GetValueOrDefault(id) <= 0) return false;
        bag[id]--;
        SaveBag(bag);
        return true;
    }

    public static IEnumerable<Item> Snacks() => Catalog.Where(i => i.Kind == "snack" && Count(i.Id) > 0);

    /// <summary>Buys something; false if there aren't enough star bits (or it's already owned).</summary>
    public static bool Buy(string id, int price, bool snack)
    {
        if (Stars < price) return false;
        if (!snack && Owns(id)) return false;
        Stars -= price;
        if (snack) AddSnack(id); else AddOwned(id);
        return true;
    }

    public static string Wallpaper { get => Labs.Get("pg.wall", "wall:mint"); set => Labs.Put("pg.wall", value); }

    /// <summary>Decorations shown in the house (owned ones can be put away).</summary>
    public static bool Shown(string decor) => Owns(decor) && !Labs.Get("pg.hidden", "").Split(',').Contains(decor);

    public static void SetShown(string decor, bool shown)
    {
        var hidden = Labs.Get("pg.hidden", "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (shown) hidden.Remove(decor); else hidden.Add(decor);
        Labs.Put("pg.hidden", string.Join(",", hidden));
    }

    // ---------------------------------------------------------------- time passing

    /// <summary>
    /// Brings the needs up to now: a little hungrier and less entertained each hour (and sleepier by day, rested by
    /// night, since it naps while you're away at night). Never below 8. Returns how many hours passed.
    /// </summary>
    public static double CatchUp()
    {
        var now = DateTime.Now;
        long ticks = long.TryParse(Labs.Get("pg.updated", ""), out var t) ? t : 0;
        if (ticks <= 0) { Labs.Put("pg.updated", now.Ticks.ToString()); return 0; }
        double hours = Math.Clamp((now - new DateTime(ticks)).TotalHours, 0, 24 * 14);
        if (hours < 0.02) return hours;
        Food = Math.Max(8, Food - hours * 4.5);
        Fun = Math.Max(8, Fun - hours * 3.5);
        bool night = now.Hour >= 22 || now.Hour < 7;
        Energy = night ? Math.Min(100, Energy + hours * 6) : Math.Max(8, Energy - hours * 2.5);
        Labs.Put("pg.updated", now.Ticks.ToString());
        return hours;
    }

    /// <summary>The overall mood from its needs: hungry, sleepy, bored, happy or fine.</summary>
    public static string Mood()
    {
        if (Food < 25) return "hungry";
        if (Energy < 22) return "sleepy";
        if (Fun < 25) return "bored";
        if ((Food + Energy + Fun) / 3 > 68) return "happy";
        return "fine";
    }

    /// <summary>A short line about how it feels, for Home and the playground.</summary>
    public static string? NeedLine()
    {
        string[] lines = Mood() switch
        {
            "hungry" => new[] { "My tummy is making game-over sounds.", "Is there… a snack in this launcher?", "I'd trade a save file for a cookie.", "Feed me and I'll stop narrating." },
            "sleepy" => new[] { "I could nap for a whole patch cycle.", "*yawns in 8-bit*", "Running on 1% battery. Like, me, not the Ally.", "Bed. Soon. Please." },
            "bored" => new[] { "Wanna play in the yard?", "I've counted every pixel. Twice.", "Entertain me! Or I'll start a speedrun of sighing.", "Throw a ball? Pretty please?" },
            _ => Array.Empty<string>(),
        };
        return lines.Length == 0 ? null : lines[Random.Shared.Next(lines.Length)];
    }

    // ---------------------------------------------------------------- earning star bits

    /// <summary>Star bits for the time spent playing your games since the last visit: one for every 10 minutes, up to 40.</summary>
    public static int EarnFromPlay(PlaytimeStore playtime)
    {
        double total = playtime.Seconds.Values.Sum(days => days.Values.Sum());
        double last = D("pg.playsecs", -1);
        Set("pg.playsecs", total);
        if (last < 0) return 0;
        int earned = (int)Math.Min(40, Math.Max(0, (total - last) / 600));
        if (earned > 0) Stars += earned;
        return earned;
    }

    /// <summary>The daily letter in the mailbox: star bits (more for a streak of days) and a note. Null if already opened today.</summary>
    public static (int Stars, string Note, int Streak)? OpenMail(string? recentGame, string name)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var lastDay = Labs.Get("pg.mail", "");
        if (lastDay == today) return null;
        int streak = lastDay == DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd") ? I("pg.streak", 0) + 1 : 1;
        Labs.Put("pg.mail", today);
        Labs.Put("pg.streak", streak.ToString());
        int stars = 8 + Math.Min(12, streak * 2);
        Stars += stars;
        var notes = new List<string>
        {
            $"Dear you,\nThanks for visiting. The yard is nicer with you in it.\nLove, {name}",
            $"Hi!\nI practiced my somersault 47 times today. Nailed it on 46.\n— {name}",
            $"Note to self (and you):\nSnacks are a love language.\n— {name}",
            $"Breaking news!\nA butterfly sat on my head for 3 whole seconds.\nI'm a celebrity now.\n— {name}",
            $"Hey friend,\nWhatever your backlog says, you're doing great.\n— {name}",
        };
        if (recentGame != null)
        {
            notes.Add($"Dear player,\nI watched you play {recentGame}. You were amazing. Mostly.\n— {name}");
            notes.Add($"P.S.\nIf {recentGame} needs a sidekick, I'm available weekends.\n— {name}");
        }
        if (streak >= 3) notes.Add($"Day {streak} in a row!\nYou keep coming back. I keep being happy about it.\n— {name}");
        return (stars, notes[Random.Shared.Next(notes.Count)], streak);
    }

    /// <summary>Whether the apple tree has fruit (one apple every three hours).</summary>
    public static bool AppleReady => !long.TryParse(Labs.Get("pg.apple", ""), out var t) || DateTime.Now - new DateTime(t) > TimeSpan.FromHours(3);

    public static void PickApple()
    {
        Labs.Put("pg.apple", DateTime.Now.Ticks.ToString());
        AddSnack("apple");
    }

    /// <summary>A star bit for petting now and then (up to ten a day).</summary>
    public static bool PetReward()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        int count = Labs.Get("pg.petday", "") == today ? I("pg.petcount", 0) : 0;
        Labs.Put("pg.petday", today);
        if (count >= 10 || Random.Shared.Next(3) != 0) { Labs.Put("pg.petcount", count.ToString()); return false; }
        Labs.Put("pg.petcount", (count + 1).ToString());
        Stars += 1;
        return true;
    }

    public static int BestCatch { get => I("pg.best", 0); set => Labs.Put("pg.best", value.ToString()); }
}
