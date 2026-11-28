using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// "Companion playground" (9.0.4, out of Experimental in 9.0.20): a little world where your companion lives. Outside, a yard under the
/// real sky (sunrise, day, sunset and stars at night) with an apple tree, flowers that grow as you keep visiting, a
/// mailbox with a daily letter, a toy box, the Star Shop and, if you buy one, a telescope. Inside, its house: a bed for
/// naps, a fridge for snacks, a TV showing the game you played last, an arcade cabinet with the Star Catch minigame, a
/// bookshelf, a mirror for trying on accessories, and the furniture you buy. It has needs (food, energy, fun) that
/// drift down slowly while you're away, a friendship level that grows as you spend time together, and star bits to
/// spend, earned from the daily letter, Star Catch, petting and playing your games.
///
/// Controller: left and right pick something (the highlight shows what), A uses it, X pets your companion, Y opens the
/// Star Shop, LB / RB go outside or inside, B leaves. Everything can also be tapped.
/// </summary>
public sealed partial class CompanionPlayground : Screen
{
    private const double W = 1280, H = 720;
    private const double YardFloor = 612, HouseFloor = 640;

    private readonly Canvas _scene = new() { Width = W, Height = H, ClipToBounds = true };
    private readonly Canvas _yard = new() { Width = W, Height = H };
    private readonly Canvas _house = new() { Width = W, Height = H, Visibility = Visibility.Collapsed };
    private readonly Canvas _hud = new() { Width = W, Height = H, IsHitTestVisible = true };
    private readonly Canvas _overlay = new() { Width = W, Height = H, Visibility = Visibility.Collapsed };
    private string _place = "yard";

    // The companion: a box that walks along the floor, with the sprite, its speech bubble and effects inside.
    private readonly Grid _actor = new() { Width = 300, Height = 300 };
    private readonly Canvas _sprite = new() { Width = 96, Height = 96, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent };
    private readonly Canvas _fx = new() { Width = 300, Height = 300, IsHitTestVisible = false };
    private readonly Border _bubble = new() { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 8, 14, 9), Opacity = 0, MaxWidth = 300, IsHitTestVisible = false };
    private readonly TextBlock _bubbleText = new() { FontSize = 17, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A)) };
    private readonly TranslateTransform _pos = new();
    private readonly TranslateTransform _hop = new();
    private readonly ScaleTransform _squash = new(1, 1);
    private readonly RotateTransform _turn = new();
    private readonly Ellipse _shadow = new() { Width = 70, Height = 14, Fill = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), IsHitTestVisible = false };
    private string _drawn = "";
    private string _mood = "happy";
    private int _look;
    private bool _blink, _asleep;

    // What can be used: each with its place, a label, its area on the scene, and what it does.
    private sealed record Spot(string Id, string Label, Rect Area, Func<Task> Act);
    private readonly List<Spot> _yardSpots = new(), _houseSpots = new();
    private List<Spot> Spots => _place switch { "yard" => _yardSpots, "town" => _townSpots, _ => _houseSpots };
    private int _focus;
    private readonly Border _ring = new() { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(4), IsHitTestVisible = false };
    private readonly Border _label = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 5, 12, 6), IsHitTestVisible = false };
    private readonly TextBlock _labelText = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };

    // The heads-up display.
    private readonly TextBlock _nameText = new() { FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
    private readonly TextBlock _levelText = new() { FontSize = 15, Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)) };
    private readonly Rectangle _xpBar = new() { Width = 0, Height = 8, RadiusX = 4, RadiusY = 4 };
    private readonly TextBlock _starsText = new() { FontSize = 26, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4D)) };
    private readonly Rectangle[] _needBars = new Rectangle[3];
    private readonly HintBar _hints = new("");

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int _ticks;
    private bool _busy, _closed;
    private DateTime _nextIdle = DateTime.Now.AddSeconds(5);

    // Bits of the scene that change.
    private readonly Rectangle _sky = new() { Width = W, Height = H };
    private readonly Canvas _stars = new() { Width = W, Height = H, IsHitTestVisible = false };
    private readonly Ellipse _sun = new() { Width = 90, Height = 90 };
    private readonly Canvas _clouds = new() { Width = W, Height = 300, IsHitTestVisible = false };
    private readonly Canvas _apples = new() { IsHitTestVisible = false };
    private readonly Rectangle _mailFlag = new() { Width = 10, Height = 22 };
    private readonly Canvas _flowerBed = new();
    private readonly Ellipse _toyBall = new() { Width = 26, Height = 26 };
    private readonly Rectangle _windowSky = new() { Width = 112, Height = 84 };
    private readonly Rectangle _night = new() { Width = W, Height = H, Fill = new SolidColorBrush(Color.FromRgb(0x05, 0x08, 0x20)), Opacity = 0, IsHitTestVisible = false };
    private readonly Border _tvScreen = new() { Width = 126, Height = 70, ClipToBounds = true };
    private readonly Canvas _wall = new() { Width = W, Height = 560 };
    private readonly Canvas _decor = new() { Width = W, Height = H };
    private readonly List<FrameworkElement> _butterflies = new();

    public CompanionPlayground()
    {
        Focusable = true;
        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x24)) };
        var view = new Viewbox { Stretch = Stretch.UniformToFill, Child = _scene };
        root.Children.Add(view);
        Content = root;

        _scene.Children.Add(_yard);
        _scene.Children.Add(_house);
        _scene.Children.Add(_town);
        BuildActor();
        _scene.Children.Add(_actor);
        _scene.Children.Add(_ring);
        _scene.Children.Add(_label);
        _scene.Children.Add(_night);
        _scene.Children.Add(_hud);
        _scene.Children.Add(_overlay);
        _tick.Tick += (_, _) => Tick();
        // Tapping the scene: the companion pets, a thing uses it.
        _scene.MouseLeftButtonUp += (_, e) => Tapped(e.GetPosition(_scene));
    }

    public override void OnShown()
    {
        CompanionLife.CatchUp();
        BuildYard();
        BuildHouse();
        BuildTown();
        BuildHud();
        SetPlace("yard", instant: true);
        _tick.Start();
        Focus();
        // Arriving: hello, and what happened while you were away.
        int earned = CompanionLife.EarnFromPlay(Host.Playtime);
        Later(0.4, () =>
        {
            Hop(2);
            string hello = Greeting();
            if (earned > 0) hello += $" You played games while I waited: +{earned} ★!";
            Say(hello, 4.5);
            Redraw(force: true);
            RefreshHud();
        });
    }

    public override void OnClosed()
    {
        _closed = true;
        _tick.Stop();
        StopGames();
        Host.RefreshCompanion();
    }

    public override void OnCovered()
    {
        // A pop-up (a menu, the shop) is fine; a whole page over it stops any game.
        Dispatcher.BeginInvoke(new Action(() => { if (Visibility != Visibility.Visible) StopGames(); }), DispatcherPriority.Loaded);
    }

    public override void OnResumed()
    {
        Redraw(force: true);
        RefreshHud();
        Focus();
    }

    // ---------------------------------------------------------------- the companion

    private void BuildActor()
    {
        _bubble.Background = Brushes.White;
        _bubble.BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0));
        _bubble.BorderThickness = new Thickness(1.5);
        _bubble.Child = _bubbleText;
        _bubble.HorizontalAlignment = HorizontalAlignment.Center;
        _bubble.VerticalAlignment = VerticalAlignment.Bottom;
        _bubble.Margin = new Thickness(0, 0, 0, 112); // just above its head
        _sprite.RenderTransformOrigin = new Point(0.5, 1);
        _sprite.RenderTransform = new TransformGroup { Children = { _squash, _turn, _hop } };
        _sprite.Cursor = Cursors.Hand;
        _shadow.HorizontalAlignment = HorizontalAlignment.Center;
        _shadow.VerticalAlignment = VerticalAlignment.Bottom;
        _shadow.Margin = new Thickness(0, 0, 0, -6);
        _actor.Children.Add(_shadow);
        _actor.Children.Add(_sprite);
        _actor.Children.Add(_fx);
        _actor.Children.Add(_bubble);
        _actor.RenderTransform = _pos;
        Canvas.SetLeft(_actor, -150);
        _pos.X = 640;
    }

    private void Redraw(bool force = false)
    {
        string mood = _asleep ? "sleepy" : _mood;
        string key = $"{CompanionArt.SpeciesId}|{CompanionArt.ColorHex}|{mood}|{_blink}|{Labs.Outfit}|{CompanionArt.Accessory}|{_look}";
        if (!force && key == _drawn) return;
        _drawn = key;
        CompanionArt.Draw(_sprite, CompanionArt.SpeciesId, CompanionArt.ColorHex, mood, _blink && !_asleep, Labs.Outfit, _look, cell: 8);
    }

    private void SetMood(string mood) { _mood = mood; Redraw(); }

    /// <summary>The face it wears when nothing's going on: from its needs.</summary>
    private string RestingMood() => CompanionLife.Mood() switch
    {
        "hungry" or "bored" => "bored",
        "sleepy" => "sleepy",
        "happy" => "happy",
        _ => "happy",
    };

    private async Task WalkTo(double x)
    {
        x = Math.Clamp(x, 70, W - 70);
        double from = _pos.X;
        double dist = Math.Abs(x - from);
        if (dist < 6) return;
        double secs = Math.Clamp(dist / 300, 0.25, 3.0);
        _look = x < from ? -1 : 1;
        Redraw();
        _pos.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromSeconds(secs)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        int steps = Math.Max(2, (int)(secs / 0.28));
        _hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -7, TimeSpan.FromSeconds(0.14)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(steps) });
        _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-4, 4, TimeSpan.FromSeconds(0.14)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(steps) });
        await Task.Delay(TimeSpan.FromSeconds(secs));
        _turn.BeginAnimation(RotateTransform.AngleProperty, null);
        _look = 0;
        Redraw();
    }

    private void Hop(int times = 1, double height = 26)
    {
        _hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -height, TimeSpan.FromSeconds(0.2)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(times), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        _squash.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1.05, TimeSpan.FromSeconds(0.2)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(times) });
    }

    private void Wiggle(double angle = 10, int times = 4, double each = 0.12) =>
        _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-angle, angle, TimeSpan.FromSeconds(each)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(times) });

    private void Say(string text, double seconds = 3.2)
    {
        _bubbleText.Inlines.Clear();
        _bubbleText.Inlines.Add(new System.Windows.Documents.Run(text));
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.2))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds + 0.35))));
        _bubble.BeginAnimation(OpacityProperty, a);
    }

    /// <summary>A symbol that floats up (or bursts out) from the companion and fades.</summary>
    private void Float(string symbol, string hex, int count = 1, bool burst = false, double size = 22)
    {
        var brush = new SolidColorBrush(PixelArt.C(hex));
        for (int i = 0; i < count; i++)
        {
            var t = new TextBlock { Text = symbol, Foreground = brush, FontSize = size + Random.Shared.Next(-4, 6), FontWeight = FontWeights.Bold };
            double x = 140 + Random.Shared.Next(-40, 40), y = 200 + Random.Shared.Next(-20, 10);
            Canvas.SetLeft(t, x); Canvas.SetTop(t, y);
            _fx.Children.Add(t);
            var time = TimeSpan.FromSeconds(burst ? 0.9 : 1.6);
            var delay = TimeSpan.FromSeconds(i * (burst ? 0.03 : 0.2));
            t.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(y, burst ? y - 50 - Random.Shared.Next(40) : y - 110, time) { BeginTime = delay });
            if (burst) t.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, x + Random.Shared.Next(-70, 70), time) { BeginTime = delay });
            var fade = new DoubleAnimation(1, 0, time) { BeginTime = delay };
            fade.Completed += (_, _) => _fx.Children.Remove(t);
            t.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void Later(double seconds, Action what) =>
        Dispatcher.BeginInvoke(async () => { await Task.Delay(TimeSpan.FromSeconds(seconds)); if (!_closed) what(); });

    private static Task Wait(double seconds) => Task.Delay(TimeSpan.FromSeconds(seconds));

    // ---------------------------------------------------------------- the yard

    private static readonly Dictionary<char, Color> Pal = new()
    {
        ['k'] = PixelArt.C("#2B2233"), ['w'] = PixelArt.C("#FFFFFF"), ['r'] = PixelArt.C("#D9534F"), ['R'] = PixelArt.C("#A83C3A"),
        ['b'] = PixelArt.C("#F6E7C8"), ['B'] = PixelArt.C("#E2CDA4"), ['d'] = PixelArt.C("#8B5A3C"), ['D'] = PixelArt.C("#6B4229"),
        ['g'] = PixelArt.C("#5DBB63"), ['G'] = PixelArt.C("#3E8E48"), ['l'] = PixelArt.C("#8EDC7A"), ['t'] = PixelArt.C("#8A5A3B"),
        ['T'] = PixelArt.C("#6A4128"), ['y'] = PixelArt.C("#FFD84D"), ['o'] = PixelArt.C("#FF9F43"), ['p'] = PixelArt.C("#FF8FB1"),
        ['P'] = PixelArt.C("#9B7BFF"), ['c'] = PixelArt.C("#7FD8FF"), ['C'] = PixelArt.C("#4A7BD9"), ['s'] = PixelArt.C("#B8C0CC"),
        ['S'] = PixelArt.C("#6D7684"), ['a'] = PixelArt.C("#E8403A"), ['m'] = PixelArt.C("#7FD8BE"), ['n'] = PixelArt.C("#2E3A59"),
        ['e'] = PixelArt.C("#CFEFFF"), ['f'] = PixelArt.C("#E9EEF5"), ['F'] = PixelArt.C("#C5CED9"), ['q'] = PixelArt.C("#C48A5A"),
        ['Q'] = PixelArt.C("#9A6440"), ['v'] = PixelArt.C("#C3A6FF"), ['h'] = PixelArt.C("#1A1D26"), ['x'] = PixelArt.C("#E04F5F"),
        ['z'] = PixelArt.C("#F2C14E"),
    };

    private static readonly string[] HouseMap =
    {
        "...........kkkkkk...........", "..........krrrrrrk..........", ".........krrrrrrrrk.........", "........krrrrrrrrrrk........",
        ".......krrrrrrrrrrrrk.......", "......krrrrrrrrrrrrrrk......", ".....krrrrrrrrrrrrrrrrk.....", "....krrrrrrrrrrrrrrrrrrk....",
        "...krrrrrrrrrrrrrrrrrrrrk...", "..kRRRRRRRRRRRRRRRRRRRRRRk..", ".kkkkkkkkkkkkkkkkkkkkkkkkkk.", "...kbbbbbbbbbbbbbbbbbbbbk...",
        "...kbbkkkkkkbbbbkkkkkkbbk...", "...kbbkeeceekbbkeeceekbbk...", "...kbbkecceekbbkecceekbbk...", "...kbbkeeeeekbbkeeeeekbbk...",
        "...kbbkkkkkkbbbbkkkkkkbbk...", "...kbbbbbbbbkkkkbbbbbbbbk...", "...kbbbbbbbkddddkbbbbbbbk...", "...kbBBBBBBkdddDkBBBBBBbk...",
        "...kbbbbbbbkdddDkbbbbbbbk...", "...kbbbbbbbkdyddkbbbbbbbk...", "...kbbbbbbbkdddDkbbbbbbbk...", "...kBBBBBBBkdddDkBBBBBBBk...",
        "...kkkkkkkkkkkkkkkkkkkkkk...",
    };

    private static readonly string[] TreeMap =
    {
        ".......gggggg.......", ".....gglllllggg.....", "....glllgllllggg....", "...gllggglllgggGg...", "..gglllllgglllgGGg..",
        "..glllgllllllggGGg..", ".gllllggglllllgGGGg.", ".glllllllllgglgGGGg.", ".gglllgglllllggGGGg.", "..gggllllllgggGGGg..",
        "..gGgggglgggggGGGg..", "...gGGGggggGGGGGg...", "....gGGGGGGGGGGg....", "......ggGGGGgg......", "........tTTt........",
        "........tTTt........", "........tTTt........", ".......ttTTtt.......", "........tTTt........", "........tTTt........",
        "......ttTTTTtt......", ".....tt......tt.....",
    };

    private static readonly string[] MailboxMap =
    {
        "..kkkkkk..", ".kCCCCCCk.", "kCCCCCCCCk", "kCwwwwwwCk", "kCCCCCCCCk", "kkkkkkkkkk", "....kk....", "....qQ....", "....qQ....",
        "....qQ....", "....qQ....", "....qQ....", "...qqQQ...",
    };

    private static readonly string[] StallMap =
    {
        "kkkkkkkkkkkkkkkkkkkkkkkkkk", "kxwxwxwxwxwxwxwxwxwxwxwxwk", "kxwxwxwxwxwxwxwxwxwxwxwxwk", ".kxkwkxkwkxkwkxkwkxkwkxkk.",
        "..q....................q..", "..q....................q..", "..q....................q..", "..q....................q..",
        "..q....................q..", "..q....................q..", "kkkkkkkkkkkkkkkkkkkkkkkkkk", "kqqqqqqqqqqqqqqqqqqqqqqqqk",
        "kqQQQQQQQQQQQQQQQQQQQQQQqk", "kqqqqqqqqqqqqqqqqqqqqqqqqk", "kqQQQQQQQQQQQQQQQQQQQQQQqk", "kqqqqqqqqqqqqqqqqqqqqqqqqk",
        "kkkkkkkkkkkkkkkkkkkkkkkkkk",
    };

    private static readonly string[] ToyBoxMap =
    {
        "kkkkkkkkkkkkkk", "kqqqqqqqqqqqqk", "kQQQQQQQQQQQQk", "kkkkkkkkkkkkkk", "kqqqqqqqqqqqqk", "kqqqqyyyyqqqqk", "kqqqqyqqyqqqqk",
        "kqqqqqqqqqqqqk", "kQQQQQQQQQQQQk", "kkkkkkkkkkkkkk",
    };

    private static readonly string[] TelescopeMap =
    {
        "........kkkk", "......kkCCCk", "....kkCCCCkk", "..kkCCCCkk..", "kkCCCCkk....", "kCCCkk......", "kkkk..kk....", ".....kSSk...",
        "....kSkkSk..", "...kSk..kSk.", "..kSk....kSk", "..kk......kk",
    };

    private static string[] FlowerMap(int stage) => stage switch
    {
        0 => new[] { ".....", ".....", ".....", ".....", "..g..", ".gg..", "..g.." },
        1 => new[] { ".....", ".....", "..l..", ".lgl.", "..g..", ".gg..", "..g.." },
        _ => new[] { ".p.p.", "ppypp", ".ppp.", "..g..", ".gg..", "..g..", "..g.." },
    };

    private static Image Put(Canvas on, string[] map, double cell, double x, double y, Dictionary<char, Color>? palette = null)
    {
        var img = PixelArt.Image(map, palette ?? Pal, cell);
        Canvas.SetLeft(img, x);
        Canvas.SetTop(img, y);
        on.Children.Add(img);
        return img;
    }

    private static Rect Area(FrameworkElement e, double pad = 8) =>
        new(Canvas.GetLeft(e) - pad, Canvas.GetTop(e) - pad, e.Width + pad * 2, e.Height + pad * 2);

    private void BuildYard()
    {
        _yard.Children.Clear();
        _yardSpots.Clear();
        _yard.Children.Add(_sky);
        _yard.Children.Add(_stars);
        _yard.Children.Add(_sun);
        _yard.Children.Add(_clouds);
        // Hills far away, then the grass.
        var hills = new Path { Data = Geometry.Parse("M0,520 C160,430 320,450 460,500 C600,440 760,420 900,490 C1040,450 1180,440 1280,480 L1280,720 L0,720 Z"), Fill = new SolidColorBrush(PixelArt.C("#6FBF73")) };
        _yard.Children.Add(hills);
        var grass = new Rectangle { Width = W, Height = 140, Fill = new LinearGradientBrush(PixelArt.C("#58B05C"), PixelArt.C("#3F8F46"), 90) };
        Canvas.SetTop(grass, 580);
        _yard.Children.Add(grass);
        for (int i = 0; i < 70; i++)
        {
            var blade = new TextBlock { Text = "ˬ", FontSize = 16, Foreground = new SolidColorBrush(PixelArt.C(i % 2 == 0 ? "#78CC6E" : "#2F7A38")) };
            Canvas.SetLeft(blade, Random.Shared.Next(0, (int)W));
            Canvas.SetTop(blade, 590 + Random.Shared.Next(0, 120));
            _yard.Children.Add(blade);
        }
        var path = new Path { Data = Geometry.Parse("M560,612 C580,650 520,690 600,720 L700,720 C640,690 690,650 660,612 Z"), Fill = new SolidColorBrush(PixelArt.C("#D9C29A")) };
        _yard.Children.Add(path);

        // The house, with its door to go in.
        var house = Put(_yard, HouseMap, 8, 500, 612 - HouseMap.Length * 8);
        _yardSpots.Add(new Spot("door", "Go inside", new Rect(500 + 11 * 8 - 10, 612 - 8 * 8 - 10, 6 * 8 + 20, 8 * 8 + 20), () => GoInside()));
        // The tree, with apples when there's fruit.
        var tree = Put(_yard, TreeMap, 9, 70, 612 - TreeMap.Length * 9);
        Canvas.SetLeft(_apples, 70); Canvas.SetTop(_apples, 612 - TreeMap.Length * 9);
        _yard.Children.Add(_apples);
        DrawApples();
        _yardSpots.Add(new Spot("tree", "Apple tree", Area(tree), () => TreeAsync()));
        // The flower bed: more flowers the more days in a row you visit.
        Canvas.SetLeft(_flowerBed, 300); Canvas.SetTop(_flowerBed, 560);
        _yard.Children.Add(_flowerBed);
        DrawFlowers();
        _yardSpots.Add(new Spot("flowers", "Flower bed", new Rect(290, 540, 180, 80), () => FlowersAsync()));
        // The mailbox, with its flag up when there's a letter.
        var mailbox = Put(_yard, MailboxMap, 7, 790, 612 - MailboxMap.Length * 7);
        _mailFlag.Fill = new SolidColorBrush(PixelArt.C("#E04F5F"));
        Canvas.SetLeft(_mailFlag, 790 + 70); Canvas.SetTop(_mailFlag, 612 - MailboxMap.Length * 7 + 4);
        _yard.Children.Add(_mailFlag);
        _yardSpots.Add(new Spot("mail", "Mailbox", Area(mailbox), () => MailAsync()));
        // The toy box, with the ball on top.
        var toys = Put(_yard, ToyBoxMap, 6, 900, 612 - ToyBoxMap.Length * 6);
        _toyBall.Fill = new RadialGradientBrush(PixelArt.C("#FF8A8A"), PixelArt.C("#E04F5F")) { GradientOrigin = new Point(0.3, 0.3) };
        Canvas.SetLeft(_toyBall, 900 + 30); Canvas.SetTop(_toyBall, 612 - ToyBoxMap.Length * 6 - 24);
        _yard.Children.Add(_toyBall);
        _yardSpots.Add(new Spot("toys", "Toy box", Area(toys, 20), () => ToysAsync()));
        // The Star Shop.
        var stall = Put(_yard, StallMap, 7, 1040, 612 - StallMap.Length * 7);
        var sign = new TextBlock { Text = "★ STAR SHOP", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(PixelArt.C("#FFD84D")) };
        Canvas.SetLeft(sign, 1062); Canvas.SetTop(sign, 612 - StallMap.Length * 7 + 36);
        _yard.Children.Add(sign);
        foreach (var (sym, dx) in new[] { ("🍪", 20.0), ("🧁", 70.0), ("🎈", 120.0) })
        {
            var item = new TextBlock { Text = sym, FontSize = 22, FontFamily = new FontFamily("Segoe UI Emoji") };
            Canvas.SetLeft(item, 1040 + dx); Canvas.SetTop(item, 612 - 7 * 7 - 30);
            _yard.Children.Add(item);
        }
        _yardSpots.Add(new Spot("shop", "Star Shop", Area(stall), () => ShopAsync()));
        // The telescope, once bought.
        if (CompanionLife.Owns("telescope"))
        {
            var scope = Put(_yard, TelescopeMap, 6, 1196, 612 - TelescopeMap.Length * 6);
            _yardSpots.Add(new Spot("telescope", "Telescope", Area(scope), () => TelescopeAsync()));
        }
        // Butterflies to chase.
        _butterflies.Clear();
        for (int i = 0; i < 2; i++)
        {
            var b = new TextBlock { Text = "ж", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(PixelArt.C(i == 0 ? "#FF8FB1" : "#FFD84D")), IsHitTestVisible = false };
            Canvas.SetLeft(b, 300 + i * 500); Canvas.SetTop(b, 460);
            _yard.Children.Add(b);
            _butterflies.Add(b);
        }
        // The way to the neighborhood.
        var townSign = Put(_yard, SignMap, 6, 8, 612 - SignMap.Length * 6);
        var townArrow = new TextBlock { Text = "◀", FontSize = 16, Foreground = new SolidColorBrush(PixelArt.C("#8B5A3C")), IsHitTestVisible = false };
        Canvas.SetLeft(townArrow, 22); Canvas.SetTop(townArrow, 612 - SignMap.Length * 6 + 4);
        _yard.Children.Add(townArrow);
        _yardSpots.Add(new Spot("town", "To the neighborhood", Area(townSign), () => GoTown()));
        _yardSpots.Insert(0, new Spot("pal", CompanionArt.Name, Rect.Empty, () => PalMenuAsync()));
        UpdateSky();
        DrawClouds();
        _yardSpots.Sort((a, b) => SpotX(a).CompareTo(SpotX(b)));
    }

    private double SpotX(Spot s) => s.Id == "pal" ? _pos.X : s.Area.X + s.Area.Width / 2;

    private void DrawApples()
    {
        _apples.Children.Clear();
        if (!CompanionLife.AppleReady) return;
        foreach (var (x, y) in new[] { (46.0, 40.0), (110.0, 58.0), (74.0, 86.0), (130.0, 30.0) })
        {
            var a = new Ellipse { Width = 16, Height = 16, Fill = new RadialGradientBrush(PixelArt.C("#FF7A6E"), PixelArt.C("#D2312A")) { GradientOrigin = new Point(0.35, 0.3) } };
            Canvas.SetLeft(a, x); Canvas.SetTop(a, y);
            _apples.Children.Add(a);
        }
    }

    private void DrawFlowers()
    {
        _flowerBed.Children.Clear();
        int streak = int.TryParse(Labs.Get("pg.streak", "1"), out var s) ? s : 1;
        string[] petals = { "#FF8FB1", "#FFD84D", "#C3A6FF", "#7FD8FF", "#FF9F43", "#FFFFFF" };
        for (int i = 0; i < 6; i++)
        {
            int stage = i < streak ? 2 : i < streak + 2 ? 1 : 0;
            var pal = new Dictionary<char, Color>(Pal) { ['p'] = PixelArt.C(petals[i]) };
            var f = PixelArt.Image(FlowerMap(stage), pal, 6);
            Canvas.SetLeft(f, i * 28);
            Canvas.SetTop(f, 10 + (i % 2) * 6);
            _flowerBed.Children.Add(f);
        }
        var soil = new Rectangle { Width = 176, Height = 10, RadiusX = 5, RadiusY = 5, Fill = new SolidColorBrush(PixelArt.C("#7A5236")) };
        Canvas.SetTop(soil, 52);
        _flowerBed.Children.Insert(0, soil);
    }

    private void DrawClouds()
    {
        _clouds.Children.Clear();
        for (int i = 0; i < 4; i++)
        {
            var cloud = new Path { Data = Geometry.Parse("M10,40 C0,40 0,20 16,20 C18,6 40,2 50,16 C60,4 84,8 84,26 C98,26 100,40 88,40 Z"), Fill = new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF)), RenderTransform = new ScaleTransform(1.2 + i * 0.2, 1.2 + i * 0.2) };
            double y = 40 + i * 55;
            Canvas.SetTop(cloud, y);
            double secs = 90 + i * 30;
            cloud.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(-200 + i * 380, W + 100, TimeSpan.FromSeconds(secs)) { RepeatBehavior = RepeatBehavior.Forever });
            _clouds.Children.Add(cloud);
        }
    }

    /// <summary>The sky follows the real time of day: dawn, day, dusk and a starry night.</summary>
    private void UpdateSky()
    {
        int h = DateTime.Now.Hour;
        (string top, string bottom, bool night) = h switch
        {
            >= 5 and < 8 => ("#FF9AA2", "#FFD6A5", false),
            >= 8 and < 17 => ("#6EC6FF", "#CDEEFF", false),
            >= 17 and < 20 => ("#7B5BA6", "#FF9F68", false),
            _ => ("#0B1030", "#27306A", true),
        };
        var brush = new LinearGradientBrush(PixelArt.C(top), PixelArt.C(bottom), 90);
        _sky.Fill = brush;
        _windowSky.Fill = brush;
        _townSky.Fill = brush;
        _stars.Visibility = night ? Visibility.Visible : Visibility.Collapsed;
        if (night && _stars.Children.Count == 0)
            for (int i = 0; i < 70; i++)
            {
                var star = new Ellipse { Width = 3, Height = 3, Fill = Brushes.White, Opacity = 0.4 + Random.Shared.NextDouble() * 0.6 };
                Canvas.SetLeft(star, Random.Shared.Next(0, (int)W)); Canvas.SetTop(star, Random.Shared.Next(0, 420));
                star.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromSeconds(1 + Random.Shared.NextDouble() * 2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                _stars.Children.Add(star);
            }
        _sun.Fill = night ? new SolidColorBrush(PixelArt.C("#F4F1D0")) : new RadialGradientBrush(PixelArt.C("#FFF6B0"), PixelArt.C("#FFC93C"));
        _sun.Width = _sun.Height = night ? 64 : 90;
        // Across the sky with the hours.
        double frac = night ? ((h + 24 - 20) % 24) / 9.0 : (h - 5) / 15.0;
        Canvas.SetLeft(_sun, 80 + Math.Clamp(frac, 0, 1) * (W - 260));
        Canvas.SetTop(_sun, 70 + Math.Abs(0.5 - Math.Clamp(frac, 0, 1)) * 180);
        _clouds.Opacity = night ? 0.25 : 1;
        _mailFlag.Visibility = Labs.Get("pg.mail", "") == DateTime.Today.ToString("yyyy-MM-dd") ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool IsNight => DateTime.Now.Hour is >= 20 or < 5;

    // ---------------------------------------------------------------- the house

    private static readonly string[] BedMap =
    {
        "kkkk....................", "kqqk....................", "kqqkkkkkkkkkkkkkkkkkkkkk", "kqqkwwwwkppppppppppppppk", "kqqkwwwwkppvppvppvppvppk",
        "kqqkwwwwkppppppppppppppk", "kqqkkkkkkvvvvvvvvvvvvvvk", "kqqqqqqqqqqqqqqqqqqqqqqk", "kQQQQQQQQQQQQQQQQQQQQQQk", "kkkkkkkkkkkkkkkkkkkkkkkk",
        "kQk..................kQk", "kkk..................kkk",
    };

    private static readonly string[] FridgeMap =
    {
        "kkkkkkkkkkkk", "kffffffffffk", "kffffffffSfk", "kffffffffSfk", "kffffffffffk", "kFFFFFFFFFFk", "kkkkkkkkkkkk", "kffffffffffk",
        "kffffffffSfk", "kffffffffSfk", "kffffffffSfk", "kffffffffffk", "kffffffffffk", "kffffffffffk", "kffffffffffk", "kffffffffffk",
        "kffffffffffk", "kFFFFFFFFFFk", "kkkkkkkkkkkk", ".kk......kk.",
    };

    private static readonly string[] TvMap =
    {
        "kkkkkkkkkkkkkkkkkkkk", "kSSSSSSSSSSSSSSSSSSk", "kShhhhhhhhhhhhhhhhSk", "kShhhhhhhhhhhhhhhhSk", "kShhhhhhhhhhhhhhhhSk",
        "kShhhhhhhhhhhhhhhhSk", "kShhhhhhhhhhhhhhhhSk", "kShhhhhhhhhhhhhhhhSk", "kShhhhhhhhhhhhhhhhSk", "kShhhhhhhhhhhhhhhhSk",
        "kShhhhhhhhhhhhhhhhSk", "kSSSSSSSSSSSSSSSxSSk", "kkkkkkkkkkkkkkkkkkkk", "........kSSk........", "....kkkkkkkkkkkk....",
        "....kqqqqqqqqqqk....", "....kqQQQQQQQQqk....", "....kqqqqqqqqqqk....", "....kqQQQQQQQQqk....", "....kkkkkkkkkkkk....",
    };

    private static readonly string[] ArcadeMap =
    {
        "kkkkkkkkkkkkkk", "kPPPPPPPPPPPPk", "kPyyyyyyyyyyPk", "kPPPPPPPPPPPPk", "kkkkkkkkkkkkkk", "kPkhhhhhhhhkPk", "kPkhhhhhhhhkPk",
        "kPkhhhhhhhhkPk", "kPkhhhhhhhhkPk", "kPkhhhhhhhhkPk", "kPkkkkkkkkkkPk", "kPPPPPPPPPPPPk", "kPPkPPPPxPcPPk", "kPkkkPPPPPPPPk",
        "kkkkkkkkkkkkkk", "kPPPPPPPPPPPPk", "kPPPPPPPPPPPPk", "kPPPyyyyyyPPPk", "kPPPPPPPPPPPPk", "kPPPPPPPPPPPPk", "kPPPPPPPPPPPPk",
        "kPPPPPPPPPPPPk", "kkkkkkkkkkkkkk",
    };

    private static readonly string[] ShelfMap =
    {
        "kkkkkkkkkkkkkkkk", "kqqqqqqqqqqqqqqk", "kqkkkkkkkkkkkkqk", "kqkxCyPmxCkyPkqk", "kqkxCyPmxCkyPkqk", "kqkxCyPmxCkyPkqk",
        "kqkkkkkkkkkkkkqk", "kqqqqqqqqqqqqqqk", "kqkkkkkkkkkkkkqk", "kqkPmkCxyPCmxkqk", "kqkPmkCxyPCmxkqk", "kqkPmkCxyPCmxkqk",
        "kqkkkkkkkkkkkkqk", "kqqqqqqqqqqqqqqk", "kqkkkkkkkkkkkkqk", "kqkyCPmCxkyPmCqk", "kqkyCPmCxkyPmCqk", "kqkyCPmCxkyPmCqk",
        "kqkkkkkkkkkkkkqk", "kqqqqqqqqqqqqqqk", "kQQQQQQQQQQQQQQk", "kkkkkkkkkkkkkkkk",
    };

    private static readonly string[] MirrorMap =
    {
        "....kkkk....", "..kkqqqqkk..", ".kqqeeeeqqk.", "kqqeewweeqqk", "kqeewweeeeqk", "kqeweeeeeeqk", "kqeeeeeeeeqk", "kqeeeeeeeeqk",
        "kqeeeeeeeeqk", "kqeeeeeeeweqk", "kqeeeeeewweqk", "kqqeeeeeeqqk", ".kqqqqqqqqk.", "..kkkqqkkk..", "....kqqk....", "....kqqk....",
        "....kqqk....", "..kkqqqqkk..", ".kqqqqqqqqk.", ".kkkkkkkkkk.",
    };

    private static readonly string[] DoorMap =
    {
        "kkkkkkkkkkkk", "kddddddddddk", "kdDDDDDDDDdk", "kdDddddddDdk", "kdDddddddDdk", "kdDddddddDdk", "kdDDDDDDDDdk", "kddddddddddk",
        "kdDDDDDDDDdk", "kdDddddddDdk", "kdDdddddyDdk", "kdDddddddDdk", "kdDddddddDdk", "kdDDDDDDDDdk", "kddddddddddk", "kdDDDDDDDDdk",
        "kdDddddddDdk", "kdDddddddDdk", "kdDDDDDDDDdk", "kddddddddddk", "kkkkkkkkkkkk",
    };

    private static readonly string[] WindowMap =
    {
        "kkkkkkkkkkkkkkkk", "kwwwwwwwwwwwwwwk", "kw......w.....wk", "kw......w.....wk", "kw......w.....wk", "kw......w.....wk",
        "kwwwwwwwwwwwwwwk", "kw......w.....wk", "kw......w.....wk", "kw......w.....wk", "kw......w.....wk", "kwwwwwwwwwwwwwwk", "kkkkkkkkkkkkkkkk",
    };

    private static readonly string[] TankMap =
    {
        "kkkkkkkkkkkkkkkkkk", "keeeeeeeeeeeeeeeek", "kecccccccccccccccek", "keccccccccccccccek", "keccccccccccccccek", "keccccccgcccccccek",
        "keccccccgccccgccek", "kecccgccgccccgccek", "keyyyyyyyyyyyyyyek", "kkkkkkkkkkkkkkkkkk", "..kqqqqqqqqqqqqk..", "..kQk........kQk..",
        "..kQk........kQk..", "..kkk........kkk..",
    };

    private static readonly string[] PlantMap =
    {
        "...g..g...", "..glg.lg..", ".gllggllg.", "..glllgg..", ".ggllgllg.", "..gglgg...", "....g.....", "..kkkkkk..", "..kooook..",
        "..kooook..", "...kook...", "...kkkk...",
    };

    private static readonly string[] TrophyMap =
    {
        "..z......z......z...", ".zzz....zzz....zzz..", ".zzz....zzz....zzz..", "..z......z......z...", ".zzz....zzz....zzz..",
        "kkkkkkkkkkkkkkkkkkkk", "kqqqqqqqqqqqqqqqqqqk", "kkkkkkkkkkkkkkkkkkkk",
    };

    private static readonly string[] LampMap =
    {
        "..kk..", ".kxxk.", ".kxxk.", "kxxxxk", "kxxxxk", "kxxxxk", "kxxxxk", ".kxxk.", ".kxxk.", "..kk..", ".kSSk.", "kSSSSk", "kkkkkk",
    };

    private static readonly string[] DiscoMap =
    {
        "...k....", "...k....", "..kkkk..", ".kswsSk.", "kswsSwsk", "kSsSwsSk", "kswsSwsk", ".kSwsSk.", "..kkkk..",
    };

    private void BuildHouse()
    {
        _house.Children.Clear();
        _houseSpots.Clear();
        _decor.Children.Clear();
        _house.Children.Add(_wall);
        DrawWallpaper();
        var floor = new Rectangle { Width = W, Height = H - 560, Fill = new SolidColorBrush(PixelArt.C("#B57E52")) };
        Canvas.SetTop(floor, 560);
        _house.Children.Add(floor);
        for (int i = 0; i < 9; i++)
        {
            var board = new Rectangle { Width = W, Height = 2, Fill = new SolidColorBrush(PixelArt.C("#94623C")) };
            Canvas.SetTop(board, 575 + i * 17);
            _house.Children.Add(board);
        }
        var skirting = new Rectangle { Width = W, Height = 12, Fill = new SolidColorBrush(PixelArt.C("#8A5A3B")) };
        Canvas.SetTop(skirting, 552);
        _house.Children.Add(skirting);

        // The window, showing the sky outside.
        Canvas.SetLeft(_windowSky, 452); Canvas.SetTop(_windowSky, 158);
        _house.Children.Add(_windowSky);
        Put(_house, WindowMap, 8, 444, 150);
        if (CompanionLife.Shown("rug"))
        {
            var rug = new Ellipse { Width = 420, Height = 70, Fill = new SolidColorBrush(PixelArt.C("#C96B8A")), Stroke = new SolidColorBrush(PixelArt.C("#F2C14E")), StrokeThickness = 6, StrokeDashArray = new DoubleCollection { 2, 1 } };
            Canvas.SetLeft(rug, 430); Canvas.SetTop(rug, 600);
            _house.Children.Add(rug);
        }
        _house.Children.Add(_decor);

        double F = HouseFloor;
        var door = Put(_house, DoorMap, 8, 40, F - DoorMap.Length * 8);
        _houseSpots.Add(new Spot("out", "Go outside", Area(door), () => GoOutside()));
        var shelf = Put(_house, ShelfMap, 8, 150, F - ShelfMap.Length * 8);
        _houseSpots.Add(new Spot("books", "Bookshelf", Area(shelf), () => ReadAsync()));
        if (CompanionLife.Shown("lamp"))
        {
            var lamp = Put(_decor, LampMap, 6, 190, F - ShelfMap.Length * 8 - LampMap.Length * 6);
            for (int i = 0; i < 2; i++)
            {
                var blob = new Ellipse { Width = 10, Height = 12, Fill = new SolidColorBrush(PixelArt.C("#FFD84D")), IsHitTestVisible = false };
                Canvas.SetLeft(blob, 198 + i * 6);
                blob.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(F - ShelfMap.Length * 8 - 24, F - ShelfMap.Length * 8 - 64, TimeSpan.FromSeconds(3 + i * 1.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
                _decor.Children.Add(blob);
            }
        }
        var bed = Put(_house, BedMap, 8, 300, F - BedMap.Length * 8);
        _houseSpots.Add(new Spot("bed", "Bed", Area(bed), () => SleepAsync()));
        if (CompanionLife.Shown("trophies"))
        {
            var trophies = Put(_decor, TrophyMap, 7, 340, 230);
            _houseSpots.Add(new Spot("trophies", "Trophy shelf", Area(trophies), () => TrophiesAsync()));
        }
        var tv = Put(_house, TvMap, 8, 520, F - TvMap.Length * 8);
        Canvas.SetLeft(_tvScreen, 520 + 16); Canvas.SetTop(_tvScreen, F - TvMap.Length * 8 + 16);
        _tvScreen.Background = new SolidColorBrush(PixelArt.C("#1A1D26"));
        _house.Children.Add(_tvScreen);
        _houseSpots.Add(new Spot("tv", "TV", Area(tv), () => TvAsync()));
        var arcade = Put(_house, ArcadeMap, 8, 720, F - ArcadeMap.Length * 8);
        var arcadeGlow = new TextBlock { Text = "★", FontSize = 26, Foreground = new SolidColorBrush(PixelArt.C("#FFD84D")), IsHitTestVisible = false };
        Canvas.SetLeft(arcadeGlow, 760); Canvas.SetTop(arcadeGlow, F - ArcadeMap.Length * 8 + 46);
        arcadeGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromSeconds(0.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        _house.Children.Add(arcadeGlow);
        _houseSpots.Add(new Spot("arcade", "Arcade: Star Catch", Area(arcade), () => StarCatchAsync()));
        if (CompanionLife.Shown("poster"))
        {
            var posterArt = new Border { Width = 84, Height = 112, ClipToBounds = true };
            var frame = new Border { Width = 100, Height = 128, BorderThickness = new Thickness(8), BorderBrush = new SolidColorBrush(PixelArt.C("#C48A5A")), Child = posterArt };
            Canvas.SetLeft(frame, 870); Canvas.SetTop(frame, 170);
            _decor.Children.Add(frame);
            _houseSpots.Add(new Spot("poster", "Game poster", new Rect(862, 162, 116, 144), () => PosterAsync()));
            ShowGameArt(posterArt, FavoriteGame());
        }
        var fridge = Put(_house, FridgeMap, 8, 880, F - FridgeMap.Length * 8);
        _houseSpots.Add(new Spot("fridge", "Fridge", Area(fridge), () => FeedAsync()));
        var mirror = Put(_house, MirrorMap, 8, 1010, F - MirrorMap.Length * 8);
        _houseSpots.Add(new Spot("mirror", "Mirror: dress up", Area(mirror), () => DressUpAsync()));
        if (CompanionLife.Shown("fishtank"))
        {
            var tank = Put(_decor, TankMap, 7, 1130, F - TankMap.Length * 7);
            for (int i = 0; i < 3; i++)
            {
                var fish = new TextBlock { Text = i % 2 == 0 ? "◀" : "▶", FontSize = 14, Foreground = new SolidColorBrush(PixelArt.C(new[] { "#FF9F43", "#FFD84D", "#FF8FB1" }[i])), IsHitTestVisible = false };
                Canvas.SetTop(fish, F - TankMap.Length * 7 + 18 + i * 12);
                double a = 1140, b = 1230;
                fish.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(i % 2 == 0 ? b : a, i % 2 == 0 ? a : b, TimeSpan.FromSeconds(3 + i)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                _decor.Children.Add(fish);
            }
            _houseSpots.Add(new Spot("fish", "Fish tank", Area(tank), () => FishAsync()));
        }
        else if (CompanionLife.Shown("plant"))
            Put(_decor, PlantMap, 7, 1150, F - PlantMap.Length * 7);
        if (CompanionLife.Shown("plant") && CompanionLife.Shown("fishtank")) Put(_decor, PlantMap, 6, 1220, F - PlantMap.Length * 6 - 100);
        if (CompanionLife.Shown("disco"))
        {
            var disco = Put(_decor, DiscoMap, 7, 612, 0);
            disco.RenderTransformOrigin = new Point(0.5, 0);
            _houseSpots.Add(new Spot("disco", "Disco ball", Area(disco), () => DiscoAsync()));
        }
        // The lamp light at night.
        _houseSpots.Insert(0, new Spot("pal", CompanionArt.Name, Rect.Empty, () => PalMenuAsync()));
        _houseSpots.Sort((a, b) => SpotX(a).CompareTo(SpotX(b)));
        ShowGameArt(_tvScreen, RecentGame(), dim: true);
    }

    private void DrawWallpaper()
    {
        _wall.Children.Clear();
        string wall = CompanionLife.Wallpaper;
        var (bg, fg) = wall switch
        {
            "wall:peach" => ("#FFD3B6", "#FFBE98"),
            "wall:night" => ("#2E3A6A", "#FFE27A"),
            "wall:lilac" => ("#E3D5FF", "#CDB8FF"),
            "wall:wood" => ("#C99566", "#A87447"),
            _ => ("#CDEFE2", "#B2E3D0"),
        };
        _wall.Children.Add(new Rectangle { Width = W, Height = 560, Fill = new SolidColorBrush(PixelArt.C(bg)) });
        var pattern = new SolidColorBrush(PixelArt.C(fg));
        if (wall == "wall:night")
            for (int i = 0; i < 60; i++)
            {
                var s = new TextBlock { Text = "✦", FontSize = 12, Foreground = pattern, Opacity = 0.7 };
                Canvas.SetLeft(s, (i * 97) % W); Canvas.SetTop(s, (i * 53) % 540);
                _wall.Children.Add(s);
            }
        else if (wall == "wall:wood")
            for (int y = 0; y < 560; y += 36) { var r = new Rectangle { Width = W, Height = 3, Fill = pattern }; Canvas.SetTop(r, y); _wall.Children.Add(r); }
        else
            for (int x = 0; x < W; x += 48) { var r = new Rectangle { Width = 18, Height = 560, Fill = pattern, Opacity = 0.6 }; Canvas.SetLeft(r, x); _wall.Children.Add(r); }
    }

    private Game? RecentGame() =>
        Host.AllGames.Where(g => g.IsGame).OrderByDescending(g => g.LastPlayed ?? g.RecentAt).FirstOrDefault();

    private Game? FavoriteGame() =>
        Host.AllGames.Where(g => g.IsGame && g.IsFavorite).OrderByDescending(g => Labs.HoursOf(g, Host.Playtime)).FirstOrDefault() ?? RecentGame();

    private static void ShowGameArt(Border box, Game? game, bool dim = false)
    {
        var art = game?.Cover ?? game?.PortraitCover ?? game?.HeaderCover;
        if (art == null) { box.Child = new TextBlock { Text = "▶", FontSize = 28, Foreground = Brushes.White, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; return; }
        box.Child = new Image { Source = art, Stretch = Stretch.UniformToFill, Opacity = dim ? 0.35 : 1 };
    }

    // ---------------------------------------------------------------- the display

    private void BuildHud()
    {
        _hud.Children.Clear();
        var card = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(18, 12, 18, 14), Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x10, 0x14, 0x24)) };
        var stack = new StackPanel { Width = 300 };
        stack.Children.Add(_nameText);
        stack.Children.Add(_levelText);
        var xpBack = new Grid { Margin = new Thickness(0, 6, 0, 10), Height = 8 };
        xpBack.Children.Add(new Rectangle { Height = 8, RadiusX = 4, RadiusY = 4, Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) });
        _xpBar.HorizontalAlignment = HorizontalAlignment.Left;
        _xpBar.Fill = new SolidColorBrush(PixelArt.C("#FF8FB1"));
        xpBack.Children.Add(_xpBar);
        stack.Children.Add(xpBack);
        string[] icons = { "🍎", "⚡", "🎈" };
        string[] names = { "Food", "Energy", "Fun" };
        string[] colors = { "#FF8A6E", "#FFD84D", "#7FD8FF" };
        for (int i = 0; i < 3; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = icons[i], FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 16, Foreground = Brushes.White });
            var name = new TextBlock { Text = names[i], FontSize = 15, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var back = new Grid { Height = 12, VerticalAlignment = VerticalAlignment.Center };
            back.Children.Add(new Rectangle { RadiusX = 6, RadiusY = 6, Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) });
            // A width to start from: animating from an unset width (NaN) fails on every frame and stops SquareDeck drawing (9.0.5).
            _needBars[i] = new Rectangle { Width = 6, RadiusX = 6, RadiusY = 6, HorizontalAlignment = HorizontalAlignment.Left, Fill = new SolidColorBrush(PixelArt.C(colors[i])) };
            back.Children.Add(_needBars[i]);
            Grid.SetColumn(back, 2);
            row.Children.Add(back);
            stack.Children.Add(row);
        }
        card.Child = stack;
        Canvas.SetLeft(card, 28); Canvas.SetTop(card, 40);
        _hud.Children.Add(card);
        var starsCard = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(18, 8, 18, 10), Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x10, 0x14, 0x24)), Child = _starsText };
        Canvas.SetRight(starsCard, 28); Canvas.SetTop(starsCard, 40);
        _hud.Children.Add(starsCard);
        _hints.Resources["FgBrush"] = Brushes.White;
        _hints.Resources["SubtleBrush"] = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
        _hints.Resources["BgBrush"] = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x24));
        var hintCard = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 8, 16, 8), Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x10, 0x14, 0x24)), Child = _hints };
        Canvas.SetRight(hintCard, 28); Canvas.SetBottom(hintCard, 34);
        _hud.Children.Add(hintCard);
        _label.Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x10, 0x14, 0x24));
        _label.Child = _labelText;
        _ring.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        _ring.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Color = Colors.White, Opacity = 0.7 };
        _ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        RefreshHud();
    }

    private void RefreshHud()
    {
        _nameText.Text = CompanionArt.Name;
        int level = CompanionLife.Level;
        _levelText.Text = $"♥ Level {level}  ·  {CompanionLife.LevelNames[level - 1]}";
        _xpBar.Width = 300 * CompanionLife.LevelProgress;
        _starsText.Text = "★ " + CompanionLife.Stars;
        double[] values = { CompanionLife.Food, CompanionLife.Energy, CompanionLife.Fun };
        for (int i = 0; i < 3; i++)
        {
            var bar = _needBars[i];
            if (bar == null) continue;
            double from = double.IsNaN(bar.ActualWidth) || bar.ActualWidth <= 0 ? (double.IsNaN(bar.Width) ? 6 : bar.Width) : bar.ActualWidth;
            bar.BeginAnimation(WidthProperty, new DoubleAnimation(from, Math.Max(6, 196 * values[i] / 100), TimeSpan.FromSeconds(0.4)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        if (_game == null && !_gameOn)
            _hints.Template = _overlay.Visibility == Visibility.Visible ? "{Back}  Close"
                : _seekAt != null ? "{Select}  Look here        Where's " + CompanionArt.Name + "?"
                : "{Select}  Use        {Details}  Pet        {Options}  Shop        " + (_place switch { "yard" => "{LB}  Town   {RB}  Inside", "town" => "{LB}{RB}  Home", _ => "{LB}{RB}  Outside" }) + "        ☰  Quests        {Back}  Leave";
        if (!_busy && !_asleep) SetMood(RestingMood());
    }

    private void ShowFocus()
    {
        var spots = Spots;
        if (spots.Count == 0 || _overlay.Visibility == Visibility.Visible) { _ring.Visibility = _label.Visibility = Visibility.Collapsed; return; }
        _focus = Math.Clamp(_focus, 0, spots.Count - 1);
        var s = spots[_focus];
        Rect r = s.Id == "pal" ? new Rect(_pos.X - 60, Floor - 112, 120, 120) : s.Area;
        _ring.Visibility = _label.Visibility = Visibility.Visible;
        _ring.Width = r.Width; _ring.Height = r.Height;
        Canvas.SetLeft(_ring, r.X); Canvas.SetTop(_ring, r.Y);
        _labelText.Text = s.Label;
        _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_label, Math.Clamp(r.X + r.Width / 2 - _label.DesiredSize.Width / 2, 10, W - _label.DesiredSize.Width - 10));
        Canvas.SetTop(_label, Math.Max(10, r.Y - 44));
    }

    private double Floor => _place switch { "yard" => YardFloor, "town" => TownFloor, _ => HouseFloor };

    private void SetPlace(string place, bool instant = false)
    {
        // Leaving in the middle of hide-and-seek: it comes out.
        if (_seekAt != null) { _seekAt = null; _actor.BeginAnimation(OpacityProperty, null); _actor.Opacity = 1; }
        _place = place;
        _yard.Visibility = place == "yard" ? Visibility.Visible : Visibility.Collapsed;
        _house.Visibility = place == "house" ? Visibility.Visible : Visibility.Collapsed;
        _town.Visibility = place == "town" ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetTop(_actor, Floor - 300);
        _focus = Math.Max(0, Spots.FindIndex(s => s.Id == "pal"));
        UpdateSky();
        // Lights down at night in the house; a moonlit yard.
        _night.BeginAnimation(OpacityProperty, null);
        _night.Opacity = IsNight ? (place == "house" ? 0.08 : 0.22) : 0;
        ShowFocus();
        RefreshHud();
    }

    // ---------------------------------------------------------------- input

    public override bool OnAction(NavAction action)
    {
        if (_game != null) { GameAction(action); return true; }
        if (_gameOn) { GameInput(action); return true; }
        if (_overlay.Visibility == Visibility.Visible)
        {
            if (action is NavAction.Accept or NavAction.Back) { CloseOverlay(); UiSound.Back(); }
            return true;
        }
        switch (action)
        {
            case NavAction.Left or NavAction.Up: MoveFocus(-1); return true;
            case NavAction.Right or NavAction.Down: MoveFocus(1); return true;
            case NavAction.Accept: _ = UseAsync(Spots[Math.Clamp(_focus, 0, Spots.Count - 1)]); return true;
            case NavAction.X: if (_seekAt == null) _ = Run(PetAsync); return true;
            case NavAction.Y: if (_seekAt == null) _ = Run(ShopAsync); return true;
            case NavAction.LB or NavAction.RB:
                // Town ◀ yard ▶ house.
                Func<Task> go = _place switch
                {
                    "yard" => action == NavAction.LB ? GoTown : GoInside,
                    "town" => GoHomeFromTown,
                    _ => GoOutside,
                };
                _ = Run(go);
                return true;
            case NavAction.Start: ShowQuests(); return true;
            case NavAction.Back: _ = AskLeaveAsync(); return true;
        }
        return true;
    }

    private bool _askingLeave;

    /// <summary>9.0.17: B asks first, so a stray press doesn't end a visit.</summary>
    private async Task AskLeaveAsync()
    {
        if (_askingLeave) return;
        _askingLeave = true;
        try
        {
            int c = await Host.AskAsync("Leave the playground?", "You're about to leave " + CompanionArt.Name + "'s playground. It'll be right here when you come back.", "Leave", "Stay");
            if (c == 0) { UiSound.Back(); Host.Pop(this); }
        }
        finally { _askingLeave = false; }
    }

    private void MoveFocus(int by)
    {
        var spots = Spots;
        if (spots.Count == 0) return;
        // Order by where things are now (the companion moves around).
        var current = spots[Math.Clamp(_focus, 0, spots.Count - 1)];
        spots.Sort((a, b) => SpotX(a).CompareTo(SpotX(b)));
        _focus = Math.Clamp(spots.IndexOf(current) + by, 0, spots.Count - 1);
        UiSound.Move();
        Host.Rumble();
        ShowFocus();
    }

    private void Tapped(Point p)
    {
        if (_game != null) { GameTap(p); return; }
        if (_gameOn) { GameInput(p.X < _pos.X ? NavAction.Left : NavAction.Right); return; }
        if (_overlay.Visibility == Visibility.Visible) { CloseOverlay(); return; }
        if (_seekAt == null && Math.Abs(p.X - _pos.X) < 60 && p.Y > Floor - 120 && p.Y < Floor + 10) { _focus = Spots.FindIndex(s => s.Id == "pal"); ShowFocus(); _ = Run(PetAsync); return; }
        int i = Spots.FindIndex(s => s.Id != "pal" && s.Area.Contains(p));
        if (i < 0) return;
        _focus = i;
        ShowFocus();
        _ = UseAsync(Spots[i]);
    }

    private async Task UseAsync(Spot s)
    {
        if (_busy) return;
        UiSound.Select();
        if (_seekAt != null) { Seek(s); return; } // hide-and-seek: looking behind things
        bool walk = s.Id is not ("pal" or "door" or "out" or "shop" or "mirror" or "home");
        // A neighbor's lost thing hidden there is found instead (side missions).
        await Run(async () => { if (walk) await WalkTo(SpotX(s)); if (!FindLost(s)) await s.Act(); });
    }

    /// <summary>Runs an action with the companion busy (no idle wandering meanwhile), then puts its face back.</summary>
    private async Task Run(Func<Task> what)
    {
        if (_busy) return;
        _busy = true;
        if (_asleep) { _asleep = false; Redraw(); }
        try { await what(); }
        catch (Exception ex) { CrashLog.Write("Companion playground", ex); }
        finally
        {
            _busy = false;
            _nextIdle = DateTime.Now.AddSeconds(6 + Random.Shared.Next(6));
            if (!_closed) { RefreshHud(); ShowFocus(); }
        }
    }

    // ---------------------------------------------------------------- living there

    private void Tick()
    {
        _ticks++;
        // Blinks now and then.
        bool blink = _ticks % 14 == 0;
        if (blink != _blink) { _blink = blink; Redraw(); }
        if (_ticks % 4 == 0 && _place == "yard") MoveButterflies();
        if (_ticks % 240 == 0) { CompanionLife.CatchUp(); UpdateSky(); RefreshHud(); }
        if (_ring.Visibility == Visibility.Visible && Spots.Count > 0 && Spots[Math.Clamp(_focus, 0, Spots.Count - 1)].Id == "pal") ShowFocus();
        if (_busy || _gameOn || _game != null || _seekAt != null || _overlay.Visibility == Visibility.Visible || DateTime.Now < _nextIdle) return;
        _nextIdle = DateTime.Now.AddSeconds(7 + Random.Shared.Next(8));
        _ = Run(IdleAsync);
    }

    private void MoveButterflies()
    {
        foreach (var b in _butterflies)
        {
            double x = Canvas.GetLeft(b), y = Canvas.GetTop(b);
            double nx = Math.Clamp(x + Random.Shared.Next(-50, 51), 40, W - 60), ny = Math.Clamp(y + Random.Shared.Next(-30, 31), 360, 560);
            b.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(nx, TimeSpan.FromSeconds(1)) { EasingFunction = new SineEase() });
            b.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(ny, TimeSpan.FromSeconds(1)) { EasingFunction = new SineEase() });
        }
    }

    /// <summary>What it does on its own: wanders, chases butterflies, naps when tired, and talks.</summary>
    private async Task IdleAsync()
    {
        var mood = CompanionLife.Mood();
        if (mood == "sleepy" && Random.Shared.Next(2) == 0)
        {
            Say(new[] { "*yaaawn*", "So… sleepy…", "Just resting my eyes." }[Random.Shared.Next(3)]);
            _asleep = true; Redraw();
            for (int i = 0; i < 4; i++) { Float("z", "#C9D3E8"); await Wait(0.9); }
            _asleep = false; Redraw();
            return;
        }
        if (CompanionLife.NeedLine() is { } need && Random.Shared.Next(3) == 0) { Say(need, 3.6); Wiggle(6, 2, 0.3); await Wait(2); return; }
        int pick = Random.Shared.Next(_place == "yard" ? 7 : 6);
        switch (pick)
        {
            case 0 when _place == "yard" && _butterflies.Count > 0:
            {
                var b = _butterflies[Random.Shared.Next(_butterflies.Count)];
                Say(new[] { "Butterfly! Come back!", "I almost got it!", "So fast!" }[Random.Shared.Next(3)]);
                for (int i = 0; i < 3; i++) { await WalkTo(Canvas.GetLeft(b)); Hop(1, 34); await Wait(0.45); }
                break;
            }
            case 1:
                await WalkTo(80 + Random.Shared.NextDouble() * (W - 160));
                if (Random.Shared.Next(2) == 0) Say(Thought());
                break;
            case 2:
                Say(Thought(), 3.6);
                Wiggle(5, 2, 0.4);
                await Wait(2);
                break;
            case 3:
                // A little dance.
                _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-12, 12, TimeSpan.FromSeconds(0.35)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) });
                Hop(4, 10);
                Float("♪", "#C3A6FF", 3);
                await Wait(2.8);
                break;
            case 4:
                // A somersault.
                _hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -60, TimeSpan.FromSeconds(0.35)) { AutoReverse = true, EasingFunction = new QuadraticEase() });
                _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.7)));
                Float("✦", "#FFFFFF", 5, burst: true);
                await Wait(1);
                _turn.BeginAnimation(RotateTransform.AngleProperty, null);
                break;
            case 5:
                // Looks around.
                _look = -1; Redraw(); await Wait(0.8);
                _look = 1; Redraw(); await Wait(0.8);
                _look = 0; Redraw();
                if (Random.Shared.Next(2) == 0) Say("Hmm… what should we do?");
                break;
            default:
                await WalkTo(Random.Shared.Next(2) == 0 ? 640 : 200 + Random.Shared.NextDouble() * 880);
                break;
        }
    }

    private string Greeting()
    {
        double away = (DateTime.Now - LastVisit()).TotalHours;
        Labs.Put("pg.visit", DateTime.Now.Ticks.ToString());
        if (away > 72) return $"You're back! It's been {Math.Floor(away / 24)} days! I kept the yard tidy.";
        if (away > 20) return "There you are! I missed you!";
        if (away < 0.2) return "Back again? Yay!";
        int h = DateTime.Now.Hour;
        return h switch
        {
            < 5 => "It's so late! Couldn't sleep either?",
            < 12 => "Good morning! Breakfast?",
            < 17 => "Hi hi! Perfect day to play.",
            < 21 => "Good evening! The sunset's pretty.",
            _ => "Hi! The stars are out tonight.",
        };
    }

    private static DateTime LastVisit() => long.TryParse(Labs.Get("pg.visit", ""), out var t) ? new DateTime(t) : DateTime.Now.AddDays(-1);

    private string Thought()
    {
        var game = RecentGame();
        var options = new List<string>
        {
            "This is the best yard in the whole launcher.", "Do clouds have save points?", "I wonder what the fish are thinking.",
            "If I had thumbs, I'd be unstoppable at games.", "Living my best pixel life.", "Is it snack o'clock yet?",
            "I named that cloud Gerald.", "Ten out of ten, would hang out again.",
        };
        if (game != null) { options.Add($"I keep thinking about {game.Name}."); options.Add($"Teach me to play {game.Name} sometime?"); }
        if (IsNight) { options.Add("The stars are so bright tonight."); options.Add("I think I saw a shooting star!"); }
        if (_place == "house") { options.Add("Home sweet home."); options.Add("This place could use a disco ball. Just saying."); }
        if (CompanionLife.Level >= 5) options.Add("Have I told you you're my favorite? You are.");
        return options[Random.Shared.Next(options.Count)];
    }

    private void Reward(int xp, double food = 0, double energy = 0, double fun = 0)
    {
        if (food != 0) CompanionLife.Food += food;
        if (energy != 0) CompanionLife.Energy += energy;
        if (fun != 0) CompanionLife.Fun += fun;
        if (xp > 0 && CompanionLife.AddXp(xp))
        {
            int level = CompanionLife.Level;
            CompanionLife.Stars += 10;
            Later(1.2, () =>
            {
                Float("★", "#FFD84D", 10, burst: true);
                Float("♥", "#FF5A7A", 6, burst: true);
                Hop(3, 34);
                CompanionSounds.Effect("chime");
                Say($"Level {level}! We're {CompanionLife.LevelNames[level - 1].ToLowerInvariant()} now! (+10 ★)", 4.5);
                RefreshHud();
            });
        }
        RefreshHud();
    }

    // ---------------------------------------------------------------- what things do

    private async Task PetAsync()
    {
        int palAt = Spots.FindIndex(s => s.Id == "pal");
        if (palAt >= 0) _focus = palAt;
        CompanionSounds.Pet(CompanionArt.SpeciesId);
        SetMood("love");
        Hop(2, 18);
        Float("♥", "#FF5A7A", 4);
        var lines = new[] { "Hehe!", "That's the spot!", "More, more!", "You're the best.", "*happy wiggle*", "I like you a lot, you know." };
        Say(lines[Random.Shared.Next(lines.Length)]);
        Quests("pet");
        bool star = CompanionLife.PetReward();
        if (star) Later(0.5, () => Float("+1 ★", "#FFD84D", 1, size: 18));
        Reward(1, fun: 3);
        await Wait(1.2);
    }

    private async Task GoInside()
    {
        await WalkTo(500 + 14 * 8);
        _actor.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.25)));
        await Wait(0.25);
        UiSound.Page();
        SetPlace("house");
        _pos.BeginAnimation(TranslateTransform.XProperty, null);
        _pos.X = 110;
        _actor.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.25)));
        Say(new[] { "Welcome to my place!", "Home sweet home!", "Shoes off, please. (I don't have shoes.)" }[Random.Shared.Next(3)]);
        await Wait(0.3);
    }

    private async Task GoOutside()
    {
        await WalkTo(90);
        _actor.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.25)));
        await Wait(0.25);
        UiSound.Page();
        SetPlace("yard");
        _pos.BeginAnimation(TranslateTransform.XProperty, null);
        _pos.X = 500 + 14 * 8;
        _actor.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.25)));
        Say(IsNight ? "Ooh, it's dark out. Look at the stars!" : "Fresh air!");
        await Wait(0.3);
    }

    private async Task TreeAsync()
    {
        if (!CompanionLife.AppleReady)
        {
            Say(new[] { "No apples yet. Trees need their beauty sleep.", "It's still growing them. Patience!", "Come back in a bit for more apples." }[Random.Shared.Next(3)]);
            Wiggle(5, 2, 0.3);
            await Wait(1.5);
            return;
        }
        Say("Shake, shake, shake!");
        for (int i = 0; i < 3; i++) { Hop(1, 30); await Wait(0.42); }
        // An apple falls into its arms.
        var apple = _apples.Children.OfType<Ellipse>().FirstOrDefault();
        if (apple != null)
            apple.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(Canvas.GetTop(apple), 22 * 9 - 30, TimeSpan.FromSeconds(0.5)) { EasingFunction = new BounceEase { Bounces = 1 } });
        await Wait(0.6);
        CompanionLife.PickApple();
        DrawApples();
        CompanionSounds.Effect("crunch");
        Say("Got one! It's in the fridge. (+1 apple)");
        Float("🍎", "#E8403A", 1);
        Reward(2, fun: 4);
        await Wait(1);
    }

    private async Task FlowersAsync()
    {
        int streak = int.TryParse(Labs.Get("pg.streak", "1"), out var s) ? s : 1;
        Say(streak >= 6 ? "The whole bed is blooming! Because you kept coming back." : $"A new flower blooms for each day in a row you visit. Day {streak}!", 3.8);
        Float("✿", "#FF8FB1", 4);
        Float("~", "#7FD8FF", 3, burst: true);
        foreach (var f in _flowerBed.Children.OfType<Image>())
        {
            f.RenderTransformOrigin = new Point(0.5, 1);
            f.RenderTransform = new ScaleTransform();
            ((ScaleTransform)f.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.15, TimeSpan.FromSeconds(0.25)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) });
        }
        Reward(1, fun: 4);
        await Wait(1.6);
    }

    private async Task MailAsync()
    {
        var mail = CompanionLife.OpenMail(RecentGame()?.Name, CompanionArt.Name);
        if (mail == null)
        {
            Say(new[] { "No more mail today. The mail-bird comes tomorrow.", "Empty! I check it every five minutes anyway.", "Tomorrow's letter is going to be great. I can feel it." }[Random.Shared.Next(3)]);
            await Wait(1.4);
            return;
        }
        Hop(1);
        CompanionSounds.Effect("chime");
        _mailFlag.Visibility = Visibility.Collapsed;
        var (stars, note, streak) = mail.Value;
        ShowLetter(note, $"+{stars} ★" + (streak > 1 ? $"   ·   {streak} days in a row" : ""));
        Reward(2, fun: 6);
        Quests("mail");
        DrawFlowers();
        await Wait(0.4);
    }

    private void ShowLetter(string text, string footer)
    {
        _overlay.Children.Clear();
        _overlay.Children.Add(new Rectangle { Width = W, Height = H, Fill = new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)) });
        var paper = new Border
        {
            Width = 560, CornerRadius = new CornerRadius(8), Padding = new Thickness(40, 34, 40, 30), Background = new SolidColorBrush(PixelArt.C("#FFF6E0")),
            BorderBrush = new SolidColorBrush(PixelArt.C("#E2CDA4")), BorderThickness = new Thickness(3), RenderTransform = new RotateTransform(-2),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.5 },
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = text, FontSize = 22, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(PixelArt.C("#4A3A2A")), FontFamily = new FontFamily("Segoe Print"), LineHeight = 34 });
        stack.Children.Add(new TextBlock { Text = footer, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 22, 0, 0), Foreground = new SolidColorBrush(PixelArt.C("#C98A1E")) });
        paper.Child = stack;
        Canvas.SetLeft(paper, (W - 560) / 2); Canvas.SetTop(paper, 150);
        _overlay.Children.Add(paper);
        _overlay.Visibility = Visibility.Visible;
        paper.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(H, 150, TimeSpan.FromSeconds(0.45)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
        ShowFocus();
        RefreshHud();
    }

    private void CloseOverlay()
    {
        _overlay.Visibility = Visibility.Collapsed;
        _overlay.Children.Clear();
        ShowFocus();
        RefreshHud();
    }

    private async Task ToysAsync()
    {
        var toys = CompanionLife.Catalog.Where(i => i.Kind == "toy" && CompanionLife.Owns(i.Id)).ToList();
        string toy = "ball";
        if (toys.Count > 1)
        {
            int c = await Host.AskAsync("Play with…", null, toys.Select(t => t.Name).Append("Cancel").ToArray());
            if (c < 0 || c >= toys.Count) return;
            toy = toys[c].Id;
        }
        if (CompanionLife.Energy < 15) { Say("Too tired to play… maybe a nap first?"); SetMood("sleepy"); await Wait(1.6); return; }
        switch (toy)
        {
            case "kite":
            {
                Say("Up, up, up!");
                var kite = new TextBlock { Text = "◆", FontSize = 46, Foreground = new SolidColorBrush(PixelArt.C("#FF8FB1")) };
                var line = new Line { Stroke = Brushes.White, StrokeThickness = 1.5, X1 = _pos.X, Y1 = Floor - 60 };
                _yard.Children.Add(line);
                _yard.Children.Add(kite);
                for (int i = 0; i < 24; i++)
                {
                    double kx = _pos.X + 150 + Math.Sin(i * 0.5) * 60, ky = 380 - i * 8 + Math.Cos(i * 0.7) * 20;
                    Canvas.SetLeft(kite, kx); Canvas.SetTop(kite, ky);
                    line.X2 = kx + 20; line.Y2 = ky + 40;
                    if (i % 6 == 0) Hop(1, 12);
                    await Wait(0.12);
                }
                await Wait(0.6);
                _yard.Children.Remove(kite); _yard.Children.Remove(line);
                Say("Best. Kite. Ever.");
                break;
            }
            case "bubbles":
            {
                Say("Bubbles! Pop pop pop!");
                for (int i = 0; i < 8; i++)
                {
                    var bubble = new Ellipse { Width = 26, Height = 26, Stroke = new SolidColorBrush(Color.FromArgb(0xC0, 0xBD, 0xE6, 0xFF)), StrokeThickness = 2.5, Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) };
                    double bx = _pos.X + Random.Shared.Next(-160, 160);
                    Canvas.SetLeft(bubble, bx);
                    _yard.Children.Add(bubble);
                    var rise = new DoubleAnimation(Floor - 80, Floor - 300 - Random.Shared.Next(100), TimeSpan.FromSeconds(1.6));
                    rise.Completed += (_, _) => _yard.Children.Remove(bubble);
                    bubble.BeginAnimation(Canvas.TopProperty, rise);
                    if (i % 2 == 0) { await WalkTo(bx); Hop(1, 40); CompanionSounds.Effect("pop"); }
                    await Wait(0.2);
                }
                break;
            }
            default:
            {
                // Ball or frisbee: thrown, chased, brought back.
                bool frisbee = toy == "frisbee";
                Say(frisbee ? "Throw it far!" : "Throw it! Throw it!");
                _toyBall.Visibility = Visibility.Collapsed;
                var thing = frisbee ? (FrameworkElement)new Ellipse { Width = 40, Height = 12, Fill = new SolidColorBrush(PixelArt.C("#7FD8FF")), Stroke = new SolidColorBrush(PixelArt.C("#4A7BD9")), StrokeThickness = 2 }
                                    : new Ellipse { Width = 26, Height = 26, Fill = _toyBall.Fill };
                _yard.Children.Add(thing);
                double startX = _pos.X, target = startX < 640 ? startX + 300 + Random.Shared.Next(150) : startX - 300 - Random.Shared.Next(150);
                target = Math.Clamp(target, 100, W - 100);
                thing.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(startX, target, TimeSpan.FromSeconds(0.9)));
                var arc = new DoubleAnimationUsingKeyFrames();
                arc.KeyFrames.Add(new EasingDoubleKeyFrame(Floor - 60, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                arc.KeyFrames.Add(new EasingDoubleKeyFrame(frisbee ? Floor - 200 : Floor - 260, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.45)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                arc.KeyFrames.Add(new EasingDoubleKeyFrame(Floor - 26, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9)), new BounceEase { Bounces = 2, EasingMode = EasingMode.EaseOut }));
                thing.BeginAnimation(Canvas.TopProperty, arc);
                await Wait(0.3);
                await WalkTo(target);
                Hop(1, 24);
                CompanionSounds.Effect("boing");
                thing.BeginAnimation(Canvas.LeftProperty, null);
                thing.BeginAnimation(Canvas.TopProperty, null);
                _yard.Children.Remove(thing);
                Say(new[] { "Got it!", "Again! Again!", "I'm basically a pro athlete." }[Random.Shared.Next(3)]);
                await WalkTo(startX);
                _toyBall.Visibility = Visibility.Visible;
                break;
            }
        }
        Float("♥", "#FF5A7A", 2);
        Reward(3, energy: -6, fun: 16);
        Quests("toy");
        await Wait(0.6);
    }

    private async Task TelescopeAsync()
    {
        if (!IsNight)
        {
            Say("You can't see stars in the daytime. Trust me, I tried.");
            await Wait(1.5);
            return;
        }
        Say("Looking at the stars…", 2.5);
        _look = 1; Redraw();
        await Wait(1.6);
        if (Random.Shared.Next(2) == 0)
        {
            var shooting = new TextBlock { Text = "✦", FontSize = 30, Foreground = Brushes.White };
            _yard.Children.Add(shooting);
            shooting.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(1100, 300, TimeSpan.FromSeconds(0.9)));
            shooting.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(80, 260, TimeSpan.FromSeconds(0.9)));
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.9));
            fade.Completed += (_, _) => _yard.Children.Remove(shooting);
            shooting.BeginAnimation(OpacityProperty, fade);
            await Wait(0.9);
            Say("A shooting star! I wished for more snacks. And you. Mostly snacks.", 3.8);
            CompanionLife.Stars += 2;
            Float("+2 ★", "#FFD84D", 1, size: 18);
        }
        else Say("I think that one's named after you.", 3);
        _look = 0; Redraw();
        Reward(2, fun: 8);
        await Wait(1);
    }

    private async Task SleepAsync()
    {
        if (CompanionLife.Energy > 85)
        {
            Say(new[] { "I'm not sleepy! Let's play!", "Nap? Me? I'm full of beans.", "Five more hours of being awake, please." }[Random.Shared.Next(3)]);
            Hop(2, 16);
            await Wait(1.4);
            return;
        }
        Say("Goodnight… *snuggles in*");
        _asleep = true; Redraw();
        _night.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, TimeSpan.FromSeconds(0.6)));
        for (int i = 0; i < 5; i++) { Float("z", "#C9D3E8", 1, size: 26); await Wait(0.8); }
        _night.BeginAnimation(OpacityProperty, new DoubleAnimation(IsNight ? 0.08 : 0, TimeSpan.FromSeconds(0.6)));
        _asleep = false; Redraw();
        Hop(1, 20);
        Say(new[] { "What a nap! I feel brand new.", "I dreamt we beat the final boss.", "Good morning! …Is it morning?" }[Random.Shared.Next(3)]);
        Reward(1, energy: 45);
        Quests("nap");
        await Wait(1);
    }

    private async Task FeedAsync()
    {
        var snacks = CompanionLife.Snacks().ToList();
        if (snacks.Count == 0)
        {
            Say("The fridge is empty! The Star Shop has snacks. (Press Y)", 3.4);
            SetMood("worried");
            await Wait(1.4);
            return;
        }
        if (CompanionLife.Food > 92) { Say("I'm stuffed! Maybe later."); await Wait(1.2); return; }
        int c = await Host.AskAsync("What's for snack?", $"{CompanionArt.Name} is {(CompanionLife.Food < 30 ? "starving" : CompanionLife.Food < 60 ? "peckish" : "not that hungry")}.",
            snacks.Select(s => $"{s.Name}  ×{CompanionLife.Count(s.Id)}").Append("Cancel").ToArray());
        if (c < 0 || c >= snacks.Count) return;
        var snack = snacks[c];
        if (!CompanionLife.TakeSnack(snack.Id)) return;
        await WalkTo(940);
        Say("Nom nom nom…");
        for (int i = 0; i < 4; i++)
        {
            _squash.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.88, TimeSpan.FromSeconds(0.14)) { AutoReverse = true });
            Float("·", "#C8A060", 3, burst: true, size: 26);
            if (i % 2 == 0) CompanionSounds.Effect("crunch");
            await Wait(0.35);
        }
        SetMood("love");
        string favorite = CompanionArt.SpeciesId switch { "cat" => "sushi", "bunny" => "apple", "robot" => "energy", "ghost" => "icecream", "dino" => "pizza", _ => "cake" };
        bool loved = snack.Id == favorite;
        Say(loved ? $"{snack.Name}! My absolute favorite!!" : new[] { "Delicious!", "Mmm, thank you!", "Best snack ever.", "*happy burp* Oops." }[Random.Shared.Next(4)]);
        Float("♥", "#FF5A7A", loved ? 6 : 2);
        Reward(loved ? 4 : 2, food: snack.Food, energy: snack.Energy, fun: snack.Fun + (loved ? 10 : 0));
        Quests("feed");
        await Wait(1.2);
    }

    private async Task TvAsync()
    {
        var game = RecentGame();
        ShowGameArt(_tvScreen, game);
        _look = 0;
        Say(game != null ? new[] { $"Ooh, {game.Name}! Is this a replay?", $"I love watching {game.Name}.", $"{game.Name}! Do the cool part again!" }[Random.Shared.Next(3)] : "Nothing on. Play something and it'll be on TV!", 3.4);
        for (int i = 0; i < 3; i++) { Wiggle(6, 2, 0.25); await Wait(0.8); }
        ShowGameArt(_tvScreen, game, dim: true);
        Reward(2, energy: -2, fun: 8);
    }

    private async Task ReadAsync()
    {
        string[] books =
        {
            "'The Hitchhiker's Guide to the Backlog'", "'One Hundred Years of Loading Screens'", "'Pride and Prejudice and Patch Notes'",
            "'Of Mice and Mousepads'", "'The Old Man and the Speedrun'", "'Crime and Punishment (Hard Mode)'", "'Where the Wild Pixels Are'",
        };
        Say("Reading " + books[Random.Shared.Next(books.Length)] + ".", 3.6);
        Float("❐", "#E8D8B0", 1);
        for (int i = 0; i < 2; i++) { _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, -6, TimeSpan.FromSeconds(0.6)) { AutoReverse = true }); await Wait(1.2); }
        Say(new[] { "What a twist!", "I'll finish it later. Bookmark!", "I'm going to be so smart." }[Random.Shared.Next(3)]);
        Reward(2, energy: -2, fun: 6);
        await Wait(0.8);
    }

    private async Task DressUpAsync()
    {
        await WalkTo(1060);
        var owned = CompanionArt.Accessories.Where(a => CompanionLife.Owns("acc:" + a.Id)).ToList();
        var options = new List<string> { "No accessory" };
        options.AddRange(owned.Select(a => (a.Id == CompanionArt.Accessory ? "★ " : "") + a.Name));
        options.Add("Outfits (earned in Unlockables)");
        options.Add("Cancel");
        int c = await Host.AskAsync("Dress up", "Accessories go with any outfit. Buy more in the Star Shop.", options.ToArray());
        if (c < 0 || c == options.Count - 1) return;
        if (c == options.Count - 2) { Host.Push(new UnlockablesScreen()); return; }
        Labs.Put("companion.accessory", c == 0 ? "" : owned[c - 1].Id);
        _drawn = "";
        _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.6)));
        await Wait(0.3);
        Redraw(force: true);
        Float("✦", "#FFFFFF", 6, burst: true);
        Say(new[] { "How do I look?", "Fashion icon!", "Stunning. I know.", "Runway ready!" }[Random.Shared.Next(4)]);
        Reward(1, fun: 5);
        await Wait(0.6);
        _turn.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private async Task TrophiesAsync()
    {
        int count = Host.Achievements.Known.Values.Sum(l => l.Count);
        Say(count > 0 ? $"{count} achievements! I polish every single one." : "Empty for now. Go win some trophies!", 3.4);
        Float("★", "#F2C14E", 4);
        Reward(1, fun: 3);
        await Wait(1.4);
    }

    private async Task PosterAsync()
    {
        var game = FavoriteGame();
        Say(game != null ? $"{game.Name}. A classic. Like us." : "It's a poster of… nothing yet. Pick a favorite game!", 3.4);
        Reward(1, fun: 3);
        await Wait(1.4);
    }

    private async Task FishAsync()
    {
        Say(new[] { "Hi Bubbles! Hi Bubbles! Hi… Bubbles!", "They're having a fish party.", "Blub blub. (That's fish for 'hello'.)" }[Random.Shared.Next(3)], 3.2);
        Float("○", "#BDE6FF", 4);
        Reward(1, fun: 4);
        await Wait(1.6);
    }

    private async Task DiscoAsync()
    {
        Say("DANCE PARTY!");
        string[] colors = { "#FF5A7A", "#FFD84D", "#7FD8FF", "#C3A6FF", "#7FD8BE" };
        var lights = new List<Ellipse>();
        for (int i = 0; i < 10; i++)
        {
            var spot = new Ellipse { Width = 50, Height = 30, Fill = new SolidColorBrush(PixelArt.C(colors[i % colors.Length])), Opacity = 0.5, IsHitTestVisible = false };
            Canvas.SetLeft(spot, Random.Shared.Next(100, 1180)); Canvas.SetTop(spot, Random.Shared.Next(80, 620));
            _decor.Children.Add(spot);
            lights.Add(spot);
        }
        for (int i = 0; i < 8; i++)
        {
            foreach (var l in lights) { Canvas.SetLeft(l, Random.Shared.Next(100, 1180)); Canvas.SetTop(l, Random.Shared.Next(80, 620)); }
            _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-15, 15, TimeSpan.FromSeconds(0.2)) { AutoReverse = true });
            Hop(1, 14);
            if (i % 2 == 0) Float("♪", colors[i % colors.Length], 1);
            await Wait(0.42);
        }
        foreach (var l in lights) _decor.Children.Remove(l);
        Say("I've still got it.");
        Reward(3, energy: -8, fun: 18);
        await Wait(0.6);
    }

    // ---------------------------------------------------------------- the Star Shop

    private async Task ShopAsync()
    {
        while (!_closed)
        {
            int cat = await Host.AskAsync("★ Star Shop", $"You have {CompanionLife.Stars} star bits. Earn more from the daily letter, Star Catch, petting, and playing your games.",
                "Snacks", "Toys", "Furniture", "Wallpaper", "Accessories", "Put furniture away / back", "Done");
            if (cat is < 0 or 6) break;
            if (cat == 5) { await ArrangeAsync(); continue; }
            if (cat == 4) { await BuyAccessoryAsync(); continue; }
            string kind = cat switch { 0 => "snack", 1 => "toy", 2 => "decor", _ => "wall" };
            var items = CompanionLife.Catalog.Where(i => i.Kind == kind && (kind == "snack" ? i.Price > 0 : true)).ToList();
            int c = await Host.AskAsync(new[] { "Snacks", "Toys", "Furniture", "Wallpaper" }[cat], null, items.Select(i =>
                $"{i.Name}  ·  " + (kind != "snack" && CompanionLife.Owns(i.Id) ? (kind == "wall" && CompanionLife.Wallpaper == i.Id ? "on the wall" : "owned") : i.Price == 0 ? "free" : $"{i.Price} ★") +
                (kind == "snack" ? $"  (in the fridge: {CompanionLife.Count(i.Id)})" : "")).Append("Back").ToArray());
            if (c < 0 || c >= items.Count) continue;
            var item = items[c];
            if (kind == "wall" && CompanionLife.Owns(item.Id)) { CompanionLife.Wallpaper = item.Id; DrawWallpaper(); Say("Fresh walls!"); continue; }
            if (kind != "snack" && CompanionLife.Owns(item.Id)) { Say("We already have that one!"); continue; }
            if (!CompanionLife.Buy(item.Id, item.Price, kind == "snack"))
            {
                UiSound.Error();
                Say($"We need {item.Price - CompanionLife.Stars} more star bits for that.");
                continue;
            }
            CompanionSounds.Effect("chime");
            Float("★", "#FFD84D", 5, burst: true);
            Say(kind switch
            {
                "snack" => $"{item.Name} for the fridge! Yum.",
                "toy" => $"A new {item.Name.ToLowerInvariant()}! It's in the toy box.",
                "wall" => "New wallpaper! Putting it up now.",
                _ => $"A {item.Name.ToLowerInvariant()}! I'll put it in the house.",
            });
            if (kind == "wall") { CompanionLife.Wallpaper = item.Id; DrawWallpaper(); }
            if (kind is "decor") { BuildHouse(); BuildYard(); SetPlace(_place); }
            Reward(1);
        }
    }

    private async Task BuyAccessoryAsync()
    {
        var list = CompanionArt.Accessories.ToList();
        int c = await Host.AskAsync("Accessories", "Bought ones are in the mirror in the house.", list.Select(a =>
            $"{a.Name}  ·  " + (CompanionLife.Owns("acc:" + a.Id) ? "owned" : $"{CompanionLife.AccessoryPrice(a.Id)} ★")).Append("Back").ToArray());
        if (c < 0 || c >= list.Count) return;
        var acc = list[c];
        if (CompanionLife.Owns("acc:" + acc.Id))
        {
            Labs.Put("companion.accessory", acc.Id);
            Redraw(force: true);
            Say("Wearing it now!");
            return;
        }
        if (!CompanionLife.Buy("acc:" + acc.Id, CompanionLife.AccessoryPrice(acc.Id), snack: false))
        {
            UiSound.Error();
            Say($"We need {CompanionLife.AccessoryPrice(acc.Id) - CompanionLife.Stars} more star bits for that.");
            return;
        }
        Labs.Put("companion.accessory", acc.Id);
        _drawn = "";
        Redraw(force: true);
        CompanionSounds.Effect("chime");
        Float("✦", "#FFFFFF", 8, burst: true);
        Say($"The {acc.Name.ToLowerInvariant()}! How do I look?");
        Reward(1, fun: 6);
    }

    private async Task ArrangeAsync()
    {
        var decor = CompanionLife.Catalog.Where(i => i.Kind == "decor" && CompanionLife.Owns(i.Id)).ToList();
        if (decor.Count == 0) { Say("We don't have any furniture yet!"); return; }
        int c = await Host.AskAsync("Furniture", "Choose something to put away or bring back.", decor.Select(d => $"{d.Name}  ·  {(CompanionLife.Shown(d.Id) ? "in the house" : "put away")}").Append("Back").ToArray());
        if (c < 0 || c >= decor.Count) return;
        CompanionLife.SetShown(decor[c].Id, !CompanionLife.Shown(decor[c].Id));
        BuildHouse();
        BuildYard();
        SetPlace(_place);
    }

    // ---------------------------------------------------------------- Star Catch (the arcade)

    private bool _gameOn;
    private readonly List<(TextBlock Star, double Speed, bool Bad)> _falling = new();
    private int _score, _caught;
    private DateTime _gameEnds, _lastFrame;
    private double _spawnIn;
    private TextBlock? _scoreText, _timeText;
    private double _targetX;

    private async Task StarCatchAsync()
    {
        if (CompanionLife.Energy < 12) { Say("Too sleepy for games… zzz… huh?"); await Wait(1.4); return; }
        Say("Star Catch! Move me with left and right. Catch the stars, dodge the rain clouds!", 3.4);
        await Wait(1.2);
        _overlay.Children.Clear();
        var sky = new Rectangle { Width = W, Height = H, Fill = new LinearGradientBrush(PixelArt.C("#0B1030"), PixelArt.C("#3A2A6A"), 90), Opacity = 0.92 };
        _overlay.Children.Add(sky);
        _scoreText = new TextBlock { FontSize = 34, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(PixelArt.C("#FFD84D")) };
        _timeText = new TextBlock { FontSize = 26, Foreground = Brushes.White };
        Canvas.SetLeft(_scoreText, 560); Canvas.SetTop(_scoreText, 40);
        Canvas.SetLeft(_timeText, 590); Canvas.SetTop(_timeText, 90);
        _overlay.Children.Add(_scoreText);
        _overlay.Children.Add(_timeText);
        _overlay.Visibility = Visibility.Visible;
        _ring.Visibility = _label.Visibility = Visibility.Collapsed;
        _scene.Children.Remove(_actor);
        _scene.Children.Add(_actor); // above the arcade's sky
        _score = 0; _caught = 0;
        _falling.Clear();
        _targetX = _pos.X;
        _gameEnds = DateTime.Now.AddSeconds(30);
        _lastFrame = DateTime.Now;
        _spawnIn = 0.3;
        _gameOn = true;
        _hints.Template = "◀ ▶  Move        {Back}  Stop";
        CompositionTarget.Rendering += GameFrame;
        while (_gameOn && !_closed) await Task.Delay(100);
    }

    private void GameInput(NavAction action)
    {
        if (action == NavAction.Back) { EndGame(quiet: false); return; }
        if (action is NavAction.Left or NavAction.LB or NavAction.LT) _targetX = Math.Clamp(_targetX - 110, 70, W - 70);
        if (action is NavAction.Right or NavAction.RB or NavAction.RT) _targetX = Math.Clamp(_targetX + 110, 70, W - 70);
        _look = action is NavAction.Left ? -1 : action is NavAction.Right ? 1 : _look;
        Redraw();
    }

    private void GameFrame(object? sender, EventArgs e)
    {
        if (!_gameOn) return;
        var now = DateTime.Now;
        double dt = Math.Min(0.05, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;
        double left = (_gameEnds - now).TotalSeconds;
        if (left <= 0) { EndGame(quiet: false); return; }
        // The companion slides toward where you're steering.
        _pos.BeginAnimation(TranslateTransform.XProperty, null);
        _pos.X += (_targetX - _pos.X) * Math.Min(1, dt * 12);
        // New stars (and now and then a rain cloud), faster as time goes on.
        double pace = 1 + (30 - left) / 22;
        _spawnIn -= dt;
        if (_spawnIn <= 0)
        {
            _spawnIn = Math.Max(0.28, 0.75 / pace);
            bool bad = Random.Shared.Next(6) == 0;
            var t = new TextBlock { Text = bad ? "☁" : "★", FontSize = bad ? 44 : 36, Foreground = new SolidColorBrush(PixelArt.C(bad ? "#8A93A8" : "#FFD84D")) };
            Canvas.SetLeft(t, Random.Shared.Next(60, (int)W - 100));
            Canvas.SetTop(t, 120);
            _overlay.Children.Add(t);
            _falling.Add((t, (180 + Random.Shared.Next(120)) * pace, bad));
        }
        double floorY = Floor;
        for (int i = _falling.Count - 1; i >= 0; i--)
        {
            var (star, speed, bad) = _falling[i];
            double y = Canvas.GetTop(star) + speed * dt;
            Canvas.SetTop(star, y);
            double x = Canvas.GetLeft(star) + 18;
            if (y > floorY - 120 && y < floorY - 40 && Math.Abs(x - _pos.X) < 62)
            {
                _overlay.Children.Remove(star);
                _falling.RemoveAt(i);
                if (bad)
                {
                    _score = Math.Max(0, _score - 2);
                    Float("💧", "#7FB8FF", 2, burst: true);
                    _hop.BeginAnimation(TranslateTransform.YProperty, null);
                    Wiggle(10, 2, 0.06);
                    Host.Rumble(120);
                }
                else
                {
                    _score++; _caught++;
                    Float("✦", "#FFD84D", 2, burst: true);
                    Hop(1, 16);
                    if (_caught % 5 == 0) CompanionSounds.Effect("chime"); else CompanionSounds.Effect("pop");
                }
                continue;
            }
            if (y > H) { _overlay.Children.Remove(star); _falling.RemoveAt(i); }
        }
        if (_scoreText != null) _scoreText.Text = "★ " + _score;
        if (_timeText != null) _timeText.Text = $"{Math.Ceiling(left)} s";
    }

    private void EndGame(bool quiet)
    {
        if (!_gameOn) return;
        _gameOn = false;
        CompositionTarget.Rendering -= GameFrame;
        // The companion back under the highlight and the letter layer.
        _scene.Children.Remove(_actor);
        _scene.Children.Insert(_scene.Children.IndexOf(_ring), _actor);
        foreach (var (star, _, _) in _falling) _overlay.Children.Remove(star);
        _falling.Clear();
        _overlay.Visibility = Visibility.Collapsed;
        _overlay.Children.Clear();
        if (quiet) return;
        int earned = Math.Min(25, (_score + 1) / 2);
        CompanionLife.Stars += earned;
        bool best = _score > CompanionLife.BestCatch;
        if (best) CompanionLife.BestCatch = _score;
        Reward(3 + _score / 5, energy: -8, fun: 20);
        Quests("starcatch");
        StoryEvent("starcatch", "", _score);
        Hop(best ? 3 : 1, 30);
        Float("★", "#FFD84D", Math.Min(10, 2 + _score / 3), burst: true);
        Say((best ? $"New best: {_score}! " : $"{_score} stars! ") + $"+{earned} star bits." + (_score >= 20 ? " You're a natural!" : _score < 5 ? " Practice makes perfect!" : ""), 4.2);
        RefreshHud();
        ShowFocus();
    }
}
