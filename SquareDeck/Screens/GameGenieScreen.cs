using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Game Genie (Experimental), from a game's Details page: the game's artwork and key facts, and questions and answers
/// about it written by Google Gemini, grouped by topic. A to open an answer (spoilers ask twice), X to ask your own
/// question, Y for more questions. The sheet is kept, so the next visit is instant.
/// </summary>
public sealed class GameGenieScreen : Screen
{
    private readonly Game _game;
    private readonly string _known;
    private readonly StackPanel _facts = new();
    private readonly TextBlock _summary = Ui.Text("", 16, "SubtleBrush");
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _side = new();
    private CancellationTokenSource _cts = new();
    private GameGenie.Sheet? _sheet;
    private bool _busy;
    private bool _shownTop; // the page has opened at the top once

    /// <summary>
    /// "Switch 2 style Game Genie": "Game Genie" in the title with a line under it; the kinds of question down the left
    /// (with how many each has), a tap or LT / RT jumping to them; on the right the game in a card (cover, name, the
    /// Genie's summary and facts), then the questions under bar-and-name headings as flat rows, in the UI style's colors.
    /// </summary>
    private readonly bool _s2 = Labs.On("s2genie");
    private readonly StackPanel _s2Nav = new() { Margin = new Thickness(0, 6, 0, 10) };
    private readonly StackPanel _s2Under = new() { Margin = new Thickness(40, 0, 18, 14) };
    private readonly List<FrameworkElement> _s2Heads = new();
    private readonly List<string> _s2Counts = new();
    private int _s2Current = -1;

    public GameGenieScreen(Game game, StoreDetails? details, GameLookup.WikiSummary? wiki)
    {
        _game = game;
        _known = Known(game, details, wiki);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ---- left: the game
        var art = new Border
        {
            Width = 260, Height = 260, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 8)), ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18)
        };
        art.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        if (game.Cover != null) art.Child = new Image { Source = game.Cover, Stretch = Stretch.UniformToFill };
        art.Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.4 };
        _side.Children.Add(art);
        var name = Ui.Text(game.Name, 26, "FgBrush", bold: true);
        name.TextWrapping = TextWrapping.Wrap;
        name.TextTrimming = TextTrimming.None;
        _side.Children.Add(name);
        _summary.TextWrapping = TextWrapping.Wrap;
        _summary.TextTrimming = TextTrimming.None;
        _summary.LineHeight = 22;
        _summary.Margin = new Thickness(0, 10, 0, 14);
        _side.Children.Add(_summary);
        _side.Children.Add(_facts);
        var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = _side, Margin = new Thickness(0, 0, 32, 0) };
        body.Children.Add(leftScroll);

        // ---- right: the questions
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = _list };
        Grid.SetColumn(_scroll, 1);
        body.Children.Add(_scroll);

        SetPage(Ask.PageTitle, body, _s2 ? "{Select}  Open answer        {Details}  Ask your own        {Options}  More questions        LT / RT  Section        {Back}  Back"
                                        : "{Select}  Open answer        {Details}  Ask your own        {Options}  More questions        {Back}  Back", wide: true);
        if (_s2) SetUpSwitch2(art, name, leftScroll, body);
    }

    // ---------------------------------------------------------------- Switch 2 style

    private void SetUpSwitch2(Border art, TextBlock name, ScrollViewer leftScroll, Grid body)
    {
        Switch2Settings.Refresh();
        var card = new SolidColorBrush(Switch2Settings.Card);
        card.Freeze();
        Resources["CircleBrush"] = card;
        Resources["TileBrush"] = card;

        // The game, in a card at the top of the right side: the cover on the left, the name, summary and facts beside it.
        leftScroll.Content = null;
        body.Children.Remove(leftScroll);
        body.Children.Remove(_scroll);
        _side.Children.Clear();
        art.Width = art.Height = 150;
        art.Margin = new Thickness(0, 0, 22, 0);
        art.VerticalAlignment = VerticalAlignment.Top;
        art.Effect = null;
        art.CornerRadius = new CornerRadius(12);
        name.FontSize = 24;
        name.FontWeight = FontWeights.Normal;
        var words = new StackPanel();
        words.Children.Add(name);
        words.Children.Add(_summary);
        words.Children.Add(_facts);
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.Children.Add(art);
        Grid.SetColumn(words, 1);
        top.Children.Add(words);
        var gameCard = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(22, 20, 22, 16), Margin = new Thickness(6, 14, 6, 6), Child = top };
        gameCard.SetResourceReference(Border.BackgroundProperty, "S2Card");
        gameCard.SetResourceReference(Border.BorderBrushProperty, "S2Line");
        var right = new StackPanel { Margin = new Thickness(0, 0, 18, 24) };
        right.Children.Add(gameCard);
        _scroll.Content = null; // the list is still the scroller's content: let go of it first, or WPF refuses the move
        right.Children.Add(_list);
        _scroll.Content = right;
        _scroll.SetValue(Grid.ColumnProperty, 0);

        ReplaceBody(Switch2Pages.Shell(_s2Nav, _scroll, _s2Under));
        UseSwitch2Chrome(Ask.PageTitle, "\uE82F");
        // A long name slides along to show the rest (and back), instead of being cut off.
        var under = new MarqueeText(Ui.Text(_game.Name, 15, "SubtleBrush")) { HorizontalAlignment = HorizontalAlignment.Left };
        _s2Under.Children.Add(under);
        _s2Under.Children.Add(Ui.Text("Answers by Google Gemini", 12, "SubtleBrush"));
        _scroll.ScrollChanged += (_, _) => MarkS2();
        BuildS2Nav();
    }

    /// <summary>The kinds of question on the left, from the headings on the page.</summary>
    private void BuildS2Nav()
    {
        if (!_s2) return;
        _s2Nav.Children.Clear();
        _s2Nav.Children.Add(Switch2Pages.NavItem("The game", null, false, () => JumpS2(-1), focusable: false));
        for (int i = 0; i < _s2Heads.Count; i++)
        {
            int index = i;
            var count = _s2Counts.ElementAtOrDefault(i);
            _s2Nav.Children.Add(Switch2Pages.NavItem(_s2Heads[i].Uid, count, false, () => JumpS2(index), focusable: false));
        }
        _s2Current = -2;
        MarkS2();
    }

    private double S2Top(FrameworkElement head)
    {
        try { return head.TransformToAncestor((Visual)_scroll.Content).Transform(new Point(0, 0)).Y; }
        catch (Exception) { return double.MaxValue; }
    }

    private void JumpS2(int index)
    {
        if (index >= _s2Heads.Count) return;
        _scroll.ScrollToVerticalOffset(index < 0 ? 0 : Math.Max(0, S2Top(_s2Heads[index]) - 10));
        UiSound.Move();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            // The highlight on the first question of that kind (or the first thing on the page).
            if (index < 0) { Descendants<Button>(_list).FirstOrDefault(b => b.Focusable && b.IsVisible)?.Focus(); return; }
            double top = S2Top(_s2Heads[index]);
            Descendants<Button>(_list).Where(b => b.Focusable && b.IsVisible)
                .Select(b => (b, y: S2Top(b))).Where(x => x.y >= top).OrderBy(x => x.y).FirstOrDefault().b?.Focus();
        }));
    }

    private void MarkS2()
    {
        if (!_s2) return;
        int current = -1;
        double line = _scroll.VerticalOffset + 80;
        for (int i = 0; i < _s2Heads.Count; i++) if (S2Top(_s2Heads[i]) <= line) current = i;
        if (_scroll.ScrollableHeight > 0 && _scroll.VerticalOffset >= _scroll.ScrollableHeight - 2 && _s2Heads.Count > 0) current = _s2Heads.Count - 1;
        if (current == _s2Current) return;
        _s2Current = current;
        for (int i = 0; i < _s2Nav.Children.Count; i++)
            if (_s2Nav.Children[i] is Button b) b.Tag = i == current + 1 ? "selected" : null;
    }

    /// <summary>What SquareDeck knows about the game, for Gemini to work from.</summary>
    internal static string Known(Game game, StoreDetails? d, GameLookup.WikiSummary? wiki)
    {
        var b = new StringBuilder();
        b.AppendLine("Title: " + game.Name);
        if (d != null)
        {
            if (d.Developers.Length > 0) b.AppendLine("Developer: " + d.Developers);
            if (d.Publishers.Length > 0) b.AppendLine("Publisher: " + d.Publishers);
            if (d.Genres.Length > 0) b.AppendLine("Genres: " + d.Genres);
            if (d.ReleaseDate.Length > 0) b.AppendLine("Released: " + d.ReleaseDate);
            if (d.Metacritic is int m) b.AppendLine("Metacritic: " + m);
            if (d.AchievementTotal > 0) b.AppendLine("Achievements: " + d.AchievementTotal);
            if (d.Description.Length > 0) b.AppendLine("Store description: " + d.Description);
        }
        if (wiki != null)
        {
            if (wiki.Description.Length > 0) b.AppendLine("Wikipedia: " + wiki.Description);
            if (wiki.Extract.Length > 0) b.AppendLine(wiki.Extract);
        }
        return b.ToString();
    }

    public override void OnShown()
    {
        if (!Host.Settings.DetailsGradient || _game.Cover is not System.Windows.Media.Imaging.BitmapSource cover) { Load(); return; }
        _ = Task.Run(() => ImageLoader.DominantColor(cover)).ContinueWith(t =>
        {
            if (t.Result is Color c) Dispatcher.BeginInvoke(() => SetPageTint(c));
        }, TaskScheduler.Default);
        Load();
    }

    public override void OnClosed() => _cts.Cancel();

    private void Load()
    {
        // Answers written ahead (for a new game, or since its details page opened) show straight away.
        _sheet = GameGenie.Cached(_game.Key);
        if (_sheet != null && GeniePrep.Fresh(_sheet)) { Show(); return; }
        if (GeniePrep.Pending(_game.Key) is { } pending) { _ = WaitForPreparedAsync(pending); return; }
        if (_sheet != null && !GameGenie.HasKey) { Show(); return; } // old, but nothing better to be had
        _ = MakeAsync();
    }

    /// <summary>The answers are being written in the background already: wait for those rather than asking twice.</summary>
    private async Task WaitForPreparedAsync(Task<GameGenie.Sheet?> pending)
    {
        _busy = true;
        ShowThinking(Ask.Who + " is almost ready with " + _game.Name + "…");
        GameGenie.Sheet? sheet = null;
        try { sheet = await pending; } catch (Exception) { }
        _busy = false;
        if (_cts.IsCancellationRequested) return;
        if (sheet != null) { _sheet = sheet; UiSound.Select(); Show(); }
        else _ = MakeAsync();
    }

    private async Task MakeAsync()
    {
        if (_busy) return;
        _busy = true;
        ShowThinking(Ask.Who + " is reading up on " + _game.Name + "…");
        try
        {
            // Each answer appears as soon as Gemini has written it; the rest keep coming in underneath.
            _sheet = await GameGenie.MakeAsync(_game.Key, _game.Name, _known, _cts.Token, partial => OnUi(() =>
            {
                if (!_busy || _cts.IsCancellationRequested || partial.Answers.Count == 0 && partial.Summary.Length == 0) return;
                _writing = partial;
                Show(writing: true);
            }));
            _writing = null;
            UiSound.Select();
            Show(keep: true);
        }
        catch (OperationCanceledException) { }
        catch (GameGenie.GenieException ex) { _writing = null; ShowProblem(ex.Message); }
        catch (Exception ex) { _writing = null; CrashLog.Write("Game Genie", ex); ShowProblem("Something went wrong: " + ex.Message); }
        finally { _busy = false; _writing = null; }
    }

    // ---------------------------------------------------------------- showing it

    /// <summary>Runs on the page's own thread.</summary>
    private void OnUi(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }

    /// <summary>The sheet as far as Gemini has written it, while it's still writing.</summary>
    private GameGenie.Sheet? _writing;

    /// <summary>Extra answers arriving from "More questions", shown under the rest while they're written.</summary>
    private List<GameGenie.Answer>? _arriving;

    /// <summary>
    /// Shows the sheet. <paramref name="writing"/>: answers are still arriving (from the first sheet or More questions),
    /// so a line says so at the end. <paramref name="keep"/> (and while writing): the question that has the focus, and
    /// the answers that are open, stay that way as the list is redrawn.
    /// </summary>
    private void Show(GameGenie.Answer? focus = null, bool writing = false, bool keep = false)
    {
        var sheet = _writing ?? _sheet;
        if (sheet == null) return;

        // Where you are, so a redraw doesn't move you.
        string? focusedQ = null;
        int focusedTool = -1;
        var openQs = new HashSet<string>();
        if (writing || keep)
        {
            if (Keyboard.FocusedElement is Button { Tag: GameGenie.Answer fa } fb && _list.IsAncestorOf(fb)) focusedQ = fa.Q;
            else if (Keyboard.FocusedElement is Button tool && _list.IsAncestorOf(tool)) focusedTool = Descendants<Button>(_list).ToList().IndexOf(tool);
            foreach (var b in Descendants<Button>(_list))
                if (b.Tag is GameGenie.Answer oa && b.Content is StackPanel { Children.Count: 2 } st && st.Children[1] is TextBlock { Visibility: Visibility.Visible })
                    openQs.Add(oa.Q);
        }

        _summary.Text = sheet.Summary;
        _facts.Children.Clear();
        foreach (var f in sheet.Facts)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = Ui.Text(f.Label.ToUpperInvariant(), 12, "SubtleBrush", bold: true);
            label.VerticalAlignment = VerticalAlignment.Top;
            label.Margin = new Thickness(0, 2, 8, 0);
            row.Children.Add(label);
            var value = Ui.Text(f.Value, 15, "FgBrush");
            value.TextWrapping = TextWrapping.Wrap;
            value.TextTrimming = TextTrimming.None;
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            _facts.Children.Add(row);
        }

        _list.Children.Clear();
        _s2Heads.Clear();
        _s2Counts.Clear();
        var tools = new WrapPanel { Margin = new Thickness(0, _s2 ? 10 : 0, 0, 10) };
        var ask = Ui.PanelButton("", "Ask your own question");
        ask.Margin = new Thickness(0, 0, 10, 10);
        ask.Click += (_, _) => _ = AskOwnAsync();
        tools.Children.Add(ask);
        var more = Ui.PanelButton("", "More questions");
        more.Margin = new Thickness(0, 0, 10, 10);
        more.Click += (_, _) => _ = MoreAsync();
        tools.Children.Add(more);
        var again = Ui.PanelButton("", "Start again");
        again.Margin = new Thickness(0, 0, 10, 10);
        again.Click += async (_, _) =>
        {
            if (await Host.AskAsync("Start again?", Ask.Who + " writes a new set of questions. Your own questions go too.", "Start again", "Cancel") != 0) return;
            GameGenie.Forget(_game.Key);
            _sheet = null;
            await MakeAsync();
        };
        tools.Children.Add(again);
        _list.Children.Add(tools);

        Button? first = null, focusButton = null, keepButton = null;
        var answers = _arriving == null ? sheet.Answers : sheet.Answers.Concat(_arriving).ToList();
        foreach (var category in GameGenie.Categories)
        {
            var items = answers.Where(a => a.Category == category).ToList();
            if (items.Count == 0) continue;
            FrameworkElement header;
            if (_s2)
            {
                header = Switch2Settings.Header(category);
                header.Uid = category;
                _s2Heads.Add(header);
                _s2Counts.Add(items.Count.ToString());
            }
            else
            {
                header = Ui.Header(category);
                header.Margin = new Thickness(4, 14, 0, 6);
            }
            _list.Children.Add(header);
            var group = new StackPanel();
            foreach (var a in items)
            {
                var row = QuestionRow(a);
                group.Children.Add(row);
                first ??= row;
                if (ReferenceEquals(a, focus)) focusButton = row;
                if (focusedQ != null && a.Q == focusedQ) keepButton = row;
                if (openQs.Contains(a.Q)) Reopen(row, a);
            }
            if (_s2) Switch2Pages.RestyleTree(group, 72); else Ui.GroupRows(group);
            _list.Children.Add(group);
        }
        if (writing)
        {
            // The Genie is still at it: a small working line under what's arrived so far.
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 16, 8, 4) };
            var dots = Ui.Glyph("\uE895", 18, "AccentBrush");
            dots.Margin = new Thickness(0, 0, 12, 0);
            dots.RenderTransformOrigin = new Point(0.5, 0.5);
            var spin = new RotateTransform();
            dots.RenderTransform = spin;
            if (!Theme.ReduceMotion) spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever });
            line.Children.Add(dots);
            int count = answers.Count(a => !a.Mine);
            line.Children.Add(Ui.Text(count == 0 ? Ask.Who + " is writing the answers…" : $"{Ask.Who} is still writing… {count} {(count == 1 ? "answer" : "answers")} so far", 15, "SubtleBrush"));
            _list.Children.Add(line);
        }

        var credit = Ui.Text("Answers are written by Google Gemini from the game's store page and Wikipedia, and can be wrong. " +
                             "Check anything important against the game itself.", 13, "SubtleBrush");
        credit.TextWrapping = TextWrapping.Wrap;
        credit.Margin = new Thickness(6, 18, 6, 12);
        credit.Opacity = 0.8;
        _list.Children.Add(credit);

        BuildS2Nav();
        if (focusButton != null) { FocusLater(focusButton); OpenAnswer(focusButton, focusButton.Tag as GameGenie.Answer); }
        else if (keepButton != null) FocusLater(keepButton);
        else if (focusedTool >= 0 && Descendants<Button>(_list).ElementAtOrDefault(focusedTool) is { } sameTool) FocusLater(sameTool);
        else
        {
            FocusLater(first ?? ask);
            // The first time the answers show, the page starts at the top (the game, its summary and facts), not
            // scrolled down to wherever the highlight landed. After that, it stays where you are.
            if (!_shownTop)
            {
                _shownTop = true;
                _scroll.ScrollToVerticalOffset(0);
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => _scroll.ScrollToVerticalOffset(0)));
            }
        }
    }

    /// <summary>Opens an answer again after a redraw (quietly, as it was: a spoiler you'd already revealed stays revealed).</summary>
    private void Reopen(Button row, GameGenie.Answer a)
    {
        if (row.Content is not StackPanel { Children.Count: 2 } stack || stack.Children[1] is not TextBlock answer) return;
        bool hidden = a.Spoiler && !_spoilersShown.Any(x => x.Q == a.Q);
        if (!hidden && a.Spoiler) _spoilersShown.Add(a);
        answer.Text = hidden ? "Spoiler: this gives away part of the story. Press A again to read it." : a.A;
        answer.Opacity = hidden ? 0.75 : 1;
        answer.Visibility = Visibility.Visible;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        if (n == 0 && root is Panel panel)
        {
            // Not drawn yet: walk the logical children.
            foreach (var child in panel.Children.OfType<DependencyObject>())
            {
                if (child is T t) yield return t;
                foreach (var d in Descendants<T>(child)) yield return d;
            }
            yield break;
        }
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    /// <summary>A question; A opens its answer under it (a spoiler asks first).</summary>
    private Button QuestionRow(GameGenie.Answer a)
    {
        var stack = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = Ui.IconBadge(a.Mine ? "" : a.Spoiler ? "" : "", 34);
        top.Children.Add(badge);
        var q = Ui.Text(a.Q, 18, "FgBrush");
        q.TextWrapping = TextWrapping.Wrap;
        q.TextTrimming = TextTrimming.None;
        q.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(q, 1);
        top.Children.Add(q);
        stack.Children.Add(top);
        var answer = Ui.Text("", 16, "SubtleBrush");
        answer.TextWrapping = TextWrapping.Wrap;
        answer.TextTrimming = TextTrimming.None;
        answer.LineHeight = 23;
        answer.Margin = new Thickness(48, 8, 8, 4);
        answer.Visibility = Visibility.Collapsed;
        stack.Children.Add(answer);
        var row = new Button { Style = Ui.StyleOf("RowButton"), Content = stack, Tag = a, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        row.Click += (_, _) => OpenAnswer(row, a);
        return row;
    }

    private readonly HashSet<GameGenie.Answer> _spoilersShown = new();

    private void OpenAnswer(Button row, GameGenie.Answer? a)
    {
        if (a == null || row.Content is not StackPanel { Children.Count: 2 } stack || stack.Children[1] is not TextBlock answer) return;
        if (answer.Visibility == Visibility.Visible && (!a.Spoiler || _spoilersShown.Contains(a)))
        {
            answer.Visibility = Visibility.Collapsed; // A again closes it
            UiSound.Back();
            return;
        }
        if (a.Spoiler && !_spoilersShown.Contains(a))
        {
            if (answer.Visibility == Visibility.Visible)
            {
                _spoilersShown.Add(a);
                answer.Text = a.A;
                answer.Opacity = 1;
            }
            else
            {
                answer.Text = "Spoiler: this gives away part of the story. Press A again to read it.";
                answer.Opacity = 0.75;
            }
        }
        else
        {
            answer.Text = a.A;
            answer.Opacity = 1;
        }
        answer.Visibility = Visibility.Visible;
        UiSound.Select();
        if (!Theme.ReduceMotion) answer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, answer.Opacity, TimeSpan.FromMilliseconds(180)));
        row.BringIntoView();
    }

    private void ShowThinking(string text)
    {
        _list.Children.Clear();
        // The Genie at work, floating from the lamp and trying one way after another to find the answers; or, with the
        // companion on, your companion doing the searching in its place.
        FrameworkElement genie = Ask.UseCompanion ? new CompanionSearch(text) : new GenieSearch(text);
        genie.Margin = new Thickness(0, 30, 0, 0);
        genie.HorizontalAlignment = HorizontalAlignment.Center;
        _list.Children.Add(genie);
        _s2Heads.Clear();
        _s2Counts.Clear();
        BuildS2Nav();
        if (_sheet == null) { _summary.Text = ""; _facts.Children.Clear(); }
    }

    private void ShowProblem(string message)
    {
        if (_sheet != null) { Show(); Host.ShowToast("Game Genie", Ask.Who + " couldn't answer", message, "", "info"); return; }
        _list.Children.Clear();
        var t = Ui.Text(message, 18, "FgBrush");
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(4, 40, 4, 16);
        _list.Children.Add(t);
        var retry = Ui.PanelButton("", "Try again");
        retry.HorizontalAlignment = HorizontalAlignment.Left;
        retry.Margin = new Thickness(0, 0, 0, 10);
        retry.Click += (_, _) => _ = MakeAsync();
        _list.Children.Add(retry);
        FocusLater(retry);
    }

    // ---------------------------------------------------------------- asking

    private async Task AskOwnAsync()
    {
        if (_busy || _sheet == null) return;
        string? question = await Host.PromptAsync(Ask.Title, "Anything about " + _game.Name + ": how to beat a part, what to do first, settings for a handheld…", "", "Ask");
        if (string.IsNullOrWhiteSpace(question)) return;
        _busy = true;
        ShowThinking(Ask.Who + " is thinking about: " + question.Trim());
        try
        {
            var answer = await GameGenie.AskAsync(_game.Key, _sheet, _known, question, _cts.Token);
            Show(answer);
        }
        catch (OperationCanceledException) { }
        catch (GameGenie.GenieException ex) { ShowProblem(ex.Message); }
        catch (Exception ex) { CrashLog.Write("Game Genie", ex); ShowProblem("Something went wrong: " + ex.Message); }
        finally { _busy = false; }
    }

    private async Task MoreAsync()
    {
        if (_busy || _sheet == null) return;
        _busy = true;
        // The list stays; new questions appear under it as they're written.
        _arriving = new List<GameGenie.Answer>();
        Show(writing: true, keep: true);
        try
        {
            var more = await GameGenie.MoreAsync(_game.Key, _sheet, _known, _cts.Token, found => OnUi(() =>
            {
                if (!_busy || _cts.IsCancellationRequested) return;
                _arriving = found;
                Show(writing: true);
            }));
            _arriving = null;
            Show(more.FirstOrDefault());
            if (more.Count == 0) Host.ShowToast("Game Genie", "No new questions this time", "Try again in a moment.", "", "info");
        }
        catch (OperationCanceledException) { }
        catch (GameGenie.GenieException ex) { _arriving = null; ShowProblem(ex.Message); }
        catch (Exception ex) { _arriving = null; CrashLog.Write("Game Genie", ex); ShowProblem("Something went wrong: " + ex.Message); }
        finally { _busy = false; _arriving = null; }
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.X: _ = AskOwnAsync(); return true;
            case NavAction.Y: _ = MoreAsync(); return true;
            case NavAction.LT or NavAction.RT when _s2:
                JumpS2(Math.Clamp(_s2Current + (action == NavAction.RT ? 1 : -1), -1, _s2Heads.Count - 1));
                return true;
        }
        return false;
    }

    /// <summary>A magic wand (the icon fonts have none): a stick with a sparkle at its tip and two small ones.</summary>
    public static Viewbox Wand(double size, Brush? brush)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var data in new[]
                 {
                     "M2.3,20.3 L13.6,9 L15,10.4 L3.7,21.7 Z",
                     "M18,1 L19.2,4.8 L23,6 L19.2,7.2 L18,11 L16.8,7.2 L13,6 L16.8,4.8 Z",
                     "M9,1.5 L9.6,3.4 L11.5,4 L9.6,4.6 L9,6.5 L8.4,4.6 L6.5,4 L8.4,3.4 Z",
                     "M21,13 L21.5,14.5 L23,15 L21.5,15.5 L21,17 L20.5,15.5 L19,15 L20.5,14.5 Z"
                 })
        {
            var path = new Path { Data = Geometry.Parse(data) };
            if (brush != null) path.Fill = brush;
            else path.SetResourceReference(Shape.FillProperty, "AccentBrush");
            canvas.Children.Add(path);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}

/// <summary>
/// Who answers on the Game Genie page: the Genie, or, with the companion on (the default when it's on), your
/// companion by name: "Ask Pip" on the game's page, and Pip doing the searching in the Genie's place.
/// </summary>
public static class Ask
{
    public static bool UseCompanion => Labs.On("companion");

    /// <summary>"The Genie", or the companion's name.</summary>
    public static string Who => UseCompanion ? CompanionArt.Name : "The Genie";

    /// <summary>The button on a game's page: "Ask Genie", or "Ask Pip".</summary>
    public static string Button => UseCompanion ? "Ask " + CompanionArt.Name : "Ask Genie";

    /// <summary>The question prompt's title.</summary>
    public static string Title => UseCompanion ? "Ask " + CompanionArt.Name : "Ask the Genie";

    /// <summary>The page's title.</summary>
    public static string PageTitle => UseCompanion ? "Ask " + CompanionArt.Name : "Game Genie";
}
