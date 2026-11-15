using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>The handful of Windows settings you reach for most on a gaming PC.</summary>
public sealed class PcSettingsScreen : ListScreen
{
    private (int PluggedIn, int Battery)? _screenOff, _sleep;
    private List<(string Id, string Name, bool Active)> _plans = new();
    private List<(string Id, string Name, bool IsDefault)> _outputs = new();
    private bool _loaded;

    public PcSettingsScreen() : base("PC settings") { }

    public override void OnShown()
    {
        Rebuild();
        _ = LoadAsync();
    }

    public override void OnResumed() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        _outputs = await Task.Run(Audio.GetOutputs);
        _screenOff = await PowerSettings.GetScreenTimeoutAsync();
        _sleep = await PowerSettings.GetSleepTimeoutAsync();
        _plans = await PowerSettings.GetPlansAsync();
        _loaded = true;
        if (Host.IsTop(this)) Rebuild();
    }

    protected override void Build()
    {
        var display = DisplaySettings.Current();

        bool restartPending = false;
        try { restartPending = WindowsUpdate.RestartPending(); } catch (Exception) { }
        AddHeader("Windows Update");
        AddRow("Restart to finish updating", restartPending ? "Restart pending" : "Not needed", "\uE777",
            click: () => ProcessUtil.Open("ms-settings:windowsupdate"));
        if (restartPending)
            AddNote("Windows is waiting to restart. It can do that on its own outside active hours, which would end a game " +
                    "in progress. Restart from the Power menu when you are between sessions.");

        AddHeader("Display");
        AddRow("Resolution", display?.Resolution ?? "Unknown", "", click: ChooseResolution);
        AddRow("Refresh rate", display == null ? "Unknown" : display.RefreshRate + " Hz", "", click: ChooseRefreshRate);

        AddHeader("Sound");
        var current = _outputs.FirstOrDefault(o => o.IsDefault).Name;
        AddRow("Sound output", current ?? (_loaded ? "No device" : "…"), "", click: ChooseOutput);
        AddRow("Volume, brightness and radios", glyph: "", click: () => Host.Push(new QuickSettingsScreen()));

        AddHeader("Power and sleep");
        if (!_loaded) AddNote("Reading power settings…");
        if (_screenOff is { } screen)
        {
            AddRow("Turn off the screen (plugged in)", PowerSettings.Describe(screen.PluggedIn), "",
                click: () => ChooseTimeout("Turn off the screen when plugged in after", "monitor", onBattery: false));
            AddRow("Turn off the screen (on battery)", PowerSettings.Describe(screen.Battery), "",
                click: () => ChooseTimeout("Turn off the screen on battery after", "monitor", onBattery: true));
        }
        if (_sleep is { } sleep)
        {
            AddRow("Sleep (plugged in)", PowerSettings.Describe(sleep.PluggedIn), "",
                click: () => ChooseTimeout("Sleep when plugged in after", "standby", onBattery: false));
            AddRow("Sleep (on battery)", PowerSettings.Describe(sleep.Battery), "",
                click: () => ChooseTimeout("Sleep on battery after", "standby", onBattery: true));
        }
        if (_plans.Count > 0)
            AddRow("Power plan", _plans.FirstOrDefault(p => p.Active).Name ?? "Unknown", "", click: ChoosePlan);

        AddHeader("Storage");
        AddRow("Format a USB drive or memory card", glyph: "", click: () => Host.Push(new StorageScreen()));

        AddHeader("More in Windows Settings");
        Shortcut("Display (HDR, scaling, night light)", "ms-settings:display");
        Shortcut("Game Mode", "ms-settings:gaming-gamemode");
        Shortcut("Graphics (per-app GPU choice)", "ms-settings:display-advancedgraphics");
        Shortcut("Sound", "ms-settings:sound");
        Shortcut("Controllers and other devices", "ms-settings:devices");
        Shortcut("Date and time", "ms-settings:dateandtime");
        Shortcut("Windows Update", "ms-settings:windowsupdate");
    }

    private void Shortcut(string label, string uri) =>
        AddRow(label, glyph: "", click: () => ProcessUtil.Open(uri));

    // ------------------------------------------------------------ display

    private async void ChooseResolution()
    {
        var current = DisplaySettings.Current();
        if (current == null) return;
        var all = DisplaySettings.All();
        var sizes = all.Select(m => (m.Width, m.Height)).Distinct().Take(12).ToList();
        if (sizes.Count == 0) return;

        int choice = await Host.AskAsync("Resolution", "Now: " + current.Resolution,
            sizes.Select(s => $"{s.Width} × {s.Height}").ToArray());
        if (choice < 0) return;

        var (w, h) = sizes[choice];
        // Keep the current refresh rate if that size supports it, else its fastest.
        var mode = all.FirstOrDefault(m => m.Width == w && m.Height == h && m.RefreshRate == current.RefreshRate)
                   ?? all.First(m => m.Width == w && m.Height == h);
        await TryModeAsync(current, mode);
    }

    private async void ChooseRefreshRate()
    {
        var current = DisplaySettings.Current();
        if (current == null) return;
        var rates = DisplaySettings.All()
            .Where(m => m.Width == current.Width && m.Height == current.Height)
            .Select(m => m.RefreshRate).Distinct().OrderByDescending(r => r).ToList();
        if (rates.Count == 0) return;

        int choice = await Host.AskAsync("Refresh rate", $"Now: {current.RefreshRate} Hz",
            rates.Select(r => r + " Hz").ToArray());
        if (choice < 0) return;
        await TryModeAsync(current, current with { RefreshRate = rates[choice] });
    }

    /// <summary>Switches, then asks to confirm; goes back by itself if nobody answers (blank screen etc.).</summary>
    private async Task TryModeAsync(DisplayMode previous, DisplayMode wanted)
    {
        if (wanted == previous) return;
        if (!DisplaySettings.Apply(wanted, keep: false))
        {
            await Host.AskAsync("Couldn't change the display", "The display didn't accept that setting.", "OK");
            return;
        }

        var dialog = new MenuScreen("Keep this display setting?",
            $"{wanted.Resolution} at {wanted.RefreshRate} Hz. It changes back in 15 seconds unless you keep it.",
            new[] { "Change back", "Keep it" });
        Host.Push(dialog);
        var answered = await Task.WhenAny(dialog.Result, Task.Delay(TimeSpan.FromSeconds(15)));

        bool keep = answered == dialog.Result && dialog.Result.Result == 1;
        if (answered != dialog.Result) Host.Pop(dialog);

        if (keep) DisplaySettings.Apply(wanted, keep: true);
        else DisplaySettings.Apply(previous, keep: false);
        Rebuild();
    }

    // ------------------------------------------------------------ sound

    private async void ChooseOutput()
    {
        var outputs = await Task.Run(Audio.GetOutputs);
        if (outputs.Count == 0)
        {
            await Host.AskAsync("No sound devices", "Windows doesn't report any connected, enabled output.", "OK");
            return;
        }
        int choice = await Host.AskAsync("Sound output", null,
            outputs.Select(o => o.IsDefault ? o.Name + "  ✓" : o.Name).ToArray());
        if (choice < 0) return;

        if (!Audio.SetDefaultOutput(outputs[choice].Id))
            await Host.AskAsync("Couldn't switch the sound output", "Windows refused the change. Try it from Windows' Sound settings.", "OK");
        await LoadAsync();
        UiSound.Select(); // hear it on the new device
    }

    // ------------------------------------------------------------ power

    private async void ChooseTimeout(string title, string what, bool onBattery)
    {
        var choices = PowerSettings.MinuteChoices;
        int choice = await Host.AskAsync(title, null, choices.Select(PowerSettings.Describe).ToArray());
        if (choice < 0) return;
        await PowerSettings.SetAsync(what, onBattery, choices[choice]);
        await LoadAsync();
    }

    private async void ChoosePlan()
    {
        int choice = await Host.AskAsync("Power plan", null, _plans.Select(p => p.Active ? p.Name + "  ✓" : p.Name).ToArray());
        if (choice < 0) return;
        await PowerSettings.SetPlanAsync(_plans[choice].Id);
        await LoadAsync();
    }
}

/// <summary>Format a USB drive or memory card. Only external drives are ever listed.</summary>
public sealed class StorageScreen : ListScreen
{
    private List<ExternalDrive>? _drives;
    private string? _busy;

    public StorageScreen() : base("Format external storage") { }

    public override void OnShown()
    {
        Rebuild();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _drives = await ExternalDrives.ListAsync();
        if (Host.IsTop(this)) Rebuild();
    }

    private static string Size(long bytes) => $"{bytes / 1024d / 1024d / 1024d:0.#} GB";

    protected override void Build()
    {
        AddNote("Formatting erases everything on the drive. Only USB drives and memory cards are listed here; " +
                "the Windows drive and internal disks are never shown.");
        if (_busy != null) { AddNote(_busy); return; }
        if (_drives == null) { AddNote("Looking for external drives…"); return; }

        if (_drives.Count == 0) AddNote("No USB drive or memory card found. Plug one in, then choose Look again.");
        foreach (var d in _drives)
        {
            var drive = d;
            string about = $"{(drive.FileSystem.Length > 0 ? drive.FileSystem : "Unformatted")}, {Size(drive.Size)}";
            AddRow(drive.Title, about, "", click: () => Format(drive));
        }
        AddRow("Look again", glyph: "", click: () => { _drives = null; Rebuild(); _ = LoadAsync(); });
    }

    private async void Format(ExternalDrive drive)
    {
        // "Cancel" is first in both dialogs, so a stray press of the Select button never erases anything.
        var systems = ExternalDrives.FileSystems;
        var labels = new List<string> { "Cancel" };
        labels.AddRange(systems.Select(f => f.Name + "  –  " + f.About));
        int pick = await Host.AskAsync("Format " + drive.Title + " as…",
            $"Now {(drive.FileSystem.Length > 0 ? drive.FileSystem : "unformatted")}, {Size(drive.Size)}, {Size(drive.Size - drive.Free)} used.",
            labels.ToArray());
        if (pick <= 0) return;
        var system = systems[pick - 1];

        int confirm = await Host.AskAsync("Erase everything on " + drive.Title + "?",
            $"All files on this {Size(drive.Size)} drive will be permanently deleted and it will be formatted as {system.Name}. " +
            "This can't be undone. Windows will ask for administrator permission.",
            "Cancel", "Erase and format");
        if (confirm != 1) return;

        _busy = $"Formatting {drive.Letter}: as {system.Name}… Don't unplug the drive.";
        Rebuild();
        var error = await ExternalDrives.FormatAsync(drive, system.Id);
        _busy = null;

        if (error == null) await Host.AskAsync("Drive formatted", $"{drive.Letter}: is now {system.Name} and empty.", "OK");
        else await Host.AskAsync("The drive was not formatted", error, "OK");
        _drives = null;
        Rebuild();
        await LoadAsync();
    }
}
