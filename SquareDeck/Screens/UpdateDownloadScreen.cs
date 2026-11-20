using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Downloading a SquareDeck update: the version, a progress bar that fills as the file arrives, how much is done,
/// the speed and the time left. B (or Cancel) stops the download. <see cref="Result"/> gives the downloaded file, or
/// null if it was cancelled; a failure comes back as the exception's message in <see cref="Problem"/>.
/// </summary>
public sealed class UpdateDownloadScreen : Screen
{
    private readonly ReleaseInfo _release;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<string?> _done = new();
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Height = 12 };
    private readonly Border _track = new() { Height = 12 };
    private readonly TextBlock _percent = Ui.Text("0%", 40, "FgBrush", bold: true);
    private readonly TextBlock _amount = Ui.Text("Connecting…", 15, "SubtleBrush");
    private readonly TextBlock _speed = Ui.Text("", 15, "SubtleBrush");
    private readonly Button _cancel;
    private DateTime _started, _lastAt;
    private long _lastBytes;
    private double _rate; // bytes a second, smoothed

    public override bool IsDialog => true;
    public Task<string?> Result => _done.Task;
    public string? Problem { get; private set; }

    public UpdateDownloadScreen(ReleaseInfo release)
    {
        _release = release;
        var panel = new StackPanel();

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        var badge = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 12)), Margin = new Thickness(0, 0, 16, 0), Child = Ui.Glyph("", 26, "BgBrush") };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        head.Children.Add(badge);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text("Downloading update", 24, "FgBrush", bold: true));
        titles.Children.Add(Ui.Text($"SquareDeck {release.Tag}  ·  you have {Updater.Current}", 15, "SubtleBrush"));
        head.Children.Add(titles);
        panel.Children.Add(head);

        var numbers = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        numbers.ColumnDefinitions.Add(new ColumnDefinition());
        numbers.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _percent.VerticalAlignment = VerticalAlignment.Bottom;
        numbers.Children.Add(_percent);
        _speed.VerticalAlignment = VerticalAlignment.Bottom;
        _speed.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetColumn(_speed, 1);
        numbers.Children.Add(_speed);
        panel.Children.Add(numbers);

        double radius = Math.Max(Theme.TileCorner > 4 ? 6 : 0, 0);
        _track.CornerRadius = _fill.CornerRadius = new CornerRadius(radius);
        _track.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        _fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var bar = new Grid();
        bar.Children.Add(_track);
        bar.Children.Add(_fill);
        bar.SizeChanged += (_, _) => _fill.Width = Math.Min(_fill.Width, bar.ActualWidth);
        panel.Children.Add(bar);
        _amount.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(_amount);

        _cancel = Ui.PanelButton("", "Cancel");
        _cancel.HorizontalAlignment = HorizontalAlignment.Right;
        _cancel.Margin = new Thickness(0, 24, 0, 0);
        _cancel.Click += (_, _) => Cancel();
        panel.Children.Add(_cancel);

        var box = new Border
        {
            Width = 620, Padding = new Thickness(36, 32, 36, 28), CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner * 2, 8)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = panel,
            Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45 }
        };
        box.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        box.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)) };
        backdrop.Children.Add(box);
        Content = backdrop;
    }

    public override void FocusDefault() => _cancel.Focus();

    public override async void OnShown()
    {
        if (_started != default) return;
        _started = _lastAt = DateTime.UtcNow;
        long total = Math.Max(0, _release.AssetSize);
        var progress = new Progress<double>(fraction => Show(fraction, total));
        try
        {
            var file = await Task.Run(() => Updater.DownloadAsync(_release, progress, _cts.Token));
            Show(1, total);
            _amount.Text = "Download complete. Installing…";
            await Task.Delay(400);
            Host.Pop(this);
            _done.TrySetResult(file);
        }
        catch (OperationCanceledException)
        {
            Host.Pop(this);
            _done.TrySetResult(null);
        }
        catch (Exception ex)
        {
            Problem = ex.Message;
            Host.Pop(this);
            _done.TrySetResult(null);
        }
    }

    private void Show(double fraction, long total)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        _percent.Text = $"{fraction * 100:0}%";
        if (_fill.Parent is Grid bar && bar.ActualWidth > 0)
        {
            double to = bar.ActualWidth * fraction;
            if (Theme.ReduceMotion) _fill.Width = to;
            else _fill.BeginAnimation(WidthProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(160)));
        }
        if (total <= 0) { _amount.Text = "Downloading…"; return; }
        long bytes = (long)(total * fraction);
        var now = DateTime.UtcNow;
        double seconds = (now - _lastAt).TotalSeconds;
        if (seconds >= 0.5)
        {
            double rate = (bytes - _lastBytes) / seconds;
            _rate = _rate <= 0 ? rate : _rate * 0.7 + rate * 0.3;
            _lastBytes = bytes;
            _lastAt = now;
            if (_rate > 0)
            {
                double left = (total - bytes) / _rate;
                _speed.Text = $"{Size(_rate)}/s  ·  " + (left < 60 ? $"{Math.Ceiling(left):0} s left" : $"{Math.Ceiling(left / 60):0} min left");
            }
        }
        _amount.Text = $"{Size(bytes)} of {Size(total)}";
    }

    private static string Size(double bytes) => bytes >= 1 << 30 ? $"{bytes / (1 << 30):0.0} GB" : bytes >= 1 << 20 ? $"{bytes / (1 << 20):0.0} MB" : $"{bytes / 1024:0} KB";

    private void Cancel()
    {
        _amount.Text = "Cancelling…";
        _cts.Cancel();
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Back) { Cancel(); return true; }
        return action != NavAction.Accept; // only Cancel can be pressed
    }

    public override void OnClosed() => _cts.Cancel();
}
