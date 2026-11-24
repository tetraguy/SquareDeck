using System.IO;

namespace SquareDeck.Services;

/// <summary>One recomp being downloaded and installed (or updated).</summary>
public sealed class RecompDownload
{
    public required RecompGame Game { get; init; }
    public required RecompRelease Release { get; init; }
    public bool Update { get; init; }
    public long Done { get; set; }
    public long Total { get; set; }
    public CancellationTokenSource Cancel { get; } = new();
    public double Fraction => Total > 0 ? Math.Clamp(Done / (double)Total, 0, 1) : 0;

    /// <summary>What it's doing, for a line under its name.</summary>
    public string Status => Total <= 0 && Done == 0 ? "Starting the download…"
        : Fraction >= 1 ? "Unpacking…"
        : Total > 0 ? $"{(Update ? "Updating to" : "Downloading")} {Release.Tag}: {(int)Math.Floor(Fraction * 100)}%  ({SystemStats.Bytes(Done)} of {SystemStats.Bytes(Total)})"
        : $"{(Update ? "Updating to" : "Downloading")} {Release.Tag}: {SystemStats.Bytes(Done)}";
}

/// <summary>
/// Recomp Zone's downloads (Recomp Zone > Downloads): installs carry on in the background when you leave a game's
/// page, and the updates found for installed recomps are kept here for the session.
/// </summary>
public static class RecompDownloads
{
    public static readonly List<RecompDownload> Active = new();

    /// <summary>Installed recomps with a newer release, by recomp id.</summary>
    public static readonly Dictionary<string, RecompRelease> Updates = new();

    public static DateTime LastCheck { get; private set; } = DateTime.MinValue;
    public static bool Checking { get; private set; }

    /// <summary>A download started or finished, or the updates changed.</summary>
    public static event Action? Changed;
    /// <summary>A download moved along (on the interface thread).</summary>
    public static event Action<RecompDownload>? Progressed;

    /// <summary>The number on Downloads in the sidebar: what's downloading and what has an update.</summary>
    public static int Badge => Active.Count + Updates.Keys.Count(id => Active.All(d => d.Game.Id != id));

    public static RecompDownload? For(string id) => Active.FirstOrDefault(d => d.Game.Id == id);

    /// <summary>Starts installing a release (or returns the download already under way). Call on the interface thread.</summary>
    public static RecompDownload Start(RecompGame game, RecompRelease release, MainWindow host)
    {
        if (For(game.Id) is { } running) return running;
        var d = new RecompDownload { Game = game, Release = release, Update = RecompZone.InstalledFor(game.Id) != null };
        Active.Add(d);
        Changed?.Invoke();
        var progress = new Progress<(long Done, long Total)>(p => { d.Done = p.Done; d.Total = p.Total; Progressed?.Invoke(d); });
        _ = RunAsync(d, progress, host);
        return d;
    }

    private static async Task RunAsync(RecompDownload d, IProgress<(long, long)> progress, MainWindow host)
    {
        try
        {
            await RecompZone.InstallAsync(d.Game, d.Release, progress, d.Cancel.Token);
            Updates.Remove(d.Game.Id);
            host.ShowToast("Recomp Zone", d.Game.Name + (d.Update ? " is updated" : " is installed"),
                d.Update ? "Now on " + d.Release.Tag + ". Your saves and settings stayed." : "Press Play to start it. It asks for your copy of the game the first time.", "", "info");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            host.ShowToast("Recomp Zone", "Couldn't install " + d.Game.Name, ex.Message, "", "warning");
        }
        finally
        {
            Active.Remove(d);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Asks GitHub for the newest release of each installed recomp (at most every half hour unless forced; GitHub
    /// allows about 60 questions an hour without an account). Located copies aren't SquareDeck's to update.
    /// </summary>
    public static async Task CheckAsync(bool force = false)
    {
        if (Checking || (!force && DateTime.Now - LastCheck < TimeSpan.FromMinutes(30))) return;
        Checking = true;
        Changed?.Invoke();
        try
        {
            foreach (var install in RecompZone.Installed())
            {
                if (install.Located || install.Tag.Length == 0 || !File.Exists(install.Program)) continue;
                var game = RecompZone.Games.FirstOrDefault(g => g.Id == install.Id);
                if (game == null || !game.CanInstall) continue;
                try
                {
                    var release = await RecompZone.ReleaseAsync(game);
                    if (release != null && release.Tag != install.Tag) Updates[install.Id] = release;
                    else Updates.Remove(install.Id);
                }
                catch (Exception ex) when (ex.Message.Contains("403")) { break; } // GitHub is limiting requests: try later
                catch (Exception) { }
            }
            LastCheck = DateTime.Now;
        }
        finally
        {
            Checking = false;
            Changed?.Invoke();
        }
    }
}
