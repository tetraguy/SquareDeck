using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>One installed app, from Windows' list of programs (desktop) or its app packages (Microsoft Store).</summary>
public sealed class ManagedApp
{
    public string Name = "", Publisher = "", Version = "", Location = "";
    public long Bytes;                 // estimated size, 0 if unknown
    public DateTime? Installed;
    public bool IsStore;
    // Desktop programs
    public string Uninstall = "", QuietUninstall = "", Modify = "", IconPath = "", ProductCode = "";
    public bool CanModify, CanRepair, IsMsi;
    // Store apps
    public string PackageFullName = "", PackageName = "";
}

/// <summary>
/// Reads the installed apps the way Settings > Apps does: desktop programs from the Uninstall entries in the
/// registry (machine-wide, 32-bit and per-user), and Microsoft Store apps from the user's app packages, named as the
/// Start menu names them. Updates, system components and frameworks are left out.
/// </summary>
public static class AppsManager
{
    public static async Task<List<ManagedApp>> ListAsync(CancellationToken ct)
    {
        var desktop = await Task.Run(ReadDesktop, ct).ConfigureAwait(false);
        List<ManagedApp> store;
        try { store = await ReadStoreAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { store = new(); }
        return desktop.Concat(store)
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static List<ManagedApp> ReadDesktop()
    {
        var apps = new List<ManagedApp>();
        var places = new (RegistryKey Root, string Path)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (root, path) in places)
        {
            try
            {
                using var list = root.OpenSubKey(path);
                if (list == null) continue;
                foreach (var id in list.GetSubKeyNames())
                {
                    try
                    {
                        using var k = list.OpenSubKey(id);
                        if (k == null) continue;
                        string name = (k.GetValue("DisplayName") as string ?? "").Trim();
                        string uninstall = k.GetValue("UninstallString") as string ?? "";
                        if (name.Length == 0 || uninstall.Length == 0) continue;
                        if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if (k.GetValue("ParentKeyName") != null) continue; // an update to another program
                        string release = k.GetValue("ReleaseType") as string ?? "";
                        if (release.Contains("Update", StringComparison.OrdinalIgnoreCase) || release.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)) continue;
                        bool msi = k.GetValue("WindowsInstaller") is int wi && wi == 1 && id.StartsWith('{');
                        var app = new ManagedApp
                        {
                            Name = name,
                            Publisher = (k.GetValue("Publisher") as string ?? "").Trim(),
                            Version = (k.GetValue("DisplayVersion") as string ?? "").Trim(),
                            Location = (k.GetValue("InstallLocation") as string ?? "").Trim().Trim('"'),
                            Uninstall = uninstall,
                            QuietUninstall = k.GetValue("QuietUninstallString") as string ?? "",
                            Modify = k.GetValue("ModifyPath") as string ?? "",
                            IconPath = (k.GetValue("DisplayIcon") as string ?? "").Trim(),
                            IsMsi = msi,
                            ProductCode = msi ? id : "",
                        };
                        app.CanModify = !(k.GetValue("NoModify") is int nm && nm == 1) && (app.Modify.Length > 0 || msi);
                        app.CanRepair = msi && !(k.GetValue("NoRepair") is int nr && nr == 1);
                        if (k.GetValue("EstimatedSize") is int kb && kb > 0) app.Bytes = kb * 1024L;
                        if (k.GetValue("InstallDate") is string date && date.Length == 8 &&
                            DateTime.TryParseExact(date, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var when)) app.Installed = when;
                        apps.Add(app);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }
        return apps;
    }

    private static async Task<List<ManagedApp>> ReadStoreAsync(CancellationToken ct)
    {
        // The packages (not frameworks, not part of Windows, removable), and the Start menu's names for them.
        const string script =
            "[Console]::OutputEncoding=[Text.Encoding]::UTF8;" +
            "$n=@{}; Get-StartApps | ForEach-Object { if ($_.AppID -like '*!*') { $f=$_.AppID.Split('!')[0]; if (-not $n.ContainsKey($f)) { $n[$f]=$_.Name } } };" +
            "Get-AppxPackage -PackageTypeFilter Main | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and $_.SignatureKind -ne 'System' } | " +
            "ForEach-Object { [pscustomobject]@{ Name=$n[$_.PackageFamilyName]; Package=$_.Name; Full=$_.PackageFullName; Publisher=$_.Publisher; Version=$_.Version.ToString(); Location=$_.InstallLocation } } | ConvertTo-Json -Compress";
        var (_, output) = await ProcessUtil.RunAsync("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"", ct).ConfigureAwait(false);
        var apps = new List<ManagedApp>();
        int start = output.IndexOfAny(new[] { '[', '{' });
        if (start < 0) return apps;
        using var doc = JsonDocument.Parse(output[start..]);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
        foreach (var item in items)
        {
            string? Get(string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            string name = Get("Name") ?? "";
            string package = Get("Package") ?? "";
            if (name.Length == 0) continue; // nothing in Start: background parts of other apps
            string publisher = Get("Publisher") ?? "";
            int cn = publisher.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
            if (cn >= 0) publisher = publisher[(cn + 3)..].Split(',')[0].Trim('"');
            apps.Add(new ManagedApp
            {
                Name = name, IsStore = true, PackageName = package, PackageFullName = Get("Full") ?? "",
                Publisher = publisher, Version = Get("Version") ?? "", Location = Get("Location") ?? ""
            });
        }
        return apps;
    }

    /// <summary>Splits a command line from the registry into the program and its arguments.</summary>
    public static (string File, string Args) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0) return (command[1..end], command[(end + 1)..].Trim());
        }
        int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return (command[..(exe + 4)], command[(exe + 4)..].Trim());
        int space = command.IndexOf(' ');
        return space > 0 ? (command[..space], command[(space + 1)..]) : (command, "");
    }

    /// <summary>Runs one of an app's commands and waits for it to finish (installers ask for permission themselves).</summary>
    public static async Task<string?> RunAsync(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
            if (p != null) await p.WaitForExitAsync();
            return null;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return "Cancelled."; }
        catch (Exception ex) { return ex.Message; }
    }

    public static Task<string?> UninstallAsync(ManagedApp app)
    {
        if (app.IsStore) return PowerShellAsync($"Remove-AppxPackage -Package '{app.PackageFullName.Replace("'", "''")}'");
        if (app.IsMsi) return RunAsync("msiexec.exe", "/x " + app.ProductCode);
        var (file, args) = SplitCommand(app.Uninstall);
        return RunAsync(file, args);
    }

    public static Task<string?> ModifyAsync(ManagedApp app)
    {
        if (app.Modify.Length > 0)
        {
            var (file, args) = SplitCommand(app.Modify);
            return RunAsync(file, args);
        }
        return RunAsync("msiexec.exe", "/i " + app.ProductCode);
    }

    /// <summary>Windows Installer's repair: puts back missing or damaged files and settings.</summary>
    public static Task<string?> RepairAsync(ManagedApp app) => RunAsync("msiexec.exe", "/fa " + app.ProductCode);

    /// <summary>A Store app back to how it was when installed (its data is removed). Windows 11.</summary>
    public static Task<string?> ResetAsync(ManagedApp app) =>
        PowerShellAsync($"Get-AppxPackage -Name '{app.PackageName.Replace("'", "''")}' | Reset-AppxPackage");

    private static async Task<string?> PowerShellAsync(string command)
    {
        try
        {
            var (code, output) = await ProcessUtil.RunAsync("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"");
            return code == 0 ? null : (output.Trim().Length > 0 ? output.Trim().Split('\n')[0] : "PowerShell couldn't do it.");
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>The program's icon file from DisplayIcon ("C:\…\app.exe,0"), or its uninstaller as a stand-in.</summary>
    public static string? IconFile(ManagedApp app)
    {
        string path = app.IconPath;
        int comma = path.LastIndexOf(',');
        if (comma > 2 && int.TryParse(path[(comma + 1)..].Trim(), out _)) path = path[..comma];
        path = path.Trim().Trim('"');
        if (path.Length > 0 && File.Exists(path)) return path;
        if (app.Location.Length > 0 && Directory.Exists(app.Location))
            return Directory.EnumerateFiles(app.Location, "*.exe").FirstOrDefault();
        return null;
    }
}

/// <summary>
/// Settings > Apps > Installed apps: every app, with its publisher, version, size and install date, like Windows'
/// own list. Search by name, sort by name, size or date, and show all, desktop or Store apps. Choosing an app opens
/// what can be done with it: uninstall, modify, repair, reset, open its folder.
/// </summary>
public sealed class InstalledAppsScreen : ListScreen
{
    private List<ManagedApp>? _apps;
    private string _search = "";
    private string _sort = "name";   // name | size | date
    private string _show = "all";    // all | desktop | store
    private CancellationTokenSource _cts = new();
    private readonly Dictionary<string, ImageSource?> _icons = new();

    /// <summary>Experimental "Switch 2 style Installed apps": which apps down the left, the list on the right.</summary>
    private readonly bool _s2 = Labs.On("s2apps");
    private readonly StackPanel _s2Nav = new() { Margin = new Thickness(0, 6, 0, 10) };
    private readonly StackPanel _s2Under = new() { Margin = new Thickness(40, 0, 18, 14) };

    public InstalledAppsScreen() : base("Installed apps", "{Select}  Options      {Details}  Search      {Options}  Sort      LB RB  Show      {Back}  Back")
    {
        if (!_s2) return;
        Rows.Width = double.NaN;
        Rows.HorizontalAlignment = HorizontalAlignment.Stretch;
        Rows.Margin = new Thickness(0, 0, 18, 24);
        // The list's scroller is still in the old body: take it out first (an element can only be in one place).
        (RowsScroll.Parent as Panel)?.Children.Remove(RowsScroll);
        if (RowsScroll.Parent is Decorator d) d.Child = null;
        (RowsScroll.Parent as ContentControl)?.SetValue(ContentControl.ContentProperty, null);
        ReplaceBody(Switch2Pages.Shell(_s2Nav, RowsScroll, _s2Under));
        UseSwitch2Chrome("Installed apps", "\uE71D");
    }

    public override void OnResumed()
    {
        if (_s2) RefreshSwitch2Colors();
        base.OnResumed();
    }

    protected override void AfterBuild()
    {
        if (!_s2) return;
        Switch2Settings.Restyle(Rows);
        BuildSwitch2Nav();
    }

    /// <summary>All, desktop and Store apps with their counts, then the biggest and the newest, down the left.</summary>
    private void BuildSwitch2Nav()
    {
        _s2Nav.Children.Clear();
        _s2Under.Children.Clear();
        var all = _apps ?? new List<ManagedApp>();
        string? N(int n) => _apps == null ? null : n.ToString("N0");
        foreach (var (id, name, count) in new[] { ("all", "All apps", all.Count), ("desktop", "Desktop programs", all.Count(a => !a.IsStore)), ("store", "Microsoft Store apps", all.Count(a => a.IsStore)) })
        {
            var which = id;
            _s2Nav.Children.Add(Switch2Pages.NavItem(name, N(count), _show == id, () => { _show = which; UiSound.Move(); RebuildFromTop(); }));
        }
        _s2Nav.Children.Add(Switch2Pages.NavLine());
        foreach (var (id, name) in new[] { ("name", "A to Z"), ("size", "Biggest first"), ("date", "Newest first") })
        {
            var which = id;
            _s2Nav.Children.Add(Switch2Pages.NavItem(name, null, _sort == id, () => { _sort = which; RebuildFromTop(); }));
        }
        if (_apps == null) return;
        long total = all.Sum(a => a.Bytes);
        if (total <= 0) return;
        _s2Under.Children.Add(Ui.Text(Size(total), 22, "FgBrush"));
        _s2Under.Children.Add(Ui.Text("used by your apps", 13, "SubtleBrush"));
        // The five biggest as a stacked bar, the rest in gray.
        var bar = new Grid { Height = 10, Margin = new Thickness(0, 10, 0, 6) };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Switch2Settings.Off) });
        var parts = new StackPanel { Orientation = Orientation.Horizontal };
        var top = all.OrderByDescending(a => a.Bytes).Take(5).ToList();
        double[] shades = { 1, 0.8, 0.62, 0.46, 0.32 };
        bar.SizeChanged += (_, e) =>
        {
            parts.Children.Clear();
            for (int i = 0; i < top.Count; i++)
            {
                var piece = new Border { Width = Math.Max(2, e.NewSize.Width * top[i].Bytes / total), Opacity = shades[i], ToolTip = top[i].Name + "  " + Size(top[i].Bytes) };
                piece.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                parts.Children.Add(piece);
            }
        };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = parts });
        _s2Under.Children.Add(bar);
        foreach (var a in top.Take(3))
        {
            var t = Ui.Text(a.Name + "  " + Size(a.Bytes), 13, "SubtleBrush");
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            _s2Under.Children.Add(t);
        }
    }

    public override void OnShown()
    {
        base.OnShown();
        if (_apps == null) _ = LoadAsync();
    }

    public override void OnClosed() => _cts.Cancel();

    private async Task LoadAsync()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _apps = null;
        Rebuild();
        try { _apps = await AppsManager.ListAsync(ct); }
        catch (OperationCanceledException) { return; }
        catch (Exception) { _apps = new(); }
        if (Host.IsTop(this)) Rebuild();
    }

    protected override void Build()
    {
        if (_apps == null) { AddNote("Finding your apps…"); return; }
        var shown = _apps.Where(a => _show == "all" || (_show == "store") == a.IsStore)
                         .Where(a => _search.Length == 0 || a.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase) || a.Publisher.Contains(_search, StringComparison.CurrentCultureIgnoreCase));
        shown = _sort switch
        {
            "size" => shown.OrderByDescending(a => a.Bytes),
            "date" => shown.OrderByDescending(a => a.Installed ?? DateTime.MinValue),
            _ => shown.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        var list = shown.ToList();

        string showName = _show switch { "desktop" => "Desktop programs", "store" => "Microsoft Store apps", _ => "All apps" };
        string sortName = _sort switch { "size" => "Size", "date" => "Install date", _ => "Name" };
        AddRow("Search", _search.Length > 0 ? _search : "Find an app", "\uE721", click: () => _ = SearchAsync());
        if (!_s2) AddRow("Showing", showName, "\uE71C", click: () => Cycle(+1));
        AddRow("Sort by", sortName, "\uE8CB", click: CycleSort);
        long total = list.Sum(a => a.Bytes);
        AddHeader($"{list.Count} apps" + (total > 0 ? $"  ·  {Size(total)}" : ""));
        if (list.Count == 0) { AddNote(_search.Length > 0 ? "No app matches that search." : "No apps found."); return; }

        var pending = new List<(Image View, ManagedApp App)>();
        foreach (var app in list)
        {
            var a = app;
            var parts = new List<string>();
            if (a.Publisher.Length > 0) parts.Add(a.Publisher);
            if (a.Version.Length > 0) parts.Add(a.Version);
            if (a.Installed is DateTime d) parts.Add(d.ToString("d"));
            if (a.IsStore) parts.Add("Microsoft Store");
            var icon = new Image();
            var row = AddRow(a.Name, a.Bytes > 0 ? Size(a.Bytes) : "", a.IsStore ? "\uE719" : null, a.IsStore ? null : icon, () => Host.Push(new AppOptionsScreen(a, () => _ = LoadAsync())),
                string.Join("  ·  ", parts));
            if (!a.IsStore) pending.Add((icon, a));
        }
        // Their icons, a few at a time in the background.
        var ct = _cts.Token;
        _ = Task.Run(() =>
        {
            foreach (var (view, app) in pending)
            {
                if (ct.IsCancellationRequested) return;
                string key = app.Name + app.Version;
                if (!_icons.TryGetValue(key, out var source))
                {
                    try { source = AppsManager.IconFile(app) is string file ? ShellThumb.Get(file, 64) : null; source?.Freeze(); }
                    catch (Exception) { source = null; }
                    _icons[key] = source;
                }
                if (source != null) Dispatcher.BeginInvoke(() => view.Source = source);
            }
        }, ct);
    }

    private static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / 1073741824.0:0.0} GB" : bytes >= 1L << 20 ? $"{bytes / 1048576.0:0} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    private async Task SearchAsync()
    {
        var text = await Host.PromptAsync("Search apps", "Part of the app's name or publisher", _search, "Search");
        if (text == null) return;
        _search = text.Trim();
        RebuildFromTop();
    }

    private void Cycle(int step)
    {
        var order = new[] { "all", "desktop", "store" };
        _show = order[(Array.IndexOf(order, _show) + step + order.Length) % order.Length];
        UiSound.Move();
        if (_s2) RebuildFromTop(); else Rebuild();
    }

    private void CycleSort()
    {
        _sort = _sort switch { "name" => "size", "size" => "date", _ => "name" };
        Rebuild();
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.X: _ = SearchAsync(); return true;
            case NavAction.Y: CycleSort(); return true;
            case NavAction.RB or NavAction.RT: Cycle(+1); return true;
            case NavAction.LB or NavAction.LT: Cycle(-1); return true;
        }
        return false;
    }
}

/// <summary>What can be done with one app: uninstall, modify, repair (Windows Installer programs), reset (Store apps), open its folder.</summary>
public sealed class AppOptionsScreen : ListScreen
{
    private readonly ManagedApp _app;
    private readonly Action _changed;
    private string? _busy;

    private readonly bool _s2 = Labs.On("s2apps");

    public AppOptionsScreen(ManagedApp app, Action changed) : base(app.Name, "{Select}  Select      {Back}  Back")
    {
        _app = app;
        _changed = changed;
        if (!_s2) return;
        UseSwitch2Chrome(app.Name.Length > 40 ? app.Name[..40] + "\u2026" : app.Name, app.IsStore ? "\uE719" : "\uE71D");
        Rows.Width = double.NaN;
        Rows.MaxWidth = 1100;
        Rows.Margin = new Thickness(18, 0, 40, 24);
    }

    public override void OnResumed()
    {
        if (_s2) RefreshSwitch2Colors();
        base.OnResumed();
    }

    protected override void AfterBuild()
    {
        if (!_s2) return;
        Switch2Settings.Restyle(Rows);
        // The app's own card at the top: its icon, name, publisher and size.
        var card = new Grid { Margin = new Thickness(14, 18, 14, 4) };
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 22, 0), Background = new SolidColorBrush(Switch2Settings.Off) };
        var img = new Image { Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.Child = img;
        if (!_app.IsStore)
            _ = Task.Run(() => { try { var f = AppsManager.IconFile(_app); var src = f != null ? ShellThumb.Get(f, 96) : null; src?.Freeze(); return src; } catch (Exception) { return null; } })
                .ContinueWith(t => Dispatcher.BeginInvoke(() => img.Source = t.Result));
        else icon.Child = new TextBlock { Text = "\uE719", FontFamily = Ui.IconFont, FontSize = 36, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Switch2Settings.Text) };
        card.Children.Add(icon);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var n = Ui.Text(_app.Name, 26, "FgBrush");
        n.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(n);
        var bits = new List<string>();
        if (_app.Publisher.Length > 0) bits.Add(_app.Publisher);
        if (_app.Bytes > 0) bits.Add(_app.Bytes >= 1L << 30 ? $"{_app.Bytes / 1073741824.0:0.0} GB" : $"{_app.Bytes / 1048576.0:0} MB");
        bits.Add(_app.IsStore ? "Microsoft Store app" : "Desktop program");
        words.Children.Add(Ui.Text(string.Join("  \u00B7  ", bits), 16, "SubtleBrush"));
        Grid.SetColumn(words, 1);
        card.Children.Add(words);
        Rows.Children.Insert(0, card);
    }

    protected override void Build()
    {
        var a = _app;
        AddHeader("About");
        if (a.Publisher.Length > 0) AddRow("Publisher", a.Publisher, "\uE77B");
        if (a.Version.Length > 0) AddRow("Version", a.Version, "\uE946");
        if (a.Bytes > 0) AddRow("Size", a.Bytes >= 1L << 30 ? $"{a.Bytes / 1073741824.0:0.0} GB" : $"{a.Bytes / 1048576.0:0} MB", "\uEDA2");
        if (a.Installed is DateTime d) AddRow("Installed", d.ToString("D"), "\uE787");
        AddRow("Kind", a.IsStore ? "Microsoft Store app" : a.IsMsi ? "Desktop program (Windows Installer)" : "Desktop program", a.IsStore ? "\uE719" : "\uE7F8");
        if (_busy != null) AddNote(_busy);

        AddHeader("Actions");
        if (a.Location.Length > 0 && Directory.Exists(a.Location))
            AddRow("Open its folder", glyph: "\uE838", click: () => ProcessUtil.Open("explorer.exe", "\"" + a.Location + "\""));
        if (a.CanModify) AddRow("Modify", glyph: "\uE70F", click: () => _ = DoAsync("Opening its setup…", () => AppsManager.ModifyAsync(a)),
            subtitle: "Opens the program's own setup, to add or remove parts of it");
        if (a.CanRepair) AddRow("Repair", glyph: "\uE90F", click: () => _ = DoAsync("Repairing…", () => AppsManager.RepairAsync(a)),
            subtitle: "Puts back missing or damaged files and settings; your data is kept");
        if (a.IsStore)
        {
            AddRow("Reset", glyph: "\uE777", click: () => _ = ResetAsync(),
                subtitle: "Back to how it was when installed. The app's data on this device is deleted");
            AddRow("More options in Windows Settings", glyph: "\uE713", click: () => ProcessUtil.Open("ms-settings:appsfeatures"));
        }
        AddRow("Uninstall", glyph: "\uE74D", click: () => _ = UninstallAsync(), subtitle: a.IsStore ? "Removes the app for this user" : "Runs the program's own uninstaller");
    }

    private async Task UninstallAsync()
    {
        if (await Host.AskAsync("Uninstall " + _app.Name + "?",
                _app.IsStore ? "The app and its data on this device will be removed." : "Its uninstaller will open. Windows may ask for permission first.",
                "Uninstall", "Cancel") != 0) return;
        if (await DoAsync("Uninstalling…", () => AppsManager.UninstallAsync(_app)))
        {
            _changed();
            Host.Pop(this);
        }
    }

    private async Task ResetAsync()
    {
        if (await Host.AskAsync("Reset " + _app.Name + "?", "Its settings, sign-ins and saved data on this device are deleted. The app stays installed.", "Reset", "Cancel") != 0) return;
        await DoAsync("Resetting…", () => AppsManager.ResetAsync(_app));
    }

    /// <summary>Runs an action with a note while it works; tells you if it failed. True when it finished without a problem.</summary>
    private async Task<bool> DoAsync(string doing, Func<Task<string?>> work)
    {
        _busy = doing;
        Rebuild();
        string? problem = null;
        await Host.RunBusyAsync(doing, async () => problem = await work());
        _busy = null;
        if (Host.IsTop(this)) Rebuild();
        if (problem != null && problem != "Cancelled.")
        {
            await Host.AskAsync("That didn't work", problem, "OK");
            return false;
        }
        return problem == null;
    }
}
