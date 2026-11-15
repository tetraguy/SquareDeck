using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>A news post or patch notes, shown as plain text so it can be read without a browser.</summary>
public sealed class NewsReaderScreen : ListScreen
{
    private readonly NewsItem _post;

    public NewsReaderScreen(NewsItem post) : base(post.Title, "Right stick or Up / Down  Scroll        {Back}  Back")
    {
        _post = post;
    }

    protected override void Build()
    {
        AddNote((_post.Feed.Length > 0 ? _post.Feed + "  ·  " : "") + _post.Date.ToString("MMMM d, yyyy"));
        AddRow("Open the original post (pictures and videos)", glyph: "", click: () => ProcessUtil.Open(_post.Url));

        var body = _post.Body.Length > 0 ? _post.Body : _post.Snippet;
        foreach (var paragraph in body.Split('\n'))
        {
            var text = paragraph.Trim();
            if (text.Length == 0) continue;
            var block = Ui.Text(text, 19);
            block.TextWrapping = TextWrapping.Wrap;
            block.TextTrimming = TextTrimming.None;
            block.Margin = new Thickness(4, 6, 12, 6);
            Rows.Children.Add(block);
        }
    }
}

/// <summary>
/// A file explorer for the controller: places and drives on the left, the folder on the right.
/// A opens, B goes up, X is the action menu, Y selects several items, LT / RT page.
/// </summary>
public sealed class ExplorerScreen : Screen
{
    private const int MaxEntries = 800;

    private readonly StackPanel _sidebar = new() { Width = 236 };
    private readonly StackPanel _rows = new();
    private readonly TextBlock _pathText = Ui.Text("", 17, "SubtleBrush");
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _clipboard = new();
    private bool _clipboardIsCut;
    private string? _folder;          // null: "This PC" (drives)
    private string _sort = "name";    // name | date | size
    private bool _showHidden;
    private string? _focusPath;
    private CancellationTokenSource _iconCts = new();

    public ExplorerScreen()
    {
        static ScrollViewer Scroller(UIElement content) => new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
            Focusable = false,
            Content = content
        };

        var right = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
        _pathText.Margin = new Thickness(4, 0, 0, 8);
        right.Children.Add(_pathText);
        right.Children.Add(_rows);

        var sidePanel = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 4, 8), Child = Scroller(_sidebar) };
        sidePanel.SetResourceReference(Border.BackgroundProperty, "TileBrush");

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(sidePanel);
        var rightScroll = Scroller(right);
        Grid.SetColumn(rightScroll, 1);
        layout.Children.Add(rightScroll);

        SetPage("Explorer", layout,
            "{Select}  Open      {Details}  Actions      {Options}  Select      LT / RT  Page      {Back}  Up", wide: true);
    }

    public override void OnShown()
    {
        _folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Rebuild();
    }

    public override void OnResumed() => Rebuild();

    public override void OnClosed() => _iconCts.Cancel();

    // ---------------------------------------------------------------- listing

    private void GoTo(string? folder)
    {
        _folder = folder;
        _selected.Clear();
        _focusPath = null;
        Rebuild();
    }

    private void Rebuild()
    {
        _iconCts.Cancel();
        _iconCts = new CancellationTokenSource();
        BuildSidebar();
        _rows.Children.Clear();
        _pathText.Text = _folder ?? "This PC";

        // Things you can do in this folder, kept at the top so they are always one press away.
        if (_folder != null)
        {
            AddRow("Up a level", null, "", null, () => GoTo(System.IO.Path.GetDirectoryName(_folder)));
            if (_clipboard.Count > 0)
                AddRow($"Paste {_clipboard.Count} {(_clipboard.Count == 1 ? "item" : "items")} here", _clipboardIsCut ? "Move" : "Copy", "", null, Paste);
            AddRow("New folder", null, "", null, NewFolder);
            AddRow("Sort", _sort switch { "date" => "Newest first", "size" => "Largest first", _ => "Name" }, "", null, () =>
            {
                _sort = _sort switch { "name" => "date", "date" => "size", _ => "name" };
                Rebuild();
            });
            AddRow("Hidden files", _showHidden ? "Shown" : "Not shown", "", null, () => { _showHidden = !_showHidden; Rebuild(); });
        }

        var pending = new List<(Image View, string Path)>();
        try
        {
            if (_folder == null)
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var root = drive.RootDirectory.FullName;
                    string name = drive.VolumeLabel.Length > 0 ? $"{drive.Name.TrimEnd('\\')}  {drive.VolumeLabel}" : drive.Name.TrimEnd('\\');
                    AddRow(name, $"{SystemStats.Bytes(drive.AvailableFreeSpace)} free of {SystemStats.Bytes(drive.TotalSize)}", "", null, () => GoTo(root));
                }
            }
            else
            {
                var info = new DirectoryInfo(_folder);
                bool Shown(FileSystemInfo i) => _showHidden || (i.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;

                IEnumerable<DirectoryInfo> folders = info.EnumerateDirectories().Where(Shown);
                IEnumerable<FileInfo> files = info.EnumerateFiles().Where(Shown);
                folders = _sort == "date" ? folders.OrderByDescending(d => d.LastWriteTime) : folders.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase);
                files = _sort switch
                {
                    "date" => files.OrderByDescending(f => f.LastWriteTime),
                    "size" => files.OrderByDescending(f => f.Length),
                    _ => files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                };

                int shown = 0;
                foreach (var d in folders)
                {
                    if (shown++ >= MaxEntries) break;
                    var path = d.FullName;
                    AddRow(Mark(path) + d.Name, "Folder", "", path, () => GoTo(path));
                }
                foreach (var f in files)
                {
                    if (shown++ >= MaxEntries) break;
                    var path = f.FullName;
                    var icon = new Image();
                    AddRow(Mark(path) + f.Name, $"{SystemStats.Bytes(f.Length)}   {f.LastWriteTime:MMM d, yyyy}", null, path, () => Open(path), icon);
                    pending.Add((icon, path));
                }
                if (shown == 0) AddNote("This folder is empty.");
                if (shown >= MaxEntries) AddNote($"Only the first {MaxEntries} items are listed.");
            }
        }
        catch (Exception)
        {
            AddNote("This folder can't be opened.");
        }

        // Icons and thumbnails come from Windows in the background so big folders appear straight away.
        var ct = _iconCts.Token;
        Task.Run(() =>
        {
            foreach (var (view, path) in pending.Take(400))
            {
                if (ct.IsCancellationRequested) return;
                var source = ShellThumb.Get(path, 48);
                if (source != null) Dispatcher.BeginInvoke(new Action(() => view.Source = source));
            }
        }, ct);

        Ui.GroupRows(_rows);
        Ui.GroupRows(_sidebar);
        var target = _rows.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == _focusPath && _focusPath != null)
                     ?? _rows.Children.OfType<Button>().FirstOrDefault(b => b.Tag is string)
                     ?? _rows.Children.OfType<Button>().FirstOrDefault();
        FocusLater(target);
    }

    private string Mark(string path) => _selected.Contains(path) ? "✓  " : "";

    private void AddRow(string label, string? value, string? glyph, string? path, Action click, Image? icon = null)
    {
        var row = Ui.Row(label, value, glyph, icon);
        row.Tag = path; // null for the action rows at the top
        row.Click += (_, _) => click();
        if (path != null) row.GotKeyboardFocus += (_, _) => _focusPath = path;
        _rows.Children.Add(row);
    }

    private void AddNote(string text)
    {
        var note = Ui.Text(text, 17, "SubtleBrush");
        note.Margin = new Thickness(4, 10, 0, 10);
        _rows.Children.Add(note);
    }

    private void BuildSidebar()
    {
        _sidebar.Children.Clear();
        void Place(string name, string? path, string glyph)
        {
            if (path != null && !Directory.Exists(path)) return;
            var row = Ui.Row(name, glyph: glyph);
            row.Margin = new Thickness(0, 2, 6, 2);
            row.FontSize = 17;
            if (string.Equals(path, _folder, StringComparison.OrdinalIgnoreCase)) row.Tag = "selected";
            row.Click += (_, _) => GoTo(path);
            _sidebar.Children.Add(row);
        }

        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _sidebar.Children.Add(Ui.Header("Places"));
        Place("Home", user, "");
        Place("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "");
        Place("Downloads", System.IO.Path.Combine(user, "Downloads"), "");
        Place("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "");
        Place("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "");
        Place("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "");
        Place("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "");

        _sidebar.Children.Add(Ui.Header("Drives"));
        Place("This PC", null, "");
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                Place(drive.Name.TrimEnd('\\') + (drive.VolumeLabel.Length > 0 ? "  " + drive.VolumeLabel : ""), drive.RootDirectory.FullName, "");
        }
        catch (Exception) { }
    }

    // ---------------------------------------------------------------- input

    public override void FocusDefault() =>
        (_rows.Children.OfType<Button>().FirstOrDefault(b => b.Tag is string) ?? _rows.Children.OfType<Button>().FirstOrDefault())?.Focus();

    public override bool OnAction(NavAction action)
    {
        var focusedPath = (Keyboard.FocusedElement as Button)?.Tag as string;
        bool onItem = focusedPath != null && focusedPath != "selected";

        switch (action)
        {
            case NavAction.Back:
                if (_selected.Count > 0) { _selected.Clear(); Rebuild(); return true; }
                if (_folder == null) return false; // leave the explorer
                GoTo(System.IO.Path.GetDirectoryName(_folder));
                return true;
            case NavAction.Y:
                if (onItem)
                {
                    if (!_selected.Remove(focusedPath!)) _selected.Add(focusedPath!);
                    Rebuild();
                }
                return true;
            case NavAction.X:
                if (onItem || _selected.Count > 0) _ = ShowActionsAsync(focusedPath);
                return true;
            case NavAction.LT or NavAction.RT:
                var rows = _rows.Children.OfType<Button>().ToList();
                int index = rows.FindIndex(r => r.IsKeyboardFocused);
                if (index >= 0) rows[Math.Clamp(index + (action == NavAction.RT ? 8 : -8), 0, rows.Count - 1)].Focus();
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- actions

    private static void Open(string path)
    {
        if (!ProcessUtil.Open(path)) ProcessUtil.Open("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + path);
    }

    /// <summary>The paths an action applies to: the selection if there is one, otherwise the highlighted item.</summary>
    private List<string> Targets(string? focused) =>
        _selected.Count > 0 ? _selected.ToList() : focused != null ? new List<string> { focused } : new List<string>();

    private async Task ShowActionsAsync(string? focused)
    {
        var targets = Targets(focused);
        if (targets.Count == 0) return;
        bool single = targets.Count == 1;
        string title = single ? System.IO.Path.GetFileName(targets[0].TrimEnd('\\')) : targets.Count + " items selected";

        var actions = new List<(string Label, string Glyph, Func<Task> Run)>();
        void Add(string label, string glyph, Action run) => actions.Add((label, glyph, () => { run(); return Task.CompletedTask; }));

        if (single) Add("Open", "", () => { if (Directory.Exists(targets[0])) GoTo(targets[0]); else Open(targets[0]); });
        if (single && File.Exists(targets[0]))
            Add("Open with…", "", () => ProcessUtil.Open("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + targets[0]));
        Add("Copy", "", () => SetClipboard(targets, cut: false));
        Add("Move (cut)", "", () => SetClipboard(targets, cut: true));
        if (single) actions.Add(("Rename", "", () => RenameAsync(targets[0])));
        actions.Add(("Delete (to the Recycle Bin)", "", () => DeleteAsync(targets)));
        actions.Add(("Compress to a ZIP file", "", () => RunAsync("Compressing…", () => FileOps.Zip(targets, _folder!))));
        if (single && targets[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            actions.Add(("Extract here", "", () => RunAsync("Extracting…", () => FileOps.Unzip(targets[0]))));
        if (single) actions.Add(("Properties", "", () => PropertiesAsync(targets[0])));
        if (_folder != null) Add("Select everything in this folder", "", () =>
        {
            foreach (var row in _rows.Children.OfType<Button>())
                if (row.Tag is string p) _selected.Add(p);
            Rebuild();
        });

        int choice = await Host.AskWithIconsAsync(title, null, actions.Select(a => (a.Label, a.Glyph)).ToArray());
        if (choice >= 0) await actions[choice].Run();
    }

    private void SetClipboard(List<string> paths, bool cut)
    {
        _clipboard.Clear();
        _clipboard.AddRange(paths);
        _clipboardIsCut = cut;
        _selected.Clear();
        Rebuild();
    }

    /// <summary>Runs a file operation behind a "working" pop-up and reports anything that went wrong.</summary>
    private async Task RunAsync(string message, Action work)
    {
        try
        {
            await Host.RunBusyAsync(message, () => Task.Run(work));
        }
        catch (Exception ex)
        {
            await Host.AskAsync("That didn't work", ex.Message, "OK");
        }
        _selected.Clear();
        Rebuild();
    }

    private async void Paste()
    {
        if (_folder == null || _clipboard.Count == 0) return;
        var sources = _clipboard.ToList();
        bool move = _clipboardIsCut;
        string folder = _folder;
        await RunAsync(move ? "Moving…" : "Copying…", () => FileOps.Transfer(sources, folder, move, CancellationToken.None));
        if (move) _clipboard.Clear();
        Rebuild();
    }

    private async void NewFolder()
    {
        if (_folder == null) return;
        var name = await Host.PromptAsync("New folder", "Name for the folder", "New folder", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var path = FileOps.UniquePath(_folder, name.Trim());
            Directory.CreateDirectory(path);
            _focusPath = path;
        }
        catch (Exception ex)
        {
            await Host.AskAsync("Couldn't create the folder", ex.Message, "OK");
        }
        Rebuild();
    }

    private async Task RenameAsync(string path)
    {
        var current = System.IO.Path.GetFileName(path.TrimEnd('\\'));
        var name = await Host.PromptAsync("Rename", null, current, "Rename");
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == current) return;
        try
        {
            _focusPath = FileOps.Rename(path, name.Trim());
        }
        catch (Exception ex)
        {
            await Host.AskAsync("Couldn't rename it", ex.Message, "OK");
        }
        Rebuild();
    }

    private async Task DeleteAsync(List<string> paths)
    {
        string what = paths.Count == 1 ? System.IO.Path.GetFileName(paths[0].TrimEnd('\\')) : paths.Count + " items";
        if (await Host.AskAsync("Delete " + what + "?", "It goes to the Recycle Bin, so you can get it back.", "Cancel", "Delete") != 1) return;
        await RunAsync("Deleting…", () => FileOps.Recycle(paths));
    }

    private async Task PropertiesAsync(string path)
    {
        string details;
        try
        {
            if (Directory.Exists(path))
            {
                var dir = new DirectoryInfo(path);
                var (count, size) = await Task.Run(() =>
                {
                    long total = 0; int n = 0;
                    try
                    {
                        foreach (var f in dir.EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                        {
                            total += f.Length;
                            if (++n >= 200000) break;
                        }
                    }
                    catch (Exception) { }
                    return (n, total);
                });
                details = $"Folder\n{path}\n\nContains {count:N0} files, {SystemStats.Bytes(size)}\nModified {dir.LastWriteTime:f}";
            }
            else
            {
                var file = new FileInfo(path);
                details = $"{(file.Extension.Length > 1 ? file.Extension[1..].ToUpperInvariant() + " file" : "File")}\n{path}\n\n" +
                          $"Size {SystemStats.Bytes(file.Length)} ({file.Length:N0} bytes)\nCreated {file.CreationTime:f}\nModified {file.LastWriteTime:f}";
            }
        }
        catch (Exception ex)
        {
            details = ex.Message;
        }
        await Host.AskAsync(System.IO.Path.GetFileName(path.TrimEnd('\\')), details, "OK");
    }
}

/// <summary>
/// Running apps, all processes and live performance. X ends the highlighted task, Y changes the sort,
/// LB / RB switch tabs.
/// </summary>
public sealed class TaskManagerScreen : ListScreen
{
    private static readonly string[] Tabs = { "Apps", "Processes", "Performance" };

    private readonly SystemStats _stats = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly List<double> _cpuHistory = new(), _memoryHistory = new();
    private int _tab;
    private string _sort = "memory"; // memory | cpu | name
    private bool _sampling;

    public TaskManagerScreen()
        : base("Task Manager", "{Select}  Actions      {Details}  End task      {Options}  Sort      LB / RB  Tabs      {Back}  Back")
    {
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public override void OnShown()
    {
        Rebuild();
        _ = RefreshAsync();
        _timer.Start();
    }

    public override void OnClosed() => _timer.Stop();

    private async Task RefreshAsync()
    {
        if (_sampling) return;
        _sampling = true;
        try
        {
            await Task.Run(_stats.Sample);
            _cpuHistory.Add(_stats.CpuPercent);
            _memoryHistory.Add(_stats.MemoryTotal > 0 ? 100.0 * _stats.MemoryUsed / _stats.MemoryTotal : 0);
            if (_cpuHistory.Count > 60) { _cpuHistory.RemoveAt(0); _memoryHistory.RemoveAt(0); }
        }
        finally { _sampling = false; }
        // Don't shuffle the list under an open menu.
        if (!Host.IsTop(this)) return;
        // The order changes as usage does: keep the highlight on the same program, not the same position.
        string? focusedName = (Keyboard.FocusedElement as Button)?.Tag is ProcessRow focusedRow ? focusedRow.Name : null;
        Rebuild();
        if (focusedName != null &&
            Rows.Children.OfType<Button>().FirstOrDefault(b => b.Tag is ProcessRow r && r.Name == focusedName) is { } same)
            FocusLater(same);
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.LB: _tab = (_tab + Tabs.Length - 1) % Tabs.Length; RebuildFromTop(); return true;
            case NavAction.RB: _tab = (_tab + 1) % Tabs.Length; RebuildFromTop(); return true;
            case NavAction.Y:
                _sort = _sort switch { "memory" => "cpu", "cpu" => "name", _ => "memory" };
                Rebuild();
                return true;
            case NavAction.X:
                if (Keyboard.FocusedElement is Button { Tag: ProcessRow row }) _ = EndAsync(row);
                return true;
        }
        return false;
    }

    private string MemoryPercent => _stats.MemoryTotal > 0 ? $"{100.0 * _stats.MemoryUsed / _stats.MemoryTotal:0}%" : "–";

    protected override void Build()
    {
        // Always-visible summary, and the tab switch for touch and mouse.
        var battery = Battery.GetPercent();
        AddNote($"CPU {_stats.CpuPercent:0}%      Memory {MemoryPercent}" + (battery != null ? $"      Battery {battery}%" : ""));
        AddRow("View", string.Join("   ", Tabs.Select((t, i) => i == _tab ? "[" + t + "]" : t)), "", click: () =>
        {
            _tab = (_tab + 1) % Tabs.Length;
            RebuildFromTop();
        });

        if (_tab == 2) { BuildPerformance(); return; }

        var source = _tab == 0 ? _stats.Apps : _stats.Processes;
        IEnumerable<ProcessRow> rows = _sort switch
        {
            "cpu" => source.OrderByDescending(p => p.CpuPercent),
            "name" => source.OrderBy(p => p.Title ?? p.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => source.OrderByDescending(p => p.MemoryBytes)
        };
        AddHeader($"{Tabs[_tab]}  ·  sorted by {_sort}");
        if (source.Count == 0) AddNote("Reading…");

        foreach (var p in rows.Take(250))
        {
            var row = p;
            string name = row.Title ?? (row.Count > 1 ? $"{row.Name}  ({row.Count})" : row.Name);
            var button = AddRow(name, $"CPU {row.CpuPercent:0.#}%      {SystemStats.Bytes(row.MemoryBytes)}",
                _tab == 0 ? "" : "", click: () => _ = ActionsAsync(row));
            button.Tag = row;
        }

        AddHeader("More");
        AddRow("Run a program…", glyph: "", click: RunNew);
    }

    private void BuildPerformance()
    {
        AddHeader("Processor");
        AddRow("CPU", $"{_stats.CpuPercent:0}%   ({Environment.ProcessorCount} logical processors)", "");
        Rows.Children.Add(Graph(_cpuHistory));

        AddHeader("Memory");
        AddRow("In use", $"{MemoryPercent}   ({SystemStats.Bytes(_stats.MemoryUsed)} of {SystemStats.Bytes(_stats.MemoryTotal)})", "");
        Rows.Children.Add(Graph(_memoryHistory));

        AddHeader("Network");
        AddRow("Download", SystemStats.Bytes(_stats.DownloadBytesPerSecond) + "/s", "");
        AddRow("Upload", SystemStats.Bytes(_stats.UploadBytesPerSecond) + "/s", "");

        AddHeader("Power and storage");
        var battery = Battery.GetPercent();
        AddRow("Battery", battery == null ? "No battery" : $"{battery}%  ({(Battery.IsPluggedIn() ? "plugged in" : "on battery")})", "");
        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                AddRow("Drive " + d.Name.TrimEnd('\\'), $"{SystemStats.Bytes(d.AvailableFreeSpace)} free of {SystemStats.Bytes(d.TotalSize)}", "");
        }
        catch (Exception) { }

        AddHeader("System");
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        AddRow("Up time", up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h {up.Minutes}m" : $"{up.Hours}h {up.Minutes}m", "");
        AddRow("Processes", _stats.Processes.Sum(p => p.Count).ToString(), "");
    }

    /// <summary>A small line chart of the last two minutes, 0 to 100%.</summary>
    private static FrameworkElement Graph(List<double> history)
    {
        const double width = 860, height = 90;
        var line = new Polyline { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        line.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        for (int i = 0; i < history.Count; i++)
            line.Points.Add(new Point(width * i / 59.0, height - history[i] / 100 * height));

        var box = new Border
        {
            Width = width + 20, Height = height + 20, Padding = new Thickness(10), CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 3, 0, 3), Child = new Canvas { Children = { line } }
        };
        box.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        return box;
    }

    private async Task ActionsAsync(ProcessRow row)
    {
        string title = row.Title ?? row.Name;
        var options = new List<(string, string)>
        {
            ("Switch to", ""), ("Close (lets it save first)", ""), ("End task (force close)", ""),
            ("Set priority", ""), ("Open file location", ""), ("Details", "")
        };
        int choice = await Host.AskWithIconsAsync(title, null, options.ToArray());
        switch (choice)
        {
            case 0:
                if (!SwitchTo(row)) await Host.AskAsync("Couldn't switch to " + title, "It has no window to bring forward.", "OK");
                break;
            case 1:
                if (SystemStats.Close(row.Ids) == 0)
                    await Host.AskAsync("Couldn't close " + title, "It has no window to close. Use End task instead.", "OK");
                break;
            case 2: await EndAsync(row); break;
            case 3: await PriorityAsync(row); break;
            case 4:
                if (PathOf(row) is string path) ProcessUtil.Open("explorer.exe", "/select,\"" + path + "\"");
                else await Host.AskAsync("Location not available", "Windows doesn't let this app see where that program is.", "OK");
                break;
            case 5: await DetailsAsync(row); break;
        }
        _ = RefreshAsync();
    }

    private static string? PathOf(ProcessRow row)
    {
        foreach (var id in row.Ids)
        {
            try
            {
                using var p = Process.GetProcessById(id);
                var path = p.MainModule?.FileName;
                if (path != null) return path;
            }
            catch (Exception) { }
        }
        return null;
    }

    private static bool SwitchTo(ProcessRow row)
    {
        var path = PathOf(row);
        return path != null && ProcessUtil.FocusWindow(path, null);
    }

    private async Task EndAsync(ProcessRow row)
    {
        string title = row.Title ?? row.Name;
        if (SystemStats.IsProtected(row.Name))
        {
            await Host.AskAsync("That one is protected",
                row.Name + " is part of Windows. Ending it would crash or sign out the PC, so it isn't allowed here.", "OK");
            return;
        }
        if (await Host.AskAsync("End " + title + "?", "It closes immediately along with anything it started. Unsaved work in it is lost.",
                "Cancel", "End task") != 1) return;

        int ended = SystemStats.Kill(row.Ids);
        if (ended == 0)
            await Host.AskAsync("Couldn't end " + title,
                "Windows refused. Programs started as administrator, services and anti-cheat need the launcher to be run as administrator.", "OK");
        _ = RefreshAsync();
    }

    private async Task PriorityAsync(ProcessRow row)
    {
        var levels = new[]
        {
            ("High", ProcessPriorityClass.High), ("Above normal", ProcessPriorityClass.AboveNormal), ("Normal", ProcessPriorityClass.Normal),
            ("Below normal", ProcessPriorityClass.BelowNormal), ("Low", ProcessPriorityClass.Idle)
        };
        int choice = await Host.AskAsync("Priority for " + (row.Title ?? row.Name),
            "Higher gives it more processor time when the PC is busy (useful for a game).", levels.Select(l => l.Item1).ToArray());
        if (choice < 0) return;
        if (!SystemStats.SetPriority(row.Ids, levels[choice].Item2))
            await Host.AskAsync("Couldn't change the priority", "Windows refused the change for this program.", "OK");
    }

    private async Task DetailsAsync(ProcessRow row)
    {
        var lines = new List<string>
        {
            "Program: " + row.Name,
            $"Processes: {row.Count}  (ID {string.Join(", ", row.Ids.Take(8))}{(row.Ids.Count > 8 ? "…" : "")})",
            $"CPU: {row.CpuPercent:0.#}%",
            "Memory: " + SystemStats.Bytes(row.MemoryBytes)
        };
        try
        {
            using var p = Process.GetProcessById(row.Ids[0]);
            lines.Add("Threads: " + p.Threads.Count);
            lines.Add("Started: " + p.StartTime.ToString("f"));
            lines.Add("Priority: " + p.PriorityClass);
        }
        catch (Exception) { }
        if (PathOf(row) is string path) lines.Add("Location: " + path);
        await Host.AskAsync(row.Title ?? row.Name, string.Join("\n", lines), "OK");
    }

    private async void RunNew()
    {
        var command = await Host.PromptAsync("Run a program", "A program name, path or web address (for example: notepad).", "", "Run");
        if (string.IsNullOrWhiteSpace(command)) return;
        if (!ProcessUtil.Open(command.Trim())) await Host.AskAsync("Couldn't run that", "Windows didn't recognise it.", "OK");
    }
}

/// <summary>AnyFSE: what it is, how to get it, and a way to open it once it is installed.</summary>
public sealed class AnyFseScreen : ListScreen
{
    private string? _busy;

    public AnyFseScreen() : base("Xbox mode and AnyFSE") { }

    public override void OnResumed() => Rebuild();

    protected override void Build()
    {
        bool installed = AnyFse.IsInstalled();

        AddNote("AnyFSE is a free tool that lets a launcher of your choice be the Home app of Windows' gaming " +
                "full-screen experience (the console-style mode on handhelds such as the ROG Ally). With it, this " +
                "launcher can be what the device opens to.");
        AddRow("Status", installed ? "Installed" : "Not installed", "");
        if (_busy != null) { AddNote(_busy); return; }

        if (installed)
        {
            // The whole job in one switch: AnyFSE starts SquareDeck, and Windows uses AnyFSE as the Xbox mode home.
            bool ours = AnyFse.LaunchesUs(), home = AnyFse.IsHomeApp();
            AddHeader("Xbox mode");
            AddRow("Use SquareDeck as the Xbox mode home app", ours ? "On" : "Off", "\uE7FC", click: () => _ = SetXboxModeAsync(!ours),
                subtitle: ours && !home ? "One step left: choose AnyFSE as the Home app in Windows" : "Starts SquareDeck when Xbox mode opens");
            AddRow("Home app in Windows", home ? "AnyFSE (done)" : "Not AnyFSE yet", "\uE713",
                click: () => ProcessUtil.Open("ms-settings:gaming-gamebar"),
                subtitle: "Settings > Gaming > Full screen experience > Home app");
            AddRow("Open Xbox mode when Windows starts", AnyFse.StartsAtSignIn ? "On" : "Off", "\uE7E8", click: () =>
            {
                AnyFse.StartsAtSignIn = !AnyFse.StartsAtSignIn;
                Rebuild();
            });
            AddHeader("AnyFSE");
            AddRow("Open AnyFSE", glyph: "", click: Open);
            AddRow("Update AnyFSE to the latest version", glyph: "", click: Install);
        }
        else
        {
            AddRow("Download and install AnyFSE", glyph: "", click: Install);
        }
        AddRow("Open the AnyFSE releases page", glyph: "", click: () => ProcessUtil.Open(AnyFse.ReleasesPage));

        AddHeader("How to set it up");
        AddNote("1.  Install AnyFSE with the button above. It downloads AnyFSE.Installer.exe from the project's GitHub " +
                "releases and runs it. The installer needs administrator rights, so Windows will ask for permission.");
        AddNote("2.  Open AnyFSE (from here or the Start menu) and choose a custom program as the launcher. Point it at:\n" +
                (Environment.ProcessPath ?? "SquareDeck.exe"));
        AddNote("3.  In Windows, go to Settings > Gaming > Full screen experience and set AnyFSE as the Home app. " +
                "AnyFSE only works while it is selected there.");
        AddRow("Open Windows Gaming settings", glyph: "", click: () => ProcessUtil.Open("ms-settings:gaming-gamebar"));

        AddHeader("Before you install");
        AddNote("The full-screen experience must be available on your device: a supported handheld, or enabled with a " +
                "third-party tool the AnyFSE page links to.");
        AddNote("Microsoft Defender may flag the AnyFSE installer. Its author says this is because of the process " +
                "monitoring it uses for an optional button-remapping feature. It is a separate project, not part of this " +
                "launcher, so decide for yourself whether you trust it.");
    }

    private async Task SetXboxModeAsync(bool on)
    {
        if (!AnyFse.UseUs(on))
        {
            await Host.AskAsync("Couldn't change AnyFSE's settings",
                "Its settings file wasn't found. Open AnyFSE once (so it creates them), then try again, or choose a custom launcher " +
                "in AnyFSE pointing at:\n" + (Environment.ProcessPath ?? "SquareDeck.exe"), "OK");
            return;
        }
        if (on && !AnyFse.IsHomeApp() &&
            await Host.AskAsync("Almost done", "AnyFSE will now start SquareDeck. Last step: in Windows, choose AnyFSE as the Home app " +
                "(Settings > Gaming > Full screen experience). Open that page now?", "Open Windows settings", "Later") == 0)
            ProcessUtil.Open("ms-settings:gaming-gamebar");
        Rebuild();
    }

    private async void Open()
    {
        if (!await AnyFse.OpenAsync())
            await Host.AskAsync("Couldn't open AnyFSE", "Its Start menu entry wasn't found. Try opening it from the Start menu.", "OK");
    }

    private async void Install()
    {
        int choice = await Host.AskAsync("Download and run the AnyFSE installer?",
            "The newest AnyFSE.Installer.exe is downloaded from github.com/ashpynov/AnyFSE and started. Windows asks for " +
            "administrator permission, and Microsoft Defender may warn about it.",
            "Cancel", "Download and install");
        if (choice != 1) return;

        _busy = "Downloading the AnyFSE installer…";
        Rebuild();
        try
        {
            var (path, version) = await Task.Run(() => AnyFse.DownloadInstallerAsync(CancellationToken.None));
            _busy = $"Running the AnyFSE {version} installer… follow its prompts.";
            Rebuild();
            if (!ProcessUtil.Start(path)) throw new InvalidOperationException("The installer was downloaded but couldn't be started.");
        }
        catch (Exception ex)
        {
            _busy = null;
            Rebuild();
            if (await Host.AskAsync("Couldn't install AnyFSE", ex.Message, "Open the releases page", "Close") == 0)
                ProcessUtil.Open(AnyFse.ReleasesPage);
            return;
        }

        // The installer runs on its own; check back when the user returns to this page.
        await Task.Delay(4000);
        _busy = null;
        if (Host.IsTop(this)) Rebuild();
    }
}

/// <summary>Larger text, high contrast, less motion, and Windows' color filters.</summary>
public sealed class AccessibilityScreen : ListScreen
{
    private static readonly int[] Sizes = { 100, 115, 130, 150 };

    public AccessibilityScreen() : base("Accessibility") { }

    protected override void Build()
    {
        var s = Host.Settings;

        AddHeader("Seeing");
        AddRow("Text and control size", s.TextScale + "%", "", click: () =>
        {
            s.TextScale = Sizes[(Array.IndexOf(Sizes, s.TextScale) + 1) % Sizes.Length];
            Apply();
            Host.Pop(this);                       // pages take the new size when they open,
            Host.Push(new AccessibilityScreen()); // so reopen this one to show it straight away
        });
        AddNote("Makes text and buttons larger on every page. The home screen keeps its layout; its tiles are already large.");

        string contrast = s.ThemeMode switch { "hc-dark" => "White on black", "hc-light" => "Black on white", _ => "Off" };
        AddRow("High contrast", contrast, "", click: () =>
        {
            // Off -> white on black -> black on white -> off (back to following Windows)
            s.ThemeMode = s.ThemeMode switch { "hc-dark" => "hc-light", "hc-light" => "system", _ => "hc-dark" };
            s.DarkTheme = Theme.ShouldBeDark(s);
            Apply();
        });
        AddNote("Pure black and white with a bright highlight and no gray text. Wallpapers and hero backgrounds are " +
                "best turned off with it (Settings).");

        AddRow("Color filters (Windows)", glyph: "", click: () => ProcessUtil.Open("ms-settings:easeofaccess-colorfilter"));
        AddNote("Grayscale, inverted and color-blindness filters are a Windows feature that applies to the whole screen, " +
                "games included. This opens its page; the shortcut Win+Ctrl+C can then switch it on and off.");

        AddHeader("Motion");
        AddRow("Reduce motion", s.ReduceMotion ? "On" : "Off", "", click: () =>
        {
            s.ReduceMotion = !s.ReduceMotion;
            Apply();
        });
        AddNote("Stops the pulsing highlight, page slides, fades, animated covers and the startup animation.");

        AddHeader("Related");
        AddRow("Vibration, sounds and the rest", glyph: "", click: () => Host.Pop(this));
    }

    private void Apply()
    {
        Host.Settings.Save();
        Host.ApplyLook();
        Host.ApplySettings();
        Rebuild();
    }
}

/// <summary>Everything about interface sounds: on / off, the set, loudness, and which kinds play.</summary>
public sealed class SoundsScreen : ListScreen
{
    private static readonly int[] Volumes = { 25, 50, 75, 100 };

    public SoundsScreen() : base("Interface sounds") { }

    protected override void Build()
    {
        var s = Host.Settings;
        static string OnOff(bool on) => on ? "On" : "Off";

        AddRow("Interface sounds", OnOff(s.UiSounds), "\uE767", click: () => Change(() => s.UiSounds = !s.UiSounds));
        AddRow("Sound set", UiSound.SetName(s.SoundSet), "\uE8D6", click: ChooseSet);
        if (SoundPacks.Find(s.SoundSet) is { } pack)
            AddNote("Sound pack \u201C" + pack.Name + "\u201D" + (pack.Author.Length > 0 ? " by " + pack.Author : "") +
                    ". Sounds a pack doesn't include use the Classic set.");
        if (s.SoundSet == "custom")
            AddNote("Custom sounds come from " + (s.SoundFolder.Length > 0 ? s.SoundFolder : "(no folder chosen)") +
                    ". Put WAV files there named " + string.Join(", ", UiSound.CustomFiles) +
                    ". Any that are missing use the Classic sound.");
        AddRow("Loudness", s.SoundVolume + "%", "\uE995", click: () =>
            Change(() => s.SoundVolume = Volumes[(Array.IndexOf(Volumes, s.SoundVolume) + 1) % Volumes.Length]));
        if (s.SoundSet == "custom" || s.SoundSet.StartsWith("pack:"))
            AddNote("Loudness applies to the built-in sets; sound packs and your own WAV files play as they were recorded.");

        AddHeader("Which sounds play");
        AddRow("Moving the highlight", OnOff(s.SoundMove), "\uE8AB", click: () => Change(() => s.SoundMove = !s.SoundMove));
        AddRow("Select and back", OnOff(s.SoundSelect), "\uE73E", click: () => Change(() => s.SoundSelect = !s.SoundSelect));
        AddRow("Changing page (LB / RB)", OnOff(s.SoundPage), "\uE8A9", click: () => Change(() => s.SoundPage = !s.SoundPage));
        AddRow("Pop-ups and achievements", OnOff(s.SoundToast), "\uEA8F", click: () => Change(() => s.SoundToast = !s.SoundToast));

        AddHeader("Background music");
        AddRow("Music on the launcher's screens", BackgroundMusic.NameOf(s.MenuMusic), "\uE8D6",
            click: () => _ = SettingsScreen.ChooseBackgroundMusicAsync(Host, Rebuild));
        AddNote("Plays quietly only while no game is running. Loudness and your own tracks are in Settings > Sound.");

        AddHeader("Sound packs");
        AddRow("Open the sound packs folder", glyph: "\uE8B7", click: () =>
        {
            try { Directory.CreateDirectory(SoundPacks.UserDir); } catch (Exception) { }
            ProcessUtil.Open(SoundPacks.UserDir);
        });
        AddNote("Packs made for the Steam Deck audio loader work here: put each pack's folder (with its pack.json and .wav files) " +
                "in that folder, then come back to this page.");

        AddHeader("Try them");
        AddRow("Move", glyph: "\uE768", click: UiSound.Move);
        AddRow("Back", glyph: "\uE768", click: UiSound.Back);
        AddRow("Page", glyph: "\uE768", click: UiSound.Page);
        AddRow("Pop-up", glyph: "\uE768", click: UiSound.Toast);
        AddRow("Error", glyph: "\uE768", click: UiSound.Error);
        AddRow("Startup chime", glyph: "\uE768", click: UiSound.Startup);
        AddNote("The game start chime and the startup chime have their own settings on the Settings page.");
    }

    private void Change(Action change)
    {
        change();
        Host.Settings.Save();
        UiSound.Configure(Host.Settings);
        UiSound.Select();
        Rebuild();
    }

    private async void ChooseSet()
    {
        SoundPacks.Refresh(); // pick up packs copied in since the launcher started
        var sets = UiSound.AllSets();
        int choice = await Host.AskAsync("Sound set", "Built-in sets first, then sound packs.", sets.Select(x => x.Name).ToArray());
        if (choice < 0) return;
        var id = sets[choice].Id;
        if (id == "custom")
        {
            var folder = await Host.PickFolderAsync("Choose the folder with your sound files");
            if (folder == null) return;
            Host.Settings.SoundFolder = folder;
        }
        Change(() => Host.Settings.SoundSet = id);
    }
}

/// <summary>
/// The ROG Ally's extra buttons: choose what each does. Reading them follows AnyFSE (MIT); see AllyButtons.
/// </summary>
public sealed class AllyButtonsScreen : ListScreen
{
    public AllyButtonsScreen() : base("ROG Ally buttons") { }

    protected override void Build()
    {
        var s = Host.Settings;
        bool supported = AllyButtons.IsSupported;
        AddRow("Remap the buttons", s.AllyRemap ? "On" : "Off", "\uE765", click: () =>
        {
            s.AllyRemap = !s.AllyRemap;
            s.Save();
            Host.ApplyAllyButtons();
            Rebuild();
        }, subtitle: (AllyButtons.Model.Length > 0 ? AllyButtons.Model : "ROG Ally") + (supported ? "" : " (button device not found)"));

        if (s.AllyRemap)
        {
            AddHeader("Buttons");
            foreach (var (id, name) in AllyButtons.Buttons)
            {
                if (id.EndsWith("library") && !AllyButtons.IsXboxAlly) continue; // only the Xbox Allys have it
                if (id.EndsWith("cc") && AllyButtons.IsXboxAlly) continue;
                string button = id;
                s.AllyBindings.TryGetValue(id, out var action);
                AddRow(name, AllyButtons.Actions.FirstOrDefault(a => a.Id == (action ?? "")).Name ?? "Nothing", "\uE7FC",
                    click: () => _ = ChooseAsync(button, name));
            }
            AddRow("The Ally's other keys", s.AllyExtraKeys ? "On" : "Off", "\uE765", click: () =>
            {
                s.AllyExtraKeys = !s.AllyExtraKeys;
                s.Save();
                Rebuild();
            }, subtitle: "Microphone, screenshot, keyboard and record keys");
            AddRow("Back to the defaults", glyph: "\uE777", click: () =>
            {
                s.AllyBindings = AllyButtons.DefaultBindings();
                s.Save();
                Rebuild();
            });
        }

        AddHeader("Armoury Crate SE");
        AddNote("Armoury Crate SE and ASUS Optimization react to these buttons too, so both may respond to a press. Set the buttons to " +
                "do nothing in Armoury Crate SE (or uninstall it). AnyFSE solves this by filtering ASUS's own service; SquareDeck " +
                "doesn't, because that needs code injected into another program.");
        AddNote("Remapping works while SquareDeck is running. Hold Mode (the back button) and press another button for the Mode + actions. " +
                "This part of SquareDeck is based on AnyFSE by Artem Shpynov (MIT licence).");
    }

    private async Task ChooseAsync(string button, string name)
    {
        int choice = await Host.AskAsync(name, null, AllyButtons.Actions.Select(a => a.Name).ToArray());
        if (choice < 0) return;
        Host.Settings.AllyBindings[button] = AllyButtons.Actions[choice].Id;
        Host.Settings.Save();
        Rebuild();
    }
}
