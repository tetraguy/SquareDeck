using System.Windows;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Settings > Device health: who's in control of the lights, the TDP and the buttons right now; then anything that
/// gets in the way, most serious first, each with its fix when there is one; then Collect a report.
/// </summary>
public sealed class DeviceHealthScreen : ListScreen
{
    private List<DeviceHealth.Check>? _checks;
    private bool _checking;

    public DeviceHealthScreen() : base("Device health") { }

    public override void OnShown()
    {
        base.OnShown();
        _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;
        Rebuild();
        try
        {
            var host = Host;
            _checks = await Task.Run(() => DeviceHealth.Run(host.Settings, () => host.InstallWidgetFromHealthAsync()));
        }
        catch (Exception) { _checks = new(); }
        finally { _checking = false; }
        Rebuild();
    }

    private static string LevelGlyph(DeviceHealth.Level level) => level switch
    {
        DeviceHealth.Level.Problem => "",  // error
        DeviceHealth.Level.Warning => "",  // warning
        DeviceHealth.Level.Info => "",     // info
        _ => ""                            // check mark
    };

    private static string LevelName(DeviceHealth.Level level) => level switch
    {
        DeviceHealth.Level.Problem => "Needs fixing",
        DeviceHealth.Level.Warning => "Worth a look",
        DeviceHealth.Level.Info => "Good to know",
        _ => "Fine"
    };

    protected override void Build()
    {
        var s = Host.Settings;

        AddHeader("Who's in control");
        foreach (var owner in DeviceHealth.Owners(s))
            AddRow(owner.Area, owner.Who, owner.Glyph, subtitle: owner.Detail);

        AddHeader("Checks");
        if (_checks == null || _checking) AddNote("Checking…");
        else
        {
            var shown = _checks.Where(c => !s.HealthDismissed.Contains(c.Id)).ToList();
            if (shown.Count == 0) AddRow("Everything looks right", null, "", subtitle: "Nothing is getting in SquareDeck's way");
            foreach (var check in shown)
            {
                var c = check;
                AddRow(c.Title, c.FixLabel ?? LevelName(c.Level), LevelGlyph(c.Level), subtitle: c.Area + ": " + c.Detail, click: () => _ = ActAsync(c));
            }
            int hidden = _checks.Count(c => s.HealthDismissed.Contains(c.Id));
            if (hidden > 0)
                AddRow("Show set-aside checks", hidden + (hidden == 1 ? " set aside" : " set aside"), "", click: () =>
                {
                    s.HealthDismissed.Clear();
                    s.Save();
                    Rebuild();
                }, subtitle: "Checks you asked not to be warned about again");
        }
        AddRow("Check again", null, "", click: () => _ = CheckAsync());

        AddHeader("Help");
        AddRow("Collect a report", null, "", click: () => _ = ReportAsync(),
            subtitle: "SquareDeck's logs, version, device and these checks in one zip (without your keys or passwords), for a bug report");
        AddRow("Notice when something needs attention", s.HealthStartupCheck ? "On" : "Off", "", click: () =>
        {
            s.HealthStartupCheck = !s.HealthStartupCheck;
            s.Save();
            Rebuild();
        }, subtitle: "A little after SquareDeck starts, if a check needs fixing");
    }

    private async Task ActAsync(DeviceHealth.Check c)
    {
        var options = new List<string>();
        if (c.Fix != null) options.Add(c.FixLabel ?? "Fix it");
        options.Add("Don't warn me about this");
        options.Add("Cancel");
        int choice = await Host.AskAsync(c.Title, c.Detail, options.ToArray());
        if (choice < 0) return;
        string picked = options[choice];
        if (picked == "Cancel") return;
        if (picked == "Don't warn me about this")
        {
            if (!Host.Settings.HealthDismissed.Contains(c.Id)) Host.Settings.HealthDismissed.Add(c.Id);
            Host.Settings.Save();
            Rebuild();
            return;
        }
        string? error;
        try { error = await c.Fix!(); }
        catch (Exception ex) { error = ex.Message; }
        if (error != null) await Host.AskAsync(c.Title, error, "OK");
        else Host.ShowToast("DEVICE HEALTH", "Done", c.Title, "", "info", remember: false);
        await Task.Delay(600);
        await CheckAsync();
    }

    private async Task ReportAsync()
    {
        try
        {
            string path = "";
            await Host.RunBusyAsync("Collecting a report…", async () => path = await Diagnostics.CollectAsync(Host.Settings));
            int pick = await Host.AskAsync("Report ready", "Saved as " + System.IO.Path.GetFileName(path) + " in " + System.IO.Path.GetDirectoryName(path) +
                ". Keys, tokens and passwords were left out.", "Show it", "OK");
            if (pick == 0) ProcessUtil.Open("explorer.exe", "/select,\"" + path + "\"");
        }
        catch (Exception ex) { await Host.AskAsync("Couldn't make the report", ex.Message, "OK"); }
    }
}
