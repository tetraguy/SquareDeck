using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>Arranges the home screen for the chosen UI style (Settings > Appearance > UI style).</summary>
public partial class MainWindow
{
    private bool _tallLayout;
    private bool _styleHooked;

    /// <summary>Lays the home screen out for <see cref="UiStyles.Current"/>. Colors and shared resources are set by Theme.Apply.</summary>
    public void ApplyUiStyle()
    {
        var s = UiStyles.Current;
        if (!_styleHooked)
        {
            _styleHooked = true;
            // The big title follows the info line, which changes with the selection.
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
                .AddValueChanged(InfoText, (_, _) => UpdateStyleTitle());
        }

        try { FontFamily = new FontFamily(s.Font); }
        catch (Exception) { FontFamily = new FontFamily("Segoe UI"); }
        ApplyRowHeights();

        // Styles with a short top bar pull it up and shrink it a little, so it clears the games row.
        bool shortTop = s.Rows[0] < 100;
        TopBar.Margin = shortTop ? new Thickness(56, 10, 56, 0) : new Thickness(62, 34, 62, 0);
        TopBar.LayoutTransform = shortTop ? new ScaleTransform(0.82, 0.82) : Transform.Identity;

        // The games row: tile size, where it sits, how much the selected tile grows.
        Motion.GrowOrigin = s.GrowOrigin;
        _tileScale = FitTileScale(s);
        double scale = _tileScale;
        GameList.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);
        GameList.VerticalAlignment = s.GamesAlign;
        Motion.SelectGrow = s.SelectGrow;
        double rowLeft = RowSideMargin * scale + 8 * scale + s.TileGap * scale;

        RowHeader.Text = s.RowHeader;
        RowHeader.Visibility = s.RowHeader.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RowHeader.Margin = new Thickness(rowLeft, 0, 0, (270 + _rowPad.Bottom) * scale + 4);

        // The round buttons.
        bool left = s.ButtonsAlign == HorizontalAlignment.Left;
        ButtonRow.HorizontalAlignment = s.ButtonsAlign;
        ButtonRow.Margin = new Thickness(left ? 88 : 0, 0, 0, 0);
        ButtonLabel.HorizontalAlignment = s.ButtonsAlign;
        ButtonLabel.Margin = new Thickness(left ? 104 : 0, 0, 0, 2);
        ButtonLabel.Visibility = s.Buttons is "card" or "tab" ? Visibility.Collapsed : Visibility.Visible;

        // Where the game's name goes.
        bool big = s.Title == "big", beside = s.Title == "row";
        StylePanel.Visibility = big || beside ? Visibility.Visible : Visibility.Collapsed;
        InfoText.Visibility = big || beside ? Visibility.Hidden : Visibility.Visible;
        StyleTitle.FontSize = big ? 54 : 20;
        StyleInfo.FontSize = big ? 18 : 13;
        StylePanel.VerticalAlignment = big ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        StylePanel.Margin = big ? new Thickness(96, 0, 0, 14) : new Thickness(rowLeft, 4, 0, 0);
        StyleTitle.Effect = big && s.HeroOpacity > 0.3 ? new DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 } : null;
        PlayPill.Visibility = s.PlayPill ? Visibility.Visible : Visibility.Collapsed;
        UpdateStyleTitle();

        // Artwork behind the home screen.
        HeroBlur.Radius = s.HeroBlur;
        // Performance mode: no live blur; the artwork is loaded tiny and stretched, which softens it for free.
        HeroImage.Effect = s.HeroBlur > 0 && !Perf.Active ? HeroBlur : null;
        HeroImage.Margin = new Thickness(s.HeroBlur > 0 ? -40 : 0);
        HeroScrim.Fill = HeroScrimBrush(s);
        HeroScrim.Visibility = HeroScrim.Fill == null ? Visibility.Collapsed : Visibility.Visible;

        // Footer: a line, a full-width bar, a floating pill, or nothing.
        FooterLine.Visibility = s.Footer == "line" ? Visibility.Visible : Visibility.Collapsed;
        FooterGrid.Margin = s.Footer == "bar" ? new Thickness(0) : s.Footer == "pill" ? new Thickness(40, 8, 40, 12) : new Thickness(30, 0, 30, 0);
        FooterGrid.Background = s.Footer == "bar" ? new SolidColorBrush(Color.FromArgb(0xF2, 0x0A, 0x0D, 0x12)) : null;
        FrostPanel(FooterPill, s.Footer == "pill");

        // Frosted: one shelf behind all the buttons, sized to them.
        FrostPanel(ButtonDock, s.ButtonDock);
        if (s.ButtonDock)
        {
            ButtonRow.Margin = new Thickness(0);
            ButtonDock.Padding = new Thickness(0);
            ButtonDock.Width = double.NaN;
            ButtonRow.SizeChanged -= SizeDockToButtons;
            ButtonRow.SizeChanged += SizeDockToButtons;
            SizeDockToButtons(null, null);
        }

        // Scan lines / grid over everything.
        StyleOverlay.Fill = UiStyles.OverlayBrush(s);
        StyleOverlay.Visibility = StyleOverlay.Fill == null ? Visibility.Collapsed : Visibility.Visible;

        // Glass looks through a heavily blurred wallpaper.
        // A style's own picture (Leonida) stays sharp on the home screen, with a light wash so text stays readable.
        bool picture = s.Wallpaper.Length > 0 && (Settings.Wallpaper.Length == 0 || Settings.Wallpaper == s.Wallpaper);
        bool blur = s.Glass && !picture;
        WallpaperLayer.Effect = blur && !Perf.Active ? new BlurEffect { Radius = 40, RenderingBias = RenderingBias.Performance } : null;
        WallpaperLayer.Margin = new Thickness(blur ? -60 : 0);
        WallpaperScrim.Opacity = picture ? 0.3 : s.Glass ? 0.12 : s.Backdrop != null && string.IsNullOrEmpty(Settings.Wallpaper) ? 0.1 : 0.4;

        // Fetch the artwork again at the size this style needs.
        _heroKey = null;
        if (Settings.HeroBackgrounds) _ = ShowHeroAsync((GameList.SelectedItem as Game)?.Actual);
        else ClearHero();
        ApplyLayout(s);
        UpdateButtonHighlight();
        UpdateLayerCaches();
        if (_dynamicVideo != null) ApplyDynamicBackground(); // keeps its lighter scrim
    }

    /// <summary>The style's row heights, a little taller at the top on 16:10 screens. Row 5 takes what's left.</summary>
    private void ApplyRowHeights()
    {
        var s = UiStyles.Current;
        var rows = (double[])s.Rows.Clone();
        // The game's name beside the row needs room for two lines (name and details), or the details get cut off.
        const double TitleRow = 60;
        if (s.Title == "row" && rows[2] < TitleRow)
        {
            rows[1] -= TitleRow - rows[2];
            rows[2] = TitleRow;
        }
        for (int i = 0; i < 7 && i < RootGrid.RowDefinitions.Count; i++)
        {
            if (i == 5) { RootGrid.RowDefinitions[i].Height = new GridLength(1, GridUnitType.Star); continue; }
            double h = rows[i];
            if (_tallLayout && i == 0) h += 24;
            if (_tallLayout && s.Id == "switch" && i is 2 or 4) h += 11;
            RootGrid.RowDefinitions[i].Height = new GridLength(h);
        }
        _rowsNow = rows;
    }

    private double[] _rowsNow = new double[7];
    private double _tileScale = 1;
    private Thickness _rowPad;

    /// <summary>
    /// How big the tiles can be so the selected one, grown and with its ring and glow, still fits inside the games
    /// row. (The row clips anything past its edges, which cut off the bottom of the highlighted tile.) Also works out
    /// the space kept above and below the tiles for that.
    /// </summary>
    private double FitTileScale(UiStyle s)
    {
        double grow = Math.Max(s.SelectGrow, Motion.TileStyle == "grow" ? 1.08 : 1.0);
        var origin = s.GrowOrigin ?? (grow > 1.1 ? new Point(0.5, 0.92) : new Point(0.5, 0.5));
        double extra = 270 * (grow - 1);
        // How far the ring's glow reaches past the tile. The ring is drawn inside the tile's gap, its glow spreads about
        // its blur radius beyond that, and all of it grows with the tile. (A fixed 18 used to cut the glow off in
        // styles with a strong glow, tiles close together and a big grow: Steam, Waves, Xenon, Grids and others.)
        double glow = s.Glow && !Perf.Active ? (s.Glass ? 20 : 24) : 0;
        double Ring = Math.Max(18, (glow + s.RingThickness - s.TileGap) * grow + 8);
        double top = Math.Max(0, extra * origin.Y + Ring - 48); // the name area above each tile is free room
        double bottom = extra * (1 - origin.Y) + Ring;
        _rowPad = new Thickness(0, top, 0, bottom);
        // The list's own frame has a fixed 1-pixel padding at the top and bottom (whatever its Padding says). Leaving it
        // out made each tile 2 pixels shorter than its 48 + 270, so the bottom of the ring was clipped (Switch and the
        // other styles where the row is filled exactly). Plus a little for rounding on scaled screens.
        const double ListChrome = 2 + 2;
        double needed = top + 318 + bottom + ListChrome;
        double tall = _tallLayout ? 24 : 0;
        double available = s.Layout is "hero" or "sidebar"
            ? RootGrid.Height - _rowsNow.Where((_, i) => i != 5).Sum() - tall // the row along the bottom takes what's left
            : _rowsNow[1];
        return Math.Max(0.3, Math.Min(s.TileScale, available / needed));
    }

    /// <summary>A frosted rounded panel (Frosted style), or hidden.</summary>
    private static void FrostPanel(Border panel, bool on)
    {
        panel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) return;
        panel.Background = UiStyles.ShelfBrush(UiStyles.Current);
        panel.BorderBrush = UiStyles.RimBrush(UiStyles.Current);
        panel.BorderThickness = new Thickness(1.2);
        panel.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.3 };
    }

    private void SizeDockToButtons(object? sender, SizeChangedEventArgs? e)
    {
        if (!UiStyles.Current.ButtonDock) return;
        double k = ButtonRow.LayoutTransform is ScaleTransform t ? t.ScaleX : 1;
        ButtonDock.Width = ButtonRow.ActualWidth * k + 28;
        // Taller below than above, so the dot under the current view (Home, Favorites, Apps) sits inside the shelf.
        ButtonDock.Height = Math.Max(0, ButtonRow.ActualHeight * k + 40);
        ButtonDock.Margin = new Thickness(0, 12, 0, 0);
    }

    private static Brush? HeroScrimBrush(UiStyle s)
    {
        var bg = (Application.Current.Resources["BgBrush"] as SolidColorBrush)?.Color ?? Colors.Black;
        Color A(byte alpha) => Color.FromArgb(alpha, bg.R, bg.G, bg.B);
        switch (s.HeroScrim)
        {
            case "bottom":
            {
                var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                brush.GradientStops.Add(new GradientStop(A(0x40), 0));
                brush.GradientStops.Add(new GradientStop(A(0x00), 0.22));
                brush.GradientStops.Add(new GradientStop(A(0x10), 0.45));
                brush.GradientStops.Add(new GradientStop(A(0xC8), 0.8));
                brush.GradientStops.Add(new GradientStop(A(0xF2), 1));
                return brush;
            }
            case "left":
            {
                var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.3), EndPoint = new Point(1, 0.7) };
                brush.GradientStops.Add(new GradientStop(A(0xD8), 0));
                brush.GradientStops.Add(new GradientStop(A(0x70), 0.38));
                brush.GradientStops.Add(new GradientStop(A(0x00), 0.7));
                return brush;
            }
            default:
                return null;
        }
    }

    /// <summary>The big title (PlayStation) or the line beside the row (Steam): the highlighted game's name and details.</summary>
    private void UpdateStyleTitle()
    {
        if (StylePanel.Visibility != Visibility.Visible) return;
        var game = GameList.SelectedItem as Game;
        StyleTitle.Text = game?.Name ?? "";
        _ = ShowStyleLogoAsync(game);
        StyleInfo.Text = InfoText.Text;
        PlayPill.Visibility = UiStyles.Current.PlayPill && game != null && !game.IsActionTile ? Visibility.Visible : Visibility.Collapsed;
        if (PlayPill.Child is TextBlock label) label.Text = game?.IsRunning == true ? "Resume" : game?.IsGame == true ? "Play Game" : "Open";
        UpdateLayoutDetails();
    }

    private readonly Dictionary<string, ImageSource?> _logoCache = new();
    private string? _logoKey;

    /// <summary>After a tall grid, hero or logo was picked for a game: shows the new one wherever it is on screen.</summary>
    public void RefreshArt(Game item, string kind)
    {
        var selected = GameList.SelectedItem as Game;
        switch (kind)
        {
            case "heroes":
                _heroKey = null;
                if (selected != null) _ = ShowHeroAsync(selected.Actual);
                break;
            case "logos":
                _logoCache.Remove(item.Key);
                _logoKey = null;
                _ = ShowStyleLogoAsync(selected);
                break;
            default:
                foreach (var g in _view.Where(g => g.Actual.Key == item.Key).Append(item).Distinct())
                {
                    g.PortraitCover = null;
                    g.PortraitTried = false;
                }
                StartPortraits();
                break;
        }
    }

    /// <summary>Cinema: the game's logo (from SteamGridDB) in place of its name. The name shows until it arrives, or if there is none.</summary>
    private async Task ShowStyleLogoAsync(Game? game)
    {
        var actual = game?.Actual;
        if (!UiStyles.Current.TitleLogo || actual == null || !actual.IsGame)
        {
            _logoKey = null;
            StyleLogo.Visibility = Visibility.Collapsed;
            StyleTitle.Visibility = Visibility.Visible;
            return;
        }
        string key = actual.Key;
        if (key == _logoKey) return;
        _logoKey = key;

        if (!_logoCache.TryGetValue(key, out var logo))
        {
            StyleLogo.Visibility = Visibility.Collapsed;
            StyleTitle.Visibility = Visibility.Visible;
            try
            {
                logo = await Task.Run(async () =>
                {
                    using var client = new SteamGridDbClient(Settings.SteamGridDbApiKey);
                    var path = await client.GetLogoAsync(actual, CancellationToken.None);
                    return path == null ? null : (ImageSource?)ImageLoader.FromFile(path, 900);
                });
            }
            catch (Exception) { logo = null; }
            _logoCache[key] = logo;
            if (_logoKey != key) return; // the highlight moved on meanwhile
        }

        StyleLogo.Source = logo;
        StyleLogo.Visibility = logo != null ? Visibility.Visible : Visibility.Collapsed;
        StyleTitle.Visibility = logo != null ? Visibility.Collapsed : Visibility.Visible;
        if (logo != null && !Theme.ReduceMotion)
            StyleLogo.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
    }

    /// <summary>
    /// Switches to a UI style. With "Match wallpaper, sounds and music" on (the default), it also brings the style's own
    /// wallpaper or moving background, interface sounds, menu music and colors, so everything fits together.
    /// </summary>
    public void UseStyle(UiStyle style)
    {
        var s = Settings;
        var leaving = UiStyles.Find(s.UiStyle);
        s.UiStyle = style.Id;
        if (s.StyleMatching)
        {
            // Its button pictures (the pixel styles bring pixel-art buttons); leaving such a style goes back to plain.
            if (style.Glyphs != null) s.GlyphLook = style.Glyphs;
            else if (leaving.Glyphs != null && s.GlyphLook == leaving.Glyphs) s.GlyphLook = "text";
            Hints.SetLook(s.GlyphLook);
            s.AccentColor = "";             // the style's own highlight color
            s.ThemePalette = "";            // and its own colors (a color theme would replace them)
            s.Wallpaper = style.Wallpaper;  // its picture, gradient or none (its own backdrop shows then)
            // Its theme too (Light / Dark / OLED), unless high contrast is on.
            if (style.ThemeMode != null && s.ThemeMode is not ("hc-dark" or "hc-light")) s.ThemeMode = style.ThemeMode;
            s.SoundSet = style.Sounds;
            s.MenuMusic = style.Music;
            UiSound.Configure(s);
            UseDynamicBackground(style.Moving);
        }
        else if (style.Wallpaper.Length > 0) s.Wallpaper = style.Wallpaper; // a style built around its own picture
        // Styles built around the game's artwork show it, unless a moving background has taken its place.
        if (style.WantsHero && s.DynamicBackground.Length == 0) s.HeroBackgrounds = true;
        s.Save();
        ApplyLook();
        UpdateMenuMusic();
    }

    private void PlayPill_Click(object sender, MouseButtonEventArgs e)
    {
        if (GameList.SelectedItem is Game game) _ = ActivateAsync(game);
    }
}
