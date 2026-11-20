using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Screenshots and clips from the user's Videos\Captures folder.</summary>
/// <summary>Screenshots of one game: from Windows' Captures folder (Game Bar names them after the game) and Steam's own.</summary>
public static class GameCaptures
{
    private static string Simple(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    public static List<string> For(Game game, int max = 40)
    {
        var found = new List<FileInfo>();
        string name = Simple(game.Name), original = Simple(game.OriginalName);
        try
        {
            if (Directory.Exists(CapturesScreen.Folder))
                foreach (var f in new DirectoryInfo(CapturesScreen.Folder).EnumerateFiles())
                {
                    if (CapturesScreen.IsVideo(f.Name) || !CapturesScreen.IsImage(f.Name)) continue;
                    string of = Simple(CapturesScreen.GameOf(f.Name));
                    if (of.Length > 0 && (of == name || of == original || (of.Length > 5 && (name.StartsWith(of) || of.StartsWith(name))))) found.Add(f);
                }
        }
        catch (Exception) { }
        // Steam's screenshots (F12): userdata/<account>/760/remote/<app id>/screenshots.
        if (game.IsSteam && SteamLibrary.FindSteamPath() is { } steam)
        {
            try
            {
                var userdata = Path.Combine(steam, "userdata");
                if (Directory.Exists(userdata))
                    foreach (var account in Directory.EnumerateDirectories(userdata))
                    {
                        var dir = Path.Combine(account, "760", "remote", game.AppId.ToString(), "screenshots");
                        if (Directory.Exists(dir))
                            found.AddRange(new DirectoryInfo(dir).EnumerateFiles("*.jpg").Concat(new DirectoryInfo(dir).EnumerateFiles("*.png")));
                    }
            }
            catch (Exception) { }
        }
        return found.OrderByDescending(f => f.LastWriteTime).Take(max).Select(f => f.FullName).ToList();
    }
}

public sealed class CapturesScreen : GridScreen
{
    private static readonly string[] ImageTypes = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
    private static readonly string[] VideoTypes = { ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".webm" };
    private const int MaxItems = 300;
    // Three across at 280 wide is the original size; more per row share the same width (each tile also takes ~16 of margin).
    private int PerRow => Math.Clamp(Host.Settings.CapturesPerRow, 3, 6);
    private double TileWidth => Math.Floor(3 * (280 + 16) / (double)PerRow - 16);
    private double TileHeight => Math.Round(TileWidth * 158 / 280);

    private readonly TextBlock _note;
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
    private readonly StackPanel _days = new();
    private CancellationTokenSource _cts = new();
    private string _filter = "all"; // all | images | videos | albums
    private string? _album;          // the game whose album is open, in the albums view
    private bool _started;
    private string? _focusPath;

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Captures");

    public static bool IsVideo(string path) =>
        VideoTypes.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsImage(string path) =>
        ImageTypes.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public CapturesScreen() : base("Captures", "{Select}  Open        {Details}  Delete / album options        {Back}  Back")
    {
        _note = Ui.Text("", 16, "SubtleBrush");
        _note.TextWrapping = TextWrapping.Wrap;
        _note.Margin = new Thickness(6, 10, 0, 0);
        // Filters on top, then one row of pictures per day.
        Body.Children.Clear();
        Body.Children.Add(_filters);
        Body.Children.Add(_note);
        Body.Children.Add(_days);
    }

    public override void OnShown()
    {
        if (!_started && Host.Settings.CaptureAlbums) _filter = "albums";
        _started = true;
        Reload();
    }

    public override void OnClosed() => _cts.Cancel();

    /// <summary>
    /// The game a capture came from, from its file name. Windows names them "Game 2026-11-14 10-30-12.png"
    /// (older versions "Game 11_14_2026 10_30_12 AM.png"); anything else shows the file name.
    /// </summary>
    public static string GameOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var match = System.Text.RegularExpressions.Regex.Match(name, @"^(.*?)\s+(\d{4}-\d{2}-\d{2}|\d{1,2}_\d{1,2}_\d{4})");
        var game = match.Success ? match.Groups[1].Value.Trim() : name;
        return game.Length > 0 ? game : name;
    }

    private void Reload()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Tiles.Children.Clear();
        _days.Children.Clear();

        List<FileInfo> all;
        try
        {
            all = Directory.Exists(Folder)
                ? new DirectoryInfo(Folder).EnumerateFiles()
                    .Where(f => ImageTypes.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) || IsVideo(f.Name))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(MaxItems)
                    .ToList()
                : new List<FileInfo>();
        }
        catch (Exception)
        {
            all = new List<FileInfo>();
        }

        int videoCount = all.Count(f => IsVideo(f.Name));
        BuildFilters(all.Count, all.Count - videoCount, videoCount, all.Select(f => GameOf(f.Name)).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        if (_filter == "albums" && _album == null)
        {
            BuildAlbums(all, ct);
            return;
        }
        var files = all.Where(f => _filter switch
        {
            "albums" => string.Equals(GameOf(f.Name), _album, StringComparison.OrdinalIgnoreCase),
            "all" => true,
            _ => (_filter == "videos") == IsVideo(f.Name)
        }).ToList();

        _note.Text = all.Count == 0
            ? "No captures in " + Folder + ".\nPress Win+Alt+PrtScn for a screenshot or Win+Alt+R to record a clip."
            : files.Count == 0 ? "Nothing of this kind yet."
            : _album != null ? $"Album: {_album}  \u00B7  {files.Count} {(files.Count == 1 ? "capture" : "captures")}  \u00B7  {Hints.KeyLabel("Back")} for all albums" : "";
        _note.Visibility = _note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var pending = new List<(Image View, string Path)>();
        Button? focus = null;
        foreach (var day in files.GroupBy(f => f.LastWriteTime.Date))
        {
            string title = day.Key == DateTime.Today ? "Today"
                : day.Key == DateTime.Today.AddDays(-1) ? "Yesterday"
                : day.Key.Year == DateTime.Today.Year ? day.Key.ToString("dddd, MMMM d") : day.Key.ToString("MMMM d, yyyy");
            var header = new DockPanel { Margin = new Thickness(4, 18, 12, 10), LastChildFill = true };
            var count = Ui.Text(day.Count() == 1 ? "1 capture" : day.Count() + " captures", 14, "SubtleBrush");
            count.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(count, Dock.Right);
            header.Children.Add(count);
            header.Children.Add(Ui.Text(title, 20, "FgBrush", bold: true));
            _days.Children.Add(header);

            var row = new WrapPanel();
            _days.Children.Add(row);
            foreach (var file in day)
            {
                var path = file.FullName;
                var box = ImageBox(TileWidth, TileHeight, out var view);
                var face = (Grid)box.Child;
                // A soft shade at the bottom so the badges read on any picture.
                var shade = new LinearGradientBrush { StartPoint = new Point(0.5, 0.55), EndPoint = new Point(0.5, 1) };
                shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
                shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x99, 0, 0, 0), 1));
                face.Children.Add(new Border { Background = shade, IsHitTestVisible = false });
                var tag = new Border
                {
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 10, 4), Margin = new Thickness(10),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                    Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
                    Child = IconText(IsVideo(path) ? "\uE768" : "\uE722", IsVideo(path) ? "Video" : file.LastWriteTime.ToString("h:mm tt"), 13, Brushes.White)
                };
                face.Children.Add(tag);

                var caption = GameOf(path) + "  \u00B7  " + file.LastWriteTime.ToString("h:mm tt");
                var tile = AddTile(box, caption, TileWidth, () => Open(path), path, row);
                tile.GotKeyboardFocus += (_, _) => _focusPath = path;
                if (path == _focusPath) focus = tile;
                pending.Add((view, path));
            }
        }

        Task.Run(() =>
        {
            foreach (var (view, path) in pending)
            {
                if (ct.IsCancellationRequested) return;
                // The shell already knows how to thumbnail videos; images we decode ourselves.
                ImageSource? source = IsVideo(path) ? ShellThumb.Get(path, 400) : ImageLoader.FromFile(path, 400);
                if (source != null) Dispatcher.BeginInvoke(new Action(() => view.Source = source));
            }
        }, ct);

        if (focus != null) FocusLater(focus);
        else FocusLater(_days.FindDescendants<Button>().FirstOrDefault() ?? _filters.Children.OfType<Button>().FirstOrDefault());
    }

    /// <summary>
    /// A symbol and a label side by side. The symbol uses the icon font and the label the normal font; one
    /// font for both shows boxes, because the icon font has no letters.
    /// </summary>
    private static StackPanel IconText(string glyph, string label, double size, Brush? color)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) };
        var text = new TextBlock { Text = label, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        if (color != null) { icon.Foreground = color; text.Foreground = color; }
        row.Children.Add(icon);
        row.Children.Add(text);
        return row;
    }

    /// <summary>One album per game, named from the capture file names, newest first; its cover is the latest capture.</summary>
    private void BuildAlbums(List<FileInfo> all, CancellationToken ct)
    {
        _note.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0) return;
        var panel = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _days.Children.Add(panel);
        var pending = new List<(Image View, string Path)>();
        Button? focus = null;
        foreach (var album in all.GroupBy(f => GameOf(f.Name), StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Max(f => f.LastWriteTime)))
        {
            var name = album.Key;
            var latest = album.First();
            int count = album.Count();
            var box = ImageBox(TileWidth, TileHeight, out var view);
            // A stack effect: two cards peeking out behind the cover.
            var stack = new Grid { Width = TileWidth, Height = TileHeight + 12 };
            for (int i = 2; i >= 1; i--)
            {
                var back = new Border
                {
                    Width = TileWidth - i * 20, Height = TileHeight, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 0, 0), Opacity = 0.35 + 0.2 * (2 - i), RenderTransform = new TranslateTransform(0, 12 - i * 6)
                };
                back.SetResourceReference(Border.BackgroundProperty, "SubtleBrush");
                stack.Children.Add(back);
            }
            box.VerticalAlignment = VerticalAlignment.Bottom;
            stack.Children.Add(box);
            var tile = AddTile(stack, $"{name}  \u00B7  {count}", TileWidth, () =>
            {
                _album = name;
                _focusPath = null;
                Reload();
            }, "album:" + name, panel);
            tile.GotKeyboardFocus += (_, _) => _focusPath = "album:" + name;
            if (_focusPath == "album:" + name) focus = tile;
            if (Host.Settings.CaptureAlbumIcons) AddAlbumIcon(tile, name);
            pending.Add((view, latest.FullName));
        }
        Task.Run(() =>
        {
            foreach (var (view, path) in pending)
            {
                if (ct.IsCancellationRequested) return;
                ImageSource? source = IsVideo(path) ? ShellThumb.Get(path, 400) : ImageLoader.FromFile(path, 400);
                if (source != null) Dispatcher.BeginInvoke(new Action(() => view.Source = source));
            }
        }, ct);
        FocusLater(focus ?? panel.Children.OfType<Button>().FirstOrDefault());
    }

    /// <summary>The game's own small picture (its icon, else its cover) before an album's name, when it's in the library.</summary>
    private void AddAlbumIcon(Button tile, string album)
    {
        string key = new string(album.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (key.Length == 0) return;
        var game = Host.AllGames.FirstOrDefault(g => new string(g.Name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() == key)
                   ?? Host.AllGames.FirstOrDefault(g => new string(g.OriginalName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() == key);
        if (game?.Cover == null || tile.Content is not StackPanel stack || stack.Children.Count < 2 || stack.Children[^1] is not TextBlock caption) return;
        stack.Children.Remove(caption);
        var icon = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner * 0.5, 5)), Margin = new Thickness(0, 0, 8, 0),
            Background = new ImageBrush(game.Cover) { Stretch = game.UsesIcon && !game.IconFills ? Stretch.Uniform : Stretch.UniformToFill },
            VerticalAlignment = VerticalAlignment.Center
        };
        if (game.UsesIcon && !game.IconFills && game.TileBackground != null)
            icon.Child = new Border { Background = new ImageBrush(game.Cover) { Stretch = Stretch.Uniform }, Margin = new Thickness(3) };
        if (icon.Child != null) icon.Background = game.TileBackground;
        caption.Margin = new Thickness(0);
        caption.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel { Margin = new Thickness(2, 8, 2, 0), LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(caption);
        stack.Children.Add(row);
    }

    private void BuildFilters(int all, int images, int videos, int albums)
    {
        _filters.Children.Clear();
        void Filter(string id, string label, int count)
        {
            var b = new Button { Style = Ui.StyleOf("PillButton"), Content = $"{label}  {count}", Margin = new Thickness(0, 0, 10, 0) };
            if (_filter == id) b.SetResourceReference(ForegroundProperty, "AccentBrush");
            b.Click += (_, _) =>
            {
                _filter = id;
                _album = null;
                _focusPath = null;
                Reload();
            };
            _filters.Children.Add(b);
        }
        Filter("all", "All", all);
        Filter("images", "Screenshots", images);
        Filter("videos", "Videos", videos);
        Filter("albums", "Albums", albums);
        // Size: how many captures across, from 3 (big) to 6 (small). Each press steps to the next.
        var size = new Button { Style = Ui.StyleOf("PillButton"), Content = IconText("\uE80A", $"{PerRow} per row", 16, null), Margin = new Thickness(10, 0, 0, 0) };
        size.Click += (_, _) =>
        {
            Host.Settings.CapturesPerRow = PerRow >= 6 ? 3 : PerRow + 1;
            Host.Settings.Save();
            Reload();
            FocusLater(_filters.Children.Count > 4 ? _filters.Children[4] : null); // stays on this button
        };
        _filters.Children.Add(size);
        var open = new Button { Style = Ui.StyleOf("PillButton"), Content = IconText("\uE8B7", "Open folder", 16, null), Margin = new Thickness(10, 0, 0, 0) };
        open.Click += (_, _) => ProcessUtil.Open(Directory.Exists(Folder) ? Folder : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        _filters.Children.Add(open);

        // Inside an album: export it or delete it.
        if (_album is string album)
        {
            var zip = new Button { Style = Ui.StyleOf("PillButton"), Content = IconText("\uE7B8", "Export as ZIP", 16, null), Margin = new Thickness(10, 0, 0, 0) };
            zip.Click += (_, _) => _ = ExportAlbumAsync(album);
            _filters.Children.Add(zip);
            var delete = new Button { Style = Ui.StyleOf("PillButton"), Content = IconText("\uE74D", "Delete album", 16, null), Margin = new Thickness(10, 0, 0, 0) };
            delete.Click += (_, _) => _ = DeleteAlbumAsync(album);
            _filters.Children.Add(delete);
        }
    }

    /// <summary>Every capture in an album: all of them, not only the newest few hundred the page shows.</summary>
    private static List<FileInfo> AlbumFiles(string album)
    {
        try
        {
            if (!Directory.Exists(Folder)) return new();
            return new DirectoryInfo(Folder).EnumerateFiles()
                .Where(f => (ImageTypes.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) || IsVideo(f.Name)) &&
                            string.Equals(GameOf(f.Name), album, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.LastWriteTime).ToList();
        }
        catch (Exception) { return new(); }
    }

    /// <summary>X on an album: export it as a ZIP, or delete it.</summary>
    private async Task AlbumMenuAsync(string album)
    {
        int count = AlbumFiles(album).Count;
        int choice = await Host.AskAsync(album, $"{count} {(count == 1 ? "capture" : "captures")}", "Export as ZIP", "Delete album", "Cancel");
        if (choice == 0) await ExportAlbumAsync(album);
        else if (choice == 1) await DeleteAlbumAsync(album);
    }

    /// <summary>
    /// Puts every capture of an album in one ZIP in the Downloads folder ("Album name 2026-11-20.zip"). Pictures and
    /// videos are already compressed, so they're stored as they are: quick, and the ZIP is the size of the files.
    /// </summary>
    private async Task ExportAlbumAsync(string album)
    {
        var files = AlbumFiles(album);
        if (files.Count == 0) { await Host.AskAsync("Nothing to export", "This album has no captures left.", "OK"); return; }
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string safe = string.Concat(album.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        string target = Path.Combine(downloads, $"{(safe.Length > 0 ? safe : "Album")} {DateTime.Now:yyyy-MM-dd}.zip");
        for (int n = 2; File.Exists(target); n++)
            target = Path.Combine(downloads, $"{(safe.Length > 0 ? safe : "Album")} {DateTime.Now:yyyy-MM-dd} ({n}).zip");
        long total = files.Sum(f => f.Length);
        Host.ShowToast("Captures", "Exporting " + album, $"{files.Count} captures, {SystemStats.Bytes(total)}", "", "info", remember: false);
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(downloads);
                string temp = target + ".part";
                using (var zip = System.IO.Compression.ZipFile.Open(temp, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in files)
                    {
                        string name = f.Name;
                        for (int n = 2; !used.Add(name); n++) name = Path.GetFileNameWithoutExtension(f.Name) + $" ({n})" + f.Extension;
                        var entry = zip.CreateEntryFromFile(f.FullName, name, System.IO.Compression.CompressionLevel.NoCompression);
                        entry.LastWriteTime = f.LastWriteTime;
                    }
                }
                File.Move(temp, target);
            });
        }
        catch (Exception ex)
        {
            try { if (File.Exists(target + ".part")) File.Delete(target + ".part"); } catch (Exception) { }
            await Host.AskAsync("Couldn't export the album", ex.Message, "OK");
            return;
        }
        int choice = await Host.AskAsync("Album exported", $"{Path.GetFileName(target)}\nSaved in Downloads ({SystemStats.Bytes(new FileInfo(target).Length)}).", "Show it", "OK");
        if (choice == 0) Host.Push(new ExplorerScreen(downloads));
    }

    /// <summary>Moves every capture of an album to the Recycle Bin (after asking).</summary>
    private async Task DeleteAlbumAsync(string album)
    {
        var files = AlbumFiles(album);
        if (files.Count == 0) return;
        int choice = await Host.AskAsync($"Delete the {album} album?",
            $"All {files.Count} {(files.Count == 1 ? "capture" : "captures")} in it will be moved to the Recycle Bin.", "Delete album", "Cancel");
        if (choice != 0) return;
        int failed = await Task.Run(() =>
        {
            int errors = 0;
            foreach (var f in files)
            {
                try { FileSystem.DeleteFile(f.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); }
                catch (Exception) { errors++; }
            }
            return errors;
        });
        if (failed > 0) _ = Host.AskAsync("Some captures weren't deleted", $"{failed} of {files.Count} couldn't be moved to the Recycle Bin (in use, or no permission).", "OK");
        _album = null;
        _focusPath = null;
        Reload();
    }

    private CaptureViewerScreen? _viewer;

    /// <summary>Opens a capture full screen, with everything in the current view to move through with LB / RB.</summary>
    private void Open(string path)
    {
        // The captures on the page right now, in the order they're shown.
        var shown = this.FindDescendants<Button>().Select(b => b.Tag as string)
            .Where(t => t != null && !t.StartsWith("album:") && (IsImage(t) || IsVideo(t)) && File.Exists(t)).Select(t => t!).Distinct().ToList();
        if (!shown.Contains(path)) shown.Insert(0, path);
        _focusPath = path;
        _viewer = new CaptureViewerScreen(shown, shown.IndexOf(path));
        Host.Push(_viewer);
    }

    public override void OnResumed()
    {
        // Back from the viewer: the page is rebuilt if something was deleted there.
        if (_viewer is { Changed: true }) Reload();
        _viewer = null;
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Back && _album != null)
        {
            // Out of an album, back to the list of albums.
            _focusPath = "album:" + _album;
            _album = null;
            Reload();
            return true;
        }
        if (action != NavAction.X) return false;
        if (Keyboard.FocusedElement is Button { Tag: string album } && album.StartsWith("album:")) _ = AlbumMenuAsync(album[6..]);
        else if (Keyboard.FocusedElement is Button { Tag: string path }) _ = DeleteAsync(path);
        return true;
    }

    private async Task DeleteAsync(string path)
    {
        int choice = await Host.AskAsync("Delete this capture?",
            Path.GetFileName(path) + "\nIt will be moved to the Recycle Bin.", "Delete", "Cancel");
        if (choice == 0) Delete(path);
    }

    private void Delete(string path)
    {
        try
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex)
        {
            _ = Host.AskAsync("Couldn't delete the file", ex.Message, "OK");
        }
        Reload();
    }
}

/// <summary>Full-screen viewer for one image or video.</summary>
public sealed class ViewerScreen : Screen
{
    private readonly MediaElement? _video;
    private readonly Button? _playButton;
    private bool _playing;

    private readonly IReadOnlyList<string>? _gallery;
    private readonly Image? _galleryImage;
    private readonly TextBlock? _counter;
    private int _index;

    /// <summary>Shows images from the web (store screenshots). LB / RB or the buttons move between them.</summary>
    public ViewerScreen(IReadOnlyList<string> imageUrls, int startIndex)
    {
        _gallery = imageUrls;
        _index = Math.Clamp(startIndex, 0, Math.Max(0, imageUrls.Count - 1));
        _galleryImage = new Image { Stretch = Stretch.Uniform };
        _counter = new TextBlock { Foreground = Brushes.White, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) };
        Build(_galleryImage, null);
        ShowCurrent();
    }

    /// <summary>Moves to the next (+1) or previous (-1) image, wrapping around at the ends.</summary>
    private void Step(int delta)
    {
        if (_gallery == null || _gallery.Count < 2) return;
        _index = ((_index + delta) % _gallery.Count + _gallery.Count) % _gallery.Count;
        ShowCurrent();
    }

    private async void ShowCurrent()
    {
        if (_gallery == null || _galleryImage == null || _gallery.Count == 0) return;
        int showing = _index;
        if (_counter != null) _counter.Text = $"{showing + 1} / {_gallery.Count}";
        string item = _gallery[showing];
        var source = System.IO.File.Exists(item)
            ? await Task.Run(() => (ImageSource?)ImageLoader.FromFile(item, 1920)) // a screenshot on this PC
            : await Task.Run(() => ImageLoader.FromUrlAsync(item, 0));
        if (showing == _index) _galleryImage.Source = source; // ignore a slow load we have moved past
    }

    public override bool OnAction(NavAction action)
    {
        if (_gallery == null) return false;
        if (action == NavAction.RB) { Step(+1); return true; }
        if (action == NavAction.LB) { Step(-1); return true; }
        return false;
    }

    /// <summary>Plays a video from the web (store trailers).</summary>
    public ViewerScreen(Uri videoUrl)
    {
        _video = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.Uniform,
            Source = videoUrl
        };
        _video.MediaEnded += (_, _) =>
        {
            _video.Stop();
            SetPlaying(false);
        };
        _playButton = Ui.PanelButton("Pause");
        _playButton.Click += (_, _) => TogglePlay();
        Build(_video, null);
    }

    /// <summary>Shows a local image or video, with a Delete button.</summary>
    public ViewerScreen(string path, Action delete)
    {
        if (CapturesScreen.IsVideo(path))
        {
            _video = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform,
                Source = new Uri(path)
            };
            _video.MediaEnded += (_, _) =>
            {
                _video.Stop();
                SetPlaying(false);
            };
            _playButton = Ui.PanelButton("Pause");
            _playButton.Click += (_, _) => TogglePlay();
            Build(_video, delete);
        }
        else
        {
            Build(new Image { Stretch = Stretch.Uniform, Source = ImageLoader.FromFile(path, 0) }, delete);
        }
    }

    private void Build(UIElement content, Action? delete)
    {
        var root = new Grid { Background = Brushes.Black };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(content);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 16)
        };
        void Add(Button b)
        {
            b.Margin = new Thickness(6, 0, 6, 0);
            bar.Children.Add(b);
        }

        if (_gallery is { Count: > 1 })
        {
            var previous = Ui.PanelButton("\u2039  LB");
            previous.Click += (_, _) => Step(-1);
            Add(previous);
            bar.Children.Add(_counter!);
            var next = Ui.PanelButton("RB  \u203A");
            next.Click += (_, _) => Step(+1);
            Add(next);
        }
        if (_playButton != null) Add(_playButton);
        if (delete != null)
        {
            var del = Ui.PanelButton("Delete");
            del.Click += async (_, _) =>
            {
                if (await Host.AskAsync("Delete this capture?", "It will be moved to the Recycle Bin.", "Delete", "Cancel") != 0)
                    return;
                CloseVideo();
                Host.Pop(this);
                delete();
            };
            Add(del);
        }
        var back = Ui.PanelButton("Back");
        back.Click += (_, _) => Host.Pop(this);
        Add(back);

        Grid.SetRow(bar, 1);
        root.Children.Add(bar);
        Content = root;
    }

    public override void OnShown()
    {
        if (_video == null) return;
        _video.Play();
        SetPlaying(true);
    }

    public override void OnClosed() => CloseVideo();

    private void CloseVideo()
    {
        if (_video == null) return;
        try
        {
            _video.Stop();
            _video.Close();
        }
        catch (Exception) { }
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
        if (_playButton != null) _playButton.Content = playing ? "Pause" : "Play";
    }
}

/// <summary>Shows what is playing on the PC and controls it.</summary>
public sealed class MediaScreen : Screen
{
    private static readonly TimeSpan SkipStep = TimeSpan.FromSeconds(10);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Image _art = new() { Stretch = Stretch.UniformToFill };
    private readonly Button _openActive = Ui.PanelButton("Open player");
    private readonly Button _search = Ui.PanelButton("Search music");
    private string _activeApp = "";      // the playing app's id, as Windows reports it
    private string _activeName = "";
    private string? _activeLaunchId;     // its Start-menu id, when it has one
    private static List<InstalledApp>? _appCache;
    private readonly TextBlock _artFallback = Ui.Glyph("", 90, "SubtleBrush");
    private readonly TextBlock _title = Ui.Text("", 32);
    private readonly TextBlock _artist = Ui.Text("", 22, "SubtleBrush");
    private readonly TextBlock _time = Ui.Text("", 18, "SubtleBrush");
    private readonly ProgressBar _progress = new() { Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 22, 0, 6) };
    private readonly Button _playPause = Ui.Round("");
    private string _artKey = "";
    private bool _refreshing;

    private const int BarCount = 46;
    private const double BarMaxHeight = 300;
    private readonly DispatcherTimer _visualTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly List<Border> _bars = new();
    private readonly List<double> _barSpeed = new(), _barPhase = new();
    private double _level, _wave;

    /// <summary>
    /// Each bar follows the overall output level, shaped by its own slow wave so the row ripples.
    /// (It reads loudness, not individual frequencies.)
    /// </summary>
    private void DrawBars()
    {
        _wave += 0.033;
        double peak = Audio.GetPeak();
        // Rise quickly with the beat, fall back more slowly.
        _level += (peak - _level) * (peak > _level ? 0.6 : 0.12);

        for (int i = 0; i < _bars.Count; i++)
        {
            double wave = 0.5 + 0.5 * Math.Sin(_wave * _barSpeed[i] + _barPhase[i]);
            double target = 2 + _level * (0.25 + 0.75 * wave) * BarMaxHeight;
            _bars[i].Height += (target - _bars[i].Height) * 0.4;
        }
    }

    public MediaScreen()
    {
        var artGrid = new Grid();
        artGrid.Children.Add(_artFallback);
        artGrid.Children.Add(_art);
        var artBox = new Border { Width = 340, Height = 340, ClipToBounds = true, Child = artGrid, Cursor = System.Windows.Input.Cursors.Hand };
        artBox.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        // Click or tap the artwork to jump to the app that is playing (or to the list of players if nothing is).
        artBox.MouseLeftButtonUp += (_, _) => { if (_activeApp.Length > 0) OpenActivePlayer(); else Host.Push(new MusicPlayersScreen()); };

        _title.TextWrapping = TextWrapping.Wrap;
        _title.MaxHeight = 130;
        _artist.Margin = new Thickness(0, 8, 0, 0);
        _progress.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        _progress.SetResourceReference(Control.BackgroundProperty, "TileBrush");
        _progress.BorderThickness = new Thickness(0);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 18, 0, 0) };
        buttons.Children.Add(MediaButton("", MediaService.PreviousAsync));
        buttons.Children.Add(MediaButton("", () => MediaService.SeekAsync(-SkipStep)));
        _playPause.Click += async (_, _) => { await MediaService.PlayPauseAsync(); await RefreshAsync(); };
        buttons.Children.Add(_playPause);
        buttons.Children.Add(MediaButton("", () => MediaService.SeekAsync(SkipStep)));
        buttons.Children.Add(MediaButton("", MediaService.NextAsync));

        var info = new StackPanel { Margin = new Thickness(44, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(_title);
        info.Children.Add(_artist);
        info.Children.Add(_progress);
        info.Children.Add(_time);
        info.Children.Add(buttons);

        var openPlayer = Ui.PanelButton("Open player");
        openPlayer.HorizontalAlignment = HorizontalAlignment.Left;
        openPlayer.Margin = new Thickness(0, 16, 0, 0);
        openPlayer.Click += (_, _) => Host.Push(new MusicPlayersScreen());

        // Only while something is playing: straight to the app it is playing in.
        _openActive.Margin = new Thickness(0, 16, 8, 0);
        _openActive.Visibility = Visibility.Collapsed;
        _openActive.Click += (_, _) => OpenActivePlayer();
        var playerButtons = new StackPanel { Orientation = Orientation.Horizontal };
        playerButtons.Children.Add(_openActive);
        playerButtons.Children.Add(openPlayer);
        // Spotify / YouTube Music search, when switched on in Settings.
        _search.Margin = new Thickness(8, 16, 0, 0);
        _search.Click += (_, _) => Host.Push(new MusicSearchScreen());
        playerButtons.Children.Add(_search);
        info.Children.Add(playerButtons);

        var layout = new Grid { VerticalAlignment = VerticalAlignment.Center };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(artBox);
        Grid.SetColumn(info, 1);
        layout.Children.Add(info);

        // Visualizer: a row of bars across the bottom of the page that move with the PC's sound level.
        var bars = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Height = BarMaxHeight,
            Opacity = 0.28,
            IsHitTestVisible = false
        };
        var random = new Random(7);
        for (int i = 0; i < BarCount; i++)
        {
            var bar = new Border
            {
                Width = 16, Height = 2, Margin = new Thickness(4, 0, 4, 0),
                CornerRadius = new CornerRadius(4, 4, 0, 0), VerticalAlignment = VerticalAlignment.Bottom
            };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            bars.Children.Add(bar);
            _bars.Add(bar);
            _barSpeed.Add(1.5 + random.NextDouble() * 4);
            _barPhase.Add(random.NextDouble() * Math.PI * 2);
        }

        var page = new Grid();
        page.Children.Add(bars);
        page.Children.Add(layout);
        _nowPage = page;

        // Four tabs: whatever is playing on the PC, your own music folder, Spotify, and the streaming services.
        var root = new DockPanel();
        DockPanel.SetDock(_tabBar, Dock.Top);
        root.Children.Add(_tabBar);
        root.Children.Add(_tabHost);
        SetPage("Multimedia", root, "LB / RB  Tabs      {Select}  Choose      {Details}  Play / Search      {Options}  More      LT / RT  Previous / next      {Back}  Back");
        _timer.Tick += async (_, _) => await RefreshAsync();
        _visualTimer.Tick += (_, _) => DrawBars();
    }

    private static readonly (string Id, string Name, string Glyph)[] Tabs =
    {
        ("now", "Now playing", "\uE8D6"), ("local", "My music", "\uE8B7"), ("spotify", "Spotify", "\uE93C"), ("streaming", "Streaming", "\uE714")
    };

    private StreamingView? _streaming;
    private ScrollViewer? _streamingScroll;

    /// <summary>The right stick scrolls the Streaming tab's services.</summary>
    public override bool OnRightStick(double x, double y, bool pressed)
    {
        if (_tab == "streaming" && _streamingScroll != null)
        {
            if (y != 0) _streamingScroll.ScrollToVerticalOffset(_streamingScroll.VerticalOffset - y * Math.Abs(y) * 26);
            return y != 0;
        }
        return base.OnRightStick(x, y, pressed);
    }

    private readonly StackPanel _tabBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
    private readonly Grid _tabHost = new();
    private readonly UIElement _nowPage;
    private LocalMusicView? _local;
    private SpotifyView? _spotify;
    private string _tab = "now";

    private void ShowTab(string id, bool focus)
    {
        _tab = id;
        Host.Settings.MusicTab = id;
        Host.Settings.Save();
        _tabBar.Children.Clear();
        foreach (var (tid, name, glyph) in Tabs)
        {
            string t = tid;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Ui.Glyph(glyph, 16, tid == id ? "BgBrush" : "FgBrush");
            g.Margin = new Thickness(0, 0, 10, 0);
            g.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(g);
            row.Children.Add(Ui.Text(name, 17, tid == id ? "BgBrush" : "FgBrush", bold: tid == id));
            var face = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(20, 9, 22, 9), Child = row };
            face.SetResourceReference(Border.BackgroundProperty, tid == id ? "AccentBrush" : "CircleBrush");
            var chip = new Button { Style = Ui.StyleOf("BareButton"), Focusable = false, Content = face, Margin = new Thickness(0, 0, 10, 0) };
            chip.Click += (_, _) => ShowTab(t, focus: true);
            _tabBar.Children.Add(chip);
        }

        _tabHost.Children.Clear();
        SetPageBackdrop(null);
        _artKey = "";
        UIElement? first;
        if (id == "local")
        {
            _local ??= new LocalMusicView(this);
            _tabHost.Children.Add(_local);
            first = _local.FirstFocus;
            _timer.Stop();
            _visualTimer.Stop();
        }
        else if (id == "streaming")
        {
            _streaming ??= new StreamingView(this);
            _streaming.Build(); // shows the service that's open now, if any
            if (_streaming.Parent is ScrollViewer old) old.Content = null; // from the last visit to this tab
            _streamingScroll = new ScrollViewer { Content = _streaming, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
            _tabHost.Children.Add(_streamingScroll);
            first = _streaming.FirstFocus;
            _timer.Stop();
            _visualTimer.Stop();
        }
        else if (id == "spotify")
        {
            _spotify ??= new SpotifyView(this);
            _tabHost.Children.Add(_spotify);
            first = null; // set once it knows whether it's signed in
            _timer.Stop();
            _visualTimer.Stop();
            Dispatcher.BeginInvoke(new Action(() => Host.FocusSoon(_spotify?.FirstFocus)), System.Windows.Threading.DispatcherPriority.Background);
        }
        else
        {
            _tabHost.Children.Add(_nowPage);
            first = _playPause;
            _timer.Start();
            _visualTimer.Start();
            _ = RefreshAsync();
        }
        if (focus && first != null) Host.FocusSoon(first);
    }

    public override bool OnAction(NavAction action)
    {
        int index = Array.FindIndex(Tabs, t => t.Id == _tab);
        if (action == NavAction.RB) { ShowTab(Tabs[(index + 1) % Tabs.Length].Id, focus: true); UiSound.Page(); return true; }
        if (action == NavAction.LB) { ShowTab(Tabs[(index + Tabs.Length - 1) % Tabs.Length].Id, focus: true); UiSound.Page(); return true; }
        if (_tab == "local" && _local != null) return _local.OnAction(action);
        if (_tab == "spotify" && _spotify != null) return _spotify.OnAction(action);
        if (_tab == "streaming") return _streaming?.Move(action) ?? false;
        // Now playing: X play / pause, LT / RT previous / next.
        switch (action)
        {
            case NavAction.X: _ = MediaService.PlayPauseAsync(); return true;
            case NavAction.LT: _ = MediaService.PreviousAsync(); return true;
            case NavAction.RT: _ = MediaService.NextAsync(); return true;
        }
        return false;
    }

    private Button MediaButton(string glyph, Func<Task> action)
    {
        var b = Ui.Round(glyph);
        b.Click += async (_, _) =>
        {
            await action();
            await Task.Delay(250); // give the player a moment to report the new state
            await RefreshAsync();
        };
        return b;
    }

    public override void FocusDefault()
    {
        if (_tab == "local") Host.FocusSoon(_local?.FirstFocus);
        else if (_tab == "spotify") Host.FocusSoon(_spotify?.FirstFocus);
        else _playPause.Focus();
    }

    private bool _shownOnce;

    public override void OnShown()
    {
        _search.Visibility = Host.Settings.MusicServices ? Visibility.Visible : Visibility.Collapsed;
        if (!_shownOnce)
        {
            _shownOnce = true;
            string tab = Host.Settings.MusicTab;
            ShowTab(Tabs.Any(t => t.Id == tab) ? tab : "now", focus: false);
        }
        if (_tab == "now")
        {
            _timer.Start();
            _visualTimer.Start();
            _ = RefreshAsync();
        }
    }

    public override void OnClosed()
    {
        _timer.Stop();
        _visualTimer.Stop();
        _spotify?.Stop();
    }

    /// <summary>Back from a streaming service (kept open, or closed): the Streaming tab shows which.</summary>
    public override void OnResumed()
    {
        if (_tab != "streaming" || _streaming == null) return;
        _streaming.Build();
        Host.FocusSoon(_streaming.FirstFocus);
    }

    /// <summary>Works out a readable name for the app that is playing, and how to bring it forward.</summary>
    private async Task ResolvePlayerAsync(string app)
    {
        _activeApp = app;
        _activeLaunchId = null;
        _activeName = GuessName(app);
        ShowActiveButton();
        if (app.Length == 0) return;
        try
        {
            _appCache ??= await Task.Run(() => InstalledApps.ScanAsync());
            if (_activeApp != app) return; // changed while we were looking
            // Store apps report the same id the Start menu uses; desktop apps report their program name.
            string bare = System.IO.Path.GetFileNameWithoutExtension(app);
            var match = _appCache.FirstOrDefault(a => string.Equals(a.AppId, app, StringComparison.OrdinalIgnoreCase))
                        ?? _appCache.FirstOrDefault(a => a.AppId.EndsWith("\\" + app, StringComparison.OrdinalIgnoreCase))
                        ?? _appCache.FirstOrDefault(a => string.Equals(a.Name, bare, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _activeName = match.Name;
                _activeLaunchId = match.AppId;
                ShowActiveButton();
            }
        }
        catch (Exception)
        {
            // The guessed name stays.
        }
    }

    private void ShowActiveButton()
    {
        _openActive.Content = "Open " + _activeName;
        _openActive.Visibility = _activeApp.Length > 0 && _activeName.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"Spotify.exe" -> "Spotify"; "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" -> "Spotify"; "MSEdge" -> "Microsoft Edge".</summary>
    private static string GuessName(string app)
    {
        if (app.Length == 0) return "";
        string name = app.Contains('!') ? app[(app.LastIndexOf('!') + 1)..] : System.IO.Path.GetFileNameWithoutExtension(app);
        if (name is "App" or "") name = app.Split('_', '!')[0].Split('.').Last();
        return name.ToLowerInvariant() switch
        {
            "msedge" => "Microsoft Edge", "chrome" => "Google Chrome", "firefox" => "Firefox", "vlc" => "VLC",
            "zunemusic" or "microsoft.zunemusic" => "Media Player", "applemusic" => "Apple Music",
            _ => name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : app
        };
    }

    /// <summary>Brings the playing app to the front.</summary>
    private void OpenActivePlayer()
    {
        if (_activeApp.Length == 0) return;
        // Starting an app that is already open switches to it.
        if (_activeLaunchId != null && InstalledApps.Launch(_activeLaunchId)) return;
        if (_activeApp.Contains('!') && InstalledApps.Launch(_activeApp)) return;

        // A desktop program with no Start-menu entry we could match: find its window by program name.
        try
        {
            string bare = System.IO.Path.GetFileNameWithoutExtension(_activeApp);
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(bare))
            {
                using (process)
                {
                    string? path = null;
                    try { path = process.MainModule?.FileName; } catch (Exception) { }
                    if (path != null && ProcessUtil.FocusWindow(path, null)) return;
                }
            }
        }
        catch (Exception) { }
        _ = Host.AskAsync("Couldn't switch to " + _activeName, "Its window wasn't found. Open it from the list of players instead.", "OK");
    }

    private static string Clock(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            // Only fetch the artwork again when the track changes.
            var now = await MediaService.GetAsync(includeThumbnail: false);
            if (now == null)
            {
                _title.Text = "Nothing is playing";
                _artist.Text = "Start music or a video in any app and it will show up here.";
                _time.Text = "";
                _progress.Value = 0;
                _art.Source = null;
                _artKey = "";
                SetPageBackdrop(null);
                _activeApp = "";
                _openActive.Visibility = Visibility.Collapsed;
                _playPause.Content = "";
                return;
            }

            if (now.App != _activeApp) _ = ResolvePlayerAsync(now.App);
            _title.Text = now.Title.Length > 0 ? now.Title : "Unknown title";
            _artist.Text = now.Artist;
            _playPause.Content = now.IsPlaying ? "" : "";
            if (now.Duration > TimeSpan.Zero)
            {
                _progress.Value = now.Position.TotalSeconds / now.Duration.TotalSeconds;
                _time.Text = Clock(now.Position) + " / " + Clock(now.Duration);
            }
            else
            {
                _progress.Value = 0;
                _time.Text = "";
            }

            var key = now.Title + "|" + now.Artist + "|" + now.App;
            if (key != _artKey)
            {
                _artKey = key;
                var withArt = await MediaService.GetAsync(includeThumbnail: true);
                _art.Source = withArt?.Thumbnail is { Length: > 0 } bytes ? ImageLoader.FromBytes(bytes, 680) : null;
                // The artwork, blurred and spread out, becomes the page's background while something plays.
                SetPageBackdrop(_art.Source);
            }
        }
        finally
        {
            _refreshing = false;
        }
    }
}

/// <summary>Music apps installed on this PC, to start one from the Music page.</summary>
public sealed class MusicPlayersScreen : ListScreen
{
    // Matched against the names in the Start menu.
    private static readonly string[] Known =
    {
        "Spotify", "Pandora", "Apple Music", "iTunes", "Amazon Music", "TIDAL", "Deezer", "YouTube Music",
        "SoundCloud", "Qobuz", "Plexamp", "iHeart", "TuneIn", "Audible", "Media Player", "Groove",
        "VLC", "foobar2000", "MusicBee", "Winamp", "AIMP"
    };

    private List<InstalledApp>? _players;

    public MusicPlayersScreen() : base("Open a music player", "{Select}  Open      {Details}  Remove from the list      {Back}  Back") { }

    public override void OnShown()
    {
        Rebuild();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var apps = await Task.Run(() => InstalledApps.ScanAsync());
            _players = apps
                .Where(a => Known.Any(k => a.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        catch (Exception)
        {
            _players = new();
        }
        if (Host.IsTop(this)) Rebuild();
    }

    protected override void Build()
    {
        if (_players == null) { AddNote("Looking for music apps\u2026"); return; }

        var hidden = Host.Settings.HiddenPlayers;
        var shown = _players.Where(p => !hidden.Contains(p.AppId)).ToList();
        if (_players.Count == 0)
            AddNote("No music apps were found on this PC (Spotify, Pandora, Apple Music, Amazon Music, TIDAL, Deezer and others are recognised).");
        else if (shown.Count == 0)
            AddNote("Every player has been removed from this list.");

        foreach (var player in shown)
        {
            var p = player;
            var icon = new Image();
            _ = LoadIconAsync(icon, p.AppId);
            var row = AddRow(p.Name, icon: icon, click: () =>
            {
                if (InstalledApps.Launch(p.AppId)) Host.Pop(this); // back to "Now playing", which picks the music up
                else _ = Host.AskAsync("Couldn't open " + p.Name, "The app didn't start.", "OK");
            });
            row.Tag = p;
        }

        AddHeader("Manage");
        if (shown.Count > 0) AddNote("Highlight a player and press " + Hints.KeyLabel("Details") + " to remove it from this list. The app itself stays installed.");
        int removed = _players.Count(p => hidden.Contains(p.AppId));
        if (removed > 0)
            AddRow("Put back removed players", removed.ToString(), "\uE7A7", click: () =>
            {
                hidden.Clear();
                Host.Settings.Save();
                Rebuild();
            });
        AddRow("Find music apps in the Microsoft Store", glyph: "\uE719",
            click: () => ProcessUtil.Open("ms-windows-store://search/?query=music%20streaming"));
    }

    public override bool OnAction(NavAction action)
    {
        if (action != NavAction.X) return false;
        if (System.Windows.Input.Keyboard.FocusedElement is Button { Tag: InstalledApp player }) _ = RemoveAsync(player);
        return true;
    }

    private async Task RemoveAsync(InstalledApp player)
    {
        if (await Host.AskAsync("Remove " + player.Name + " from this list?",
                "It stays installed; it just won't be offered here. You can put it back from the bottom of the list.",
                "Remove", "Cancel") != 0) return;
        Host.Settings.HiddenPlayers.Add(player.AppId);
        Host.Settings.Save();
        Rebuild();
    }

    private static async Task LoadIconAsync(Image view, string appId) =>
        view.Source = await Task.Run(() => ShellThumb.ForApp(appId, 48));
}
