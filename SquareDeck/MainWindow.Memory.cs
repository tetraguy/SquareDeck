using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// Lean mode: while a game is being played (SquareDeck behind it), SquareDeck gives back as much memory as it can.
/// The covers are let go of (their files are remembered and read back afterwards), open pages close, caches of
/// pictures are emptied, an idle built-in Spotify player stops, and Windows is told SquareDeck's memory is the first
/// to reclaim. It all comes back within a second or two of SquareDeck being in front again.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _leanDelay = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _leanTrim = new() { Interval = TimeSpan.FromSeconds(45) };
    private bool _lean;

    private void InitLeanMode()
    {
        // Wait a little before going lean: the launch splash finishes, and a quick look back at SquareDeck doesn't
        // throw everything away.
        _leanDelay.Tick += (_, _) => { _leanDelay.Stop(); EnterLean(); };
        // Memory creeps back while SquareDeck runs its timers; hand it back now and then.
        _leanTrim.Tick += (_, _) => ProcessUtil.TrimWorkingSet();
    }

    /// <summary>Called when SquareDeck goes behind a game, or comes back.</summary>
    private void UpdateLean(bool behindGame)
    {
        if (behindGame && Settings.LeanWhilePlaying)
        {
            if (!_lean && !_leanDelay.IsEnabled) _leanDelay.Start();
            return;
        }
        _leanDelay.Stop();
        if (_lean) LeaveLean();
    }

    private void EnterLean()
    {
        if (_lean || !_backgroundMode || !AllGames.Any(g => g.IsRunning)) return;
        _lean = true;
        try
        {
            // Open pages close, unless one is doing something that must finish (a download, a question, the
            // launch splash) or is worth keeping (a video or web page, a cloud game).
            if (Settings.ClosePagesWhilePlaying && _screens.Count > 0 &&
                !_screens.Any(x => x.IsDialog || x is LaunchSplashScreen or UpdateDownloadScreen or BusyScreen or BoostScreen
                                                 or CloudGameScreen or StreamingScreen or BrowserScreen) &&
                RootScreen is { } root)
                Pop(root);

            // Covers on every tile: Home, the library and games not installed.
            foreach (var g in _view.Concat(AllGames).Concat(NotInstalledGames).Distinct()) g.Park();
            _logoCache.Clear();
            _logoKey = null;
            LibraryScreen.ForgetArt();
            FriendsScreen.ForgetPictures();

            // The built-in Spotify player is a whole web browser: stop it if it isn't playing (it starts again when used).
            if (SpotifyPlayer.IsRunning && !SpotifyPlayer.IsPlaying) SpotifyPlayer.Stop();
        }
        catch (Exception ex) { CrashLog.Write("Lean mode", ex); }

        // Compact what's left and give it back to Windows, and tell Windows SquareDeck's memory can go first.
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect();
        SetMemoryPriority(low: true);
        ProcessUtil.TrimWorkingSet();
        _leanTrim.Start();
    }

    private async void LeaveLean()
    {
        _lean = false;
        _leanTrim.Stop();
        SetMemoryPriority(low: false);
        try
        {
            // The visible tiles first, then the rest, a few at a time so SquareDeck stays responsive.
            var games = _view.Concat(AllGames).Concat(NotInstalledGames).Distinct().Where(g => g.Parked).ToList();
            foreach (var batch in games.Chunk(6))
            {
                if (_lean) return; // back in a game already
                await Task.WhenAll(batch.Select(g => g.UnparkAsync()));
            }
            _ = ShowStyleLogoAsync(GameList.SelectedItem as Game);
        }
        catch (Exception ex) { CrashLog.Write("Leaving lean mode", ex); }
    }

    // ---- memory priority: which program's memory Windows takes back first when it runs short

    [StructLayout(LayoutKind.Sequential)] private struct MemoryPriorityInformation { public uint Priority; }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref MemoryPriorityInformation info, int size);
    private const int ProcessMemoryPriority = 0, MemoryPriorityVeryLow = 1, MemoryPriorityNormal = 5;

    private static void SetMemoryPriority(bool low)
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            var info = new MemoryPriorityInformation { Priority = (uint)(low ? MemoryPriorityVeryLow : MemoryPriorityNormal) };
            SetProcessInformation(me.Handle, ProcessMemoryPriority, ref info, Marshal.SizeOf<MemoryPriorityInformation>());
        }
        catch (Exception) { }
    }
}
