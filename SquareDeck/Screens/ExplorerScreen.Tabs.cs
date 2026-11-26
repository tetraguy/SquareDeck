using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

// Explorer's tabs (like Windows 11's: one folder each, with its own back and forward, dragged files can be dropped on
// another tab), the sidebar as a blade that L1 or F1 folds away, and the Recycle Bin.
public sealed partial class ExplorerScreen
{
    private sealed class FolderTab
    {
        public string? Folder;
        public Stack<string?> Back = new(), Forward = new();
        public string? FocusPath;
        public string Filter = "";
        public Button? Header;
    }

    private const double SidebarWidth = 252;
    private const int MaxTabs = 8;
    private readonly List<FolderTab> _tabs = new();
    private int _tabIndex;
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal };
    private bool _sidebarOpen = true;
    private readonly DispatcherTimer _dragHover = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private FolderTab? _hoverTab;
    private Dictionary<string, RecycledItem> _recycled = new(StringComparer.OrdinalIgnoreCase);

    private FolderTab CurrentTab => _tabs[_tabIndex];

    /// <summary>The strip of tabs above the top bar, with + for a new one.</summary>
    private UIElement BuildTabArea(string? startFolder)
    {
        _tabs.Add(new FolderTab { Folder = startFolder, Back = _back, Forward = _forward });
        _dragHover.Tick += (_, _) =>
        {
            _dragHover.Stop();
            if (_hoverTab != null && _tabs.IndexOf(_hoverTab) is int i and >= 0 && i != _tabIndex) SwitchTab(i);
        };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, PanningMode = PanningMode.HorizontalOnly, Content = _tabStrip, Margin = new Thickness(0, 0, 0, 8)
        };
        return scroll;
    }

    private void RenderTabs()
    {
        _tabStrip.Children.Clear();
        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            int index = i;
            bool current = i == _tabIndex;
            var row = new DockPanel { Margin = new Thickness(12, 0, 6, 0) };
            var glyph = Ui.Text(RecycleBin.Is(tab.Folder) ? "" : tab.Folder == null ? "" : "", 14, current ? "AccentBrush" : "SubtleBrush");
            glyph.FontFamily = Ui.IconFont;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(glyph, Dock.Left);
            row.Children.Add(glyph);
            if (_tabs.Count > 1)
            {
                var close = new Button
                {
                    Style = Ui.StyleOf("BareButton"), Focusable = false, Width = 26, Height = 26, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Close tab",
                    Content = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                ((TextBlock)close.Content).SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
                close.Click += (_, e) => { e.Handled = true; CloseTab(index); };
                DockPanel.SetDock(close, Dock.Right);
                row.Children.Add(close);
            }
            var name = Ui.Text(TabName(tab.Folder), 14, current ? "FgBrush" : "SubtleBrush", bold: current);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxWidth = 150;
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);

            var face = new Border { CornerRadius = new CornerRadius(10, 10, current ? 0 : 10, current ? 0 : 10), Height = 40, Child = row, BorderThickness = new Thickness(0, 0, 0, current ? 3 : 0) };
            face.SetResourceReference(Border.BackgroundProperty, current ? "CircleBrush" : "TileBrush");
            face.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            if (!current) face.Opacity = 0.8;
            var header = new Button { Style = Ui.StyleOf("BareButton"), Focusable = true, Content = face, Margin = new Thickness(0, 0, 6, 0), ToolTip = tab.Folder ?? "This PC", AllowDrop = true };
            // The left stick reaches the tabs too (up from the top bar); A opens the highlighted one, X closes it.
            var rest = face.BorderThickness;
            header.GotKeyboardFocus += (_, _) => { face.BorderThickness = new Thickness(2, 2, 2, current ? 3 : 2); face.Opacity = 1; };
            header.LostKeyboardFocus += (_, _) => { face.BorderThickness = rest; if (!current) face.Opacity = 0.8; };
            header.Click += (_, _) => { if (index == _tabIndex) FocusDefault(); else SwitchTab(index); };
            header.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle && _tabs.Count > 1) CloseTab(index); };
            // Dragged files: hovering a tab opens it; dropping on it moves (or copies) them into its folder.
            header.DragEnter += (_, e) => { _hoverTab = tab; _dragHover.Stop(); _dragHover.Start(); DragFeedback(e, tab.Folder); };
            header.DragOver += (_, e) => DragFeedback(e, tab.Folder);
            header.DragLeave += (_, _) => { if (_hoverTab == tab) { _dragHover.Stop(); _hoverTab = null; } };
            header.Drop += (_, e) => { _dragHover.Stop(); _hoverTab = null; _ = DropAsync(e, tab.Folder); };
            tab.Header = header;
            _tabStrip.Children.Add(header);
        }
        if (_tabs.Count < MaxTabs)
        {
            var add = new Button
            {
                Style = Ui.StyleOf("BareButton"), Focusable = true, Width = 40, Height = 40, ToolTip = "New tab (Ctrl+T)",
                Content = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            ((TextBlock)add.Content).SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
            add.Click += (_, _) => NewTab(_folder);
            var plus = (TextBlock)add.Content;
            add.GotKeyboardFocus += (_, _) => { plus.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); plus.FontSize = 18; };
            add.LostKeyboardFocus += (_, _) => { plus.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush"); plus.FontSize = 15; };
            _tabStrip.Children.Add(add);
        }
        _tabs[_tabIndex].Header?.BringIntoView();
    }

    private static string TabName(string? folder)
    {
        if (folder == null) return "This PC";
        if (RecycleBin.Is(folder)) return "Recycle Bin";
        if (NetworkPlaces.IsFtp(folder)) return Uri.TryCreate(folder, UriKind.Absolute, out var u) ? (u.Segments.Length > 1 ? Uri.UnescapeDataString(u.Segments[^1].TrimEnd('/')) : u.Host) : folder;
        string name = Path.GetFileName(folder.TrimEnd('\\'));
        return name.Length > 0 ? name : folder.TrimEnd('\\');
    }

    /// <summary>Keeps the current tab's place (folder, highlight, filter) before leaving it.</summary>
    private void SaveTab()
    {
        var t = CurrentTab;
        t.Folder = _folder;
        t.FocusPath = FocusedEntry()?.Path ?? _focusPath;
        t.Filter = _filter;
        t.Back = _back;
        t.Forward = _forward;
    }

    private void SwitchTab(int index)
    {
        if (index < 0 || index >= _tabs.Count || index == _tabIndex) return;
        SaveTab();
        _tabIndex = index;
        var t = CurrentTab;
        _back = t.Back;
        _forward = t.Forward;
        _folder = t.Folder;
        _focusPath = t.FocusPath;
        _filter = t.Filter;
        _checked.Clear();
        UiSound.Page();
        RenderTabs();
        _ = LoadAsync(keepFocus: false);
    }

    private void NewTab(string? folder)
    {
        if (_tabs.Count >= MaxTabs) { Host.ShowToast("Explorer", "That's the most tabs", $"Close one to open another (up to {MaxTabs}).", "", "info"); return; }
        SaveTab();
        _tabs.Insert(_tabIndex + 1, new FolderTab { Folder = folder });
        int index = _tabIndex + 1;
        var t = _tabs[index];
        _tabIndex = index;
        _back = t.Back;
        _forward = t.Forward;
        _folder = t.Folder;
        _focusPath = null;
        _filter = "";
        _checked.Clear();
        UiSound.Page();
        RenderTabs();
        _ = LoadAsync(keepFocus: false);
    }

    private void CloseTab(int index)
    {
        if (_tabs.Count <= 1 || index < 0 || index >= _tabs.Count) return;
        if (index == _tabIndex)
        {
            _tabs.RemoveAt(index);
            _tabIndex = Math.Min(index, _tabs.Count - 1);
            var t = CurrentTab;
            _back = t.Back;
            _forward = t.Forward;
            _folder = t.Folder;
            _focusPath = t.FocusPath;
            _filter = t.Filter;
            _checked.Clear();
            RenderTabs();
            _ = LoadAsync(keepFocus: false);
        }
        else
        {
            SaveTab();
            _tabs.RemoveAt(index);
            if (index < _tabIndex) _tabIndex--;
            RenderTabs();
        }
        UiSound.Back();
    }

    /// <summary>The tab whose header has the controller's highlight, if any.</summary>
    private int FocusedTabIndex() => _tabs.FindIndex(t => t.Header?.IsKeyboardFocused == true);

    private void NextTab(int step)
    {
        if (_tabs.Count < 2) return;
        SwitchTab(((_tabIndex + step) % _tabs.Count + _tabs.Count) % _tabs.Count);
    }

    /// <summary>F1 folds the sidebar, Ctrl+T / Ctrl+W open and close tabs, Ctrl+Tab moves between them.</summary>
    private void OnExplorerKeys(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (e.Key)
        {
            case Key.F1: ToggleSidebar(); break;
            case Key.T when ctrl: NewTab(_folder); break;
            case Key.W when ctrl: CloseTab(_tabIndex); break;
            case Key.Tab when ctrl: NextTab(shift ? -1 : 1); break;
            default: return;
        }
        e.Handled = true;
    }

    // ---------------------------------------------------------------- the sidebar blade

    private void ToggleSidebar() => SetSidebar(!_sidebarOpen, animate: true);

    private void SetSidebar(bool open, bool animate)
    {
        _sidebarOpen = open;
        if (_settings.ExplorerSidebarOpen != open) { _settings.ExplorerSidebarOpen = open; _settings.Save(); }
        if (!open && _sidebar.IsKeyboardFocusWithin) FocusDefault();
        var duration = TimeSpan.FromMilliseconds(animate && !Theme.ReduceMotion ? 220 : 0);
        var ease = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };
        if (open) _sidebar.Visibility = Visibility.Visible;
        var width = new DoubleAnimation(open ? SidebarWidth : 0, duration) { EasingFunction = ease };
        if (!open) width.Completed += (_, _) => { if (!_sidebarOpen) _sidebar.Visibility = Visibility.Collapsed; };
        _sidebar.BeginAnimation(WidthProperty, width);
        _sidebar.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0, 0, open ? 14 : 0, 0), duration) { EasingFunction = ease });
        _sidebar.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, duration));
    }

    // ---------------------------------------------------------------- dragging files

    /// <summary>Hooks a file's tile or row so it can be dragged (with the mouse or a finger) onto a tab or a folder.</summary>
    private void HookDrag(Button button, Entry entry)
    {
        if (RecycleBin.Is(_folder) || NetworkPlaces.IsFtp(entry.Path) || entry.Path.StartsWith("action:") || _folder == null) return;
        Point? down = null;
        button.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(button);
        button.PreviewMouseLeftButtonUp += (_, _) => down = null;
        button.PreviewMouseMove += (_, e) =>
        {
            if (down is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
            var now = e.GetPosition(button);
            if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance * 3 && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance * 3) return;
            down = null;
            var paths = (_checked.Contains(entry.Path) ? _entries.Where(x => _checked.Contains(x.Path)).Select(x => x.Path) : new[] { entry.Path }).ToArray();
            var data = new DataObject(DataFormats.FileDrop, paths);
            try { DragDrop.DoDragDrop(button, data, DragDropEffects.Copy | DragDropEffects.Move); }
            catch (Exception) { }
        };
        if (entry.IsFolder)
        {
            // Dropping on a folder puts things inside it.
            button.AllowDrop = true;
            button.DragOver += (_, e) => { DragFeedback(e, entry.Path); e.Handled = true; };
            button.Drop += (_, e) => { e.Handled = true; _ = DropAsync(e, entry.Path); };
        }
    }

    private static bool CanReceive(string? folder) => folder != null && !RecycleBin.Is(folder) && !NetworkPlaces.IsFtp(folder);

    private static DragDropEffects EffectFor(DragEventArgs e, string folder)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return DragDropEffects.None;
        if (paths.Any(p => string.Equals(Path.GetDirectoryName(p.TrimEnd('\\')), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                           folder.TrimEnd('\\').StartsWith(p.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(p.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            return DragDropEffects.None; // already there, or into itself
        if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0) return DragDropEffects.Copy;
        if ((e.KeyStates & DragDropKeyStates.ShiftKey) != 0) return DragDropEffects.Move;
        // Like Windows: on the same drive it moves, onto another drive it copies.
        bool sameDrive = string.Equals(Path.GetPathRoot(paths[0]), Path.GetPathRoot(folder), StringComparison.OrdinalIgnoreCase);
        return sameDrive ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    private static void DragFeedback(DragEventArgs e, string? folder)
    {
        e.Effects = CanReceive(folder) ? EffectFor(e, folder!) : DragDropEffects.None;
        e.Handled = true;
    }

    private async Task DropAsync(DragEventArgs e, string? folder)
    {
        if (!CanReceive(folder) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var effect = EffectFor(e, folder!);
        if (effect == DragDropEffects.None) return;
        bool move = effect == DragDropEffects.Move;
        string what = paths.Length == 1 ? Path.GetFileName(paths[0].TrimEnd('\\')) : $"{paths.Length} items";
        await RunAsync(move ? "Moving…" : "Copying…", () => FileOps.Transfer(paths, folder!, move, CancellationToken.None));
        Host.ShowToast(move ? "Moved" : "Copied", what, "To " + TabName(folder), "", "info");
    }

    /// <summary>The files area takes drops too (into the folder that's open), so a tab can be opened by hovering and then dropped into.</summary>
    private void HookFilesDrop()
    {
        _files.AllowDrop = true;
        _files.DragOver += (_, e) => { if (!e.Handled) DragFeedback(e, _folder); };
        _files.Drop += (_, e) => { if (!e.Handled) _ = DropAsync(e, _folder); };
    }

    /// <summary>X on a file with more than one tab open: send it to another tab's folder (the controller's drag and drop).</summary>
    private void AddTabActions(List<(string Label, string Glyph, Func<Task> Run)> actions, List<Entry> targets)
    {
        if (_tabs.Count < 2 || !CanReceive(_folder) || targets.Any(t => NetworkPlaces.IsFtp(t.Path))) return;
        SaveTab();
        foreach (var other in _tabs.Where(t => t != CurrentTab && CanReceive(t.Folder)))
        {
            string folder = other.Folder!;
            var paths = targets.Select(t => t.Path).ToList();
            actions.Add(($"Move to the \"{TabName(folder)}\" tab", "", () => RunAsync("Moving…", () => FileOps.Transfer(paths, folder, true, CancellationToken.None))));
            actions.Add(($"Copy to the \"{TabName(folder)}\" tab", "", () => RunAsync("Copying…", () => FileOps.Transfer(paths, folder, false, CancellationToken.None))));
        }
    }

    // ---------------------------------------------------------------- the Recycle Bin

    private List<Entry> ReadRecycleBin()
    {
        var items = RecycleBin.List();
        var map = new Dictionary<string, RecycledItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items) map[i.Path] = i;
        Dispatcher.BeginInvoke(() => _recycled = map);
        return items.Select(i => new Entry(i.Path, i.Name, i.IsFolder, false, i.Deleted, i.Size)).ToList();
    }

    private string RecycleStatus(Entry entry) =>
        _recycled.TryGetValue(entry.Path, out var item)
            ? $"{entry.Name}  ·  from {(item.OriginalFolder.Length > 0 ? item.OriginalFolder : "?")}  ·  deleted {entry.Modified:MMM d, h:mm tt}" + (entry.IsFolder ? "" : "  ·  " + SystemStats.Bytes(entry.Size))
            : entry.Name;

    private async Task RecycleActionsAsync(Entry? focused)
    {
        var targets = Targets(focused);
        var actions = new List<(string Label, string Glyph, Func<Task> Run)>();
        if (targets.Count > 0)
        {
            var paths = targets.Select(t => t.Path).ToList();
            string what = targets.Count == 1 ? targets[0].Name : $"{targets.Count} items";
            actions.Add(("Restore", "", () => RunAsync("Restoring…", () => RecycleBin.Restore(paths))));
            actions.Add(("Delete for good", "", async () =>
            {
                if (await Host.AskAsync("Delete " + what + " for good?", "This can't be undone.", "Cancel", "Delete") != 1) return;
                await RunAsync("Deleting…", () => RecycleBin.DeleteForever(paths));
            }));
            actions.Add((_checked.Contains(targets[0].Path) && targets.Count == 1 ? "Unselect" : "Select", "", () => { foreach (var t in targets) ToggleCheck(t); return Task.CompletedTask; }));
        }
        AddRecycleMenu(actions);
        string title = targets.Count == 1 ? targets[0].Name : targets.Count > 1 ? $"{targets.Count} items selected" : "Recycle Bin";
        int choice = await Host.AskWithIconsAsync(title, targets.Count == 1 ? RecycleStatus(targets[0]) : null, actions.Select(a => (a.Label, a.Glyph)).ToArray());
        if (choice >= 0) await actions[choice].Run();
    }

    private void AddRecycleMenu(List<(string Label, string Glyph, Func<Task> Run)> actions)
    {
        if (_entries.Count == 0) return;
        var all = _entries.Select(e => e.Path).ToList();
        actions.Add(("Restore everything", "", () => RunAsync("Restoring…", () => RecycleBin.Restore(all))));
        actions.Add(("Empty the Recycle Bin", "", async () =>
        {
            if (await Host.AskAsync("Empty the Recycle Bin?", $"{_entries.Count:N0} items are deleted for good. This can't be undone.", "Cancel", "Empty") != 1) return;
            await RunAsync("Emptying…", RecycleBin.Empty);
        }));
    }
}
