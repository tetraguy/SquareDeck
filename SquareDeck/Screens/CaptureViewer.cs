using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualBasic.FileIO;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// A capture opened from the Captures page, full screen: LB / RB move through the screenshots and clips of the view it
/// was opened from (newest first, as on the page), with Share and Delete.
/// </summary>
public sealed class CaptureViewerScreen : Screen
{
    private readonly List<string> _paths;
    private readonly Grid _stage = new();
    private readonly TextBlock _title = new() { Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _detail = new() { Foreground = Brushes.White, Opacity = 0.7, FontSize = 14 };
    private readonly TextBlock _counter = new() { Foreground = Brushes.White, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0), MinWidth = 70, TextAlignment = TextAlignment.Center };
    private readonly Button _play = Ui.PanelButton("Pause");
    private readonly Button _share = Ui.PanelButton("Share");
    private readonly Button _previous = Ui.PanelButton("‹  LB"), _next = Ui.PanelButton("RB  ›");
    private MediaElement? _video;
    private bool _playing;
    private int _index;

    /// <summary>Set when something was deleted, so the page behind reloads.</summary>
    public bool Changed { get; private set; }

    private string Current => _paths[_index];

    public CaptureViewerScreen(IReadOnlyList<string> paths, int index)
    {
        _paths = paths.ToList();
        _index = Math.Clamp(index, 0, Math.Max(0, _paths.Count - 1));

        var root = new Grid { Background = Brushes.Black };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel { Margin = new Thickness(40, 16, 40, 10) };
        head.Children.Add(_title);
        head.Children.Add(_detail);
        root.Children.Add(head);
        Grid.SetRow(_stage, 1);
        root.Children.Add(_stage);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 8) };
        void Add(UIElement e, double gap = 6)
        {
            if (e is FrameworkElement fe) fe.Margin = new Thickness(gap, 0, gap, 0);
            bar.Children.Add(e);
        }
        _previous.Click += (_, _) => Step(-1);
        _next.Click += (_, _) => Step(+1);
        _play.Click += (_, _) => TogglePlay();
        _share.Click += (_, _) => Share();
        var delete = Ui.PanelButton("Delete");
        delete.Click += (_, _) => _ = DeleteAsync();
        var back = Ui.PanelButton("Back");
        back.Click += (_, _) => Host.Pop(this);
        Add(_previous);
        bar.Children.Add(_counter);
        Add(_next);
        Add(_play, 14);
        Add(_share);
        Add(delete);
        Add(back);
        var bottom = new StackPanel();
        bottom.Children.Add(bar);
        var hints = new HintBar("{LB} {RB}  Previous / next      {Options}  Share      {Details}  Delete      {Back}  Back")
            { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12), Opacity = 0.85 };
        bottom.Children.Add(hints);
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);
        Content = root;
        Show();
    }

    public override void FocusDefault() => _share.Focus();

    public override void OnResumed()
    {
        // Back from the share page or a question: carry on playing.
        if (_video != null && _playing) _video.Play();
    }

    public override void OnClosed() => CloseVideo();

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.RB: Step(+1); return true;
            case NavAction.LB: Step(-1); return true;
            case NavAction.Y: Share(); return true;
            case NavAction.X: _ = DeleteAsync(); return true;
        }
        return false;
    }

    /// <summary>The next (+1) or previous (-1) capture, wrapping around at the ends.</summary>
    private void Step(int delta)
    {
        if (_paths.Count < 2) return;
        _index = ((_index + delta) % _paths.Count + _paths.Count) % _paths.Count;
        UiSound.Move();
        Show();
    }

    private async void Show()
    {
        CloseVideo();
        _stage.Children.Clear();
        if (_paths.Count == 0) return;
        int showing = _index;
        string path = Current;
        bool many = _paths.Count > 1;
        _previous.Visibility = _next.Visibility = _counter.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        _counter.Text = $"{showing + 1} / {_paths.Count}";
        _title.Text = CapturesScreen.GameOf(path);
        string when = "";
        try { when = File.GetLastWriteTime(path).ToString("dddd, MMMM d, yyyy  h:mm tt"); } catch (Exception) { }
        _detail.Text = (CapturesScreen.IsVideo(path) ? "Clip" : "Screenshot") + (when.Length > 0 ? "  ·  " + when : "");

        if (CapturesScreen.IsVideo(path))
        {
            _play.Visibility = Visibility.Visible;
            var video = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform, Source = new Uri(path) };
            video.MediaEnded += (_, _) => { video.Stop(); SetPlaying(false); };
            _video = video;
            _stage.Children.Add(video);
            video.Play();
            SetPlaying(true);
        }
        else
        {
            _play.Visibility = Visibility.Collapsed;
            var image = new Image { Stretch = Stretch.Uniform };
            _stage.Children.Add(image);
            var source = await Task.Run(() => (ImageSource?)ImageLoader.FromFile(path, 1920));
            if (showing == _index) image.Source = source; // ignore a slow load we have moved past
        }
    }

    private void Share()
    {
        if (_paths.Count == 0) return;
        if (_video != null && _playing) _video.Pause();
        Host.Push(new ShareScreen(Current));
    }

    private async Task DeleteAsync()
    {
        if (_paths.Count == 0) return;
        string path = Current;
        if (await Host.AskAsync("Delete this capture?", Path.GetFileName(path) + "\nIt will be moved to the Recycle Bin.", "Delete", "Cancel") != 0) return;
        CloseVideo();
        _stage.Children.Clear();
        try { FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); }
        catch (Exception ex)
        {
            await Host.AskAsync("Couldn't delete the file", ex.Message, "OK");
            Show();
            return;
        }
        Changed = true;
        _paths.RemoveAt(_index);
        if (_paths.Count == 0) { Host.Pop(this); return; }
        _index = Math.Min(_index, _paths.Count - 1); // the one that followed it
        Show();
    }

    private void CloseVideo()
    {
        if (_video == null) return;
        try { _video.Stop(); _video.Close(); } catch (Exception) { }
        _video = null;
        _playing = false;
    }

    private void TogglePlay()
    {
        if (_video == null) return;
        if (_playing) _video.Pause(); else _video.Play();
        SetPlaying(!_playing);
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        _play.Content = playing ? "Pause" : "Play";
    }
}

/// <summary>
/// SquareDeck's own share sheet for a capture: the file at the top, then tiles for everything that can be done with it
/// from here (copy it, open or edit it, save a copy to a folder, OneDrive or a USB drive).
/// </summary>
public sealed class ShareScreen : Screen
{
    private readonly string _path;
    private readonly TextBlock _status = Ui.Text("", 15, "AccentBrush", bold: true);
    private Button? _first;

    public override bool IsDialog => true;

    public ShareScreen(string path)
    {
        _path = path;
        bool video = CapturesScreen.IsVideo(path);
        var panel = new StackPanel();

        // Header: "Share" and a close button.
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var close = new Button { Style = Ui.StyleOf("PillButton"), Content = Icon("", 14), Padding = new Thickness(0), Width = 40, Height = 40, Focusable = false };
        close.Click += (_, _) => Host.Pop(this);
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var shareIcon = Icon("", 20);
        shareIcon.Margin = new Thickness(0, 0, 12, 0);
        title.Children.Add(shareIcon);
        title.Children.Add(Ui.Text("Share", 22, "FgBrush", bold: true));
        head.Children.Add(title);
        panel.Children.Add(head);

        // The file: picture, name, size.
        var file = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 6) };
        file.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        var fileRow = new DockPanel();
        var thumb = new Border { Width = 96, Height = 54, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 14, 0), Background = Brushes.Black };
        if (!video && ImageLoader.FromFile(path, 240) is { } small) thumb.Background = new ImageBrush(small) { Stretch = Stretch.UniformToFill };
        else thumb.Child = Icon("", 22);
        fileRow.Children.Add(thumb);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text(Path.GetFileName(path), 16, "FgBrush", bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(name);
        words.Children.Add(Ui.Text(Describe(path, video), 13, "SubtleBrush"));
        fileRow.Children.Add(words);
        file.Child = fileRow;
        panel.Children.Add(file);

        // Copy: for pasting into a chat, an email or a document.
        panel.Children.Add(Section("Copy"));
        var copy = new WrapPanel();
        if (!video) copy.Children.Add(Tile("", "#3B82F6", "Copy picture", () => Do("Picture copied. Paste it anywhere.", CopyPicture)));
        copy.Children.Add(Tile("", "#6366F1", "Copy file", () => Do("File copied. Paste it into a chat, email or folder.", CopyFile)));
        copy.Children.Add(Tile("", "#64748B", "Copy path", () => Do("Path copied.", () => Clipboard.SetText(_path))));
        panel.Children.Add(copy);

        panel.Children.Add(Section("Share using"));
        var apps = new WrapPanel();
        apps.Children.Add(Tile(video ? "" : "", "#0EA5E9", video ? "Play in Windows" : "Photos", () => Leave(() => ProcessUtil.Open(_path))));
        if (!video) apps.Children.Add(Tile("", "#F59E0B", "Edit in Paint", () => Leave(() => ProcessUtil.Open("mspaint.exe", "\"" + _path + "\""))));
        apps.Children.Add(Tile("", "#EAB308", "Show in folder", () => Leave(() => ProcessUtil.Open("explorer.exe", "/select,\"" + _path + "\""))));
        string oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
        if (oneDrive.Length > 0 && Directory.Exists(oneDrive))
            apps.Children.Add(Tile("", "#2563EB", "OneDrive", () => SaveTo(Path.Combine(oneDrive, "Pictures", "SquareDeck"), "OneDrive > Pictures > SquareDeck. It uploads by itself.")));
        apps.Children.Add(Tile("", "#10B981", "Desktop", () => SaveTo(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "your desktop")));
        apps.Children.Add(Tile("", "#EC4899", video ? "Videos" : "Pictures",
            () => SaveTo(Environment.GetFolderPath(video ? Environment.SpecialFolder.MyVideos : Environment.SpecialFolder.MyPictures), video ? "Videos" : "Pictures")));
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).Take(3))
            {
                string root = drive.RootDirectory.FullName, label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "USB drive" : drive.VolumeLabel;
                apps.Children.Add(Tile("", "#8B5CF6", $"{label} ({root.TrimEnd('\\')})", () => SaveTo(Path.Combine(root, "SquareDeck"), label)));
            }
        }
        catch (Exception) { }
        panel.Children.Add(apps);

        _status.Margin = new Thickness(4, 12, 0, 0);
        _status.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_status);

        var card = new Border
        {
            Width = 640, Padding = new Thickness(28, 24, 28, 24), CornerRadius = new CornerRadius(20), Child = panel,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45 }
        };
        card.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        card.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)) };
        backdrop.Children.Add(card);
        Content = backdrop;
    }

    public override void FocusDefault() => _first?.Focus();

    private static TextBlock Icon(string glyph, double size)
    {
        var t = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        return t;
    }

    private static TextBlock Section(string text)
    {
        var t = Ui.Text(text, 15, "FgBrush", bold: true);
        t.Margin = new Thickness(2, 14, 0, 8);
        return t;
    }

    private static string Describe(string path, bool video)
    {
        try
        {
            long bytes = new FileInfo(path).Length;
            string size = bytes >= 1 << 20 ? $"{bytes / 1048576.0:0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";
            return (video ? "Clip" : "Screenshot") + "  ·  " + size;
        }
        catch (Exception) { return video ? "Clip" : "Screenshot"; }
    }

    /// <summary>One target: an icon on a colored square with its name underneath.</summary>
    private Button Tile(string glyph, string color, string label, Action click)
    {
        var square = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
        };
        square.Child = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text(label, 13);
        name.TextAlignment = TextAlignment.Center;
        name.TextWrapping = TextWrapping.Wrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxHeight = 36;
        name.Margin = new Thickness(0, 8, 0, 0);
        var stack = new StackPanel { Width = 104, Height = 100 };
        stack.Children.Add(square);
        stack.Children.Add(name);
        var b = Ui.Tile(stack);
        b.Margin = new Thickness(0, 0, 4, 4);
        b.Click += (_, _) => click();
        _first ??= b;
        return b;
    }

    /// <summary>Does something that stays on this page, and says how it went.</summary>
    private void Do(string done, Action action)
    {
        try
        {
            action();
            _status.Text = "✓  " + done;
            UiSound.Select();
        }
        catch (Exception ex)
        {
            _status.Text = "Couldn't do that: " + ex.Message;
            UiSound.Error();
        }
    }

    /// <summary>Opens another program with the file, and closes the sheet.</summary>
    private void Leave(Func<bool> open)
    {
        if (open()) Host.Pop(this);
        else { _status.Text = "That couldn't be opened on this PC."; UiSound.Error(); }
    }

    private void CopyPicture()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // the file isn't kept open
        image.UriSource = new Uri(_path);
        image.EndInit();
        image.Freeze();
        Clipboard.SetImage(image);
    }

    private void CopyFile() => Clipboard.SetFileDropList(new StringCollection { _path });

    private void SaveTo(string folder, string where) => Do("Saved a copy to " + where + (where.EndsWith('.') ? "" : "."), () =>
    {
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, Path.GetFileName(_path));
        for (int n = 2; File.Exists(target); n++)
            target = Path.Combine(folder, Path.GetFileNameWithoutExtension(_path) + $" ({n})" + Path.GetExtension(_path));
        File.Copy(_path, target);
    });
}
