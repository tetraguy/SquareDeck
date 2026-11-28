using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The playground's minigames (9.0.6), on top of Star Catch: fishing at the pond (wait for the bite, then press A in
/// time; twelve fish to collect, some only by day or night), Memory Match at the café (six pairs), and Beat Hop on the
/// stage (press A as each note reaches the ring). Each takes over the controller until it ends; B stops it.
/// </summary>
public sealed partial class CompanionPlayground
{
    private string? _game;
    private TaskCompletionSource<bool>? _gameDone;

    private Task StartGame(string id)
    {
        _game = id;
        _gameDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _overlay.Children.Clear();
        _overlay.Visibility = Visibility.Visible;
        _ring.Visibility = _label.Visibility = Visibility.Collapsed;
        return _gameDone.Task;
    }

    private void FinishGame()
    {
        _game = null;
        _fishTimer.Stop();
        CompositionTarget.Rendering -= BeatFrame;
        _overlay.Visibility = Visibility.Collapsed;
        _overlay.Children.Clear();
        var done = _gameDone;
        _gameDone = null;
        done?.TrySetResult(true);
        if (!_closed) { RefreshHud(); ShowFocus(); }
    }

    /// <summary>Stops whatever minigame is running (leaving the page, or another page over it).</summary>
    private void StopGames()
    {
        EndGame(quiet: true);
        if (_game != null) FinishGame();
        _seekAt = null;
        _actor.BeginAnimation(OpacityProperty, null);
        _actor.Opacity = 1;
    }

    private void GameAction(NavAction action)
    {
        if (action == NavAction.Back) { UiSound.Back(); FinishGame(); return; }
        switch (_game)
        {
            case "fish": FishInput(action); break;
            case "memory": MemoryInput(action); break;
            case "beat": BeatInput(action); break;
        }
    }

    private void GameTap(Point p)
    {
        switch (_game)
        {
            case "fish": FishInput(NavAction.Accept); break;
            case "beat": BeatInput(NavAction.Accept); break;
            case "memory":
            {
                for (int i = 0; i < _cards.Count; i++)
                {
                    var c = _cards[i].Face;
                    var r = new Rect(Canvas.GetLeft(c), Canvas.GetTop(c), c.Width, c.Height);
                    if (r.Contains(p)) { _cursor = i; MemoryInput(NavAction.Accept); return; }
                }
                break;
            }
        }
    }

    private static TextBlock Big(string text, double size, string hex, double x, double y)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(PixelArt.C(hex)) };
        Canvas.SetLeft(t, x); Canvas.SetTop(t, y);
        return t;
    }

    private static Canvas Buddy(double cell, string mood = "happy")
    {
        var c = new Canvas { Width = 12 * cell, Height = 12 * cell };
        CompanionArt.Draw(c, CompanionArt.SpeciesId, CompanionArt.ColorHex, mood, false, Labs.Outfit, cell: cell);
        return c;
    }

    // ---------------------------------------------------------------- fishing

    private readonly DispatcherTimer _fishTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private bool _fishHooked;
    private string _fishState = "wait"; // wait, bite, show
    private DateTime _fishAt;
    private double _fishWindow;
    private Ellipse? _bobber;
    private TextBlock? _fishInfo, _fishBang;
    private Canvas? _fishBuddy;
    private int _fishCaught;

    private async Task FishingAsync()
    {
        if (CompanionLife.Energy < 10) { Say("Too sleepy to fish… zzz."); await Wait(1.2); return; }
        var done = StartGame("fish");
        bool night = IsNight;
        _overlay.Children.Add(new Rectangle { Width = W, Height = H, Fill = new LinearGradientBrush(PixelArt.C(night ? "#0B1030" : "#9AD8FF"), PixelArt.C(night ? "#27306A" : "#E6F6FF"), 90) });
        var water = new Rectangle { Width = W, Height = 360, Fill = new LinearGradientBrush(PixelArt.C(night ? "#1E3A6A" : "#4AA8E8"), PixelArt.C(night ? "#0E1E40" : "#2A6FB8"), 90) };
        Canvas.SetTop(water, 360);
        _overlay.Children.Add(water);
        for (int i = 0; i < 12; i++)
        {
            var wave = new TextBlock { Text = "～", FontSize = 26, Foreground = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)) };
            Canvas.SetTop(wave, 390 + (i % 4) * 70);
            wave.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation((i * 110) % W, (i * 110) % W + 60, TimeSpan.FromSeconds(2 + i % 3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            _overlay.Children.Add(wave);
        }
        // A wooden dock, with your companion sitting at the end holding a rod.
        var dock = new Rectangle { Width = 380, Height = 26, Fill = new SolidColorBrush(PixelArt.C("#9A6440")) };
        Canvas.SetTop(dock, 380);
        _overlay.Children.Add(dock);
        for (int i = 0; i < 3; i++) { var post = new Rectangle { Width = 14, Height = 110, Fill = new SolidColorBrush(PixelArt.C("#6B4229")) }; Canvas.SetLeft(post, 40 + i * 140); Canvas.SetTop(post, 400); _overlay.Children.Add(post); }
        _fishBuddy = Buddy(8);
        Canvas.SetLeft(_fishBuddy, 280); Canvas.SetTop(_fishBuddy, 380 - 96);
        _overlay.Children.Add(_fishBuddy);
        var rod = new Line { X1 = 360, Y1 = 320, X2 = 470, Y2 = 220, Stroke = new SolidColorBrush(PixelArt.C("#6B4229")), StrokeThickness = 5 };
        var lineToBobber = new Line { X1 = 470, Y1 = 220, X2 = 760, Y2 = 470, Stroke = Brushes.White, StrokeThickness = 1.5, Opacity = 0.8 };
        _overlay.Children.Add(rod);
        _overlay.Children.Add(lineToBobber);
        _bobber = new Ellipse { Width = 22, Height = 22, Fill = new LinearGradientBrush(PixelArt.C("#E04F5F"), Colors.White, 90) { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) } };
        Canvas.SetLeft(_bobber, 749); Canvas.SetTop(_bobber, 460);
        _overlay.Children.Add(_bobber);
        _fishBang = Big("!", 70, "#FFD84D", 740, 360);
        _fishBang.Visibility = Visibility.Collapsed;
        _overlay.Children.Add(_fishBang);
        _overlay.Children.Add(Big("Fishing", 40, "#FFFFFF", 60, 50));
        _fishInfo = Big("Wait for the bobber to dip, then press A!", 22, "#FFFFFF", 60, 104);
        _fishInfo.FontWeight = FontWeights.Normal;
        _overlay.Children.Add(_fishInfo);
        _overlay.Children.Add(Big($"Collection {CompanionLife.SpeciesCaught}/{CompanionLife.FishList.Length}", 20, "#FFD84D", 60, 140));
        _hints.Template = "{Select}  Reel in        {Back}  Done";
        _fishCaught = 0;
        CastLine();
        _fishTimer.Tick -= FishTick;
        _fishTimer.Tick += FishTick;
        _fishTimer.Start();
        await done;
        if (_fishCaught > 0) Reward(Math.Min(6, _fishCaught), energy: -2 * _fishCaught, fun: 4 * _fishCaught);
        Say(_fishCaught == 0 ? "The fish were shy today." : $"{_fishCaught} fish! What a haul.", 3);
    }

    private void CastLine()
    {
        _fishState = "wait";
        _fishAt = DateTime.Now.AddSeconds(1.5 + Random.Shared.NextDouble() * 4);
        if (_fishBang != null) _fishBang.Visibility = Visibility.Collapsed;
        if (_fishInfo != null) _fishInfo.Text = "Wait for the bobber to dip, then press A!";
    }

    private void FishTick(object? sender, EventArgs e)
    {
        if (_game != "fish" || _bobber == null) return;
        double t = DateTime.Now.TimeOfDay.TotalSeconds;
        if (_fishState == "wait")
        {
            Canvas.SetTop(_bobber, 460 + Math.Sin(t * 3) * 3);
            // A nibble or two first, to keep you on your toes.
            if ((_fishAt - DateTime.Now).TotalSeconds is > 0.4 and < 0.6) Canvas.SetTop(_bobber, 466);
            if (DateTime.Now >= _fishAt)
            {
                _fishState = "bite";
                _fishWindow = 0.55 + Random.Shared.NextDouble() * 0.35;
                _fishAt = DateTime.Now.AddSeconds(_fishWindow);
                if (_fishBang != null) _fishBang.Visibility = Visibility.Visible;
                CompanionSounds.Effect("pop");
                Host.Rumble(80);
            }
        }
        else if (_fishState == "bite")
        {
            Canvas.SetTop(_bobber, 474 + Math.Sin(t * 30) * 4);
            if (DateTime.Now >= _fishAt)
            {
                if (_fishInfo != null) _fishInfo.Text = "Too slow! It got away. Wait for the next one…";
                CastLine();
                _fishAt = DateTime.Now.AddSeconds(2 + Random.Shared.NextDouble() * 3);
            }
        }
    }

    private void FishInput(NavAction action)
    {
        if (action != NavAction.Accept) return;
        if (_fishState == "show") { CastLine(); return; }
        if (_fishState == "wait")
        {
            if (_fishInfo != null) _fishInfo.Text = "Too soon! The fish swam off. Patience…";
            UiSound.Error();
            CastLine();
            return;
        }
        if (_fishState != "bite") return;
        _fishState = "show";
        if (_fishBang != null) _fishBang.Visibility = Visibility.Collapsed;
        var fish = CompanionLife.RollFish(IsNight);
        bool first = CompanionLife.AddFish(fish.Id);
        _fishCaught++;
        CompanionLife.Stars += fish.Stars;
        Quests("fish");
        StoryEvent("fish", fish.Id);
        CompanionSounds.Effect(fish.Rarity is "rare" or "legendary" ? "chime" : "boing");
        Host.Rumble(140);
        // The catch, held up for you to see.
        var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(26, 16, 26, 18), Background = new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xF6, 0xE0)) };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = fish.Id == "boot" ? "👢" : "🐟", FontSize = 64, FontFamily = new FontFamily("Segoe UI Emoji"), Foreground = new SolidColorBrush(PixelArt.C(fish.Color)), HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = fish.Name, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(PixelArt.C("#4A3A2A")), HorizontalAlignment = HorizontalAlignment.Center });
        string rarity = char.ToUpperInvariant(fish.Rarity[0]) + fish.Rarity[1..];
        stack.Children.Add(new TextBlock { Text = $"{rarity}  ·  +{fish.Stars} ★" + (first ? "  ·  NEW!" : ""), FontSize = 18, Foreground = new SolidColorBrush(PixelArt.C("#C98A1E")), HorizontalAlignment = HorizontalAlignment.Center });
        card.Child = stack;
        Canvas.SetLeft(card, 840); Canvas.SetTop(card, 170);
        _overlay.Children.Add(card);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4)) { BeginTime = TimeSpan.FromSeconds(2.4) };
        fade.Completed += (_, _) => _overlay.Children.Remove(card);
        card.BeginAnimation(OpacityProperty, fade);
        if (_fishInfo != null) _fishInfo.Text = fish.Id == "boot" ? "…an old boot. Classic. Press A to cast again." : first ? "A new one for the collection! Press A to cast again." : "Got one! Press A to cast again.";
        if (_fishBuddy != null) _fishBuddy.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(284, 250, TimeSpan.FromSeconds(0.18)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) });
    }

    // ---------------------------------------------------------------- Memory Match

    private sealed record MemCard(Border Face, TextBlock Symbol, string Value)
    {
        public bool Up { get; set; }
        public bool Matched { get; set; }
    }

    private readonly List<MemCard> _cards = new();
    private int _cursor, _moves, _first = -1;
    private bool _memLocked;
    private TextBlock? _memInfo;
    private Border? _memCursor;

    private async Task MemoryAsync()
    {
        var done = StartGame("memory");
        _overlay.Children.Add(new Rectangle { Width = W, Height = H, Fill = new LinearGradientBrush(PixelArt.C("#5A3A2A"), PixelArt.C("#8B5A3C"), 90) });
        _overlay.Children.Add(Big("Memory Match", 40, "#FFF6E0", 60, 40));
        _memInfo = Big("Find the pairs! Moves: 0", 22, "#FFF6E0", 60, 94);
        _memInfo.FontWeight = FontWeights.Normal;
        _overlay.Children.Add(_memInfo);
        var buddy = Buddy(8);
        Canvas.SetLeft(buddy, 1110); Canvas.SetTop(buddy, 560);
        _overlay.Children.Add(buddy);
        string[] symbols = { "♥", "★", "♪", "☀", "☂", "♣" };
        string[] colors = { "#E04F5F", "#F2C14E", "#9B7BFF", "#FF9F43", "#4A7BD9", "#3E8E48" };
        var deck = symbols.Concat(symbols).OrderBy(_ => Random.Shared.Next()).ToList();
        _cards.Clear();
        for (int i = 0; i < 12; i++)
        {
            int col = i % 4, row = i / 4;
            var sym = new TextBlock { Text = "?", FontSize = 54, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(PixelArt.C("#FFF6E0")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var face = new Border { Width = 150, Height = 130, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(PixelArt.C("#C96B8A")), BorderBrush = new SolidColorBrush(PixelArt.C("#FFF6E0")), BorderThickness = new Thickness(4), Child = sym };
            Canvas.SetLeft(face, 250 + col * 175); Canvas.SetTop(face, 170 + row * 155);
            _overlay.Children.Add(face);
            _cards.Add(new MemCard(face, sym, deck[i]) { });
            sym.Tag = colors[Array.IndexOf(symbols, deck[i])];
        }
        _memCursor = new Border { Width = 166, Height = 146, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(5), IsHitTestVisible = false };
        _memCursor.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        _overlay.Children.Add(_memCursor);
        _cursor = 0; _moves = 0; _first = -1; _memLocked = false;
        PlaceMemCursor();
        _hints.Template = "{Select}  Flip        {Back}  Stop";
        await done;
    }

    private void PlaceMemCursor()
    {
        if (_memCursor == null || _cards.Count == 0) return;
        var f = _cards[_cursor].Face;
        Canvas.SetLeft(_memCursor, Canvas.GetLeft(f) - 8);
        Canvas.SetTop(_memCursor, Canvas.GetTop(f) - 8);
    }

    private void Flip(MemCard c, bool up)
    {
        c.Up = up;
        c.Symbol.Text = up ? c.Value : "?";
        c.Symbol.Foreground = new SolidColorBrush(PixelArt.C(up ? (string)c.Symbol.Tag : "#FFF6E0"));
        c.Face.Background = new SolidColorBrush(PixelArt.C(up ? "#FFF6E0" : "#C96B8A"));
        var st = new ScaleTransform(1, 1);
        c.Face.RenderTransformOrigin = new Point(0.5, 0.5);
        c.Face.RenderTransform = st;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.1, 1, TimeSpan.FromSeconds(0.18)));
    }

    private void MemoryInput(NavAction action)
    {
        if (_cards.Count == 0) return;
        int col = _cursor % 4, row = _cursor / 4;
        switch (action)
        {
            case NavAction.Left: col = Math.Max(0, col - 1); break;
            case NavAction.Right: col = Math.Min(3, col + 1); break;
            case NavAction.Up: row = Math.Max(0, row - 1); break;
            case NavAction.Down: row = Math.Min(2, row + 1); break;
            case NavAction.Accept: MemoryFlip(); return;
            default: return;
        }
        _cursor = row * 4 + col;
        UiSound.Move();
        PlaceMemCursor();
    }

    private async void MemoryFlip()
    {
        if (_memLocked) return;
        var card = _cards[_cursor];
        if (card.Up || card.Matched) return;
        UiSound.Select();
        Flip(card, true);
        if (_first < 0) { _first = _cursor; return; }
        _moves++;
        var other = _cards[_first];
        _first = -1;
        if (_memInfo != null) _memInfo.Text = $"Find the pairs! Moves: {_moves}";
        if (other.Value == card.Value)
        {
            other.Matched = card.Matched = true;
            CompanionSounds.Effect("chime");
            if (_cards.All(c => c.Matched)) await MemoryWin();
            return;
        }
        _memLocked = true;
        await Task.Delay(750);
        if (_game != "memory") return;
        Flip(card, false);
        Flip(other, false);
        _memLocked = false;
    }

    private async Task MemoryWin()
    {
        int stars = Math.Clamp(22 - _moves, 4, 16);
        CompanionLife.Stars += stars;
        bool best = CompanionLife.Best("memory") == 0 || _moves < CompanionLife.Best("memory");
        if (best) CompanionLife.SetBest("memory", _moves);
        Quests("memory");
        StoryEvent("memory", "", _moves);
        if (_memInfo != null) _memInfo.Text = $"All pairs in {_moves} moves!" + (best ? " A new best!" : "") + $"  +{stars} ★";
        Host.Rumble(160);
        await Task.Delay(2200);
        if (_game != "memory") return;
        FinishGame();
        Reward(3, energy: -3, fun: 14);
        Say(_moves <= 10 ? "Incredible memory! Are you a computer?" : _moves <= 14 ? "Great job! Mochi would be impressed." : "We did it! Practice makes perfect.", 3.4);
    }

    // ---------------------------------------------------------------- Beat Hop

    private sealed class Note
    {
        public double Time;
        public TextBlock Shape = null!;
        public bool Done;
    }

    private readonly List<Note> _notes = new();
    private DateTime _beatStart;
    private int _beatScore, _beatCombo, _beatBestCombo, _beatPerfect, _beatGood, _beatMiss;
    private TextBlock? _beatJudge, _beatCombText;
    private Canvas? _beatBuddy;
    private const double HitX = 300, BeatSpeed = 430, Lead = 2.2;

    private async Task BeatHopAsync()
    {
        if (CompanionLife.Energy < 10) { Say("Too tired to dance… maybe after a nap."); await Wait(1.2); return; }
        var done = StartGame("beat");
        _overlay.Children.Add(new Rectangle { Width = W, Height = H, Fill = new LinearGradientBrush(PixelArt.C("#1A1040"), PixelArt.C("#5A2A7A"), 90) });
        for (int i = 0; i < 6; i++)
        {
            var light = new Ellipse { Width = 240, Height = 240, Fill = new RadialGradientBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF)), IsHitTestVisible = false };
            Canvas.SetTop(light, 40);
            light.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(i * 220, i * 220 + 120, TimeSpan.FromSeconds(1.5 + i * 0.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            _overlay.Children.Add(light);
        }
        _overlay.Children.Add(Big("Beat Hop", 40, "#FFFFFF", 60, 40));
        var how = Big("Press A as each note reaches the ring!", 22, "#FFFFFF", 60, 94);
        how.FontWeight = FontWeights.Normal;
        _overlay.Children.Add(how);
        var lane = new Rectangle { Width = W, Height = 90, Fill = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)) };
        Canvas.SetTop(lane, 375);
        _overlay.Children.Add(lane);
        var ring = new Ellipse { Width = 90, Height = 90, Stroke = Brushes.White, StrokeThickness = 6 };
        Canvas.SetLeft(ring, HitX - 45); Canvas.SetTop(ring, 375);
        _overlay.Children.Add(ring);
        _beatBuddy = Buddy(9);
        Canvas.SetLeft(_beatBuddy, HitX - 54); Canvas.SetTop(_beatBuddy, 520);
        _overlay.Children.Add(_beatBuddy);
        if (FindNeighbor("luna") is { } luna)
        {
            var ghost = new Canvas { Width = 72, Height = 72 };
            CompanionArt.Draw(ghost, luna.Species, luna.Color, "dance", false, luna.Outfit, 0, false, 6, luna.Accessory);
            Canvas.SetLeft(ghost, 1100); Canvas.SetTop(ghost, 560);
            ghost.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(560, 545, TimeSpan.FromSeconds(0.27)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            _overlay.Children.Add(ghost);
        }
        _beatJudge = Big("", 40, "#FFD84D", HitX - 60, 300);
        _beatCombText = Big("", 26, "#FFFFFF", HitX + 80, 395);
        _overlay.Children.Add(_beatJudge);
        _overlay.Children.Add(_beatCombText);
        // The song: 32 notes on a steady beat, with some doubles and rests.
        _notes.Clear();
        double beat = 60.0 / 112;
        double time = 0;
        for (int i = 0; i < 40 && _notes.Count < 32; i++)
        {
            time += beat;
            if (i % 8 == 7) continue; // a rest
            _notes.Add(new Note { Time = time });
            if (i % 6 == 3 && _notes.Count < 32) _notes.Add(new Note { Time = time + beat / 2 }); // a double
        }
        foreach (var n in _notes)
        {
            n.Shape = new TextBlock { Text = "●", FontSize = 58, Foreground = new SolidColorBrush(PixelArt.C(new[] { "#FF5A7A", "#FFD84D", "#7FD8FF", "#7FD8BE" }[_notes.IndexOf(n) % 4])) };
            Canvas.SetTop(n.Shape, 382);
            Canvas.SetLeft(n.Shape, W + 100);
            _overlay.Children.Add(n.Shape);
        }
        _beatScore = _beatCombo = _beatBestCombo = _beatPerfect = _beatGood = _beatMiss = 0;
        _beatStart = DateTime.Now.AddSeconds(Lead);
        _hints.Template = "{Select}  Hit        {Back}  Stop";
        CompositionTarget.Rendering -= BeatFrame;
        CompositionTarget.Rendering += BeatFrame;
        await done;
    }

    private double BeatNow => (DateTime.Now - _beatStart).TotalSeconds;

    private void BeatFrame(object? sender, EventArgs e)
    {
        if (_game != "beat") return;
        double now = BeatNow;
        foreach (var n in _notes)
        {
            if (n.Done) continue;
            double x = HitX + (n.Time - now) * BeatSpeed;
            Canvas.SetLeft(n.Shape, x - 22);
            if (now - n.Time > 0.16) { n.Done = true; n.Shape.Opacity = 0.2; Judge("Miss", "#AAB4C8"); _beatMiss++; _beatCombo = 0; }
        }
        if (_notes.All(n => n.Done) && now > (_notes.LastOrDefault()?.Time ?? 0) + 0.8) BeatEnd();
    }

    private void BeatInput(NavAction action)
    {
        if (action is not (NavAction.Accept or NavAction.X or NavAction.Y)) return;
        double now = BeatNow;
        var next = _notes.Where(n => !n.Done).OrderBy(n => Math.Abs(n.Time - now)).FirstOrDefault();
        if (next == null) return;
        double off = Math.Abs(next.Time - now);
        if (off > 0.22) return; // nothing near: no penalty
        next.Done = true;
        next.Shape.Visibility = Visibility.Collapsed;
        if (off < 0.07) { _beatScore += 2; _beatPerfect++; _beatCombo++; Judge("Perfect!", "#FFD84D"); CompanionSounds.Effect("chime"); }
        else if (off < 0.14) { _beatScore += 1; _beatGood++; _beatCombo++; Judge("Good", "#7FD8BE"); CompanionSounds.Effect("pop"); }
        else { _beatMiss++; _beatCombo = 0; Judge("Miss", "#AAB4C8"); }
        _beatBestCombo = Math.Max(_beatBestCombo, _beatCombo);
        if (_beatBuddy != null && _beatCombo > 0) _beatBuddy.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(520, 490, TimeSpan.FromSeconds(0.1)) { AutoReverse = true });
        Host.Rumble(40);
    }

    private void Judge(string text, string hex)
    {
        if (_beatJudge == null) return;
        _beatJudge.Text = text;
        _beatJudge.Foreground = new SolidColorBrush(PixelArt.C(hex));
        _beatJudge.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.6)));
        if (_beatCombText != null) _beatCombText.Text = _beatCombo >= 3 ? $"{_beatCombo} combo!" : "";
    }

    private async void BeatEnd()
    {
        if (_game != "beat") return;
        CompositionTarget.Rendering -= BeatFrame;
        int percent = _notes.Count == 0 ? 0 : (int)Math.Round(100.0 * _beatScore / (_notes.Count * 2));
        string grade = percent >= 95 ? "S" : percent >= 80 ? "A" : percent >= 60 ? "B" : percent >= 40 ? "C" : "D";
        int stars = Math.Max(2, percent / 7);
        CompanionLife.Stars += stars;
        bool best = percent > CompanionLife.Best("beat");
        if (best) CompanionLife.SetBest("beat", percent);
        Quests("beat");
        StoryEvent("beatcombo", "", _beatBestCombo);
        StoryEvent("beatgrade", "", percent);
        var result = Big($"Grade {grade}   ·   {percent}%", 46, "#FFD84D", 440, 200);
        var detail = Big($"Perfect {_beatPerfect}   Good {_beatGood}   Miss {_beatMiss}   ·   Best combo {_beatBestCombo}   ·   +{stars} ★" + (best ? "   ·   New best!" : ""), 22, "#FFFFFF", 300, 270);
        detail.FontWeight = FontWeights.Normal;
        _overlay.Children.Add(result);
        _overlay.Children.Add(detail);
        Host.Rumble(160);
        await Task.Delay(2800);
        if (_game != "beat") return;
        FinishGame();
        Reward(3 + percent / 25, energy: -6, fun: 18);
        Say(grade is "S" or "A" ? "We rocked it! Luna's going to be so jealous." : "That was fun! Again sometime?", 3.2);
    }
}
