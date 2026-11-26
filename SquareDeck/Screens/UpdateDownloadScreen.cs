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
    private ProgressFill? _barFill;
    private readonly Border _track = new() { Height = 12 };
    private readonly TextBlock _percent = Ui.Text("0%", 40, "FgBrush", bold: true);
    private readonly TextBlock _amount = Ui.Text("Connecting…", 15, "SubtleBrush");
    private readonly TextBlock _speed = Ui.Text("", 15, "SubtleBrush");
    private readonly Button _cancel;
    private DateTime _started, _lastAt;
    private long _lastBytes;
    private double _rate; // bytes a second, smoothed
    private readonly UpdatePal? _pal = UpdatePal.Enabled && !Theme.ReduceMotion ? new UpdatePal() : null;
    private readonly bool _preview;

    public override bool IsDialog => true;
    public Task<string?> Result => _done.Task;
    public string? Problem { get; private set; }

    /// <param name="preview">Plays a pretend download (Settings > Experimental, to see the companion), nothing fetched.</param>
    public UpdateDownloadScreen(ReleaseInfo release, bool preview = false)
    {
        _release = release;
        _preview = preview;
        // The Switch 2 look, as the other pop-ups have it: a floating card in the card color, a regular-weight title,
        // the bar's track in the line color and Cancel as a flat row with the rimmed highlight.
        Switch2Settings.ApplyColors(this);
        var panel = new StackPanel();

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        var badge = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 12)), Margin = new Thickness(0, 0, 16, 0), Child = Ui.Glyph("", 26, "BgBrush") };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        head.Children.Add(badge);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text("Downloading update", 22, "FgBrush"));
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
        _track.SetResourceReference(Border.BackgroundProperty, "S2Line");
        _fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var bar = new Grid();
        bar.Children.Add(_track);
        bar.Children.Add(_fill);
        _barFill = new ProgressFill(bar, _fill);
        if (_pal != null) panel.Children.Add(_pal.View);
        panel.Children.Add(bar);
        _amount.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(_amount);

        var line = new Border { Height = 1, Margin = new Thickness(0, 22, 0, 0) };
        line.SetResourceReference(Border.BackgroundProperty, "S2Line");
        panel.Children.Add(line);
        _cancel = Switch2Pages.SettingRow("Cancel", null, Cancel);
        _cancel.Margin = new Thickness(0);
        panel.Children.Add(_cancel);

        var box = new Border
        {
            Width = 620, Padding = new Thickness(30, 28, 30, 18), CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Switch2Settings.Card),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = panel,
            Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45 }
        };
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)) };
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
            var file = _preview ? await PretendAsync(progress, _cts.Token) : await Task.Run(() => Updater.DownloadAsync(_release, progress, _cts.Token));
            Show(1, total);
            _amount.Text = _preview ? "Download complete. (This was a preview.)" : "Download complete. Installing…";
            _pal?.Done();
            await Task.Delay(_pal != null ? 1800 : 400);
            Host.Pop(this);
            _done.TrySetResult(file);
        }
        catch (OperationCanceledException)
        {
            if (_pal != null && !_closed) { _pal.Cancelled(); await Task.Delay(1300); }
            Host.Pop(this);
            _done.TrySetResult(null);
        }
        catch (Exception ex)
        {
            Problem = ex.Message;
            if (_pal != null && !_closed) { _pal.Failed(); _amount.Text = "The download stopped."; await Task.Delay(2000); }
            Host.Pop(this);
            _done.TrySetResult(null);
        }
    }

    /// <summary>The preview: about seven seconds of made-up progress, with a quick stretch in the middle.</summary>
    private static async Task<string?> PretendAsync(IProgress<double> progress, CancellationToken token)
    {
        double f = 0;
        while (f < 1)
        {
            await Task.Delay(100, token);
            f = Math.Min(1, f + (f is > 0.4 and < 0.65 ? 0.035 : 0.011));
            progress.Report(f);
        }
        return "";
    }

    private void Show(double fraction, long total)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        // The bar and the number move together. Downloads report many times a second, and restarting a slide on each one
        // kept the bar trailing behind the percentage, so the bar is put straight at the same point instead.
        _percent.Text = $"{Math.Floor(fraction * 100):0}%";
        _barFill?.Snap(fraction);
        _pal?.Progress(fraction, _preview ? (fraction is > 0.4 and < 0.65 ? 20e6 : 2e6) : _rate);
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

    private bool _closed;

    public override void OnClosed()
    {
        _closed = true;
        _cts.Cancel();
    }
}
