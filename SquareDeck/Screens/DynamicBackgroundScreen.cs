using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Settings > Appearance > Moving background: the bundled videos as tiles (the highlighted one plays), Off,
/// and how strongly to blur it. Changes apply straight away, behind this page too.
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

    public DynamicBackgroundScreen() : base("Moving background", "{Select}  Use this      {Back}  Back")
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

    /// <summary>A rounded box with the video's still picture (or a label for Off).</summary>
    private static Grid Face(string? name, string? label)
    {
        var grid = new Grid { Width = W, Height = H, ClipToBounds = true };
        grid.Clip = new RectangleGeometry(new Rect(0, 0, W, H), 12, 12);
        var back = new Border();
        back.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        grid.Children.Add(back);
        if (name != null)
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
