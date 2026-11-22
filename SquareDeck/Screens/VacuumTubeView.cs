using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The Music page's VacuumTube tab (Experimental): a big Open button for YouTube's TV app, and VacuumTube's main
/// switches (ad blocking, SponsorBlock, DeArrow and the rest) to set before opening it. Everything else is in
/// VacuumTube's own settings, inside YouTube's Settings.
/// </summary>
public sealed class VacuumTubeView : Grid
{
    private readonly Screen _page;
    private readonly StackPanel _switches = new();
    private readonly Button _open;

    public VacuumTubeView(Screen page)
    {
        _page = page;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 0, 36, 0) };
        var badge = new Border
        {
            Width = 132, Height = 132, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner * 1.5, 18)), ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28))
        };
        if (LoadIcon() is ImageSource icon) badge.Child = new Image { Source = icon, Stretch = Stretch.Uniform, Margin = new Thickness(10) };
        else badge.Child = Ui.Glyph("", 54, "FgBrush");
        left.Children.Add(badge);
        var title = Ui.Text("VacuumTube", 32, "FgBrush", bold: true);
        title.Margin = new Thickness(0, 22, 0, 6);
        left.Children.Add(title);
        var about = Ui.Text("YouTube's TV and console app, made for the controller, with VacuumTube's ad blocking and extras. " +
                            "Sign in with a code on your phone, or watch as a guest.", 16, "SubtleBrush");
        about.TextWrapping = TextWrapping.Wrap;
        about.TextTrimming = TextTrimming.None;
        about.LineHeight = 23;
        left.Children.Add(about);
        _open = Ui.PanelButton("", "Open VacuumTube");
        _open.HorizontalAlignment = HorizontalAlignment.Left;
        _open.Margin = new Thickness(0, 24, 0, 0);
        _open.Click += (_, _) => Open();
        left.Children.Add(_open);
        var credit = Ui.Text($"VacuumTube {VacuumTube.Version} by shy · github.com/shy1132/VacuumTube · MIT licence", 13, "SubtleBrush");
        credit.TextWrapping = TextWrapping.Wrap;
        credit.Margin = new Thickness(0, 18, 0, 0);
        credit.Opacity = 0.8;
        left.Children.Add(credit);
        Children.Add(left);

        var right = new StackPanel();
        right.Children.Add(Ui.Header("VacuumTube settings"));
        right.Children.Add(_switches);
        var note = Ui.Text("The rest of VacuumTube's settings (more SponsorBlock choices, video codecs, the screensaver, music mode) are in " +
                           "YouTube's own Settings once it's open, under VacuumTube.",
                           15, "SubtleBrush");
        note.TextWrapping = TextWrapping.Wrap;
        note.TextTrimming = TextTrimming.None;
        note.LineHeight = 22;
        note.Margin = new Thickness(8, 10, 8, 8);
        right.Children.Add(note);
        right.Children.Add(Ui.Header("Coming back here"));
        var leave = Ui.Text("Press View + Menu together (the two small buttons in the middle of the controller). With touch or a mouse, " +
                            "tap the \u2190 SquareDeck button that shows in the top corner; with a keyboard, Ctrl+Q. YouTube's own Exit " +
                            "(B on its home row) works too.", 15, "SubtleBrush");
        leave.TextWrapping = TextWrapping.Wrap;
        leave.TextTrimming = TextTrimming.None;
        leave.LineHeight = 22;
        leave.Margin = new Thickness(8, 6, 8, 8);
        right.Children.Add(leave);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = right };
        Grid.SetColumn(scroll, 1);
        Children.Add(scroll);

        BuildSwitches();
    }

    public UIElement FirstFocus => _open;

    private void Open()
    {
        UiSound.Select();
        _page.Host.Push(new VacuumTubeScreen());
    }

    private void BuildSwitches(string? focusKey = null)
    {
        _switches.Children.Clear();
        Button? focus = null;
        foreach (var (key, name, about, fallback) in VacuumTube.Switches)
        {
            string k = key;
            bool on = VacuumTube.Get(k, fallback);
            var row = Ui.Row(name, on ? "On" : "Off", null, null, about);
            row.Click += (_, _) =>
            {
                VacuumTube.Set(k, !VacuumTube.Get(k, fallback));
                UiSound.Select();
                BuildSwitches(k);
            };
            _switches.Children.Add(row);
            if (k == focusKey) focus = row;
        }
        Ui.GroupRows(_switches);
        if (focus != null) _page.Host.FocusSoon(focus);
    }

    private static ImageSource? LoadIcon()
    {
        try
        {
            if (!File.Exists(VacuumTube.IconPath)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 264;
            bmp.UriSource = new Uri(VacuumTube.IconPath);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception) { return null; }
    }
}
