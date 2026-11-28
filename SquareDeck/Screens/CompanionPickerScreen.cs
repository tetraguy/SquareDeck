using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// 9.0.17: choosing the companion's character, color or accessory with a picture of each choice: every card shows the
/// companion as it would look with it (the cat in each color, your character wearing each accessory), and the big
/// preview on the left follows the highlight. Choices you don't have yet show what they cost or how to get them.
/// </summary>
public sealed class CompanionPickerScreen : Screen
{
    public sealed record Choice(string Id, string Name, string Species, string Color, string Accessory, string? Locked = null);

    private readonly string _title;
    private readonly IReadOnlyList<Choice> _choices;
    private readonly string _current;
    private readonly Action<Choice> _pick;
    private readonly Canvas _big = new() { Width = 240, Height = 240 };
    private readonly TextBlock _bigName = Ui.Text("", 26, "FgBrush");
    private readonly TextBlock _bigNote = Ui.Text("", 15, "SubtleBrush");
    private readonly WrapPanel _cards = new() { Width = 760 };
    private readonly DateTime _start = DateTime.UtcNow;
    private readonly System.Windows.Threading.DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(260) };
    private Choice? _shown;

    public override bool IsDialog => true;

    public CompanionPickerScreen(string title, string about, IReadOnlyList<Choice> choices, string current, Action<Choice> pick)
    {
        _title = title;
        _choices = choices;
        _current = current;
        _pick = pick;
        Switch2Settings.Refresh();

        var card = new Border { CornerRadius = new CornerRadius(24), Padding = new Thickness(34, 28, 34, 22), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), MaxHeight = 900 };
        card.Background = new SolidColorBrush(Switch2Settings.Background);
        card.BorderBrush = new SolidColorBrush(Switch2Settings.Line);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        head.Children.Add(Ui.Text(title, 30, "FgBrush"));
        var lead = Ui.Text(about, 15, "SubtleBrush");
        lead.TextWrapping = TextWrapping.Wrap;
        lead.MaxWidth = 1060;
        head.Children.Add(lead);
        layout.Children.Add(head);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
        var stage = new Border { Width = 270, Height = 270, CornerRadius = new CornerRadius(135), HorizontalAlignment = HorizontalAlignment.Center, Background = new SolidColorBrush(Switch2Settings.Card) };
        var holder = new Grid();
        holder.Children.Add(_big);
        _big.HorizontalAlignment = HorizontalAlignment.Center;
        _big.VerticalAlignment = VerticalAlignment.Center;
        stage.Child = holder;
        left.Children.Add(stage);
        _bigName.HorizontalAlignment = HorizontalAlignment.Center;
        _bigName.Margin = new Thickness(0, 16, 0, 2);
        left.Children.Add(_bigName);
        _bigNote.HorizontalAlignment = HorizontalAlignment.Center;
        _bigNote.TextAlignment = TextAlignment.Center;
        _bigNote.TextWrapping = TextWrapping.Wrap;
        left.Children.Add(_bigNote);
        body.Children.Add(left);
        var scroll = new ScrollViewer { Content = _cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, MaxHeight = 620, PanningMode = PanningMode.VerticalOnly };
        Grid.SetColumn(scroll, 1);
        body.Children.Add(scroll);
        Grid.SetRow(body, 1);
        layout.Children.Add(body);
        var hints = new HintBar("{Select}  Choose      {Back}  Back") { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        Grid.SetRow(hints, 2);
        layout.Children.Add(hints);
        card.Child = layout;

        var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xB8, 0, 0, 0)) };
        root.Children.Add(card);
        Content = root;
        BuildCards();
        _blink.Tick += (_, _) => DrawBig();
    }

    public override void OnShown()
    {
        _blink.Start();
        var first = _cards.Children.OfType<Button>().FirstOrDefault(b => (b.DataContext as Choice)?.Id == _current) ?? _cards.Children.OfType<Button>().FirstOrDefault();
        FocusLater(first);
    }

    public override void OnClosed() => _blink.Stop();

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Back) { Host.Pop(this); return true; }
        return false;
    }

    private void BuildCards()
    {
        var accent = (Brush)(TryFindResource("AccentBrush") ?? Brushes.DeepSkyBlue);
        foreach (var c in _choices)
        {
            var choice = c;
            bool current = c.Id == _current;
            var face = new Grid { Width = 168, Height = 176 };
            var sprite = new Canvas { Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 18, 0, 0) };
            CompanionArt.Draw(sprite, c.Species, c.Color, "happy", false, Labs.Outfit, cell: 8, accessory: c.Accessory);
            if (c.Locked != null) sprite.Opacity = 0.45;
            face.Children.Add(sprite);
            var name = Ui.Text(c.Name, 16, current ? "AccentBrush" : "FgBrush");
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.VerticalAlignment = VerticalAlignment.Bottom;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(6, 0, 6, c.Locked != null ? 30 : 14);
            face.Children.Add(name);
            if (c.Locked != null)
            {
                var lockText = Ui.Text("\uE72E  " + c.Locked, 12, "SubtleBrush");
                lockText.FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI");
                lockText.HorizontalAlignment = HorizontalAlignment.Center;
                lockText.VerticalAlignment = VerticalAlignment.Bottom;
                lockText.Margin = new Thickness(0, 0, 0, 10);
                face.Children.Add(lockText);
            }
            if (current)
            {
                var tick = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0), Background = accent };
                tick.Child = new TextBlock { Text = "\uE73E", FontFamily = Ui.IconFont, FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                face.Children.Add(tick);
            }
            var b = new Button { Style = Switch2Pages.TileStyle, Content = face, Margin = new Thickness(0, 0, 12, 12), DataContext = c };
            b.GotKeyboardFocus += (_, _) => { _shown = choice; DrawBig(); };
            b.MouseEnter += (_, _) => { _shown = choice; DrawBig(); };
            b.Click += (_, _) =>
            {
                if (choice.Locked != null) { UiSound.Error(); _bigNote.Text = choice.Locked; return; }
                UiSound.Select();
                _pick(choice);
                Host.Pop(this);
            };
            _cards.Children.Add(b);
        }
    }

    /// <summary>The big preview: the highlighted choice, blinking and bobbing now and then.</summary>
    private void DrawBig()
    {
        var c = _shown ?? _choices.FirstOrDefault(x => x.Id == _current) ?? _choices.FirstOrDefault();
        if (c == null) return;
        double t = (DateTime.UtcNow - _start).TotalSeconds;
        bool blink = (int)(t * 3.8) % 14 == 0;
        string mood = (int)(t / 3) % 3 == 1 ? "cheer" : "happy";
        CompanionArt.Draw(_big, c.Species, c.Color, mood, blink, Labs.Outfit, cell: 20, accessory: c.Accessory);
        _big.RenderTransform = new TranslateTransform(0, -Math.Abs(Math.Sin(t * 2.2)) * 8);
        _bigName.Text = c.Name;
        _bigNote.Text = c.Locked ?? (c.Id == _current ? "Chosen now" : "");
    }
}
