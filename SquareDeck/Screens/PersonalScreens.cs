using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Ready-made looks, plus wallpaper, tile corners and sound set to mix your own.</summary>
public sealed class ThemeScreen : ListScreen
{
    public ThemeScreen() : base("Themes") { }

    public override void OnResumed() => Rebuild();

    protected override void Build()
    {
        var s = Host.Settings;

        AddHeader("My themes");
        AddRow("Save the current look", s.SavedThemes.Count > 0 ? $"{s.SavedThemes.Count} saved" : "", "\uE74E", click: () => _ = SaveCurrentAsync(),
            subtitle: "Colors, highlight, wallpaper, moving background, sounds, music and button pictures, together");
        foreach (var saved in s.SavedThemes.ToList())
        {
            var t = saved;
            AddRow(t.Name, Theme.Palettes.TryGetValue(t.ThemePalette, out var tp) ? tp.Name : "Standard colors", "\uE790",
                click: () => _ = SavedThemeMenuAsync(t), subtitle: "Saved " + t.Saved.ToString("MMM d, yyyy"));
        }

        // Ready-made looks set light or dark, colors, wallpaper, corners and sounds together: only for the Switch style
        // (the other styles bring all of that themselves).
        if (UiStyles.Current.Id == "switch") AddHeader("Ready-made looks");
        foreach (var preset in Theme.Presets.Where(_ => UiStyles.Current.Id == "switch"))
        {
            var p = preset;
            string details = (p.Mode switch { "dark" => "Dark", "oled" => "Black", _ => "Light" }) + ", " +
                             Theme.CornerStyles.First(c => c.Radius == p.Corner).Name.ToLowerInvariant() + " tiles, " +
                             UiSound.Sets.First(x => x.Id == p.Sounds).Name.ToLowerInvariant() + " sounds";
            AddRow(p.Name, details, "", click: () =>
            {
                s.ThemeMode = p.Mode;
                s.DarkTheme = p.Mode != "light";
                s.AccentColor = p.Accent;
                s.Wallpaper = p.Wallpaper;
                s.TileCorner = p.Corner;
                s.SoundSet = p.Sounds;
                s.ThemePalette = p.Palette;
                Apply();
            });
        }

        // Mixing your own wallpaper, colors, corners and sounds is for the Switch style too; the others bring their own.
        if (UiStyles.Current.Id != "switch")
        {
            AddNote("Ready-made looks and making your own are for the Switch style. " + UiStyles.Current.Name +
                    " brings its own colors, wallpaper and sounds; pick Switch in Settings > Themes to mix your own.");
            return;
        }
        AddHeader("Make your own");
        AddRow("Wallpaper", WallpaperLabel(s.Wallpaper), "", click: () => Host.Push(new WallpaperScreen()));
        AddRow("Color theme", Theme.Palettes.TryGetValue(s.ThemePalette, out var pal) ? pal.Name : "Standard", "\uE790",
            click: () => Host.Push(new ColorThemeScreen()), subtitle: "Background and button colors (styles with their own colors keep them)");
        AddRow("Tile corners", Theme.CornerStyles.FirstOrDefault(c => c.Radius == s.TileCorner).Name ?? "Custom", "", click: () =>
        {
            // Square -> Rounded -> Round -> Square
            int index = Array.FindIndex(Theme.CornerStyles, c => c.Radius == s.TileCorner);
            s.TileCorner = Theme.CornerStyles[(index + 1) % Theme.CornerStyles.Length].Radius;
            Apply();
        });
        AddRow("Sound set", UiSound.SetName(s.SoundSet), "", click: ChooseSounds);
        if (s.SoundSet == "custom")
            AddNote("Custom sounds come from " + (s.SoundFolder.Length > 0 ? s.SoundFolder : "(no folder chosen)") +
                    ". Put WAV files there named " + string.Join(", ", UiSound.CustomFiles) +
                    ". Any that are missing use the Classic sound.");
        if (!s.UiSounds) AddNote("Interface sounds are currently turned off in Settings.");
    }

    private static string WallpaperLabel(string setting)
    {
        if (string.IsNullOrEmpty(setting)) return "None";
        if (setting.StartsWith("preset:", StringComparison.Ordinal) && int.TryParse(setting[7..], out var i) &&
            i >= 0 && i < Theme.Wallpapers.Length) return Theme.Wallpapers[i].Name;
        if (setting.StartsWith("image:", StringComparison.Ordinal))
            return Theme.PictureWallpapers.FirstOrDefault(w => w.File == setting[6..]).Name ?? "Picture";
        return "Custom image";
    }

    private void Apply()
    {
        Host.Settings.Save();
        Host.ApplyLook();
        UiSound.Select();
        Rebuild();
    }

    private void ChooseSounds() => Host.Push(new SoundsScreen());

    private async Task SaveCurrentAsync()
    {
        var s = Host.Settings;
        var name = await Host.PromptAsync("Save this look", "Give it a name, to find it again here.",
            "My theme " + (s.SavedThemes.Count + 1));
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        // The same name replaces the earlier one.
        s.SavedThemes.RemoveAll(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        s.SavedThemes.Insert(0, SavedTheme.From(s, name));
        s.Save();
        UiSound.Select();
        Rebuild();
    }

    private async Task SavedThemeMenuAsync(SavedTheme t)
    {
        var s = Host.Settings;
        int choice = await Host.AskAsync(t.Name, "Saved " + t.Saved.ToString("MMMM d, yyyy"),
            "Use this look", "Update with the current look", "Rename", "Delete", "Cancel");
        switch (choice)
        {
            case 0:
                t.ApplyTo(s);
                s.DarkTheme = Theme.ShouldBeDark(s);
                s.Save();
                // Exactly as saved: the style's own matching is skipped, so nothing replaces what was chosen.
                Host.ApplyLook();
                Host.UseDynamicBackground(s.DynamicBackground);
                Host.ApplySettings();
                UiSound.Select();
                break;
            case 1:
                int at = s.SavedThemes.IndexOf(t);
                s.SavedThemes[at] = SavedTheme.From(s, t.Name);
                s.Save();
                break;
            case 2:
                var name = await Host.PromptAsync("Rename", null, t.Name);
                if (string.IsNullOrWhiteSpace(name)) return;
                t.Name = name.Trim();
                s.Save();
                break;
            case 3:
                if (await Host.AskAsync("Delete " + t.Name + "?", "The look you're using now isn't changed.", "Delete", "Cancel") != 0) return;
                s.SavedThemes.Remove(t);
                s.Save();
                break;
            default:
                return;
        }
        Rebuild();
    }
}

/// <summary>Color themes as swatches: background, tiles and highlight, in the light or dark version now in use.</summary>
public sealed class ColorThemeScreen : GridScreen
{
    private bool _built;

    public ColorThemeScreen() : base("Color theme") { }

    public override void OnShown()
    {
        if (_built) return;
        _built = true;
        Add("", "Standard", Theme.CurrentIsDark ? new[] { "#2D2D2D", "#FFFFFF", "#B4B4B4", "#4B4B4B", "#3C3C3C", "#20E0D0" }
                                                : new[] { "#EBEBEB", "#2D2D2D", "#6B6B6B", "#FFFFFF", "#D2D2D2", "#0AB9C9" });
        foreach (var (id, theme) in Theme.Palettes) Add(id, theme.Name, Theme.CurrentIsDark ? theme.Dark : theme.Light);
        if (UiStyles.PaletteFor(UiStyles.Current, Theme.CurrentIsDark) != null)
        {
            var note = Ui.Text("The UI style in use has colors of its own, so a color theme shows in styles without them.", 15, "SubtleBrush");
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(6, 4, 0, 10);
            Body.Children.Insert(0, note);
        }
    }

    private void Add(string id, string name, string[] c)
    {
        static System.Windows.Media.SolidColorBrush B(string hex) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        // A small mock-up: the background with a row of tiles, the highlighted one ringed, and a line of text.
        var box = new Grid { Width = 208, Height = 117, Background = B(c[0]) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
        for (int i = 0; i < 3; i++)
            row.Children.Add(new Border
            {
                Width = 44, Height = 44, Margin = new Thickness(5), CornerRadius = new CornerRadius(6), Background = B(c[4]),
                BorderBrush = i == 0 ? B(c[5]) : null, BorderThickness = new Thickness(i == 0 ? 3 : 0)
            });
        box.Children.Add(row);
        var line = new Border { Width = 92, Height = 8, CornerRadius = new CornerRadius(4), Background = B(c[1]), Opacity = 0.85,
                                VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
        box.Children.Add(line);
        var dot = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = B(c[3]), BorderBrush = B(c[5]), BorderThickness = new Thickness(2),
                               HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0) };
        box.Children.Add(dot);
        bool current = Host.Settings.ThemePalette == id;
        AddTile(box, current ? "\u2713 " + name : name, 208, () =>
        {
            Host.Settings.ThemePalette = id;
            Host.Settings.Save();
            Host.ApplyLook();
            UiSound.Select();
            Host.Pop(this);
        });
    }
}

/// <summary>Background for the home screen.</summary>
public sealed class WallpaperScreen : GridScreen
{
    private readonly WrapPanel _blur = new() { Margin = new Thickness(0, 0, 0, 12) };

    public WallpaperScreen() : base("Wallpaper")
    {
        Body.Children.Insert(0, _blur);
        BuildBlur();
        AddTile(GlyphBox(208, 117, ""), "None", 208, () => Pick(""));
        for (int i = 0; i < Theme.Wallpapers.Length; i++)
        {
            string setting = "preset:" + i;
            var swatch = new Border { Width = 208, Height = 117, Background = Theme.WallpaperBrush(setting) };
            AddTile(swatch, Theme.Wallpapers[i].Name, 208, () => Pick(setting));
        }
        foreach (var (name, file) in Theme.PictureWallpapers)
        {
            string setting = "image:" + file;
            // A small copy for the tile, not the full-size picture.
            var path = Path.Combine(AppContext.BaseDirectory, "Wallpapers", file);
            var thumb = File.Exists(path) ? ImageLoader.FromFile(path, 420) : null;
            if (thumb == null) continue;
            var swatch = new Border { Width = 208, Height = 117, Background = new System.Windows.Media.ImageBrush(thumb) { Stretch = System.Windows.Media.Stretch.UniformToFill } };
            AddTile(swatch, name, 208, () => Pick(setting));
        }
        AddTile(GlyphBox(208, 117, ""), "Choose image…", 208, Browse);
    }

    public override void OnShown()
    {
        base.OnShown();
        BuildBlur();
    }

    /// <summary>How soft the wallpaper is, as a row of choices (the same levels as the moving background's).</summary>
    private void BuildBlur()
    {
        var s = Host?.Settings ?? Settings.Load();
        _blur.Children.Clear();
        var label = Ui.Text("Blur", 17, "FgBrush", bold: true);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(4, 0, 14, 6);
        _blur.Children.Add(label);
        foreach (var (radius, name) in MainWindow.DynamicBlurLevels)
        {
            int r = radius;
            var b = Ui.PanelButton(name);
            b.FontSize = 16;
            b.Margin = new Thickness(0, 0, 6, 6);
            if (Math.Abs(s.WallpaperBlur - r) <= 2) b.SetResourceReference(ForegroundProperty, "AccentBrush");
            b.DataContext = "blur:" + r;
            b.Click += (_, _) =>
            {
                Host.Settings.WallpaperBlur = r;
                Host.Settings.Save();
                Host.ApplyLook();
                BuildBlur();
                FocusLater(_blur.Children.OfType<Button>().FirstOrDefault(x => x.DataContext as string == "blur:" + r));
            };
            _blur.Children.Add(b);
        }
    }

    private void Pick(string setting)
    {
        Host.Settings.Wallpaper = setting;
        Host.Settings.Save();
        Host.ApplyLook();
        Host.Pop(this);
    }

    private async void Browse()
    {
        var picked = await Host.PickFileAsync("Choose a wallpaper", new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" });
        if (picked == null) return;
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            foreach (var old in Directory.EnumerateFiles(Settings.DataDir, "wallpaper_*"))
            {
                try { File.Delete(old); } catch (IOException) { }
            }
            var dest = Path.Combine(Settings.DataDir, "wallpaper_" + DateTime.Now.Ticks + Path.GetExtension(picked));
            File.Copy(picked, dest, overwrite: true);
            Pick(dest);
        }
        catch (Exception ex)
        {
            _ = Host.AskAsync("Couldn't use that image", ex.Message, "OK");
        }
    }
}

/// <summary>Achievements the launcher has seen you unlock, newest first.</summary>
public sealed class UnlocksScreen : ListScreen
{
    public UnlocksScreen() : base("Recent achievements", "{Back}  Back") { }

    protected override void Build()
    {
        var feed = Host.Achievements.Feed;
        if (feed.Count == 0)
        {
            AddNote("Nothing here yet. When you close a Steam game, the launcher checks whether you unlocked anything " +
                    "while playing and lists it here.");
            AddNote("This needs “Game details” set to Public in your Steam profile's privacy settings, and only " +
                    "covers sessions played while the launcher is open.");
            return;
        }

        foreach (var day in feed.Take(120).GroupBy(e => e.When.Date))
        {
            AddHeader(day.Key == DateTime.Today ? "Today" : day.Key == DateTime.Today.AddDays(-1) ? "Yesterday" : day.Key.ToString("MMMM d"));
            foreach (var e in day)
            {
                var entry = e;
                var icon = new Image();
                if (entry.Icon.Length > 0) _ = LoadIconAsync(icon, entry.Icon);
                AddRow(entry.Name + "  –  " + entry.Game, Ui.Ago(entry.When), icon: icon, click: () =>
                    _ = Host.AskAsync(entry.Name, entry.Game + (entry.Description.Length > 0 ? "\n\n" + entry.Description : ""), "OK"));
            }
        }
    }

    private static async Task LoadIconAsync(Image view, string url) =>
        view.Source = await Task.Run(() => ImageLoader.FromUrlAsync(url, 64));
}
