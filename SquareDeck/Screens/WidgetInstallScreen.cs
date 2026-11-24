using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Installing or updating the Game Bar widget, step by step: each step with its state (waiting, working, done,
/// skipped, failed) and a line about what's happening, Windows' own progress while it installs, the time so far, and
/// Cancel. It ends with the result: Open Game Bar after an install, the reason and Try again after a failure.
/// Result: true when the widget is installed.
/// </summary>
public sealed class WidgetInstallScreen : Screen
{
    private readonly WidgetInstaller.Package _package;
    private readonly bool _update;
    private readonly bool _mandatory;
    private readonly TaskCompletionSource<(bool Ok, string Message)> _done = new();
    private readonly Dictionary<string, (Border Mark, TextBlock Glyph, TextBlock Name, TextBlock Detail)> _rows = new();
    private readonly TextBlock _title, _subtitle, _elapsed, _percent;
    private readonly Border _bar, _barFill;
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
    private readonly System.Windows.Threading.DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _cancel;
    private DateTime _started;
    private bool _running, _lastOk;
    private string _lastMessage = "";

    public override bool IsDialog => true;

    /// <summary>The result once the screen closes: installed, and the message.</summary>
    public Task<(bool Ok, string Message)> Result => _done.Task;

    public WidgetInstallScreen(WidgetInstaller.Package package, bool update, bool mandatory = false)
    {
        _package = package;
        _update = update;
        _mandatory = mandatory;

        var panel = new StackPanel();

        // The widget's icon, what's happening, and to which version.
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        var icon = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 18, 0), ClipToBounds = true };
        icon.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        icon.Child = WidgetGlyph();
        DockPanel.SetDock(icon, Dock.Left);
        head.Children.Add(icon);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _title = Ui.Text((update ? "Updating" : "Installing") + " the Game Bar widget", 24, "FgBrush", bold: true);
        _title.TextWrapping = TextWrapping.Wrap;
        words.Children.Add(_title);
        var installed = WidgetInstaller.InstalledVersion();
        _subtitle = Ui.Text(installed != null && update ? $"Version {installed}  →  {package.Version}" : $"Version {package.Version}", 15, "SubtleBrush");
        _subtitle.Margin = new Thickness(0, 4, 0, 0);
        words.Children.Add(_subtitle);
        head.Children.Add(words);
        panel.Children.Add(head);

        // The steps.
        foreach (var (id, name) in WidgetInstaller.Steps)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var mark = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            mark.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            var glyph = new TextBlock { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform() };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
            mark.Child = glyph;
            DockPanel.SetDock(mark, Dock.Left);
            row.Children.Add(mark);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var label = Ui.Text(name, 17, "SubtleBrush", bold: true);
            text.Children.Add(label);
            var detail = Ui.Text("", 14, "SubtleBrush");
            detail.TextWrapping = TextWrapping.Wrap;
            detail.Visibility = Visibility.Collapsed;
            detail.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(detail);
            row.Children.Add(text);
            panel.Children.Add(row);
            _rows[id] = (mark, glyph, label, detail);
            if (id == "install")
            {
                // Windows' own progress, under the Installing step.
                var track = new Grid { Margin = new Thickness(44, 0, 0, 14), Visibility = Visibility.Collapsed };
                track.ColumnDefinitions.Add(new ColumnDefinition());
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
                _bar.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
                _barFill = new Border { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                _barFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                var holder = new Grid();
                holder.Children.Add(_bar);
                holder.Children.Add(_barFill);
                track.Children.Add(holder);
                _percent = Ui.Text("0%", 13, "SubtleBrush", bold: true);
                _percent.Margin = new Thickness(12, 0, 0, 0);
                Grid.SetColumn(_percent, 1);
                track.Children.Add(_percent);
                panel.Children.Add(track);
                _barTrack = track;
            }
        }
        _bar ??= new Border();
        _barFill ??= new Border();
        _percent ??= new TextBlock();

        _elapsed = Ui.Text("", 13, "SubtleBrush");
        _elapsed.HorizontalAlignment = HorizontalAlignment.Center;
        _elapsed.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(_elapsed);
        panel.Children.Add(_buttons);

        var box = new Border
        {
            Width = 640, Padding = new Thickness(36, 32, 36, 30), CornerRadius = new CornerRadius(22),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45 },
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = panel
        };
        box.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        box.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)) };
        backdrop.Children.Add(Ui.SkinBox(box));
        Content = backdrop;

        _clock.Tick += (_, _) => ShowElapsed();
    }

    private Grid? _barTrack;

    public override void OnShown() => _ = RunAsync();

    public override void OnClosed()
    {
        _clock.Stop();
        _cancel?.Cancel();
        _done.TrySetResult((false, "Closed."));
    }

    public override void FocusDefault() => _buttons.Children.OfType<Button>().FirstOrDefault()?.Focus();

    /// <summary>
    /// The widget's gray glyph, as Game Bar's widget list shows it (Widget\SquareDeck.Widget\SquareDeck-glyph.svg):
    /// a rounded square outline with a small filled square in its lower right. Drawn here, so it needs no file.
    /// </summary>
    private static UIElement WidgetGlyph()
    {
        var gray = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        gray.Freeze();
        var canvas = new Canvas { Width = 1024, Height = 1024 };
        // WPF draws a stroke inside the shape (SVG centers it), so the outline's box is the SVG's grown by half the stroke.
        var frame = new System.Windows.Shapes.Rectangle { Width = 832, Height = 832, RadiusX = 172, RadiusY = 172, Stroke = gray, StrokeThickness = 104 };
        Canvas.SetLeft(frame, 96); Canvas.SetTop(frame, 96);
        var dot = new System.Windows.Shapes.Rectangle { Width = 232, Height = 232, RadiusX = 56, RadiusY = 56, Fill = gray };
        Canvas.SetLeft(dot, 520); Canvas.SetTop(dot, 520);
        canvas.Children.Add(frame);
        canvas.Children.Add(dot);
        return new Viewbox { Child = canvas, Margin = new Thickness(12), Stretch = Stretch.Uniform };
    }

    private void ShowElapsed()
    {
        var t = DateTime.Now - _started;
        _elapsed.Text = _running ? $"{(int)t.TotalMinutes}:{t.Seconds:00} so far" : "";
    }

    private void Buttons(params (string Glyph, string Text, Action Click)[] buttons)
    {
        _buttons.Children.Clear();
        foreach (var (glyph, text, click) in buttons)
        {
            var b = Ui.PanelButton(glyph, text);
            b.Margin = new Thickness(6, 0, 6, 0);
            b.Click += (_, _) => click();
            _buttons.Children.Add(b);
        }
        FocusLater(_buttons.Children.OfType<Button>().FirstOrDefault());
    }

    private async Task RunAsync()
    {
        // Every step back to waiting (for Try again).
        foreach (var (id, _) in WidgetInstaller.Steps) SetStep(new WidgetInstaller.Step(id, "waiting", ""));
        if (_barTrack != null) _barTrack.Visibility = Visibility.Collapsed;
        _cancel = new CancellationTokenSource();
        _started = DateTime.Now;
        _running = true;
        _clock.Start();
        ShowElapsed();
        Buttons(("", "Cancel", () => _cancel?.Cancel()));
        var progress = new Progress<WidgetInstaller.Step>(SetStep);
        var (ok, message) = await WidgetInstaller.InstallAsync(_package, progress, _cancel.Token);
        _lastOk = ok;
        _lastMessage = message;
        _running = false;
        _clock.Stop();
        _elapsed.Text = $"Took {(int)(DateTime.Now - _started).TotalMinutes}:{(DateTime.Now - _started).Seconds:00}";
        if (ok)
        {
            UiSound.Toast();
            _title.Text = _update ? "The widget is updated" : "The widget is installed";
            _subtitle.Text = "Open Game Bar with Win + G or the Xbox button: SquareDeck is in its widget menu.";
            Buttons(("", "Open Game Bar", () => { ProcessUtil.Open("ms-gamebar:"); Finish(true, message); }),
                    ("", "Done", () => Finish(true, message)));
        }
        else
        {
            _title.Text = _update ? "The widget wasn't updated" : "The widget wasn't installed";
            _subtitle.Text = message;
            _subtitle.TextWrapping = TextWrapping.Wrap;
            var buttons = new List<(string, string, Action)> { ("", "Try again", () => _ = RunAsync()) };
            buttons.Add(_mandatory ? ("", "Ask me next time", (Action)(() => Finish(false, message))) : ("", "Close", (Action)(() => Finish(false, message))));
            if (File.Exists(Path.Combine(Settings.DataDir, "widget-install.log")))
                buttons.Add(("", "Show the log", () => Host.Push(new ExplorerScreen(Settings.DataDir))));
            Buttons(buttons.ToArray());
        }
    }

    private void Finish(bool ok, string message)
    {
        _done.TrySetResult((ok, message));
        Host.Pop(this);
    }

    private void SetStep(WidgetInstaller.Step step)
    {
        if (!_rows.TryGetValue(step.Id, out var row)) return;
        var glyphSpin = (RotateTransform)row.Glyph.RenderTransform;
        if (step.State != "progress") glyphSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        switch (step.State)
        {
            case "waiting":
                row.Glyph.Text = "";
                row.Mark.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
                row.Name.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
                break;
            case "active":
            case "progress":
                row.Glyph.Text = ""; // a turning arrow while it works
                row.Glyph.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
                row.Mark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                row.Name.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
                if (step.State == "active" && !Theme.ReduceMotion)
                    glyphSpin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
                break;
            case "done":
                row.Glyph.Text = "";
                row.Glyph.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
                row.Mark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                row.Name.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
                break;
            case "skipped":
                row.Glyph.Text = "";
                row.Glyph.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
                row.Mark.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
                row.Name.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
                break;
            case "failed":
                row.Glyph.Text = "";
                row.Glyph.Foreground = Brushes.White;
                row.Mark.Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x3B, 0x3B));
                row.Name.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
                break;
        }
        row.Detail.Text = step.Detail;
        row.Detail.Visibility = step.Detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (step.Id == "install" && _barTrack != null)
        {
            if (step.State is "active" or "progress")
            {
                _barTrack.Visibility = Visibility.Visible;
                double pct = step.Percent ?? 0;
                _percent.Text = $"{pct:0}%";
                double width = Math.Max(0, _bar.ActualWidth) * pct / 100;
                if (Theme.ReduceMotion) _barFill.Width = width;
                else _barFill.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(250)));
            }
            else if (step.State == "done") { _percent.Text = "100%"; _barFill.BeginAnimation(WidthProperty, null); _barFill.Width = _bar.ActualWidth; }
            else _barTrack.Visibility = step.State == "failed" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // While it runs, B is Cancel; afterwards it's Close (or, for a required update, nothing: a button has to be chosen).
    public override bool OnAction(NavAction action)
    {
        if (action != NavAction.Back) return false;
        if (_running) _cancel?.Cancel();
        else if (!_mandatory || _lastOk) Finish(_lastOk, _lastMessage);
        return true;
    }
}
