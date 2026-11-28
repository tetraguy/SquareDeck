using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The neighborhood (9.0.6): down the path from the yard. A café, the post office, an open-air stage, a pond and the
/// bulletin board, with five neighbors who have their own personalities, hearts to earn, side missions to help with,
/// and games: Memory Match at the café, Beat Hop on the stage, fishing at the pond, and rock, paper, scissors with
/// anyone. Also here: your companion's own menu (jokes, a dance, hide-and-seek in the yard) and the quest board.
/// </summary>
public sealed partial class CompanionPlayground
{
    private const double TownFloor = 652;
    private readonly Canvas _town = new() { Width = W, Height = H, Visibility = Visibility.Collapsed };
    private readonly Rectangle _townSky = new() { Width = W, Height = H };
    private readonly List<Spot> _townSpots = new();
    private readonly Dictionary<string, Canvas> _neighborSprites = new();

    /// <summary>A side mission: what's asked, what kind of thing finishes it, and the thanks.</summary>
    private sealed record Request(string Text, string Kind, string Target, int Number, int Stars, string Thanks, string? Reward = null, string? RewardName = null);

    private sealed record Neighbor(string Id, string Name, string Species, string Color, string Outfit, string Accessory, double X, string Role,
                                   string Favorite, string[] Chat, Request[] Story);

    private static readonly Neighbor[] Neighbors =
    {
        new("mochi", "Mochi", "cat", "#FFB38A", "", "bowtie", 170, "runs the café", "sushi",
            new[] { "Welcome to Café Mochi! The cocoa is warm and the naps are free.", "I named every fish in the pond. They don't come when called.",
                    "A good café is 10% coffee and 90% cozy.", "Have you tried the cake? I may have taste-tested it. Six times." },
            new[]
            {
                new Request("Bring me some sushi from your fridge. For… research.", "give", "sushi", 0, 15, "Mmm. Research complete. Very thorough research."),
                new Request("Beat my record: finish Memory Match in 14 moves or fewer.", "memory", "", 14, 20, "14 or fewer?! You have the memory of a cat. That's a compliment."),
                new Request("Catch a golden koi for the café's tank. They only bite now and then!", "fish", "goldkoi", 0, 40, "It's beautiful! Have this fish tank for your house. Tank you!", "fishtank", "Fish tank"),
            }),
        new("pepper", "Pepper", "bunny", "#FF8FB1", "cap", "", 410, "delivers the mail", "icecream",
            new[] { "Special delivery! …Oh, it's just me. Hi!", "I've hopped this street 3,000 times today. Probably.", "Rain or shine, the mail gets there. Mostly shine.",
                    "Did you get your letter? I deliver those! Well, the bird does. I supervise." },
            new[]
            {
                new Request("Could you take this parcel to Bramble by the pond? My feet are tired.", "deliver", "bramble", 0, 12, "Delivered? You're a natural mail carrier!"),
                new Request("Beat me at rock, paper, scissors! Nobody ever beats me.", "rps", "", 0, 15, "You beat me?! Rematch tomorrow. And the day after."),
                new Request("Bring me an ice cream. Delivering is hot work!", "give", "icecream", 0, 25, "Brain freeze! Worth it. Here, take this telescope I found in a parcel.", "telescope", "Telescope"),
            }),
        new("ziggy", "Ziggy", "robot", "#7FB8FF", "", "glasses", 600, "invents things", "energy",
            new[] { "Beep boop! Greetings, organic friend.", "My latest invention: a toaster that only toasts happiness. Results pending.",
                    "Error 404: boredom not found when you're around.", "I calculated that you're 98.7% awesome. The rest is rounding." },
            new[]
            {
                new Request("Help calibrate my star sensor: score 15 or more in Star Catch.", "starcatch", "", 15, 15, "Sensor calibrated! Stars detected: so many."),
                new Request("Bring me a star soda. Robots run on fizz.", "give", "energy", 0, 15, "*fizz* Systems at 200%! Whee!"),
                new Request("I dropped a gear somewhere in town. Could you search around?", "find", "Ziggy's gear", 0, 30, "My gear! You found it! Here, I built you a monocle. It improves vision by 0%.", "acc:monocle", "Monocle"),
            }),
        new("luna", "Luna", "ghost", "#C3A6FF", "", "star", 790, "plays music on the stage", "cake",
            new[] { "Ooooo~ that's my singing warm-up.", "Music is just organized vibes.", "I've been practicing this song for 300 years. Almost ready.",
                    "Want to hear a spooky song? It's mostly about snacks." },
            new[]
            {
                new Request("Play Beat Hop and get a combo of 10 or more!", "beatcombo", "", 10, 15, "What rhythm! You could haunt a concert hall."),
                new Request("Take this song I wrote to Pepper at the post office, please?", "deliver", "pepper", 0, 15, "She liked it? Ooooo~ I'm blushing. Can ghosts blush?"),
                new Request("Get 80% or better in Beat Hop. Then we'll start a band.", "beatgrade", "", 80, 40, "We're a band now! Here, this disco ball is our first stage prop.", "disco", "Disco ball"),
            }),
        new("bramble", "Bramble", "dino", "#7FD8BE", "", "", 1010, "tends the garden by the pond", "pizza",
            new[] { "Rawr! Oh, sorry. That's how I say good morning.", "I planted a seed yesterday. I've been watching it ever since.",
                    "My arms are tiny but my heart is huge.", "The pond fish and I have an understanding. They swim, I watch." },
            new[]
            {
                new Request("I lost my hat! I think I left it somewhere in your yard?", "find", "Bramble's hat", 0, 15, "My hat! My beautiful hat! You're the best.", "acc:sweatband", "Sweatband"),
                new Request("Could you bring me an apple from your tree? For a pie!", "give", "apple", 0, 12, "This will make a perfect pie. I'll save you a slice!"),
                new Request("Catch a carp for my garden pond, please? Any carp will do.", "fish", "carp", 0, 20, "He looks happy already! I'll name him Gerald."),
            }),
    };

    private static Neighbor? FindNeighbor(string id) => Neighbors.FirstOrDefault(n => n.Id == id);

    // ---------------------------------------------------------------- drawing the town

    private static readonly string[] CafeMap =
    {
        "kkkkkkkkkkkkkkkkkkkkkkkkkk", "kbbbbbbbbbbbbbbbbbbbbbbbbk", "kbbbbbbbbbbbbbbbbbbbbbbbbk", "kbbbbbbbbbbbbbbbbbbbbbbbbk",
        "kkkkkkkkkkkkkkkkkkkkkkkkkk", "kpwpwpwpwpwpwpwpwpwpwpwpwk", "kpwpwpwpwpwpwpwpwpwpwpwpwk", ".kpkwkpkwkpkwkpkwkpkwkpkk.",
        "..kbbbbbbbbbbbbbbbbbbbbk..", "..kbkkkkkkbbbbbbkkkkkkbk..", "..kbkeeeekbbbbbbkeeeekbk..", "..kbkeceekbkkkkbkeceekbk..",
        "..kbkeeeekbkddkbkeeeekbk..", "..kbkkkkkkbkddkbkkkkkkbk..", "..kbbbbbbbbkdykbbbbbbbbk..", "..kbbbbbbbbkddkbbbbbbbbk..",
        "..kBBBBBBBBkddkBBBBBBBBk..", "..kkkkkkkkkkkkkkkkkkkkkk..",
    };

    private static readonly string[] PostMap =
    {
        "....kkkkkkkkkkkkkkkk....", "...kCCCCCCCCCCCCCCCCk...", "..kCCCCCCCCCCCCCCCCCCk..", ".kkkkkkkkkkkkkkkkkkkkkk.",
        "..kffffffffffffffffffk..", "..kfkkkkkkfffkkkkkkkfk..", "..kfkeeeekfffkeeeeekfk..", "..kfkeeeekfkkkeeeeekfk..",
        "..kfkkkkkkfkdkkkkkkkfk..", "..kffffffffkdkffffffk...", "..kffffffffkykffffffk...", "..kfffffffkddkffffffk...",
        "..kFFFFFFFkddkFFFFFFk...", "..kkkkkkkkkkkkkkkkkkkk..",
    };

    private static readonly string[] StageMap =
    {
        "kkkkkkkkkkkkkkkkkkkkkkkkkkkkkk", "kxxxxxxxxxxxxxxxxxxxxxxxxxxxxk", "kxzxxxxxxxxxxxxxxxxxxxxxxxxzxk", "kxxk......................kxxk",
        "kxxk......................kxxk", "kxxk......................kxxk", "kxxk......................kxxk", "kxxk......................kxxk",
        "kxxk......................kxxk", "kxxk......................kxxk", "kkkkkkkkkkkkkkkkkkkkkkkkkkkkkk", "kqqqqqqqqqqqqqqqqqqqqqqqqqqqqk",
        "kQQQQQQQQQQQQQQQQQQQQQQQQQQQQk", "kkkkkkkkkkkkkkkkkkkkkkkkkkkkkk",
    };

    private static readonly string[] BoardMap =
    {
        "kkkkkkkkkkkkkk", "kqqqqqqqqqqqqk", "kqffwfqfffwfqk", "kqffffqffffqqk", "kqqfwffqqfffqk", "kqffffqfwffqqk", "kqqqqqqqqqqqqk",
        "kkkkkkkkkkkkkk", ".....kQQk.....", ".....kQQk.....", ".....kQQk.....", "....kkQQkk....",
    };

    private static readonly string[] SignMap =
    {
        "kkkkkkkk", "kqqqqqqk", "kqwwwwqk", "kqqqqqqk", "kkkkkkkk", "...kk...", "...qQ...", "...qQ...", "...qQ...", "..kqQk..",
    };

    private static readonly string[] StreetLampMap =
    {
        ".kkkk.", "kyyyyk", "kyyyyk", ".kkkk.", "..kk..", "..SS..", "..SS..", "..SS..", "..SS..", "..SS..", "..SS..", "..SS..", "..SS..", ".kSSk.", "kkkkkk",
    };

    private void BuildTown()
    {
        _town.Children.Clear();
        _townSpots.Clear();
        _neighborSprites.Clear();
        _town.Children.Add(_townSky);
        // Houses far away on the hill.
        var hill = new Path { Data = Geometry.Parse("M0,470 C200,410 420,430 640,460 C860,420 1080,410 1280,450 L1280,720 L0,720 Z"), Fill = new SolidColorBrush(PixelArt.C("#7CC27E")) };
        _town.Children.Add(hill);
        for (int i = 0; i < 7; i++)
        {
            var roof = new Path { Data = Geometry.Parse("M0,20 L18,0 L36,20 Z"), Fill = new SolidColorBrush(PixelArt.C(new[] { "#D9534F", "#4A7BD9", "#F2C14E", "#9B7BFF" }[i % 4])) };
            var wall = new Rectangle { Width = 30, Height = 22, Fill = new SolidColorBrush(PixelArt.C("#F6E7C8")) };
            double hx = 80 + i * 175, hy = 430 + (i % 3) * 10;
            Canvas.SetLeft(roof, hx); Canvas.SetTop(roof, hy - 20);
            Canvas.SetLeft(wall, hx + 3); Canvas.SetTop(wall, hy);
            _town.Children.Add(wall); _town.Children.Add(roof);
        }
        // The sidewalk and the street.
        var walk = new Rectangle { Width = W, Height = 110, Fill = new SolidColorBrush(PixelArt.C("#D8D2C4")) };
        Canvas.SetTop(walk, 560);
        _town.Children.Add(walk);
        for (int x = 0; x < W; x += 64) { var seam = new Rectangle { Width = 2, Height = 110, Fill = new SolidColorBrush(PixelArt.C("#BDB6A6")) }; Canvas.SetLeft(seam, x); Canvas.SetTop(seam, 560); _town.Children.Add(seam); }
        var road = new Rectangle { Width = W, Height = 60, Fill = new SolidColorBrush(PixelArt.C("#5A5F6B")) };
        Canvas.SetTop(road, 670);
        _town.Children.Add(road);
        for (int x = 20; x < W; x += 120) { var dash = new Rectangle { Width = 60, Height = 6, Fill = new SolidColorBrush(PixelArt.C("#F2C14E")) }; Canvas.SetLeft(dash, x); Canvas.SetTop(dash, 696); _town.Children.Add(dash); }

        // The café.
        var cafe = Put(_town, CafeMap, 8, 40, 560 - CafeMap.Length * 8);
        var cafeSign = new TextBlock { Text = "CAFÉ MOCHI", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(PixelArt.C("#8B5A3C")) };
        Canvas.SetLeft(cafeSign, 86); Canvas.SetTop(cafeSign, 560 - CafeMap.Length * 8 + 6);
        _town.Children.Add(cafeSign);
        _townSpots.Add(new Spot("cafe", "Café Mochi", Area(cafe), () => CafeAsync()));
        // The post office.
        var post = Put(_town, PostMap, 8, 300, 560 - PostMap.Length * 8);
        var postSign = new TextBlock { Text = "✉ POST", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        Canvas.SetLeft(postSign, 360); Canvas.SetTop(postSign, 560 - PostMap.Length * 8 + 4);
        _town.Children.Add(postSign);
        _townSpots.Add(new Spot("post", "Post office", Area(post), () => PostAsync()));
        // The bulletin board.
        var board = Put(_town, BoardMap, 6, 498, 560 - BoardMap.Length * 6);
        _townSpots.Add(new Spot("board", "Quest board", Area(board), () => { ShowQuests(); return Task.CompletedTask; }));
        // The stage.
        var stage = Put(_town, StageMap, 8, 660, 560 - StageMap.Length * 8 + 24);
        var notes = new TextBlock { Text = "♪  ♫  ♪", FontSize = 22, Foreground = new SolidColorBrush(PixelArt.C("#F2C14E")), IsHitTestVisible = false };
        Canvas.SetLeft(notes, 760); Canvas.SetTop(notes, 400);
        notes.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        _town.Children.Add(notes);
        _townSpots.Add(new Spot("stage", "Stage: Beat Hop", Area(stage), () => BeatHopAsync()));
        // The pond.
        var pond = new Ellipse { Width = 230, Height = 70, Fill = new LinearGradientBrush(PixelArt.C("#7FD8FF"), PixelArt.C("#3A8FD9"), 90), Stroke = new SolidColorBrush(PixelArt.C("#B5A27A")), StrokeThickness = 6 };
        Canvas.SetLeft(pond, 960); Canvas.SetTop(pond, 570);
        _town.Children.Add(pond);
        for (int i = 0; i < 3; i++)
        {
            var ripple = new Ellipse { Width = 30, Height = 8, Stroke = Brushes.White, StrokeThickness = 1.5, Opacity = 0.6, IsHitTestVisible = false };
            Canvas.SetLeft(ripple, 1000 + i * 55); Canvas.SetTop(ripple, 595 + (i % 2) * 14);
            ripple.BeginAnimation(OpacityProperty, new DoubleAnimation(0.1, 0.7, TimeSpan.FromSeconds(1.2 + i * 0.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            _town.Children.Add(ripple);
        }
        foreach (double rx in new[] { 965.0, 1172.0 })
            for (int r = 0; r < 3; r++)
            {
                var reed = new Rectangle { Width = 3, Height = 30 + r * 6, Fill = new SolidColorBrush(PixelArt.C("#3E8E48")) };
                Canvas.SetLeft(reed, rx + r * 6); Canvas.SetTop(reed, 556 - r * 6);
                _town.Children.Add(reed);
            }
        _townSpots.Add(new Spot("pond", "Pond: go fishing", new Rect(952, 556, 246, 96), () => FishingAsync()));
        // Lamps.
        foreach (double lx in new[] { 270.0, 620.0, 920.0 }) Put(_town, StreetLampMap, 6, lx, 560 - StreetLampMap.Length * 6 + 12);
        // The way home.
        var sign = Put(_town, SignMap, 6, 1222, 560 - SignMap.Length * 6 + 6);
        var arrow = new TextBlock { Text = "▶", FontSize = 16, Foreground = new SolidColorBrush(PixelArt.C("#8B5A3C")), IsHitTestVisible = false };
        Canvas.SetLeft(arrow, 1240); Canvas.SetTop(arrow, 560 - SignMap.Length * 6 + 10);
        _town.Children.Add(arrow);
        _townSpots.Add(new Spot("home", "Back home", Area(sign), () => GoHomeFromTown()));

        // The neighbors, each by their place.
        foreach (var n in Neighbors)
        {
            var sprite = new Canvas { Width = 72, Height = 72 };
            CompanionArt.Draw(sprite, n.Species, n.Color, "happy", false, n.Outfit, 0, false, 6, n.Accessory);
            double top = n.Id == "luna" ? 560 - StageMap.Length * 8 + 24 + 10 * 8 - 72 : TownFloor - 76;
            Canvas.SetLeft(sprite, n.X - 36); Canvas.SetTop(sprite, top);
            sprite.RenderTransformOrigin = new Point(0.5, 1);
            var bob = new TranslateTransform();
            sprite.RenderTransform = bob;
            bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3, TimeSpan.FromSeconds(0.6 + Random.Shared.NextDouble() * 0.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            _town.Children.Add(sprite);
            _neighborSprites[n.Id] = sprite;
            var tag = new TextBlock { Text = n.Name + (StoryOfferWaiting(n) ? "  !" : ""), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, IsHitTestVisible = false };
            tag.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8 };
            Canvas.SetLeft(tag, n.X - 30); Canvas.SetTop(tag, top - 20);
            _town.Children.Add(tag);
            var nn = n;
            _townSpots.Add(new Spot("n:" + n.Id, n.Name + ", who " + n.Role, new Rect(n.X - 44, top - 10, 88, 88), () => TalkAsync(nn)));
        }
        _townSpots.Insert(0, new Spot("pal", CompanionArt.Name, Rect.Empty, () => PalMenuAsync()));
        _townSpots.Sort((a, b) => SpotX(a).CompareTo(SpotX(b)));
    }

    /// <summary>A neighbor with something new to ask, or something to hand in (an exclamation mark over their name).</summary>
    private static bool StoryOfferWaiting(Neighbor n) =>
        CompanionLife.StoryStage(n.Id) < n.Story.Length && (!CompanionLife.StoryOn(n.Id) || CompanionLife.StoryReady(n.Id));

    private void NeighborHop(string id)
    {
        if (!_neighborSprites.TryGetValue(id, out var s)) return;
        s.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(Canvas.GetTop(s), Canvas.GetTop(s) - 22, TimeSpan.FromSeconds(0.16)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) });
    }

    // ---------------------------------------------------------------- getting around

    private async Task GoTown()
    {
        await WalkTo(70);
        _actor.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.25)));
        await Wait(0.25);
        UiSound.Page();
        BuildTown();
        SetPlace("town");
        _pos.BeginAnimation(TranslateTransform.XProperty, null);
        _pos.X = 1180;
        _actor.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.25)));
        Say(new[] { "To the neighborhood!", "Let's say hi to everyone!", "I love it here. Everyone's so nice." }[Random.Shared.Next(3)]);
        await Wait(0.3);
    }

    private async Task GoHomeFromTown()
    {
        await WalkTo(1220);
        _actor.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.25)));
        await Wait(0.25);
        UiSound.Page();
        SetPlace("yard");
        _pos.BeginAnimation(TranslateTransform.XProperty, null);
        _pos.X = 90;
        _actor.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.25)));
        Say("Home sweet yard!");
        await Wait(0.3);
    }

    // ---------------------------------------------------------------- the neighbors

    private async Task TalkAsync(Neighbor n)
    {
        // Delivering something to this neighbor for someone else?
        foreach (var other in Neighbors)
        {
            int st = CompanionLife.StoryStage(other.Id);
            if (st < other.Story.Length && CompanionLife.StoryOn(other.Id) && !CompanionLife.StoryReady(other.Id) && other.Story[st] is { Kind: "deliver" } d && d.Target == n.Id)
            {
                CompanionLife.SetStoryReady(other.Id);
                NeighborHop(n.Id);
                CompanionSounds.Effect("chime");
                await Host.AskAsync(n.Name, $"Oh! Something from {other.Name}? For me? Thank you for bringing it! (Go tell {other.Name} it's delivered.)", "You're welcome!");
                CompanionLife.AddHearts(n.Id, 1);
                Quests("talk");
            }
        }
        if (CompanionLife.FirstToday("talk." + n.Id)) { CompanionLife.AddHearts(n.Id, 1); Quests("talk"); }
        while (!_closed)
        {
            int hearts = CompanionLife.Hearts(n.Id);
            int stage = CompanionLife.StoryStage(n.Id);
            var req = stage < n.Story.Length ? n.Story[stage] : null;
            var options = new List<(string Label, Func<Task<bool>> Do)>();
            options.Add(("Chat", async () => { await ChatAsync(n); return true; }));
            if (req != null)
            {
                bool on = CompanionLife.StoryOn(n.Id), ready = CompanionLife.StoryReady(n.Id);
                if (!on) options.Add(("Can I help? (side mission)", async () => { await OfferAsync(n, req); return true; }));
                else if (ready) options.Add(("It's done!", async () => { await FinishStoryAsync(n, req); return true; }));
                else if (req.Kind == "give") options.Add(($"Give the {CompanionLife.Find(req.Target)?.Name.ToLowerInvariant() ?? req.Target}", async () => { await GiveForStoryAsync(n, req); return true; }));
            }
            options.Add(("Give a gift", async () => { await GiftAsync(n); return true; }));
            options.Add(("Rock, paper, scissors", async () => { await RpsAsync(n); return true; }));
            if (n.Id == "mochi") options.Add(("Play Memory Match", async () => { await MemoryAsync(); return false; }));
            if (n.Id == "luna") options.Add(("Play Beat Hop", async () => { await BeatHopAsync(); return false; }));
            if (n.Id == "bramble") options.Add(("Go fishing together", async () => { await FishingAsync(); return false; }));
            options.Add(("Bye!", () => Task.FromResult(false)));
            string heartsText = new string('♥', hearts) + new string('♡', 10 - hearts);
            string status = req == null ? "All their side missions are done. Best of friends!"
                : CompanionLife.StoryOn(n.Id) ? (CompanionLife.StoryReady(n.Id) ? "Mission: done! Tell them." : "Mission: " + req.Text)
                : "They have something to ask you.";
            int c = await Host.AskAsync($"{n.Name}, who {n.Role}", $"{heartsText}\n{status}", options.Select(o => o.Label).ToArray());
            if (c < 0 || c >= options.Count) break;
            if (!await options[c].Do()) break;
        }
        BuildTown();
        ShowFocus();
    }

    private async Task ChatAsync(Neighbor n)
    {
        NeighborHop(n.Id);
        var lines = n.Chat.ToList();
        int hearts = CompanionLife.Hearts(n.Id);
        if (hearts >= 5) lines.Add($"You and {CompanionArt.Name} are my favorite visitors. Don't tell the others.");
        if (hearts >= 9) lines.Add("Best friends forever? Best friends forever.");
        if (IsNight) lines.Add("It's late! The stars are pretty tonight, though.");
        if (RecentGame() is { } g) lines.Add($"I heard you've been playing {g.Name}. Is it good? {CompanionArt.Name} won't stop talking about it.");
        await Host.AskAsync(n.Name, lines[Random.Shared.Next(lines.Count)], "Ha, nice!");
    }

    private async Task OfferAsync(Neighbor n, Request req)
    {
        int c = await Host.AskAsync(n.Name, req.Text + $"\n\nReward: {req.Stars} ★" + (req.RewardName != null ? " and a " + req.RewardName.ToLowerInvariant() : ""), "Sure, I'll help!", "Maybe later");
        if (c != 0) return;
        string? spot = null;
        if (req.Kind == "find")
        {
            // Hidden somewhere: in the yard for Bramble's hat, in town for Ziggy's gear.
            bool yard = n.Id == "bramble";
            var places = (yard ? new[] { "tree", "flowers", "mail", "toys", "shop" } : new[] { "cafe", "post", "board", "stage", "pond" });
            spot = places[Random.Shared.Next(places.Length)];
        }
        CompanionLife.TakeStory(n.Id, spot);
        NeighborHop(n.Id);
        Say(req.Kind switch
        {
            "find" => "A treasure hunt! Let's check every spot.",
            "deliver" => "Mail mission! Let's go.",
            "give" => "I think we have that in the fridge… or the shop does.",
            _ => "We can do it!",
        });
    }

    private async Task GiveForStoryAsync(Neighbor n, Request req)
    {
        if (!CompanionLife.TakeSnack(req.Target))
        {
            var item = CompanionLife.Find(req.Target);
            await Host.AskAsync(n.Name, $"You don't have a {item?.Name.ToLowerInvariant() ?? req.Target} yet." + (req.Target == "apple" ? " The tree in your yard grows them!" : " The Star Shop sells them."), "OK");
            return;
        }
        CompanionLife.SetStoryReady(n.Id);
        await FinishStoryAsync(n, req);
    }

    private async Task FinishStoryAsync(Neighbor n, Request req)
    {
        CompanionLife.NextStory(n.Id);
        CompanionLife.Stars += req.Stars;
        CompanionLife.AddHearts(n.Id, 2);
        string reward = $"+{req.Stars} ★";
        if (req.Reward != null)
        {
            if (req.Reward.StartsWith("acc:", StringComparison.Ordinal)) CompanionLife.Buy(req.Reward, 0, snack: false);
            else CompanionLife.Buy(req.Reward, 0, snack: false);
            reward += ", " + req.RewardName;
        }
        NeighborHop(n.Id);
        CompanionSounds.Effect("chime");
        Float("★", "#FFD84D", 8, burst: true);
        Reward(5);
        await Host.AskAsync(n.Name, req.Thanks + $"\n\n{reward}", "Yay!");
        if (req.Reward is "fishtank" or "disco" or "telescope") { BuildHouse(); BuildYard(); }
        Say(new[] { "We make a great team!", "Mission complete!", "Helping friends is the best." }[Random.Shared.Next(3)]);
    }

    private async Task GiftAsync(Neighbor n)
    {
        var snacks = CompanionLife.Snacks().ToList();
        if (snacks.Count == 0) { await Host.AskAsync(n.Name, "Your fridge is empty! Snacks make great gifts. The Star Shop has lots.", "OK"); return; }
        int c = await Host.AskAsync("Give a gift", $"What would {n.Name} like?", snacks.Select(s => $"{s.Name}  ×{CompanionLife.Count(s.Id)}").Append("Cancel").ToArray());
        if (c < 0 || c >= snacks.Count) return;
        var snack = snacks[c];
        if (!CompanionLife.TakeSnack(snack.Id)) return;
        bool loved = snack.Id == n.Favorite;
        CompanionLife.AddHearts(n.Id, loved ? 2 : 1);
        NeighborHop(n.Id);
        Quests("gift");
        CompanionSounds.Effect(loved ? "chime" : "pop");
        await Host.AskAsync(n.Name, loved ? $"{snack.Name}?! It's my favorite! How did you know?!" : new[] { $"A {snack.Name.ToLowerInvariant()}! Thank you!", "For me? You're so sweet!", "Yum! I'll save it for later. (I won't.)" }[Random.Shared.Next(3)], "♥");
        // Its own side mission might have wanted exactly this.
        int stage = CompanionLife.StoryStage(n.Id);
        if (stage < n.Story.Length && n.Story[stage] is { Kind: "give" } r && r.Target == snack.Id && CompanionLife.StoryOn(n.Id)) { CompanionLife.SetStoryReady(n.Id); await FinishStoryAsync(n, r); }
    }

    private async Task RpsAsync(Neighbor n)
    {
        string[] names = { "Rock ✊", "Paper ✋", "Scissors ✌" };
        int mine = 0, theirs = 0;
        string last = "Best of three!";
        while (mine < 2 && theirs < 2)
        {
            int c = await Host.AskAsync($"Rock, paper, scissors with {n.Name}", $"{last}\nYou {mine} – {theirs} {n.Name}", names.Append("Give up").ToArray());
            if (c < 0 || c > 2) return;
            int t = Random.Shared.Next(3);
            if (t == c) last = $"{n.Name} picked {names[t]} too. A tie!";
            else if ((c + 3 - t) % 3 == 1) { mine++; last = $"{n.Name} picked {names[t]}. You win that one!"; }
            else { theirs++; last = $"{n.Name} picked {names[t]}. They win that one!"; }
        }
        bool won = mine > theirs;
        NeighborHop(n.Id);
        if (won)
        {
            CompanionLife.Stars += 3;
            CompanionLife.AddHearts(n.Id, 1);
            Quests("rps");
            int stage = CompanionLife.StoryStage(n.Id);
            if (stage < n.Story.Length && n.Story[stage].Kind == "rps" && CompanionLife.StoryOn(n.Id)) CompanionLife.SetStoryReady(n.Id);
            Float("★", "#FFD84D", 4, burst: true);
        }
        await Host.AskAsync(n.Name, (won ? "You win! Good game! (+3 ★)" : "I win! Rematch anytime!") + $"\n{last}", "GG");
        Reward(2, fun: 6);
    }

    // ---------------------------------------------------------------- the places in town

    private async Task CafeAsync()
    {
        int c = await Host.AskAsync("Café Mochi", "Warm drinks at café prices, and a game of Memory Match at the corner table.",
            "Hot cocoa  ·  3 ★", "Star soda  ·  5 ★", "Cake slice  ·  10 ★", "Play Memory Match", "Leave");
        switch (c)
        {
            case 0 or 1 or 2:
            {
                var (id, price) = c switch { 0 => ("cocoa", 3), 1 => ("energy", 5), _ => ("cake", 10) };
                if (!CompanionLife.Buy(id, price, snack: true)) { UiSound.Error(); Say("Not enough star bits…"); return; }
                CompanionSounds.Effect("chime");
                Say("To go, please! It's in the fridge.");
                break;
            }
            case 3: await MemoryAsync(); break;
        }
    }

    private async Task PostAsync()
    {
        int c = await Host.AskAsync("Post office", "Pepper keeps the mail moving. Letters arrive in your mailbox every day.", "Check for a parcel", "Leave");
        if (c != 0) return;
        if (CompanionLife.FirstToday("parcel"))
        {
            var gifts = new[] { "cookie", "onigiri", "icecream", "cocoa" };
            string g = gifts[Random.Shared.Next(gifts.Length)];
            CompanionLife.AddSnack(g);
            CompanionSounds.Effect("chime");
            Say($"A parcel for us! A {CompanionLife.Find(g)?.Name.ToLowerInvariant()}! It's in the fridge.");
        }
        else Say("No parcels today. Come back tomorrow!");
    }

    // ---------------------------------------------------------------- your companion's own menu

    private async Task PalMenuAsync()
    {
        var options = new List<string> { "Pet", "Tell me a joke", "Dance together" };
        if (_place == "yard") options.Add("Play hide-and-seek");
        options.Add("Today's quests");
        options.Add("Cancel");
        int c = await Host.AskAsync(CompanionArt.Name, CompanionLife.NeedLine() ?? Thought(), options.ToArray());
        if (c < 0) return;
        switch (options[c])
        {
            case "Pet": await PetAsync(); break;
            case "Tell me a joke": await JokeAsync(); break;
            case "Dance together": await DanceAsync(); break;
            case "Play hide-and-seek": await HideAndSeekAsync(); break;
            case "Today's quests": ShowQuests(); break;
        }
    }

    private static readonly string[] Jokes =
    {
        "Why did the gamer bring a ladder? To reach the next level!", "Why don't skeletons play video games? They don't have the guts.",
        "What do you call a sleeping dinosaur? A dino-snore!", "Why was the computer cold? It left its Windows open.",
        "What's a ghost's favorite game? Hide-and-shriek!", "Why did the controller break up with the TV? It needed more space… bar.",
        "How do you make a tissue dance? Put a little boogie in it.", "Why did the cookie go to the doctor? It felt crummy.",
        "What do you call a fish with no eyes? A fsh.", "Why can't a bicycle stand on its own? It's two-tired.",
        "What did the ocean say to the beach? Nothing, it just waved.", "Why did the scarecrow win an award? He was outstanding in his field.",
        "I'm reading a book about anti-gravity. It's impossible to put down.", "What do you call a bear with no teeth? A gummy bear!",
        "Why did the pixel go to therapy? It had too many issues with resolution.",
    };

    private async Task JokeAsync()
    {
        var joke = Jokes[Random.Shared.Next(Jokes.Length)];
        int q = joke.IndexOf('?');
        if (q > 0 && q < joke.Length - 2)
        {
            Say(joke[..(q + 1)], 2.6);
            await Wait(2.4);
            Say(joke[(q + 1)..].Trim(), 3);
        }
        else Say(joke, 3.6);
        await Wait(1.2);
        Hop(3, 10);
        Float("HA", "#FFD84D", 3, burst: true, size: 18);
        CompanionSounds.Effect("giggle");
        Quests("joke");
        Reward(1, fun: 5);
        await Wait(1);
    }

    private async Task DanceAsync()
    {
        Say("Let's dance! Follow my moves!");
        string[] colors = { "#FF5A7A", "#FFD84D", "#7FD8FF", "#C3A6FF" };
        for (int i = 0; i < 8; i++)
        {
            switch (i % 4)
            {
                case 0: _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-18, 18, TimeSpan.FromSeconds(0.2)) { AutoReverse = true }); break;
                case 1: Hop(1, 30); break;
                case 2: _squash.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.2, TimeSpan.FromSeconds(0.2)) { AutoReverse = true }); break;
                default: _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.4))); break;
            }
            Float("♪", colors[i % 4], 1);
            Host.Rumble();
            await Wait(0.45);
        }
        _turn.BeginAnimation(RotateTransform.AngleProperty, null);
        Say("We should go on tour.");
        Reward(2, energy: -4, fun: 12);
    }

    // ---------------------------------------------------------------- hide-and-seek

    private string? _seekAt;
    private int _seekTries;

    private async Task HideAndSeekAsync()
    {
        Say("Close your eyes and count to three! No peeking!", 2.4);
        await Wait(1.6);
        _overlay.Children.Clear();
        var dark = new Rectangle { Width = W, Height = H, Fill = new SolidColorBrush(PixelArt.C("#10142A")) };
        var count = new TextBlock { FontSize = 72, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        Canvas.SetLeft(count, 560); Canvas.SetTop(count, 300);
        _overlay.Children.Add(dark);
        _overlay.Children.Add(count);
        _overlay.Visibility = Visibility.Visible;
        _ring.Visibility = _label.Visibility = Visibility.Collapsed;
        foreach (var n in new[] { "1…", "2…", "3!" }) { count.Text = n; UiSound.Move(); await Wait(0.8); }
        var places = new[] { "tree", "door", "mail", "toys", "shop", "flowers" };
        _seekAt = places[Random.Shared.Next(places.Length)];
        _seekTries = 0;
        _actor.BeginAnimation(OpacityProperty, null); // an earlier fade holds its value otherwise
        _actor.Opacity = 0;
        _overlay.Visibility = Visibility.Collapsed;
        _overlay.Children.Clear();
        _focus = Math.Max(0, Spots.FindIndex(s => s.Id == "tree"));
        ShowFocus();
        RefreshHud();
        // A giggle now and then, as a hint.
        Later(4, () => { if (_seekAt != null) CompanionSounds.Effect("giggle"); });
    }

    /// <summary>Looking behind something during hide-and-seek. True when this handled the press.</summary>
    private bool Seek(Spot s)
    {
        if (_seekAt == null) return false;
        if (s.Id is "pal") return true;
        if (s.Id == _seekAt)
        {
            FoundHider(s, won: true);
            return true;
        }
        _seekTries++;
        UiSound.Error();
        var miss = new TextBlock { Text = new[] { "Not here!", "Nope!", "Hehe, colder…" }[Random.Shared.Next(3)], FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        miss.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1 };
        Canvas.SetLeft(miss, s.Area.X + s.Area.Width / 2 - 40); Canvas.SetTop(miss, s.Area.Y - 30);
        _yard.Children.Add(miss);
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.2));
        fade.Completed += (_, _) => _yard.Children.Remove(miss);
        miss.BeginAnimation(OpacityProperty, fade);
        if (_seekTries >= 3) FoundHider(_yardSpots.First(x => x.Id == _seekAt), won: false);
        else CompanionSounds.Effect("giggle", 300);
        return true;
    }

    private void FoundHider(Spot at, bool won)
    {
        _seekAt = null;
        _pos.BeginAnimation(TranslateTransform.XProperty, null);
        _pos.X = Math.Clamp(at.Area.X + at.Area.Width / 2, 70, W - 70);
        _actor.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
        Hop(won ? 3 : 1, 30);
        if (won)
        {
            CompanionSounds.Effect("chime");
            Float("★", "#FFD84D", 5, burst: true);
            CompanionLife.Stars += 3;
            Say(new[] { "You found me! (+3 ★)", "Aww, you're good at this! (+3 ★)", "How did you know?! (+3 ★)" }[Random.Shared.Next(3)]);
            Quests("hide");
            Reward(2, fun: 12);
        }
        else
        {
            CompanionSounds.Effect("giggle");
            Say($"Ha! I was behind the {at.Label.ToLowerInvariant()} the whole time!");
            Reward(1, fun: 8);
        }
        _focus = Math.Max(0, Spots.FindIndex(s => s.Id == "pal"));
        ShowFocus();
    }

    // ---------------------------------------------------------------- lost things (side missions)

    /// <summary>Using a spot where a neighbor's lost thing is hidden finds it. True when this handled the press.</summary>
    private bool FindLost(Spot s)
    {
        foreach (var n in Neighbors)
        {
            int stage = CompanionLife.StoryStage(n.Id);
            if (stage >= n.Story.Length || n.Story[stage].Kind != "find" || !CompanionLife.StoryOn(n.Id) || CompanionLife.StoryReady(n.Id)) continue;
            if (CompanionLife.StorySpot(n.Id) != s.Id) continue;
            CompanionLife.SetStoryReady(n.Id);
            CompanionSounds.Effect("chime");
            Float("✦", "#FFFFFF", 8, burst: true);
            Hop(2, 26);
            Say($"Look! {n.Story[stage].Target}! Let's take it back to {n.Name}.", 3.6);
            return true;
        }
        return false;
    }

    /// <summary>A side-mission event (a score, a fish…): marks matching missions done.</summary>
    private void StoryEvent(string kind, string target = "", int number = 0)
    {
        foreach (var n in Neighbors)
        {
            int stage = CompanionLife.StoryStage(n.Id);
            if (stage >= n.Story.Length || !CompanionLife.StoryOn(n.Id) || CompanionLife.StoryReady(n.Id)) continue;
            var r = n.Story[stage];
            if (r.Kind != kind) continue;
            bool met = kind switch
            {
                "fish" => r.Target == target,
                "starcatch" or "beatcombo" or "beatgrade" => number >= r.Number,
                "memory" => number <= r.Number,
                _ => false,
            };
            if (!met) continue;
            CompanionLife.SetStoryReady(n.Id);
            Later(1.5, () => Say($"That's what {n.Name} asked for! Let's go tell them.", 3.4));
        }
    }

    // ---------------------------------------------------------------- quests

    /// <summary>Counts toward today's quests, celebrating any just finished.</summary>
    private void Quests(string evt, int amount = 1)
    {
        var done = CompanionLife.Track(evt, amount);
        double delay = 0.8;
        foreach (var q in done)
        {
            string text = string.Format(q.Text, CompanionArt.Name);
            Later(delay, () =>
            {
                CompanionSounds.Effect("chime");
                Float("✓", "#7FD8BE", 3, burst: true, size: 26);
                Say(q.Id == "bonus" ? $"All of today's quests done! Bonus +{q.Stars} ★!" : $"Quest done: {text}! +{q.Stars} ★", 3.6);
                RefreshHud();
            });
            delay += 3.8;
        }
    }

    private void ShowQuests()
    {
        var lines = new List<string> { "TODAY'S QUESTS" };
        foreach (var q in CompanionLife.TodaysQuests())
        {
            int p = CompanionLife.QuestProgress(q);
            lines.Add($"{(p >= q.Target ? "✓" : "○")}  {string.Format(q.Text, CompanionArt.Name)}   {Math.Min(p, q.Target)}/{q.Target}   ·   {q.Stars} ★");
        }
        lines.Add("Finish all three for a 15 ★ bonus. New quests every day.");
        lines.Add("");
        lines.Add("SIDE MISSIONS");
        bool any = false;
        foreach (var n in Neighbors)
        {
            int st = CompanionLife.StoryStage(n.Id);
            if (st >= n.Story.Length) { lines.Add($"✓  {n.Name}: all done!"); any = true; continue; }
            if (CompanionLife.StoryOn(n.Id)) { lines.Add($"{(CompanionLife.StoryReady(n.Id) ? "!" : "○")}  {n.Name}: {n.Story[st].Text}"); any = true; }
        }
        if (!any) lines.Add("Talk to the neighbors in town: they'd love some help.");
        lines.Add("");
        lines.Add($"FISH COLLECTION  {CompanionLife.SpeciesCaught}/{CompanionLife.FishList.Length}   ·   BEST: Star Catch {CompanionLife.BestCatch}, Beat Hop {CompanionLife.Best("beat")}%, Memory {(CompanionLife.Best("memory") is int m and > 0 ? m + " moves" : "–")}");
        ShowPanel(lines);
    }

    private void ShowPanel(List<string> lines)
    {
        _overlay.Children.Clear();
        _overlay.Children.Add(new Rectangle { Width = W, Height = H, Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) });
        var card = new Border
        {
            Width = 900, CornerRadius = new CornerRadius(22), Padding = new Thickness(36, 28, 36, 28), Background = new SolidColorBrush(PixelArt.C("#FFF6E0")),
            BorderBrush = new SolidColorBrush(PixelArt.C("#C48A5A")), BorderThickness = new Thickness(6),
        };
        var stack = new StackPanel();
        foreach (var l in lines)
        {
            bool head = l.Length > 0 && l == l.ToUpperInvariant() && l.Any(char.IsLetter) && !l.StartsWith("✓") && !l.StartsWith("○");
            stack.Children.Add(new TextBlock
            {
                Text = l, FontSize = head ? 22 : 18, FontWeight = head ? FontWeights.Bold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(PixelArt.C(head ? "#8B5A3C" : "#4A3A2A")), Margin = new Thickness(0, head ? 6 : 2, 0, 2),
            });
        }
        card.Child = stack;
        Canvas.SetLeft(card, (W - 900) / 2); Canvas.SetTop(card, 90);
        _overlay.Children.Add(card);
        _overlay.Visibility = Visibility.Visible;
        ShowFocus();
        RefreshHud();
    }
}
