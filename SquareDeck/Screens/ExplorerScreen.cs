using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// File explorer built for the controller and touch, laid out like Handheld Explorer: a sidebar of quick-access
/// folders, pinned folders and drives (with how full they are), a top bar with back, up, a breadcrumb path,
/// filter, grid/list and menu, then the folder as big tiles or detailed rows, and a status line.
///
/// A opens, B goes back (clearing a filter or selection first), X is the actions menu, Y ticks items to work
/// on several at once, LB / RB go back and forward, LT / RT page, View switches grid and list, Menu is the
/// main menu.
/// </summary>
public sealed class ExplorerScreen : Screen
{
    private const int MaxShown = 1500;
    private const double TileW = 132, TileH = 146;

    private sealed record Entry(string Path, string Name, bool IsFolder, bool IsHidden, DateTime Modified, long Size)
    {
        public string Extension => IsFolder ? "" : System.IO.Path.GetExtension(Name);
        public string TypeText => IsFolder ? "Folder" : Extension.Length > 1 ? Extension[1..].ToUpperInvariant() + " file" : "File";
    }

    private Settings _settings => Host.Settings;
    private readonly Stack<string?> _back = new(), _forward = new();
    private readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _clipboard = new();
    private bool _clipboardIsCut;
    private string? _folder;                    // null = This PC
    private string _filter = "";
    private string? _focusPath;
    private List<Entry> _entries = new();
    private readonly List<(Entry Entry, Button Button, Image Icon, Border Check)> _shown = new();

    // Layout pieces.
    private readonly Grid _layout = new();
    private readonly Border _sidebar = new() { Width = 252, CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 14, 0), Padding = new Thickness(8, 14, 8, 8) };
    private readonly StackPanel _nav = new();
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 0) };
    private readonly ScrollViewer _crumbScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.HorizontalOnly };
    private readonly Panel _gridPanel = new WrapPanel();
    private readonly StackPanel _listPanel = new();
    private readonly ScrollViewer _files = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.VerticalOnly };
    private readonly TextBlock _empty = Ui.Text("", 18, "SubtleBrush");
    private readonly TextBlock _status = Ui.Text("", 14, "SubtleBrush");
    private readonly TextBlock _viewGlyph = new() { FontFamily = Ui.IconFont, FontSize = 18 };

    private CancellationTokenSource _loadCts = new();
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _refreshSoon = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public ExplorerScreen(string? startFolder = null)
    {
        _folder = startFolder;
        _refreshSoon.Tick += (_, _) => { _refreshSoon.Stop(); _ = LoadAsync(keepFocus: true); };

        // Sidebar
        _sidebar.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        _sidebar.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        _sidebar.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        var sideDock = new DockPanel();
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 10) };
        var brandIcon = Ui.Text("", 20, "AccentBrush");
        brandIcon.FontFamily = Ui.IconFont;
        brand.Children.Add(brandIcon);
        var brandText = Ui.Text("Explorer", 19, "FgBrush", bold: true);
        brandText.Margin = new Thickness(10, 0, 0, 0);
        brand.Children.Add(brandText);
        DockPanel.SetDock(brand, System.Windows.Controls.Dock.Top);
        sideDock.Children.Add(brand);
        sideDock.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, PanningMode = PanningMode.VerticalOnly, Content = _nav
        });
        _sidebar.Child = sideDock;

        // Top bar: sidebar, back, up, path, filter, grid/list, menu.
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = true };
        top.Children.Add(DockTo(IconButton("", "Show or hide the sidebar", ToggleSidebar), System.Windows.Controls.Dock.Left));
        top.Children.Add(DockTo(IconButton("", "Back", GoBack), System.Windows.Controls.Dock.Left));
        top.Children.Add(DockTo(IconButton("", "Up one level", GoUp), System.Windows.Controls.Dock.Left));
        top.Children.Add(DockTo(IconButton("", "Menu", () => _ = MainMenuAsync()), System.Windows.Controls.Dock.Right));
        var view = IconButton("", "Grid or list", ToggleView);
        view.Content = _viewGlyph;
        top.Children.Add(DockTo(view, System.Windows.Controls.Dock.Right));
        top.Children.Add(DockTo(IconButton("", "Filter this folder", () => _ = PromptFilterAsync()), System.Windows.Controls.Dock.Right));
        var crumbBox = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(6, 0, 6, 0), Height = 48, Child = _crumbScroll };
        crumbBox.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        _crumbScroll.Content = _crumbs;
        top.Children.Add(crumbBox);

        // Files, empty message and status.
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Visibility = Visibility.Collapsed;
        _empty.IsHitTestVisible = false;
        var filesHost = new Grid();
        filesHost.Children.Add(_files);
        filesHost.Children.Add(_empty);
        _status.Margin = new Thickness(6, 8, 0, 0);
        _status.TextTrimming = TextTrimming.CharacterEllipsis;

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(top);
        Grid.SetRow(filesHost, 1);
        content.Children.Add(filesHost);
        Grid.SetRow(_status, 2);
        content.Children.Add(_status);

        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _layout.Children.Add(_sidebar);
        Grid.SetColumn(content, 1);
        _layout.Children.Add(content);

        SetPage("Explorer", _layout,
            "{Select}  Open      {Back}  Back      {Details}  Actions      {Options}  Select      LB RB  Back / forward", wide: true);
        CompactHeader();
    }

    private static UIElement DockTo(UIElement element, System.Windows.Controls.Dock side)
    {
        DockPanel.SetDock(element, side);
        return element;
    }

    private static Button IconButton(string glyph, string tip, Action click)
    {
        var button = new Button
        {
            Style = Ui.StyleOf("PillButton"), Width = 54, Height = 48, Margin = new Thickness(0, 0, 6, 0), ToolTip = tip, Padding = new Thickness(0),
            Content = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center }
        };
        button.Click += (_, _) => click();
        return button;
    }

    public override void OnShown()
    {
        BuildSidebar();
        _ = LoadAsync(keepFocus: false);
    }

    public override void OnResumed() => BuildSidebar();

    public override void OnClosed()
    {
        _loadCts.Cancel();
        _refreshSoon.Stop();
        _watcher?.Dispose();
    }

    public override void FocusDefault()
    {
        var target = _shown.FirstOrDefault(s => string.Equals(s.Entry.Path, _focusPath, StringComparison.OrdinalIgnoreCase)).Button
                     ?? _shown.FirstOrDefault().Button;
        if (target != null) target.Focus();
        else base.FocusDefault();
    }

    // ================================================================ input

    public override bool OnAction(NavAction action)
    {
        var focused = FocusedEntry();
        switch (action)
        {
            case NavAction.X:
                if (focused != null || _checked.Count > 0) _ = ActionsAsync(focused);
                return true;
            case NavAction.Y:
                if (focused != null && _folder != null) ToggleCheck(focused);
                return true;
            case NavAction.Start:
                _ = MainMenuAsync();
                return true;
            case NavAction.View:
                ToggleView();
                return true;
            case NavAction.LB:
                GoBack();
                return true;
            case NavAction.RB:
                GoForward();
                return true;
            case NavAction.LT:
                Page(-1);
                return true;
            case NavAction.RT:
                Page(+1);
                return true;
            case NavAction.Back:
                if (_filter.Length > 0) { _filter = ""; Show(); return true; }
                if (_checked.Count > 0) { _checked.Clear(); RefreshChecks(); return true; }
                if (_back.Count > 0) { GoBack(); return true; }
                return false; // closes the page
        }
        return false;
    }

    private Entry? FocusedEntry() => _shown.FirstOrDefault(s => s.Button.IsKeyboardFocused).Entry;

    /// <summary>LT / RT: a screenful up or down.</summary>
    private void Page(int direction)
    {
        if (_shown.Count == 0) return;
        int index = Math.Max(0, _shown.FindIndex(s => s.Button.IsKeyboardFocused));
        bool grid = _settings.ExplorerView == "grid";
        double itemH = grid ? TileH + 6 : 58;
        int columns = grid ? Math.Max(1, (int)(_files.ViewportWidth / (TileW + 6))) : 1;
        int rows = Math.Max(1, (int)(_files.ViewportHeight / itemH) - 1);
        int target = Math.Clamp(index + direction * rows * columns, 0, _shown.Count - 1);
        _shown[target].Button.Focus();
        _shown[target].Button.BringIntoView();
    }

    // ================================================================ navigation

    private void NavigateTo(string? folder, bool remember = true)
    {
        if (remember && !string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase))
        {
            _back.Push(_folder);
            _forward.Clear();
        }
        _focusPath = null;
        _filter = "";
        _checked.Clear();
        _folder = folder;
        _ = LoadAsync(keepFocus: false);
    }

    private void GoBack()
    {
        if (_back.Count == 0) return;
        _forward.Push(_folder);
        string? previous = _folder;
        _folder = _back.Pop();
        _focusPath = previous; // land on the folder we came out of
        _filter = "";
        _checked.Clear();
        _ = LoadAsync(keepFocus: true);
    }

    private void GoForward()
    {
        if (_forward.Count == 0) return;
        _back.Push(_folder);
        _folder = _forward.Pop();
        _focusPath = null;
        _filter = "";
        _checked.Clear();
        _ = LoadAsync(keepFocus: false);
    }

    private void GoUp()
    {
        if (_folder == null) return;
        var parent = Directory.GetParent(_folder.TrimEnd('\\') + (_folder.EndsWith(":\\") ? "" : ""))?.FullName;
        if (_folder.Length <= 3) parent = null; // a drive's parent is This PC
        string child = _folder;
        NavigateTo(parent);
        _focusPath = child;
    }

    // ================================================================ loading

    private async Task LoadAsync(bool keepFocus)
    {
        _loadCts.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        if (keepFocus && FocusedEntry() is { } focused && _focusPath == null) _focusPath = focused.Path;

        string? folder = _folder;
        bool hidden = _settings.ExplorerShowHidden;
        List<Entry> entries;
        string? error = null;
        try
        {
            entries = await Task.Run(() => folder == null ? new List<Entry>() : Read(folder, hidden), ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            entries = new List<Entry>();
            error = ex is UnauthorizedAccessException ? "You don't have permission to open this folder." : ex.Message;
        }
        if (ct.IsCancellationRequested) return;
        _entries = entries;
        Watch(folder);
        BuildCrumbs();
        BuildSidebar();
        Show(error);
    }

    private static List<Entry> Read(string folder, bool showHidden)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
        };
        var list = new List<Entry>();
        foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
        {
            try
            {
                bool dir = (info.Attributes & FileAttributes.Directory) != 0;
                list.Add(new Entry(info.FullName, info.Name, dir, (info.Attributes & FileAttributes.Hidden) != 0, info.LastWriteTime,
                    !dir && info is FileInfo f ? f.Length : 0));
            }
            catch (Exception) { }
        }
        return list;
    }

    /// <summary>Refreshes on its own when something in the folder changes.</summary>
    private void Watch(string? folder)
    {
        _watcher?.Dispose();
        _watcher = null;
        if (folder == null) return;
        try
        {
            _watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, EnableRaisingEvents = true };
            FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(() => { _refreshSoon.Stop(); _refreshSoon.Start(); });
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Renamed += (_, _) => Dispatcher.BeginInvoke(() => { _refreshSoon.Stop(); _refreshSoon.Start(); });
        }
        catch (Exception) { _watcher = null; }
    }

    // ================================================================ layout

    private void Show(string? error = null)
    {
        bool grid = _settings.ExplorerView == "grid";
        _viewGlyph.Text = grid ? "" : ""; // shows what you'd switch to
        _gridPanel.Children.Clear();
        _listPanel.Children.Clear();
        _shown.Clear();
        _files.Content = grid ? _gridPanel : _listPanel;
        _files.ScrollToTop();

        if (_folder == null)
        {
            ShowDrives();
            UpdateStatus();
            FocusLater(_shown.FirstOrDefault().Button);
            return;
        }

        IEnumerable<Entry> items = _entries;
        if (_filter.Length > 0) items = items.Where(e => e.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        var sorted = Sort(items).ToList();
        foreach (var entry in sorted.Take(MaxShown))
        {
            var (button, icon, check) = grid ? Tile(entry) : Row(entry);
            (grid ? _gridPanel : _listPanel).Children.Add(button);
            _shown.Add((entry, button, icon, check));
        }

        _empty.Text = error ?? (_filter.Length > 0 ? $"Nothing here matches \"{_filter}\"." : "This folder is empty.");
        _empty.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (sorted.Count > MaxShown)
        {
            var more = Ui.Text($"Showing the first {MaxShown:N0} of {sorted.Count:N0}. Filter the folder to find the rest.", 14, "SubtleBrush");
            more.Margin = new Thickness(8, 12, 0, 12);
            (grid ? _gridPanel : _listPanel).Children.Add(more);
        }
        RefreshChecks();
        UpdateStatus();
        LoadIcons();
        var target = _shown.FirstOrDefault(s => string.Equals(s.Entry.Path, _focusPath, StringComparison.OrdinalIgnoreCase)).Button
                     ?? _shown.FirstOrDefault().Button;
        if (target != null) FocusLater(target);
    }

    private IEnumerable<Entry> Sort(IEnumerable<Entry> items)
    {
        bool desc = _settings.ExplorerSortDescending;
        IOrderedEnumerable<Entry> folders = items.OrderByDescending(e => e.IsFolder); // folders first, always
        folders = _settings.ExplorerSort switch
        {
            "date" => desc ? folders.ThenBy(e => e.Modified) : folders.ThenByDescending(e => e.Modified), // newest first by default
            "size" => desc ? folders.ThenBy(e => e.Size) : folders.ThenByDescending(e => e.Size),          // biggest first by default
            "type" => desc ? folders.ThenByDescending(e => e.Extension, StringComparer.OrdinalIgnoreCase) : folders.ThenBy(e => e.Extension, StringComparer.OrdinalIgnoreCase),
            _ => folders
        };
        return desc && _settings.ExplorerSort == "name"
            ? folders.ThenByDescending(e => e.Name, NaturalComparer.Instance)
            : folders.ThenBy(e => e.Name, NaturalComparer.Instance);
    }

    private (Button, Image, Border) Tile(Entry entry)
    {
        var grid = new Grid { Width = TileW - 16, Height = TileH - 16 };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var placeholder = Ui.Text(entry.IsFolder ? "" : "", 44, "SubtleBrush");
        placeholder.FontFamily = Ui.IconFont;
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(placeholder);
        var icon = new Image { Width = 72, Height = 72, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        icon.Tag = placeholder;
        grid.Children.Add(icon);
        var name = Ui.Text(entry.Name, 13, "FgBrush");
        name.TextWrapping = TextWrapping.Wrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextAlignment = TextAlignment.Center;
        name.MaxHeight = 36;
        name.Margin = new Thickness(2, 4, 2, 0);
        Grid.SetRow(name, 1);
        grid.Children.Add(name);
        var check = CheckBadge(24);
        check.HorizontalAlignment = HorizontalAlignment.Right;
        check.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(check);

        var button = Ui.Tile(grid);
        button.Width = TileW;
        button.Margin = new Thickness(0, 0, 6, 6);
        if (entry.IsHidden) grid.Opacity = 0.55;
        Hook(button, entry);
        return (button, icon, check);
    }

    private (Button, Image, Border) Row(Entry entry)
    {
        var row = new Grid { Height = 40 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        var placeholder = Ui.Text(entry.IsFolder ? "" : "", 22, "SubtleBrush");
        placeholder.FontFamily = Ui.IconFont;
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(placeholder);
        var icon = new Image { Width = 34, Height = 34, Stretch = Stretch.Uniform, Tag = placeholder };
        row.Children.Add(icon);
        var name = Ui.Text(entry.Name, 17, "FgBrush");
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var date = Ui.Text(entry.Modified.ToString("g"), 13, "SubtleBrush");
        date.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(date, 2);
        row.Children.Add(date);
        var size = Ui.Text(entry.IsFolder ? "" : SystemStats.Bytes(entry.Size), 13, "SubtleBrush");
        size.VerticalAlignment = VerticalAlignment.Center;
        size.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(size, 3);
        row.Children.Add(size);
        var check = CheckBadge(24);
        check.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(check, 4);
        row.Children.Add(check);

        var button = new Button { Style = Ui.StyleOf("RowButton"), Content = row, Padding = new Thickness(4), Margin = new Thickness(0, 2, 0, 2) };
        if (entry.IsHidden) row.Opacity = 0.55;
        Hook(button, entry);
        return (button, icon, check);
    }

    private static Border CheckBadge(double size)
    {
        var mark = Ui.Text("", size / 2, "BgBrush");
        mark.FontFamily = Ui.IconFont;
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        var badge = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Child = mark, Visibility = Visibility.Collapsed };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        return badge;
    }

    private void Hook(Button button, Entry entry)
    {
        button.Tag = entry.Path;
        button.Click += (_, _) => Open(entry);
        button.GotKeyboardFocus += (_, _) => { _focusPath = entry.Path; UpdateStatus(); };
        button.MouseRightButtonUp += (_, e) => { button.Focus(); e.Handled = true; _ = ActionsAsync(entry); };
        // Touch: press and hold for actions.
        button.PreviewTouchDown += (_, _) =>
        {
            var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            hold.Tick += (_, _) => { hold.Stop(); button.Focus(); _ = ActionsAsync(entry); };
            button.PreviewTouchUp += (_, _) => hold.Stop();
            hold.Start();
        };
    }

    /// <summary>This PC: the drives as big cards, with how full each is.</summary>
    private void ShowDrives()
    {
        _gridPanel.Children.Clear();
        _files.Content = _gridPanel;
        foreach (var drive in DriveInfo.GetDrives())
        {
            string label;
            double used = 0;
            string detail = "";
            try
            {
                if (!drive.IsReady) continue;
                label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? DriveName(drive) : drive.VolumeLabel;
                used = drive.TotalSize > 0 ? 1 - (double)drive.AvailableFreeSpace / drive.TotalSize : 0;
                detail = $"{SystemStats.Bytes(drive.AvailableFreeSpace)} free of {SystemStats.Bytes(drive.TotalSize)}";
            }
            catch (Exception) { continue; }

            var card = new StackPanel { Width = 300, Margin = new Thickness(6) };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = Ui.Text(drive.DriveType == DriveType.Removable ? "" : drive.DriveType == DriveType.Network ? "" : "", 34, "AccentBrush");
            glyph.FontFamily = Ui.IconFont;
            head.Children.Add(glyph);
            var names = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(Ui.Text($"{label} ({drive.Name.TrimEnd('\\')})", 18, "FgBrush", bold: true));
            names.Children.Add(Ui.Text(detail, 13, "SubtleBrush"));
            head.Children.Add(names);
            card.Children.Add(head);
            card.Children.Add(UsageBar(used, 300, 8, new Thickness(0, 14, 0, 2)));

            var box = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Child = card };
            box.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            var button = Ui.Tile(box);
            button.Margin = new Thickness(0, 0, 8, 8);
            var entry = new Entry(drive.RootDirectory.FullName, label, true, false, DateTime.MinValue, 0);
            button.Tag = entry.Path;
            button.Click += (_, _) => NavigateTo(entry.Path);
            button.GotKeyboardFocus += (_, _) => _focusPath = entry.Path;
            _gridPanel.Children.Add(button);
            _shown.Add((entry, button, new Image(), new Border()));
        }
    }

    private static string DriveName(DriveInfo drive) => drive.DriveType switch
    {
        DriveType.Removable => "USB drive",
        DriveType.Network => "Network drive",
        DriveType.CDRom => "Disc drive",
        _ => "Local disk"
    };

    /// <summary>A rounded bar showing how full something is; it turns red past 90%.</summary>
    private static Grid UsageBar(double fraction, double width, double height, Thickness margin)
    {
        var bar = new Grid { Width = width, Height = height, Margin = margin, HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Border { CornerRadius = new CornerRadius(height / 2), Opacity = 0.18 };
        track.SetResourceReference(Border.BackgroundProperty, "FgBrush");
        bar.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), Width = Math.Max(height, width * Math.Clamp(fraction, 0, 1)), HorizontalAlignment = HorizontalAlignment.Left };
        if (fraction > 0.9) fill.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        else fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        bar.Children.Add(fill);
        return bar;
    }

    /// <summary>Shell icons and picture thumbnails, loaded one after another in the background.</summary>
    private void LoadIcons()
    {
        var ct = _loadCts.Token;
        var work = _shown.Select(s => (s.Entry, s.Icon)).ToList();
        _ = Task.Run(async () =>
        {
            foreach (var (entry, view) in work)
            {
                if (ct.IsCancellationRequested) return;
                var image = ShellThumb.Get(entry.Path, entry.IsFolder ? 96 : 128);
                if (image == null) continue;
                if (!image.IsFrozen && image.CanFreeze) image.Freeze();
                await Dispatcher.BeginInvoke(() =>
                {
                    view.Source = image;
                    if (view.Tag is UIElement placeholder) placeholder.Visibility = Visibility.Collapsed;
                });
            }
        }, ct);
    }

    private void BuildCrumbs()
    {
        _crumbs.Children.Clear();
        void Crumb(string label, string? path, bool last)
        {
            var b = new Button
            {
                Style = Ui.StyleOf("BareButton"), Padding = new Thickness(10, 0, 10, 0), Height = 44, ToolTip = path ?? "This PC",
                Content = Ui.Text(label, 16, last ? "FgBrush" : "SubtleBrush", bold: last), Focusable = false
            };
            b.Click += (_, _) => NavigateTo(path);
            _crumbs.Children.Add(b);
            if (!last)
            {
                var sep = Ui.Text("", 10, "SubtleBrush");
                sep.FontFamily = Ui.IconFont;
                sep.VerticalAlignment = VerticalAlignment.Center;
                _crumbs.Children.Add(sep);
            }
        }

        if (_folder == null) { Crumb("This PC", null, true); return; }
        Crumb("This PC", null, false);
        var parts = new List<string>();
        for (var d = new DirectoryInfo(_folder); d != null; d = d.Parent) parts.Insert(0, d.FullName);
        for (int i = 0; i < parts.Count; i++)
        {
            string label = i == 0 ? parts[i].TrimEnd('\\') : System.IO.Path.GetFileName(parts[i].TrimEnd('\\'));
            Crumb(label, parts[i], i == parts.Count - 1);
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => _crumbScroll.ScrollToRightEnd()));
    }

    private void BuildSidebar()
    {
        _nav.Children.Clear();
        Section("QUICK ACCESS");
        Nav("", "Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Nav("", "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Nav("", "Downloads", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        Nav("", "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Nav("", "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Nav("", "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Nav("", "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

        var pins = _settings.ExplorerPins.Where(Directory.Exists).ToList();
        if (pins.Count > 0)
        {
            Section("PINNED");
            foreach (var pin in pins) Nav("", System.IO.Path.GetFileName(pin.TrimEnd('\\')) is { Length: > 0 } n ? n : pin, pin);
        }

        Section("DRIVES");
        Nav("", "This PC", null);
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                double used = drive.TotalSize > 0 ? 1 - (double)drive.AvailableFreeSpace / drive.TotalSize : 0;
                string label = (string.IsNullOrWhiteSpace(drive.VolumeLabel) ? DriveName(drive) : drive.VolumeLabel) + $" ({drive.Name.TrimEnd('\\')})";
                Nav("", label, drive.RootDirectory.FullName, used, $"{SystemStats.Bytes(drive.AvailableFreeSpace)} free of {SystemStats.Bytes(drive.TotalSize)}");
            }
            catch (Exception) { }
        }
        Ui.GroupRows(_nav);
    }

    private void Section(string text)
    {
        var header = Ui.Text(text, 12, "SubtleBrush", bold: true);
        header.Margin = new Thickness(12, _nav.Children.Count == 0 ? 0 : 16, 0, 6);
        _nav.Children.Add(header);
    }

    private void Nav(string glyph, string label, string? path, double? used = null, string? sub = null)
    {
        if (path != null && !Directory.Exists(path)) return;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = Ui.Text(glyph, 17, "FgBrush");
        icon.FontFamily = Ui.IconFont;
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text(label, 15, "FgBrush");
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(name);
        if (used is double u)
        {
            text.Children.Add(UsageBar(u, 168, 4, new Thickness(0, 6, 0, 3)));
            text.Children.Add(Ui.Text(sub ?? "", 11, "SubtleBrush"));
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var button = new Button { Style = Ui.StyleOf("RowButton"), Content = grid, FontSize = 15, Margin = new Thickness(0, 1, 0, 1) };
        bool here = string.Equals(path?.TrimEnd('\\'), _folder?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        if (here) button.Tag = "selected";
        button.Click += (_, _) => NavigateTo(path);
        if (path != null && _settings.ExplorerPins.Contains(path, StringComparer.OrdinalIgnoreCase))
            button.MouseRightButtonUp += (_, e) => { e.Handled = true; TogglePin(path); };
        _nav.Children.Add(button);
    }

    private void ToggleSidebar() => _sidebar.Visibility = _sidebar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void ToggleView()
    {
        _settings.ExplorerView = _settings.ExplorerView == "grid" ? "list" : "grid";
        _settings.Save();
        Show();
    }

    private void UpdateStatus()
    {
        var parts = new List<string>();
        if (_folder == null) parts.Add("This PC");
        else
        {
            int visible = _shown.Count;
            parts.Add(visible == 1 ? "1 item" : $"{visible:N0} items");
            if (_filter.Length > 0) parts.Add($"filtered by \"{_filter}\"");
        }
        if (_checked.Count > 0) parts.Add($"{_checked.Count} selected");
        if (_clipboard.Count > 0) parts.Add($"{(_clipboardIsCut ? "Moving" : "Copying")} {_clipboard.Count} (paste from the menu)");
        if (FocusedEntry() is { } focused && _folder != null)
            parts.Add(focused.IsFolder ? focused.Name : $"{focused.Name}  ·  {focused.TypeText}  ·  {SystemStats.Bytes(focused.Size)}");
        _status.Text = string.Join("     ·     ", parts);
    }

    // ================================================================ selecting

    private void ToggleCheck(Entry entry)
    {
        if (!_checked.Remove(entry.Path)) _checked.Add(entry.Path);
        RefreshChecks();
        UiSound.Move();
    }

    private void RefreshChecks()
    {
        foreach (var (entry, _, _, check) in _shown)
            check.Visibility = _checked.Contains(entry.Path) ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus();
    }

    /// <summary>The ticked items, or the highlighted one when nothing is ticked.</summary>
    private List<Entry> Targets(Entry? focused)
    {
        var ticked = _entries.Where(e => _checked.Contains(e.Path)).ToList();
        if (ticked.Count > 0) return ticked;
        return focused != null ? new List<Entry> { focused } : new List<Entry>();
    }

    // ================================================================ actions

    private void Open(Entry entry)
    {
        if (entry.IsFolder) { NavigateTo(entry.Path); return; }
        try { ProcessUtil.Open(entry.Path); }
        catch (Exception ex) { _ = Host.AskAsync("Couldn't open it", ex.Message, "OK"); }
    }

    private async Task ActionsAsync(Entry? focused)
    {
        if (_folder == null && focused != null)
        {
            // A drive on This PC.
            int pick = await Host.AskWithIconsAsync(focused.Name, null, ("Open", ""), ("Pin to the sidebar", ""), ("Properties", ""));
            if (pick == 0) NavigateTo(focused.Path);
            else if (pick == 1) TogglePin(focused.Path);
            else if (pick == 2) await PropertiesAsync(new List<Entry> { focused });
            return;
        }

        var targets = Targets(focused);
        if (targets.Count == 0) return;
        bool single = targets.Count == 1;
        var first = targets[0];
        string title = single ? first.Name : $"{targets.Count} items selected";
        var actions = new List<(string Label, string Glyph, Func<Task> Run)>();
        void Add(string label, string glyph, Action run) => actions.Add((label, glyph, () => { run(); return Task.CompletedTask; }));

        if (single) Add("Open", "", () => Open(first));
        if (single && !first.IsFolder)
            Add("Open with…", "", () => ProcessUtil.Open("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + first.Path));
        Add(_checked.Contains(first.Path) && single ? "Unselect" : "Select", "", () => { foreach (var t in targets) ToggleCheck(t); });
        Add("Copy", "", () => SetClipboard(targets, cut: false));
        Add("Cut (to move)", "", () => SetClipboard(targets, cut: true));
        if (_clipboard.Count > 0 && single && first.IsFolder)
            actions.Add(($"Paste into \"{first.Name}\"", "", () => PasteAsync(first.Path)));
        if (single) actions.Add(("Rename", "", () => RenameAsync(first)));
        actions.Add(("Delete (to the Recycle Bin)", "", () => DeleteAsync(targets)));
        actions.Add(("Compress to a ZIP file", "", () => RunAsync("Compressing…", () => FileOps.Zip(targets.Select(t => t.Path).ToList(), _folder!))));
        if (single && first.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            actions.Add(("Extract here", "", () => RunAsync("Extracting…", () => FileOps.Unzip(first.Path))));
        if (single && first.IsFolder)
            Add(IsPinned(first.Path) ? "Unpin from the sidebar" : "Pin to the sidebar", "", () => TogglePin(first.Path));
        if (single) Add("Copy the path", "", () => CopyPath(first.Path));
        if (single) Add("Show in Windows Explorer", "", () => ProcessUtil.Open("explorer.exe", "/select,\"" + first.Path + "\""));
        actions.Add(("Properties", "", () => PropertiesAsync(targets)));

        int choice = await Host.AskWithIconsAsync(title, single ? first.TypeText : null, actions.Select(a => (a.Label, a.Glyph)).ToArray());
        if (choice >= 0) await actions[choice].Run();
    }

    private async Task MainMenuAsync()
    {
        var actions = new List<(string Label, string Glyph, Func<Task> Run)>();
        void Add(string label, string glyph, Action run) => actions.Add((label, glyph, () => { run(); return Task.CompletedTask; }));
        if (_folder != null)
        {
            actions.Add(("New folder", "", NewFolderAsync));
            if (_clipboard.Count > 0) actions.Add(($"Paste {_clipboard.Count} item{(_clipboard.Count == 1 ? "" : "s")} here", "", () => PasteAsync(_folder)));
            Add("Select everything", "", () => { foreach (var s in _shown) _checked.Add(s.Entry.Path); RefreshChecks(); });
            if (_checked.Count > 0) Add("Clear the selection", "", () => { _checked.Clear(); RefreshChecks(); });
        }
        actions.Add(("Filter this folder", "", PromptFilterAsync));
        actions.Add(($"Sort by: {SortLabel()}", "", SortMenuAsync));
        Add(_settings.ExplorerView == "grid" ? "Show as a list" : "Show as a grid", _settings.ExplorerView == "grid" ? "" : "", ToggleView);
        Add(_settings.ExplorerShowHidden ? "Hide hidden files" : "Show hidden files", "", () =>
        {
            _settings.ExplorerShowHidden = !_settings.ExplorerShowHidden;
            _settings.Save();
            _ = LoadAsync(keepFocus: true);
        });
        Add(_sidebar.Visibility == Visibility.Visible ? "Hide the sidebar" : "Show the sidebar", "", ToggleSidebar);
        if (_folder != null)
        {
            Add(IsPinned(_folder) ? "Unpin this folder" : "Pin this folder to the sidebar", "", () => TogglePin(_folder));
            Add("Open this folder in Windows Explorer", "", () => ProcessUtil.Open("explorer.exe", "\"" + _folder + "\""));
        }
        actions.Add(("Controls", "", ControlsAsync));

        int choice = await Host.AskWithIconsAsync("Explorer", _folder ?? "This PC", actions.Select(a => (a.Label, a.Glyph)).ToArray());
        if (choice >= 0) await actions[choice].Run();
    }

    private string SortLabel() => (_settings.ExplorerSort switch { "date" => "Date", "size" => "Size", "type" => "Type", _ => "Name" })
                                  + (_settings.ExplorerSortDescending ? " (reversed)" : "");

    private async Task SortMenuAsync()
    {
        var options = new[] { ("name", "Name (A to Z)"), ("date", "Date (newest first)"), ("size", "Size (biggest first)"), ("type", "Type") };
        var labels = options.Select(o => (o.Item1 == _settings.ExplorerSort ? "✓  " : "") + o.Item2).ToList();
        labels.Add(_settings.ExplorerSortDescending ? "Normal order" : "Reverse the order");
        int pick = await Host.AskAsync("Sort by", null, labels.ToArray());
        if (pick < 0) return;
        if (pick < options.Length) _settings.ExplorerSort = options[pick].Item1;
        else _settings.ExplorerSortDescending = !_settings.ExplorerSortDescending;
        _settings.Save();
        Show();
    }

    private async Task ControlsAsync() => await Host.AskAsync("Explorer controls",
        "A  Open\nB  Back (clears a filter or selection first)\nX  Actions for the item or the selection\nY  Select / unselect (to work on several)\n" +
        "LB / RB  Back / forward\nLT / RT  Page up / down\nView  Grid or list\nMenu  Main menu\n\nTouch: tap to open, press and hold for actions.\nMouse: right-click for actions.", "OK");

    private async Task PromptFilterAsync()
    {
        var text = await Host.PromptAsync("Filter this folder", "Show only names containing:", _filter, "Filter");
        if (text == null) return;
        _filter = text.Trim();
        Show();
    }

    private void SetClipboard(List<Entry> targets, bool cut)
    {
        _clipboard.Clear();
        _clipboard.AddRange(targets.Select(t => t.Path));
        _clipboardIsCut = cut;
        _checked.Clear();
        RefreshChecks();
        Host.ShowToast(cut ? "Ready to move" : "Copied", targets.Count == 1 ? targets[0].Name : $"{targets.Count} items",
            "Open the folder to put them in, then choose Paste from the menu.", "", "info");
    }

    private async Task PasteAsync(string destination)
    {
        if (_clipboard.Count == 0) return;
        var sources = _clipboard.ToList();
        bool move = _clipboardIsCut;
        await RunAsync(move ? "Moving…" : "Copying…", () => FileOps.Transfer(sources, destination, move, CancellationToken.None));
        if (move) _clipboard.Clear();
        UpdateStatus();
    }

    private async Task NewFolderAsync()
    {
        if (_folder == null) return;
        var name = await Host.PromptAsync("New folder", "Name for the folder", "New folder", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!ValidName(name.Trim())) { await Host.AskAsync("That name can't be used", "Names can't contain \\ / : * ? \" < > |", "OK"); return; }
        try
        {
            var path = FileOps.UniquePath(_folder, name.Trim());
            Directory.CreateDirectory(path);
            _focusPath = path;
        }
        catch (Exception ex) { await Host.AskAsync("Couldn't create the folder", ex.Message, "OK"); }
        await LoadAsync(keepFocus: false);
    }

    private static bool ValidName(string name) => name.Length > 0 && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0 && name is not ("." or "..");

    private async Task RenameAsync(Entry entry)
    {
        var name = await Host.PromptAsync("Rename", null, entry.Name, "Rename");
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == entry.Name) return;
        if (!ValidName(name.Trim())) { await Host.AskAsync("That name can't be used", "Names can't contain \\ / : * ? \" < > |", "OK"); return; }
        try { _focusPath = FileOps.Rename(entry.Path, name.Trim()); }
        catch (Exception ex) { await Host.AskAsync("Couldn't rename it", ex.Message, "OK"); }
        await LoadAsync(keepFocus: false);
    }

    private async Task DeleteAsync(List<Entry> targets)
    {
        string what = targets.Count == 1 ? targets[0].Name : targets.Count + " items";
        if (await Host.AskAsync("Delete " + what + "?", "It goes to the Recycle Bin, so you can get it back.", "Cancel", "Delete") != 1) return;
        await RunAsync("Deleting…", () => FileOps.Recycle(targets.Select(t => t.Path).ToList()));
    }

    /// <summary>Runs a file operation behind a "working" pop-up and reports anything that went wrong.</summary>
    private async Task RunAsync(string message, Action work)
    {
        try { await Host.RunBusyAsync(message, () => Task.Run(work)); }
        catch (Exception ex) { await Host.AskAsync("That didn't work", ex.Message, "OK"); }
        _checked.Clear();
        await LoadAsync(keepFocus: true);
    }

    private bool IsPinned(string path) => _settings.ExplorerPins.Contains(path, StringComparer.OrdinalIgnoreCase);

    private void TogglePin(string path)
    {
        if (IsPinned(path)) _settings.ExplorerPins.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        else _settings.ExplorerPins.Add(path);
        _settings.Save();
        BuildSidebar();
    }

    private void CopyPath(string path)
    {
        try
        {
            Clipboard.SetText(path);
            Host.ShowToast("Copied", "Path copied", path, "", "info");
        }
        catch (Exception) { }
    }

    private async Task PropertiesAsync(List<Entry> targets)
    {
        string title = targets.Count == 1 ? targets[0].Name : $"{targets.Count} items";
        string details;
        try
        {
            var (files, folders, bytes) = await Task.Run(() =>
            {
                long total = 0; int f = 0, d = 0;
                foreach (var t in targets)
                {
                    if (!t.IsFolder) { f++; total += t.Size; continue; }
                    d++;
                    try
                    {
                        foreach (var file in new DirectoryInfo(t.Path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
                        {
                            total += file.Length;
                            if (++f >= 300000) break;
                        }
                    }
                    catch (Exception) { }
                }
                return (f, d, total);
            });
            if (targets.Count == 1)
            {
                var t = targets[0];
                details = t.IsFolder
                    ? $"Folder\n{t.Path}\n\nContains {files:N0} files, {SystemStats.Bytes(bytes)}\nModified {t.Modified:f}"
                    : $"{t.TypeText}\n{t.Path}\n\nSize {SystemStats.Bytes(t.Size)} ({t.Size:N0} bytes)\nCreated {File.GetCreationTime(t.Path):f}\nModified {t.Modified:f}";
                if (_folder == null && t.IsFolder)
                {
                    var drive = new DriveInfo(t.Path);
                    details = $"{drive.DriveFormat} drive\n{t.Path}\n\n{SystemStats.Bytes(drive.AvailableFreeSpace)} free of {SystemStats.Bytes(drive.TotalSize)}";
                }
            }
            else details = $"{folders:N0} folders and {files:N0} files\n{SystemStats.Bytes(bytes)} in total";
        }
        catch (Exception ex) { details = ex.Message; }
        await Host.AskAsync(title, details, "OK");
    }
}

/// <summary>Sorts "file 2" before "file 10", like Windows Explorer.</summary>
internal sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string a, string b);

    public int Compare(string? x, string? y)
    {
        try { return StrCmpLogicalW(x ?? "", y ?? ""); }
        catch (Exception) { return string.Compare(x, y, StringComparison.OrdinalIgnoreCase); }
    }
}
