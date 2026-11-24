using System.IO;
using System.Windows;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Retro Lounge's RetroAchievements sign-in and DAT checks, shared by Setup and the game page.</summary>
public static class RetroAccount
{
    /// <summary>
    /// Signs in to RetroAchievements with your user name and web API key (retroachievements.org > Settings > Keys),
    /// then offers to turn achievements on in RetroArch.
    /// </summary>
    public static async Task SignInAsync(MainWindow host)
    {
        var s = host.Settings;
        if (s.RaUser.Length > 0)
        {
            int pick = await host.AskAsync("RetroAchievements", "Signed in as " + s.RaUser + ".", "Turn achievements on in RetroArch", "Sign out", "Cancel");
            if (pick == 0) EnableInRetroArch(host, s.RaUser);
            if (pick == 1) { s.RaUser = ""; s.RaApiKey = ""; s.Save(); }
            return;
        }
        var user = (await host.PromptAsync("RetroAchievements user name", "The name you sign in to retroachievements.org with.", "", "Next"))?.Trim();
        if (string.IsNullOrEmpty(user)) return;
        var key = (await host.PromptAsync("Web API key", "On retroachievements.org: Settings > Keys > Web API Key. SquareDeck only reads your progress with it.", "", "Sign in"))?.Trim();
        if (string.IsNullOrEmpty(key)) return;
        string? error = null;
        await host.RunBusyAsync("Signing in to RetroAchievements…", async () => error = await RetroExtras.CheckSignInAsync(user, key));
        if (error != null) { await host.AskAsync("Couldn't sign in", error, "OK"); return; }
        s.RaUser = user;
        s.RaApiKey = key;
        s.Save();
        host.ShowToast("RETRO LOUNGE", "Signed in to RetroAchievements", user, "", "info");
        if (RetroLounge.InstalledFor("retroarch") != null &&
            await host.AskAsync("Earn achievements in RetroArch too?", "SquareDeck turns them on in RetroArch for " + user + ". RetroArch asks for your password once (Settings > Achievements).", "Turn them on", "Not now") == 0)
            EnableInRetroArch(host, user);
    }

    private static void EnableInRetroArch(MainWindow host, string user) =>
        host.ShowToast("RETRO LOUNGE", RetroExtras.EnableInRetroArch(user) ? "Achievements are on in RetroArch" : "RetroArch isn't installed",
            "Enter your password once in RetroArch: Settings > Achievements.", "", "info");

    /// <summary>Checks the ROMs against the DAT files (choosing their folder first if there isn't one yet).</summary>
    public static async Task VerifyAsync(MainWindow host, bool chooseFolder = false)
    {
        var s = host.Settings;
        if (chooseFolder || s.RetroDatFolder.Length == 0 || !Directory.Exists(s.RetroDatFolder))
        {
            int pick = await host.AskAsync("DAT files", "Download the DAT files for your consoles from No-Intro (datomatic.no-intro.org) or Redump, put them in a folder, and choose it. SquareDeck reads them; nothing is uploaded.", "Choose the folder…", "Cancel");
            if (pick != 0) return;
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Folder with DAT files" };
            if (dialog.ShowDialog() != true) return;
            s.RetroDatFolder = dialog.FolderName;
            s.Save();
        }
        List<RetroExtras.VerifyResult>? results = null;
        await host.RunCancellableBusyAsync("Checking your ROMs against the DAT files…", async ct => results = await RetroExtras.VerifyAsync(s.RetroDatFolder, null, ct));
        if (results != null) host.Push(new RetroVerifyScreen(results));
    }
}

/// <summary>A game's RetroAchievements: earned first, with points and when.</summary>
public sealed class RetroAchievementsScreen : ListScreen
{
    private readonly RetroExtras.RaProgress _p;

    public RetroAchievementsScreen(RetroExtras.RaProgress progress) : base("RetroAchievements – " + progress.Title) => _p = progress;

    protected override void Build()
    {
        AddNote($"{_p.Earned} of {_p.Total} earned  ·  {_p.PointsEarned} of {_p.Points} points. Earn them by playing in RetroArch with achievements on (Retro Lounge > Setup > RetroAchievements).");
        foreach (var a in _p.Achievements)
            AddRow(a.Title, a.Points + " pts", a.Earned != null ? "" : "",
                subtitle: a.Description + (a.Earned is DateTime d ? "  ·  earned " + d.ToString("MMM d, yyyy") : ""));
        AddRow("Open on retroachievements.org", null, "", click: () => ProcessUtil.Open($"https://retroachievements.org/game/{_p.GameId}"));
    }
}

/// <summary>The results of checking ROMs against DAT files: what's unknown or a duplicate, by console.</summary>
public sealed class RetroVerifyScreen : ListScreen
{
    private readonly List<RetroExtras.VerifyResult> _results;

    public RetroVerifyScreen(List<RetroExtras.VerifyResult> results) : base("ROMs checked") => _results = results;

    protected override void Build()
    {
        int verified = _results.Count(r => r.Verdict == RetroExtras.Verdict.Verified);
        int unknown = _results.Count(r => r.Verdict == RetroExtras.Verdict.Unknown);
        int dupes = _results.Count(r => r.Verdict == RetroExtras.Verdict.Duplicate);
        int skipped = _results.Count(r => r.Verdict == RetroExtras.Verdict.Skipped);
        AddNote($"{verified} verified (a known good copy), {unknown} not in your DAT files, {dupes} duplicates, {skipped} skipped (disc games, folders and .7z files aren't checked).");
        AddNote("Not in your DAT files can mean a bad copy, a hack or translation, or a console whose DAT file isn't in the folder.");

        if (dupes > 0)
        {
            AddHeader("Duplicates");
            foreach (var r in _results.Where(r => r.Verdict == RetroExtras.Verdict.Duplicate))
            {
                var item = r;
                AddRow(Path.GetFileName(r.Path), RetroLounge.SystemOf(r.System)?.Short, "", click: () => Open(item.Path),
                    subtitle: "Same data as " + Path.GetFileName(r.Duplicate));
            }
        }
        foreach (var group in _results.Where(r => r.Verdict == RetroExtras.Verdict.Unknown).GroupBy(r => r.System).OrderBy(g => g.Key))
        {
            AddHeader("Not in your DAT files: " + (RetroLounge.SystemOf(group.Key)?.Name ?? group.Key));
            foreach (var r in group)
            {
                var item = r;
                AddRow(Path.GetFileName(r.Path), null, "", click: () => Open(item.Path), subtitle: Path.GetDirectoryName(r.Path));
            }
        }
        AddRow("Choose another DAT folder", null, "", click: () => { Host.Pop(this); _ = RetroAccount.VerifyAsync(Host, chooseFolder: true); });
    }

    private void Open(string path)
    {
        string? folder = Path.GetDirectoryName(path);
        if (folder != null && Directory.Exists(folder)) Host.Push(new ExplorerScreen(folder));
    }
}
