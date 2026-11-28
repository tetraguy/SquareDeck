using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// The companion, with more heart (9.0.4): twenty new acts (skateboarding, guitar, a ninja vanish, breakdancing,
/// hiccups, a superhero flight, a selfie…), wit for every page and for your games (with comebacks when you keep poking
/// it), walking over to point at the game you've highlighted, and new ways to touch it: tap to pet, double-tap to boop
/// its nose, hold for a hug, and pick it up and drop it somewhere else along the bottom. With the playground on, it
/// also tells you when it's hungry, sleepy or bored.
/// </summary>
public partial class MainWindow
{
    // ---------------------------------------------------------------- the new acts

    private static double? CompanionMoreSeconds(string action) => action switch
    {
        "skate" => 3.2, "guitar" => 3.2, "sing" => 2.8, "bake" => 2.8, "selfie" => 2.4, "hero" => 2.8, "ninja" => 2.2, "breakdance" => 2.4,
        "hiccup" => 2.6, "sneak" => 3, "drums" => 2.8, "levitate" => 3.2, "laugh" => 2.2, "shiver" => 2.4, "cool" => 2.6, "point" => 2.4,
        "boop" => 1.4, "hug" => 2.2, "dangle" => 60, "drop" => 1.4, "sulk" => 3.2, "snowball" => 2.4, "wink" => 1.6, "shrug" => 1.8,
        "victory" => 2.4, "peekedge" => 2.8,
        _ => CompanionPlaySeconds(action), // 9.0.17
    };

    private static string? CompanionMoreMood(string? action) => action switch
    {
        "skate" or "guitar" or "drums" or "breakdance" or "sing" => "dance",
        "bake" or "levitate" or "sneak" or "snowball" or "peekedge" => "happy",
        "selfie" or "hero" or "laugh" or "cool" or "point" or "victory" or "drop" => "cheer",
        "boop" or "hiccup" or "shiver" or "dangle" => "worried",
        "hug" or "wink" => "love",
        "sulk" or "shrug" => "bored",
        "ninja" => "happy",
        _ => CompanionPlayMood(action), // 9.0.17
    };

    private static string? CompanionMoreSay(string action) => action switch
    {
        "skate" => new[] { "Kickflip! …Ish.", "Radical!", "Grind time!" }[Random.Shared.Next(3)],
        "guitar" => new[] { "This one's called 'Loading Screen Blues'.", "♪ Shred! ♪", "Thank you, I'll be here all week." }[Random.Shared.Next(3)],
        "sing" => new[] { "♪ La la laaa ♪", "♪ Do re mi fa SO-lid gaming ♪", "I'm basically a pop star." }[Random.Shared.Next(3)],
        "bake" => new[] { "Fresh cupcakes!", "Baked with 100% pixels.", "Don't tell the fridge." }[Random.Shared.Next(3)],
        "selfie" => new[] { "Say cheese!", "#companionlife", "Getting my good side. All sides are good." }[Random.Shared.Next(3)],
        "hero" => new[] { "Up, up and away!", "Never fear, I'm here!", "Saving the day, one tile at a time." }[Random.Shared.Next(3)],
        "ninja" => new[] { "Ninja vanish!", "You didn't see me.", "*poof*" }[Random.Shared.Next(3)],
        "breakdance" => new[] { "Windmill!", "Check these moves!", "B-boy mode: on." }[Random.Shared.Next(3)],
        "hiccup" => "Hic!",
        "sneak" => new[] { "Shhh… sneaking.", "Tiptoe, tiptoe…", "Stealth level: legendary." }[Random.Shared.Next(3)],
        "drums" => new[] { "Ba-dum-tss!", "Drum solo!", "Feel that beat!" }[Random.Shared.Next(3)],
        "levitate" => new[] { "Ommm… whoa, I'm floating!", "Zen mode: maximum.", "Look, no feet!" }[Random.Shared.Next(3)],
        "laugh" => new[] { "Hahaha!", "Hehehe, good one!", "I'm cracking up!" }[Random.Shared.Next(3)],
        "shiver" => new[] { "Brrr! Who left the window open?", "S-s-so c-cold!", "I need a scarf." }[Random.Shared.Next(3)],
        "cool" => new[] { "Deal with it.", "Too cool for this row.", "Shades on. Game on." }[Random.Shared.Next(3)],
        "boop" => new[] { "Boop!", "My nose!", "Hey! Beep!", "You booped me!" }[Random.Shared.Next(4)],
        "hug" => new[] { "Aww, a hug!", "Squeeeeeze!", "Best. Friend. Ever.", "Okay, okay, I love you too." }[Random.Shared.Next(4)],
        "dangle" => new[] { "Whoa, put me down!", "Wheee! Where are we going?", "I'm flying!", "Careful, I'm fragile! (I'm not.)" }[Random.Shared.Next(4)],
        "drop" => new[] { "Nailed the landing!", "Oof! New spot!", "Ten out of ten.", "I'll just live here now." }[Random.Shared.Next(4)],
        "snowball" => new[] { "Snowball fight!", "Incoming!", "Got you!" }[Random.Shared.Next(3)],
        "wink" => new[] { "*wink*", "Our secret.", "You know what I mean." }[Random.Shared.Next(3)],
        "shrug" => new[] { "¯\\_(ツ)_/¯", "Eh.", "Who knows?" }[Random.Shared.Next(3)],
        "victory" => new[] { "Victory!", "We did it!", "GG!" }[Random.Shared.Next(3)],
        "peekedge" => new[] { "What's over here?", "Peek!", "Just checking." }[Random.Shared.Next(3)],
        _ => CompanionPlaySay(action), // 9.0.17
    };

    /// <summary>The moves and props of the new acts (nothing for the older ones).</summary>
    private void CompanionMoreAnimate(string action)
    {
        if (_companionSprite == null) return;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        switch (action)
        {
            case "skate":
            {
                // A skateboard appears under it: it rolls off to the left, ollies, and rolls back.
                var board = Prop("▬", "#C46B3A", 30, 30, 112, 3.0);
                var (from, to) = CompanionRoamTarget(160);
                CompanionGlideX(to, 2.4, new SineEase { EasingMode = EasingMode.EaseInOut });
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (1.1, 0), (1.3, -30), (1.55, 0), (3, 0)));
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.4, to < from ? 8 : -8), (2.2, to < from ? 8 : -8), (2.6, 0)));
                board?.BeginAnimation(Canvas.TopProperty, Keys((0, 112), (1.1, 112), (1.3, 84), (1.55, 112)));
                _companionLook = to < from ? -1 : 1; _companionLookUntil = DateTime.Now.AddSeconds(2.6);
                CompanionSounds.Effect("whoosh");
                break;
            }
            case "guitar":
            {
                Prop("🎸", "#E89A4A", 30, 62, 64, 3.0, emoji: true);
                for (int i = 0; i < 5; i++) Later(0.3 + i * 0.5, () => Sparkles(Random.Shared.Next(2) == 0 ? "♪" : "♫", 1, "#FFD84D", rise: true));
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(0.32)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5) });
                break;
            }
            case "sing":
                for (int n = 0; n < 6; n++) { int i = n; Later(i * 0.4, () => Sparkles(i % 2 == 0 ? "♪" : "♫", 1, i % 3 == 0 ? "#FF8FB1" : "#C3A6FF", rise: true)); }
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.08, TimeSpan.FromSeconds(0.4)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
                break;
            case "bake":
            {
                var cake = Prop("🧁", "#FF8FB1", 26, 80, 70, 2.6, emoji: true);
                cake?.BeginAnimation(OpacityProperty, Keys((0, 0), (0.8, 0), (1.1, 1), (2.3, 1), (2.6, 0)));
                Later(1.0, () => Sparkles("✦", 5, "#FFE27A"));
                _companionMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-2, 2, TimeSpan.FromMilliseconds(100)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) });
                break;
            }
            case "selfie":
            {
                Prop("📱", "#AAB4C8", 22, 84, 40, 2.2, emoji: true);
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.4, -10), (1.6, -10), (2, 0)));
                Later(1.1, () => { Sparkles("✦", 8, "#FFFFFF"); CompanionSounds.Effect("chime"); });
                break;
            }
            case "hero":
            {
                // Flies up with a little cape streak behind, hovers heroically, and lands.
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.6, -70), (2.0, -64), (2.6, 0)));
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.6, -12), (2.0, -12), (2.6, 0)));
                for (int i = 0; i < 6; i++) Later(0.2 + i * 0.25, () => Sparkles("❘", 1, "#E04F5F", rise: false));
                CompanionSounds.Effect("slide");
                break;
            }
            case "ninja":
            {
                // A puff of smoke, gone, and back in a different spot with another puff.
                Sparkles("☁", 6, "#AAB4C8");
                CompanionSounds.Effect("pop");
                _companionSprite.BeginAnimation(OpacityProperty, Keys((0, 1), (0.15, 0), (1.1, 0), (1.3, 1)));
                Later(0.6, () =>
                {
                    var (_, to) = CompanionRoamTarget(150);
                    _companionPos.BeginAnimation(TranslateTransform.XProperty, null);
                    _companionPos.X = to;
                });
                Later(1.1, () => { Sparkles("☁", 6, "#AAB4C8"); CompanionSounds.Effect("pop"); });
                break;
            }
            case "breakdance":
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.3, 180), (1.5, 180 + 720), (1.9, 1080), (2.2, 1080)));
                _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0, 1), (0.3, 1.15), (1.6, 1.15), (2.0, 1)));
                Later(2.25, () => _companionTurn.BeginAnimation(RotateTransform.AngleProperty, null));
                Sparkles("✦", 6, "#C3A6FF");
                break;
            case "hiccup":
                for (int i = 0; i < 3; i++)
                {
                    double at = 0.2 + i * 0.75;
                    Later(at, () =>
                    {
                        _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.1, -14), (0.25, 0)));
                        Prop("hic!", "#AAB4C8", 12, 62 + Random.Shared.Next(-8, 8), 28, 0.7);
                        CompanionSounds.Effect("pop");
                    });
                }
                break;
            case "sneak":
            {
                var (from, to) = CompanionRoamTarget(110);
                CompanionGlideX(to, 2.6, new SineEase { EasingMode = EasingMode.EaseInOut });
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.88, TimeSpan.FromSeconds(0.3)) { AutoReverse = true, BeginTime = TimeSpan.Zero });
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3, TimeSpan.FromSeconds(0.32)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) });
                Prop("…", "#AAB4C8", 16, 64, 30, 2.4);
                _companionLook = to < from ? -1 : 1; _companionLookUntil = DateTime.Now.AddSeconds(2.6);
                break;
            }
            case "drums":
            {
                Prop("🥁", "#E04F5F", 26, 30, 92, 2.6, emoji: true);
                for (int i = 0; i < 6; i++) Later(0.2 + i * 0.38, () => { Prop(Random.Shared.Next(2) == 0 ? "ba" : "dum", "#FFD84D", 12, 20 + Random.Shared.Next(60), 30, 0.6); });
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -5, TimeSpan.FromSeconds(0.19)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(7) });
                Later(2.4, () => CompanionSounds.Effect("drum"));
                break;
            }
            case "levitate":
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.8, -26), (1.6, -20), (2.4, -28), (3.1, 0)));
                for (int i = 0; i < 4; i++) Later(0.4 + i * 0.6, () => Sparkles("✧", 2, "#BDE6FF", rise: true));
                break;
            case "laugh":
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -4, TimeSpan.FromSeconds(0.09)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(8) });
                Prop("HA", "#FFD84D", 14, 70, 20, 1.0);
                Later(0.5, () => Prop("HA", "#FFD84D", 13, 14, 26, 1.0));
                Later(1.0, () => Prop("HA!", "#FFD84D", 15, 66, 12, 1.0));
                CompanionSounds.Effect("giggle");
                break;
            case "shiver":
                _companionMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-3, 3, TimeSpan.FromMilliseconds(45)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(20) });
                for (int i = 0; i < 4; i++) Later(i * 0.5, () => Sparkles("❄", 1, "#BDE6FF", rise: false));
                break;
            case "cool":
            {
                // Puts on shades: they drop down onto its face.
                var shades = Prop("⌐■-■", "#1E222B", 15, 30, 0, 2.5);
                shades?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(0, 72, TimeSpan.FromSeconds(0.5)) { EasingFunction = new BounceEase { Bounces = 1, Bounciness = 3 } });
                Later(0.6, () => Sparkles("✦", 3, "#FFFFFF"));
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.6, 0), (0.8, 6), (2.2, 6), (2.5, 0)));
                break;
            }
            case "point":
            {
                // Walks over under the game you've highlighted and points up at it.
                double target = _companionPointTo ?? _companionPos.X;
                _companionPointTo = null;
                double secs = Math.Clamp(Math.Abs(target - _companionPos.X) / 220, 0.4, 3.2);
                _companionActionUntil = DateTime.Now.AddSeconds(secs + 1.8);
                CompanionGlideX(target, secs, new SineEase { EasingMode = EasingMode.EaseInOut });
                int steps = Math.Max(2, (int)(secs / 0.3));
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -5, TimeSpan.FromSeconds(0.15)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(steps) });
                _companionLook = target < _companionPos.X ? -1 : 1; _companionLookUntil = DateTime.Now.AddSeconds(secs);
                Later(secs, () =>
                {
                    var arrow = Prop("▲", "#FFD84D", 18, 52, 30, 1.8);
                    arrow?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(30, 18, TimeSpan.FromSeconds(0.3)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.15, -12), (0.3, 0)));
                    _companionLook = 0;
                });
                break;
            }
            case "boop":
                _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0, 1), (0.08, 0.8), (0.3, 1.12), (0.5, 1)));
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (0.08, 1.15), (0.3, 0.9), (0.5, 1)));
                Prop("!", "#FFD84D", 22, 74, 20, 0.9);
                CompanionSounds.Effect("pop");
                break;
            case "hug":
                _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0, 1), (0.25, 1.22), (1.6, 1.22), (2.0, 1)));
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (0.25, 0.86), (1.6, 0.86), (2.0, 1)));
                Sparkles("♥", 7, "#FF5A7A", rise: true);
                CompanionSounds.Effect("kiss");
                break;
            case "dangle":
                CompanionDangle(); // 9.0.17: four ways of hanging on
                break;
            case "drop":
                CompanionLand(); // 9.0.17: five ways of landing
                break;
            case "sulk":
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(3);
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (0.5, 0.9), (2.6, 0.9), (3.1, 1)));
                Prop("…", "#AAB4C8", 18, 14, 30, 2.8);
                break;
            case "snowball":
            {
                var ball = Prop("●", "#FFFFFF", 16, 60, 70, 1.8);
                ball?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(60, -260, TimeSpan.FromSeconds(1.2)) { EasingFunction = new SineEase() });
                ball?.BeginAnimation(Canvas.TopProperty, Keys((0, 70), (0.6, 0), (1.2, 90)));
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.15, 14), (0.4, -10), (0.7, 0)));
                Later(1.2, () => Sparkles("❄", 4, "#FFFFFF"));
                break;
            }
            case "wink":
                Prop("✧", "#FFE27A", 14, 66, 50, 1.2);
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.3, 8), (1.2, 8), (1.5, 0)));
                break;
            case "shrug":
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.25, -6), (1.2, -6), (1.5, 0)));
                Prop("?", "#AAB4C8", 18, 14, 30, 1.6);
                Prop("?", "#AAB4C8", 14, 78, 34, 1.6);
                break;
            case "victory":
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -24, TimeSpan.FromSeconds(0.22)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3), EasingFunction = ease });
                foreach (var hex in new[] { "#FF6B8A", "#FFD84D", "#7FD8BE", "#7FB8FF" }) Sparkles("★", 2, hex);
                CompanionSounds.Effect("chime");
                break;
            case "peekedge":
            {
                // Tiptoes to the edge of its range and peeks over, then strolls back.
                double edge = CompanionBase().X - CompanionRange();
                double back = _companionPos.X;
                CompanionGlideX(edge, 1.0, new SineEase { EasingMode = EasingMode.EaseInOut });
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (1.0, 0), (1.3, -18), (1.9, -18), (2.1, 0)));
                Later(2.0, () => CompanionGlideX(back, 0.8, new SineEase { EasingMode = EasingMode.EaseInOut }));
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(2);
                break;
            }
        }
    }

    // ---------------------------------------------------------------- pointing at the highlighted game

    private double? _companionPointTo;

    /// <summary>Walks under the highlighted tile on Home and points at it, saying something about it. False if the tile can't be found.</summary>
    private bool CompanionPointAt(string line)
    {
        if (_companion == null || _screens.Count > 0 || GameList.SelectedItem == null) return false;
        if (GameList.ItemContainerGenerator.ContainerFromItem(GameList.SelectedItem) is not FrameworkElement tile || !tile.IsVisible) return false;
        double width = RootGrid.ActualWidth;
        if (width <= 0) return false;
        double center = tile.TranslatePoint(new Point(tile.ActualWidth / 2, 0), RootGrid).X;
        double home = width - 6 - 60; // where its middle is when it stands in its corner
        double bx = CompanionBase().X;
        double target = Math.Clamp(center - home, bx - CompanionRange(), bx);
        _companionPointTo = target;
        CompanionQueue("point", line);
        return true;
    }

    // ---------------------------------------------------------------- wit

    /// <summary>Extra things to say about a game, with a bit of attitude.</summary>
    private List<string> CompanionWitFor(Game g)
    {
        var lines = new List<string>();
        double hours = Labs.HoursOf(g, Playtime);
        if (hours >= 500) lines.Add($"{Labs.Hours(hours)}? Should I give you two some privacy?");
        else if (hours >= 200) lines.Add("At this point, it's not a game. It's a lifestyle.");
        else if (hours >= 50) lines.Add("You and this one go way back, huh?");
        if (hours is > 0 and < 1) lines.Add("Barely tried it. Commitment issues?");
        if (!g.IsInstalled) lines.Add("It's not even installed. Bold of you to look at it like that.");
        if (g.IsFavorite) lines.Add("Ah, the favorite. I'm not jealous. (I'm a little jealous.)");
        var last = g.LastPlayed ?? g.RecentAt;
        if (last > DateTime.MinValue.AddYears(1) && (DateTime.Now - last).TotalDays > 365) lines.Add("This one's been waiting over a year. It misses you.");
        if (DateTime.Now.Hour is >= 1 and < 5) lines.Add("It's late. One more game always becomes four.");
        return lines;
    }

    /// <summary>Extra lines and acts for pages (on top of what each page already has).</summary>
    private static (string[] Idle, string[] Lines)? CompanionMoreFor(Screen? screen) => screen switch
    {
        null => (new[] { "skate", "guitar", "sing", "bake", "selfie", "hero", "ninja", "breakdance", "hiccup", "sneak", "drums", "levitate", "cool", "wink", "peekedge", "snowball" },
            new[] { "I alphabetized your games. Just kidding. Or am I?", "Scrolling is cardio, right?", "I'm not saying pick a game, but… pick a game.",
                    "Fun fact: I'm 12 pixels tall and 100% heart.", "If you need me, I'll be here. Literally always here.", "Is it just me, or is this row looking extra good today?",
                    "Plot twist: the real game was the friends we made along the way.", "My favorite game? You picking one." }),
        SettingsScreen => (new[] { "cool", "shrug" }, new[] { "Don't touch that one. Kidding! Touch everything.", "Ah yes, settings. Where fun goes to get organized.",
                    "If you break something, I saw nothing.", "Have you tried turning it off and on again?" }),
        LibraryScreen or Switch2LibraryScreen => (new[] { "sneak", "selfie" }, new[] { "Your backlog has a backlog.", "So many games, so little time. Mostly little time.",
                    "Sorting by 'will actually play'… zero results.", "I see you buying games during sales." }),
        StoresScreen or WishlistScreen or FreeGamesScreen => (new[] { "selfie", "cool" }, new[] { "Your wallet just flinched.", "Wishlist: a list of dreams. And sales.",
                    "Free? Say no more.", "Just browsing. Sure. That's what they all say." }),
        MediaScreen => (new[] { "guitar", "drums", "sing", "breakdance" }, new[] { "Requesting a song: anything I can dance to.", "DJ, turn it up!", "My dance moves are open source." }),
        CapturesScreen => (new[] { "selfie", "cool" }, new[] { "Frame-worthy!", "That's your best screenshot. Don't tell the others.", "Is that me in the background?" }),
        FriendsScreen => (new[] { "wink", "sing" }, new[] { "Your friends seem cool. I'm cooler, but still.", "Invite them! More players, more fun." }),
        DetailsScreen => (new[] { "point", "selfie" }, new[] { "Ooh, reading the lore?", "Trailer? Trailer! Trailer!", "Let's just press play and find out." }),
        _ => null,
    };

    private static readonly string[] TickleComebacks =
    {
        "Hey! Personal space, please!", "I'm going to start charging for this.", "Okay, now you're just showing off.", "Are you trying to unlock an achievement?",
        "Achievement unlocked: Most Annoying Finger.", "I bite. (I don't. But I could. I won't.)", "Is this how you treat all your companions?",
        "I'll remember this. I have a really good memory.", "Fine. FINE. Tickle war it is!", "You know I'm not a button, right?",
    };

    private static readonly string[] IdleLines =
    {
        "Hellooo? Did you fall asleep?", "I'll just… stand here. Being cute.", "Should I start a podcast? 'Waiting For You', episode 1.",
        "The games won't play themselves. I checked.", "*taps foot*", "I counted to a thousand. Twice.", "Are we still friends? Blink twice.",
    };

    private readonly DispatcherTimer _companionWit = new() { Interval = TimeSpan.FromSeconds(20) };
    private bool _companionWitHooked, _companionIdleSaid;
    private DateTime _companionLateAt, _companionNeedAt;
    private int _companionScrolls;

    private void StartCompanionWit()
    {
        if (!_companionWitHooked) { _companionWit.Tick += (_, _) => CompanionWitTick(); _companionWitHooked = true; }
        _companionWit.Start();
    }

    /// <summary>Every 20 seconds: something cheeky when you've gone quiet, it's very late, or (with the playground) it needs something.</summary>
    private void CompanionWitTick()
    {
        if (_companion == null || _companionAsleep || _companionNap || Theme.ReduceMotion || _companion.Visibility != Visibility.Visible) return;
        if (_companionAction != null && DateTime.Now < _companionActionUntil) return;
        var now = DateTime.Now;
        var quiet = DateTime.UtcNow - _lastInput;
        if (quiet > TimeSpan.FromSeconds(90) && !_companionIdleSaid)
        {
            _companionIdleSaid = true;
            CompanionQueue(Random.Shared.Next(2) == 0 ? "sulk" : "shrug", Pick(IdleLines, MoreIdleLines));
            return;
        }
        if (quiet < TimeSpan.FromSeconds(30)) _companionIdleSaid = false;
        if (now.Hour is >= 1 and < 5 && now - _companionLateAt > TimeSpan.FromMinutes(25))
        {
            _companionLateAt = now;
            CompanionQueue("yawn", new[] { $"It's {now:h:mm}. The backlog will still be here tomorrow.", "Bedtime was hours ago. I'm telling.", "Night owl mode: activated. Hoot." }[Random.Shared.Next(3)]);
            return;
        }
        if (CompanionLife.On && now - _companionNeedAt > TimeSpan.FromMinutes(12))
        {
            CompanionLife.CatchUp();
            if (CompanionLife.NeedLine() is { } need)
            {
                _companionNeedAt = now;
                string act = CompanionLife.Mood() switch { "hungry" => "sandwich", "sleepy" => "yawn", _ => "sulk" };
                CompanionQueue(act, need + " (Settings > Home screen > Companion playground)");
            }
        }
    }

    /// <summary>Counted on Home: a lot of scrolling without picking anything gets a comment.</summary>
    private void CompanionCountScroll()
    {
        if (_companion == null) return;
        if (++_companionScrolls < 45) return;
        _companionScrolls = 0;
        if (Random.Shared.Next(2) == 0)
            CompanionQueue("shrug", new[] { "Can't decide? Eeny, meeny, miny… that one!", "You've scrolled past everything twice. I'm dizzy for you.",
                                            "Analysis paralysis! Just pick the shiny one.", "Want me to choose? I'm great at choosing. I choose snacks." }[Random.Shared.Next(4)]);
    }

    // ---------------------------------------------------------------- touching it

    private Point? _touchDown;
    private DateTime _touchDownAt, _lastTapAt, _lastBoopAt;
    private bool _touchDragging, _touchHeld;
    private double _touchStartX;
    private readonly DispatcherTimer _touchHold = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _touchHoldHooked;

    /// <summary>Tap to pet, double-tap to boop, hold to hug, drag to carry it somewhere else along the bottom.</summary>
    private void HookCompanionTouch()
    {
        if (_companionSprite == null) return;
        if (!_touchHoldHooked)
        {
            _touchHold.Tick += (_, _) =>
            {
                _touchHold.Stop();
                if (_touchDown == null || _touchDragging) return;
                _touchHeld = true;
                CompanionReact("hug");
            };
            _touchHoldHooked = true;
        }
        _companionSprite.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _touchDown = e.GetPosition(RootGrid);
            _touchDownAt = DateTime.Now;
            _touchDragging = _touchHeld = false;
            _touchStartX = _companionPos.X;
            _companionSprite.CaptureMouse();
            _touchHold.Stop();
            _touchHold.Start();
        };
        _companionSprite.MouseMove += (_, e) =>
        {
            if (_touchDown is not { } down || !_companionSprite.IsMouseCaptured) return;
            var p = e.GetPosition(RootGrid);
            double dx = p.X - down.X, dy = p.Y - down.Y;
            if (!_touchDragging && (Math.Abs(dx) > 16 || Math.Abs(dy) > 16))
            {
                _touchDragging = true;
                _touchHold.Stop();
                _companionPos.BeginAnimation(TranslateTransform.XProperty, null);
                _companionQueue.Clear();
                CompanionReact("dangle");
            }
            if (!_touchDragging) return;
            double bx = CompanionBase().X;
            _companionPos.X = Math.Clamp(_touchStartX + dx, bx - CompanionRange(), bx);
            _companionMove.BeginAnimation(TranslateTransform.YProperty, null);
            _companionMove.Y = Math.Clamp(dy, -140, 0) - 18;
            CompanionDragMoved(p); // 9.0.17: flings and shakes
        };
        _companionSprite.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _touchHold.Stop();
            if (_companionSprite.IsMouseCaptured) _companionSprite.ReleaseMouseCapture();
            if (_touchDown == null) return;
            _touchDown = null;
            _lastInput = DateTime.UtcNow;
            if (_touchDragging) { _touchDragging = false; CompanionReact("drop"); return; }
            if (_touchHeld) { _touchHeld = false; return; }
            var now = DateTime.Now;
            // A quick double tap boops its nose (once in a while: keep tapping and it's tickling).
            if (now - _lastTapAt < TimeSpan.FromMilliseconds(330) && now - _lastBoopAt > TimeSpan.FromSeconds(3))
            {
                _lastTapAt = DateTime.MinValue;
                _lastBoopAt = now;
                CompanionReact("boop");
                return;
            }
            _lastTapAt = now;
            CompanionPet();
        };
        _companionSprite.LostMouseCapture += (_, _) =>
        {
            if (!_touchDragging) return;
            _touchDragging = false;
            _touchDown = null;
            CompanionReact("drop");
        };
    }

    /// <summary>Poked too many times in a row: a comeback (or, now and then, it just gets dizzy as before).</summary>
    private string TickleComeback() => Pick(TickleComebacks, MoreTickleComebacks);

    private static string Pick(string[] a, string[] b) { int i = Random.Shared.Next(a.Length + b.Length); return i < a.Length ? a[i] : b[i - a.Length]; }
}
