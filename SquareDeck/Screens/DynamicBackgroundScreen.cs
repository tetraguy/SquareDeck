using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Settings > Appearance > Moving background: the bundled videos as tiles (the highlighted one plays), Off,
/// your own videos (added from here, kept in SquareDeck's data folder), and how strongly to blur it.
/// Changes apply straight away, behind this page too.
/// </summary>
public sealed class DynamicBackgroundScreen : GridScreen
{
    private const double W = 272, H = 153;
    private readonly WrapPanel _blur = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly MediaElement _preview = new()
    {
        LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, IsMuted = true, Stretch = Stretch.UniformToFill, IsHitTestVisible = false
    };
    private Grid? _previewHost;

    public static string UserFolder => Path.Combine(Settings.DataDir, "backgrounds");
    private static readonly string[] VideoTypes = { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm" };

    public DynamicBackgroundScreen() : base("Moving background", "{Select}  Use this      {Options}  Remove your video      {Back}  Back")
    {
        _preview.MediaEnded += (_, _) => { _preview.Position = TimeSpan.Zero; _preview.Play(); };
    }

    public override void OnShown() => Build();

    public override void OnClosed() => StopPreview();

    public override void OnCovered() => StopPreview();

    private void Build()
    {
        StopPreview();
        var s = Host.Settings;
        Body.Children.Clear();
        Tiles.Children.Clear();

        var note = Ui.Text("A slowly moving video behind the home screen and every page. It pauses while a game is running, " +
                           "and when SquareDeck isn't in front. It replaces the still wallpaper, and choosing one turns off the game artwork " +
                           "behind the home row.", 16, "SubtleBrush");
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(4, 0, 0, 12);
        Body.Children.Add(note);

        // Blur level, as a row of choices.
        _blur.Children.Clear();
        var blurLabel = Ui.Text("Blur", 17, "FgBrush", bold: true);
        blurLabel.VerticalAlignment = VerticalAlignment.Center;
        blurLabel.Margin = new Thickness(4, 0, 14, 6);
        _blur.Children.Add(blurLabel);
        foreach (var (radius, name) in MainWindow.DynamicBlurLevels)
        {
            int r = radius;
            var b = Ui.PanelButton(name);
            b.FontSize = 16;
            b.Margin = new Thickness(0, 0, 6, 6);
            if (Math.Abs(s.DynamicBlur - r) <= 2) b.SetResourceReference(ForegroundProperty, "AccentBrush");
            b.DataContext = "blur:" + r;
            b.Click += (_, _) =>
            {
                s.DynamicBlur = r;
                s.Save();
                Host.ApplyDynamicBackground();
                Build();
                FocusLater(_blur.Children.OfType<Button>().FirstOrDefault(x => x.DataContext as string == "blur:" + r));
            };
            _blur.Children.Add(b);
        }
        Body.Children.Add(_blur);

        Button? focus = null;
        var offRow = new WrapPanel();
        var off = AddTile(Face(null, "Off"), "Off" + (s.DynamicBackground.Length == 0 ? "   ✓" : ""), W, () => Use(""), into: offRow);
        if (s.DynamicBackground.Length == 0) focus = off;
        Body.Children.Add(offRow);

        // Your own videos, and the tile that adds one.
        Body.Children.Add(Ui.Header("Your videos"));
        var mine = new WrapPanel();
        var add = AddTile(GlyphFace("\uE710", "Add a video"), "Add your own…", W, () => _ = ImportAsync(), into: mine);
        add.DataContext = "add";
        foreach (var file in UserVideos())
        {
            string path = file;
            var face = Face(null, null, path);
            bool chosen = string.Equals(s.DynamicBackground, path, StringComparison.OrdinalIgnoreCase);
            var tile = AddTile(face, Path.GetFileNameWithoutExtension(path) + (chosen ? "   ✓" : ""), W, () => Use(path), into: mine);
            tile.DataContext = "mine:" + path;
            tile.GotKeyboardFocus += (_, _) => StartPreview(face, path);
            tile.LostKeyboardFocus += (_, _) => StopPreview();
            tile.MouseEnter += (_, _) => StartPreview(face, path);
            if (chosen) focus = tile;
        }
        Body.Children.Add(mine);

        foreach (var (group, names) in MainWindow.DynamicBackgrounds)
        {
            Body.Children.Add(Ui.Header(group));
            var row = new WrapPanel();
            foreach (var name in names)
            {
                if (MainWindow.DynamicPathFor(name) == null) continue;
                string n = name;
                var face = Face(n, null);
                bool chosen = string.Equals(s.DynamicBackground, n, StringComparison.OrdinalIgnoreCase);
                var tile = AddTile(face, n + (chosen ? "   ✓" : ""), W, () => Use(n), into: row);
                tile.GotKeyboardFocus += (_, _) => StartPreview(face, n);
                tile.LostKeyboardFocus += (_, _) => StopPreview();
                tile.MouseEnter += (_, _) => StartPreview(face, n);
                if (chosen) focus = tile;
            }
            Body.Children.Add(row);
        }
        FocusLater(focus);
    }

    private static IEnumerable<string> UserVideos()
    {
        try
        {
            return Directory.Exists(UserFolder)
                ? Directory.EnumerateFiles(UserFolder).Where(f => VideoTypes.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToList()
                : Enumerable.Empty<string>();
        }
        catch (Exception) { return Enumerable.Empty<string>(); }
    }

    /// <summary>Copies a video you pick into SquareDeck's own folder (so it keeps working if the original moves) and uses it.</summary>
    private async Task ImportAsync()
    {
        var picked = await Host.PickFileAsync("Choose a video for the background", VideoTypes);
        if (picked == null) return;
        string? copy = null, problem = null;
        await Host.RunBusyAsync("Adding " + Path.GetFileName(picked), () => Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(UserFolder);
                string name = Path.GetFileNameWithoutExtension(picked), ext = Path.GetExtension(picked);
                string target = Path.Combine(UserFolder, name + ext);
                for (int i = 2; File.Exists(target); i++) target = Path.Combine(UserFolder, $"{name} ({i}){ext}");
                File.Copy(picked, target);
                copy = target;
            }
            catch (Exception ex) { problem = ex.Message; }
        }));
        if (copy == null)
        {
            await Host.AskAsync("Couldn't add the video", problem ?? "Something went wrong.", "OK");
            return;
        }
        Use(copy);
        if (!new[] { ".mp4", ".m4v", ".wmv" }.Contains(Path.GetExtension(copy).ToLowerInvariant()))
            await Host.AskAsync("Added", "If it doesn't play, Windows needs a codec for this kind of video. MP4 (H.264) plays everywhere.", "OK");
    }

    /// <summary>Y on one of your videos: removes it (asks first).</summary>
    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Y && System.Windows.Input.Keyboard.FocusedElement is Button { DataContext: string id } && id.StartsWith("mine:"))
        {
            _ = RemoveAsync(id[5..]);
            return true;
        }
        return base.OnAction(action);
    }

    private async Task RemoveAsync(string path)
    {
        if (await Host.AskAsync("Remove this video?", Path.GetFileName(path) + "\nIt's deleted from SquareDeck's folder (not from where you added it from).", "Remove", "Cancel") != 0) return;
        StopPreview();
        if (string.Equals(Host.Settings.DynamicBackground, path, StringComparison.OrdinalIgnoreCase)) Host.UseDynamicBackground("");
        await Task.Delay(300); // let the background let go of the file
        try { File.Delete(path); } catch (Exception) { }
        Build();
    }

    private static Grid GlyphFace(string glyph, string label)
    {
        var grid = new Grid { Width = W, Height = H, ClipToBounds = true, Clip = new RectangleGeometry(new Rect(0, 0, W, H), 12, 12) };
        var back = new Border();
        back.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        grid.Children.Add(back);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var icon = Ui.Glyph(glyph, 34, "AccentBrush");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        var text = Ui.Text(label, 15, "SubtleBrush");
        text.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(text);
        grid.Children.Add(stack);
        return grid;
    }

    /// <summary>A rounded box with the video's still picture (or a label for Off).</summary>
    private static Grid Face(string? name, string? label, string? file = null)
    {
        var grid = new Grid { Width = W, Height = H, ClipToBounds = true };
        grid.Clip = new RectangleGeometry(new Rect(0, 0, W, H), 12, 12);
        var back = new Border();
        back.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        grid.Children.Add(back);
        if (file != null)
        {
            // Your own video: Windows' thumbnail of it.
            var image = new Image { Stretch = Stretch.UniformToFill };
            grid.Children.Add(image);
            _ = System.Threading.Tasks.Task.Run(() => { try { var t = ShellThumb.Get(file, 480); t?.Freeze(); return t; } catch (Exception) { return null; } })
                .ContinueWith(t => image.Source = t.Result, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }
        else if (name != null)
        {
            string still = Path.Combine(MainWindow.BackgroundsFolder, "previews", name + ".jpg");
            if (File.Exists(still) && ImageLoader.FromFile(still, 480) is { } picture)
                grid.Children.Add(new Image { Source = picture, Stretch = Stretch.UniformToFill });
        }
        else
        {
            var icon = Ui.Text("", 40, "SubtleBrush");
            icon.FontFamily = Ui.IconFont;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(icon);
        }
        return grid;
    }

    private void StartPreview(Grid face, string name)
    {
        if (ReferenceEquals(_previewHost, face)) return;
        StopPreview();
        var path = MainWindow.DynamicPathFor(name);
        if (path == null || Theme.ReduceMotion) return;
        _previewHost = face;
        face.Children.Add(_preview);
        _preview.Source = new Uri(path);
        _preview.Play();
    }

    private void StopPreview()
    {
        if (_previewHost == null) return;
        _preview.Stop();
        _preview.Close();
        _previewHost.Children.Remove(_preview);
        _previewHost = null;
    }

    private void Use(string name)
    {
        Host.UseDynamicBackground(name); // also turns off the game artwork behind the home row
        Build();
    }
}
