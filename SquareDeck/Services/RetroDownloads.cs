namespace SquareDeck.Services;

/// <summary>One emulator being downloaded and installed (or updated).</summary>
public sealed class RetroDownload
{
    public required RetroEmulator Emulator { get; init; }
    public required RetroRelease Release { get; init; }
    public bool Update { get; init; }
    public long Done { get; set; }
    public long Total { get; set; }
    public CancellationTokenSource Cancel { get; } = new();
    public double Fraction => Total > 0 ? Math.Clamp(Done / (double)Total, 0, 1) : 0;

    public string Status => Total <= 0 && Done == 0 ? "Starting the download…"
        : Total > 0 && Fraction >= 1 ? "Unpacking and setting it up…"
        : Total > 0 ? $"{(Update ? "Updating to" : "Downloading")} {Release.Version}: {(int)Math.Floor(Fraction * 100)}%  ({SystemStats.Bytes(Done)} of {SystemStats.Bytes(Total)})"
        : $"{(Update ? "Updating to" : "Downloading")} {Release.Version}: {SystemStats.Bytes(Done)}";
}

/// <summary>Retro Lounge's downloads: installs carry on in the background, and updates found are kept for the session.</summary>
public static class RetroDownloads
{
    public static readonly List<RetroDownload> Active = new();
    public static readonly Dictionary<string, RetroRelease> Updates = new();
    public static DateTime LastCheck { get; private set; } = DateTime.MinValue;
    public static bool Checking { get; private set; }

    public static event Action? Changed;
    public static event Action<RetroDownload>? Progressed;

    public static int Badge => Active.Count + Updates.Keys.Count(id => Active.All(d => d.Emulator.Id != id));

    public static RetroDownload? For(string id) => Active.FirstOrDefault(d => d.Emulator.Id == id);

    /// <summary>Starts installing (or returns the download already under way). Call on the interface thread.</summary>
    public static RetroDownload Start(RetroEmulator emu, RetroRelease release, MainWindow host)
    {
        if (For(emu.Id) is { } running) return running;
        var d = new RetroDownload { Emulator = emu, Release = release, Update = RetroLounge.InstalledFor(emu.Id) != null };
        Active.Add(d);
        Changed?.Invoke();
        var progress = new Progress<(long Done, long Total)>(p => { d.Done = p.Done; d.Total = p.Total; Progressed?.Invoke(d); });
        _ = RunAsync(d, progress, host);
        return d;
    }

    /// <summary>Asks for the newest release, then installs it.</summary>
    public static async Task<RetroDownload?> StartNewestAsync(RetroEmulator emu, MainWindow host)
    {
        if (For(emu.Id) is { } running) return running;
        try
        {
            var release = await RetroLounge.ReleaseAsync(emu);
            if (release == null) { host.ShowToast("Retro Lounge", "No Windows download for " + emu.Name, "Its page may have changed. Try again later, or use Locate installed.", "", "warning"); return null; }
            return Start(emu, release, host);
        }
        catch (Exception ex)
        {
            host.ShowToast("Retro Lounge", "Couldn't reach " + emu.Name + "'s downloads", ex.Message.Contains("403") ? "GitHub is limiting requests right now. Try again in a few minutes." : ex.Message, "", "warning");
            return null;
        }
    }

    private static async Task RunAsync(RetroDownload d, IProgress<(long, long)> progress, MainWindow host)
    {
        try
        {
            await RetroLounge.InstallAsync(d.Emulator, d.Release, progress, d.Cancel.Token);
            Updates.Remove(d.Emulator.Id);
            host.ShowToast("Retro Lounge", d.Emulator.Name + (d.Update ? " is updated" : " is ready"),
                d.Update ? "Now on " + d.Release.Version + ". Its settings and saves stayed." : "Its games play straight from Retro Lounge now.", "", "info");
            AllyControl.React("download");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { host.ShowToast("Retro Lounge", "Couldn't install " + d.Emulator.Name, ex.Message, "", "warning"); }
        finally
        {
            Active.Remove(d);
            Changed?.Invoke();
        }
    }

    /// <summary>Checks the installed emulators for newer versions (at most every half hour unless forced).</summary>
    public static async Task CheckAsync(bool force = false)
    {
        if (Checking || (!force && DateTime.Now - LastCheck < TimeSpan.FromMinutes(30))) return;
        Checking = true;
        Changed?.Invoke();
        try
        {
            foreach (var install in RetroLounge.Installed())
            {
                if (install.Located || install.Version.Length == 0 || RetroLounge.Emulator(install.Id) is not { } emu) continue;
                try
                {
                    var release = await RetroLounge.ReleaseAsync(emu);
                    if (release != null && release.Version != install.Version) Updates[install.Id] = release;
                    else Updates.Remove(install.Id);
                }
                catch (Exception ex) when (ex.Message.Contains("403")) { break; }
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
