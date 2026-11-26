using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The Y menu for a game or app: its logo (or name) over a strip of its artwork, then its options in groups, each
/// with an icon. Returns the chosen option's index.
/// </summary>
public sealed class GameOptionsScreen : Screen
{
    public sealed record Option(string Group, string Label, string Glyph, bool Danger = false);

    private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Game _game;
    private readonly Border _logoHost = new() { Height = 96, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
    private readonly Image _art = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
    private readonly CancellationTokenSource _cts = new();
    private readonly StackPanel _rows = new();
    private int _result = -1;

    public override bool IsDialog => true;

    public Task<int> Result => _done.Task;

    public GameOptionsScreen(Game game, IReadOnlyList<Option> options, string? subtitle)
    {
        _game = game;

        bool s2 = Switch2Pages.SystemOn; // Experimental: Switch 2 style
        if (s2) Switch2Settings.ApplyColors(this);
        // Header: artwork behind, the logo (or the name until the logo arrives, or if there is none).
        var header = new Grid { Height = 150, ClipToBounds = true };
        _art.Source = game.Cover;
        _art.Effect = new BlurEffect { Radius = 26, RenderingBias = RenderingBias.Performance };
        _art.Margin = new Thickness(-40);
        header.Children.Add(_art);
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        var bg = (Application.Current.Resources["DialogBrush"] as SolidColorBrush)?.Color
                 ?? (Application.Current.Resources["BgBrush"] as SolidColorBrush)?.Color ?? Colors.Black;
        if (s2) bg = Switch2Settings.Card;
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, bg.R, bg.G, bg.B), 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, bg.R, bg.G, bg.B), 1));
        header.Children.Add(new Border { Background = fade });
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(34, 0, 34, 10) };
        _logoHost.Child = NameText(game.Name);
        titleStack.Children.Add(_logoHost);
        if (!string.IsNullOrEmpty(subtitle))
        {
            var sub = Ui.Text(subtitle, 15, "SubtleBrush");
            sub.HorizontalAlignment = HorizontalAlignment.Center; // under the centered logo
            titleStack.Children.Add(sub);
        }
        header.Children.Add(titleStack);

        // The options, in groups.
        for (int i = 0; i < options.Count; i++)
        {
            var o = options[i];
            if (i == 0 || options[i - 1].Group != o.Group)
            {
                var h = Ui.Text(s2 ? o.Group : o.Group.ToUpperInvariant(), 12, "SubtleBrush", bold: true);
                if (s2) h.Tag = "group"; // drawn as a bar-and-name heading below
                h.Margin = new Thickness(8, i == 0 ? 2 : 14, 0, 6);
                _rows.Children.Add(h);
            }
            int index = i;
            var row = Ui.Row(o.Label, glyph: o.Glyph);
            row.FontSize = 18;
            if (o.Danger) row.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x7A));
            row.Click += (_, _) => Choose(index);
            _rows.Children.Add(row);
        }
        Ui.GroupRows(_rows);
        if (s2)
        {
            // Switch 2 style: flat rows on thin lines, no icons, headings with a short bar.
            Switch2Settings.Restyle(_rows);
            foreach (var h in _rows.Children.OfType<FrameworkElement>().Where(e => e.Tag as string == "s2header"))
                h.Margin = new Thickness(14, _rows.Children.IndexOf(h) == 0 ? 4 : 20, 0, 4);
            foreach (var r in _rows.Children.OfType<Button>()) r.MinHeight = 58;
        }

        var body = new ScrollViewer
        {
            MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly,
            Focusable = false, Content = _rows, Padding = new Thickness(26, 6, 26, 20)
        };
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(header);
        Grid.SetRow(body, 1);
        panel.Children.Add(body);

        var box = new Border
        {
            Width = 640, CornerRadius = new CornerRadius(22), ClipToBounds = true, Child = panel,
            Effect = new DropShadowEffect { BlurRadius = 44, ShadowDepth = 8, Opacity = 0.5 },
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        box.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        box.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        if (s2) { box.Background = new SolidColorBrush(Switch2Settings.Card); box.BorderThickness = new Thickness(0); }
        // Rounded corners for the artwork too.
        box.Loaded += (_, _) => panel.Clip = new RectangleGeometry(new Rect(0, 0, box.ActualWidth, box.ActualHeight), 22, 22);

        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xA8, 0, 0, 0)) };
        backdrop.Children.Add(box);
        var hints = new HintBar("{Select}  Select      {Back}  Close")
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 40, 24)
        };
        backdrop.Children.Add(hints);
        Content = backdrop;
    }

    private static TextBlock NameText(string name)
    {
        var t = Ui.Text(name, 34, "FgBrush", bold: true);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.MaxWidth = 560;
        t.TextAlignment = TextAlignment.Center;
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Bottom;
        return t;
    }

    public override void OnShown()
    {
        _art.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.85, TimeSpan.FromMilliseconds(300)));
        _ = LoadArtAsync();
    }

    /// <summary>The game's logo and wide artwork from SteamGridDB (usually already downloaded).</summary>
    private async Task LoadArtAsync()
    {
        if (!_game.IsGame) return;
        var key = Host.Settings.SteamGridDbApiKey;
        var ct = _cts.Token;
        try
        {
            var (logo, hero) = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(key);
                var logoPath = await client.GetLogoAsync(_game, ct);
                var heroPath = await client.GetHeroAsync(_game, ct);
                return (logoPath == null ? null : ImageLoader.FromFile(logoPath, 640),
                        heroPath == null ? null : ImageLoader.FromFile(heroPath, 900));
            }, ct);
            if (ct.IsCancellationRequested) return;
            if (hero != null) _art.Source = hero;
            if (logo != null)
            {
                var image = new Image
                {
                    Source = logo, Stretch = Stretch.Uniform, MaxHeight = 96, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0, ToolTip = _game.Name,
                    Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 }
                };
                _logoHost.Child = image;
                image.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
            }
        }
        catch (Exception) { }
    }

    private void Choose(int index)
    {
        _result = index;
        Host.Pop(this);
    }

    public override void OnClosed()
    {
        _cts.Cancel();
        _done.TrySetResult(_result);
    }

    public override void FocusDefault() => _rows.Children.OfType<Button>().FirstOrDefault()?.Focus();
}
