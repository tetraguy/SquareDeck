using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// The companion, grown up: it notices the game you've highlighted and acts it out (revving a race car, swinging a
/// sword, rolling a d20, getting spooked, planting a seed…), says something about that game (how long you've played,
/// when you last did, how long it takes to beat), reacts when you scroll too fast, likes being tapped, says goodbye in
/// the game's own spirit and asks how it went when you're back, knows the holidays and the time of day, has something
/// to do and say on the newer pages, and has many more things to say everywhere.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _companionNotice = new() { Interval = TimeSpan.FromMilliseconds(1100) };
    private readonly Queue<DateTime> _companionSteps = new();
    private readonly Dictionary<string, string> _companionLines = new();
    private DateTime _companionNoticedAt, _companionDizzyAt, _companionPetAt;
    private string? _companionNoticedKey;
    private int _companionPets;
    private Game? _companionLaunched;
    private DateTime _companionLaunchedAt;
    private bool _companionNoticeHooked, _companionPetHooked;

    // ---------------------------------------------------------------- what kind of game

    /// <summary>
    /// What kind of game it is, for the companion's act: from the store's genre and, where that says little, from
    /// words in the name. Null when it can't tell.
    /// </summary>
    private static string? CompanionKind(Game g)
    {
        string name = g.Name.ToLowerInvariant();
        bool N(params string[] words) => words.Any(name.Contains);
        if (g.IsRetro) return "retro";
        if (N("forza", "gran turismo", "need for speed", "kart", "rally", "dirt ", "racing", "racer", "f1 ", "drive", "burnout", "wreckfest", "motorsport", "trackmania")) return "racing";
        if (N("resident evil", "silent hill", "outlast", "phasmophobia", "amnesia", "dead space", "alien: isolation", "five nights", "horror", "lethal company", "the evil within", "until dawn")) return "horror";
        if (N("minecraft", "terraria", "craft", "lego", "satisfactory", "factorio", "planet coaster", "builder")) return "build";
        if (N("stardew", "farm", "harvest", "animal crossing", "garden", "ranch", "slime rancher", "coral island")) return "farm";
        if (N("fishing", "fish ", "dredge")) return "fish";
        if (N("chess", "civilization", "total war", "xcom", "age of empires", "stellaris", "crusader kings", "company of heroes", "starcraft", "into the breach")) return "strategy";
        if (N("tetris", "puzzle", "portal", "the witness", "baba is you", "talos")) return "puzzle";
        if (N("fifa", "ea sports fc", "nba", "madden", "rocket league", "golf", "tennis", "football", "soccer", "hockey", "nhl", "mlb", "skate")) return "sports";
        if (N("flight simulator", "ace combat", "microsoft flight")) return "flight";
        if (N("kerbal", "no man's sky", "elite dangerous", "starfield", "space", "star citizen", "everspace", "outer wilds")) return "space";
        if (N("tekken", "street fighter", "mortal kombat", "smash", "guilty gear", "fighter", "king of fighters", "soulcalibur", "brawl")) return "fighting";
        if (N("doom", "call of duty", "halo", "battlefield", "counter-strike", "valorant", "apex", "overwatch", "destiny", "borderlands", "titanfall", "quake", "wolfenstein", "far cry", "shooter", "helldivers")) return "shooter";
        if (N("beat saber", "guitar hero", "rhythm", "hi-fi rush", "dance", "crypt of the necrodancer", "osu")) return "rhythm";
        if (N("valheim", "rust", "the forest", "sons of the forest", "don't starve", "subnautica", "the long dark", "ark", "7 days to die", "grounded", "raft")) return "survival";
        if (N("mario", "sonic", "celeste", "hollow knight", "ori and", "rayman", "crash bandicoot", "spyro", "platformer", "cuphead", "shovel knight")) return "platform";
        if (N("sims", "cities:", "simulator", "tycoon", "two point", "euro truck", "powerwash", "house flipper")) return "sim";
        if (N("elden ring", "souls", "zelda", "witcher", "skyrim", "elder scrolls", "baldur", "final fantasy", "dragon", "persona", "diablo", "mass effect", "fallout", "pathfinder", "divinity", "monster hunter")) return "rpg";
        var genre = (GenreCache.Of(g) ?? "").ToLowerInvariant();
        if (genre.Contains("horror")) return "horror";
        if (genre.Contains("racing") || genre.Contains("driving")) return "racing";
        if (genre.Contains("shooter")) return "shooter";
        if (genre.Contains("fighting")) return "fighting";
        if (genre.Contains("sport")) return "sports";
        if (genre.Contains("puzzle")) return "puzzle";
        if (genre.Contains("strategy")) return "strategy";
        if (genre.Contains("rpg") || genre.Contains("role")) return "rpg";
        if (genre.Contains("platform")) return "platform";
        if (genre.Contains("simulation")) return "sim";
        if (genre.Contains("survival")) return "survival";
        if (genre.Contains("music") || genre.Contains("rhythm")) return "rhythm";
        if (genre.Contains("casual") || genre.Contains("family")) return "casual";
        if (genre.Contains("adventure")) return "adventure";
        if (genre.Contains("action")) return "action";
        if (g.ReleaseYear is int y and > 1970 and < 2000) return "retro";
        return null;
    }

    private static readonly Dictionary<string, string[]> KindActs = new()
    {
        ["racing"] = new[] { "vroom", "vroom", "zoom" },
        ["horror"] = new[] { "spooked", "flashlight" },
        ["shooter"] = new[] { "pewpew" },
        ["fighting"] = new[] { "punch" },
        ["rpg"] = new[] { "slash", "dice", "potion" },
        ["adventure"] = new[] { "slash", "lookaround", "potion" },
        ["action"] = new[] { "slash", "punch" },
        ["platform"] = new[] { "coin", "hop" },
        ["sports"] = new[] { "dribble", "ball" },
        ["puzzle"] = new[] { "puzzle", "think" },
        ["farm"] = new[] { "farm", "tea" },
        ["strategy"] = new[] { "chess", "think" },
        ["space"] = new[] { "rocket" },
        ["flight"] = new[] { "plane" },
        ["survival"] = new[] { "camp" },
        ["rhythm"] = new[] { "notes", "dance" },
        ["build"] = new[] { "build" },
        ["sim"] = new[] { "build", "tinker", "tea" },
        ["casual"] = new[] { "tea", "bubbles" },
        ["retro"] = new[] { "pixel", "coin" },
        ["fish"] = new[] { "fish" },
    };

    private static readonly Dictionary<string, string[]> KindLines = new()
    {
        ["racing"] = new[] { "Vroom vroom! {0}?", "Floor it!", "I call the passenger seat.", "Brake late, apex early.", "Mind the hairpin!", "Race you!" },
        ["horror"] = new[] { "{0}? Lights on, please.", "I'll… watch from behind you.", "Did you hear that?", "Nope. Nope nope nope.", "Hold my hand?", "I'm not scared. You're scared." },
        ["shooter"] = new[] { "Lock and load!", "Pew pew! {0}!", "Check your corners.", "Reloading… done!", "Headshots only.", "I've got your back." },
        ["fighting"] = new[] { "Round one… fight!", "Show me that combo!", "Hi-yah!", "Down, forward, punch!", "No button mashing!", "K.O.!" },
        ["rpg"] = new[] { "An epic quest awaits!", "Did you save recently?", "Level up time!", "I'd pick the rogue.", "Side quests first?", "{0}… a whole world in there." },
        ["adventure"] = new[] { "Adventure time!", "Where to this time?", "Treasure ahead, I can feel it.", "Follow the map!", "{0}! Let's explore." },
        ["action"] = new[] { "Action!", "Let's go!", "Big moves only.", "{0}! Ready?", "No time to lose!" },
        ["platform"] = new[] { "Jump! Jump!", "Collect them all!", "Mind the gap!", "Double jump!", "One more level?" },
        ["sports"] = new[] { "Game day!", "Go team!", "Score one for me!", "What a play!", "Overtime?" },
        ["puzzle"] = new[] { "Thinking cap on.", "The answer's always simpler.", "Ooh, a brain teaser.", "Click… it fits!", "Try turning it around." },
        ["farm"] = new[] { "Time to water the crops!", "Morning chores!", "I love the smell of fresh hay.", "Grow, little sprout!", "Peaceful, isn't it?" },
        ["strategy"] = new[] { "Plan three moves ahead.", "One more turn…", "A cunning plan!", "Expand or defend?", "Checkmate, eventually." },
        ["space"] = new[] { "To infinity!", "Prepare for liftoff!", "Space is big. Like, really big.", "Where no one has gone before!", "Fuel check: OK!" },
        ["flight"] = new[] { "Clear skies today!", "Cleared for takeoff!", "Flaps, check.", "Window seat, please!" },
        ["survival"] = new[] { "Gather wood first!", "Build a shelter before night.", "Don't forget to eat.", "Cozy campfire time.", "Stay alive out there!" },
        ["rhythm"] = new[] { "Feel the beat!", "♪ On the beat!", "Perfect! Perfect! Perfect!", "Full combo?", "Let's groove!" },
        ["build"] = new[] { "Let's build something!", "Just one more block…", "Dream big, build bigger!", "Measure twice, place once.", "A castle? A castle." },
        ["sim"] = new[] { "Running the show!", "Everything in its place.", "So satisfying.", "Let's manage things!", "Another day at the office." },
        ["casual"] = new[] { "Easy and breezy.", "Nice and relaxed.", "Tea and a game?", "Just a quick one.", "No pressure!" },
        ["retro"] = new[] { "Old school!", "Blow on the cartridge!", "Pixels forever!", "1UP!", "A classic!" },
        ["fish"] = new[] { "Patience…", "Something's biting!", "The big one's out there.", "Cast it far!" },
    };

    /// <summary>What it says about a game: something about the game's kind, or about you and it.</summary>
    private string CompanionLineFor(Game g)
    {
        var personal = new List<string>();
        double hours = Labs.HoursOf(g, Playtime);
        var last = g.LastPlayed ?? g.RecentAt;
        double days = last > DateTime.MinValue.AddYears(1) ? (DateTime.Now - last).TotalDays : -1;
        var status = Library.Find(g.Key)?.Status;
        if (hours >= 100) personal.Add($"{Labs.Hours(hours)} in {g.Name}! Wow.");
        else if (hours >= 10) personal.Add($"You've played {Labs.Hours(hours)} of this one.");
        else if (hours < 0.1 && g.IsInstalled) personal.Add("You haven't tried this one yet!");
        if (days > 180) personal.Add("It's been ages since you played this!");
        else if (days is >= 0 and < 1) personal.Add("Back for more already?");
        else if (days > 30) personal.Add($"Last played {Math.Floor(days / 30):0} months ago.");
        if (HowLongToBeat.Cached(g).Times is { Main: > 0 } t)
        {
            if (hours > 0 && hours < t.Main && status != "Finished") personal.Add($"About {Labs.Hours(t.Main - hours)} left to beat it.");
            else if (hours <= 0) personal.Add($"Takes about {Labs.Hours(t.Main)} to beat.");
        }
        if (status == "Finished") personal.Add("You beat this one! Hero.");
        if (status == "Dropped") personal.Add("Giving it another chance?");
        if (g.IsFavorite) personal.Add("One of your favorites!");
        if (!g.IsInstalled) personal.Add("Install this one? I'd play it.");
        personal.AddRange(CompanionWitFor(g)); // 9.0.4: with a bit of attitude
        var kind = CompanionKind(g);
        bool usePersonal = personal.Count > 0 && (kind == null || Random.Shared.Next(2) == 0);
        if (usePersonal) return personal[Random.Shared.Next(personal.Count)];
        var lines = kind != null && KindLines.TryGetValue(kind, out var l) ? l : new[] { "Ooh, {0}!", "{0}? Good choice.", "That one looks fun!", "Let's play {0}!" };
        return string.Format(lines[Random.Shared.Next(lines.Length)], g.Name);
    }

    private static string CompanionActFor(Game g)
    {
        var kind = CompanionKind(g);
        var acts = kind != null && KindActs.TryGetValue(kind, out var a) ? a : new[] { "game", "think", "lookaround" };
        return acts[Random.Shared.Next(acts.Length)];
    }

    /// <summary>Plays an act with its own words (instead of the act's usual ones).</summary>
    private void CompanionQueue(string action, string line)
    {
        _companionLines[action] = line;
        CompanionQueue(action);
    }

    private string? CompanionTakeLine(string action) => _companionLines.Remove(action, out var line) ? line : null;

    // ---------------------------------------------------------------- noticing the highlighted game

    /// <summary>The highlight moved on Home: it notices the game after a moment, and gets dizzy if you whiz past many.</summary>
    private void CompanionNoticeSelection()
    {
        if (_companion == null) return;
        if (!_companionNoticeHooked) { _companionNotice.Tick += (_, _) => CompanionNoticeTick(); _companionNoticeHooked = true; }
        var now = DateTime.Now;
        CompanionCountScroll();
        _companionSteps.Enqueue(now);
        while (_companionSteps.Count > 0 && now - _companionSteps.Peek() > TimeSpan.FromSeconds(1.6)) _companionSteps.Dequeue();
        if (_companionSteps.Count >= 10 && now - _companionDizzyAt > TimeSpan.FromSeconds(25))
        {
            _companionDizzyAt = now;
            _companionSteps.Clear();
            CompanionQueue("dizzy");
            return;
        }
        _companionNotice.Stop();
        if (!_companionAsleep) _companionNotice.Start();
    }

    private void CompanionNoticeTick()
    {
        _companionNotice.Stop();
        if (_companion == null || _screens.Count > 0 || Theme.ReduceMotion) return;
        if (GameList.SelectedItem is not Game item) return;
        var game = item.IsContinueTile ? item.Target ?? item : item;
        if (!game.IsGame) return;
        var now = DateTime.Now;
        if (now - _companionNoticedAt < TimeSpan.FromSeconds(6)) return;
        if (game.Key == _companionNoticedKey && now - _companionNoticedAt < TimeSpan.FromMinutes(3)) return;
        if (Random.Shared.Next(100) >= 65) return; // not every single time
        _companionNoticedAt = now;
        _companionNoticedKey = game.Key;
        // 9.0.4: now and then it walks over and points at the game instead.
        if (Random.Shared.Next(3) == 0 && CompanionPointAt(CompanionLineFor(game))) return;
        CompanionQueue(CompanionActFor(game), CompanionLineFor(game));
    }

    // ---------------------------------------------------------------- arriving, leaving, coming back

    /// <summary>The first move on arriving at a page, when it's about something in particular (a game's page).</summary>
    private (string Act, string? Line)? CompanionArriveFor(Screen? top)
    {
        if (top is DetailsScreen d && d.Game.IsGame) return (CompanionActFor(d.Game), CompanionLineFor(d.Game));
        if (top == null && Holiday() is { } h && Random.Shared.Next(3) == 0) return h;
        return null;
    }

    /// <summary>A game starts: goodbye, in the game's spirit.</summary>
    private void CompanionLaunch(Game item)
    {
        _companionLaunched = item;
        _companionLaunchedAt = DateTime.Now;
        _companionScrolls = 0;
        var kind = CompanionKind(item);
        string line = kind switch
        {
            "horror" => "Good luck… I'll wait here. With the lights on.",
            "racing" => "Green light! Go go go!",
            "shooter" => "Go get 'em, soldier!",
            "fighting" => "Show them who's boss!",
            "rpg" or "adventure" => "May your quest be legendary!",
            "sports" => "Go win the big game!",
            "puzzle" => "You've got this, genius.",
            "farm" => "Say hi to the chickens!",
            "strategy" => "Conquer wisely!",
            "space" or "flight" => "Safe travels, pilot!",
            "survival" => "Stay warm out there!",
            "build" => "Build something amazing!",
            "rhythm" => "Hit every note!",
            "retro" => "Press start!",
            _ => new[] { "Have fun!", "Enjoy!", "See you soon!", "Go get 'em!" }[Random.Shared.Next(4)],
        };
        _companionLines["bye"] = line;
        CompanionReact("bye");
    }

    /// <summary>Back from a game: "how was it?", with how long you played.</summary>
    private string? CompanionWelcomeBack()
    {
        if (_companionLaunched == null) return null;
        var mins = (DateTime.Now - _companionLaunchedAt).TotalMinutes;
        var name = _companionLaunched.Name;
        _companionLaunched = null;
        if (mins < 3) return "That was quick! Everything OK?";
        if (mins > 180) return $"Welcome back! {Labs.Hours(mins / 60)} of {name}… epic session!";
        return new[] { $"Welcome back! How was {name}?", $"Back already? {Labs.Hours(mins / 60)} well spent!", $"I missed you! Did you win?", $"{name} again soon?" }[Random.Shared.Next(4)];
    }

    // ---------------------------------------------------------------- the calendar and the clock

    private static (string Act, string? Line)? Holiday()
    {
        var d = DateTime.Today;
        var thanksgiving = new DateTime(d.Year, 11, 1).AddDays(((int)DayOfWeek.Thursday - (int)new DateTime(d.Year, 11, 1).DayOfWeek + 7) % 7 + 21);
        if (d == thanksgiving || d == thanksgiving.AddDays(1)) return ("feast", "Happy Thanksgiving! Save me some pie!");
        if (d.Month == 10 && d.Day >= 25) return ("pumpkin", d.Day == 31 ? "Happy Halloween! Boo!" : "Spooky season!");
        if (d.Month == 12 && d.Day is >= 20 and <= 26) return ("snow", d.Day is 24 or 25 ? "Merry Christmas!" : "Happy holidays!");
        if ((d.Month == 12 && d.Day == 31) || (d.Month == 1 && d.Day <= 2)) return ("fireworks", "Happy New Year!");
        if (d.Month == 2 && d.Day == 14) return ("hearts", "Happy Valentine's Day!");
        if (d.Month == 3 && d.Day == 17) return ("coin", "Lucky day!");
        if (d.Month == 4 && d.Day == 1) return ("trip", "April Fools! …Ow.");
        if (d.Month == 5 && d.Day == 4) return ("rocket", "May the fourth be with you!");
        return null;
    }

    private static string Greeting()
    {
        var now = DateTime.Now;
        var options = new List<string> { "Hi!", "Welcome back!", "Hello!", "Hey there!" };
        if (now.Hour is >= 5 and < 12) options.AddRange(new[] { "Good morning!", "Morning! Coffee first?" });
        else if (now.Hour is >= 12 and < 17) options.AddRange(new[] { "Good afternoon!", "Afternoon gaming? Nice." });
        else if (now.Hour is >= 17 and < 22) options.AddRange(new[] { "Good evening!", "Game night!" });
        else options.AddRange(new[] { "Up late, huh?", "Just one more game…" });
        if (now.DayOfWeek == DayOfWeek.Friday && now.Hour >= 16) options.Add("It's Friday! Let's play!");
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) options.Add("Weekend! All the games!");
        if (now.DayOfWeek == DayOfWeek.Monday && now.Hour < 12) options.Add("Mondays need games.");
        if (Holiday() is { Line: { } h }) options.Add(h);
        return options[Random.Shared.Next(options.Count)];
    }

    // ---------------------------------------------------------------- more pages and more to say

    /// <summary>The newer pages, and more to do and say on the others.</summary>
    private static (string Arrive, string[] Idle, string[] Lines)? CompanionPlaceExtra(Screen? screen) => screen switch
    {
        S2PassportScreen or PassportScreen => ("trophy", new[] { "trophy", "flip", "read", "cheer", "plane" },
            new[] { "Stamp stamp!", "Where to next?", "Look at all those stamps!", "One more and you rank up?", "Frequent flyer!" }),
        S2BossScreen or BossScreen => ("slash", new[] { "slash", "punch", "pewpew", "cheer", "trophy" },
            new[] { "Get that boss!", "Critical hit!", "It's weak to finishing games!", "Charge!", "We can take it!" }),
        S2MoodScreen or MoodScreen => ("think", new[] { "think", "tea", "dice", "bubbles", "lookaround" },
            new[] { "How ARE you feeling?", "Can't decide? Roll the dice!", "I'm feeling… snacky.", "Pick again! Press Y!", "Surprise me!" }),
        S2LeaderboardScreen or LeaderboardScreen => ("trophy", new[] { "trophy", "cheer", "punch", "dribble" },
            new[] { "Who's number one?", "Gold medal energy!", "Catch up to them!", "Friendly rivalry!", "You're climbing!" }),
        S2FreeGamesScreen or FreeGamesScreen => ("shop", new[] { "shop", "coin", "cheer", "lookaround" },
            new[] { "Free games! Grab them all!", "Free is my favorite price.", "Claim it before it's gone!", "Ooh, a deal!", "Amazon Prime? Fancy!" }),
        RetroLoungeScreen => ("pixel", new[] { "pixel", "coin", "dance", "juggle" },
            new[] { "Old school!", "Blow on the cartridge!", "Insert coin!", "8-bit forever!", "High score time!" }),
        RecompZoneScreen => ("tinker", new[] { "tinker", "build", "think", "pixel" },
            new[] { "Rebuilt from scratch!", "Compiling… done!", "Classic games, new tricks.", "So many bits!" }),
        GameNewsScreen => ("read", new[] { "read", "think", "lookaround" }, new[] { "Extra, extra!", "Patch notes!", "Any good news?", "Read all about it!" }),
        ProfileScreen => ("wave", new[] { "flip", "cheer", "hearts" }, new[] { "That's you!", "Looking good!", "Nice profile!" }),
        NotificationsScreen => ("read", new[] { "read", "lookaround" }, new[] { "You've got mail!", "Anything important?", "Ding!" }),
        Switch2LibraryScreen => ("search", new[] { "search", "read", "roam", "think", "dice" },
            new[] { "So many games…", "Which one today?", "Your collection's growing!", "Have you finished that one?" }),
        _ => null,
    };

    private static readonly string[] HomeMore =
    {
        "Did you know? I can juggle.", "I wonder what's for dinner.", "Your library looks great today.", "*hums a tune*", "Psst… press A.",
        "I'm on your side. Literally.", "Is it snack time?", "I like it here.", "Let's beat the backlog!", "Tap me! I dare you.",
        "Heads or tails? Heads. Play something!", "I practiced my moonwalk.", "Do you think I'd be a good sidekick?", "Ready, player one?",
    };

    private static readonly string[] HomeMoreActs = { "tea", "dice", "pixel", "coin", "notes", "puzzle", "chess", "fish", "camp", "rocket", "build", "plane", "potion",
        // 8.5.17
        "yoyo", "kite", "paint", "jumprope", "gum", "headphones", "stargaze", "sandwich" };

    /// <summary>What each character says when petted.</summary>
    private static string[] PetLines(string species) => species switch
    {
        "cat" => new[] { "Mrrow!", "Purrrr…", "Mew!", "*headbutts your finger*", "More scritches, please." },
        "bunny" => new[] { "Eep!", "*nose twitch*", "*happy hop*", "*binky!*", "Ears are ticklish!" },
        "ghost" => new[] { "Ooooo~", "Hehe, boo!", "*floats happily*", "You can touch me?!", "Spooky… and happy." },
        "robot" => new[] { "Beep boop!", "Affection detected.", "*whirr*", "Happiness at 100%.", "01101000 01101001!" },
        "dino" => new[] { "Rawr! (That means thanks.)", "*tail wag*", "Stomp stomp!", "Tiny roar!", "*happy chomp*" },
        _ => new[] { "Bloop!", "*jiggles*", "Squish!", "Hehe, wobbly!", "Boing!" },
    };

    /// <summary>Tapped: it giggles (and gets a bit much if you keep going).</summary>
    private void CompanionPet()
    {
        var now = DateTime.Now;
        _companionPets = now - _companionPetAt < TimeSpan.FromSeconds(2) ? _companionPets + 1 : 1;
        _companionPetAt = now;
        if (_companionPets >= 5)
        {
            _companionPets = 0;
            CompanionSounds.Pet(CompanionArt.SpeciesId, excited: true);
            _companionLines["dizzy"] = Random.Shared.Next(3) == 0 ? "Okay, okay! I'm dizzy!" : TickleComeback(); // 9.0.4: comebacks
            CompanionReact("dizzy");
            return;
        }
        // Its own noise for each character, a bigger one when you keep going.
        var species = CompanionArt.SpeciesId;
        CompanionSounds.Pet(species, excited: _companionPets >= 4);
        var lines = PetLines(species).Concat(new[] { "Hehe!", "That tickles!", "Again!" }).ToArray();
        _companionLines["pet"] = lines[Random.Shared.Next(lines.Length)];
        CompanionReact("pet");
        // With the playground on, now and then a star bit for the attention (9.0.4).
        if (CompanionLife.On && CompanionLife.PetReward()) Later(0.4, () => Prop("+1 ★", "#FFD84D", 13, 64, 10, 1.4));
    }

    /// <summary>Pets it from its page (as tapping it does on Home).</summary>
    public void PetCompanion() => CompanionPet();

    private void HookCompanionPet()
    {
        if (_companion == null || _companionSprite == null || _companionPetHooked) return;
        _companion.IsHitTestVisible = true;
        _companionSprite.Background = Brushes.Transparent;
        _companionSprite.Cursor = System.Windows.Input.Cursors.Hand;
        HookCompanionTouch(); // 9.0.4: tap to pet, double-tap to boop, hold to hug, drag to carry
        StartCompanionWit();
        HookCompanionNap(); // 9.0.17: naps while the handheld sleeps
        _companionPetHooked = true;
    }

    // ---------------------------------------------------------------- the new acts

    private static double? CompanionExtraSeconds(string action) => action switch
    {
        "vroom" => 1.9, "slash" => 1.4, "pewpew" => 1.8, "punch" => 1.8, "spooked" => 2.4, "puzzle" => 2.4, "farm" => 2.8, "dribble" => 2.6,
        "dice" => 2.4, "potion" => 2.2, "chess" => 2.4, "rocket" => 2.4, "plane" => 2.6, "coin" => 1.6, "camp" => 3.2, "tea" => 3, "notes" => 3,
        "build" => 3, "pixel" => 2, "fish" => 3.4, "flashlight" => 2.8, "dizzy" => 2.6, "pet" => 1.4, "snow" => 3.5, "fireworks" => 3,
        "feast" => 3, "pumpkin" => 2.4,
        "yoyo" => 2.6, "kite" => 3.4, "paint" => 3, "jumprope" => 2.6, "gum" => 2.8, "headphones" => 3.2, "stargaze" => 3.4, "sandwich" => 2.8,
        _ => CompanionMoreSeconds(action), // 9.0.4
    };

    private static string? CompanionExtraMood(string? action) => action switch
    {
        "coin" or "rocket" or "dice" or "pet" or "fireworks" or "feast" or "pixel" => "cheer",
        "spooked" or "flashlight" or "dizzy" => "worried",
        "vroom" or "notes" or "dribble" or "pewpew" or "punch" or "slash" => "dance",
        "tea" or "camp" or "farm" or "puzzle" or "chess" or "build" or "fish" or "potion" or "plane" or "snow" => "happy",
        "pumpkin" => "love",
        "yoyo" or "kite" or "paint" or "stargaze" or "sandwich" => "happy",
        "jumprope" or "headphones" => "dance",
        "gum" => "cheer",
        _ => CompanionMoreMood(action),
    };

    private static string? CompanionExtraSay(string action) => action switch
    {
        "wave" => Greeting(),
        "vroom" => "Vroom vroom!",
        "slash" => "Hyah!",
        "pewpew" => "Pew pew!",
        "punch" => "Hi-yah!",
        "spooked" => "W-w-what was that?!",
        "flashlight" => "Is anyone there…?",
        "puzzle" => "Click! It fits.",
        "farm" => "Grow, little one!",
        "dribble" => "Swish!",
        "potion" => "+50 HP!",
        "chess" => "Knight to… somewhere.",
        "rocket" => "3… 2… 1… liftoff!",
        "plane" => "Clear skies!",
        "coin" => "Coin get!",
        "camp" => "Cozy…",
        "tea" => "Tea break?",
        "notes" => "Feel the rhythm!",
        "build" => "Built it!",
        "pixel" => "1UP!",
        "fish" => "Got one!",
        "dizzy" => "Whoa, slow down! @_@",
        "snow" => "Let it snow!",
        "fireworks" => "Ooooh! Aaaah!",
        "feast" => "Gobble gobble!",
        "pumpkin" => "Boo!",
        "yoyo" => "Walk the dog!",
        "kite" => "Up, up and away!",
        "paint" => "A masterpiece!",
        "jumprope" => "One, two, three…",
        "gum" => "*pop!*",
        "headphones" => "This song slaps.",
        "stargaze" => "Make a wish…",
        "sandwich" => "Snack time!",
        _ => CompanionMoreSay(action),
    };

    /// <summary>
    /// One of the reactions to a pet that every character has, each with a sound: a hop (boing), a spin (whoosh), a
    /// backflip (slide whistle), a ticklish wiggle (giggle), a blush with a kiss, star eyes (chime), a bow (ba-dum-tss),
    /// zoomies (zip back and forth), a heart burst (pop) and a static spark (zap).
    /// </summary>
    private void PetReaction()
    {
        if (_companionSprite == null || Theme.ReduceMotion) return;
        switch (Random.Shared.Next(10))
        {
            case 0:
                CompanionSounds.Effect("boing", 380);
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.18, -30), (0.36, 0), (0.48, -10), (0.6, 0)));
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 0.8), (0.18, 1.1), (0.36, 0.85), (0.5, 1)));
                break;
            case 1:
                CompanionSounds.Effect("whoosh", 380);
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.5)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
                Sparkles("✦", 4, "#FFFFFF");
                break;
            case 2:
                CompanionSounds.Effect("slide", 380);
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.3, -40), (0.6, 0)));
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, -360, TimeSpan.FromSeconds(0.6)));
                break;
            case 3:
                CompanionSounds.Effect("giggle", 380);
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(0.08)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5) });
                Prop("hehe", "#FFD84D", 13, 58, 6, 1.0);
                break;
            case 4:
                CompanionSounds.Effect("kiss", 380);
                Prop("◕‿◕", "#FF8FB1", 12, 18, 30, 1.2);
                Sparkles("♥", 5, "#FF8FB1", rise: true);
                _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0, 1), (0.2, 1.1), (0.5, 1)));
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (0.2, 1.1), (0.5, 1)));
                break;
            case 5:
                CompanionSounds.Effect("chime", 380);
                Prop("★", "#FFE27A", 14, 22, 26, 1.1);
                Prop("★", "#FFE27A", 14, 42, 26, 1.1);
                Sparkles("✦", 6, "#FFE27A");
                break;
            case 6:
                CompanionSounds.Effect("drum", 380);
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.25, 18), (0.6, 18), (0.8, 0)));
                Prop("ta-da!", "#C3A6FF", 13, 50, 4, 1.0);
                break;
            case 7:
                CompanionSounds.Effect("whoosh", 380);
                _companionMove.BeginAnimation(TranslateTransform.XProperty, Keys((0, 0), (0.15, -40), (0.35, 40), (0.55, -24), (0.75, 0)));
                Sparkles("~", 3, "#AAB4C8");
                break;
            case 8:
                CompanionSounds.Effect("pop", 380);
                for (int i = 0; i < 8; i++)
                {
                    var heart = Prop("♥", i % 2 == 0 ? "#FF5A7A" : "#FF8FB1", 12, 30, 24, 0.9);
                    double a = i / 8.0 * Math.PI * 2;
                    heart?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(30, 30 + Math.Cos(a) * 44, TimeSpan.FromSeconds(0.6)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    heart?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(24, 24 + Math.Sin(a) * 36, TimeSpan.FromSeconds(0.6)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
                break;
            default:
                CompanionSounds.Effect("zap", 380);
                Sparkles("⚡", 3, "#FFE27A");
                _companionMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-2, 2, TimeSpan.FromMilliseconds(40)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(6) });
                break;
        }
    }

    /// <summary>A little something extra when it's petted, in each character's own way.</summary>
    private void PetFlourish(string species)
    {
        if (_companionSprite == null || Theme.ReduceMotion) return;
        switch (species)
        {
            case "cat":
                // Purrs: soft waves beside it and a slow, happy lean.
                for (int i = 0; i < 3; i++) { var w = Prop("≈", "#FFD8A8", 14, 74, 30 + i * 10, 1.3); w?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(74, 92, TimeSpan.FromSeconds(1.2)) { BeginTime = TimeSpan.FromSeconds(i * 0.15) }); }
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.4, 8), (1.1, 8), (1.4, 0)));
                break;
            case "bunny":
                // Two happy hops.
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -18, TimeSpan.FromSeconds(0.17)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                break;
            case "ghost":
                // Fades a little, floats up and comes back.
                _companionSprite.BeginAnimation(OpacityProperty, Keys((0, 1), (0.4, 0.45), (1.0, 1)));
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.5, -14), (1.2, 0)));
                break;
            case "robot":
                // Sparks and a quick happy spin of the head (a little tick back and forth).
                Sparkles("⚡", 3, "#6FE6FF");
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.1, -6), (0.2, 6), (0.3, -6), (0.4, 6), (0.5, 0)));
                break;
            case "dino":
                // A stomp: a little shake and dust.
                _companionMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-3, 3, TimeSpan.FromMilliseconds(70)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) });
                Sparkles("∴", 3, "#C8B89A");
                break;
            default:
                // The blob: an extra-big jiggle.
                _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0, 1), (0.1, 1.2), (0.3, 0.9), (0.5, 1.08), (0.7, 1)));
                break;
        }
    }

    /// <summary>A prop: a symbol in the effects layer, which fades out when it's done.</summary>
    private TextBlock? Prop(string symbol, string hex, double size, double x, double y, double seconds, bool emoji = false)
    {
        if (_companionFx == null) return null;
        var t = new TextBlock
        {
            Text = symbol, FontSize = size, FontWeight = FontWeights.Bold, Foreground = (Brush)new BrushConverter().ConvertFromString(hex)!,
            FontFamily = new FontFamily(emoji ? "Segoe UI Emoji" : "Segoe UI Symbol"), RenderTransformOrigin = new Point(0.5, 0.5),
        };
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        _companionFx.Children.Add(t);
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.35)) { BeginTime = TimeSpan.FromSeconds(Math.Max(0, seconds - 0.35)) };
        fade.Completed += (_, _) => _companionFx?.Children.Remove(t);
        t.BeginAnimation(OpacityProperty, fade);
        return t;
    }

    private static DoubleAnimationUsingKeyFrames Keys(params (double At, double Value)[] frames)
    {
        var a = new DoubleAnimationUsingKeyFrames();
        foreach (var (at, v) in frames) a.KeyFrames.Add(new EasingDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        return a;
    }

    private void Later(double seconds, Action what) =>
        Dispatcher.BeginInvoke(async () => { await Task.Delay(TimeSpan.FromSeconds(seconds)); if (_companion != null) what(); });

    /// <summary>Plays the new acts (the props and moves); nothing for the older ones.</summary>
    private void CompanionExtraAnimate(string action)
    {
        switch (action)
        {
            case "vroom":
            {
                // A race car tears past behind, with speed lines; it leans into the wind and watches it go.
                var car = Prop("🏎", "#FF4A4A", 30, 200, 96, 1.6, emoji: true);
                car?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(200, -280, TimeSpan.FromSeconds(1.1)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
                for (int i = 0; i < 4; i++)
                {
                    var line = Prop("―", "#AAB4C8", 18, 200, 90 + i * 8, 1.4);
                    line?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(220 + i * 20, -240, TimeSpan.FromSeconds(1.0)) { BeginTime = TimeSpan.FromSeconds(0.1 + i * 0.05) });
                }
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.4, -14), (1.2, -14), (1.6, 0)));
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(1.6);
                break;
            }
            case "slash":
            {
                // A sword swing in an arc with a streak of light, lunging into it.
                var sword = Prop("🗡", "#E0E6F0", 30, 70, 50, 1.2, emoji: true);
                if (sword != null)
                {
                    var turn = new RotateTransform(-70);
                    sword.RenderTransform = turn;
                    turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-70, 110, TimeSpan.FromSeconds(0.32)) { BeginTime = TimeSpan.FromSeconds(0.2), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
                var arc = new Path { Data = Geometry.Parse("M 20,40 A 46,46 0 0 0 100,50"), Stroke = Brushes.White, StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Opacity = 0,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.White, BlurRadius = 12, ShadowDepth = 0 } };
                _companionFx?.Children.Add(arc);
                var flash = Keys((0, 0), (0.25, 0), (0.35, 1), (0.7, 0));
                flash.Completed += (_, _) => _companionFx?.Children.Remove(arc);
                arc.BeginAnimation(OpacityProperty, flash);
                _companionMove.BeginAnimation(TranslateTransform.XProperty, Keys((0, 0), (0.2, 6), (0.4, -14), (0.9, 0)));
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.3, -10), (0.6, 0)));
                Later(0.4, () => Sparkles("✦", 4, "#FFFFFF"));
                break;
            }
            case "pewpew":
                for (int i = 0; i < 6; i++)
                {
                    int n = i;
                    Later(n * 0.2, () =>
                    {
                        var shot = Prop("●", "#5CFFC8", 10, 22, 94, 0.6);
                        shot?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(22, -300, TimeSpan.FromSeconds(0.45)));
                        _companionMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(4, 0, TimeSpan.FromSeconds(0.12)));
                    });
                }
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(1.6);
                break;
            case "punch":
            {
                string[] words = { "POW!", "BAM!", "WHAM!", "K.O.!" };
                string[] colors = { "#FFD84D", "#FF6B8A", "#7FB8FF", "#FF8A3D" };
                for (int i = 0; i < 3; i++)
                {
                    int n = i;
                    Later(n * 0.45, () =>
                    {
                        var w = Prop(words[Random.Shared.Next(words.Length)], colors[n % colors.Length], 18, -20 + Random.Shared.Next(-20, 30), 30 + Random.Shared.Next(-10, 30), 0.7);
                        if (w != null)
                        {
                            var pop = new ScaleTransform(0, 0);
                            w.RenderTransform = pop;
                            var grow = new DoubleAnimation(0, 1.3, TimeSpan.FromSeconds(0.18)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut } };
                            pop.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                            pop.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                        }
                        _companionMove.BeginAnimation(TranslateTransform.XProperty, Keys((0, 0), (0.08, -12), (0.25, 0)));
                    });
                }
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(1.6);
                break;
            }
            case "spooked":
            {
                // A ghost rises up behind; it shivers, then jumps out of its skin.
                var ghost = Prop("👻", "#E8ECF4", 34, 0, 100, 2.2, emoji: true);
                if (ghost != null)
                {
                    ghost.BeginAnimation(Canvas.TopProperty, Keys((0, 100), (1.2, 20), (2.2, 0)));
                    ghost.BeginAnimation(Canvas.LeftProperty, Keys((0, 0), (0.6, -8), (1.2, 6), (2.2, -4)));
                }
                _companionMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-3, 3, TimeSpan.FromMilliseconds(50)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(10) });
                Later(1.1, () =>
                {
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -40, TimeSpan.FromSeconds(0.2)) { AutoReverse = true, EasingFunction = new QuadraticEase() });
                    Sparkles("!", 2, "#FF6B6B");
                });
                break;
            }
            case "flashlight":
            {
                // A cone of light sweeping the dark.
                var cone = new Polygon { Points = new PointCollection { new(60, 90), new(-120, 40), new(-120, 140) }, Opacity = 0, RenderTransformOrigin = new Point(1, 0.5) };
                cone.Fill = new LinearGradientBrush(Color.FromArgb(0x90, 0xFF, 0xF2, 0xB0), Color.FromArgb(0x00, 0xFF, 0xF2, 0xB0), 180);
                var sweep = new RotateTransform(0, 60, 90);
                cone.RenderTransform = sweep;
                _companionFx?.Children.Add(cone);
                var show = Keys((0, 0), (0.3, 1), (2.4, 1), (2.8, 0));
                show.Completed += (_, _) => _companionFx?.Children.Remove(cone);
                cone.BeginAnimation(OpacityProperty, show);
                sweep.BeginAnimation(RotateTransform.AngleProperty, Keys((0, -12), (0.9, 14), (1.8, -10), (2.6, 6)));
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(2.6);
                break;
            }
            case "puzzle":
            {
                var spots = new[] { (-40.0, 0.0), (120.0, 10.0), (40.0, -40.0) };
                for (int i = 0; i < 3; i++)
                {
                    var (sx, sy) = spots[i];
                    var piece = Prop("🧩", new[] { "#7FB8FF", "#FFD84D", "#FF6B8A" }[i], 22, sx, sy, 2.3, emoji: true);
                    piece?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(sx, 38 + i * 14, TimeSpan.FromSeconds(1.1)) { BeginTime = TimeSpan.FromSeconds(i * 0.2), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut } });
                    piece?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(sy, 18, TimeSpan.FromSeconds(1.1)) { BeginTime = TimeSpan.FromSeconds(i * 0.2), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut } });
                }
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.6, 8), (1.4, 8), (1.7, 0)));
                Later(1.6, () => Sparkles("✦", 6, "#FFFFFF"));
                break;
            }
            case "farm":
            {
                // A sprout pops up and grows into a flower while it waters it.
                var plant = Prop("🌱", "#5CCB6A", 26, -6, 96, 2.8, emoji: true);
                if (plant != null)
                {
                    var grow = new ScaleTransform(0, 0);
                    plant.RenderTransformOrigin = new Point(0.5, 1);
                    plant.RenderTransform = grow;
                    var up = Keys((0, 0), (0.6, 1), (1.4, 1), (1.7, 1.3));
                    grow.BeginAnimation(ScaleTransform.ScaleXProperty, up);
                    grow.BeginAnimation(ScaleTransform.ScaleYProperty, up);
                    Later(1.5, () => plant.Text = "🌻");
                }
                for (int i = 0; i < 4; i++)
                {
                    var drop = Prop("💧", "#7FB8FF", 12, 6 + i * 4, 60, 0.9, emoji: true);
                    drop?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(60, 100, TimeSpan.FromSeconds(0.6)) { BeginTime = TimeSpan.FromSeconds(0.3 + i * 0.15) });
                }
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(2.6);
                break;
            }
            case "dribble":
            {
                var ball = Prop("🏀", "#FF8A3D", 22, 86, 100, 2.5, emoji: true);
                ball?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(100, 72, TimeSpan.FromSeconds(0.2)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3, TimeSpan.FromSeconds(0.2)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5) });
                Later(2.0, () =>
                {
                    ball?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(72, -40, TimeSpan.FromSeconds(0.4)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                    Prop("+2", "#FFD84D", 16, 60, 10, 0.8);
                });
                break;
            }
            case "dice":
            {
                int roll = Random.Shared.Next(1, 21);
                var die = Prop("🎲", "#FFFFFF", 26, 70, 90, 2.3, emoji: true);
                if (die != null)
                {
                    var spin = new RotateTransform();
                    die.RenderTransform = spin;
                    spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 720, TimeSpan.FromSeconds(1.0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    die.BeginAnimation(Canvas.TopProperty, Keys((0, 90), (0.45, 10), (1.0, 96)));
                    die.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(70, -20, TimeSpan.FromSeconds(1.0)));
                }
                Later(1.1, () =>
                {
                    Prop(roll.ToString(), roll == 20 ? "#FFD84D" : roll == 1 ? "#FF6B6B" : "#FFFFFF", 22, -16, 56, 1.2);
                    if (roll == 20) Sparkles("★", 8, "#FFD84D");
                    if (roll == 1) Sparkles("✶", 3, "#FF6B6B");
                });
                if (!_companionLines.ContainsKey("dice"))
                    _companionLines["dice"] = roll == 20 ? "Natural 20!!" : roll == 1 ? "Critical fail… oops." : $"Rolled a {roll}!";
                break;
            }
            case "potion":
                Prop("⚗", "#7FE0A0", 24, 84, 76, 2.1);
                Sparkles("°", 6, "#7FE0A0", rise: true);
                _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.1, TimeSpan.FromSeconds(0.3)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), BeginTime = TimeSpan.FromSeconds(0.8) });
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.1, TimeSpan.FromSeconds(0.3)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), BeginTime = TimeSpan.FromSeconds(0.8) });
                Later(1.2, () => Sparkles("+", 5, "#7FE0A0"));
                break;
            case "chess":
            {
                var knight = Prop("♞", "#E8ECF4", 30, 0, 96, 2.3);
                if (knight != null)
                {
                    knight.BeginAnimation(Canvas.TopProperty, Keys((0, 96), (0.5, 96), (0.9, 50), (1.6, 50)));
                    knight.BeginAnimation(Canvas.LeftProperty, Keys((0, 0), (0.9, 0), (1.3, -30), (1.6, -30)));
                }
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.4, 6), (1.8, 6), (2.2, 0)));
                break;
            }
            case "rocket":
            {
                var rocket = Prop("🚀", "#E8ECF4", 30, 80, 92, 2.3, emoji: true);
                if (rocket != null)
                {
                    rocket.BeginAnimation(Canvas.TopProperty, Keys((0, 92), (0.8, 88), (2.2, -260)));
                    rocket.BeginAnimation(Canvas.LeftProperty, Keys((0, 80), (0.8, 80), (2.2, 160)));
                    rocket.RenderTransform = new TranslateTransform();
                    ((TranslateTransform)rocket.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1.5, 1.5, TimeSpan.FromMilliseconds(40)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(10) });
                }
                Later(0.7, () => Sparkles("☁", 4, "#C8D0DC"));
                _companionLook = 1; _companionLookUntil = DateTime.Now.AddSeconds(2.2);
                break;
            }
            case "plane":
            {
                var plane = Prop("✈", "#E8ECF4", 26, 220, 10, 2.5);
                if (plane != null)
                {
                    plane.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(220, -300, TimeSpan.FromSeconds(2.2)));
                    plane.BeginAnimation(Canvas.TopProperty, Keys((0, 10), (0.7, -10), (1.4, 20), (2.2, 0)));
                    plane.RenderTransform = new ScaleTransform(-1, 1);
                }
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(2.2);
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.8, -8), (1.6, 8), (2.4, 0)));
                break;
            }
            case "coin":
            {
                // Jumps, bumps a block over its head, and a coin pops out.
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.25, -30), (0.5, 0)));
                var block = Prop("▣", "#E0A040", 24, 48, 18, 1.5);
                block?.BeginAnimation(Canvas.TopProperty, Keys((0, 18), (0.25, 18), (0.32, 10), (0.45, 18)));
                Later(0.25, () =>
                {
                    var coin = Prop("◉", "#FFD84D", 18, 52, 10, 0.9);
                    coin?.BeginAnimation(Canvas.TopProperty, Keys((0, 10), (0.35, -30), (0.7, -10)));
                    Prop("+1", "#FFFFFF", 14, 76, -16, 0.9);
                });
                break;
            }
            case "camp":
            {
                var fire = Prop("🔥", "#FF8A3D", 28, -10, 96, 3.1, emoji: true);
                if (fire != null)
                {
                    var flick = new ScaleTransform(1, 1);
                    fire.RenderTransformOrigin = new Point(0.5, 1);
                    fire.RenderTransform = flick;
                    flick.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1.15, TimeSpan.FromSeconds(0.18)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                }
                Sparkles("·", 6, "#FFB060", rise: true);
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(3);
                _companionMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -6, TimeSpan.FromSeconds(0.6)) { AutoReverse = true });
                break;
            }
            case "tea":
                Prop("☕", "#E8D8B0", 22, 84, 80, 2.9, emoji: true);
                for (int i = 0; i < 3; i++)
                {
                    var steam = Prop("∿", "#DDE3EA", 14, 88 + i * 3, 70, 1.4);
                    steam?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(70, 30, TimeSpan.FromSeconds(1.4)) { BeginTime = TimeSpan.FromSeconds(i * 0.4) });
                }
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (1.0, 0), (1.3, -8), (2.2, -8), (2.6, 0)));
                break;
            case "notes":
                foreach (var (sym, hex) in new[] { ("♪", "#C3A6FF"), ("♫", "#7FD8BE"), ("♬", "#FF6B8A"), ("♩", "#FFD84D") }) Sparkles(sym, 2, hex, rise: true);
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-10, 10, TimeSpan.FromSeconds(0.3)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5) });
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -8, TimeSpan.FromSeconds(0.15)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(10) });
                break;
            case "build":
            {
                // Taps a block with a pickaxe three times; it breaks into bits and a little house appears.
                var block = Prop("■", "#8B6B4A", 24, -4, 98, 1.4);
                var pick = Prop("⛏", "#C8D0DC", 22, 16, 80, 1.4);
                if (pick != null)
                {
                    var swing = new RotateTransform();
                    pick.RenderTransform = swing;
                    swing.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, -40, TimeSpan.FromSeconds(0.2)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
                }
                Later(1.2, () => { Sparkles("▪", 6, "#8B6B4A"); Prop("⌂", "#FFD84D", 30, -8, 88, 1.6); });
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(2.8);
                break;
            }
            case "pixel":
            {
                // An 8-bit jump: in steps, as old games move, with square sparkles.
                var step = new DoubleAnimationUsingKeyFrames();
                foreach (var (at, v) in new[] { (0.0, 0.0), (0.1, -10.0), (0.2, -20.0), (0.3, -26.0), (0.4, -20.0), (0.5, -10.0), (0.6, 0.0) })
                    step.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at))));
                step.RepeatBehavior = new RepeatBehavior(2);
                _companionMove.BeginAnimation(TranslateTransform.YProperty, step);
                Sparkles("■", 6, "#7FD8BE");
                Later(0.6, () => Prop("1UP", "#5CFF7A", 16, 70, 10, 1.1)?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(10, -20, TimeSpan.FromSeconds(1))));
                break;
            }
            case "fish":
            {
                var lineShape = new Line { X1 = 88, Y1 = 78, X2 = 88, Y2 = 78, Stroke = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA)), StrokeThickness = 1.5 };
                _companionFx?.Children.Add(lineShape);
                lineShape.BeginAnimation(Line.X2Property, new DoubleAnimation(88, -90, TimeSpan.FromSeconds(0.5)) { EasingFunction = new QuadraticEase() });
                lineShape.BeginAnimation(Line.Y2Property, new DoubleAnimation(78, 116, TimeSpan.FromSeconds(0.5)));
                var bob = Prop("●", "#FF6B6B", 10, -94, 110, 3.2);
                bob?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(110, 116, TimeSpan.FromSeconds(0.3)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4), BeginTime = TimeSpan.FromSeconds(0.5) });
                Later(2.3, () =>
                {
                    var fishy = Prop("🐟", "#7FB8FF", 22, -96, 110, 1.0, emoji: true);
                    fishy?.BeginAnimation(Canvas.TopProperty, Keys((0, 110), (0.4, 50), (0.9, 90)));
                    fishy?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(-96, 40, TimeSpan.FromSeconds(0.9)));
                    Sparkles("~", 4, "#9FD8FF");
                });
                Later(3.3, () => _companionFx?.Children.Remove(lineShape));
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(3.2);
                break;
            }
            case "dizzy":
                for (int i = 0; i < 3; i++)
                {
                    var star = Prop("★", "#FFD84D", 14, 52, 40, 2.5);
                    if (star == null) continue;
                    var x = new DoubleAnimationUsingKeyFrames { RepeatBehavior = new RepeatBehavior(3) };
                    var y = new DoubleAnimationUsingKeyFrames { RepeatBehavior = new RepeatBehavior(3) };
                    for (int k = 0; k <= 8; k++)
                    {
                        double a = (k / 8.0 + i / 3.0) * Math.PI * 2;
                        x.KeyFrames.Add(new LinearDoubleKeyFrame(54 + Math.Cos(a) * 26, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(k * 0.1))));
                        y.KeyFrames.Add(new LinearDoubleKeyFrame(48 + Math.Sin(a) * 8, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(k * 0.1))));
                    }
                    star.BeginAnimation(Canvas.LeftProperty, x);
                    star.BeginAnimation(Canvas.TopProperty, y);
                }
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-12, 12, TimeSpan.FromSeconds(0.3)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4), EasingFunction = new SineEase() });
                break;
            case "yoyo":
            {
                // A yo-yo drops, spins at the bottom and climbs back, three times.
                var yo = Prop("◉", "#FF5A7A", 16, 62, 30, 2.5);
                var cord = new Line { X1 = 70, Y1 = 30, X2 = 70, Y2 = 30, Stroke = Brushes.White, StrokeThickness = 1.5, Opacity = 0.8 };
                _companionFx?.Children.Add(cord);
                var drop = Keys((0, 30), (0.4, 92), (0.8, 30));
                drop.RepeatBehavior = new RepeatBehavior(3);
                yo?.BeginAnimation(Canvas.TopProperty, drop);
                var reach = Keys((0, 38), (0.4, 100), (0.8, 38));
                reach.RepeatBehavior = new RepeatBehavior(3);
                cord.BeginAnimation(Line.Y2Property, reach);
                Later(2.5, () => _companionFx?.Children.Remove(cord));
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.4, 3), (0.8, 0)));
                break;
            }
            case "kite":
            {
                // A kite climbs on its string and dances in the wind while it holds on and leans back.
                var kite = Prop("◆", "#7FB8FF", 26, 60, 40, 3.3);
                var tail = Prop("〰", "#FFD84D", 16, 66, 66, 3.3);
                var x = Keys((0, 60), (0.8, 110), (1.6, 90), (2.4, 120), (3.2, 100));
                var y = Keys((0, 40), (0.8, -50), (1.6, -36), (2.4, -60), (3.2, -44));
                kite?.BeginAnimation(Canvas.LeftProperty, x);
                kite?.BeginAnimation(Canvas.TopProperty, y);
                tail?.BeginAnimation(Canvas.LeftProperty, Keys((0, 66), (0.8, 116), (1.6, 96), (2.4, 126), (3.2, 106)));
                tail?.BeginAnimation(Canvas.TopProperty, Keys((0, 66), (0.8, -24), (1.6, -10), (2.4, -34), (3.2, -18)));
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.6, -8), (2.8, -8), (3.3, 0)));
                _companionLook = 1; _companionLookUntil = DateTime.Now.AddSeconds(3.2);
                break;
            }
            case "paint":
            {
                // An easel; dabs of color land on it one by one, then it steps back to admire it.
                Prop("▭", "#E8ECF2", 40, 84, 20, 2.9);
                string[] colors = { "#FF5A7A", "#FFD84D", "#7FD8BE", "#7FB8FF", "#C3A6FF" };
                for (int i = 0; i < colors.Length; i++)
                {
                    int n = i;
                    Later(0.3 + n * 0.4, () => Prop("●", colors[n], 9, 92 + Random.Shared.Next(0, 18), 36 + Random.Shared.Next(0, 16), 2.6 - n * 0.4));
                }
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.3, 6), (0.5, -2), (0.7, 6), (0.9, -2), (1.1, 6), (2.3, 0)));
                _companionMove.BeginAnimation(TranslateTransform.XProperty, Keys((0, 0), (2.2, 0), (2.6, -10), (3, 0)));
                _companionLook = 1; _companionLookUntil = DateTime.Now.AddSeconds(2.9);
                break;
            }
            case "jumprope":
            {
                // Skipping rope: the rope swings over its head and under its feet as it hops, faster each time.
                var rope = new System.Windows.Shapes.Ellipse { Width = 84, Height = 92, Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x8F, 0xB1)), StrokeThickness = 2.5, Opacity = 0.85,
                    RenderTransformOrigin = new Point(0.5, 0.5) };
                var squash = new ScaleTransform(1, 1);
                rope.RenderTransform = squash;
                Canvas.SetLeft(rope, -6); Canvas.SetTop(rope, -12);
                _companionFx?.Children.Add(rope);
                var swing = new DoubleAnimation(1, -1, TimeSpan.FromSeconds(0.4)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) };
                squash.BeginAnimation(ScaleTransform.ScaleYProperty, swing);
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -16, TimeSpan.FromSeconds(0.2)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(6), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                Later(2.5, () => _companionFx?.Children.Remove(rope));
                break;
            }
            case "gum":
            {
                // A bubble of gum grows, wobbles… and pops.
                var bubble = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0x8F, 0xB1)), RenderTransformOrigin = new Point(0.5, 0.5) };
                Canvas.SetLeft(bubble, 33); Canvas.SetTop(bubble, 44);
                _companionFx?.Children.Add(bubble);
                var grow = new ScaleTransform(1, 1);
                bubble.RenderTransform = grow;
                var size = Keys((0, 1), (1.6, 6), (1.8, 5.6), (2.0, 6.4), (2.1, 0.01));
                grow.BeginAnimation(ScaleTransform.ScaleXProperty, size);
                grow.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (1.6, 6), (1.8, 6.3), (2.0, 5.8), (2.1, 0.01)));
                Later(2.1, () =>
                {
                    _companionFx?.Children.Remove(bubble);
                    Sparkles("✱", 5, "#FF8FB1");
                    _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (0.1, 0.85), (0.3, 1)));
                });
                break;
            }
            case "headphones":
            {
                // Headphones on, nodding to the beat, notes drifting out.
                var cans = Prop("🎧", "#FFFFFF", 30, 21, -12, 3.1, emoji: true);
                for (int i = 0; i < 6; i++)
                {
                    int n = i;
                    Later(0.3 + n * 0.45, () =>
                    {
                        var note = Prop(n % 2 == 0 ? "♪" : "♫", n % 3 == 0 ? "#C3A6FF" : "#7FD8BE", 16, n % 2 == 0 ? -6 : 72, 20, 1.2);
                        note?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(20, -30, TimeSpan.FromSeconds(1.2)));
                    });
                }
                var nod = new DoubleAnimation(0, 5, TimeSpan.FromSeconds(0.25)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(6) };
                _companionMove.BeginAnimation(TranslateTransform.YProperty, nod);
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-5, 5, TimeSpan.FromSeconds(0.5)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
                break;
            }
            case "stargaze":
            {
                // Lies back (tilts) and watches stars twinkle, then a shooting star streaks across.
                for (int i = 0; i < 6; i++)
                {
                    var star = Prop("✦", "#FFE27A", 8 + Random.Shared.Next(6), Random.Shared.Next(-60, 120), Random.Shared.Next(-60, -10), 3.3);
                    star?.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromSeconds(0.4 + Random.Shared.NextDouble() * 0.4)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
                }
                Later(1.8, () =>
                {
                    var shooting = Prop("☄", "#FFFFFF", 18, 130, -60, 1.0);
                    shooting?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(130, -80, TimeSpan.FromSeconds(0.8)));
                    shooting?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(-60, -10, TimeSpan.FromSeconds(0.8)));
                });
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.5, -18), (3.0, -18), (3.4, 0)));
                break;
            }
            case "sandwich":
            {
                // A sandwich, three big bites (crumbs fly), a satisfied pat of the belly.
                var food = Prop("🥪", "#FFFFFF", 24, 52, 34, 2.0, emoji: true);
                for (int i = 0; i < 3; i++)
                {
                    int n = i;
                    Later(0.4 + n * 0.5, () =>
                    {
                        Sparkles("·", 3, "#E0B070");
                        _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (0.1, 0.9), (0.25, 1)));
                        if (food != null) food.FontSize = 24 - (n + 1) * 5;
                    });
                }
                Later(2.1, () => Sparkles("♥", 2, "#FF5A7A", rise: true));
                break;
            }
            case "pet":
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (0.1, 0.82), (0.35, 1.08), (0.6, 1)));
                _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0, 1), (0.1, 1.15), (0.35, 0.95), (0.6, 1)));
                Sparkles("♥", 3, "#FF5A7A", rise: true);
                // Its own way of liking it, or one of the shared reactions (with a sound of its own), turn about.
                if (Random.Shared.Next(2) == 0) PetFlourish(CompanionArt.SpeciesId);
                else PetReaction();
                break;
            case "snow":
                for (int i = 0; i < 12; i++)
                {
                    double sx = Random.Shared.Next(-80, 130);
                    var flake = Prop("❄", "#FFFFFF", 10 + Random.Shared.Next(8), sx, -40, 3.4);
                    flake?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(-40, 120, TimeSpan.FromSeconds(2.4 + Random.Shared.NextDouble())) { BeginTime = TimeSpan.FromSeconds(i * 0.08) });
                    flake?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(sx, sx + Random.Shared.Next(-20, 20), TimeSpan.FromSeconds(2.6)));
                }
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-6, 6, TimeSpan.FromSeconds(0.5)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
                break;
            case "fireworks":
                for (int i = 0; i < 4; i++)
                {
                    int n = i;
                    Later(n * 0.6, () =>
                    {
                        string hex = new[] { "#FF6B8A", "#FFD84D", "#7FD8BE", "#C3A6FF" }[n];
                        double cx = Random.Shared.Next(-60, 100), cy = Random.Shared.Next(-40, 10);
                        for (int k = 0; k < 8; k++)
                        {
                            var spark = Prop("✦", hex, 12, cx, cy, 0.9);
                            double a = k / 8.0 * Math.PI * 2;
                            spark?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(cx, cx + Math.Cos(a) * 34, TimeSpan.FromSeconds(0.7)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                            spark?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(cy, cy + Math.Sin(a) * 34 + 10, TimeSpan.FromSeconds(0.7)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                        }
                    });
                }
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -12, TimeSpan.FromSeconds(0.3)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) });
                break;
            case "feast":
                Prop("🥧", "#E0A060", 22, -14, 98, 2.9, emoji: true);
                Prop("🍗", "#C8844A", 22, 86, 82, 2.9, emoji: true);
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -6, TimeSpan.FromSeconds(0.25)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5) });
                Later(1.2, () => Sparkles("♥", 3, "#FF8A3D", rise: true));
                break;
            case "pumpkin":
            {
                var pumpkin = Prop("🎃", "#FF8A3D", 28, 0, 96, 2.3, emoji: true);
                pumpkin?.BeginAnimation(Canvas.TopProperty, Keys((0, 130), (0.4, 96), (2.0, 96)));
                Later(0.9, () => _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -30, TimeSpan.FromSeconds(0.2)) { AutoReverse = true }));
                Sparkles("🦇", 2, "#5A3E6E", rise: true);
                break;
            }
        }
    }
}
