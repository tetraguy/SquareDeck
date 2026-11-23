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

    /// <summary>Advanced styles with icons of their own (Glow, Hologram) draw them on the round buttons in place of the symbols.</summary>
    private void ApplySkinIcons()
    {
        var skin = Skins.Current;
        var scaling = skin?.IconPixel == true ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;
        foreach (var (button, name) in new (Button, string)[]
        {
            (HomeBtn, "home"), (FavBtn, "favorites"), (AppsBtn, "apps"), (CapturesBtn, "captures"), (MusicBtn, "music"),
            (FriendsBtn, "friends"), (NewsBtn, "news"), (BrowserBtn, "browser"), (ExplorerBtn, "explorer"),
            (MyDeviceBtn, "device"), (StoresBtn, "stores"), (SettingsBtn, "settings"), (PowerBtn, "power")
        })
        {
            SkinParts.SetIconScaling(button, scaling);
            bool has = skin != null && skin.Icons.TryGetValue(name, out var icon) && SetIcon(button, icon);
            if (!has) SkinParts.SetIcon(button, null);
            // A symbol the kit has no icon for takes the kit's icon color (Fatanzia's white), then goes back to its own.
            _glyphColors.TryAdd(button, button.ReadLocalValue(Button.ForegroundProperty));
            var own = _glyphColors[button];
            if (!has && skin?.GlyphColor is { } white) button.Foreground = white;
            else if (own is Brush brush) button.Foreground = brush;
            else button.SetResourceReference(Button.ForegroundProperty, "SubtleBrush"); // Stores, Settings and Power
        }

        static bool SetIcon(Button b, ImageSource icon) { SkinParts.SetIcon(b, icon); return true; }
    }

    private readonly Dictionary<Button, object> _glyphColors = new();

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

        try { FontFamily = AppFonts.Family(s.Font); }
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
        ButtonLabel.Visibility = s.Buttons is "card" or "tab" or "wide" ? Visibility.Collapsed : Visibility.Visible;
        ApplySkinIcons();

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
        HeroImage.Effect = s.HeroBlur > 0 && !Perf.Active && !_heroPreBlurred ? HeroBlur : null;
        HeroImage.Margin = new Thickness(s.HeroBlur > 0 ? -40 : 0);
        HeroScrim.Fill = HeroScrimBrush(s);
        HeroScrim.Visibility = HeroScrim.Fill == null ? Visibility.Collapsed : Visibility.Visible;

        // Footer: a line, a full-width bar, a floating pill, or nothing.
        ApplyFooter(s);

        // One shelf behind all the buttons, sized to them: frosted, solid, outlined or glowing.
        FrostPanel(ButtonDock, s.ButtonDock);
        if (s.ButtonDock) DockLook(s.DockLook);
        if (s.ButtonDock)
        {
            ButtonRow.Margin = new Thickness(0);
            ButtonDock.Padding = new Thickness(0);
            ButtonDock.Width = double.NaN;
            ButtonRow.SizeChanged -= SizeDockToButtons;
            ButtonRow.SizeChanged += SizeDockToButtons;
            SizeDockToButtons(null, null);
        }

        // Gentle animated decoration behind the home screen (stars, radar, leaves...).
        ApplyAmbient(s);

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
        double grow = Math.Max(s.SelectGrow, Motion.HighlightGrow > 1 ? 1.08 : 1.0);
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

    /// <summary>A 90s taskbar: battleship grey with a white raised top edge.</summary>
    private static Brush Taskbar()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.Absolute };
        b.EndPoint = new Point(0, 72);
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xDF, 0xDF, 0xDF), 0));
        b.GradientStops.Add(new GradientStop(Colors.White, 1.0 / 72));
        b.GradientStops.Add(new GradientStop(Colors.White, 3.0 / 72));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xC0, 0xC0, 0xC0), 3.0 / 72));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xC0, 0xC0, 0xC0), 1));
        b.Freeze();
        return b;
    }

    private void ApplyFooter(UiStyle s)
    {
        var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.White;
        bool lineLike = s.Footer is "line" or "neonline";
        FooterLine.Visibility = lineLike ? Visibility.Visible : Visibility.Collapsed;
        FooterLine.Height = s.Footer == "neonline" ? 2 : 1;
        FooterLine.Effect = null;
        if (s.Footer == "neonline")
        {
            FooterLine.Fill = new SolidColorBrush(accent);
            FooterLine.Effect = Perf.Active ? null : new DropShadowEffect { Color = accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 1 };
        }
        else FooterLine.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "FgBrush");

        bool floating = s.Footer is "pill" or "outlinepill" or "floatbar";
        FooterGrid.Margin = s.Footer is "bar" or "glassbar" or "taskbar" or "fade" or "accentbar" ? new Thickness(0)
                          : floating ? new Thickness(40, 8, 40, 12) : new Thickness(30, 0, 30, 0);
        FooterGrid.Background = s.Footer switch
        {
            // Dark text on a light style needs a light bar to be read.
            "bar" => Theme.CurrentIsDark ? new SolidColorBrush(Color.FromArgb(0xF2, 0x0A, 0x0D, 0x12)) : new SolidColorBrush(Color.FromArgb(0xF2, 0xEE, 0xEE, 0xEE)),
            "glassbar" => UiStyles.ShelfBrush(s),
            "taskbar" => Taskbar(),
            "accentbar" => new SolidColorBrush(Color.FromArgb(0xE0, accent.R, accent.G, accent.B)),
            "fade" => FadeUp(),
            _ => null
        };

        // The floating panel behind the hints: frosted (pill), an outline, or a solid rounded bar.
        FrostPanel(FooterPill, floating);
        FooterPill.CornerRadius = new CornerRadius(s.Footer == "floatbar" ? 16 : 26);
        if (s.Footer == "outlinepill")
        {
            FooterPill.Background = Brushes.Transparent;
            FooterPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, accent.R, accent.G, accent.B));
            FooterPill.BorderThickness = new Thickness(2);
            FooterPill.Effect = null;
        }
        else if (s.Footer == "floatbar")
        {
            FooterPill.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
            FooterPill.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
            FooterPill.BorderThickness = new Thickness(1);
            FooterPill.Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Opacity = 0.45 };
        }
    }

    /// <summary>Clear at the top of the footer, darkening toward the bottom edge.</summary>
    private static Brush FadeUp()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xA0, 0, 0, 0), 0.7));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xD0, 0, 0, 0), 1));
        b.Freeze();
        return b;
    }

    private void DockLook(string look)
    {
        var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.White;
        ButtonDock.CornerRadius = new CornerRadius(look is "bevel" ? 4 : look is "sticker" ? 22 : look is "underline" ? 0 : 50);
        switch (look)
        {
            case "smoke":
                ButtonDock.Background = new SolidColorBrush(Color.FromArgb(0xA8, 0x0C, 0x0E, 0x14));
                ButtonDock.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
                ButtonDock.BorderThickness = new Thickness(1);
                ButtonDock.Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.45 };
                break;
            case "gradient":
            {
                var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, accent.R, accent.G, accent.B), 0));
                fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, accent.R, accent.G, accent.B), 1));
                ButtonDock.Background = fade;
                ButtonDock.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
                ButtonDock.BorderThickness = new Thickness(1);
                ButtonDock.Effect = Perf.Active ? null : new DropShadowEffect { Color = accent, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.4 };
                break;
            }
            case "bevel":
                ButtonDock.Background = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
                ButtonDock.BorderBrush = new LinearGradientBrush(Colors.White, Color.FromRgb(0x40, 0x40, 0x40), 45);
                ButtonDock.BorderThickness = new Thickness(3);
                ButtonDock.Effect = null;
                break;
            case "underline":
                ButtonDock.Background = Brushes.Transparent;
                ButtonDock.BorderBrush = new SolidColorBrush(accent);
                ButtonDock.BorderThickness = new Thickness(0, 0, 0, 4);
                ButtonDock.Effect = null;
                break;
            case "sticker":
                ButtonDock.Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xF7, 0xEE));
                ButtonDock.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                ButtonDock.BorderThickness = new Thickness(2.5);
                ButtonDock.Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 0, ShadowDepth = 7, Direction = 315, Opacity = 0.9, Color = Colors.Black };
                break;
            case "solid":
                ButtonDock.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
                ButtonDock.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
                ButtonDock.BorderThickness = new Thickness(1);
                ButtonDock.Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.35 };
                break;
            case "outline":
                ButtonDock.Background = Brushes.Transparent;
                ButtonDock.BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, accent.R, accent.G, accent.B));
                ButtonDock.BorderThickness = new Thickness(2);
                ButtonDock.Effect = null;
                break;
            case "glow":
                ButtonDock.Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x08, 0x08, 0x10));
                ButtonDock.BorderBrush = new SolidColorBrush(accent);
                ButtonDock.BorderThickness = new Thickness(1.5);
                ButtonDock.Effect = Perf.Active ? null : new DropShadowEffect { Color = accent, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.75 };
                break;
            // glass: FrostPanel already did it
        }
    }

    private void SizeDockToButtons(object? sender, SizeChangedEventArgs? e)
    {
        if (!UiStyles.Current.ButtonDock) return;
        double k = ButtonRow.LayoutTransform is ScaleTransform t ? t.ScaleX : 1;
        // Sized to the buttons themselves, not to the row they sit in: the row is often much taller than the buttons
        // (the shelf came out far too tall in some styles and ran off the bottom of its row).
        double width = 0, tallest = 0;
        foreach (UIElement child in ButtonRow.Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var m = (child as FrameworkElement)?.Margin ?? new Thickness();
            width += child.DesiredSize.Width;
            tallest = Math.Max(tallest, child.DesiredSize.Height - m.Top - m.Bottom);
        }
        if (width <= 0 || tallest <= 0) return;
        ButtonDock.Width = width * k + 28;
        // A little taller below than above, so the dot under the current view (Home, Favorites, Apps) sits inside it.
        double rowHeight = Grid.GetRow(ButtonRow) < RootGrid.RowDefinitions.Count ? RootGrid.RowDefinitions[Grid.GetRow(ButtonRow)].ActualHeight : 0;
        double height = tallest * k + 26;
        if (rowHeight > 0) height = Math.Min(height, rowHeight + 6);
        ButtonDock.Height = height;
        ButtonDock.Margin = new Thickness(0, 6, 0, 0);
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
            case "headers":
                // The wide art on home tiles (Rectangle) and in the library's Rectangle view.
                foreach (var g in _view.Where(g => g.Actual.Key == item.Key).Append(item).Append(item.Actual).Distinct())
                {
                    g.HeaderCover = null;
                    g.HeaderTried = false;
                }
                Screens.LibraryScreen.ForgetArt();
                StartPortraits();
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
    /// <summary>Switches to a look (Settings > Themes): its UI style, colors, sounds and music.</summary>
    public void UseLook(Look look)
    {
        Settings.LookId = look.Id;
        Looks.Select(look.Id);
        // The look's own library layout and art (you can still change them in the library afterwards).
        Settings.LibraryLayout = look.Design.LibraryLayout;
        Settings.LibraryView = look.Design.LibraryView;
        // Its home screen, colors, sounds and music. A look built around the game's artwork shows it, with the
        // moving background set aside (a moving background would cover it).
        var style = UiStyles.Find(look.StyleId);
        if (style.WantsHero && Settings.StyleMatching) UseDynamicBackground("");
        UseStyle(style); // saves and redraws
    }

    public void UseStyle(UiStyle style)
    {
        var s = Settings;
        var leaving = UiStyles.Find(s.UiStyle);
        s.UiStyle = style.Id;
        s.DarkTheme = Theme.ShouldBeDark(s); // each style other than Switch has its own light or dark
        // A style of your own always brings everything you chose for it.
        var mine = s.CustomStyles.FirstOrDefault(c => c.Id == style.Id);
        if (mine != null) MyStyles.ApplyExtras(s, mine);
        else if (s.StyleMatching || MyStyles.IsCustom(leaving.Id))
        {
            // Its own sound for moving around (most ready-made styles use one set for everything), and how its selected game stands out.
            s.NavSoundSet = style.NavSounds ?? "";
            if (style.TileAnimation != null) s.TileHighlight = style.TileAnimation;
            else if (leaving.TileAnimation != null && s.TileHighlight == leaving.TileAnimation) s.TileHighlight = "pulse";
        }
        if (s.StyleMatching || mine != null)
        {
            // Its button pictures (the pixel styles bring pixel-art buttons); leaving such a style goes back to plain.
            if (style.Glyphs != null) s.GlyphLook = style.Glyphs;
            else if (leaving.Glyphs != null && s.GlyphLook == leaving.Glyphs) s.GlyphLook = "text";
            Hints.SetLook(s.GlyphLook);
            s.AccentColor = "";             // the style's own highlight color
            s.ThemePalette = style.Id == "switch" ? "steel" : ""; // and its own colors (Switch's own is the Steel color theme)
            s.Wallpaper = style.Wallpaper;  // its picture, gradient or none (its own backdrop shows then)
            // Its own theme (light, dark or OLED) comes with it automatically (Theme.EffectiveMode); your choice for
            // Switch is kept for when you go back to it.
            s.SoundSet = style.Sounds;
            s.MenuMusic = style.Music;
            UiSound.Configure(s);
            if (style.LaunchChime != null)
            {
                s.LaunchChime = style.LaunchChime;
                UiSound.SetLaunchChime(s.LaunchChime, s.LaunchChimeFile);
            }
            UseDynamicBackground(style.Moving);
        }
        else if (style.Wallpaper.Length > 0) s.Wallpaper = style.Wallpaper; // a style built around its own picture
        // Styles built around the game's artwork show it, unless a moving background has taken its place.
        if (style.WantsHero && s.DynamicBackground.Length == 0) s.HeroBackgrounds = true;
        s.Save();
        ApplyLook();
        // Animations, button pictures and music the style may have changed take effect now, not at the next start.
        ApplySettings();
        if (mine != null) _ = ApplyStyleLockScreenAsync(mine);
    }

    /// <summary>Puts a style's lock screen picture on the Windows lock screen, if it has one.</summary>
    private async Task ApplyStyleLockScreenAsync(CustomStyle style)
    {
        if (MyStyles.LockScreenFile(style) is not { } file) return;
        if (!await Screens.LockScreenScreen.SetQuietlyAsync(file, Settings))
            ShowToast("Themes", "Lock screen not changed", "Windows didn't allow it. Settings > Windows lock screen can set it with permission.", "", "info", remember: false);
    }

    private void PlayPill_Click(object sender, MouseButtonEventArgs e)
    {
        if (GameList.SelectedItem is Game game) _ = ActivateAsync(game);
    }
}
