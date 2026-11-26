using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Settings > Themes > Your styles > Create a style (or Edit): builds a UI style of your own. A large picture of the
/// home screen on the left follows every change; on the right, everything that can be chosen, grouped: background,
/// sound, colors, type, the home screen's buttons and shelf, an effect, animations and the lock screen. Most choices
/// open a page of small pictures of the home screen with each option, so you can see it before choosing.
/// </summary>
public sealed class StyleBuilderScreen : ListScreen
{
    private readonly CustomStyle _draft;
    private readonly bool _isNew;
    private readonly Action? _saved;
    private bool _dirty;
    private readonly Border _preview = new() { Width = 500, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _name = Ui.Text("", 26, "FgBrush", bold: true);
    private readonly TextBlock _about = Ui.Text("", 15, "SubtleBrush");

    private const string HintLine = "{Select}  Change      {Details}  Save and use      {Back}  Back";

    public StyleBuilderScreen(CustomStyle draft, bool isNew, Action? saved = null) : base(isNew ? "Create a style" : "Edit style", HintLine)
    {
        _draft = draft;
        _isNew = isNew;
        _saved = saved;
        _dirty = isNew;

        // The picture of the home screen on the left, the choices on the right.
        Rows.Width = 640;
        Rows.HorizontalAlignment = HorizontalAlignment.Left;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(530) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(10, 4, 20, 0) };
        left.Children.Add(_preview);
        _name.Margin = new Thickness(2, 16, 0, 2);
        _name.TextTrimming = TextTrimming.CharacterEllipsis;
        left.Children.Add(_name);
        _about.TextWrapping = TextWrapping.Wrap;
        _about.TextTrimming = TextTrimming.None;
        left.Children.Add(_about);
        left.Children.Add(BuildDemo());
        var tip = Ui.Text("Every choice shows here as you make it: the picture follows your colors, art, icons, button pictures and Continue; sounds and music play when you pick them; animations play here or full screen.", 13, "SubtleBrush");
        tip.TextWrapping = TextWrapping.Wrap;
        tip.TextTrimming = TextTrimming.None;
        tip.Margin = new Thickness(2, 14, 0, 0);
        tip.Opacity = 0.8;
        left.Children.Add(tip);
        grid.Children.Add(left);
        // The rows' scroller is still in the page's body: swapping the body frees it to go into the grid.
        ReplaceBody(grid);
        Grid.SetColumn(RowsScroll, 1);
        grid.Children.Add(RowsScroll);
    }

    private bool _holdingMusic;

    /// <summary>The menu music pauses while you build a style, so its sounds and music are heard on their own.</summary>
    public override void OnShown()
    {
        base.OnShown();
        if (!_holdingMusic) { _holdingMusic = true; Host.HoldMenuMusic(true); }
    }

    public override void OnClosed()
    {
        base.OnClosed();
        if (_holdingMusic) { _holdingMusic = false; Host.HoldMenuMusic(false); }
    }

    public override void OnResumed()
    {
        base.OnResumed();
        Rebuild();
    }

    private void UpdatePreview()
    {
        var style = _draft.ToStyle();
        _preview.Child = new Viewbox { Width = 500, Stretch = Stretch.Uniform, Child = UiStyleScreen.Preview(style, new UiStyleScreen.Extras(_draft.Glyphs, _draft.ContinueTile)) };
        _name.Text = _draft.Name + (_dirty ? "  •" : "");
        _about.Text = style.Description + (_dirty ? " Not saved yet." : "");
        _name.FontFamily = AppFonts.Family(style.Font);
    }

    private void Changed()
    {
        _dirty = true;
        Rebuild();
    }

    // ================================================================ the choices

    protected override void Build()
    {
        var d = _draft;
        var start = MyStyles.BuiltIn(d.BasedOn);
        UpdatePreview();

        AddHeader("Name");
        AddRow("Name", d.Name, "\uE8AC", click: () => _ = RenameAsync(), subtitle: "What it's called in Themes");
        AddRow("Starting point", start.Name + "  \u00B7  " + MyStyles.TierOf(start), "\uE8B1", click: () => _ = RestartAsync(),
            subtitle: "The ready-made style yours is built on (Legendary or Mythical). Choose another to start again from it");

        AddHeader("Remix");
        AddRow("Take pieces from another style", null, "\uE8B1", click: () => _ = RemixAsync(),
            subtitle: "Its wallpaper or moving background, music, sounds, font or icons, from any Legendary or Mythical style");

        AddHeader("Background");
        AddRow("Wallpaper or moving background", BackgroundName(d), "\uE91B", click: ChooseBackground,
            subtitle: "A picture, a gradient, a moving background, or one of your own");

        AddHeader("Sound");
        AddRow("Background music", MusicName(d.Music), "\uE8D6", click: () => _ = ChooseMusicAsync(), subtitle: "Plays quietly on the home screen and in menus");
        AddRow("Interface sounds", UiSound.SetName(d.Sounds), "\uE767", click: () => _ = ChooseSoundsAsync(nav: false),
            subtitle: "Selecting, going back, pages and pop-ups");
        AddRow("Navigation sound", d.NavSounds.Length == 0 ? "Same as interface sounds" : UiSound.SetName(d.NavSounds), "\uE7C9",
            click: () => _ = ChooseSoundsAsync(nav: true), subtitle: "Moving the highlight around");

        AddHeader("Colors");
        AddRow("Light, dark or OLED", MyStyles.Modes.FirstOrDefault(m => m.Id == d.Mode).Name ?? "Dark", "\uE793", click: () => _ = ChooseModeAsync());
        AddRow("Color theme", PaletteName(d.Palette), "\uE790", click: ChoosePalette, subtitle: "The background, text, buttons and tiles");
        AddRow("Highlight color", AccentName(d.Accent), "\uE7E6", click: () => _ = ChooseAccentAsync(), subtitle: "The ring around the selection and other highlights");

        AddHeader("Type and shape");
        AddRow("Font", d.Font == "style" ? "From the starting style" : SettingsScreen.UiFonts.FirstOrDefault(f => f.Id == d.Font).Name ?? FontName(d.Font), "\uE8D2", click: ChooseFont);
        AddRow("Corners", d.Corners < 0 ? "From the starting style" : MyStyles.CornerChoices.FirstOrDefault(c => c.Radius == d.Corners).Name ?? d.Corners + " px", "\uE71A",
            click: () => _ = ChooseCornersAsync(), subtitle: "How round the tiles, rows and panels are");

        AddHeader("Home screen");
        AddRow("Button shape", NameIn(MyStyles.Shapes, d.ButtonShape), "\uEA3A", click: () => Pick("Button shape", "The buttons under the games.", MyStyles.Shapes, () => d.ButtonShape, (c, v) => c.ButtonShape = v));
        AddRow("Button look", NameIn(MyStyles.Fills, d.ButtonFill), "\uE771", click: () => Pick("Button look", "How the buttons are filled and lit.", MyStyles.Fills, () => d.ButtonFill, (c, v) => c.ButtonFill = v));
        AddRow("Glow around the selection", d.Glow ? "On" : "Off", "\uE706", click: () => { d.Glow = !d.Glow; Changed(); },
            subtitle: "A soft glow in the highlight color around the selected game and button");
        AddRow("Button shelf", NameIn(MyStyles.Shelves, d.Shelf), "\uE8A9", click: () => Pick("Button shelf", "A shelf behind all the buttons, or none.", MyStyles.Shelves, () => d.Shelf, (c, v) => c.Shelf = v));
        AddRow("Footer", NameIn(MyStyles.Footers, d.Footer), "\uE8A5", click: () => Pick("Footer", "Behind the button hints at the bottom.", MyStyles.Footers, () => d.Footer, (c, v) => c.Footer = v));
        AddRow("Button pictures", SquareDeck.Hints.Looks.FirstOrDefault(x => x.Id == d.Glyphs).Name ?? "Simple", "\uE7FC", click: () => _ = ChooseGlyphsAsync(),
            subtitle: "How controller buttons are drawn in hints: simple, controller icons or pixel art");
        AddRow("Icons", IconsName(d), "\uE8B9", click: () => _ = ChooseIconsAsync(),
            subtitle: "The home buttons' icons: the plain symbols, or the drawn icons of a Legendary or Mythical style");
        AddRow("Game art", NameIn(MyStyles.Arts, d.TileArt), "\uE8B9", click: ChooseArt, subtitle: "Square, tall or rectangle");
        AddRow("Continue where you left off", d.ContinueTile ? "On" : "Off", "\uE768", click: () => { d.ContinueTile = !d.ContinueTile; Changed(); },
            subtitle: "The last game you played first on the home row, ready to carry on");

        AddHeader("Effect");
        AddRow("Effect", NameOf(d.Effect), "\uE781", click: ChooseEffect,
            subtitle: "Halftone, comic book, retro, futuristic, synthwave, Y2K, dreamcore and more");

        AddHeader("Animations");
        AddRow("Home buttons", NameIn(SettingsScreen.ButtonStyles, d.ButtonAnimation), "\uE7C9",
            click: () => _ = ChooseFromAsync("Home buttons", "How the home buttons react when highlighted.", SettingsScreen.ButtonStyles, v => d.ButtonAnimation = v, DemoButton));
        AddRow("Opening pages", NameIn(SettingsScreen.PageStyles, d.PageAnimation), "\uE8A7",
            click: () => _ = ChooseFromAsync("Opening pages", "How pages and pop-ups appear.", SettingsScreen.PageStyles, v => d.PageAnimation = v, DemoPage));
        AddRow("Selected game", NameIn(Motion.TileHighlights, d.TileAnimation), "\uE7FC",
            click: () => _ = ChooseFromAsync("Selected game", "How the highlighted game stands out on the home row.", Motion.TileHighlights, v => d.TileAnimation = v, DemoTile));
        var boots = MainWindow.BootAnimations.Concat(MainWindow.MoreBootAnimations.Where(m => MainWindow.BootAnimations.All(b => b.Id != m.Id))).ToArray();
        AddRow("Startup", NameIn(boots, d.BootAnimation), "\uE7E8",
            click: () => _ = ChooseFromAsync("Startup animation", "Plays when SquareDeck opens.", boots, v => d.BootAnimation = v, id => Host.PreviewIntro(id)));
        AddRow("Closing", NameIn(MainWindow.ExitStyles, d.ExitAnimation), "\uE711",
            click: () => _ = ChooseFromAsync("Closing animation", "Plays when you exit SquareDeck.", MainWindow.ExitStyles, v => d.ExitAnimation = v, id => _ = Host.PreviewExitAsync(id)));
        var launch = LaunchSplashScreen.Styles.Select(x => (x.Id, x.Name)).Append(("random", "Surprise me")).ToArray();
        AddRow("Starting a game", NameIn(launch, d.LaunchAnimation), "\uE768",
            click: () => _ = ChooseFromAsync("Starting a game", "The splash while a game starts.", launch, v => d.LaunchAnimation = v, id => Host.Push(LaunchSplashScreen.Preview(id))));
        var updateLooks = new[] { ("", "Leave mine as it is") }.Concat(SilentUpdate.Looks).ToArray();
        AddRow("Updating SquareDeck", NameIn(updateLooks, d.UpdateLook), "\uE895",
            click: () => _ = ChooseFromAsync("Update screen animation", "Plays on the screen that installs SquareDeck's updates.", updateLooks, v => d.UpdateLook = v, id => Host.PreviewUpdateScreen(id, d.UpdateHaptic)));
        var updateBuzz = new[] { ("", "Leave mine as it is") }.Concat(SilentUpdate.Haptics).ToArray();
        AddRow("Update screen vibration", NameIn(updateBuzz, d.UpdateHaptic), "\uE7FC",
            click: () => _ = ChooseFromAsync("Update screen vibration", "How the controller buzzes while an update installs.", updateBuzz, v => d.UpdateHaptic = v, id => Host.PreviewUpdateScreen(d.UpdateLook, id)));

        AddHeader("Windows lock screen");
        AddRow("Lock screen picture", LockName(d), "\uE72E", click: () => _ = ChooseLockScreenAsync(),
            subtitle: "Put on the Windows lock screen when you choose this style");

        AddHeader(_isNew ? "Save" : "Save and share");
        AddRow("Save and use", null, "\uE73E", click: () => _ = SaveAsync(use: true), subtitle: "Keeps your style and switches to it");
        AddRow("Save", null, "\uE74E", click: () => _ = SaveAsync(use: false), subtitle: "Keeps it in Your styles for later");
        if (!_isNew)
        {
            AddRow("Export", null, "\uEDE1", click: () => _ = ExportAsync(), subtitle: "Saves it as a file to share or keep, with any pictures, videos and music of your own");
            AddRow("Delete this style", null, "\uE74D", click: () => _ = DeleteAsync());
        }
    }

    // ================================================================ names

    private static string NameIn((string Id, string Name)[] list, string id) => list.FirstOrDefault(x => x.Id == id).Name ?? list[0].Name;
    private static string NameIn((string Id, string Name, string About)[] list, string id) => list.FirstOrDefault(x => x.Id == id).Name ?? list[0].Name;
    private static string NameOf(string effect) => MyStyles.Effects.FirstOrDefault(e => e.Id == effect).Name ?? "None";

    private static string BackgroundName(CustomStyle d)
    {
        if (d.Moving.Length > 0) return Path.IsPathRooted(d.Moving) ? "Your video" : d.Moving + " (moving)";
        if (d.Wallpaper.Length == 0) return "None";
        if (d.Wallpaper.StartsWith("preset:") && int.TryParse(d.Wallpaper[7..], out var i) && i >= 0 && i < Theme.Wallpapers.Length) return Theme.Wallpapers[i].Name + " gradient";
        if (d.Wallpaper.StartsWith("image:")) return Theme.PictureWallpapers.FirstOrDefault(p => p.File == d.Wallpaper[6..]).Name
            ?? (MyStyles.Everything().FirstOrDefault(st => st.Wallpaper == d.Wallpaper) is { } from ? from.Name + "'s picture" : "Picture");
        return "Your picture";
    }

    private static string MusicName(string id) => id switch
    {
        "" => "Off",
        "shuffle" => "Shuffle all",
        _ => BackgroundMusic.List().FirstOrDefault(t => t.Id == id)?.Name ?? "Off"
    };

    private static string PaletteName(string id) => id switch
    {
        "style" => "From the starting style",
        "plain" => "SquareDeck gray",
        _ => Theme.Palettes.TryGetValue(id, out var p) ? p.Name : "From the starting style"
    };

    private static string AccentName(string hex)
    {
        if (hex.Length == 0) return "The colors' own";
        return Theme.AccentChoices.Concat(MyStyles.MoreAccents).FirstOrDefault(a => string.Equals(a.Hex, hex, StringComparison.OrdinalIgnoreCase)).Name ?? hex;
    }

    private static string LockName(CustomStyle d) => d.LockScreen switch
    {
        "" => "Leave it as it is",
        "wallpaper" => "This style's wallpaper",
        var v when v.StartsWith("image:") => Theme.PictureWallpapers.FirstOrDefault(p => p.File == v[6..]).Name ?? "Picture",
        _ => "Your picture"
    };

    private static string FontName(string font) =>
        font.Contains('#') ? font[(font.LastIndexOf('#') + 1)..] : font.StartsWith("file:") ? Path.GetFileNameWithoutExtension(font[5..]) : font.Split(',')[0].Trim();

    private static string IconsName(CustomStyle d)
    {
        if (d.IconsFrom.Length > 0 && UiStyles.Advanced.FirstOrDefault(x => x.Id == d.IconsFrom) is { } from) return from.Name + "'s icons";
        return MyStyles.BuiltIn(d.BasedOn).Skin != null ? "From the starting style" : "Plain symbols";
    }

    // ================================================================ trying animations

    private readonly Border _demoTile = new() { Width = 70, Height = 70, CornerRadius = new CornerRadius(12), Margin = new Thickness(8) };
    private readonly Border _demoRing = new() { Width = 84, Height = 84, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(3) };
    private readonly Button _demoButton = new() { Width = 56, Height = 56, Focusable = false, IsHitTestVisible = false };
    private Grid? _demoTileHost;

    /// <summary>A game tile and a home button under the picture, where the home button, page and selected-game animations play.</summary>
    private UIElement BuildDemo()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var label = Ui.Text("Animations play here", 13, "SubtleBrush");
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Width = 130;
        label.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(label);
        _demoTile.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _demoTile.Opacity = 0.85;
        _demoRing.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        _demoTileHost = new Grid { Width = 96, Height = 96, Margin = new Thickness(10, 0, 20, 0) };
        _demoTileHost.Children.Add(_demoTile);
        _demoTileHost.Children.Add(_demoRing);
        _demoRing.Visibility = Visibility.Hidden;
        row.Children.Add(_demoTileHost);
        _demoButton.Style = Ui.StyleOf("RoundButton");
        _demoButton.Content = Ui.Glyph("\uE80F", 22, "FgBrush");
        row.Children.Add(_demoButton);
        return row;
    }

    /// <summary>How a home button reacts when it's highlighted, played on the sample button.</summary>
    private async void DemoButton(string style)
    {
        string was = Motion.ButtonStyle;
        bool wasOn = Motion.ButtonAnimations;
        Motion.ButtonStyle = style;
        Motion.ButtonAnimations = true;
        UiSound.Move();
        Motion.Highlight(_demoButton, true);
        await Task.Delay(1100);
        Motion.Highlight(_demoButton, false);
        await Task.Delay(300);
        Motion.ButtonStyle = was;
        Motion.ButtonAnimations = wasOn;
    }

    /// <summary>How the selected game stands out, played on the sample tile for a few seconds.</summary>
    private async void DemoTile(string style)
    {
        string was = Motion.TileStyle;
        Motion.TileStyle = style;
        _demoRing.Visibility = Visibility.Visible;
        UiSound.Move();
        Motion.GrowTile(_demoTileHost, true);
        Motion.StartPulse(_demoRing);
        await Task.Delay(2600);
        Motion.StopPulse(_demoRing);
        Motion.GrowTile(_demoTileHost, false);
        _demoRing.Visibility = Visibility.Hidden;
        Motion.TileStyle = was;
    }

    /// <summary>How pages open, played on the picture of your style.</summary>
    private void DemoPage(string style)
    {
        string was = Motion.PageStyle;
        Motion.PageStyle = style;
        UiSound.Page();
        try { Motion.OpenPage(_preview, false); }
        finally { Motion.PageStyle = was; }
    }

    // ================================================================ pickers

    /// <summary>A small picture of the home screen with one change made.</summary>
    private FrameworkElement Mini(Action<CustomStyle> change, string? caption = null)
    {
        var d = _draft.Copy();
        change(d);
        var grid = new Grid();
        grid.Children.Add(new Viewbox { Width = PreviewPickerScreen.W, Height = PreviewPickerScreen.H, Stretch = Stretch.UniformToFill, Child = UiStyleScreen.Preview(d.ToStyle(), new UiStyleScreen.Extras(d.Glyphs, d.ContinueTile)) });
        if (caption != null)
        {
            var band = new Border { VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)), Padding = new Thickness(10, 5, 10, 6) };
            band.Child = new TextBlock { Text = caption, Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            grid.Children.Add(band);
        }
        return grid;
    }

    /// <summary>Choosing among options shown as pictures of the home screen.</summary>
    private void Pick(string title, string note, (string Id, string Name, string About)[] options, Func<string> current, Action<CustomStyle, string> set) =>
        Host.Push(new PreviewPickerScreen(title, note, options, current, id => Mini(d => set(d, id)), id => { set(_draft, id); _dirty = true; }));

    private static void CopyInto(CustomStyle from, CustomStyle to)
    {
        foreach (var p in typeof(CustomStyle).GetProperties()) if (p.CanWrite) p.SetValue(to, p.GetValue(from));
    }

    private void ChooseBackground()
    {
        var d = _draft;
        var options = new List<(string Id, string Name, string About)> { ("none", "None", "Just the background color") };
        if (Path.IsPathRooted(d.Wallpaper)) options.Add(("keep-picture", "Your picture", "The picture you chose"));
        if (Path.IsPathRooted(d.Moving)) options.Add(("keep-video", "Your video", "The video you chose"));
        options.Add(("own-picture", "A picture of your own…", "Any picture on this device"));
        options.Add(("own-video", "A video of your own…", "Any MP4 on this device, as a moving background"));
        foreach (var (_, names) in MainWindow.DynamicBackgrounds)
            foreach (var name in names) options.Add(("moving:" + name, name, "Moving background"));
        foreach (var (name, file) in Theme.PictureWallpapers) options.Add(("image:" + file, name, "Picture"));
        // The pictures that come with the Advanced, Legendary and Mythical styles (and any other style's own).
        foreach (var st in MyStyles.Everything())
        {
            if (!st.Wallpaper.StartsWith("image:") || options.Any(o => o.Id == st.Wallpaper)) continue;
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Wallpapers", st.Wallpaper[6..]))) continue;
            options.Add((st.Wallpaper, st.Name, MyStyles.TierOf(st) + " style's picture"));
        }
        for (int i = 0; i < Theme.Wallpapers.Length; i++) options.Add(("preset:" + i, Theme.Wallpapers[i].Name, "Gradient"));

        string Current() => d.Moving.Length > 0 ? (Path.IsPathRooted(d.Moving) ? "keep-video" : "moving:" + d.Moving)
            : d.Wallpaper.Length == 0 ? "none" : Path.IsPathRooted(d.Wallpaper) ? "keep-picture" : d.Wallpaper;

        Host.Push(new PreviewPickerScreen("Wallpaper or moving background", "Pictures, gradients and moving backgrounds. The ones marked ▶ move.",
            options.ToArray(), Current, BackgroundFace, id => _ = SetBackgroundAsync(id)));
    }

    private FrameworkElement BackgroundFace(string id)
    {
        var grid = new Grid();
        string? file = id switch
        {
            "keep-picture" => _draft.Wallpaper,
            var v when v.StartsWith("image:") => Path.Combine(AppContext.BaseDirectory, "Wallpapers", v[6..]),
            var v when v.StartsWith("moving:") => Path.Combine(MainWindow.BackgroundsFolder, "previews", v[7..] + ".jpg"),
            _ => null
        };
        if (file != null && File.Exists(file) && ImageLoader.FromFile(file, 320) is { } picture)
        {
            grid.Children.Add(new Image { Source = picture, Stretch = Stretch.UniformToFill });
            if (id.StartsWith("moving:")) RenderOptions.SetBitmapScalingMode(grid, BitmapScalingMode.NearestNeighbor);
        }
        else if (id.StartsWith("preset:") && Theme.WallpaperBrush(id) is { } gradient) grid.Children.Add(new Rectangle { Fill = gradient });
        else
        {
            string glyph = id switch { "own-picture" => "\uEB9F", "own-video" or "keep-video" => "\uE714", _ => "\uE711" };
            var icon = Ui.Glyph(glyph, 40, "SubtleBrush");
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(icon);
        }
        if (id.StartsWith("moving:") || id == "keep-video")
        {
            var badge = new Border { Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 3),
                                     Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            badge.Child = new TextBlock { Text = "▶ Moving", Foreground = Brushes.White, FontSize = 12 };
            grid.Children.Add(badge);
        }
        return grid;
    }

    private async Task SetBackgroundAsync(string id)
    {
        var d = _draft;
        try
        {
            switch (id)
            {
                case "none": d.Wallpaper = ""; d.Moving = ""; break;
                case "keep-picture": d.Moving = ""; break;
                case "keep-video": break;
                case "own-picture":
                    if (await Host.PickFileAsync("Choose a picture", new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" }) is not { } picture) return;
                    d.Wallpaper = MyStyles.Keep(d.Id, picture, "wallpaper");
                    d.Moving = "";
                    break;
                case "own-video":
                    if (await Host.PickFileAsync("Choose a video", new[] { ".mp4", ".wmv", ".m4v" }) is not { } video) return;
                    d.Moving = MyStyles.Keep(d.Id, video, "moving");
                    d.Wallpaper = "";
                    break;
                default:
                    if (id.StartsWith("moving:")) { d.Moving = id[7..]; d.Wallpaper = ""; }
                    else { d.Wallpaper = id; d.Moving = ""; }
                    break;
            }
            _dirty = true;
        }
        catch (Exception ex)
        {
            await Host.AskAsync("Couldn't use that file", ex.Message, "OK");
        }
    }

    private async Task ChooseMusicAsync()
    {
        var tracks = BackgroundMusic.List();
        var names = new List<string> { "Off", "Shuffle all" };
        names.AddRange(tracks.Select(t => t.Name));
        names.Add("Add a track of your own…");
        int choice = await Host.AskAsync("Background music", "Plays on the home screen and in menus.", names.ToArray());
        if (choice < 0) return;
        if (choice == 0) _draft.Music = "";
        else if (choice == 1) _draft.Music = "shuffle";
        else if (choice < names.Count - 1) _draft.Music = tracks[choice - 2].Id;
        else
        {
            if (await Host.PickFileAsync("Choose a music file", new[] { ".mp3", ".wav", ".wma", ".m4a", ".aac" }) is not { } file) return;
            try
            {
                Directory.CreateDirectory(BackgroundMusic.UserDir);
                File.Copy(file, Path.Combine(BackgroundMusic.UserDir, Path.GetFileName(file)), true);
                _draft.Music = "track:" + Path.GetFileNameWithoutExtension(file);
            }
            catch (Exception ex) { await Host.AskAsync("Couldn't add the music", ex.Message, "OK"); return; }
        }
        Changed();
        if (_draft.Music.Length > 0) Host.PreviewMusic(_draft.Music); // a taste of it
    }

    private async Task ChooseSoundsAsync(bool nav)
    {
        var sets = UiSound.AllSets().Where(x => x.Id != "custom").ToList();
        var names = sets.Select(x => x.Name).ToList();
        if (nav) names.Insert(0, "Same as interface sounds");
        int choice = await Host.AskAsync(nav ? "Navigation sound" : "Interface sounds",
            nav ? "The sound as the highlight moves." : "Selecting, going back, pages and pop-ups.", names.ToArray());
        if (choice < 0) return;
        string id = nav ? (choice == 0 ? "" : sets[choice - 1].Id) : sets[choice].Id;
        if (nav) _draft.NavSounds = id; else _draft.Sounds = id;
        UiSound.Preview(id.Length == 0 ? _draft.Sounds : id, move: nav);
        Changed();
    }

    private async Task ChooseModeAsync()
    {
        int choice = await Host.AskAsync("Light, dark or OLED", "OLED uses true black, which switches those pixels off on OLED screens.", MyStyles.Modes.Select(m => m.Name).ToArray());
        if (choice < 0) return;
        _draft.Mode = MyStyles.Modes[choice].Id;
        Changed();
    }

    private void ChoosePalette()
    {
        var options = new List<(string Id, string Name, string About)>
        {
            ("style", "From the starting style", "The starting style's own colors"), ("plain", "SquareDeck gray", "SquareDeck's plain grays")
        };
        options.AddRange(Theme.Palettes.Select(p => (p.Key, p.Value.Name, "A color theme")));
        Host.Push(new PreviewPickerScreen("Color theme", "The background, text, buttons and tiles, in light, dark or OLED as you chose.",
            options.ToArray(), () => _draft.Palette, id => Mini(d => d.Palette = id), id => { _draft.Palette = id; _dirty = true; }));
    }

    private async Task ChooseAccentAsync()
    {
        var colors = Theme.AccentChoices.Concat(MyStyles.MoreAccents).ToArray();
        var names = new List<string> { "The colors' own" };
        names.AddRange(colors.Select(c => c.Name));
        names.Add("Enter a color code…");
        int choice = await Host.AskAsync("Highlight color", null, names.ToArray());
        if (choice < 0) return;
        if (choice == 0) _draft.Accent = "";
        else if (choice <= colors.Length) _draft.Accent = colors[choice - 1].Hex;
        else
        {
            var text = await Host.PromptAsync("Highlight color", "A color code such as #FF3FB4.", _draft.Accent.Length > 0 ? _draft.Accent : "#", "Use");
            if (text == null) return;
            text = text.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            if (!Theme.TryParseColor(text, out _)) { await Host.AskAsync("That isn't a color code", "Use # and six letters or numbers, like #FF3FB4.", "OK"); return; }
            _draft.Accent = text.ToUpperInvariant();
        }
        Changed();
    }

    private void ChooseFont()
    {
        var start = MyStyles.BuiltIn(_draft.BasedOn);
        var list = SettingsScreen.UiFonts.Select(f => f.Id == "style" ? (f.Id, "From the starting style", "The starting style's own font") : f).ToList();
        // The fonts the other styles come with (the Mythical ones' own typefaces too).
        foreach (var st in MyStyles.Everything())
        {
            if (string.IsNullOrEmpty(st.Font) || list.Any(o => o.Item1 == st.Font)) continue;
            list.Add((st.Font, FontName(st.Font), "From " + st.Name + " (" + MyStyles.TierOf(st) + ")"));
        }
        var options = list.ToArray();
        Host.Push(new PreviewPickerScreen("Font", "Used everywhere in SquareDeck with this style.", options, () => _draft.Font,
            id => PreviewPickerScreen.Font(id == "style" ? start.Font : id), id => { _draft.Font = id; _dirty = true; }));
    }

    private async Task ChooseCornersAsync()
    {
        var names = new List<string> { "From the starting style" };
        names.AddRange(MyStyles.CornerChoices.Select(c => c.Name));
        int choice = await Host.AskAsync("Corners", "How round the tiles, rows and panels are.", names.ToArray());
        if (choice < 0) return;
        _draft.Corners = choice == 0 ? -1 : MyStyles.CornerChoices[choice - 1].Radius;
        Changed();
    }

    private Task ChooseGlyphsAsync()
    {
        var options = SquareDeck.Hints.Looks.Select(l => (l.Id, l.Name, "How controller buttons are drawn in the hints")).ToArray();
        Host.Push(new PreviewPickerScreen("Button pictures", "How controller buttons are drawn in the hints. The picture of your style shows them too.",
            options, () => _draft.Glyphs, GlyphFace, id => { _draft.Glyphs = id; _dirty = true; }));
        return Task.CompletedTask;
    }

    /// <summary>The four face buttons and the bumpers in a look, with the hint line under them as it would read.</summary>
    private static FrameworkElement GlyphFace(string look)
    {
        var grid = new Grid();
        grid.SetResourceReference(Panel.BackgroundProperty, "CardBrush");
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var b in new[] { "A", "B", "X", "Y", "LB", "RB" }) row.Children.Add(GlyphPicture(look, b, 34));
        stack.Children.Add(row);
        var hint = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var (b, words) in new[] { ("A", "Start"), ("B", "Back") })
        {
            hint.Children.Add(GlyphPicture(look, b, 20));
            var t = Ui.Text(words, 14, "FgBrush");
            t.Margin = new Thickness(4, 0, 14, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            hint.Children.Add(t);
        }
        stack.Children.Add(hint);
        grid.Children.Add(stack);
        return grid;
    }

    private static FrameworkElement GlyphPicture(string look, string button, double size)
    {
        if (SquareDeck.Hints.PreviewFile(look, button) is { } file && ImageLoader.FromFile(file, 96) is { } picture)
        {
            var img = new Image { Source = picture, Width = size, Height = size, Margin = new Thickness(3), Stretch = Stretch.Uniform };
            if (look == "pixel") RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            return img;
        }
        // Simple: the letter on a filled disc, as the hint bar draws it.
        var ring = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Margin = new Thickness(3) };
        ring.SetResourceReference(Border.BackgroundProperty, "FgBrush");
        var letter = Ui.Text(button, size * (button.Length > 1 ? 0.36 : 0.5), "BgBrush", bold: true);
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        letter.VerticalAlignment = VerticalAlignment.Center;
        ring.Child = letter;
        return ring;
    }


    private void ChooseArt() =>
        Host.Push(new PreviewPickerScreen("Game art", "The shape of the games on the home row.", MyStyles.Arts, () => _draft.TileArt,
            id => PreviewPickerScreen.Art(id, "medium"), id => { _draft.TileArt = id; _dirty = true; }));

    private void ChooseEffect()
    {
        var options = MyStyles.Effects.Select(e => (e.Id, e.Name, e.About)).ToArray();
        Host.Push(new PreviewPickerScreen("Effect", "A pattern over the screen and a gentle moving decoration behind the home screen.", options, () => _draft.Effect,
            id => Mini(d => d.Effect = id, MyStyles.Effects.First(e => e.Id == id).About), id => { _draft.Effect = id; _dirty = true; }));
    }

    /// <summary>The home button icons: the symbols, or another style's drawn icons.</summary>
    private Task ChooseIconsAsync()
    {
        var sets = MyStyles.IconSets();
        bool startHas = MyStyles.BuiltIn(_draft.BasedOn).Skin != null;
        var options = new List<(string Id, string Name, string About)> { ("", startHas ? "From the starting style" : "Plain symbols", startHas ? "The starting style's own artwork" : "SquareDeck's symbols") };
        options.AddRange(sets.Select(x => (x.Id, x.Name + "  \u00B7  " + MyStyles.TierOf(x), "Its drawn home button icons")));
        Host.Push(new PreviewPickerScreen("Icons", "The pictures on the home screen's round buttons." +
            (startHas ? " Your starting style has artwork of its own, so its icons are used; start from a Basic style to borrow another style's." : ""),
            options.ToArray(), () => _draft.IconsFrom, id => Mini(d => d.IconsFrom = id), id => { _draft.IconsFrom = id; _dirty = true; }));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Remix: pick any ready-made style (Legendary or Mythical), then which of its pieces to take: its background, music,
    /// interface and navigation sounds, font and icons, one at a time or all together.
    /// </summary>
    private Task RemixAsync()
    {
        var options = MyStyles.Everything().Select(s => (s.Id, s.Name + "  ·  " + MyStyles.TierOf(s), s.Description)).ToArray();
        PreviewPickerScreen? picker = null;
        picker = new PreviewPickerScreen("Take pieces from a style", "Choose the style to borrow from. Next you choose which of its pieces to take.",
            options, () => "", id => new Viewbox { Width = PreviewPickerScreen.W, Height = PreviewPickerScreen.H, Stretch = Stretch.UniformToFill, Child = UiStyleScreen.Preview(UiStyles.Find(id)) },
            id =>
            {
                Host.Pop(picker!);
                _ = TakePiecesAsync(UiStyles.Find(id));
            });
        Host.Push(picker);
        return Task.CompletedTask;
    }

    private async Task TakePiecesAsync(UiStyle from)
    {
        await Task.Delay(150); // the picker closes first
        bool hasIcons = MyStyles.IconFolder(from.Id) != null;
        var pieces = new List<(string Id, string Name)>
        {
            ("all", "All of these"),
            ("background", from.Moving.Length > 0 ? "Its moving background" : from.Wallpaper.Length > 0 ? "Its wallpaper" : "Its background (plain)"),
            ("music", from.Music.Length > 0 ? "Its music: " + MusicName(from.Music) : "Its music (none)"),
            ("sounds", "Its interface sounds: " + UiSound.SetName(from.Sounds)),
            ("nav", "Its navigation sound: " + UiSound.SetName(from.NavSounds ?? from.Sounds)),
            ("font", "Its font: " + FontName(from.Font)),
        };
        if (hasIcons) pieces.Add(("icons", "Its icons"));
        while (true)
        {
            int choice = await Host.AskAsync("From " + from.Name, "Pick a piece to take. You can take more than one; choose Done when you're finished.", pieces.Select(p => p.Name).Append("Done").ToArray());
            if (choice < 0 || choice >= pieces.Count) break;
            string what = pieces[choice].Id;
            bool all = what == "all";
            if (all || what == "background") { _draft.Wallpaper = from.Wallpaper; _draft.Moving = from.Moving; }
            if (all || what == "music") _draft.Music = from.Music;
            if (all || what == "sounds") _draft.Sounds = from.Sounds;
            if (all || what == "nav") _draft.NavSounds = from.NavSounds ?? "";
            if (all || what == "font") _draft.Font = from.Font;
            if ((all || what == "icons") && hasIcons) _draft.IconsFrom = from.Id;
            _dirty = true;
            UpdatePreview();
            if (what is "sounds" or "nav") UiSound.Preview(what == "nav" ? (from.NavSounds ?? from.Sounds) : from.Sounds, move: what == "nav");
            else UiSound.Select();
            if (all) break;
        }
        Rebuild();
    }

    private async Task ChooseFromAsync(string title, string? message, (string Id, string Name)[] options, Action<string> set, Action<string>? play = null)
    {
        int choice = await Host.AskAsync(title, message, options.Select(o => o.Name).ToArray());
        if (choice < 0) return;
        set(options[choice].Id);
        Changed();
        // The choice, shown straight away (the pop-up has closed by now).
        if (play != null) { await Task.Delay(250); try { play(options[choice].Id); } catch (Exception) { } }
    }

    private async Task ChooseLockScreenAsync()
    {
        bool picture = _draft.Wallpaper.Length > 0 && !_draft.Wallpaper.StartsWith("preset:");
        var names = new List<string> { "Leave it as it is" };
        if (picture) names.Add("Use this style's wallpaper");
        names.Add("One of SquareDeck's pictures…");
        names.Add("A picture of your own…");
        int choice = await Host.AskAsync("Windows lock screen", "The picture Windows shows when the device is locked, set when you choose this style.", names.ToArray());
        if (choice < 0) return;
        string pickedName = names[choice];
        if (choice == 0) _draft.LockScreen = "";
        else if (pickedName == "Use this style's wallpaper") _draft.LockScreen = "wallpaper";
        else if (pickedName.StartsWith("One of"))
        {
            var options = Theme.PictureWallpapers.Select(p => ("image:" + p.File, p.Name, "Picture")).ToArray();
            Host.Push(new PreviewPickerScreen("Lock screen picture", "One of the pictures that come with SquareDeck.", options, () => _draft.LockScreen,
                BackgroundFace, id => { _draft.LockScreen = id; _dirty = true; }));
            return;
        }
        else
        {
            if (await Host.PickFileAsync("Choose a picture for the lock screen", new[] { ".jpg", ".jpeg", ".png", ".bmp" }) is not { } file) return;
            try { _draft.LockScreen = MyStyles.Keep(_draft.Id, file, "lockscreen"); }
            catch (Exception ex) { await Host.AskAsync("Couldn't use that picture", ex.Message, "OK"); return; }
        }
        Changed();
    }

    private async Task RenameAsync()
    {
        var name = await Host.PromptAsync("Name your style", null, _draft.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        _draft.Name = MyStyles.UniqueName(Host.Settings, name, _draft.Id);
        Changed();
    }

    private async Task RestartAsync()
    {
        var options = MyStyles.Everything().Select(s => (s.Id, s.Name + "  \u00B7  " + MyStyles.TierOf(s), s.Description)).ToArray();
        var picker = new PreviewPickerScreen("Starting point", "Start again from any ready-made style, Legendary or Mythical: everything here changes to that style's choices (the name stays).",
            options, () => _draft.BasedOn, id => new Viewbox { Width = PreviewPickerScreen.W, Height = PreviewPickerScreen.H, Stretch = Stretch.UniformToFill, Child = UiStyleScreen.Preview(UiStyles.Find(id)) },
            id =>
            {
                var fresh = CustomStyle.From(UiStyles.Find(id), Host.Settings, _draft.Name);
                fresh.Id = _draft.Id;
                CopyInto(fresh, _draft);
                _dirty = true;
            });
        Host.Push(picker);
        await Task.CompletedTask;
    }

    // ================================================================ saving

    private async Task SaveAsync(bool use)
    {
        var s = Host.Settings;
        if (_isNew && _draft.Name == "My style")
        {
            var name = await Host.PromptAsync("Name your style", "To find it again in Themes.", _draft.Name);
            if (name == null) return;
            _draft.Name = name;
        }
        _draft.Name = MyStyles.UniqueName(s, _draft.Name, _draft.Id);
        _draft.Saved = DateTime.Now;
        int at = s.CustomStyles.FindIndex(c => c.Id == _draft.Id);
        if (at >= 0) s.CustomStyles[at] = _draft.Copy(); else s.CustomStyles.Insert(0, _draft.Copy());
        s.Save();
        MyStyles.Load(s);
        _dirty = false;
        UiSound.Select();
        Host.Rumble(50);
        // Using it now, or it's the one in use: (re)apply so the changes show.
        if (use || s.UiStyle == _draft.Id) Host.UseStyle(UiStyles.Find(_draft.Id));
        Host.ShowToast("Themes", _draft.Name + (use ? " is in use" : " saved"), use ? "Your style, with everything you chose" : "It's in Your styles", "", "info", remember: false);
        _saved?.Invoke();
        Host.Pop(this);
    }

    private async Task ExportAsync()
    {
        if (_dirty && await Host.AskAsync("Save first?", "Export saves the style as it was last saved.", "Save and export", "Cancel") != 0) return;
        if (_dirty)
        {
            var s = Host.Settings;
            int at = s.CustomStyles.FindIndex(c => c.Id == _draft.Id);
            if (at >= 0) s.CustomStyles[at] = _draft.Copy();
            s.Save();
            MyStyles.Load(s);
            _dirty = false;
        }
        await ExportStyleAsync(Host, _draft);
    }

    /// <summary>Asks where to put the file and exports (shared with the Themes page).</summary>
    public static async Task ExportStyleAsync(MainWindow host, CustomStyle style)
    {
        var folder = await host.PickFolderAsync("Choose where to save " + style.Name);
        if (folder == null) return;
        try
        {
            string path = MyStyles.Export(style, folder);
            UiSound.Select();
            host.ShowToast("Themes", style.Name + " exported", path, "", "info", remember: false);
        }
        catch (Exception ex) { await host.AskAsync("Couldn't export the style", ex.Message, "OK"); }
    }

    private async Task DeleteAsync()
    {
        if (await Host.AskAsync("Delete " + _draft.Name + "?", "This can't be undone. Export it first if you might want it again.", "Delete", "Cancel") != 0) return;
        var s = Host.Settings;
        bool inUse = s.UiStyle == _draft.Id;
        MyStyles.Delete(s, _draft);
        s.Save();
        MyStyles.Load(s);
        if (inUse) Host.UseStyle(UiStyles.All[0]);
        _saved?.Invoke();
        Host.Pop(this);
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.X) { _ = SaveAsync(use: true); return true; }
        if (action == NavAction.Back && _dirty) { _ = LeaveAsync(); return true; }
        return false;
    }

    private async Task LeaveAsync()
    {
        int choice = await Host.AskAsync("Leave without saving?", "Your changes to " + _draft.Name + " aren't saved.", "Save and use", "Save", "Leave without saving", "Keep editing");
        if (choice == 0) await SaveAsync(use: true);
        else if (choice == 1) await SaveAsync(use: false);
        else if (choice == 2)
        {
            // A new style that's never been saved leaves nothing behind.
            if (_isNew && Host.Settings.CustomStyles.All(c => c.Id != _draft.Id))
                try { if (Directory.Exists(MyStyles.FolderFor(_draft.Id))) Directory.Delete(MyStyles.FolderFor(_draft.Id), true); } catch (Exception) { }
            _dirty = false;
            Host.Pop(this);
        }
    }
}
