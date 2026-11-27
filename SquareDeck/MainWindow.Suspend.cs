using System.Runtime.InteropServices;
using System.Windows.Threading;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// Pause the game while you're in SquareDeck (Settings > Games > While playing, off by default): a few seconds after
/// SquareDeck comes to the front over a running game, the game's processes are frozen, so it stops using the processor,
/// graphics and battery. The moment you go back (or close it from SquareDeck, or SquareDeck closes), it carries on.
/// The frozen processes are written down, so a SquareDeck that crashed lets them go the next time it starts.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _suspendDelay = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly List<int> _suspended = new();
    private Game? _suspendedGame;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr process);
    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr process);
    private const uint ProcessSuspendResume = 0x0800;

    private void InitSuspend()
    {
        // Left frozen by a SquareDeck that didn't close properly: let them go.
        if (Labs.Get("suspended") is { Length: > 0 } left)
        {
            foreach (var part in left.Split(','))
                if (int.TryParse(part, out int pid)) Resume(pid);
            Labs.Put("suspended", "");
        }
        _suspendDelay.Tick += (_, _) => { _suspendDelay.Stop(); _ = SuspendGameAsync(); };
        Activated += (_, _) => { if (Settings.SuspendGames && _suspended.Count == 0) _suspendDelay.Start(); };
        Deactivated += (_, _) => { _suspendDelay.Stop(); ResumeGame(); };
        Closed += (_, _) => { _suspendDelay.Stop(); ResumeGame(); };
    }

    private async Task SuspendGameAsync()
    {
        if (!Settings.SuspendGames || !IsActive || _suspended.Count > 0 || _holdVisible > 0) return;
        // Not while SquareDeck is launching or closing something.
        if (_screens.LastOrDefault() is GameBusyScreen or LaunchSplashScreen) return;
        var game = AllGames.FirstOrDefault(g => g.IsRunning);
        if (game == null) return;
        string? exe = ExactProgram(game);
        string? dir = exe == null && !string.IsNullOrEmpty(game.InstallDir) && System.IO.Directory.Exists(game.InstallDir) ? game.InstallDir : null;
        if (exe == null && dir == null) return;
        var pids = await Task.Run(() => ProcessUtil.PidsFor(exe, dir));
        if (!IsActive || !Settings.SuspendGames || pids.Count == 0) return; // went back to the game while looking
        foreach (int pid in pids)
            if (Suspend(pid)) _suspended.Add(pid);
        if (_suspended.Count == 0) return; // protected (anti-cheat, Store app): it just keeps running
        _suspendedGame = game;
        Labs.Put("suspended", string.Join(",", _suspended));
        ShowToast("GAME PAUSED", game.Name + " is paused", "It carries on when you go back to it", "", "info", game: game, remember: false);
    }

    /// <summary>Lets the paused game carry on. Safe to call any time.</summary>
    internal void ResumeGame()
    {
        if (_suspended.Count == 0) return;
        foreach (int pid in _suspended) Resume(pid);
        _suspended.Clear();
        _suspendedGame = null;
        Labs.Put("suspended", "");
    }

    /// <summary>True while <paramref name="game"/> is paused by SquareDeck.</summary>
    internal bool IsSuspended(Game game) => _suspendedGame == game && _suspended.Count > 0;

    private static bool Suspend(int pid)
    {
        var h = OpenProcess(ProcessSuspendResume, false, pid);
        if (h == IntPtr.Zero) return false;
        try { return NtSuspendProcess(h) >= 0; }
        finally { CloseHandle(h); }
    }

    private static void Resume(int pid)
    {
        var h = OpenProcess(ProcessSuspendResume, false, pid);
        if (h == IntPtr.Zero) return;
        try { NtResumeProcess(h); }
        finally { CloseHandle(h); }
    }
}
