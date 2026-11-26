using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The countdown before a game starts, full screen in the Switch 2 manner: the page color, the game's cover large in
/// the middle with the Switch 2 rim (blue into purple into pink, or the UI style's own ring) running around it and
/// draining as the seconds go, the logo (or name) in quiet type under it, and each second rising in large regular
/// numbers, ending on "Start!" with a flash of the rim. Start now and Cancel stay under it; B cancels.
/// Settings > Games > Countdown look picks this or the card (<see cref="CountdownScreen"/>).
/// </summary>
public sealed class Switch2CountdownScreen : Screen
{
    private const double CoverSize = 300, RimGap = 12, RimThickness = 7;

    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _cts = new();
    private readonly Game _game;
    private readonly int _total;
    private readonly bool _preview;
    private int _seconds;
    private bool _go, _finishing;

    private readonly Rectangle _rimTrack, _rim, _flash;
    private readonly RotateTransform _rimTurn = new(0, 0.5, 0.5);
    private readonly Border _cover;
    private readonly ScaleTransform _coverPop = new(1, 1);
    private readonly TextBlock _name, _label;
    private readonly Image _logo = new() { MaxWidth = 460, MaxHeight = 110, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Grid _numberHost = new() { Height = 150, ClipToBounds = true };
    private readonly Button _now;

    public override bool IsDialog => true;

    /// <summary>True to start the game, false if it was cancelled.</summary>
    public Task<bool> Result => _done.Task;

    /// <param name="preview">From Settings: plays it through and closes, nothing starts.</param>
    public Switch2CountdownScreen(Game? game, int seconds = 3, bool preview = false)
    {
        _game = game ?? new Game { Name = "Your game" };
        _total = _seconds = Math.Max(1, seconds);
        _preview = preview;
        Switch2Settings.ApplyColors(this);
        var p = GameCard.ColorsOf(_game);

        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");

        // A soft wash of the highlight color behind the cover.
        var glow = new Ellipse
        {
            Width = 1000, Height = 1000, IsHitTestVisible = false, Opacity = 0.35,
            Fill = new RadialGradientBrush(Color.FromArgb(0x60, Switch2Settings.Blue.R, Switch2Settings.Blue.G, Switch2Settings.Blue.B), Color.FromArgb(0, Switch2Settings.Blue.R, Switch2Settings.Blue.G, Switch2Settings.Blue.B)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -220, 0, 0)
        };
        root.Children.Add(glow);

        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        // ---- the cover, with the rim around it that drains over the countdown
        double box = CoverSize + (RimGap + RimThickness) * 2;
        var art = new Grid { Width = box, Height = box, HorizontalAlignment = HorizontalAlignment.Center };
        double radius = GameCard.Round(30) + RimGap;
        var rimBrush = Switch2Settings.RimBrush.Clone();
        rimBrush.RelativeTransform = _rimTurn;
        double inner = box - RimThickness;
        double perimeter = 2 * (inner + inner) - (8 - 2 * Math.PI) * radius;
        double around = perimeter / RimThickness;
        _rimTrack = new Rectangle { RadiusX = radius, RadiusY = radius, StrokeThickness = RimThickness, Stroke = new SolidColorBrush(Switch2Settings.Line) };
        _rim = new Rectangle
        {
            RadiusX = radius, RadiusY = radius, StrokeThickness = RimThickness, Stroke = rimBrush, StrokeDashCap = PenLineCap.Round,
            StrokeDashArray = new DoubleCollection { around, around * 2 }, Tag = around,
            Effect = Perf.Active ? null : new DropShadowEffect { Color = Switch2Settings.Blue, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.7 }
        };
        _flash = new Rectangle { RadiusX = radius, RadiusY = radius, StrokeThickness = 4, Stroke = rimBrush, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        _cover = GameCard.Cover(_game, CoverSize);
        _cover.CornerRadius = new CornerRadius(GameCard.Round(30));
        _cover.HorizontalAlignment = HorizontalAlignment.Center;
        _cover.VerticalAlignment = VerticalAlignment.Center;
        _cover.RenderTransformOrigin = new Point(0.5, 0.5);
        _cover.RenderTransform = _coverPop;
        art.Children.Add(_rimTrack);
        art.Children.Add(_rim);
        art.Children.Add(_flash);
        art.Children.Add(_cover);
        column.Children.Add(art);

        // ---- the logo, or the name until it arrives
        var title = new Grid { Height = 110, Margin = new Thickness(0, 26, 0, 0) };
        _name = Ui.Text(_game.Name, 32, "FgBrush");
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _name.VerticalAlignment = VerticalAlignment.Center;
        _name.TextTrimming = TextTrimming.CharacterEllipsis;
        _name.MaxWidth = 760;
        title.Children.Add(_name);
        title.Children.Add(_logo);
        column.Children.Add(title);

        _label = Ui.Text(preview ? "Starting in  ·  preview" : "Starting in", 18, "SubtleBrush");
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.Margin = new Thickness(0, 10, 0, 0);
        column.Children.Add(_label);
        column.Children.Add(_numberHost);

        _now = GameCard.Pill("Start now", "\uE768", true, p, () => { _go = !_preview; Host.Pop(this); });
        var buttons = GameCard.Buttons(_now, GameCard.Pill("Cancel", "\uE711", false, p, () => Host.Pop(this)));
        buttons.Margin = new Thickness(0, 6, 0, 0);
        column.Children.Add(buttons);
        root.Children.Add(column);
        Content = root;

        _timer.Tick += (_, _) =>
        {
            if (--_seconds > 0) { ShowNumber(_seconds.ToString(), false); Host.Rumble(70); return; }
            _ = StartAsync();
        };
    }

    public override void FocusDefault() => _now.Focus();

    public override void OnShown()
    {
        if (_timer.IsEnabled || _finishing) return;
        ShowNumber(_seconds.ToString(), false);
        _timer.Start();
        Host.Rumble(70);
        // The rim drains smoothly over the whole countdown, and keeps turning its colors as it goes.
        if (_rim.Tag is double around)
            _rim.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, -around, TimeSpan.FromSeconds(_total)));
        if (!Theme.ReduceMotion)
        {
            _rimTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.4)) { RepeatBehavior = RepeatBehavior.Forever });
            var breathe = new DoubleAnimation(1, 1.025, TimeSpan.FromSeconds(0.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            _coverPop.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
            _coverPop.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
        }
        _ = LoadLogoAsync();
    }

    /// <summary>A number rises into place and the one before rises away.</summary>
    private void ShowNumber(string text, bool start)
    {
        foreach (var old in _numberHost.Children.OfType<TextBlock>().ToList())
        {
            if (Theme.ReduceMotion) { _numberHost.Children.Remove(old); continue; }
            var lift = (TranslateTransform)old.RenderTransform;
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -70, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) => _numberHost.Children.Remove(old);
            old.BeginAnimation(OpacityProperty, fade);
        }
        var number = new TextBlock
        {
            Text = text, FontSize = start ? 96 : 120, FontWeight = FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, RenderTransform = new TranslateTransform(), RenderTransformOrigin = new Point(0.5, 0.5)
        };
        number.SetResourceReference(TextBlock.ForegroundProperty, start ? "AccentBrush" : "FgBrush");
        if (start && !Perf.Active) number.Effect = new DropShadowEffect { Color = Switch2Settings.Blue, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.6 };
        _numberHost.Children.Add(number);
        if (Theme.ReduceMotion) return;
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
        ((TranslateTransform)number.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(80, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        number.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    /// <summary>"Start!": the rim flashes out from the cover, a longer pulse, then the game starts.</summary>
    private async Task StartAsync()
    {
        if (_finishing) return;
        _finishing = true;
        _timer.Stop();
        Host.Rumble(160);
        ShowNumber("Start!", true);
        _label.Text = "";
        if (!Theme.ReduceMotion)
        {
            var span = TimeSpan.FromMilliseconds(520);
            var grow = new DoubleAnimation(1, 1.35, span) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            ((ScaleTransform)_flash.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            ((ScaleTransform)_flash.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            _flash.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, span));
            var pop = new DoubleAnimation(1.08, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            _coverPop.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            _coverPop.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        await Task.Delay(_preview ? 900 : 520);
        _go = !_preview;
        Host.Pop(this);
    }

    private async Task LoadLogoAsync()
    {
        if (_preview && _game.AppId == 0 && _game.Cover == null) return; // the stand-in game of a preview
        try
        {
            var key = Host.Settings.SteamGridDbApiKey;
            var ct = _cts.Token;
            var logo = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(key);
                var path = await client.GetLogoAsync(_game, ct);
                return path == null ? null : ImageLoader.FromFile(path, 920);
            }, ct);
            if (logo == null || ct.IsCancellationRequested) return;
            _logo.Source = logo;
            _logo.Visibility = Visibility.Visible;
            _name.Visibility = Visibility.Hidden;
            if (!Theme.ReduceMotion) _logo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        }
        catch (Exception)
        {
            // No logo, offline, or the countdown ended first: the name stays.
        }
    }

    public override void OnClosed()
    {
        _timer.Stop();
        _cts.Cancel();
        _done.TrySetResult(_go);
    }
}
