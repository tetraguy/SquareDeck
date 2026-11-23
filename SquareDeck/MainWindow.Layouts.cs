using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// Whole-screen arrangements for the UI styles that move things around rather than just restyling them
/// (<see cref="UiStyle.Layout"/>): "hero" (Waves, Xenon, Grids), "toprow" (Sphere), "sidebar" (Desktop) and
/// "stage" (Stage). The same elements are used in every layout; this only moves, restyles and adds to them,
/// and "classic" puts everything back where the window's XAML has it.
/// </summary>
public partial class MainWindow
{
    private Border? _sidebar, _topFill, _infoPill;
    private TextBlock? _rowTitle, _rowCount, _kicker, _subtitle, _infoPillText;
    private StackPanel? _actions, _dots;
    private bool _layoutParts;
    private double _rowSide = RowSideMargin;
    private CancellationTokenSource? _portraitCts;
    private bool _portraitBusy;

    private string LayoutName => UiStyles.Current.Layout;

    /// <summary>The main buttons sit in the top bar as tabs (Up from the games reaches them).</summary>
    private bool TabsOnTop => UiStyles.Current.NavTabs;

    /// <summary>The main buttons are a list in a sidebar (Left from the first game reaches them).</summary>
    private bool SidebarNav => LayoutName == "sidebar";

    private void EnsureLayoutParts()
    {
        if (_layoutParts) return;
        _layoutParts = true;
        int topIndex = RootGrid.Children.IndexOf(TopBar);

        _topFill = new Border { IsHitTestVisible = false, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetRow(_topFill, 0);
        RootGrid.Children.Insert(topIndex, _topFill);

        _sidebar = new Border { Width = 232, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        Grid.SetRow(_sidebar, 0);
        Grid.SetRowSpan(_sidebar, 7);
        RootGrid.Children.Insert(topIndex, _sidebar);

        _rowTitle = MakeText(20, "FgBrush", bold: false);
        _rowCount = MakeText(17, "SubtleBrush", bold: false);
        _rowCount.HorizontalAlignment = HorizontalAlignment.Right;
        _rowCount.VerticalAlignment = VerticalAlignment.Bottom;
        // Below the page layer, so pages opened on top cover them.
        int pageLayer = RootGrid.Children.IndexOf(ScreenLayer);
        foreach (var t in new[] { _rowTitle, _rowCount }) { t.Visibility = Visibility.Collapsed; RootGrid.Children.Insert(pageLayer++, t); }

        // Extra lines inside the title panel.
        _kicker = MakeText(24, "FgBrush", bold: false);
        _kicker.Margin = new Thickness(2, 10, 0, 0);
        _subtitle = MakeText(16, "SubtleBrush", bold: false);
        _subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        _actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        _dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        _infoPillText = MakeText(15, "FgBrush", bold: false);
        _infoPill = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 9, 20, 9), Margin = new Thickness(0, 14, 0, 0),
                                 HorizontalAlignment = HorizontalAlignment.Center, Child = _infoPillText,
                                 Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x14, 0x18, 0x24)) };
        int infoIndex = StylePanel.Children.IndexOf(StyleInfo);
        StylePanel.Children.Insert(infoIndex, _subtitle);
        StylePanel.Children.Insert(infoIndex + 1, _kicker); // name, where it's from, "Continue where you left off", then the details
        StylePanel.Children.Add(_actions);
        StylePanel.Children.Add(_dots);
        StylePanel.Children.Add(_infoPill);
        foreach (UIElement e in new UIElement[] { _kicker, _subtitle, _actions, _dots, _infoPill }) e.Visibility = Visibility.Collapsed;

        // The row's inner panel only exists once the window is shown.
        GameList.Loaded += (_, _) => SetItemsPanelSide(_rowSide);
    }

    private static TextBlock MakeText(double size, string brush, bool bold)
    {
        var t = new TextBlock { FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    private static void Place(UIElement element, int row, int span = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetRowSpan(element, span);
    }

    /// <summary>Called at the end of ApplyUiStyle: puts things where this style's layout wants them.</summary>
    private void ApplyLayout(UiStyle s)
    {
        EnsureLayoutParts();

        // ---- Back to the classic arrangement first.
        Place(GameList, 1); Place(RowHeader, 1); Place(EmptyText, 1); Place(StylePanel, 2); Place(InfoText, 2);
        Place(ButtonRow, 3); Place(ButtonDock, 3); Place(ButtonLabel, 4);
        GameList.Margin = new Thickness(0);
        RowHeader.FontSize = 18;
        RowHeader.FontWeight = FontWeights.SemiBold;
        ButtonRow.Orientation = Orientation.Horizontal;
        ButtonRow.VerticalAlignment = VerticalAlignment.Stretch;
        ModeText.Visibility = Visibility.Visible;
        StylePanel.HorizontalAlignment = HorizontalAlignment.Left;
        StyleTitle.TextAlignment = StyleInfo.TextAlignment = TextAlignment.Left;
        StyleTitle.HorizontalAlignment = StyleInfo.HorizontalAlignment = HorizontalAlignment.Left;
        StyleLogo.HorizontalAlignment = HorizontalAlignment.Left;
        StyleLogo.MaxHeight = 150;
        StyleLogo.MaxWidth = 560;
        StyleLogo.Margin = new Thickness(0, 0, 0, 6);
        StyleTitle.FontWeight = FontWeights.Bold;
        StyleInfo.Visibility = Visibility.Visible;
        _sidebar!.Width = 232;
        _actions!.HorizontalAlignment = HorizontalAlignment.Left;
        _sidebar!.Visibility = _topFill!.Visibility = Visibility.Collapsed;
        _rowTitle!.Visibility = _rowCount!.Visibility = Visibility.Collapsed;
        foreach (UIElement e in new UIElement[] { _kicker!, _subtitle!, _actions, _dots!, _infoPill! }) e.Visibility = Visibility.Collapsed;
        _rowSide = RowSideMargin;

        string kindStyle = TabsOnTop ? "tabs" : SidebarNav ? "side" : "round";
        foreach (var b in ButtonRow.Children.OfType<Button>())
        {
            bool primary = b == HomeBtn || b == FavBtn || b == AppsBtn;
            b.Style = (Style)FindResource(kindStyle switch
            {
                "tabs" => primary ? "TabButton" : "TabIconButton",
                "side" => "SideButton",
                _ => "RoundButton"
            });
        }

        // Tab and sidebar colors.
        var res = Application.Current.Resources;
        Color accent = (res["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.White;
        res["TabActiveFill"] = s.TabPill ? new SolidColorBrush(accent) : Brushes.Transparent;
        res["TabActiveText"] = s.TabPill ? Brushes.White : res["FgBrush"];
        res["TabLineVisibility"] = s.TabPill ? Visibility.Hidden : Visibility.Visible;
        res["SideActiveFill"] = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B));

        // Poster tiles.
        // The picture sits inside the gap, the ring and 4 more on each side (TileInnerMargin); make that area 2:3.
        double inset = 2 * (s.TileGap + s.RingThickness + 4);
        double posterWidth = Math.Round((270 - inset) / 1.5 + inset);
        double wideWidth = Math.Round((270 - inset) * 460 / 215 + inset);
        bool shapeChanged = Game.Portrait != s.Portrait || Game.Wide != s.Wide || (s.Portrait && Math.Abs(Game.PortraitWidth - posterWidth) > 0.5)
                            || (s.Wide && Math.Abs(Game.WideWidth - wideWidth) > 0.5);
        Game.Portrait = s.Portrait;
        Game.PortraitWidth = posterWidth;
        Game.Wide = s.Wide;
        Game.WideWidth = wideWidth;
        if (shapeChanged)
        {
            foreach (var g in _view) g.RefreshShape();
            _scrollTarget = 0;
            StartPortraits();
        }

        double tile = (270 + _rowPad.Bottom) * _tileScale;
        double rowLeft = RowSideMargin * _tileScale + 8 * _tileScale + s.TileGap * _tileScale;

        if (TabsOnTop)
        {
            // The tabs follow the avatar on the left of the top bar; the status icons stay on the right.
            ModeText.Visibility = Visibility.Collapsed;
            Place(ButtonRow, 0);
            ButtonRow.HorizontalAlignment = HorizontalAlignment.Left;
            ButtonRow.VerticalAlignment = VerticalAlignment.Top;
            ButtonRow.Margin = new Thickness(136, 12, 0, 0);
            ButtonDock.Visibility = Visibility.Collapsed;
            ButtonLabel.Visibility = Visibility.Collapsed;
        }

        if (s.TopBarFill)
        {
            _topFill.Visibility = Visibility.Visible;
            _topFill.Height = s.Rows[0];
            var bg = (res["BgBrush"] as SolidColorBrush)?.Color ?? Colors.Black;
            _topFill.Background = new LinearGradientBrush(Color.FromArgb(0xF0, bg.R, bg.G, bg.B), Color.FromArgb(0xB0, bg.R, bg.G, bg.B), 90);
            _topFill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0));
            _topFill.BorderThickness = new Thickness(0, 0, 0, 1);
        }

        switch (s.Layout)
        {
            case "hero":
                // Big logo, details and Play over the artwork; the library row along the bottom.
                Place(StylePanel, 1);
                StylePanel.VerticalAlignment = VerticalAlignment.Bottom;
                StylePanel.Margin = new Thickness(72, 0, 0, 6);
                StyleTitle.FontSize = 50;
                StyleLogo.MaxHeight = 130;
                Place(GameList, 5); Place(RowHeader, 5); Place(EmptyText, 5); Place(_rowCount, 5);
                RowHeader.FontSize = 24;
                RowHeader.FontWeight = FontWeights.Bold;
                RowHeader.Margin = new Thickness(rowLeft, 0, 0, tile + 10);
                _rowCount.Visibility = Visibility.Visible;
                _rowCount.Margin = new Thickness(0, 0, 70, tile + 12);
                _actions.Visibility = Visibility.Visible;
                break;

            case "toprow":
                // Small tiles under the tabs; the game's name and Play large at the bottom left.
                GameList.VerticalAlignment = VerticalAlignment.Top;
                Place(_rowTitle, 2);
                _rowTitle.Visibility = Visibility.Visible;
                _rowTitle.VerticalAlignment = VerticalAlignment.Top;
                _rowTitle.Margin = new Thickness(rowLeft + 4, 14, 0, 0);
                Place(StylePanel, 5);
                StylePanel.VerticalAlignment = VerticalAlignment.Bottom;
                StylePanel.Margin = new Thickness(92, 0, 0, 18);
                StyleTitle.FontSize = 50;
                StyleLogo.MaxHeight = 120;
                _actions.Visibility = Visibility.Visible;
                break;

            case "sidebar":
                // A sidebar with every button; the game and a row of posters to its right.
                ModeText.Visibility = Visibility.Collapsed;
                _sidebar.Visibility = Visibility.Visible;
                var bg = (res["BgBrush"] as SolidColorBrush)?.Color ?? Colors.Black;
                _sidebar.Background = new SolidColorBrush(Color.FromArgb(0xF4, (byte)(bg.R * 0.8 + 4), (byte)(bg.G * 0.8 + 4), (byte)(bg.B * 0.8 + 10)));
                _sidebar.BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
                _sidebar.BorderThickness = new Thickness(0, 0, 1, 0);
                _sidebar.Child = SidebarHeader(s);
                Place(ButtonRow, 0, 7);
                ButtonRow.Orientation = Orientation.Vertical;
                ButtonRow.HorizontalAlignment = HorizontalAlignment.Left;
                ButtonRow.VerticalAlignment = VerticalAlignment.Top;
                ButtonRow.Margin = new Thickness(16, 92, 0, 0);
                ButtonDock.Visibility = Visibility.Collapsed;
                ButtonLabel.Visibility = Visibility.Collapsed;
                TopBar.Margin = new Thickness(260, 10, 40, 0);
                Place(StylePanel, 1);
                StylePanel.VerticalAlignment = VerticalAlignment.Bottom;
                StylePanel.Margin = new Thickness(272, 0, 0, 4);
                StyleTitle.FontSize = 46;
                StyleLogo.MaxHeight = 110;
                Place(GameList, 5); Place(RowHeader, 5); Place(EmptyText, 5);
                GameList.Margin = new Thickness(232, 0, 0, 0);
                RowHeader.FontSize = 22;
                RowHeader.FontWeight = FontWeights.SemiBold;
                RowHeader.Margin = new Thickness(232 + rowLeft, 0, 0, tile + 10);
                _actions.Visibility = Visibility.Visible;
                break;

            case "stage":
                // The selected game in the middle; its name, buttons and details centered under it.
                _rowSide = Math.Max(RowSideMargin, RootGrid.Width / _tileScale / 2 - 90 - s.TileGap);
                Place(StylePanel, 2, 4); // above the footer, not over its button hints
                StylePanel.VerticalAlignment = VerticalAlignment.Top;
                StylePanel.HorizontalAlignment = HorizontalAlignment.Center;
                StylePanel.Margin = new Thickness(0, 2, 0, 0);
                StyleTitle.FontSize = 32;
                StyleTitle.HorizontalAlignment = HorizontalAlignment.Center;
                StyleLogo.HorizontalAlignment = HorizontalAlignment.Center;
                StyleLogo.MaxHeight = 90; // above the centered buttons and details
                StyleInfo.Visibility = Visibility.Collapsed;
                _subtitle!.Visibility = Visibility.Visible;
                _actions.Visibility = Visibility.Visible;
                _actions.HorizontalAlignment = HorizontalAlignment.Center;
                _dots!.Visibility = Visibility.Visible;
                _infoPill!.Visibility = Visibility.Visible;
                break;
        }

        SetItemsPanelSide(_rowSide);
        FitButtonRow();
        UpdateLayoutDetails();
        QueueFitStylePanel();
        if (s.Portrait) StartPortraits();
    }

    private bool _stylePanelHooked, _stylePanelQueued;

    /// <summary>
    /// Stage layout: the logo, buttons, page dots and details under the game are scaled down when together they're
    /// taller than the space above the footer (Pixel Dawn's logo, square buttons and wide type ran into the hints).
    /// Run again whenever the panel's content changes, for example when a game's logo arrives.
    /// </summary>
    private void FitStylePanel()
    {
        if (!_stylePanelHooked)
        {
            _stylePanelHooked = true;
            StylePanel.SizeChanged += (_, _) => QueueFitStylePanel();
        }
        if (UiStyles.Current.Layout != "stage" || StylePanel.Visibility != Visibility.Visible)
        {
            if (StylePanel.LayoutTransform != Transform.Identity) StylePanel.LayoutTransform = Transform.Identity;
            return;
        }
        int first = Grid.GetRow(StylePanel), span = Grid.GetRowSpan(StylePanel);
        double room = 0;
        for (int i = first; i < first + span && i < RootGrid.RowDefinitions.Count; i++)
            room += RootGrid.RowDefinitions[i].ActualHeight;
        room -= StylePanel.Margin.Top + StylePanel.Margin.Bottom + 6;
        if (room <= 0) return;

        // Its natural height, at full size.
        double needed = 0;
        foreach (UIElement child in StylePanel.Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(RootGrid.Width, double.PositiveInfinity));
            needed += child.DesiredSize.Height;
        }
        double k = needed > room ? Math.Max(0.6, room / needed) : 1;
        double now = StylePanel.LayoutTransform is ScaleTransform t ? t.ScaleX : 1;
        if (Math.Abs(now - k) < 0.01) return;
        StylePanel.LayoutTransform = k >= 1 ? Transform.Identity : new ScaleTransform(k, k);
    }

    private void QueueFitStylePanel()
    {
        if (_stylePanelQueued) return;
        _stylePanelQueued = true;
        Dispatcher.BeginInvoke(new Action(() => { _stylePanelQueued = false; FitStylePanel(); }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Shrinks the row of round buttons to fit the screen when there are more than fit (they used to run off both
    /// edges, with the frosted shelf behind them). Tabs and the sidebar arrange their own.
    /// </summary>
    public void FitButtonRow()
    {
        if (TabsOnTop || SidebarNav)
        {
            ButtonRow.LayoutTransform = Transform.Identity;
            return;
        }
        double width = 0, tallest = 0;
        foreach (UIElement child in ButtonRow.Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width += child.DesiredSize.Width;
            tallest = Math.Max(tallest, child.DesiredSize.Height);
        }
        double room = RootGrid.Width - 2 * Math.Max(48, ButtonRow.Margin.Left) - (UiStyles.Current.ButtonDock ? 40 : 0);
        double k = width > room && width > 0 ? room / width : 1;
        // And to the height of its row: buttons taller than the row were cut off at the bottom (square buttons in a
        // short row, as in Pixel City).
        int row = Grid.GetRow(ButtonRow);
        if (Grid.GetRowSpan(ButtonRow) == 1 && row < RootGrid.RowDefinitions.Count && RootGrid.RowDefinitions[row].Height is { IsAbsolute: true } h
            && h.Value > 0 && tallest > 0)
            k = Math.Min(k, (h.Value - ButtonRow.Margin.Top - ButtonRow.Margin.Bottom - 4) / tallest);
        ButtonRow.LayoutTransform = k < 0.999 ? new ScaleTransform(k, k) : Transform.Identity;
        SizeDockToButtons(null, null);
    }

    /// <summary>The sidebar's heading: a round badge and the name, spaced out.</summary>
    private static UIElement SidebarHeader(UiStyle s)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 24, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(2.5) };
        badge.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        var letter = new TextBlock { Text = s.Brand.Length > 0 ? s.Brand[..1] : "S", FontSize = 20, FontWeight = FontWeights.Bold,
                                     HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        letter.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        badge.Child = letter;
        row.Children.Add(badge);
        var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = string.Join(" ", s.Brand.ToCharArray()), FontSize = 15, FontWeight = FontWeights.SemiBold };
        name.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        var sub = new TextBlock { Text = "L A U N C H E R", FontSize = 9, Margin = new Thickness(0, 2, 0, 0) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
        words.Children.Add(name);
        words.Children.Add(sub);
        row.Children.Add(words);
        return row;
    }

    /// <summary>Side space inside the games row (Stage keeps a lot so the first and last games can sit in the middle).</summary>
    private void SetItemsPanelSide(double side)
    {
        if (FindItemsPanel(GameList) is StackPanel panel) panel.Margin = new Thickness(side, _rowPad.Top, side, _rowPad.Bottom);
    }

    private static StackPanel? FindItemsPanel(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is StackPanel { IsItemsHost: true } host) return host;
            if (FindItemsPanel(child) is { } found) return found;
        }
        return null;
    }

    // ================================================================ details that follow the selection

    /// <summary>The selection changed (or the style did): Play / favorite buttons, row title, dots and so on.</summary>
    private void UpdateLayoutDetails()
    {
        if (!_layoutParts) return;
        var s = UiStyles.Current;
        var item = GameList.SelectedItem as Game;
        var game = item?.Actual;

        if (_rowTitle!.Visibility == Visibility.Visible) _rowTitle.Text = game?.Name ?? "";

        if (_rowCount!.Visibility == Visibility.Visible)
        {
            var games = AllGames.Where(g => g.IsGame && !g.OnWishlist).ToList();
            _rowCount.Text = $"{games.Count} games · {games.Count(g => g.IsInstalled)} installed";
        }

        // "Continue where you left off" over the most recently played game.
        if (s.Layout is "hero" && game is { IsGame: true })
        {
            var latest = AllGames.Where(g => g.IsGame).OrderByDescending(g => g.RecentAt).FirstOrDefault();
            bool continuing = latest != null && latest.Key == game.Key;
            _kicker!.Text = "Continue where you left off";
            _kicker.Visibility = continuing ? Visibility.Visible : Visibility.Collapsed;
        }
        else _kicker!.Visibility = Visibility.Collapsed;

        if (_subtitle!.Visibility == Visibility.Visible || s.Layout == "stage")
        {
            _subtitle.Text = game == null ? "" : game.IsSteam ? "Steam" : game.Source.Length > 0 ? game.Source
                : game.Kind == ItemKind.App ? "App" : game.IsRecomp ? "Recomp Zone" : game.IsGame ? "Added by you" : "";
            _subtitle.Visibility = s.Layout == "stage" && _subtitle.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (s.Layout == "stage")
        {
            _infoPillText!.Text = InfoText.Text;
            _infoPill!.Visibility = InfoText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildDots();
        }

        BuildActions(s, item);
    }

    private void BuildDots()
    {
        _dots!.Children.Clear();
        int total = _view.Count;
        if (total < 2) return;
        int count = Math.Min(9, total);
        int active = (int)Math.Round(Math.Max(0, GameList.SelectedIndex) * (count - 1.0) / (total - 1));
        for (int i = 0; i < count; i++)
        {
            var dot = new Border { Height = 8, Width = i == active ? 28 : 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(4, 0, 4, 0) };
            dot.SetResourceReference(Border.BackgroundProperty, i == active ? "AccentBrush" : "SubtleBrush");
            if (i != active) dot.Opacity = 0.55;
            _dots.Children.Add(dot);
        }
    }

    // ================================================================ Play, More, favorite

    private void BuildActions(UiStyle s, Game? item)
    {
        _actions!.Children.Clear();
        if (s.Actions.Length == 0 || item == null || item.IsActionTile)
        {
            if (s.Actions.Length > 0) _actions.Visibility = Visibility.Collapsed;
            if (_actionIndex >= 0) { _actionIndex = -1; UpdateHomeRing(); } // nothing left to highlight: back to the games
            return;
        }
        _actions.Visibility = Visibility.Visible;
        var game = item.Actual;
        string playText = game.IsRunning ? "Resume" : game.IsGame ? "Play" : "Open";

        // Play: the style's own colors, or the highlight color.
        Brush fill;
        Color first;
        var res = Application.Current.Resources;
        if (s.PlayColors is { Length: > 0 } colors && Theme.TryParseArgb(colors[0], out first))
        {
            fill = colors.Length > 1 && Theme.TryParseArgb(colors[1], out var second)
                ? new LinearGradientBrush(first, second, 0) : new SolidColorBrush(first);
        }
        else
        {
            first = (res["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            fill = new SolidColorBrush(first);
        }
        bool bright = (0.299 * first.R + 0.587 * first.G + 0.114 * first.B) / 255 > 0.62 && first.A > 0xC0;
        Brush ink = bright ? new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x18)) : Brushes.White;

        var playRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (s.KeyHint && Hints.GlyphFile("Select") is { } glyphFile && HintBar.Glyph(glyphFile, 26) is { } glyph)
        {
            glyph.Margin = new Thickness(0, 0, 12, 0);
            playRow.Children.Add(glyph);
        }
        else if (s.KeyHint)
        {
            var key = new Border { BorderBrush = ink, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(6),
                                   Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
                                   Child = new TextBlock { Text = Hints.KeyLabel("Select"), FontSize = 13, Foreground = ink } };
            playRow.Children.Add(key);
        }
        else if (s.Actions == "icons")
        {
            playRow.Children.Add(new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 18, Foreground = ink,
                                                 VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        }
        playRow.Children.Add(new TextBlock { Text = playText, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = ink, VerticalAlignment = VerticalAlignment.Center });

        bool words = s.Actions == "words";
        double height = words ? 48 : 54;
        var play = ActionButton(playRow, fill, height, words ? 190 : s.Layout == "toprow" ? 270 : 210, height / 2, () => _ = ActivateAsync(item));
        if (s.Glow && !Perf.Active)
            play.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = first, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.55 };
        _actions.Children.Add(play);

        var dark = new SolidColorBrush(Color.FromArgb(0xC8, 0x1C, 0x20, 0x2C));
        if (words)
        {
            if (game.IsGame)
                _actions.Children.Add(ActionButton(Word(game.IsFavorite ? "Remove from favorites" : "Add to favorites"), dark, height, double.NaN, 10,
                    () => { ToggleFavorite(game); UpdateLayoutDetails(); }));
            _actions.Children.Add(ActionButton(Word("More"), dark, height, double.NaN, 10, () => _ = OpenOptionsAsync(item)));
        }
        else
        {
            _actions.Children.Add(ActionButton(Glyph(""), dark, height, height, height / 2, () => _ = OpenOptionsAsync(item)));
            if (game.IsGame && s.Layout != "toprow")
                _actions.Children.Add(ActionButton(Glyph(game.IsFavorite ? "" : ""), dark, height, height, height / 2,
                    () => { ToggleFavorite(game); UpdateLayoutDetails(); }));
        }

        // The controller was on these buttons: keep the highlight (they were just rebuilt for the new game).
        if (_actionIndex >= 0) Dispatcher.BeginInvoke(new Action(() => { if (_actionIndex >= 0) SetActionIndex(_actionIndex); }), System.Windows.Threading.DispatcherPriority.Loaded);

        static TextBlock Word(string text) => new() { Text = text, FontSize = 18, Foreground = Brushes.White, Margin = new Thickness(26, 0, 26, 0),
                                                      VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        static TextBlock Glyph(string glyph) => new() { Text = glyph, FontFamily = Ui.IconFont, FontSize = 18, Foreground = Brushes.White,
                                                        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    }

    // ================================================================ the action buttons with a controller

    private int _actionIndex = -1; // -1: not in the Play / favorite / More buttons

    /// <summary>The Play / favorite / More buttons under the game, when this style shows them.</summary>
    private List<Border> ActionButtons() =>
        _actions is { Visibility: Visibility.Visible } a && a.IsVisible ? a.Children.OfType<Border>().Where(b => b.Tag is Action).ToList() : new();

    private bool ActionsReachable => ActionButtons().Count > 0;

    /// <summary>Highlights one of the action buttons (or none): a bright ring, and it grows a little.</summary>
    private void SetActionIndex(int index)
    {
        var buttons = ActionButtons();
        if (index >= buttons.Count) index = buttons.Count - 1;
        if (index >= 0 && index != _actionIndex) Rumble();
        _actionIndex = index;
        for (int i = 0; i < buttons.Count; i++)
        {
            bool on = i == index;
            buttons[i].BorderThickness = new Thickness(on ? 3 : 1);
            buttons[i].BorderBrush = on ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            Motion.Highlight(buttons[i], on, 1.06);
        }
        UpdateHomeRing();
        ButtonLabel.Text = "";
    }

    /// <summary>
    /// The controller in the action buttons: Left / Right between them, A presses, Up or B back to the games,
    /// Down on to the buttons or footer below.
    /// </summary>
    private void HandleActionRow(NavAction action, Action goDown)
    {
        var buttons = ActionButtons();
        if (buttons.Count == 0) { SetActionIndex(-1); return; }
        int index = Math.Clamp(_actionIndex, 0, buttons.Count - 1);
        switch (action)
        {
            case NavAction.Left: SetActionIndex(Math.Max(0, index - 1)); break;
            case NavAction.Right: SetActionIndex(Math.Min(buttons.Count - 1, index + 1)); break;
            case NavAction.Up:
            case NavAction.Back:
                SetActionIndex(-1);
                break;
            case NavAction.Down:
                SetActionIndex(-1);
                goDown();
                break;
            case NavAction.Accept:
                var target = buttons[index];
                UiSound.Select();
                Motion.PressThen(target, () =>
                {
                    (target.Tag as Action)?.Invoke();
                    // Favorite rebuilds the buttons: keep the highlight on the same one.
                    Dispatcher.BeginInvoke(new Action(() => { if (_actionIndex >= 0) SetActionIndex(index); }), System.Windows.Threading.DispatcherPriority.Loaded);
                });
                break;
            case NavAction.Y:
                SetActionIndex(-1);
                if (GameList.SelectedItem is Game g) _ = OpenOptionsAsync(g);
                break;
        }
    }

    /// <summary>A clickable shape (mouse, touch, and the controller through Down from the games).</summary>
    private static Border ActionButton(UIElement content, Brush fill, double height, double width, double corner, Action click)
    {
        var b = new Border
        {
            Height = height, Width = width, CornerRadius = new CornerRadius(corner), Background = fill, Child = content,
            Margin = new Thickness(0, 0, 12, 0), Cursor = Cursors.Hand,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1)
        };
        b.MouseEnter += (_, _) => b.BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));
        b.MouseLeave += (_, _) => b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        b.Tag = click; // the controller presses it through this (see HandleActionRow)
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; UiSound.Select(); click(); };
        b.TouchUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    // ================================================================ poster art

    /// <summary>Poster styles: fetches tall covers for the games around the selection, a few at a time.</summary>
    private void StartPortraits()
    {
        if (!(Game.Portrait || Game.Wide) || _portraitBusy) return;
        _portraitCts?.Cancel();
        _portraitCts = new CancellationTokenSource();
        _ = LoadPortraitsAsync(_portraitCts.Token);
    }

    private async Task LoadPortraitsAsync(CancellationToken ct)
    {
        _portraitBusy = true;
        try
        {
            using var client = new SteamGridDbClient(Settings.SteamGridDbApiKey);
            while (!ct.IsCancellationRequested && (Game.Portrait || Game.Wide))
            {
                // Tall poster art, or wide header capsules, nearest to the selection first.
                bool wide = Game.Wide;
                int at = Math.Max(0, GameList.SelectedIndex);
                var next = _view.Select((g, i) => (g, i))
                    .Where(x => !(wide ? x.g.HeaderTried : x.g.PortraitTried) && x.g.Actual.IsGame && Math.Abs(x.i - at) <= 14)
                    .OrderBy(x => Math.Abs(x.i - at)).Select(x => x.g).FirstOrDefault();
                if (next == null) break;
                if (wide) next.HeaderTried = true; else next.PortraitTried = true;
                var actual = next.Actual;
                ImageSource? picture = wide ? actual.HeaderCover : actual.PortraitCover;
                if (picture == null)
                {
                    try
                    {
                        picture = await Task.Run(async () =>
                        {
                            var path = wide ? await client.GetHeaderAsync(actual, ct) : await client.GetPortraitAsync(actual, ct);
                            return path == null ? null : (ImageSource?)ImageLoader.FromFile(path, wide ? 520 : 360);
                        }, ct);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception) { picture = null; }
                    if (wide) { actual.HeaderTried = true; if (picture != null) actual.HeaderCover = picture; }
                    else { actual.PortraitTried = true; if (picture != null) actual.PortraitCover = picture; }
                }
                if (picture != null && !ReferenceEquals(next, actual))
                {
                    if (wide) next.HeaderCover = picture; else next.PortraitCover = picture;
                }
            }
        }
        catch (Exception) { }
        finally { _portraitBusy = false; }
    }
}
