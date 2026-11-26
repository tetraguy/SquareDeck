using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Movie night: the companion watches the trailer from a red cinema seat beside the buttons and keeps busy, moving
/// from one bit to the next every few seconds: munching popcorn from its striped bucket, tossing pieces up and
/// catching them (and now and then missing one: it bounces off its head), putting on 3D glasses and leaning back as
/// things fly at it, slurping a soda, and nodding along with a film reel spinning beside it. A new trailer starts with
/// a clapperboard "Action!"; skipping makes it jump and spill popcorn everywhere; pausing makes it bored, then drowsy
/// (Zzz), and playing again wakes it with a "Shh!". Little sounds go with it (crunch, clap, slurp) when interface
/// sounds are on. Shown when the companion is on and motion isn't reduced. The video is a web view that nothing can
/// be drawn over, so the companion sits under it, by the buttons.
/// </summary>
public sealed class TrailerPal
{
    public static bool Enabled => Labs.On("companion") && !Theme.ReduceMotion;

    private const double Size = 66, Cell = Size / 12, SpriteX = 31, SpriteY = 26;
    private static readonly string[] Scenes = { "munch", "toss", "glasses", "soda", "munch", "reel", "toss" };

    private readonly Canvas _root = new() { Width = 160, Height = 104, ClipToBounds = false, IsHitTestVisible = false };
    private readonly Canvas _fx = new() { IsHitTestVisible = false };
    private readonly Canvas _sprite = new() { Width = Size, Height = Size, RenderTransformOrigin = new Point(0.5, 1) };
    private readonly TranslateTransform _bob = new();
    private readonly RotateTransform _lean = new();
    private readonly Canvas _bucket = new() { Width = 7 * 4.6, Height = 7 * 4.6 };
    private readonly TextBlock _prop = Emoji("", 26);
    private readonly TextBlock _say = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Opacity = 0, Width = 118, TextWrapping = TextWrapping.Wrap };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly List<(FrameworkElement E, double X, double Y, double VX, double VY, DateTime Born, double Life)> _bits = new();
    private readonly DateTime _start = DateTime.UtcNow;
    private DateTime _sceneAt = DateTime.UtcNow, _eventAt, _sayUntil, _pausedAt;
    private string _scene = "munch", _event = "", _lastFrame = "";
    private int _sceneIndex, _lastBeat = -1, _saidBeat = -1;
    private bool _paused;

    public FrameworkElement View => _root;

    public TrailerPal()
    {
        // The seat: a red velvet back and an armrest, behind the companion; a strip of floor lights under it.
        var seat = new Border
        {
            Width = Size + 18, Height = Size * 0.78, CornerRadius = new CornerRadius(16, 16, 6, 6),
            Background = new LinearGradientBrush(Color.FromRgb(0xB0, 0x2A, 0x3A), Color.FromRgb(0x6E, 0x14, 0x22), 90),
            Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Opacity = 0.4 }
        };
        Canvas.SetLeft(seat, 22); Canvas.SetTop(seat, SpriteY - 4);
        var arm = new Border { Width = Size + 30, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x10, 0x18)) };
        Canvas.SetLeft(arm, 16); Canvas.SetTop(arm, SpriteY - 8 + Size * 0.78);
        for (int i = 0; i < 6; i++)
        {
            var light = new Ellipse { Width = 4, Height = 4, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x6B)), Opacity = 0.7 };
            Canvas.SetLeft(light, 22 + i * 16); Canvas.SetTop(light, SpriteY + Size * 0.78 + 8);
            _root.Children.Add(light);
        }
        _root.Children.Add(seat);
        _sprite.RenderTransform = new TransformGroup { Children = { _lean, _bob } };
        Canvas.SetLeft(_sprite, SpriteX); Canvas.SetTop(_sprite, SpriteY);
        _root.Children.Add(_sprite);
        _root.Children.Add(arm);
        DrawBucket();
        _root.Children.Add(_bucket);
        _root.Children.Add(_prop);
        _root.Children.Add(_fx);
        _say.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
        Canvas.SetLeft(_say, 108); Canvas.SetTop(_say, 2);
        _root.Children.Add(_say);
        _timer.Tick += (_, _) => Tick();
        _root.Loaded += (_, _) => _timer.Start();
        _root.Unloaded += (_, _) => _timer.Stop();
        Event("clap");
        Say(Pick("Lights down… Action!", "Popcorn's ready!", "Shh, it's starting."), 2.5);
    }

    private static TextBlock Emoji(string text, double size) => new()
    {
        Text = text, FontSize = size, FontFamily = new FontFamily("Segoe UI Emoji"), RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new RotateTransform(), IsHitTestVisible = false
    };

    private static string Pick(params string[] lines) => lines[Random.Shared.Next(lines.Length)];

    /// <summary>The striped bucket with popcorn heaped on top, in the companion's pixel style.</summary>
    private void DrawBucket()
    {
        string[] art = { ".YWYWY.", "YWYWYWY", "RWRWRWR", "RWRWRWR", ".RWRWR.", ".RWRWR.", "..RWR.." };
        var colors = new Dictionary<char, Brush>
        {
            ['Y'] = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x5C)), ['W'] = new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xE4)),
            ['R'] = new SolidColorBrush(Color.FromRgb(0xE0, 0x3A, 0x3E)),
        };
        double c = 4.6;
        for (int y = 0; y < art.Length; y++)
            for (int x = 0; x < art[y].Length; x++)
                if (colors.TryGetValue(art[y][x], out var brush))
                {
                    var r = new Rectangle { Width = c, Height = c, Fill = brush };
                    Canvas.SetLeft(r, x * c); Canvas.SetTop(r, y * c);
                    _bucket.Children.Add(r);
                }
    }

    public void Say(string text, double seconds = 2)
    {
        _say.Text = text;
        _say.Opacity = 1;
        _sayUntil = DateTime.UtcNow.AddSeconds(seconds);
    }

    private void Event(string name)
    {
        _event = name;
        _eventAt = DateTime.UtcNow;
        switch (name)
        {
            case "clap":
                CompanionSounds.Effect("clap", 450);
                break;
            case "spill":
                // Popcorn everywhere.
                for (int i = 0; i < 14; i++)
                    Bit(Kernel(), 92, 50, -60 + Random.Shared.NextDouble() * 120, -170 - Random.Shared.NextDouble() * 90, 1.1);
                CompanionSounds.Effect("pop");
                break;
        }
    }

    private static FrameworkElement Kernel() => new Rectangle
    {
        Width = 6, Height = 6, RadiusX = 2, RadiusY = 2,
        Fill = new SolidColorBrush(Random.Shared.Next(3) == 0 ? Color.FromRgb(0xFF, 0xD8, 0x5C) : Color.FromRgb(0xFF, 0xF1, 0xB8))
    };

    /// <summary>A little flying piece (popcorn, a bubble, a Z) that falls (or rises) and fades.</summary>
    private void Bit(FrameworkElement e, double x, double y, double vx, double vy, double life)
    {
        _fx.Children.Add(e);
        Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
        _bits.Add((e, x, y, vx, vy, DateTime.UtcNow, life));
    }

    /// <summary>The trailer was paused or played.</summary>
    public void Paused(bool paused)
    {
        if ((DateTime.UtcNow - _start).TotalSeconds < 2.5) return; // still loading, not a real pause
        _paused = paused;
        if (paused) { _pausedAt = DateTime.UtcNow; Say(Pick("Intermission?", "Hey, I was watching that!", "Pause for snacks.")); }
        else Say(Pick("Shh! It's back on!", "Ooh, here we go.", "Okay, okay, I'm watching."), 1.6);
    }

    /// <summary>Another trailer started: the clapperboard.</summary>
    public void NewTrailer()
    {
        Event("clap");
        Say(Pick("Scene two… Action!", "Another one!", "Ooh, this one looks good.", "Roll the next reel!"));
    }

    /// <summary>Skipped ahead or back: a jump, and popcorn everywhere.</summary>
    public void Skipped()
    {
        if (_paused) return;
        Event("spill");
        Say(Pick("Eek!", "Whoa, my popcorn!", "Spoilers!", "Wait, what happened?"), 1.4);
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        double t = (now - _start).TotalSeconds;
        double st = (now - _sceneAt).TotalSeconds, et = (now - _eventAt).TotalSeconds;
        string mood = "happy";
        bool blink = (int)(t * 10) % 37 == 0, wave = false;
        int look = 0;
        double bob = 0, lean = 0;
        double bucketX = SpriteX + 44, bucketY = SpriteY + 30;
        string prop = "";
        double propX = 0, propY = 0, propSpin = 0, propSize = 26;

        // The next bit every six and a half seconds.
        if (!_paused && st > 6.5)
        {
            _sceneIndex = (_sceneIndex + 1) % Scenes.Length;
            _scene = Scenes[_sceneIndex];
            _sceneAt = now;
            st = 0;
            _lastBeat = -1;
            switch (_scene)
            {
                case "glasses": Say(Pick("Whoa, 3D!", "It's coming right at me!", "*puts on 3D glasses*"), 2); break;
                case "soda": Say(Pick("*slurp*", "Gotta stay hydrated.", "Mmm, fizzy."), 1.6); break;
                case "reel": Say(Pick("Roll film!", "Classic cinema.", "I love this part."), 1.6); break;
            }
        }

        if (_event == "clap" && et < 1.3)
        {
            // The clapperboard: held up, it snaps shut ("Action!"), then goes away.
            prop = "🎬"; propX = SpriteX + 46; propY = SpriteY - 26 - Math.Max(0, 0.3 - et) * 40;
            propSpin = et < 0.45 ? -25 + et * 55 : 0;
            mood = "cheer"; wave = true;
            bob = et > 0.45 && et < 0.7 ? -Math.Sin((et - 0.45) / 0.25 * Math.PI) * 8 : 0;
        }
        else if (_event == "spill" && et < 1.1)
        {
            mood = et < 0.4 ? "worried" : "cheer";
            bob = et < 0.5 ? -Math.Sin(et / 0.5 * Math.PI) * 16 : 0;
            bucketY -= et < 0.3 ? 10 * Math.Sin(et / 0.3 * Math.PI) : 0;
        }
        else if (_paused)
        {
            double pt = (now - _pausedAt).TotalSeconds;
            if (pt > 6)
            {
                // Dozing off: sleepy, slumped, Zs floating up.
                mood = "sleepy";
                lean = -6;
                bob = Math.Sin(t * 1.4) * 1.5;
                int beat = (int)(t / 1.1);
                if (beat != _lastBeat) { _lastBeat = beat; Bit(new TextBlock { Text = "z", FontSize = 11 + Random.Shared.Next(5), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xC3, 0xA6, 0xFF)) }, SpriteX + 50, SpriteY + 4, 12, -28, 1.6); }
            }
            else { mood = "bored"; bob = Math.Sin(t * 1.5); }
        }
        else
        {
            switch (_scene)
            {
                case "toss":
                {
                    // Toss one up and catch it (one in four bounces off its head).
                    double loop = st % 2.2;
                    int beat = (int)(st / 2.2);
                    bool miss = (beat * 7 + 3) % 4 == 0;
                    if (beat != _lastBeat && loop < 0.1)
                    {
                        _lastBeat = beat;
                        var k = Kernel();
                        Bit(k, bucketX + 12, bucketY, miss ? -10 : -26, -220, miss ? 1.2 : 0.95);
                    }
                    look = 0;
                    if (loop > 0.6 && loop < 1.0) { mood = miss ? "worried" : "cheer"; bob = -Math.Sin((loop - 0.6) / 0.4 * Math.PI) * (miss ? 4 : 12); }
                    if (loop > 1.0 && loop < 1.5 && !miss) mood = (int)(loop * 12) % 2 == 0 ? "cheer" : "happy"; // munch
                    if (loop is > 0.95 and < 1.05 && beat != _saidBeat) { _saidBeat = beat; if (miss) Say(Pick("Oops!", "Bonk!", "Missed!"), 1); else if (Random.Shared.Next(3) == 0) Say(Pick("Caught it!", "Nailed it!", "Yum!"), 1); }
                    break;
                }
                case "glasses":
                {
                    // 3D glasses on, leaning back as things fly out of the screen, eyes darting.
                    prop = "🕶"; propSize = 22; propX = SpriteX + 2.2 * Cell; propY = SpriteY + 3.6 * Cell;
                    mood = st % 2 < 1 ? "cheer" : "happy";
                    lean = -8 + Math.Sin(st * 2) * 3;
                    look = (int)(st * 1.5) % 3 - 1;
                    bob = Math.Sin(st * 3) * 1.5;
                    break;
                }
                case "soda":
                {
                    // A soda: sips with bubbles rising, and a little fizz.
                    prop = "🥤"; propSize = 24; propX = SpriteX + 46; propY = SpriteY + 18;
                    double loop = st % 2.4;
                    int beat = (int)(st / 2.4);
                    if (loop < 1.0)
                    {
                        mood = "happy"; wave = true;
                        propX -= 10; propY -= 10;
                        if (beat != _lastBeat) { _lastBeat = beat; CompanionSounds.Effect("slurp"); }
                        if ((int)(loop * 10) % 3 == 0) Bit(new Ellipse { Width = 4, Height = 4, Stroke = new SolidColorBrush(Color.FromRgb(0x9F, 0xD8, 0xFF)), StrokeThickness = 1 }, propX + 10, propY, Random.Shared.Next(-6, 6), -40, 0.7);
                    }
                    else mood = blink ? "happy" : "happy";
                    break;
                }
                case "reel":
                {
                    // A film reel spinning beside it while it nods along.
                    prop = "🎞"; propSize = 24; propX = SpriteX - 30; propY = SpriteY + 22; propSpin = st * 180;
                    bob = -Math.Abs(Math.Sin(st * 4)) * 3;
                    mood = (int)(st * 2) % 4 == 0 ? "cheer" : "happy";
                    if ((int)(st * 3) % 5 == 0 && st % 0.33 < 0.04) Bit(new TextBlock { Text = "✦", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE2, 0x7A)) }, propX + 12, propY, Random.Shared.Next(-20, 20), -30, 0.8);
                    break;
                }
                default: // munch
                {
                    // Reach in, the piece to the mouth, munch munch.
                    double loop = st % 3.2;
                    int beat = (int)(st / 3.2);
                    if (loop > 1.4 && loop < 1.75) bucketY += 2;
                    else if (loop >= 1.75 && loop < 2.3)
                    {
                        wave = true;
                        if (beat != _lastBeat)
                        {
                            _lastBeat = beat;
                            Bit(Kernel(), bucketX + 12, bucketY, -60, -110, 0.5);
                        }
                    }
                    else if (loop >= 2.3)
                    {
                        mood = (int)((loop - 2.3) * 9) % 2 == 0 ? "cheer" : "happy";
                        bob = mood == "cheer" ? -1.5 : 0;
                        if (loop < 2.35 && Random.Shared.Next(2) == 0) CompanionSounds.Effect("crunch");
                    }
                    break;
                }
            }
        }

        // Flying bits: popcorn, bubbles, Zs.
        for (int i = _bits.Count - 1; i >= 0; i--)
        {
            var b = _bits[i];
            double age = (now - b.Born).TotalSeconds;
            if (age > b.Life) { _fx.Children.Remove(b.E); _bits.RemoveAt(i); continue; }
            bool falls = b.E is Rectangle;
            double x = b.X + b.VX * age, y = b.Y + b.VY * age + (falls ? 420 * age * age : 0);
            Canvas.SetLeft(b.E, x); Canvas.SetTop(b.E, y);
            b.E.Opacity = Math.Min(1, (b.Life - age) / 0.3);
        }

        string frame = $"{mood}|{blink}|{wave}|{look}";
        if (frame != _lastFrame)
        {
            CompanionArt.Draw(_sprite, CompanionArt.SpeciesId, CompanionArt.ColorHex, mood, blink && mood != "cheer", Labs.Outfit, look, wave, Cell);
            _lastFrame = frame;
        }
        _bob.Y = bob;
        _lean.Angle = lean;
        Canvas.SetLeft(_bucket, bucketX);
        Canvas.SetTop(_bucket, bucketY + bob * 0.6);
        _prop.Text = prop;
        _prop.FontSize = propSize;
        if (prop.Length > 0)
        {
            Canvas.SetLeft(_prop, propX);
            Canvas.SetTop(_prop, propY + bob * 0.6);
            ((RotateTransform)_prop.RenderTransform).Angle = propSpin;
        }
        if (_sayUntil != default && now > _sayUntil) { _sayUntil = default; _say.Opacity = 0; }
    }
}
