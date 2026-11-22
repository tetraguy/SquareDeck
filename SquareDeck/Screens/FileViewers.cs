using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Opens files from the File explorer inside SquareDeck: pictures and videos in the viewer, music in the Music
/// player, web shortcuts and web pages in the browser, and text files in the text editor. Anything else goes to
/// Windows as before.
/// </summary>
public static class FileOpener
{
    public static readonly string[] Images = { ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico", ".heic", ".avif" };
    public static readonly string[] Videos = { ".mp4", ".m4v", ".mkv", ".avi", ".wmv", ".mov", ".webm", ".mpg", ".mpeg", ".3gp", ".ts" };
    public static readonly string[] Audio = { ".mp3", ".m4a", ".aac", ".wma", ".wav", ".flac" };
    public static readonly string[] Text =
    {
        ".txt", ".log", ".md", ".markdown", ".ini", ".cfg", ".conf", ".config", ".json", ".xml", ".csv", ".tsv", ".yaml", ".yml", ".toml",
        ".nfo", ".bat", ".cmd", ".ps1", ".sh", ".cs", ".js", ".ts", ".css", ".lua", ".py", ".vdf", ".srt", ".properties", ".reg", ".diz"
    };
    public static readonly string[] WebPages = { ".htm", ".html", ".url", ".website" };

    private static bool Is(string path, string[] types) => types.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsMedia(string path) => Is(path, Images) || Is(path, Videos);
    public static bool IsVideo(string path) => Is(path, Videos);

    /// <summary>Opens the file in SquareDeck if it can; false if it's for Windows to open.</summary>
    /// <param name="siblings">The files around it, in the order shown, so the viewer and player can move through the folder.</param>
    public static bool TryOpen(MainWindow host, string path, IReadOnlyList<string> siblings)
    {
        if (IsMedia(path))
        {
            var media = siblings.Where(IsMedia).ToList();
            int at = media.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (at < 0) { media.Insert(0, path); at = 0; }
            host.Push(new MediaViewerScreen(media, at));
            return true;
        }
        if (Is(path, Audio))
        {
            _ = PlayMusicAsync(host, path, siblings);
            return true;
        }
        if (Is(path, WebPages))
        {
            var url = WebAddress(path);
            if (url == null) return false;
            host.Push(BrowserScreen.Open(host, url));
            return true;
        }
        if (Is(path, Text))
        {
            host.Push(new TextEditorScreen(path));
            return true;
        }
        return false;
    }

    /// <summary>The address a web shortcut points to, or a web page file's own address.</summary>
    private static string? WebAddress(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".htm" or ".html") return new Uri(path).AbsoluteUri;
        try
        {
            foreach (var line in File.ReadLines(path).Take(200))
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    var url = line[4..].Trim();
                    if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return url;
                    return null; // a steam:// or other link: Windows knows what to do with it
                }
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>Plays the song and the folder's other songs after it in the Music player, and opens the Music page.</summary>
    private static async Task PlayMusicAsync(MainWindow host, string path, IReadOnlyList<string> siblings)
    {
        var songs = siblings.Where(p => Is(p, Audio)).ToList();
        int at = songs.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (at < 0) { songs.Insert(0, path); at = 0; }
        // Up to 300 songs around the one chosen, so a huge folder doesn't keep you waiting.
        int from = Math.Max(0, at - 150);
        songs = songs.Skip(from).Take(300).ToList();
        at -= from;
        var tracks = await Task.WhenAll(songs.Select(LocalMusic.TrackAsync));
        LocalMusic.PlayList(tracks, at);
        host.Settings.MusicTab = "local";
        host.Push(new MediaScreen());
    }
}

/// <summary>
/// Pictures and videos from a folder, full screen. LB / RB (or left and right) go through the folder. Pictures: A
/// zooms in and out, LT / RT zoom step by step, the stick or d-pad moves around a zoomed picture, Y turns it.
/// Videos: A plays and pauses, left and right skip 10 seconds, up and down change the volume. X shows or hides the
/// details. Touch: pinch to zoom, drag to move, swipe to change picture; the mouse wheel zooms.
/// </summary>
public sealed class MediaViewerScreen : Screen
{
    private readonly List<string> _files;
    private int _index;
    private readonly Grid _stage = new() { ClipToBounds = true, Background = Brushes.Black };
    private readonly Image _image = new() { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5) };
    private MediaElement? _video;
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly TranslateTransform _pan = new();
    private readonly RotateTransform _turn = new();
    private readonly TextBlock _name = new() { Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _details = new() { Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255)), FontSize = 15 };
    private readonly TextBlock _counter = new() { Foreground = Brushes.White, FontSize = 17, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hints = new() { Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255)), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _message = new() { Foreground = Brushes.White, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 700 };
    private readonly Border _top, _bottom, _seekFill = new() { Height = 4, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
    private readonly Grid _seek = new() { Height = 4, Margin = new Thickness(40, 0, 40, 6), Visibility = Visibility.Collapsed };
    private readonly TextBlock _time = new() { Foreground = Brushes.White, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _chrome = true, _playing, _failed;
    private int _load;
    private Point? _dragFrom;

    public MediaViewerScreen(List<string> files, int index)
    {
        _files = files;
        _index = index;
        var group = new TransformGroup();
        group.Children.Add(_turn);
        group.Children.Add(_zoom);
        group.Children.Add(_pan);
        _image.RenderTransform = group;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _stage.Children.Add(_image);
        _stage.Children.Add(_message);

        // Top: name and details. Bottom: the place in the folder, video progress and the buttons.
        var shade = new LinearGradientBrush(Color.FromArgb(0xB0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90);
        var topStack = new StackPanel();
        topStack.Children.Add(_name);
        topStack.Children.Add(_details);
        _top = new Border { Background = shade, Padding = new Thickness(40, 26, 40, 40), VerticalAlignment = VerticalAlignment.Top, Child = topStack, IsHitTestVisible = false };
        _seekFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _seek.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)), CornerRadius = new CornerRadius(2) });
        _seek.Children.Add(_seekFill);
        var bottomStack = new StackPanel();
        bottomStack.Children.Add(_seek);
        bottomStack.Children.Add(_time);
        var row = new DockPanel { Margin = new Thickness(40, 6, 40, 0) };
        DockPanel.SetDock(_counter, Dock.Right);
        row.Children.Add(_counter);
        row.Children.Add(_hints);
        bottomStack.Children.Add(row);
        _bottom = new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xB0, 0, 0, 0), 90),
            Padding = new Thickness(0, 40, 0, 22), VerticalAlignment = VerticalAlignment.Bottom, Child = bottomStack, IsHitTestVisible = false
        };
        var root = new Grid { Background = Brushes.Black };
        root.Children.Add(_stage);
        root.Children.Add(_top);
        root.Children.Add(_bottom);
        Content = root;
        Focusable = true;

        // Mouse and touch.
        _stage.MouseWheel += (_, e) => { if (_video == null) ZoomBy(e.Delta > 0 ? 1.2 : 1 / 1.2); };
        _stage.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { ToggleZoom(); return; }
            _dragFrom = e.GetPosition(_stage);
            _stage.CaptureMouse();
        };
        _stage.MouseMove += (_, e) =>
        {
            if (_dragFrom is not { } from || _zoom.ScaleX <= 1.01) return;
            var now = e.GetPosition(_stage);
            PanBy(now.X - from.X, now.Y - from.Y);
            _dragFrom = now;
        };
        _stage.MouseLeftButtonUp += (_, _) => { _dragFrom = null; _stage.ReleaseMouseCapture(); };
        _stage.IsManipulationEnabled = true;
        _stage.ManipulationStarting += (_, e) => { e.ManipulationContainer = _stage; e.Handled = true; };
        _stage.ManipulationDelta += (_, e) =>
        {
            var d = e.DeltaManipulation;
            if (_video == null && Math.Abs(d.Scale.X - 1) > 0.001) ZoomBy(d.Scale.X);
            if (_zoom.ScaleX > 1.01) PanBy(d.Translation.X, d.Translation.Y);
            e.Handled = true;
        };
        _stage.ManipulationCompleted += (_, e) =>
        {
            // A swipe on a picture that isn't zoomed moves through the folder.
            if (_zoom.ScaleX <= 1.01 && Math.Abs(e.TotalManipulation.Translation.X) > 120 && Math.Abs(e.TotalManipulation.Translation.Y) < 100)
                Step(e.TotalManipulation.Translation.X < 0 ? 1 : -1);
            e.Handled = true;
        };
        _clock.Tick += (_, _) => UpdateTime();
    }

    public override void FocusDefault() => Focus();

    public override void OnShown()
    {
        Focus();
        Show();
    }

    public override void OnClosed()
    {
        _clock.Stop();
        StopVideo();
    }

    public override void OnCovered()
    {
        if (_playing) TogglePlay();
    }

    private string Current => _files[_index];

    private void Step(int delta)
    {
        if (_files.Count < 2) return;
        _index = ((_index + delta) % _files.Count + _files.Count) % _files.Count;
        Show();
    }

    private async void Show()
    {
        int load = ++_load;
        StopVideo();
        ResetView();
        _failed = false;
        _message.Text = "";
        string path = Current;
        _name.Text = Path.GetFileName(path);
        _counter.Text = _files.Count > 1 ? $"{_index + 1} / {_files.Count}" : "";
        var info = new FileInfo(path);
        string size = info.Exists ? Size(info.Length) : "";
        string date = info.Exists ? info.LastWriteTime.ToString("MMM d, yyyy  h:mm tt") : "";

        if (FileOpener.IsVideo(path))
        {
            _image.Source = null;
            _seek.Visibility = Visibility.Visible;
            _time.Visibility = Visibility.Visible;
            _details.Text = string.Join("   ·   ", new[] { "Video", size, date }.Where(s => s.Length > 0));
            _hints.Text = Hint("{Select} Play / pause    ◀ ▶ 10 seconds    ▲ ▼ Volume    {LB} {RB} Previous / next    X Details    {Back} Close");
            _video = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform, Volume = 0.8, Source = new Uri(path) };
            _video.MediaOpened += (_, _) => UpdateTime();
            _video.MediaEnded += (_, _) => { _video.Stop(); _playing = false; UpdateTime(); };
            _video.MediaFailed += (_, _) =>
            {
                _failed = true;
                _message.Text = "This video's format can't be played inside SquareDeck.\nPress Y to open it with Windows' own player.";
            };
            _stage.Children.Insert(0, _video);
            _video.Play();
            _playing = true;
            _clock.Start();
            return;
        }

        _seek.Visibility = Visibility.Collapsed;
        _time.Visibility = Visibility.Collapsed;
        _hints.Text = Hint("{Select} Zoom    LT / RT Zoom in steps    Y Turn    {LB} {RB} Previous / next    X Details    {Back} Close");
        _details.Text = string.Join("   ·   ", new[] { size, date }.Where(s => s.Length > 0));
        var picture = await Task.Run(() => { try { return ImageLoader.FromFile(path, 3840); } catch { return null; } });
        if (load != _load) return; // moved on while it loaded
        _image.Source = picture;
        _image.Opacity = 0;
        _image.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(Theme.ReduceMotion ? 0 : 160)));
        if (picture is System.Windows.Media.Imaging.BitmapSource bitmap)
            _details.Text = string.Join("   ·   ", new[] { $"{bitmap.PixelWidth} × {bitmap.PixelHeight}", size, date }.Where(s => s.Length > 0));
        else
        {
            _failed = true;
            _message.Text = "This picture can't be shown inside SquareDeck.\nPress Y to open it with Windows.";
        }
    }

    private static string Hint(string text) => text.Replace("{Select}", Hints.KeyLabel("Select")).Replace("{Back}", Hints.KeyLabel("Back"))
        .Replace("{LB}", Hints.KeyLabel("LB")).Replace("{RB}", Hints.KeyLabel("RB"));

    private static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    private void StopVideo()
    {
        _clock.Stop();
        if (_video == null) return;
        try { _video.Stop(); _video.Close(); } catch (Exception) { }
        _stage.Children.Remove(_video);
        _video = null;
        _playing = false;
    }

    private void TogglePlay()
    {
        if (_video == null) return;
        if (_playing) _video.Pause(); else _video.Play();
        _playing = !_playing;
        UpdateTime();
    }

    private void UpdateTime()
    {
        if (_video == null || !_video.NaturalDuration.HasTimeSpan) { _time.Text = ""; return; }
        var total = _video.NaturalDuration.TimeSpan;
        var at = _video.Position;
        _seekFill.Width = Math.Max(0, _seek.ActualWidth * Math.Clamp(at.TotalSeconds / Math.Max(1, total.TotalSeconds), 0, 1));
        string F(TimeSpan t) => t.ToString(t.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
        _time.Text = $"{(_playing ? "▶" : "❚❚")}   {F(at)} / {F(total)}   ·   Volume {(int)Math.Round(_video.Volume * 100)}%";
    }

    private void SeekBy(double seconds)
    {
        if (_video == null || !_video.NaturalDuration.HasTimeSpan) return;
        var total = _video.NaturalDuration.TimeSpan;
        _video.Position = TimeSpan.FromSeconds(Math.Clamp(_video.Position.TotalSeconds + seconds, 0, total.TotalSeconds));
        UpdateTime();
    }

    private void ResetView()
    {
        _zoom.ScaleX = _zoom.ScaleY = 1;
        _pan.X = _pan.Y = 0;
        _turn.Angle = 0;
    }

    private void ToggleZoom()
    {
        if (_video != null) return;
        if (_zoom.ScaleX > 1.01) { _zoom.ScaleX = _zoom.ScaleY = 1; _pan.X = _pan.Y = 0; }
        else _zoom.ScaleX = _zoom.ScaleY = 2.5;
    }

    private void ZoomBy(double factor)
    {
        double next = Math.Clamp(_zoom.ScaleX * factor, 1, 8);
        _zoom.ScaleX = _zoom.ScaleY = next;
        if (next <= 1.01) { _pan.X = _pan.Y = 0; return; }
        PanBy(0, 0);
    }

    /// <summary>Moves a zoomed picture, without letting it drift off the screen.</summary>
    private void PanBy(double dx, double dy)
    {
        double maxX = Math.Max(0, (_stage.ActualWidth * _zoom.ScaleX - _stage.ActualWidth) / 2);
        double maxY = Math.Max(0, (_stage.ActualHeight * _zoom.ScaleY - _stage.ActualHeight) / 2);
        _pan.X = Math.Clamp(_pan.X + dx, -maxX, maxX);
        _pan.Y = Math.Clamp(_pan.Y + dy, -maxY, maxY);
    }

    private void ToggleChrome()
    {
        _chrome = !_chrome;
        foreach (var bar in new[] { _top, _bottom })
            bar.BeginAnimation(OpacityProperty, new DoubleAnimation(_chrome ? 1 : 0, TimeSpan.FromMilliseconds(180)));
    }

    public override bool OnRightStick(double x, double y, bool pressed)
    {
        if (_video != null || _zoom.ScaleX <= 1.01 || (x == 0 && y == 0)) return false;
        PanBy(-x * 28, y * 28);
        return true;
    }

    public override bool OnAction(NavAction action)
    {
        bool zoomed = _zoom.ScaleX > 1.01;
        switch (action)
        {
            case NavAction.LB: Step(-1); return true;
            case NavAction.RB: Step(1); return true;
            case NavAction.X: ToggleChrome(); return true;
            case NavAction.Y when _failed:
                try { ProcessUtil.Open(Current); } catch (Exception) { }
                return true;
        }
        if (_video != null)
        {
            switch (action)
            {
                case NavAction.Accept: TogglePlay(); return true;
                case NavAction.Left: SeekBy(-10); return true;
                case NavAction.Right: SeekBy(10); return true;
                case NavAction.Up: _video.Volume = Math.Min(1, _video.Volume + 0.1); UpdateTime(); return true;
                case NavAction.Down: _video.Volume = Math.Max(0, _video.Volume - 0.1); UpdateTime(); return true;
            }
            return false;
        }
        switch (action)
        {
            case NavAction.Accept: ToggleZoom(); return true;
            case NavAction.RT: ZoomBy(1.25); return true;
            case NavAction.LT: ZoomBy(1 / 1.25); return true;
            case NavAction.Y: _turn.Angle = (_turn.Angle + 90) % 360; return true;
            case NavAction.Left: if (zoomed) PanBy(90, 0); else Step(-1); return true;
            case NavAction.Right: if (zoomed) PanBy(-90, 0); else Step(1); return true;
            case NavAction.Up: if (zoomed) PanBy(0, 90); return true;
            case NavAction.Down: if (zoomed) PanBy(0, -90); return true;
            case NavAction.Back when zoomed: ToggleZoom(); return true;
        }
        return false;
    }
}

/// <summary>
/// A text editor for text files: read and change them with a keyboard (or the touch keyboard), save, wrap long
/// lines, change the text size and find words. It opens for reading (so nothing changes by accident); A, a tap or a
/// click starts editing.
/// </summary>
public sealed class TextEditorScreen : Screen
{
    private const long MaxBytes = 16L << 20;

    private readonly string _path;
    private readonly TextBox _box = new()
    {
        AcceptsReturn = true, AcceptsTab = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(18, 14, 18, 14), IsUndoEnabled = true
    };
    private readonly TextBlock _status = Ui.Text("", 14, "SubtleBrush");
    private readonly TextBlock _mode = Ui.Text("", 14, "AccentBrush", bold: true);
    private Encoding _encoding = new UTF8Encoding(false);
    private string _saved = "";
    private bool _editing, _loaded, _closing;
    private double _size = 18;

    public TextEditorScreen(string path)
    {
        _path = path;
        bool code = !new[] { ".txt", ".md", ".markdown", ".nfo", ".srt", ".diz", ".log" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        _box.FontFamily = new FontFamily(code ? "Cascadia Mono, Consolas" : "Segoe UI Variable Text, Segoe UI");
        _box.FontSize = _size;
        _box.TextWrapping = code ? TextWrapping.NoWrap : TextWrapping.Wrap;
        _box.SetResourceReference(Control.ForegroundProperty, "FgBrush");
        _box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "AccentBrush");
        _box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, "AccentBrush");
        _box.IsReadOnly = true;
        _box.TextChanged += (_, _) => { if (_loaded) UpdateStatus(); };
        _box.SelectionChanged += (_, _) => { if (_loaded) UpdateStatus(); };
        _box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { _ = SaveAsync(); e.Handled = true; }
            else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { _ = FindAsync(); e.Handled = true; }
            else if (e.Key == Key.Escape && _editing) { SetEditing(false); e.Handled = true; }
        };
        _box.PreviewMouseDown += (_, _) => { if (!_editing && _loaded) SetEditing(true); };
        _box.PreviewTouchDown += (_, _) => { if (!_editing && _loaded) SetEditing(true); };

        var paper = new Border { CornerRadius = new CornerRadius(14), Child = _box, ClipToBounds = true };
        paper.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var footer = new DockPanel { Margin = new Thickness(6, 10, 6, 0) };
        DockPanel.SetDock(_mode, Dock.Right);
        footer.Children.Add(_mode);
        footer.Children.Add(_status);
        var body = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        body.Children.Add(footer);
        body.Children.Add(paper);
        SetPage(Path.GetFileName(path), body,
            "{Select}  Edit        X  Save        Y  More        LT / RT  Text size        {Back}  Close", wide: true);
    }

    public override void FocusDefault() => _box.Focus();

    public override async void OnShown()
    {
        _box.Focus();
        try
        {
            var info = new FileInfo(_path);
            if (info.Length > MaxBytes)
            {
                _box.Text = $"This file is {info.Length / (1 << 20)} MB, too big to open here. Press Y to open it with Windows.";
                _mode.Text = "Too big";
                return;
            }
            var (text, encoding) = await Task.Run(() =>
            {
                using var reader = new StreamReader(_path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
                var t = reader.ReadToEnd();
                return (t, reader.CurrentEncoding);
            });
            _encoding = encoding;
            _box.Text = text;
            _saved = text;
            _box.CaretIndex = 0;
            _loaded = true;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _box.Text = "This file couldn't be opened: " + ex.Message;
            _mode.Text = "Can't open";
        }
    }

    private bool Dirty => _loaded && _box.Text != _saved;

    private void SetEditing(bool on)
    {
        _editing = on;
        _box.IsReadOnly = !on;
        _box.Focus();
        UpdateStatus();
        if (on && !Hints.Keyboard) _ = TouchKeyboard.ShowAsync();
    }

    private void UpdateStatus()
    {
        int line = _box.GetLineIndexFromCharacterIndex(_box.CaretIndex) + 1;
        int lines = Math.Max(1, _box.LineCount);
        int words = _box.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        _status.Text = $"Line {Math.Max(1, line)} of {lines:N0}   ·   {words:N0} words   ·   {_box.Text.Length:N0} characters   ·   {EncodingName()}" +
                       (_box.TextWrapping == TextWrapping.Wrap ? "   ·   Wrapping" : "");
        _mode.Text = (_editing ? "Editing" : "Reading") + (Dirty ? "  ·  Not saved" : "");
    }

    private string EncodingName() => _encoding switch
    {
        UTF8Encoding u => u.GetPreamble().Length > 0 ? "UTF-8 with BOM" : "UTF-8",
        UnicodeEncoding => "UTF-16",
        _ => _encoding.WebName.ToUpperInvariant()
    };

    private async Task<bool> SaveAsync()
    {
        if (!_loaded) return false;
        try
        {
            string text = _box.Text;
            await Task.Run(() => File.WriteAllText(_path, text, _encoding));
            _saved = text;
            UpdateStatus();
            UiSound.Select();
            return true;
        }
        catch (Exception ex)
        {
            await Host.AskAsync("Couldn't save", ex.Message + (ex is UnauthorizedAccessException ? "\n\nThis file may be read-only or need administrator rights." : ""), "OK");
            return false;
        }
    }

    private async Task FindAsync()
    {
        var word = await Host.PromptAsync("Find", "Finds the next place the text appears, after the cursor.", _box.SelectedText, "Find");
        if (string.IsNullOrEmpty(word)) return;
        int from = _box.SelectionStart + _box.SelectionLength;
        int at = _box.Text.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);
        if (at < 0) at = _box.Text.IndexOf(word, 0, StringComparison.OrdinalIgnoreCase);
        if (at < 0) { await Host.AskAsync("Not found", $"\"{word}\" isn't in this file.", "OK"); return; }
        _box.Focus();
        _box.Select(at, word.Length);
        int lineIndex = _box.GetLineIndexFromCharacterIndex(at);
        _box.ScrollToLine(Math.Max(0, lineIndex - 3));
    }

    private async Task MenuAsync()
    {
        var options = new List<(string Label, string Glyph, Func<Task> Run)>
        {
            ("Save", "", async () => await SaveAsync()),
            ("Find…", "", FindAsync),
            (_box.TextWrapping == TextWrapping.Wrap ? "Don't wrap long lines" : "Wrap long lines", "", () =>
            {
                _box.TextWrapping = _box.TextWrapping == TextWrapping.Wrap ? TextWrapping.NoWrap : TextWrapping.Wrap;
                UpdateStatus();
                return Task.CompletedTask;
            }),
            ("Go to the top", "", () => { _box.CaretIndex = 0; _box.ScrollToHome(); return Task.CompletedTask; }),
            ("Go to the end", "", () => { _box.CaretIndex = _box.Text.Length; _box.ScrollToEnd(); return Task.CompletedTask; }),
            ("Undo", "", () => { if (_box.CanUndo) _box.Undo(); return Task.CompletedTask; }),
            ("Open with Windows", "", () => { ProcessUtil.Open(_path); return Task.CompletedTask; }),
        };
        if (Dirty) options.Insert(1, ("Undo all changes", "", () => { _box.Text = _saved; UpdateStatus(); return Task.CompletedTask; }));
        int pick = await Host.AskWithIconsAsync(Path.GetFileName(_path), null, options.Select(o => (o.Label, o.Glyph)).ToArray());
        if (pick >= 0 && pick < options.Count) await options[pick].Run();
    }

    private async Task CloseAsync()
    {
        if (_closing) return;
        if (Dirty)
        {
            int choice = await Host.AskAsync("Save your changes?", Path.GetFileName(_path) + " has changes that aren't saved.", "Save", "Don't save", "Cancel");
            if (choice == 0 && !await SaveAsync()) return;
            if (choice is < 0 or 2) return;
        }
        _closing = true;
        Host.Pop(this);
    }

    private void Resize(double by)
    {
        _size = Math.Clamp(_size + by, 11, 40);
        _box.FontSize = _size;
    }

    public override bool OnRightStick(double y)
    {
        _box.ScrollToVerticalOffset(_box.VerticalOffset + y * 40);
        return true;
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Back:
                if (_editing) { SetEditing(false); return true; }
                _ = CloseAsync();
                return true;
            case NavAction.X: _ = SaveAsync(); return true;
            case NavAction.Y: _ = MenuAsync(); return true;
            case NavAction.LT: Resize(-2); return true;
            case NavAction.RT: Resize(2); return true;
        }
        if (!_editing)
        {
            switch (action)
            {
                case NavAction.Accept when _loaded: SetEditing(true); return true;
                case NavAction.Up: _box.LineUp(); _box.LineUp(); _box.LineUp(); return true;
                case NavAction.Down: _box.LineDown(); _box.LineDown(); _box.LineDown(); return true;
                case NavAction.LB: _box.PageUp(); return true;
                case NavAction.RB: _box.PageDown(); return true;
                case NavAction.Left: _box.LineLeft(); return true;
                case NavAction.Right: _box.LineRight(); return true;
            }
            return false;
        }
        // Editing: the d-pad moves the cursor (a keyboard does that itself).
        switch (action)
        {
            case NavAction.Up: EditingCommands.MoveUpByLine.Execute(null, _box); return true;
            case NavAction.Down: EditingCommands.MoveDownByLine.Execute(null, _box); return true;
            case NavAction.Left: EditingCommands.MoveLeftByCharacter.Execute(null, _box); return true;
            case NavAction.Right: EditingCommands.MoveRightByCharacter.Execute(null, _box); return true;
            case NavAction.LB: EditingCommands.MoveUpByPage.Execute(null, _box); return true;
            case NavAction.RB: EditingCommands.MoveDownByPage.Execute(null, _box); return true;
            case NavAction.Accept when Hints.Keyboard:
                // Enter on a keyboard: a new line.
                int caret = _box.CaretIndex;
                _box.SelectedText = Environment.NewLine;
                _box.CaretIndex = caret + Environment.NewLine.Length;
                _box.SelectionLength = 0;
                return true;
            case NavAction.Accept: _ = TouchKeyboard.ShowAsync(); return true;
        }
        return false;
    }
}
