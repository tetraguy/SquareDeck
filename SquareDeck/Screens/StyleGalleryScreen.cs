using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Experimental "Style gallery" (8.5.92): the UI styles in a row along the bottom of the home screen. Move along them,
/// press A to preview one (the whole launcher switches to it, for real), A again on it to keep it, B to leave with
/// your own style back. LB / RB change group, X hides the row to see the whole home screen, Y previews a surprise.
/// Previews keep your music; everything else is put back exactly as it was if you leave without choosing.
/// </summary>
public sealed class StyleGalleryScreen : Screen
{
    public const string Lab = "stylegallery";

    private readonly bool _backToSettings;
    private readonly List<(string Name, List<UiStyle> Styles)> _groups = new();
    private int _group, _index;
    private string? _snapshot;          // the settings as they were before the first preview
    private string _startStyle = "";
    private string? _previewing;
    private bool _busy, _closing;

    private readonly Border _strip = new();
    private System.Windows.Shapes.Rectangle? _scrim;
    // Room around the row so the highlighted card's 1.08 grow (from its bottom edge) isn't clipped by the scroller (8.5.96).
    private readonly StackPanel _cards = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 22, 12, 4) };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _groupText = new() { FontSize = 15 };
    private readonly TextBlock _name = new() { FontSize = 28, FontWeight = FontWeights.Bold };
    private readonly TextBlock _about = new() { FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _tier = new() { FontSize = 13, FontWeight = FontWeights.SemiBold };
    private readonly HintBar _hints = new("");
    private readonly List<Border> _cardViews = new();
    private readonly HashSet<int> _drawn = new();
    private readonly List<Border> _rings = new(); // the highlight, drawn around the outside of each card's frame (8.5.98)
    // Motion (8.5.97): the row glides to the highlight and the highlighted card grows with a short ease, instead of jumping.
    private int _lit = -1;
    private double _scrollPos, _scrollTarget;
    private TimeSpan _lastFrame;
    private bool _gliding, _drawQueued;
    private static readonly BitmapCache CardCache = MakeCache();
    private static BitmapCache MakeCache() { var c = new BitmapCache(1) { SnapsToDevicePixels = true }; c.Freeze(); return c; }

    private const double CardW = 208, CardH = 117;

    public override bool IsDialog => true;

    public static bool On => Labs.On(Lab);

    /// <summary>Opens the gallery over the home screen (closing the pages on top), on a group ("mythical", "legendary", "mine"…).</summary>
    public static void Open(MainWindow host, string group = "all", bool backToSettings = true)
    {
        if (host.RootScreen is { } root) host.Pop(root);
        host.Push(new StyleGalleryScreen(group, backToSettings));
    }

    private StyleGalleryScreen(string group, bool backToSettings)
    {
        _backToSettings = backToSettings;
        _scroll = new ScrollViewer
        {
            Content = _cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, PanningMode = PanningMode.HorizontalOnly, Margin = new Thickness(-12, -6, -12, 0),
        };
        var info = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        info.ColumnDefinitions.Add(new ColumnDefinition());
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel();
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        nameLine.Children.Add(_name);
        _tier.Margin = new Thickness(14, 0, 0, 4);
        _tier.VerticalAlignment = VerticalAlignment.Bottom;
        nameLine.Children.Add(_tier);
        words.Children.Add(nameLine);
        _about.Margin = new Thickness(0, 4, 0, 0);
        words.Children.Add(_about);
        info.Children.Add(words);
        _groupText.VerticalAlignment = VerticalAlignment.Top;
        _groupText.Margin = new Thickness(20, 6, 0, 0);
        Grid.SetColumn(_groupText, 1);
        info.Children.Add(_groupText);
        // Fixed light text on the gallery's own dark panel, so it reads the same whatever style is previewed behind it.
        foreach (var t in new[] { _name, _groupText }) t.Foreground = Brushes.White;
        _about.Foreground = new SolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF));
        _tier.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

        var panel = new StackPanel { Margin = new Thickness(56, 22, 56, 18) };
        panel.Children.Add(info);
        panel.Children.Add(_scroll);
        _hints.Margin = new Thickness(0, 12, 0, 0);
        _hints.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Children.Add(_hints);
        _strip.Child = panel;
        _strip.VerticalAlignment = VerticalAlignment.Bottom;
        // 8.5.93: a panel of its own between the home screen and the previews (it was only a fade, so the home screen's
        // buttons and text showed through and the cards blended into it): a dark, nearly solid card with a hairline
        // edge and a shadow, over a scrim that dims the bottom of the home screen.
        _strip.Margin = new Thickness(28, 0, 28, 18);
        _strip.CornerRadius = new CornerRadius(26);
        // 8.5.95: fully solid (nothing of the home screen shows through) with a brighter edge.
        _strip.Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0C, 0x10));
        _strip.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        _strip.BorderThickness = new Thickness(1.5);
        // The hints and anything else inside take light text on the dark panel too, whatever the previewed style uses.
        _strip.Resources["FgBrush"] = Brushes.White;
        _strip.Resources["SubtleBrush"] = new SolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF));
        // The button badges are white pills with the letter in the page's background color: dark here, so a light
        // style (Sakura, Frosted Light…) can't turn "A", "X" and "Esc" white on white (8.5.96).
        _strip.Resources["BgBrush"] = new SolidColorBrush(Color.FromRgb(0x0B, 0x0C, 0x10));
        if (!Perf.Active) _strip.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 6, Opacity = 0.6 };
        var scrim = new System.Windows.Shapes.Rectangle { IsHitTestVisible = false };
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.2));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xD0, 0, 0, 0), 0.6));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xF2, 0, 0, 0), 1));
        scrim.Fill = fade;
        _scrim = scrim;
        // 8.5.94: the whole screen takes the clicks and taps, so the home screen underneath can't be used while
        // styles are previewed (with the row hidden, its buttons used to open pages and leave the preview half done).
        // A click or tap there just brings the row back.
        var root = new Grid { Background = Brushes.Transparent };
        bool OnRow(Point p) => _strip.Opacity >= 0.5 && p.X >= 0 && p.Y >= 0 && p.X <= _strip.ActualWidth && p.Y <= _strip.ActualHeight;
        root.PreviewMouseDown += (_, e) =>
        {
            if (OnRow(e.GetPosition(_strip))) return; // the row's own cards
            e.Handled = true;
            ShowRow();
        };
        root.PreviewTouchDown += (_, e) =>
        {
            if (OnRow(e.GetTouchPoint(_strip).Position)) return;
            e.Handled = true;
            ShowRow();
        };
        root.PreviewMouseWheel += (_, e) => e.Handled = true;
        root.Children.Add(scrim);
        root.Children.Add(_strip);
        Content = root;

        BuildGroups();
        _group = Math.Max(0, _groups.FindIndex(g => g.Name.Equals(group, StringComparison.OrdinalIgnoreCase) || Key(g.Name) == group));
    }

    private static string Key(string name) => name switch { "Mythical" => "mythical", "Legendary" => "legendary", "Yours" => "mine", "Unlocked" => "unlockable", _ => "all" };

    private void BuildGroups()
    {
        var legendary = UiStyles.LegendaryBasics.Concat(UiStyles.Advanced.Where(x => !x.Mythical && x.Unlock == null)).ToList();
        var mythical = UiStyles.MythicalInOrder();
        var unlocked = UiStyles.Advanced.Where(x => x.Unlock != null && !StyleUnlocks.IsLocked(x)).ToList();
        var mine = UiStyles.Custom.ToList();
        var all = legendary.Concat(mythical).Concat(unlocked).Concat(mine).GroupBy(x => x.Id).Select(g => g.First()).ToList();
        _groups.Add(("All", all));
        _groups.Add(("Mythical", mythical));
        _groups.Add(("Legendary", legendary));
        if (unlocked.Count > 0) _groups.Add(("Unlocked", unlocked));
        if (mine.Count > 0) _groups.Add(("Yours", mine));
    }

    private List<UiStyle> Styles => _groups[_group].Styles;

    public override void OnShown()
    {
        _startStyle = Host.Settings.UiStyle;
        ShowGroup(Math.Max(0, Styles.FindIndex(x => x.Id == _startStyle)));
        UiSound.Select();
    }

    public override void FocusDefault() => Focus();

    // ---------------------------------------------------------------- drawing

    private void ShowGroup(int index)
    {
        _cards.Children.Clear();
        _cardViews.Clear();
        _rings.Clear();
        _drawn.Clear();
        _lit = -1;
        foreach (var style in Styles)
        {
            var st = style;
            var card = new StackPanel { Margin = new Thickness(0, 0, 16, 0), Width = CardW, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = new ScaleTransform(1, 1) };
            // The picture is clipped to the frame's rounded corners, so it never spills over them (8.5.98).
            var frame = new Border { Width = CardW, Height = CardH, CornerRadius = new CornerRadius(12),
                                     Clip = new RectangleGeometry(new Rect(0, 0, CardW, CardH), 12, 12) };
            frame.Background = ColorOf(st);
            // Each picture on a dark plate with a shadow, so its edges show against the panel.
            var plate = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(2), Child = frame,
                                     Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1C, 0x1D, 0x23)),
                                     BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
                                     Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.55 } };
            // The highlight is a ring outside the frame, with a small gap, instead of a border painted over the picture.
            var ring = new Border { CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(3), Margin = new Thickness(-6),
                                    IsHitTestVisible = false, Opacity = 0, BorderBrush = Brushes.Transparent };
            var holder = new Grid();
            holder.Children.Add(plate);
            holder.Children.Add(ring);
            card.Children.Add(holder);
            _rings.Add(ring);
            var label = new TextBlock { Text = (st.Id == _startStyle ? "★ " : "") + st.Name, FontSize = 14, Margin = new Thickness(4, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                                        Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)) };
            card.Children.Add(label);
            card.MouseLeftButtonUp += (_, _) => { int i = Styles.IndexOf(st); if (i == _index) _ = PressAsync(); else Move(i - _index); };
            _cards.Children.Add(card);
            _cardViews.Add(frame);
        }
        _index = Math.Clamp(index, 0, Math.Max(0, Styles.Count - 1));
        Highlight(scroll: false);
        Dispatcher.BeginInvoke(new Action(() => Highlight(scroll: true, instant: true)), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>A flat stand-in in the style's highlight color until its picture is drawn.</summary>
    private static Brush ColorOf(UiStyle s)
    {
        var p = UiStyles.PaletteFor(s, true) ?? UiStyles.PaletteFor(s, false);
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(p != null && p[0].Length > 0 ? p[0] : "#202024")); }
        catch (FormatException) { return Brushes.DimGray; }
    }

    /// <summary>The little home-screen pictures, drawn only for the cards near the highlight (there are a lot of styles).
    /// One per idle moment, nearest first, so building them never holds up a button press or the glide (8.5.97).</summary>
    private void DrawNear()
    {
        if (_drawQueued || _closing) return;
        _drawQueued = true;
        Dispatcher.BeginInvoke(new Action(DrawOne), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void DrawOne()
    {
        _drawQueued = false;
        if (_closing || _cardViews.Count == 0) return;
        int lo = Math.Max(0, _index - 5), hi = Math.Min(_cardViews.Count - 1, _index + 8), pick = -1;
        for (int r = 0; r <= 8 && pick < 0; r++)
            foreach (int i in new[] { _index + r, _index - r })
                if (i >= lo && i <= hi && !_drawn.Contains(i)) { pick = i; break; }
        if (pick < 0) return;
        _drawn.Add(pick);
        try { _cardViews[pick].Child = new Viewbox { Stretch = Stretch.UniformToFill, Child = UiStyleScreen.Preview(Styles[pick]) }; }
        catch (Exception) { }
        DrawNear();
    }

    /// <summary>Grows (or settles) one card with a short ease-out.</summary>
    private void Grow(int i, bool on)
    {
        if (i < 0 || i >= _cards.Children.Count) return;
        if (((FrameworkElement)_cards.Children[i]).RenderTransform is not ScaleTransform sc) return;
        var a = new DoubleAnimation(on ? 1.08 : 1, TimeSpan.FromMilliseconds(on ? 140 : 110)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Glides the row toward an offset; pressing again mid-glide just moves the target, so holding a direction flows.</summary>
    private void ScrollTo(double target, bool instant)
    {
        _scrollTarget = Math.Clamp(target, 0, Math.Max(0, _scroll.ScrollableWidth));
        if (instant || _scroll.ScrollableWidth <= 0)
        {
            StopGlide();
            _scrollPos = _scrollTarget;
            _scroll.ScrollToHorizontalOffset(_scrollTarget);
            return;
        }
        if (_gliding) return;
        _gliding = true;
        _scrollPos = _scroll.HorizontalOffset;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
        if (_lastFrame == TimeSpan.Zero) { _lastFrame = now; return; }
        double dt = (now - _lastFrame).TotalSeconds;
        if (dt <= 0) return;
        _lastFrame = now;
        dt = Math.Min(dt, 0.05);
        double d = _scrollTarget - _scrollPos;
        if (_closing || Math.Abs(d) < 0.6)
        {
            _scrollPos = _scrollTarget;
            _scroll.ScrollToHorizontalOffset(_scrollPos);
            StopGlide();
            return;
        }
        _scrollPos += d * (1 - Math.Exp(-dt * 16)); // critically damped feel, about 0.2 s to settle
        _scroll.ScrollToHorizontalOffset(_scrollPos);
    }

    private void StopGlide()
    {
        if (!_gliding) return;
        _gliding = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void Highlight(bool scroll, bool instant = false)
    {
        if (Styles.Count == 0) { _name.Text = "No styles here"; return; }
        DrawNear();
        for (int i = 0; i < _cardViews.Count; i++)
        {
            bool on = i == _index;
            var ring = _rings[i];
            bool previewing = Styles[i].Id == _previewing;
            if (on) ring.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            else if (previewing) ring.BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
            double want = on || previewing ? 1 : 0;
            if (instant) { ring.BeginAnimation(OpacityProperty, null); ring.Opacity = want; ring.Tag = want; }
            else if (!(ring.Tag is double had && had == want))
            {
                ring.Tag = want;
                ring.BeginAnimation(OpacityProperty, new DoubleAnimation(want, TimeSpan.FromMilliseconds(want > 0 ? 120 : 90)));
            }
            // Cards near the highlight are kept as pictures, so the glide only moves bitmaps (their shadows and previews
            // aren't redrawn every frame); the highlighted one stays live so it's sharp when grown.
            var card = (FrameworkElement)_cards.Children[i];
            var cache = !on && i >= _index - 7 && i <= _index + 10 ? CardCache : null;
            if (!ReferenceEquals(card.CacheMode, cache)) card.CacheMode = cache;
        }
        if (_lit != _index)
        {
            if (instant) foreach (var c in _cards.Children.OfType<FrameworkElement>()) if (c.RenderTransform is ScaleTransform s0) { s0.BeginAnimation(ScaleTransform.ScaleXProperty, null); s0.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
            Grow(_lit, false);
            Grow(_index, true);
            _lit = _index;
        }
        var st = Styles[_index];
        _name.Text = st.Name;
        string family = st.Font.Contains('#') ? st.Font[(st.Font.IndexOf('#') + 1)..] : st.Font.StartsWith("file:") ? "" : st.Font;
        _tier.Text = string.Join("  ·  ", new[]
        {
            MyStyles.IsCustom(st.Id) ? "Yours" : st.Mythical ? "Mythical" : st.Unlock != null ? "Unlocked" : "Legendary",
            family, st.Id == _startStyle ? "★ Yours now" : "", st.Id == _previewing ? "Previewing" : "",
        }.Where(x => x.Length > 0));
        _about.Text = st.Description;
        _groupText.Text = $"◀ LB   {_groups[_group].Name}  ·  {_index + 1} of {Styles.Count}   RB ▶";
        _hints.Template = (st.Id == _previewing ? "{Select}  Use this style" : "{Select}  Preview") +
                          "      {Back}  " + (_previewing != null ? "Leave (put mine back)" : "Leave") + "      {Details}  Hide the row      {Options}  Surprise me";
        if (scroll && _index < _cards.Children.Count)
        {
            var card = (FrameworkElement)_cards.Children[_index];
            double x = VisualTreeHelper.GetOffset(card).X; // layout spot, unaffected by the grow
            ScrollTo(x - (_scroll.ViewportWidth - CardW) / 2, instant);
        }
    }

    private void Move(int by)
    {
        if (Styles.Count == 0) return;
        int next = Math.Clamp(_index + by, 0, Styles.Count - 1);
        if (next == _index) return;
        _index = next;
        UiSound.Move();
        Host.Rumble();
        Highlight(scroll: true);
    }

    // ---------------------------------------------------------------- the buttons

    public override bool OnAction(NavAction action)
    {
        if (_closing) return true;
        switch (action)
        {
            case NavAction.Left: Move(-1); break;
            case NavAction.Right: Move(1); break;
            case NavAction.LT: Move(-8); break;
            case NavAction.RT: Move(8); break;
            case NavAction.LB or NavAction.RB:
                int g = (_group + (action == NavAction.RB ? 1 : _groups.Count - 1)) % _groups.Count;
                string keep = Styles.Count > 0 ? Styles[_index].Id : _startStyle;
                _group = g;
                UiSound.Page();
                ShowGroup(Math.Max(0, Styles.FindIndex(x => x.Id == keep)));
                break;
            case NavAction.Accept: _ = PressAsync(); break;
            case NavAction.X:
                // Hide the row to see the whole home screen (any button brings it back).
                bool show = _strip.Opacity < 0.5;
                _strip.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(180)));
                _scrim?.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(180)));
                _strip.IsHitTestVisible = show;
                break;
            case NavAction.Y:
                var others = Styles.Where(x => x.Id != _previewing).ToList();
                if (others.Count == 0) break;
                _index = Styles.IndexOf(others[Random.Shared.Next(others.Count)]);
                Highlight(scroll: true);
                _ = PreviewAsync(Styles[_index]);
                break;
            case NavAction.Back: _ = LeaveAsync(keep: false); break;
        }
        // The row back into view after any button while it's hidden.
        if (action != NavAction.X) ShowRow();
        return true; // the home screen underneath doesn't move
    }

    /// <summary>The row (and the dimming) back into view, if X had hidden it.</summary>
    private void ShowRow()
    {
        if (_strip.Opacity >= 0.5) return;
        _strip.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        _scrim?.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        _strip.IsHitTestVisible = true;
    }

    private async Task PressAsync()
    {
        if (Styles.Count == 0) return;
        var st = Styles[_index];
        if (st.Id == _previewing || (st.Id == _startStyle && _previewing == null)) await LeaveAsync(keep: true);
        else await PreviewAsync(st);
    }

    private async Task PreviewAsync(UiStyle st)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _snapshot ??= JsonSerializer.Serialize(Host.Settings, SafeFile.Indented);
            UiSound.Select();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Host.UseStyle(st, preview: true); // with its music, so you hear the whole style (8.5.99)
            _previewing = st.Id;
            Highlight(scroll: false);
        }
        catch (Exception ex) { CrashLog.Write("Style gallery preview", ex); }
        finally { _busy = false; }
    }

    private async Task LeaveAsync(bool keep)
    {
        if (_busy || _closing) return;
        _closing = true;
        StopGlide();
        try
        {
            if (keep && Styles.Count > 0)
            {
                var st = Styles[_index];
                if (st.Id != _startStyle || _previewing != null)
                {
                    // With style matching off a style doesn't bring its music: the preview's music goes back to yours.
                    if (_snapshot != null && !Host.Settings.StyleMatching && !MyStyles.IsCustom(st.Id)
                        && JsonSerializer.Deserialize<Settings>(_snapshot, SafeFile.Indented) is { } before)
                        Host.Settings.MenuMusic = before.MenuMusic;
                    Host.UseStyle(st); // properly now: with its music too
                    Host.ShowToast("Themes", st.Name + " applied", Host.Settings.StyleMatching ? "With its colors, wallpaper, sounds and music" : "Your colors, wallpaper and sounds are kept", "", "info", remember: false);
                }
                UiSound.Select();
            }
            else
            {
                if (_snapshot != null) Restore(_snapshot);
                UiSound.Back();
            }
        }
        catch (Exception ex) { CrashLog.Write("Style gallery", ex); }
        await Task.Yield();
        Host.Pop(this);
        if (_backToSettings) Host.Push(new SettingsScreen("looks"));
    }

    /// <summary>Puts every setting back as it was before the first preview, and the look with it.</summary>
    private void Restore(string json)
    {
        var old = JsonSerializer.Deserialize<Settings>(json, SafeFile.Indented);
        if (old == null) return;
        var s = Host.Settings;
        var labsBefore = s.Labs.ToHashSet();
        foreach (var p in typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.SetMethod is not { IsPublic: true } || p.GetIndexParameters().Length > 0 || p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
            try { p.SetValue(s, p.GetValue(old)); } catch (Exception) { }
        }
        s.Save();
        var changed = labsBefore.Union(s.Labs).Where(id => labsBefore.Contains(id) != s.Labs.Contains(id)).ToList();
        Host.ReapplyAfterPreview(changed);
    }
}
