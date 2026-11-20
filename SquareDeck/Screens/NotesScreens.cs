using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// A game's notebook: your rating and status at the top, then your notes as colored cards (pinned first) and a few
/// templates to start from. Each note opens in <see cref="NoteEditorScreen"/>, a rich-text editor that can take this
/// game's screenshots. X on a card pins it, changes its color or deletes it.
/// </summary>
public sealed class GameNotesScreen : Screen
{
    public static readonly string[] Statuses = { "Playing", "Finished", "Dropped", "Backlog" };

    /// <summary>Starting points for a new note: (glyph, name, heading, lines). Lines starting with ☐ are checklist items.</summary>
    private static readonly (string Glyph, string Name, string[] Lines)[] Templates =
    {
        ("", "Where I left off", new[] { "Location: ", "Next goal: ", "Don't forget: " }),
        ("", "Boss strategy", new[] { "Weak points: ", "Attacks to watch for: ", "What worked: " }),
        ("", "Collectibles", new[] { "☐ ", "☐ ", "☐ " }),
        ("", "Codes and secrets", new[] { "Codes: ", "Passwords: ", "Secrets found: " }),
        ("", "Build and loadout", new[] { "Character / class: ", "Gear: ", "Skills: " }),
    };

    private readonly Game _game;
    private readonly StackPanel _page = new() { Margin = new Thickness(0, 0, 0, 20) };
    private List<NoteEntry> _notes = new();
    private string? _focusId;

    public GameNotesScreen(Game game)
    {
        _game = game;
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = _page
        };
        SetPage("Notebook – " + game.Name, scroll, "{Select}  Open        {Details}  Note options        {Back}  Back");
    }

    private ItemOverride Mine => Host.Library.For(_game.Key);

    public override void OnShown()
    {
        if (GameNotebook.Migrate(_game, Host.Library)) Host.Library.Save();
        Build();
    }

    public override void OnResumed() => Build();

    private void Build()
    {
        _notes = GameNotebook.Load(_game);
        _page.Children.Clear();
        _page.Children.Add(Header());

        // Notes
        var title = new DockPanel { Margin = new Thickness(4, 26, 4, 12) };
        var count = Ui.Text(_notes.Count == 0 ? "" : _notes.Count == 1 ? "1 note" : $"{_notes.Count} notes", 15, "SubtleBrush");
        count.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(count, Dock.Right);
        title.Children.Add(count);
        title.Children.Add(Ui.Text("Notes", 24, "FgBrush", bold: true));
        _page.Children.Add(title);

        var cards = new WrapPanel();
        Button? focus = null;
        var create = Card(null);
        cards.Children.Add(create);
        create.Click += (_, _) => Open(null, null);
        foreach (var note in GameNotebook.Ordered(_notes))
        {
            var card = Card(note);
            var n = note;
            card.Click += (_, _) => Open(n, null);
            card.Tag = n;
            cards.Children.Add(card);
            if (n.Id == _focusId) focus = card;
        }
        _page.Children.Add(cards);

        if (_notes.Count == 0)
        {
            var empty = Ui.Text("Your notebook for " + _game.Name + ". Write down where you stopped, tactics, codes or anything else, " +
                                "with your screenshots right next to the words.", 16, "SubtleBrush");
            empty.TextWrapping = TextWrapping.Wrap;
            empty.Margin = new Thickness(6, 4, 0, 0);
            empty.MaxWidth = 900;
            empty.HorizontalAlignment = HorizontalAlignment.Left;
            _page.Children.Add(empty);
        }

        // Templates
        var templateTitle = Ui.Text("Start from a template", 18, "FgBrush", bold: true);
        templateTitle.Margin = new Thickness(4, 22, 0, 10);
        _page.Children.Add(templateTitle);
        var templates = new WrapPanel();
        foreach (var template in Templates)
        {
            var t = template;
            var b = new Button { Style = Ui.StyleOf("PillButton"), Margin = new Thickness(0, 0, 10, 10), Content = IconLine(t.Glyph, t.Name) };
            b.Click += (_, _) => Open(null, t);
            templates.Children.Add(b);
        }
        _page.Children.Add(templates);

        FocusLater(focus ?? (_notes.Count > 0 && cards.Children.Count > 1 ? cards.Children[1] as Button : create));
    }

    private static StackPanel IconLine(string glyph, string text)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0) });
        line.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return line;
    }

    /// <summary>The game's cover with your rating and status, each a press away.</summary>
    private UIElement Header()
    {
        var mine = Host.Library.Find(_game.Key);
        var card = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(22) };
        card.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        card.BorderThickness = new Thickness(1);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var cover = new Border { Width = 120, Height = 120, CornerRadius = new CornerRadius(16), ClipToBounds = true, Margin = new Thickness(0, 0, 24, 0) };
        if (_game.Cover is { } art) cover.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        else cover.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        grid.Children.Add(cover);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 1);
        right.Children.Add(Ui.Text(_game.Name, 26, "FgBrush", bold: true));

        // Stars: five buttons; pressing the current rating again clears it.
        var stars = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 8, 0, 6) };
        int rating = mine?.Rating ?? 0;
        for (int i = 1; i <= 5; i++)
        {
            int value = i;
            var star = new Button { Style = Ui.StyleOf("PillButton"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 4, 0), Background = Brushes.Transparent, ToolTip = $"{value} of 5" };
            var glyph = new TextBlock { Text = value <= rating ? "" : "", FontFamily = Ui.IconFont, FontSize = 22 };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, value <= rating ? "AccentBrush" : "SubtleBrush");
            star.Content = glyph;
            star.Click += (_, _) => { Mine.Rating = Mine.Rating == value ? 0 : value; Saved(); };
            stars.Children.Add(star);
        }
        right.Children.Add(stars);

        var chips = new WrapPanel();
        foreach (var status in Statuses)
        {
            var st = status;
            bool on = mine?.Status == st;
            var chip = new Button { Style = Ui.StyleOf("PillButton"), Content = st, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(16, 6, 16, 6), FontSize = 15 };
            if (on) { chip.SetResourceReference(BackgroundProperty, "AccentBrush"); chip.SetResourceReference(ForegroundProperty, "BgBrush"); }
            chip.Click += (_, _) => { Mine.Status = Mine.Status == st ? null : st; Saved(); };
            chips.Children.Add(chip);
        }
        right.Children.Add(chips);
        var hint = Ui.Text("Rating and status can filter and sort your library.", 13, "SubtleBrush");
        right.Children.Add(hint);
        grid.Children.Add(right);
        card.Child = grid;
        return card;
    }

    private void Saved()
    {
        Host.Library.Save();
        Host.LibraryEdited();
        _focusId = null;
        Build();
    }

    /// <summary>A note as a card: its color along the top, title, a few lines, its first picture and when it changed.</summary>
    private Button Card(NoteEntry? note)
    {
        const double W = 300, H = 214;
        var face = new Border { Width = W, Height = H, CornerRadius = new CornerRadius(18), ClipToBounds = true, BorderThickness = new Thickness(1) };
        face.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        var tint = note != null ? GameNotebook.ColorOf(note.Color) : null;
        if (tint is { } c)
        {
            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.6, 1) };
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, c.R, c.G, c.B), 0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x1C, c.R, c.G, c.B), 1));
            var host = new Grid();
            var under = new Border(); under.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            host.Children.Add(under);
            host.Children.Add(new Border { Background = fill });
            face.Child = host;
        }
        else face.SetResourceReference(Border.BackgroundProperty, "TileBrush");

        var grid = new Grid();
        if (face.Child is Grid tinted) tinted.Children.Add(grid); else face.Child = grid;

        if (note == null)
        {
            var add = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var plus = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), HorizontalAlignment = HorizontalAlignment.Center, Child = Ui.Glyph("", 28, "BgBrush") };
            plus.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            add.Children.Add(plus);
            var t = Ui.Text("New note", 18, "FgBrush", bold: true);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 12, 0, 0);
            add.Children.Add(t);
            grid.Children.Add(add);
            return Ui.Tile(face);
        }

        if (tint is { } strip) grid.Children.Add(new Border { Height = 6, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(strip) });
        var body = new DockPanel { Margin = new Thickness(18, 18, 18, 14), LastChildFill = true };

        var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(foot, Dock.Bottom);
        var when = Ui.Text(Ui.Ago(note.Updated), 13, "SubtleBrush");
        foot.Children.Add(when);
        var badges = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        void Badge(string glyph, string text)
        {
            var b = Ui.Glyph(glyph, 13, "SubtleBrush"); b.Margin = new Thickness(10, 1, 4, 0); b.VerticalAlignment = VerticalAlignment.Center;
            badges.Children.Add(b);
            var tb = Ui.Text(text, 13, "SubtleBrush"); tb.VerticalAlignment = VerticalAlignment.Center;
            badges.Children.Add(tb);
        }
        if (note.Checks > 0) Badge("", $"{note.Checked}/{note.Checks}");
        if (note.Pictures > 0) Badge("", note.Pictures.ToString());
        foot.Children.Add(badges);
        body.Children.Add(foot);

        var top = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        if (note.Pinned)
        {
            var pin = Ui.Glyph("", 16, "AccentBrush");
            pin.VerticalAlignment = VerticalAlignment.Top;
            pin.Margin = new Thickness(8, 4, 0, 0);
            DockPanel.SetDock(pin, Dock.Right);
            top.Children.Add(pin);
        }
        var title = Ui.Text(note.Title, 19, "FgBrush", bold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        top.Children.Add(title);
        body.Children.Add(top);

        var content = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        string thumb = GameNotebook.ThumbPath(_game, note);
        bool picture = thumb.Length > 0 && File.Exists(thumb);
        if (picture) content.ColumnDefinitions.Add(new ColumnDefinition());
        if (picture) content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var preview = Ui.Text(note.Preview.Length > 0 ? note.Preview : "Empty note", 14, "SubtleBrush");
        preview.TextWrapping = TextWrapping.Wrap;
        preview.TextTrimming = TextTrimming.WordEllipsis;
        content.Children.Add(preview);
        if (picture && ImageLoader.FromFile(thumb, 220) is { } image)
        {
            var pic = new Border { Width = 104, Height = 104, CornerRadius = new CornerRadius(12), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top,
                                   Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill } };
            Grid.SetColumn(pic, 1);
            content.Children.Add(pic);
        }
        body.Children.Add(content);
        grid.Children.Add(body);
        return Ui.Tile(face);
    }

    private void Open(NoteEntry? note, (string Glyph, string Name, string[] Lines)? template)
    {
        if (note == null)
        {
            note = new NoteEntry { Title = template?.Name ?? "Untitled note" };
            if (template is { } t)
            {
                var doc = new FlowDocument();
                foreach (var line in t.Lines)
                {
                    var p = new Paragraph();
                    // Labels ("Location:") bold, the rest for you to fill in.
                    int colon = line.IndexOf(':');
                    if (colon > 0) { p.Inlines.Add(new Bold(new Run(line[..(colon + 1)]))); p.Inlines.Add(new Run(line[(colon + 1)..])); }
                    else p.Inlines.Add(new Run(line));
                    doc.Blocks.Add(p);
                }
                GameNotebook.WriteDocument(_game, note, doc);
            }
            _notes.Add(note);
            GameNotebook.Save(_game, _notes);
        }
        _focusId = note.Id;
        Host.Push(new NoteEditorScreen(_game, _notes, note));
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.X && Keyboard.FocusedElement is Button { Tag: NoteEntry note })
        {
            _ = OptionsAsync(note);
            return true;
        }
        return false;
    }

    private async Task OptionsAsync(NoteEntry note)
    {
        int choice = await Host.AskAsync(note.Title, null, note.Pinned ? "Unpin" : "Pin to the top", "Change color", "Delete note", "Cancel");
        switch (choice)
        {
            case 0:
                note.Pinned = !note.Pinned;
                GameNotebook.Save(_game, _notes);
                break;
            case 1:
                int color = await Host.AskAsync("Note color", null, GameNotebook.Colors.Select(c => c.Name).Append("Cancel").ToArray());
                if (color < 0 || color >= GameNotebook.Colors.Length) return;
                note.Color = GameNotebook.Colors[color].Id;
                GameNotebook.Save(_game, _notes);
                break;
            case 2:
                if (await Host.AskAsync("Delete “" + note.Title + "”?", "The note and its pictures are removed. This can't be undone.", "Cancel", "Delete") != 1) return;
                GameNotebook.Delete(_game, _notes, note);
                _focusId = null;
                break;
            default:
                return;
        }
        _focusId = choice == 2 ? null : note.Id;
        Build();
    }
}

/// <summary>
/// One note, in a rich-text editor: a title, a toolbar (bold, italic, underline, strikethrough, headings, lists,
/// checklists, quotes, highlight, text color, the date) and the page. Pictures come from this game's screenshots or
/// any file and sit in the text with a caption. Typing works with a keyboard or the touch keyboard; with a
/// controller, A on the page types a line (or ticks a checklist item). Saves by itself.
/// </summary>
public sealed class NoteEditorScreen : Screen
{
    private static readonly (string Name, string Hex)[] InkColors =
    {
        ("Default", ""), ("Red", "#FF5A5F"), ("Orange", "#FF9F43"), ("Yellow", "#FFD166"), ("Green", "#3DDC97"), ("Blue", "#4EA8FF"), ("Purple", "#B084FF"),
    };

    private readonly Game _game;
    private readonly List<NoteEntry> _notes;
    private readonly NoteEntry _note;
    private readonly RichTextBox _editor;
    private readonly Button _title;
    private readonly TextBlock _meta;
    private readonly Border _paper;
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _dirty, _loading;
    private DateTime? _savedAt;
    private int _ink;

    public NoteEditorScreen(Game game, List<NoteEntry> notes, NoteEntry note)
    {
        _game = game;
        _notes = notes;
        _note = note;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        // Title and details
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        _title = new Button { Style = Ui.StyleOf("PillButton"), Padding = new Thickness(18, 6, 18, 8), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Rename" };
        _title.Click += (_, _) => _ = RenameAsync();
        _meta = Ui.Text("", 14, "SubtleBrush");
        _meta.VerticalAlignment = VerticalAlignment.Center;
        _meta.Margin = new Thickness(16, 0, 0, 0);
        DockPanel.SetDock(_title, Dock.Left);
        head.Children.Add(_title);
        head.Children.Add(_meta);
        root.Children.Add(head);

        // Toolbar
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(tools, 1);
        root.Children.Add(tools);

        // The page
        _editor = new RichTextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 19, AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0), FocusVisualStyle = null,
            SpellCheck = { IsEnabled = true }
        };
        _editor.SetResourceReference(ForegroundProperty, "FgBrush");
        _editor.SetResourceReference(RichTextBox.CaretBrushProperty, "AccentBrush");
        _editor.SetResourceReference(RichTextBox.SelectionBrushProperty, "AccentBrush");
        _editor.TextChanged += (_, _) => { if (!_loading) { _dirty = true; ShowMeta(); } };
        _paper = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(34, 28, 34, 28), Child = _editor, BorderThickness = new Thickness(2) };
        _paper.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        _paper.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        _editor.GotKeyboardFocus += (_, _) => _paper.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        _editor.LostKeyboardFocus += (_, _) => _paper.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        if (GameNotebook.ColorOf(note.Color) is { } c)
            _paper.Background = new LinearGradientBrush(Color.FromArgb(0x30, c.R, c.G, c.B), Color.FromArgb(0x10, c.R, c.G, c.B), 90);
        Grid.SetRow(_paper, 2);
        root.Children.Add(_paper);

        void Tool(string label, string tip, Action act, bool glyph = true, FontWeight? weight = null, FontStyle? style = null, TextDecorationCollection? decorations = null)
        {
            var text = new TextBlock { Text = label, FontSize = glyph ? 17 : 17, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            if (glyph) text.FontFamily = Ui.IconFont;
            if (weight is { } w) text.FontWeight = w;
            if (style is { } st) text.FontStyle = st;
            if (decorations != null) text.TextDecorations = decorations;
            var b = new Button { Style = Ui.StyleOf("PillButton"), Content = text, ToolTip = tip, Width = 48, Height = 46, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (_, _) =>
            {
                act();
                _dirty = true;
                ShowMeta();
                // With a mouse or touch, go straight back to typing; with a controller, stay on the toolbar.
                if (InputManager.Current.MostRecentInputDevice is MouseDevice or StylusDevice) _editor.Focus();
            };
            tools.Children.Add(b);
        }
        void Gap() => tools.Children.Add(new Border { Width = 10 });

        Tool("", "Type a line (for controllers)", () => _ = TypeAsync());
        Gap();
        Tool("B", "Bold", () => EditingCommands.ToggleBold.Execute(null, _editor), glyph: false, weight: FontWeights.Bold);
        Tool("I", "Italic", () => EditingCommands.ToggleItalic.Execute(null, _editor), glyph: false, style: FontStyles.Italic);
        Tool("U", "Underline", () => EditingCommands.ToggleUnderline.Execute(null, _editor), glyph: false, decorations: TextDecorations.Underline);
        Tool("S", "Strikethrough", ToggleStrike, glyph: false, decorations: TextDecorations.Strikethrough);
        Gap();
        Tool("H1", "Big heading", () => Heading(30), glyph: false, weight: FontWeights.Bold);
        Tool("H2", "Small heading", () => Heading(23), glyph: false, weight: FontWeights.SemiBold);
        Tool("", "Bulleted list", () => EditingCommands.ToggleBullets.Execute(null, _editor));
        Tool("1.", "Numbered list", () => EditingCommands.ToggleNumbering.Execute(null, _editor), glyph: false, weight: FontWeights.SemiBold);
        Tool("☑", "Checklist item", ToggleCheck, glyph: false);
        Tool("❝", "Quote", Quote, glyph: false);
        Gap();
        Tool("", "Highlight", Highlight);
        Tool("", "Text color", NextInk);
        Tool("", "Insert the date and time", Stamp);
        Gap();
        Tool("", "Insert a screenshot of this game", () => Host.Push(new NotePicturePickerScreen(_game, InsertPicture)));
        Tool("", "Insert a picture from a file", () => _ = PictureFromFileAsync());
        Gap();
        Tool("", "Note color", () => _ = ColorAsync());
        Tool("", "Pin to the top of the notebook", () => { _note.Pinned = !_note.Pinned; GameNotebook.Save(_game, _notes); Host.ShowToast("Notebook", _note.Pinned ? "Pinned" : "Unpinned", _note.Title, "", remember: false); });
        Tool("", "Delete note", () => _ = DeleteAsync());

        SetPage(game.Name, root, "{Select}  Type or tick        {Back}  Save and close");
        _autosave.Tick += (_, _) => { if (_dirty) SaveNow(); };
    }

    public override void OnShown()
    {
        _loading = true;
        _editor.Document = GameNotebook.ReadDocument(_game, _note);
        _editor.Document.PagePadding = new Thickness(0);
        _editor.Document.LineHeight = 30;
        _editor.Document.FontFamily = FontFamily;
        if (!_editor.Document.Blocks.Any()) _editor.Document.Blocks.Add(new Paragraph());
        _loading = false;
        ShowTitle();
        ShowMeta();
        _autosave.Start();
        // Fade the page in.
        if (!Theme.ReduceMotion)
        {
            _paper.Opacity = 0;
            _paper.RenderTransform = new TranslateTransform(0, 16);
            _paper.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
            ((TranslateTransform)_paper.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        _editor.CaretPosition = _editor.Document.ContentEnd;
    }

    public override void FocusDefault() => _editor.Focus();

    public override void OnClosed()
    {
        _autosave.Stop();
        if (_dirty) SaveNow();
    }

    private void ShowTitle()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = _note.Title, FontSize = 26, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 760, TextTrimming = TextTrimming.CharacterEllipsis });
        line.Children.Add(new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 15, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 2, 0, 0) });
        _title.Content = line;
    }

    private void ShowMeta()
    {
        var text = new TextRange(_editor.Document.ContentStart, _editor.Document.ContentEnd).Text;
        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetterOrDigit));
        string saved = _dirty ? "Editing…" : _savedAt is { } at ? "Saved " + (DateTime.Now - at < TimeSpan.FromSeconds(30) ? "just now" : Ui.Ago(at)) : "Saved";
        _meta.Text = $"{words} {(words == 1 ? "word" : "words")}  ·  {saved}  ·  started {_note.Created:MMM d, yyyy}";
    }

    private void SaveNow()
    {
        try
        {
            GameNotebook.WriteDocument(_game, _note, _editor.Document);
            if (!_notes.Contains(_note)) _notes.Add(_note);
            GameNotebook.Save(_game, _notes);
            _dirty = false;
            _savedAt = DateTime.Now;
            ShowMeta();
        }
        catch (Exception ex)
        {
            _meta.Text = "Couldn't save: " + ex.Message;
        }
    }

    // ------------------------------------------------------------------ toolbar actions

    private Paragraph CurrentParagraph()
    {
        if (_editor.CaretPosition.Paragraph is { } p) return p;
        var added = new Paragraph();
        _editor.Document.Blocks.Add(added);
        return added;
    }

    private void ToggleStrike()
    {
        var current = _editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        bool on = current is TextDecorationCollection d && d.Any(x => x.Location == TextDecorationLocation.Strikethrough);
        _editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, on ? new TextDecorationCollection() : TextDecorations.Strikethrough);
    }

    private void Heading(double size)
    {
        var p = CurrentParagraph();
        bool already = Math.Abs(p.FontSize - size) < 0.5;
        p.FontSize = already ? 19 : size;
        p.FontWeight = already ? FontWeights.Normal : FontWeights.Bold;
        p.Margin = already ? new Thickness(0) : new Thickness(0, 10, 0, 4);
    }

    /// <summary>None → ☐ → ☑ → none, on the line with the cursor.</summary>
    private void ToggleCheck()
    {
        var p = CurrentParagraph();
        var text = new TextRange(p.ContentStart, p.ContentEnd).Text;
        var start = p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        if (text.StartsWith("☐") || text.StartsWith("☑"))
        {
            bool done = text.StartsWith("☐");
            int length = text.Length > 1 && text[1] == ' ' ? 2 : 1;
            var end = start.GetPositionAtOffset(length, LogicalDirection.Forward) ?? start;
            new TextRange(start, end).Text = "";
            start = p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            if (done)
            {
                start.InsertTextInRun("☑ ");
                // A ticked item is greyed and struck through.
                var whole = new TextRange(p.ContentStart, p.ContentEnd);
                whole.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
                whole.ApplyPropertyValue(TextElement.ForegroundProperty, FindResource("SubtleBrush"));
            }
            else
            {
                var whole = new TextRange(p.ContentStart, p.ContentEnd);
                whole.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
                whole.ApplyPropertyValue(TextElement.ForegroundProperty, FindResource("FgBrush"));
            }
        }
        else start.InsertTextInRun("☐ ");
    }

    private void Quote()
    {
        var p = CurrentParagraph();
        bool quoted = p.BorderThickness.Left > 0;
        p.BorderThickness = quoted ? new Thickness(0) : new Thickness(4, 0, 0, 0);
        p.BorderBrush = quoted ? null : (Brush)FindResource("AccentBrush");
        p.Padding = quoted ? new Thickness(0) : new Thickness(16, 4, 0, 4);
        p.FontStyle = quoted ? FontStyles.Normal : FontStyles.Italic;
        p.Foreground = quoted ? null : (Brush)FindResource("SubtleBrush");
    }

    private void Highlight()
    {
        var current = _editor.Selection.GetPropertyValue(TextElement.BackgroundProperty);
        bool on = current is SolidColorBrush;
        var accent = (FindResource("AccentBrush") as SolidColorBrush)?.Color ?? Colors.Gold;
        _editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, on ? null : new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B)));
    }

    private void NextInk()
    {
        _ink = (_ink + 1) % InkColors.Length;
        var (name, hex) = InkColors[_ink];
        _editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,
            hex.Length == 0 ? FindResource("FgBrush") : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));
        Host.ShowToast("Text color", name, "Press again for the next color", "", remember: false);
    }

    private void Stamp()
    {
        var p = new Paragraph(new Italic(new Run(DateTime.Now.ToString("dddd, MMMM d  ·  h:mm tt")))) { Margin = new Thickness(0, 12, 0, 2) };
        p.SetResourceReference(TextElement.ForegroundProperty, "SubtleBrush");
        p.FontSize = 15;
        Insert(p);
        var next = new Paragraph();
        _editor.Document.Blocks.InsertAfter(p, next);
        _editor.CaretPosition = next.ContentStart;
    }

    /// <summary>Puts a block after the line with the cursor (or at the end).</summary>
    private void Insert(Block block)
    {
        var at = _editor.CaretPosition.Paragraph;
        Block? anchor = at;
        // Inside a list, after the whole list.
        while (anchor?.Parent is ListItem item && item.Parent is List list) anchor = list;
        if (anchor != null && anchor.Parent is FlowDocument) _editor.Document.Blocks.InsertAfter(anchor, block);
        else _editor.Document.Blocks.Add(block);
    }

    private async Task TypeAsync()
    {
        var text = await Host.PromptAsync("Add to the note", "Type a line. It goes in where the cursor is.", "", "Add");
        if (string.IsNullOrEmpty(text)) return;
        _editor.CaretPosition.InsertTextInRun(text);
        var p = _editor.CaretPosition.Paragraph;
        if (p != null)
        {
            var next = new Paragraph();
            if (p.Parent is FlowDocument) { _editor.Document.Blocks.InsertAfter(p, next); _editor.CaretPosition = next.ContentStart; }
        }
        _dirty = true;
        ShowMeta();
    }

    private async Task RenameAsync()
    {
        var name = await Host.PromptAsync("Note title", null, _note.Title);
        if (string.IsNullOrWhiteSpace(name)) return;
        _note.Title = name.Trim();
        GameNotebook.Save(_game, _notes);
        ShowTitle();
    }

    private async Task ColorAsync()
    {
        int choice = await Host.AskAsync("Note color", null, GameNotebook.Colors.Select(c => c.Name).Append("Cancel").ToArray());
        if (choice < 0 || choice >= GameNotebook.Colors.Length) return;
        _note.Color = GameNotebook.Colors[choice].Id;
        GameNotebook.Save(_game, _notes);
        if (GameNotebook.ColorOf(_note.Color) is { } c)
            _paper.Background = new LinearGradientBrush(Color.FromArgb(0x30, c.R, c.G, c.B), Color.FromArgb(0x10, c.R, c.G, c.B), 90);
        else _paper.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
    }

    private async Task DeleteAsync()
    {
        if (await Host.AskAsync("Delete “" + _note.Title + "”?", "The note and its pictures are removed. This can't be undone.", "Cancel", "Delete") != 1) return;
        _dirty = false;
        _autosave.Stop();
        GameNotebook.Delete(_game, _notes, _note);
        Host.Pop(this);
    }

    private async Task PictureFromFileAsync()
    {
        var picked = await Host.PickFileAsync("Choose a picture", new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" });
        if (picked != null) InsertPicture(picked);
    }

    /// <summary>A picture in its own line, rounded, with a small caption under it.</summary>
    public void InsertPicture(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 1280;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxWidth = 720, MaxHeight = 420, HorizontalAlignment = HorizontalAlignment.Left };
            var picture = new Paragraph(new InlineUIContainer(image)) { Margin = new Thickness(0, 10, 0, 2) };
            Insert(picture);
            var when = File.GetLastWriteTime(path);
            var caption = new Paragraph(new Run((CapturesScreen.IsImage(path) ? "Screenshot" : "Picture") + "  ·  " + when.ToString("MMM d, yyyy  h:mm tt")))
            {
                FontSize = 13, Margin = new Thickness(0, 0, 0, 12)
            };
            caption.SetResourceReference(TextElement.ForegroundProperty, "SubtleBrush");
            _editor.Document.Blocks.InsertAfter(picture, caption);
            var next = new Paragraph();
            _editor.Document.Blocks.InsertAfter(caption, next);
            _editor.CaretPosition = next.ContentStart;
            _dirty = true;
            SaveNow();
        }
        catch (Exception ex)
        {
            _ = Host.AskAsync("Couldn't add that picture", ex.Message, "OK");
        }
    }

    public override bool OnAction(NavAction action)
    {
        // With a controller, A on the page ticks a checklist line, or types a new line.
        if (action == NavAction.Accept && ReferenceEquals(Keyboard.FocusedElement, _editor))
        {
            var p = _editor.CaretPosition.Paragraph;
            var text = p == null ? "" : new TextRange(p.ContentStart, p.ContentEnd).Text;
            if (text.StartsWith("☐") || text.StartsWith("☑")) { ToggleCheck(); _dirty = true; ShowMeta(); }
            else _ = TypeAsync();
            return true;
        }
        return false;
    }
}

/// <summary>This game's screenshots (Game Bar and Steam) to drop into a note, or any picture file.</summary>
public sealed class NotePicturePickerScreen : GridScreen
{
    private readonly Game _game;
    private readonly Action<string> _picked;
    private TextBlock? _empty;
    private bool _all;

    public NotePicturePickerScreen(Game game, Action<string> picked) : base("Add a screenshot – " + game.Name, "{Select}  Add        {Back}  Back")
    {
        _game = game;
        _picked = picked;
    }

    public override void OnShown() => Fill();

    private void Fill()
    {
        Tiles.Children.Clear();
        if (_empty != null) Body.Children.Remove(_empty);
        _empty = null;
        AddTile(GlyphBox(280, 158, ""), "Choose a file…", 280, async () =>
        {
            var file = await Host.PickFileAsync("Choose a picture", new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" });
            if (file == null) return;
            Host.Pop(this);
            _picked(file);
        });
        AddTile(GlyphBox(280, 158, _all ? "" : ""), _all ? "Only this game's" : "All my captures", 280, () => { _all = !_all; Fill(); });

        var files = _all
            ? (Directory.Exists(CapturesScreen.Folder)
                ? new DirectoryInfo(CapturesScreen.Folder).EnumerateFiles().Where(f => CapturesScreen.IsImage(f.Name)).OrderByDescending(f => f.LastWriteTime).Take(80).Select(f => f.FullName).ToList()
                : new List<string>())
            : GameCaptures.For(_game, 80);
        Button? first = null;
        foreach (var file in files)
        {
            var path = file;
            var box = ImageBox(280, 158, out var view);
            var tile = AddTile(box, File.GetLastWriteTime(path).ToString("MMM d  ·  h:mm tt"), 280, () => { Host.Pop(this); _picked(path); });
            first ??= tile;
            _ = Task.Run(() => ImageLoader.FromFile(path, 300)).ContinueWith(t => view.Source = t.Result, TaskScheduler.FromCurrentSynchronizationContext());
        }
        if (files.Count == 0)
        {
            var note = Ui.Text(_all ? "No screenshots in your Captures folder yet." :
                "No screenshots of " + _game.Name + " yet. Take one with Win+Alt+PrtScn (or F12 in Steam), or show all your captures.", 16, "SubtleBrush");
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(6, 4, 0, 0);
            Body.Children.Add(note);
            _empty = note;
        }
        FocusLater(first ?? Tiles.Children.OfType<Button>().FirstOrDefault());
    }
}
