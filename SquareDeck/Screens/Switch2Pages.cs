using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Experimental "Switch 2 style pages": Captures, Multimedia and Stores laid out like the Switch 2's System Settings,
/// as the Switch 2 style Settings and Library are. The pieces they share: the two panes (sections on the left, the
/// page on the right, a thin line between), the section buttons, the plain rows, and the grid tile with the rimmed
/// highlight. Colors come from the page (Switch2Settings.ApplyColors), so they follow the UI style.
/// </summary>
public static class Switch2Pages
{
    public static bool On => Labs.On("s2pages");

    /// <summary>Experimental "Switch 2 style Recomp Zone, Retro Lounge, Explorer and Task Manager".</summary>
    public static bool ToolsOn => Labs.On("s2tools");

    /// <summary>Experimental "Switch 2 style News and Friends".</summary>
    public static bool SocialOn => Labs.On("s2social");

    /// <summary>Experimental "Switch 2 style menus, Search, Browser, Notifications and game pages".</summary>
    public static bool SystemOn => Labs.On("s2system");

    /// <summary>Pages drawn this way, whose pop-ups take the Switch 2 look too.</summary>
    public static bool Applies(Screen screen) =>
        screen is Switch2LibraryScreen || (Switch2Settings.On && screen is SettingsScreen)
        || (On && screen is CapturesScreen or MediaScreen or StoresScreen)
        || (ToolsOn && screen is RecompZoneScreen or RetroLoungeScreen or ExplorerScreen or TaskManagerScreen)
        || (SocialOn && screen is GameNewsScreen or FriendsScreen or ProfileScreen)
        || (SystemOn && screen is SearchScreen or DetailsScreen)
        || screen is S2PlayScreen
        || (Labs.On("s2apps") && screen is InstalledAppsScreen or AppOptionsScreen)
        || (Labs.On("s2setup") && screen is OnboardingScreen);

    /// <summary>
    /// The two panes: the sections (and anything under them) on the left, the page on the right, a thin line between.
    /// </summary>
    public static Grid Shell(UIElement nav, UIElement page, UIElement? underNav = null, double navWidth = 272)
    {
        var left = new Grid { Width = navWidth };
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.Children.Add(nav is ScrollViewer ? nav : new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = nav
        });
        if (underNav != null)
        {
            Grid.SetRow(underNav, 1);
            left.Children.Add(underNav);
        }
        var divider = new Rectangle { Width = 1, Margin = new Thickness(8, 18, 18, 18) };
        divider.SetResourceReference(Shape.FillProperty, "S2Line");

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(left);
        Grid.SetColumn(divider, 1);
        grid.Children.Add(divider);
        Grid.SetColumn(page, 2);
        grid.Children.Add(page);
        return grid;
    }

    /// <summary>A section on the left: its name, and a count in gray; blue with a bar beside it when it's the one shown.</summary>
    public static Button NavItem(string name, string? count, bool selected, Action click, bool focusable = true)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrEmpty(count))
        {
            var n = Ui.Text(count, 16, "SubtleBrush");
            n.VerticalAlignment = VerticalAlignment.Center;
            n.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(n, 1);
            grid.Children.Add(n);
        }
        var button = new Button { Style = Switch2Settings.NavStyle, Content = grid, Tag = selected ? "selected" : null, Focusable = focusable };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>The thin line between groups of sections.</summary>
    public static Rectangle NavLine()
    {
        var line = new Rectangle { Height = 1, Margin = new Thickness(40, 8, 18, 8) };
        line.SetResourceReference(Shape.FillProperty, "S2Line");
        return line;
    }

    /// <summary>A plain settings row: the name on the left, its value in blue on the right.</summary>
    public static Button SettingRow(string name, string? value, Action click)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(value))
        {
            var v = Ui.Text(value, 19, "AccentBrush");
            v.VerticalAlignment = VerticalAlignment.Center;
            v.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(v, 1);
            grid.Children.Add(v);
        }
        var button = new Button { Style = Switch2Settings.RowStyle, Content = grid, DataContext = "setting:" + name };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Two settings side by side on one line (the second from the middle), as the Library's Show as and Sort by.</summary>
    public static UniformGrid OptionLine(params Button[] rows)
    {
        var line = new UniformGrid { Rows = 1, Columns = 2 };
        foreach (var r in rows) line.Children.Add(r);
        return line;
    }

    /// <summary>The page's own title over the right pane, with a count on the right in gray.</summary>
    public static Grid PageTitle(string name, string? count)
    {
        var title = new Grid { Margin = new Thickness(14, 22, 14, 4) };
        title.Children.Add(Ui.Text(name, 28, "FgBrush"));
        if (!string.IsNullOrEmpty(count))
        {
            var c = Ui.Text(count, 18, "SubtleBrush");
            c.HorizontalAlignment = HorizontalAlignment.Right;
            c.VerticalAlignment = VerticalAlignment.Bottom;
            title.Children.Add(c);
        }
        return title;
    }

    /// <summary>
    /// A grid tile SquareDeck already built (a picture with its caption under it) redrawn the Switch 2 way: the rimmed
    /// highlight card round it, the caption in regular type.
    /// </summary>
    public static void RestyleTile(Button tile)
    {
        tile.Style = TileStyle;
        tile.Margin = new Thickness(0, 0, 8, 10);
        if (tile.Content is StackPanel stack)
            foreach (var caption in stack.Children.OfType<TextBlock>())
            {
                caption.FontWeight = FontWeights.Normal;
                caption.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
            }
    }

    /// <summary>
    /// Redraws what SquareDeck built under <paramref name="root"/> the Switch 2 way: tiles get the rimmed highlight card,
    /// rows become flat rows on thin lines. Anything already redrawn is left alone, so it can be run again as a page
    /// fills in.
    /// </summary>
    public static void RestyleTree(DependencyObject root, double rowHeight = 64)
    {
        var tile = Ui.StyleOf("TileButton");
        var row = Ui.StyleOf("RowButton");
        // Section titles marked for it become the bar-and-name kind.
        foreach (var title in Descendants<TextBlock>(root).Where(t => t.Tag as string == "s2title").ToList())
            if (title.Parent is Panel panel)
            {
                int at = panel.Children.IndexOf(title);
                panel.Children.RemoveAt(at);
                var header = Switch2Settings.Header(title.Text);
                header.Margin = new Thickness(4, title.Margin.Top, 0, title.Margin.Bottom);
                panel.Children.Insert(at, header);
            }
        foreach (var button in Descendants<Button>(root).ToList())
        {
            if (button.Style == tile) RestyleTile(button);
            else if (button.Style == row)
            {
                button.Style = Switch2Settings.RowStyle;
                button.MinHeight = rowHeight;
                button.Margin = new Thickness(0);
                button.Resources.Remove("RowCorner");
            }
        }
    }

    /// <summary>Keeps redrawing a part of a page the Switch 2 way as it's built and rebuilt (a moment after it changes).</summary>
    public static void AutoRestyle(FrameworkElement root, double rowHeight = 64)
    {
        var soon = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        soon.Tick += (_, _) => { soon.Stop(); RestyleTree(root, rowHeight); };
        root.LayoutUpdated += (_, _) => { if (!soon.IsEnabled) soon.Start(); };
        root.Unloaded += (_, _) => soon.Stop();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T found) yield return found;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    /// <summary>
    /// A page's own sidebar (Recomp Zone, Retro Lounge) as the Switch 2's list of sections: no card behind it, a thin
    /// line down its right side.
    /// </summary>
    public static Grid SidebarHolder(UIElement nav, double width = 272)
    {
        var holder = new Grid { Width = width, Margin = new Thickness(0, 0, 18, 12) };
        holder.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = nav, Margin = new Thickness(0, 6, 9, 0)
        });
        var line = new Rectangle { Width = 1, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 18) };
        line.SetResourceReference(Shape.FillProperty, "S2Line");
        holder.Children.Add(line);
        return holder;
    }

    private static Style? _tile;

    /// <summary>A grid tile: a little padding round the picture, and the rimmed card behind it when highlighted.</summary>
    public static Style TileStyle => _tile ??= (Style)System.Windows.Markup.XamlReader.Parse(
        """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Setter Property="Foreground" Value="{DynamicResource FgBrush}"/>
          <Setter Property="Margin" Value="0,0,8,10"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid>
                  <Border x:Name="Card" CornerRadius="14" BorderThickness="3" BorderBrush="Transparent" Background="Transparent"/>
                  <Border Padding="8,8,8,10">
                    <ContentPresenter/>
                  </Border>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="Card" Property="Background" Value="{DynamicResource S2Card}"/>
                    <Setter TargetName="Card" Property="BorderBrush" Value="{DynamicResource S2Rim}"/>
                    <Setter TargetName="Card" Property="Effect" Value="{DynamicResource S2Glow}"/>
                  </Trigger>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Card" Property="Background" Value="{DynamicResource S2Card}"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);
}
