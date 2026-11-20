using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Adrenaline Hook, inside SquareDeck (Settings > Experimental): add games to AMD Software: Adrenalin Edition, set their
/// Adrenalin switches, repair them after updates, keep backups, and send the Radeon overlay shortcuts. Rebuilt from
/// Adrenaline Hook by tetraguy (github.com/tetraguy/Adrenaline-Hook), for the controller and SquareDeck's theme.
/// </summary>
public sealed class AdrenalineHookScreen : ListScreen
{
    public AdrenalineHookScreen() : base("Adrenaline Hook") { }

    public override void OnResumed() { base.OnResumed(); Rebuild(); }

    private void AfterChange(string what)
    {
        string more = AdrenalineHook.ClosedForChange ? "AMD Software was closed to save it. Open it from here." : "";
        Host.ShowToast("ADRENALINE HOOK", what, more, "", "info");
        Rebuild();
    }

    protected override void Build()
    {
        var s = Host.Settings;
        var hooked = AdrenalineHook.Hooked();
        int missing = hooked.Count(h => h.Missing);

        AddHeader("AMD Software");
        string version = AdrenalineHook.SoftwareVersion;
        AddRow("AMD Software: Adrenalin Edition", AdrenalineHook.SoftwareRunning ? "Open" : AdrenalineHook.SoftwarePath != null ? "Closed" : "Not found", "",
            click: () =>
            {
                if (!AdrenalineHook.OpenSoftware())
                    _ = Host.AskAsync("AMD Software", "AMD Software wasn't found. Get it from amd.com (AMD Software: Adrenalin Edition).", "OK");
            },
            subtitle: AdrenalineHook.GraphicsName + (version.Length > 0 ? "  ·  version " + version : "") + "  ·  Select to open it");

        AddHeader("Hook games");
        AddRow("Xbox and Microsoft Store games", null, "", click: () => Host.Push(new AdrenalinePickScreen("store")),
            subtitle: "Game Pass and Store games AMD Software can't add by itself");
        AddRow("Your SquareDeck library", null, "", click: () => Host.Push(new AdrenalinePickScreen("library")),
            subtitle: "Games SquareDeck knows, with the program it found for each");
        AddRow("Installed programs", null, "", click: () => Host.Push(new AdrenalinePickScreen("installed")),
            subtitle: "Anything in Windows' list of installed programs");
        AddRow("Choose a program…", null, "", click: ChooseProgram, subtitle: "Any .exe, from a file picker");
        AddRow("Add new Xbox games by themselves", OnOff(s.AdrenalineAutoHook), "", click: () =>
        {
            s.AdrenalineAutoHook = !s.AdrenalineAutoHook;
            s.Save();
            if (s.AdrenalineAutoHook) _ = AutoHookNowAsync();
            Rebuild();
        }, subtitle: "When SquareDeck starts, Game Pass games you've installed since are added");

        AddHeader("Hooked games");
        AddRow("Games in AMD Software", hooked.Count.ToString(), "", click: () => Host.Push(new AdrenalineHookedScreen()),
            subtitle: "Adrenalin switches for each game: Radeon Super Resolution, sharpness, Fluid Motion Frames, FreeSync");
        AddRow("Repair moved games", missing > 0 ? missing + " missing" : "All found", "", click: () => _ = RepairAsync(),
            subtitle: "Updates move Xbox games to a new folder; this finds each one again");

        AddHeader("Radeon overlays");
        AddNote("AMD Software's default shortcuts. They're also actions for the handheld buttons and the quick menu. If you changed them in AMD Software, these won't match.");
        foreach (var sc in AdrenalineHook.Shortcuts)
        {
            var id = sc.Id;
            AddRow(sc.Name, null, "", click: () => AdrenalineHook.Send(id), subtitle: sc.About);
        }

        AddHeader("Backups");
        var backups = AdrenalineHook.Backups();
        AddRow("Back up the game list", null, "", click: () =>
        {
            var error = AdrenalineHook.BackUpNow();
            Host.ShowToast("ADRENALINE HOOK", error == null ? "Backed up" : "Couldn't back up", error ?? "", "", "info");
            Rebuild();
        }, subtitle: "A copy is also made before every change (the 15 newest are kept)");
        AddRow("Restore a backup", backups.Count.ToString(), "", click: () => _ = RestoreAsync(backups));
        AddRow("Start over", null, "", click: () => _ = StartOverAsync(),
            subtitle: "Empties AMD Software's game list; it finds its games again. A backup is kept first");

        AddNote("Based on Adrenaline Hook by tetraguy (github.com/tetraguy/Adrenaline-Hook). AMD Software is closed while its list is changed, " +
                "then you can open it again. Experimental: AMD can change how its game list works in a new version.");
    }

    private static string OnOff(bool on) => on ? "On" : "Off";

    private void ChooseProgram()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a game's program", Filter = "Programs (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        string name = System.Diagnostics.FileVersionInfo.GetVersionInfo(dialog.FileName).ProductName is { Length: > 0 } product
            ? product : System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        var (added, skipped) = AdrenalineHook.Add(new[] { new HookCandidate(name, dialog.FileName, dialog.FileName, "Manual") });
        AfterChange(added > 0 ? name + " added" : skipped > 0 ? name + " is already in AMD Software" : "Nothing added");
    }

    private async Task AutoHookNowAsync()
    {
        int added = await AdrenalineHook.AutoHookAsync();
        if (added > 0) AfterChange(added + (added == 1 ? " game added" : " games added"));
    }

    private async Task RepairAsync()
    {
        Host.ShowToast("ADRENALINE HOOK", "Looking for moved games…", "", "", "info");
        int fixedCount = await AdrenalineHook.RepairAsync();
        AfterChange(fixedCount > 0 ? fixedCount + (fixedCount == 1 ? " game repaired" : " games repaired") : "Nothing to repair");
    }

    private async Task RestoreAsync(List<(string Path, DateTime At, int Games)> backups)
    {
        if (backups.Count == 0) { await Host.AskAsync("Restore a backup", "There are no backups yet.", "OK"); return; }
        var list = backups.Take(15).ToList();
        int choice = await Host.AskAsync("Restore a backup", "Your list now is backed up first.",
            list.Select(b => $"{b.At:g}  ({b.Games} games)").ToArray());
        if (choice < 0) return;
        var error = AdrenalineHook.Restore(list[choice].Path);
        AfterChange(error == null ? "Restored" : "Couldn't restore: " + error);
    }

    private async Task StartOverAsync()
    {
        if (await Host.AskAsync("Start over?", "AMD Software's game list is emptied (a backup is kept). It finds the games it knows again, but hooked games have to be added again.",
                "Start over", "Cancel") != 0) return;
        var error = AdrenalineHook.StartOver();
        AfterChange(error == null ? "The game list was emptied" : "Couldn't: " + error);
    }
}

/// <summary>Found games to pick from: select any number, then hook them in one go. Games already in AMD Software are marked.</summary>
public sealed class AdrenalinePickScreen : ListScreen
{
    private readonly string _source;
    private List<HookCandidate>? _found;
    private HashSet<string> _hooked = new(StringComparer.OrdinalIgnoreCase);
    private bool _loading;

    public AdrenalinePickScreen(string source) : base(source switch
    {
        "store" => "Xbox and Microsoft Store games",
        "library" => "From your library",
        _ => "Installed programs"
    })
    {
        _source = source;
    }

    public override void OnShown()
    {
        base.OnShown();
        if (_found == null && !_loading) _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        Rebuild();
        try
        {
            _hooked = AdrenalineHook.HookedKeys();
            _found = _source switch
            {
                "store" => await AdrenalineHook.ScanStoreAsync(),
                "library" => await AdrenalineHook.ScanLibraryAsync(Host.AllGames),
                _ => await AdrenalineHook.ScanInstalledAsync()
            };
        }
        catch (Exception) { _found = new(); }
        _loading = false;
        Rebuild();
    }

    private bool IsHooked(HookCandidate c) => _hooked.Contains(c.Name) || _hooked.Contains(c.ExePath);

    protected override void Build()
    {
        if (_loading || _found == null)
        {
            AddNote("Looking for games…");
            AddRow("Searching", null, "", click: () => { });
            return;
        }
        if (_found.Count == 0)
        {
            AddNote("Nothing was found here.");
            AddRow("Back", null, "", click: () => Host.Pop(this));
            return;
        }
        int picked = _found.Count(f => f.Selected);
        AddRow(picked > 0 ? $"Hook {picked} selected" : "Select games below, then hook them", null, "", click: Hook,
            subtitle: "AMD Software is closed while its list is changed");
        AddRow("Select every new one", null, "", click: () =>
        {
            bool all = _found.Where(f => !IsHooked(f)).All(f => f.Selected);
            foreach (var f in _found.Where(f => !IsHooked(f))) f.Selected = !all;
            Rebuild();
        });

        string? group = null;
        foreach (var c in _found)
        {
            string g = c.Source switch { "Xbox" => "Xbox and Game Pass games", "Store" => "Other Microsoft Store apps", "Library" => "Games", _ => "Programs" };
            if (g != group) { AddHeader(g); group = g; }
            var item = c;
            bool hooked = IsHooked(c);
            var row = AddRow(c.Name, hooked ? "In AMD Software" : c.Selected ? "✓ Selected" : null, hooked ? "" : c.Selected ? "" : "",
                click: () =>
                {
                    if (IsHooked(item)) return;
                    item.Selected = !item.Selected;
                    Rebuild();
                }, subtitle: System.IO.Path.GetFileName(c.ExePath) + "  ·  " + System.IO.Path.GetDirectoryName(c.ExePath));
            if (hooked) row.Opacity = 0.6;
        }
    }

    private void Hook()
    {
        var picked = _found?.Where(f => f.Selected && !IsHooked(f)).ToList() ?? new();
        if (picked.Count == 0) return;
        var (added, skipped) = AdrenalineHook.Add(picked);
        foreach (var p in picked) p.Selected = false;
        _hooked = AdrenalineHook.HookedKeys();
        Host.ShowToast("ADRENALINE HOOK", $"{added} added" + (skipped > 0 ? $", {skipped} already there" : ""),
            AdrenalineHook.ClosedForChange ? "AMD Software was closed to save it" : "", "", "info");
        Rebuild();
    }
}

/// <summary>The games in AMD Software's list. Each opens its Adrenalin switches.</summary>
public sealed class AdrenalineHookedScreen : ListScreen
{
    public AdrenalineHookedScreen() : base("Games in AMD Software") { }

    public override void OnResumed() { base.OnResumed(); Rebuild(); }

    protected override void Build()
    {
        var games = AdrenalineHook.Hooked();
        if (games.Count == 0)
        {
            AddNote("AMD Software's game list is empty, or wasn't found.");
            AddRow("Back", null, "", click: () => Host.Pop(this));
            return;
        }
        foreach (var g in games)
        {
            var game = g;
            // The game's own icon (its Store logo, or its program's icon), loaded after the list shows.
            var icon = new System.Windows.Controls.Image { Width = 40, Height = 40, Stretch = System.Windows.Media.Stretch.Uniform };
            AddRow(g.Title, g.Missing ? "Missing" : g.SteamId > 0 ? "Steam" : g.Manual ? "Hooked" : null, null, icon: icon,
                click: () => Host.Push(new AdrenalineGameScreen(game.Title, game.ExePath, game.Guid)),
                subtitle: (g.PlayedMinutes > 0 ? AdrenalineGameScreen.Played(g.PlayedMinutes) + "  \u00B7  " : "") + (g.ExePath.Length > 0 ? g.ExePath : "Added by AMD Software"));
            if (g.IconPath is string path) AdrenalineUi.LoadIcon(icon, path);
        }
    }
}

/// <summary>One game's Adrenalin switches as stored in AMD Software's list, and removing it.</summary>
public sealed class AdrenalineGameScreen : ListScreen
{
    private readonly string _title, _exe, _key;

    public AdrenalineGameScreen(string title, string exe, string? guid = null) : base(title)
    {
        _title = title;
        _exe = exe;
        _key = string.IsNullOrEmpty(guid) ? title : guid;
    }

    public static string Played(int minutes) => minutes >= 60 ? $"{minutes / 60} h played" : $"{minutes} min played";

    private void Change(Func<AdrenalineHook.GameSwitches, AdrenalineHook.GameSwitches> edit)
    {
        var now = AdrenalineHook.Switches(_key);
        if (now == null) return;
        bool ok = AdrenalineHook.SetSwitches(_key, edit(now));
        Host.ShowToast("ADRENALINE HOOK", ok ? "Saved" : "Couldn't save", AdrenalineHook.ClosedForChange ? "AMD Software was closed to save it" : "", "", "info");
        Rebuild();
    }

    protected override void Build()
    {
        var sw = AdrenalineHook.Switches(_key);
        if (sw == null)
        {
            AddNote("This game isn't in AMD Software's list any more.");
            AddRow("Back", null, "", click: () => Host.Pop(this));
            return;
        }
        static string OnOff(bool on) => on ? "On" : "Off";

        AddHeader("Adrenalin switches");
        AddRow("Radeon Super Resolution", OnOff(sw.Upscaling), "", click: () => Change(x => x with { Upscaling = !x.Upscaling }),
            subtitle: "AMD's upscaling for games run below the screen's resolution");
        AddRow("Sharpness", sw.Sharpness + "%", "", click: async () =>
        {
            var dialog = new SliderDialog("Sharpness – " + _title, "How much Radeon Super Resolution sharpens the picture.", "Sharpness", "",
                sw.Sharpness, null, 0, 100, v => v + "%");
            Host.Push(dialog);
            int value = await dialog.Result;
            Change(x => x with { Sharpness = value });
        });
        AddRow("Fluid Motion Frames", OnOff(sw.FrameGen), "", click: () => Change(x => x with { FrameGen = !x.FrameGen }),
            subtitle: "AMD's frame generation, for smoother motion");
        if (sw.FrameGen)
            AddRow("Frame generation mode", sw.FrameGenMode == 1 ? "Performance" : "Quality", "",
                click: () => Change(x => x with { FrameGenMode = x.FrameGenMode == 1 ? 0 : 1 }));
        AddRow("Force FreeSync", OnOff(sw.FreeSync), "", click: () => Change(x => x with { FreeSync = !x.FreeSync }));

        AddHeader("In AMD Software");
        AddRow("Favorite", OnOff(sw.Favorite), "", click: () => Change(x => x with { Favorite = !x.Favorite }));
        AddRow("Hidden", OnOff(sw.Hidden), "", click: () => Change(x => x with { Hidden = !x.Hidden }));
        if (_exe.Length > 0)
            AddRow("Open its folder", null, "", click: () => ProcessUtil.Open("explorer.exe", "/select,\"" + _exe + "\""), subtitle: _exe);
        AddRow("Remove from AMD Software", null, "", click: async () =>
        {
            if (await Host.AskAsync("Remove " + _title + "?", "It's taken out of AMD Software's game list (a backup is kept).", "Remove", "Cancel") != 0) return;
            AdrenalineHook.Remove(new[] { _key });
            Host.Pop(this);
        });
        AddNote("These switches are stored with the game in AMD Software's list, which AMD Software reads when it opens. " +
                "Whether each one takes effect depends on your graphics and AMD Software's version; AMD Software can also change them itself.");
    }
}

/// <summary>Shared by the Adrenaline pages and game profiles.</summary>
internal static class AdrenalineUi
{
    /// <summary>Loads a file's picture (a Store logo, or a program's icon) into an Image without holding up the page.</summary>
    public static void LoadIcon(System.Windows.Controls.Image view, string path)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var image = ShellThumb.Get(path, 64);
                if (image is { IsFrozen: false, CanFreeze: true }) image.Freeze();
                return image;
            }
            catch (Exception) { return null; }
        }).ContinueWith(t => { if (t.Result != null) view.Source = t.Result; }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}
