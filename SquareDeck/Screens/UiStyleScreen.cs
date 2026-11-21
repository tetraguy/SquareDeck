using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Settings > Appearance > UI style: the whole look of the launcher, with a small picture of each.</summary>
public sealed class UiStyleScreen : GridScreen
{
    // Previews are the home screen at about 27%, sized so three cards fit in each row.
    private const double W = 340, H = W * 9 / 16, K = W / 1280;

    public UiStyleScreen() : base("UI style", "{Select}  Use this style      {Back}  Back")
    {
        var note = Ui.Text("Changes the home screen and every page: colors, type, corners, buttons and artwork. Each style comes with " +
                           "its own theme (light, dark or OLED), a matching wallpaper or moving background, interface sounds and menu music.", 16, "SubtleBrush");
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(4, 0, 0, 10);
        Body.Children.Insert(0, note);
        ShowMatch(focus: false);
    }

    private Button? _match;

    /// <summary>The "Match wallpaper, sounds and music" switch above the styles.</summary>
    private void ShowMatch(bool focus)
    {
        bool on = Host?.Settings.StyleMatching ?? true;
        var row = Ui.Row("Match theme, wallpaper, sounds and music to the style", on ? "On" : "Off", "\uE790",
            subtitle: "Light, dark or OLED theme too. Off keeps your own theme, wallpaper, sounds, music and highlight color");
        row.Margin = new Thickness(0, 0, 0, 14);
        row.Click += (_, _) =>
        {
            Host.Settings.StyleMatching = !Host.Settings.StyleMatching;
            Host.Settings.Save();
            ShowMatch(focus: true);
        };
        int at = _match != null ? Body.Children.IndexOf(_match) : 1;
        if (_match != null) Body.Children.Remove(_match);
        _match = row;
        Body.Children.Insert(Math.Max(0, at), row);
        if (focus) FocusLater(row);
        ShowSurprise();
    }

    private Button? _surprise;

    /// <summary>"Surprise me": switches to a random style other than the current one.</summary>
    private void ShowSurprise()
    {
        if (_surprise != null) Body.Children.Remove(_surprise);
        var row = Ui.Row("Surprise me", "", "\uE8B1", subtitle: "Switch to a random style you're not using now");
        row.Margin = new Thickness(0, 0, 0, 14);
        row.Click += (_, _) =>
        {
            var others = UiStyles.All.Where(x => x.Id != Host.Settings.UiStyle).ToList();
            if (others.Count == 0) return;
            Use(others[Random.Shared.Next(others.Count)]);
        };
        _surprise = row;
        int at = _match != null ? Body.Children.IndexOf(_match) + 1 : 1;
        Body.Children.Insert(Math.Max(0, at), row);
    }

    public override void OnShown()
    {
        ShowMatch(focus: false);
        Build();
    }

    private void Build()
    {
        Tiles.Children.Clear();
        var current = Host.Settings.UiStyle;
        Button? focus = null;
        foreach (var style in UiStyles.All)
        {
            var st = style;
            var card = new StackPanel();
            card.Children.Add(Preview(st));
            var name = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 10, 0, 0) };
            name.Children.Add(Ui.Text(st.Name, 18, "FgBrush", bold: true));
            if (st.Id == current)
            {
                var check = Ui.Text("", 16, "AccentBrush");
                check.FontFamily = Ui.IconFont;
                check.Margin = new Thickness(10, 3, 0, 0);
                name.Children.Add(check);
            }
            card.Children.Add(name);
            var about = Ui.Text(st.Description, 13, "SubtleBrush");
            about.TextWrapping = TextWrapping.Wrap;
            about.Height = 38;
            about.Margin = new Thickness(2, 2, 0, 0);
            card.Children.Add(about);
            var tile = AddTile(card, null, W, () => Use(st));
            if (st.Id == current) focus = tile;
        }
        FocusLater(focus ?? Tiles.Children.OfType<Button>().FirstOrDefault());
    }

    private void Use(UiStyle style)
    {
        Host.UseStyle(style);
        Build();
        // Say so: the home screen behind this page has already changed.
        UiSound.Select();
        Host.Rumble(40);
        Host.ShowToast("UI style", style.Name + " applied", Host.Settings.StyleMatching ? "With its colors, wallpaper, sounds and music" : "Your colors, wallpaper and sounds are kept", "", "info", remember: false);
    }

    /// <summary>A small drawing of the home screen in this style.</summary>
    internal static UIElement Preview(UiStyle s)
    {
        bool dark = !s.ForceLight && (s.Light == null || Theme.CurrentIsDark);
        var p = UiStyles.PaletteFor(s, dark);
        var res = Application.Current.Resources;
        Brush B(int i, string key) => p != null ? Brush(p[i]) : res[key] as Brush ?? Brushes.Gray;
        Brush bg = B(0, "BgBrush"), fg = B(1, "FgBrush"), circle = B(3, "CircleBrush"), tileBrush = B(4, "TileBrush");
        Brush accent = p != null && p[5].Length > 0 ? Brush(p[5]) : res["AccentBrush"] as Brush ?? Brushes.Cyan;
        var accentColor = (accent as SolidColorBrush)?.Color ?? Colors.Cyan;

        var c = new Canvas { Width = W, Height = H, Background = bg, ClipToBounds = true };

        // Artwork behind: a stand-in "game art" gradient.
        // A style with its own moving background shows a still from it (the pixel-art styles).
        string still = s.Moving.Length > 0 ? System.IO.Path.Combine(MainWindow.BackgroundsFolder, "previews", s.Moving + ".jpg") : "";
        if (still.Length > 0 && System.IO.File.Exists(still) && ImageLoader.FromFile(still, 480) is { } stillImage)
        {
            Add(c, new Rectangle { Width = W, Height = H, Fill = new ImageBrush(stillImage) { Stretch = Stretch.UniformToFill } }, 0, 0);
            if (s.Glyphs == "pixel") RenderOptions.SetBitmapScalingMode(c, BitmapScalingMode.NearestNeighbor); // pixel art stays crisp
        }
        else if (UiStyles.Backdrop(s) is { } backdrop) Add(c, new Rectangle { Width = W, Height = H, Fill = backdrop }, 0, 0);
        else if (s.HeroOpacity > 0.05)
        {
            var art = new RadialGradientBrush { Center = new Point(0.62, 0.35), GradientOrigin = new Point(0.62, 0.3), RadiusX = 0.7, RadiusY = 0.9 };
            art.GradientStops.Add(new GradientStop(Color.FromRgb(0xF2, 0x9A, 0x4A), 0));
            art.GradientStops.Add(new GradientStop(Color.FromRgb(0x7A, 0x3B, 0x8F), 0.45));
            art.GradientStops.Add(new GradientStop(Color.FromRgb(0x14, 0x2B, 0x4A), 1));
            Add(c, new Rectangle { Width = W, Height = H, Fill = art, Opacity = Math.Min(1, s.HeroOpacity * (s.HeroBlur > 10 ? 1.6 : 1)) }, 0, 0);
            if (s.HeroScrim != "none")
            {
                var bgc = ((SolidColorBrush)bg).Color;
                var scrim = s.HeroScrim == "left"
                    ? new LinearGradientBrush(Color.FromArgb(0xD8, bgc.R, bgc.G, bgc.B), Color.FromArgb(0, bgc.R, bgc.G, bgc.B), 0)
                    : new LinearGradientBrush(Color.FromArgb(0, bgc.R, bgc.G, bgc.B), Color.FromArgb(0xF0, bgc.R, bgc.G, bgc.B), 90);
                Add(c, new Rectangle { Width = W, Height = H, Fill = scrim }, 0, 0);
            }
        }

        // Styles that rearrange the whole screen get a sketch of their own arrangement.
        if (s.Layout != "classic") DrawLayout(c, s, fg, accent, circle);
        else
        {
        // Rows, as on the home screen.
        double y = 0;
        var rows = new double[7];
        double fixedSum = s.Rows.Where((_, i) => i != 5).Sum();
        for (int i = 0; i < 7; i++) { rows[i] = i == 5 ? Math.Max(0, 720 - fixedSum) : s.Rows[i]; }
        double[] top = new double[7];
        for (int i = 0; i < 7; i++) { top[i] = y; y += rows[i]; }

        // Top bar: a profile dot and the clock.
        Add(c, new Ellipse { Width = 46 * K, Height = 46 * K, Fill = circle }, 62 * K, 40 * K);
        Add(c, new Rectangle { Width = 70 * K, Height = 14 * K, Fill = fg, Opacity = 0.8, RadiusX = 3, RadiusY = 3 }, (1280 - 62 - 70) * K, 52 * K);

        // Tiles.
        double scale = s.TileScale, tileBox = 270 * scale, gap = s.TileGap * scale, inner = (s.TileGap + s.RingThickness + 4) * scale;
        double listH = 318 * scale;
        double listTop = s.GamesAlign switch
        {
            VerticalAlignment.Bottom => top[1] + rows[1] - listH,
            VerticalAlignment.Center => top[1] + (rows[1] - listH) / 2,
            _ => top[1]
        };
        double tileTop = listTop + 48 * scale;
        double x = 86 * scale;
        double corner = (s.TileCorner ?? Theme.TileCorner) * scale * K;
        var colors = new[] { "#E86F51", "#3E7CB1", "#7BB661", "#C94BA8", "#F2C14E", "#4BB3C9", "#8E6FD8" };
        for (int i = 0; i < 9 && x * K < W; i++)
        {
            bool selected = i == 0;
            double grow = selected ? Math.Max(1, s.SelectGrow) : 1;
            double size = (tileBox - inner * 2) * grow;
            double tx = x + inner - (size - (tileBox - inner * 2)) / 2, ty = tileTop + inner - (size - (tileBox - inner * 2)) / 2;
            var face = new Rectangle
            {
                Width = size * K, Height = size * K, RadiusX = corner, RadiusY = corner,
                Fill = Brush(colors[i % colors.Length]), // covers show at full strength in every style, glass included
                Stroke = s.Glass ? new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)) : null, StrokeThickness = 0.6
            };
            if (!s.Glass && i % 3 == 2) face.Fill = tileBrush;
            if (!s.Glass && s.Stroke != null) { face.Stroke = s.Stroke == "accent" ? accent : Brush(s.Stroke); face.StrokeThickness = Math.Max(0.6, s.StrokeWidth * 0.5); }
            if (s.Shadow == "hard") face.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 0, ShadowDepth = 2.5, Direction = 315, Opacity = 1, Color = Colors.Black };
            Add(c, face, tx * K, ty * K);
            if (selected)
            {
                double ring = s.RingThickness * scale;
                var outline = new Rectangle
                {
                    Width = (size + inner * 2 - gap * 2) * K, Height = (size + inner * 2 - gap * 2) * K,
                    Stroke = accent, StrokeThickness = Math.Max(1, ring * K * 1.4), RadiusX = corner + 1.5, RadiusY = corner + 1.5
                };
                // A gradient ring (Switch).
                if (s.RingColors is { Length: > 1 } ringColors)
                {
                    var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                    for (int j = 0; j < ringColors.Length; j++)
                        if (Theme.TryParseArgb(ringColors[j], out var rc)) g.GradientStops.Add(new GradientStop(rc, (double)j / (ringColors.Length - 1)));
                    outline.Stroke = g;
                }
                if (s.Glow) outline.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = s.Glass ? Colors.White : accentColor, BlurRadius = 8, ShadowDepth = 0 };
                Add(c, outline, (tx - inner + gap) * K, (ty - inner + gap) * K);
                if (s.Title == "inside")
                    Add(c, new Rectangle { Width = size * K, Height = 9, Fill = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)) }, tx * K, (ty + size) * K - 9);
                if (s.Title == "above")
                    Add(c, new Rectangle { Width = 90 * K * 1.6, Height = 4, Fill = accent, RadiusX = 2, RadiusY = 2 }, (x + 8) * K, (listTop + 18 * scale) * K);
            }
            x += 270 * scale;
        }

        // Big title and Play button (PlayStation), or the name beside the row (Steam).
        if (s.Title == "big")
        {
            Add(c, new Rectangle { Width = 120, Height = 10, Fill = fg, RadiusX = 2, RadiusY = 2 }, 96 * K, (top[2] + rows[2] - 110) * K);
            Add(c, new Rectangle { Width = 46, Height = 13, Fill = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), RadiusX = 6.5, RadiusY = 6.5 }, 96 * K, (top[2] + rows[2] - 60) * K);
        }
        else if (s.Title == "row")
            Add(c, new Rectangle { Width = 50, Height = 5, Fill = fg, RadiusX = 2, RadiusY = 2 }, (86 * scale + 10) * K, (top[2] + 10) * K);
        if (s.RowHeader.Length > 0)
            Add(c, new Rectangle { Width = 34, Height = 4, Fill = fg, Opacity = 0.8 }, (86 * scale + 10) * K, (tileTop - 4) * K - 6);

        // Buttons.
        var (bw, bh, bc) = s.Buttons switch
        {
            "card" => (100.0, 86.0, 8.0), "tab" => (104.0, 46.0, 23.0), "square" => (88.0, 88.0, s.SquareCorner >= 0 ? s.SquareCorner : s.RowCorner + 4),
            "small" => (70.0, 70.0, 35.0), "large" => (116.0, 116.0, 58.0), "soft" => (92.0, 92.0, 32.0),
            "wide" => (134.0, 82.0, Math.Max(8, s.RowCorner + 6)), _ => (96.0, 96.0, 48.0)
        };
        double margin = s.Buttons switch { "tab" => 3, "small" => 8, "wide" => 5, _ => 6 };
        int count = s.Buttons is "wide" or "large" ? 7 : 9;
        double rowWidth = count * (bw + margin * 2);
        double bx = s.ButtonsAlign == HorizontalAlignment.Left ? 88 : (1280 - rowWidth) / 2;
        double by = top[3] + (rows[3] - bh) / 2;
        Brush buttonFill = s.Buttons == "tab" ? new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))
            : s.OutlineButtons ? Brushes.Transparent : s.Glass ? UiStyles.ShelfBrush(s) : circle;
        if (s.ButtonLook == "accent") buttonFill = new SolidColorBrush(Color.FromArgb(0xE6, accentColor.R, accentColor.G, accentColor.B));
        else if (s.ButtonLook == "neon") buttonFill = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0));
        if (s.ButtonDock)
        {
            // The shelf: glass, solid, outlined or glowing.
            var shelf = new Rectangle { Width = (rowWidth + 28) * K, Height = (bh + 16) * K, RadiusX = (bh + 16) * K / 2, RadiusY = (bh + 16) * K / 2,
                                        Fill = UiStyles.ShelfBrush(s), Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), StrokeThickness = 0.6 };
            if (s.DockLook == "solid") { shelf.Fill = circle; shelf.Stroke = null; }
            else if (s.DockLook == "outline") { shelf.Fill = Brushes.Transparent; shelf.Stroke = fg; shelf.StrokeThickness = 1; }
            else if (s.DockLook == "glow")
            {
                shelf.Fill = new SolidColorBrush(Color.FromArgb(0x40, accentColor.R, accentColor.G, accentColor.B));
                shelf.Stroke = accent; shelf.StrokeThickness = 1;
                shelf.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accentColor, BlurRadius = 10, ShadowDepth = 0 };
            }
            Add(c, shelf, (bx - 14) * K, (by - 8) * K);
        }
        for (int i = 0; i < count; i++)
        {
            var b = new Rectangle
            {
                Width = (bw - 18) * K, Height = (bh - 18) * K, RadiusX = Math.Max(0, bc - 9) * K, RadiusY = Math.Max(0, bc - 9) * K, Fill = buttonFill,
                Stroke = s.Stroke == null ? null : s.Stroke == "accent" ? accent : Brush(s.Stroke), StrokeThickness = Math.Max(0.5, s.StrokeWidth * K * 1.5)
            };
            if (s.Glass) b.Stroke = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
            if (s.ButtonLook == "neon")
            {
                b.Stroke = accent;
                b.StrokeThickness = 1.2;
                b.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accentColor, BlurRadius = 6, ShadowDepth = 0 };
            }
            Add(c, b, (bx + margin + 9) * K, (by + 9) * K);
            if (s.Gloss)
            {
                // The shine across the top half.
                var shine = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                shine.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF), 0));
                shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 0.95));
                shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1));
                Add(c, new Rectangle { Width = (bw - 24) * K, Height = (bh - 18) * K / 2, RadiusX = Math.Max(0, bc - 12) * K, RadiusY = Math.Max(0, bc - 12) * K, Fill = shine },
                    (bx + margin + 12) * K, (by + 10) * K);
            }
            bx += bw + margin * 2;
        }

        // Footer.
        if (s.Footer == "bar")
            Add(c, new Rectangle { Width = W, Height = rows[6] * K, Fill = new SolidColorBrush(Color.FromArgb(0xF2, 0x0A, 0x0D, 0x12)) }, 0, H - rows[6] * K);
        else if (s.Footer == "pill")
            Add(c, new Rectangle { Width = W - 80 * K, Height = (rows[6] - 20) * K, RadiusX = 7, RadiusY = 7, Fill = UiStyles.ShelfBrush(s),
                                   Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), StrokeThickness = 0.6 }, 40 * K, H - (rows[6] - 8) * K);
        else if (s.Footer == "line")
            Add(c, new Rectangle { Width = W - 60 * K, Height = 0.6, Fill = fg }, 30 * K, H - rows[6] * K);

        }

        if (UiStyles.OverlayBrush(s) is { } overlay)
        {
            // The real overlay's lines are too fine at this size; draw them bigger.
            var o = new Rectangle { Width = W, Height = H, Fill = overlay, Opacity = 0.9, RenderTransform = new ScaleTransform(s.Overlay == "grid" ? 0.5 : 1, s.Overlay == "grid" ? 0.5 : 1) };
            if (s.Overlay == "grid") { o.Width = W * 2; o.Height = H * 2; }
            Add(c, o, 0, 0);
        }

        var frame = new Border { Width = W, Height = H, CornerRadius = new CornerRadius(12), Child = c, ClipToBounds = true };
        c.Clip = new RectangleGeometry(new Rect(0, 0, W, H), 12, 12);
        return frame;
    }

    /// <summary>A sketch of the hero, toprow, sidebar and stage arrangements (in home-screen pixels, 1280 × 720).</summary>
    private static void DrawLayout(Canvas c, UiStyle s, Brush fg, Brush accent, Brush circle)
    {
        var colors = new[] { "#E86F51", "#3E7CB1", "#7BB661", "#C94BA8", "#F2C14E", "#4BB3C9", "#8E6FD8" };
        double corner = (s.TileCorner ?? 10) * K;
        void Box(double x, double y, double w, double h, Brush fill, double radius = -1, double opacity = 1) =>
            Add(c, new Rectangle { Width = w * K, Height = h * K, Fill = fill, RadiusX = radius < 0 ? corner : radius * K, RadiusY = radius < 0 ? corner : radius * K, Opacity = opacity }, x * K, y * K);
        void Ring(double x, double y, double w, double h) =>
            Add(c, new Rectangle { Width = (w + 12) * K, Height = (h + 12) * K, Stroke = accent, StrokeThickness = 1.6, RadiusX = corner + 1.5, RadiusY = corner + 1.5 }, (x - 6) * K, (y - 6) * K);
        void Tabs(double x, double y)
        {
            for (int i = 0; i < 3; i++)
            {
                if (i == 0 && s.TabPill) Box(x - 14, y - 10, 104, 40, accent, 20);
                Box(x, y, 76, 18, fg, 4, i == 0 ? 1 : 0.5);
                if (i == 0 && !s.TabPill) Box(x, y + 28, 76, 5, accent, 2);
                x += 120;
            }
        }
        void Status() => Box(1280 - 260, 30, 200, 16, fg, 4, 0.7);
        void Play(double x, double y, double w) { Box(x, y, w, 48, accent, 24); Box(x + w + 14, y, 48, 48, circle, 24); }
        void Posters(double x, double y, double w, double h, int n, double gap)
        {
            for (int i = 0; i < n && x < 1280; i++)
            {
                Box(x, y, w, h, Brush(colors[i % colors.Length]));
                if (i == 0) Ring(x, y, w, h);
                x += w + gap;
            }
        }

        switch (s.Layout)
        {
            case "hero":
                if (s.TopBarFill) Box(0, 0, 1280, 70, new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), 0);
                Tabs(150, 26); Status();
                Box(72, 120, 420, 70, fg, 8);
                Box(72, 214, 300, 20, fg, 4, 0.7);
                Play(72, 260, 190);
                Box(72, 400, 220, 22, fg, 4);
                Posters(72, 448, 150, 225, 8, 18);
                break;
            case "toprow":
                Tabs(150, 26); Status();
                Box(72, 80, 200, 200, Brush(colors[0]));
                Ring(72, 80, 200, 200);
                Posters(296, 80, 130, 130, 7, 14);
                Box(72, 470, 500, 46, fg, 8);
                Box(72, 540, 200, 18, fg, 4, 0.7);
                Play(72, 590, 270);
                break;
            case "sidebar":
                Box(0, 0, 232, 720, new SolidColorBrush(Color.FromArgb(0xE8, 0x0A, 0x0C, 0x1E)), 0);
                Box(22, 30, 170, 34, fg, 6, 0.9);
                for (int i = 0; i < 6; i++) Box(16, 110 + i * 54, 200, 40, i == 0 ? accent : fg, 10, i == 0 ? 0.6 : 0.25);
                Box(272, 110, 470, 54, fg, 8);
                Box(272, 190, 380, 16, fg, 4, 0.6);
                Play(272, 240, 210);
                Box(272, 360, 220, 22, fg, 4);
                Posters(272, 410, 150, 225, 6, 18);
                break;
            case "stage":
                Tabs(90, 26); Status();
                Posters(168, 140, 220, 330, 1, 0);
                Box(492, 100, 270, 410, Brush(colors[1]));
                Ring(492, 100, 270, 410);
                Box(800, 140, 220, 330, Brush(colors[2]));
                Box(1050, 140, 220, 330, Brush(colors[3]));
                Box(452, 534, 376, 34, fg, 6);
                Box(380, 600, 180, 46, accent, 12);
                Box(576, 600, 170, 46, circle, 12);
                Box(762, 600, 120, 46, circle, 12);
                Box(560, 670, 160, 12, fg, 6, 0.5);
                break;
        }
    }

    private static void Add(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }

    private static SolidColorBrush Brush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch (Exception) { return new SolidColorBrush(Colors.Gray); }
    }
}
