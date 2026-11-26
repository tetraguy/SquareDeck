using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// Lean mode: while a game is being played (SquareDeck behind it), or after a minute with SquareDeck out of sight
/// behind another app or minimized, SquareDeck gives back as much memory as it can.
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

    /// <summary>Called when SquareDeck goes out of sight (behind a game or another app, or minimized), or comes back.</summary>
    private void UpdateLean(bool hidden)
    {
        bool game = AllGames.Any(g => g.IsRunning);
        if (hidden && (game ? Settings.LeanWhilePlaying : Settings.LeanInBackground))
        {
            // Behind a game: after 15 seconds. Behind anything else: after a minute, so a quick look elsewhere costs nothing.
            if (!_lean && !_leanDelay.IsEnabled)
            {
                _leanDelay.Interval = TimeSpan.FromSeconds(game ? 15 : 60);
                _leanDelay.Start();
            }
            return;
        }
        _leanDelay.Stop();
        if (_lean) LeaveLean();
    }

    private void EnterLean()
    {
        if (_lean || !_backgroundMode) return;
        bool game = AllGames.Any(g => g.IsRunning);
        _lean = true;
        CancelCoverFetches(); // nothing downloads covers behind the game; whatever's still missing is asked for again after
        try
        {
            // Open pages close, unless one is doing something that must finish (a download, a question, the
            // launch splash) or is worth keeping (a video or web page, a cloud game). Only behind a game.
            if (game && Settings.ClosePagesWhilePlaying && _screens.Count > 0 &&
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

            // Out of sight without a game (behind a game, UpdateGameMemory has done these already): the moving
            // background's video, the menu music player and pages kept in memory go too. Music you're listening to
            // (your own, Spotify, a music service) keeps playing.
            if (!game)
            {
                StopDynamicBackground();
                SuspendMenuMusic();
                if (!_screens.OfType<BrowserScreen>().Any()) BrowserScreen.DisposeKept();
                if (!_screens.OfType<StreamingScreen>().Any() && StreamingScreen.Current?.Service.Music != true) StreamingScreen.CloseKept();
                _idleUnloaded = true;
            }
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

    private bool _idleUnloaded;

    private async void LeaveLean()
    {
        _lean = false;
        _leanTrim.Stop();
        SetMemoryPriority(low: false);
        if (_idleUnloaded && !AllGames.Any(g => g.IsRunning))
        {
            _idleUnloaded = false;
            RestoreDynamicBackground(); // once SquareDeck is in front (it waits for that itself)
            UpdateMenuMusic();          // carries on from the same spot
        }
        try
        {
            // The visible tiles first, then the rest, a few at a time so SquareDeck stays responsive. Each game on its
            // own: one picture that fails doesn't leave every tile after it without art.
            var games = _view.Concat(AllGames).Concat(NotInstalledGames).Distinct().Where(g => g.Parked).ToList();
            foreach (var batch in games.Chunk(6))
            {
                if (_lean) return; // back in a game already
                await Task.WhenAll(batch.Select(async g =>
                {
                    try { await g.UnparkAsync(); }
                    catch (Exception ex) { CrashLog.Write("Leaving lean mode: " + g.Name, ex); }
                }));
            }
            _ = ShowStyleLogoAsync(GameList.SelectedItem as Game);
        }
        catch (Exception ex) { CrashLog.Write("Leaving lean mode", ex); }
        RestoreMissingArt();
        // Once more a little later: a cover whose file was busy, or tiles Home rebuilt while the game's playtime and
        // the Steam library were being refreshed, get their art then.
        await Task.Delay(3000);
        RestoreMissingArt();
    }

    /// <summary>
    /// After a game: any Home tile still without its picture gets it loaded again the usual way (saved covers come
    /// straight from disk). This catches tiles the read-back missed, like the "Continue where you left off" tile made
    /// while its game's cover was put away, or a cover file that changed while the game ran.
    /// </summary>
    private void RestoreMissingArt()
    {
        if (_lean) return;
        try
        {
            // Anything still marked as put away (a read-back cut short by going back into the game) is read back now.
            foreach (var g in _view.Concat(AllGames).Concat(NotInstalledGames).Distinct().Where(g => g.Parked).ToList())
                _ = g.UnparkAsync();
            if (_continueTile is { Cover: null, Target: { } target })
            {
                if (target.Cover != null) _continueTile.Cover = target.Cover;
                else _ = LoadContinueArtAsync(_continueTile);
            }
            var missing = _view.Where(g => g.Cover == null && !g.Parked && g.Kind is ItemKind.SteamGame or ItemKind.External or ItemKind.Custom).ToList();
            if (missing.Count == 0) return;
            EnsureCovers(missing.Where(g => g.IsSteam).ToList());
            foreach (var g in missing.Where(g => !g.IsSteam)) _ = ReloadOwnCoverAsync(g);
        }
        catch (Exception ex) { CrashLog.Write("Restoring art after a game", ex); }
    }

    /// <summary>A game added by hand or from another store: its own cover file, if it has one.</summary>
    private async Task ReloadOwnCoverAsync(Game game)
    {
        if (CustomCoverPath(game) is not string path) return;
        if (SteamGridDbClient.IsIconFile(path)) { await ShowIconFileAsync(game, path); return; }
        var image = await Task.Run(() => ImageLoader.FromFile(path, 640));
        if (image != null && game.Cover == null) game.Cover = image;
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
