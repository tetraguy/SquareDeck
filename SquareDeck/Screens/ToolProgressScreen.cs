using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The pop-up while ASUS's Armoury Crate tool is fetched and run: three steps (Downloading, Unzipping, Running) with a
/// moving picture for each (an arrow dropping into a tray, a box opening, a turning gear), a bar for the download and
/// Cancel until the tool starts.
/// </summary>
public sealed class ToolProgressScreen : Screen
{
    public override bool IsDialog => true;

    private readonly TextBlock _title, _detail, _glyph;
    private readonly Border _barTrack, _barFill;
    private readonly (Border Dot, TextBlock Label)[] _steps = new (Border, TextBlock)[3];
    private readonly RotateTransform _spin = new();
    private readonly TranslateTransform _drop = new();
    private readonly ScaleTransform _pulse = new() { CenterX = 40, CenterY = 40 };
    private readonly Button _cancelButton;
    private readonly Action _cancel;
    private ArmouryCrate.Stage _stage = (ArmouryCrate.Stage)(-1);
    private bool _cancellable = true;

    public ToolProgressScreen(string title, Action cancel)
    {
        _cancel = cancel;
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        // The moving picture.
        var stage = new Grid { Width = 80, Height = 80, Margin = new Thickness(0, 0, 0, 18), HorizontalAlignment = HorizontalAlignment.Center };
        var ring = new Border { CornerRadius = new CornerRadius(40), Opacity = 0.18 };
        ring.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        stage.Children.Add(ring);
        _glyph = Ui.Glyph("", 36);
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _glyph.HorizontalAlignment = HorizontalAlignment.Center;
        _glyph.VerticalAlignment = VerticalAlignment.Center;
        _glyph.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(_spin);
        group.Children.Add(_drop);
        _glyph.RenderTransform = group;
        stage.RenderTransform = _pulse;
        stage.Children.Add(_glyph);
        panel.Children.Add(stage);

        _title = Ui.Text(title, 26, "FgBrush", bold: true);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.TextAlignment = TextAlignment.Center;
        _title.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_title);

        // The three steps.
        var steps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 14) };
        string[] names = { "Downloading", "Unzipping", "Running" };
        for (int i = 0; i < 3; i++)
        {
            if (i > 0)
            {
                var line = new Border { Width = 34, Height = 2, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                line.SetResourceReference(Border.BackgroundProperty, "SubtleBrush");
                line.Opacity = 0.4;
                steps.Children.Add(line);
            }
            var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Border.BackgroundProperty, "SubtleBrush");
            var label = Ui.Text(names[i], 16, "SubtleBrush");
            label.VerticalAlignment = VerticalAlignment.Center;
            var item = new StackPanel { Orientation = Orientation.Horizontal };
            item.Children.Add(dot);
            item.Children.Add(label);
            steps.Children.Add(item);
            _steps[i] = (dot, label);
        }
        panel.Children.Add(steps);

        _barTrack = new Border { Height = 8, CornerRadius = new CornerRadius(4), Width = 420, HorizontalAlignment = HorizontalAlignment.Center };
        _barTrack.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        _barFill = new Border { Height = 8, CornerRadius = new CornerRadius(4), Width = 0, HorizontalAlignment = HorizontalAlignment.Left };
        _barFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _barTrack.Child = _barFill;
        panel.Children.Add(_barTrack);

        _detail = Ui.Text("", 15, "SubtleBrush");
        _detail.HorizontalAlignment = HorizontalAlignment.Center;
        _detail.TextAlignment = TextAlignment.Center;
        _detail.TextWrapping = TextWrapping.Wrap;
        _detail.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(_detail);

        _cancelButton = Ui.PanelButton("", "Cancel");
        _cancelButton.HorizontalAlignment = HorizontalAlignment.Center;
        _cancelButton.Margin = new Thickness(0, 20, 0, 0);
        _cancelButton.Click += (_, _) => Cancel();
        panel.Children.Add(_cancelButton);

        var box = new Border
        {
            Width = 620, Padding = new Thickness(40, 34, 40, 34), CornerRadius = new CornerRadius(20), Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45 },
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        box.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        box.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)) };
        backdrop.Children.Add(Ui.SkinBox(box));
        Content = backdrop;
    }

    public override void FocusDefault() => _cancelButton.Focus();

    /// <summary>Moves to a step (and its picture), with a line about it and, while downloading, how far along.</summary>
    public void Report(ArmouryCrate.Stage stage, string detail, double? fraction)
    {
        _detail.Text = detail;
        if (stage == ArmouryCrate.Stage.Downloading)
        {
            _barTrack.Visibility = Visibility.Visible;
            if (fraction is double f) { _barFill.BeginAnimation(WidthProperty, null); _barFill.Width = 420 * Math.Clamp(f, 0, 1); }
            else if (_barFill.Width <= 0) Indeterminate();
        }
        else if (stage != ArmouryCrate.Stage.Unzipping) _barTrack.Visibility = Visibility.Collapsed;
        if (stage == _stage) return;
        _stage = stage;
        if (stage == ArmouryCrate.Stage.Unzipping) { _barFill.BeginAnimation(WidthProperty, null); _barFill.Width = 0; Indeterminate(); }
        for (int i = 0; i < 3; i++)
        {
            bool done = i < (int)stage, now = i == (int)stage;
            _steps[i].Dot.SetResourceReference(Border.BackgroundProperty, done || now ? "AccentBrush" : "SubtleBrush");
            _steps[i].Label.SetResourceReference(TextBlock.ForegroundProperty, now ? "FgBrush" : done ? "AccentBrush" : "SubtleBrush");
        }
        // Once ASUS's tool runs, it's in charge: Cancel goes.
        if (stage >= ArmouryCrate.Stage.Running && _cancellable)
        {
            _cancellable = false;
            _cancelButton.Visibility = Visibility.Collapsed;
        }
        Animate(stage);
    }

    private void Indeterminate()
    {
        if (Theme.ReduceMotion) { _barFill.Width = 140; return; }
        _barFill.BeginAnimation(WidthProperty, new DoubleAnimation(40, 300, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
    }

    /// <summary>An arrow dropping in for the download, a box breathing for the unzip, a gear turning while the tool runs.</summary>
    private void Animate(ArmouryCrate.Stage stage)
    {
        _spin.BeginAnimation(RotateTransform.AngleProperty, null);
        _drop.BeginAnimation(TranslateTransform.YProperty, null);
        _pulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _pulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _spin.Angle = 0;
        _drop.Y = 0;
        _glyph.Text = stage switch { ArmouryCrate.Stage.Downloading => "", ArmouryCrate.Stage.Unzipping => "", _ => "" };
        if (Theme.ReduceMotion) return;
        switch (stage)
        {
            case ArmouryCrate.Stage.Downloading:
                _drop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, 8, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase() });
                break;
            case ArmouryCrate.Stage.Unzipping:
                var breathe = new DoubleAnimation(0.92, 1.1, TimeSpan.FromSeconds(0.55)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                _pulse.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
                _pulse.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
                break;
            default:
                _spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.4)) { RepeatBehavior = RepeatBehavior.Forever });
                break;
        }
    }

    private void Cancel()
    {
        if (!_cancellable) return;
        UiSound.Back();
        _cancel();
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Back) { Cancel(); return true; }
        if (action == NavAction.Accept && _cancellable) { Cancel(); return true; }
        return true;
    }
}
