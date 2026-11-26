using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Experimental "Companion on the update screens": your companion rides the progress bar while an update downloads
/// (carrying the new version along), reacts to how it's going, cheers when it's done, shrugs at a cancel and looks
/// worried if something fails. <see cref="UpdatePal"/> is the one inside SquareDeck (the download card);
/// <see cref="RenderFrames"/> draws its faces as pictures for the full-screen install window, which runs on its own.
/// </summary>
public sealed class UpdatePal
{
    public static bool Enabled => Labs.On("updatecompanion");

    private const double Size = 60;
    private readonly Canvas _lane = new() { Height = Size + 6, ClipToBounds = false, Margin = new Thickness(0, 0, 0, 2) };
    private readonly Canvas _sprite = new() { Width = Size, Height = Size };
    private readonly TranslateTransform _bob = new();
    private readonly TextBlock _prop = new() { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22, Visibility = Visibility.Collapsed };
    private readonly Border _bubble = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 6, 12, 7), Opacity = 0 };
    private readonly TextBlock _say = new() { FontSize = 14, TextWrapping = TextWrapping.NoWrap };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DateTime _start = DateTime.UtcNow;
    private string _mood = "look";   // look (connecting), walk, zoom, cheer, bored, worried
    private double _fraction, _shown;
    private DateTime _hopAt, _sayUntil;
    private string _lastFrame = "";
    private int _milestone = 1;

    /// <summary>The lane the companion walks along: put it right above the progress bar.</summary>
    public FrameworkElement View => _lane;

    public UpdatePal()
    {
        _sprite.RenderTransform = _bob;
        _prop.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _bubble.SetResourceReference(Border.BackgroundProperty, "FgBrush");
        _say.SetResourceReference(TextBlock.ForegroundProperty, "BgBrush");
        _bubble.Child = _say;
        _lane.Children.Add(_bubble);
        _lane.Children.Add(_prop);
        _lane.Children.Add(_sprite);
        _timer.Tick += (_, _) => Tick();
        _lane.Loaded += (_, _) => _timer.Start();
        _lane.Unloaded += (_, _) => _timer.Stop();
        Say(Pick($"{CompanionArt.Name} is knocking on GitHub's door…", "Fetching the new version!", "Ooh, an update!"), 3);
    }

    private static string Pick(params string[] lines) => lines[Random.Shared.Next(lines.Length)];

    public void Say(string text, double seconds = 2.6)
    {
        _say.Text = text;
        _sayUntil = DateTime.UtcNow.AddSeconds(seconds);
        _bubble.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.18)));
    }

    public void Hop() => _hopAt = DateTime.UtcNow;

    /// <summary>The download moved on. <paramref name="bytesPerSecond"/> picks the walk or the dash.</summary>
    public void Progress(double fraction, double bytesPerSecond)
    {
        if (_mood is "cheer" or "bored" or "worried") return;
        _fraction = Math.Clamp(fraction, 0, 1);
        string mood = bytesPerSecond > 8 * 1024 * 1024 ? "zoom" : "walk";
        if (_mood == "look") { _prop.Text = "\uE7B8"; _prop.Visibility = Visibility.Visible; Say(Pick("Got it! Carrying it home.", "Heave-ho!", "Here we go!")); }
        if (mood == "zoom" && _mood != "zoom") Say(Pick("Zoom! Fast internet!", "Wheee, this is quick!", "Hold on tight!"), 2);
        _mood = mood;
        while (_milestone < 4 && _fraction >= _milestone * 0.25)
        {
            Hop();
            Say(_milestone switch { 1 => Pick("A quarter of the way!", "25%, nice."), 2 => Pick("Halfway there!", "Half done, keep going!"), _ => Pick("Almost there!", "Nearly home!") }, 2);
            _milestone++;
        }
    }

    public void Done()
    {
        _fraction = 1;
        _mood = "cheer";
        _prop.Text = "\uE734"; // a star
        Say(Pick("Got it! Installing now!", "Downloaded! See you in the new version!", "Ta-da!"), 4);
        Hop();
    }

    public void Cancelled()
    {
        _mood = "bored";
        _prop.Visibility = Visibility.Collapsed;
        Say(Pick("Okay, maybe next time.", "Aww. Later, then."), 3);
    }

    public void Failed()
    {
        _mood = "worried";
        _prop.Text = "\uE783"; // a warning sign
        _prop.Visibility = Visibility.Visible;
        Say(Pick("Uh-oh… that didn't work.", "Oops, the download tripped.", "Hmm, something went wrong."), 4);
    }

    private void Tick()
    {
        double t = (DateTime.UtcNow - _start).TotalSeconds;
        double width = _lane.ActualWidth;
        if (width <= 0) return;
        _shown += (_fraction - _shown) * 0.12;
        double x = Math.Clamp(_shown * width - Size / 2, -6, width - Size + 6);
        double y = 0;
        bool blink = (int)(t * 10) % 34 == 0;
        string frame;
        switch (_mood)
        {
            case "look":
                // Looking left and right, waiting for the first bytes.
                int look = (int)(t * 1.2) % 4 switch { 0 => -1, 2 => 1, _ => 0 };
                frame = (blink ? "b" : "h") + look;
                y = Math.Sin(t * 2) * 1.5;
                break;
            case "walk":
                frame = (blink ? "b" : "h") + "1";
                y = -Math.Abs(Math.Sin(t * 7)) * 5;
                break;
            case "zoom":
                frame = "c1";
                y = -Math.Abs(Math.Sin(t * 12)) * 7;
                break;
            case "cheer":
                frame = (int)(t * 4) % 2 == 0 ? "c0" : "cw";
                y = -Math.Abs(Math.Sin(t * 5)) * 22;
                break;
            case "bored":
                frame = "bored";
                break;
            default: // worried
                frame = "worried";
                x += Math.Sin(t * 30) * 2;
                break;
        }
        double hop = (DateTime.UtcNow - _hopAt).TotalSeconds;
        if (hop < 0.45) y -= Math.Sin(hop / 0.45 * Math.PI) * 18;
        if (frame != _lastFrame) { Draw(frame); _lastFrame = frame; }
        Canvas.SetLeft(_sprite, x);
        Canvas.SetTop(_sprite, 6);
        _bob.Y = y;
        Canvas.SetLeft(_prop, x + Size - 8);
        Canvas.SetTop(_prop, 6 + Size * 0.35 + y);
        // The bubble on whichever side has room.
        double bw = _bubble.ActualWidth;
        Canvas.SetLeft(_bubble, x + Size / 2 < width / 2 ? x + Size + 22 : Math.Max(0, x - bw - 12));
        Canvas.SetTop(_bubble, 8);
        if (_sayUntil != default && DateTime.UtcNow > _sayUntil)
        {
            _sayUntil = default;
            _bubble.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.3)));
        }
    }

    private void Draw(string frame)
    {
        // h/b = happy/blinking, c = cheer, then the look (-1, 0, 1) or w for a wave.
        string mood = frame switch { "bored" => "bored", "worried" => "worried", _ when frame[0] == 'c' => "cheer", _ => "happy" };
        bool blink = frame[0] == 'b' && frame != "bored";
        bool wave = frame.EndsWith('w');
        int look = frame.Length == 2 && frame[1] == '1' ? 1 : frame.EndsWith("-1") ? -1 : 0;
        CompanionArt.Draw(_sprite, CompanionArt.SpeciesId, CompanionArt.ColorHex, mood, blink, Labs.Outfit, look, wave, Size / 12);
    }

    /// <summary>
    /// The faces the full-screen install window uses, drawn as pictures into <paramref name="folder"/>: happy, blink,
    /// look and lookblink (walking right), cheer, cheerwave, wave, worried, sleepy and bored. False if it couldn't.
    /// </summary>
    public static bool RenderFrames(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var frames = new (string Name, string Mood, bool Blink, int Look, bool Wave)[]
            {
                ("happy", "happy", false, 0, false), ("blink", "happy", true, 0, false), ("look", "happy", false, 1, false),
                ("lookblink", "happy", true, 1, false), ("cheer", "cheer", false, 0, false), ("cheerwave", "cheer", false, 0, true),
                ("wave", "happy", false, 0, true), ("worried", "worried", false, 0, false), ("sleepy", "sleepy", false, 0, false),
                ("bored", "bored", false, 0, false),
            };
            const double px = 144; // drawn big, so it stays sharp when shown at any size
            foreach (var f in frames)
            {
                var canvas = new Canvas { Width = px, Height = px };
                CompanionArt.Draw(canvas, CompanionArt.SpeciesId, CompanionArt.ColorHex, f.Mood, f.Blink, Labs.Outfit, f.Look, f.Wave, px / 12);
                canvas.Measure(new Size(px, px));
                canvas.Arrange(new Rect(0, 0, px, px));
                canvas.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)px, (int)px, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(canvas);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, f.Name + ".png"));
                png.Save(file);
            }
            return true;
        }
        catch (Exception) { return false; }
    }
}
