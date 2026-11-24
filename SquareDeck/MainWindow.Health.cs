using SquareDeck.Services;

namespace SquareDeck;

/// <summary>Device health: the notice a little after start, and the widget fix the health page offers.</summary>
public partial class MainWindow
{
    /// <summary>
    /// Forty seconds after start (once the drivers and the widget are settled), the checks run once; if any needs
    /// fixing that hasn't been set aside, a notice says how many and where to look.
    /// </summary>
    private async Task StartupHealthCheckAsync()
    {
        if (!Settings.HealthStartupCheck) return;
        await Task.Delay(TimeSpan.FromSeconds(40));
        try
        {
            var checks = await Task.Run(() => DeviceHealth.Run(Settings));
            var problems = DeviceHealth.Unseen(Settings, checks).Where(c => c.Level == DeviceHealth.Level.Problem).ToList();
            if (problems.Count == 0) return;
            string title = problems.Count == 1 ? problems[0].Title : $"{problems.Count} things need attention";
            ShowToast("DEVICE HEALTH", title, "Settings > Device health has the fix" + (problems.Count == 1 ? "" : "es") + ".", "", "info");
        }
        catch (Exception) { }
    }

    /// <summary>The health page's "Install it" / "Update it" for the Game Bar widget. Null when it went in.</summary>
    public async Task<string?> InstallWidgetFromHealthAsync()
    {
        if (WidgetInstaller.Find() is not { } package) return "There's no widget package next to SquareDeck.";
        var (ok, message) = await InstallWidgetWithScreenAsync(package, update: WidgetInstaller.InstalledVersion() != null);
        return ok ? null : message;
    }
}

public partial class MainWindow
{
    /// <summary>
    /// What's new: the first start after an update shows that version's changes once (Settings > Updates and data can
    /// turn it off). The changelog comes with SquareDeck; if it's missing, it's read from GitHub.
    /// </summary>
    private async Task ShowWhatsNewAsync()
    {
        var current = Updater.Current;
        if (!Version.TryParse(Settings.WhatsNewSeen, out var seen))
        {
            // A first run (or settings from before this existed): nothing to show, just remember the version.
            Settings.WhatsNewSeen = current.ToString();
            Settings.Save();
            return;
        }
        if (seen >= current) return;
        Settings.WhatsNewSeen = current.ToString();
        Settings.Save();
        if (!Settings.WhatsNewAfterUpdate) return;

        // After start-up has settled: no boot video or first-run setup in front.
        await Task.Delay(TimeSpan.FromSeconds(6));
        for (int i = 0; i < 60 && (_bootVideo != null || Settings.SetupDone != true || _screens.Count > 0); i++) await Task.Delay(1000);
        string? log = null;
        try
        {
            string local = System.IO.Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
            log = System.IO.File.Exists(local) ? await System.IO.File.ReadAllTextAsync(local) : await Updater.ChangelogAsync(CancellationToken.None);
        }
        catch (Exception) { }
        if (log == null) return;
        string notes = Updater.ChangelogSince(log, seen, current);
        if (notes.Length == 0) return;
        Push(new Screens.WhatsNewScreen(seen, current, notes));
    }
}
