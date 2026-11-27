using System.Windows;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// SquareDeck out of sight, game or no game. As soon as another app is in front, the menu music, the moving
/// background and the highlight's pulse already pause (UpdateFocusPause). On top of that:
///  - after a few seconds in the background, everything else that moves on its own stops too: the companion, the
///    ambient effects, the tilt effect and pages' own animations (Screen.OnAppRest), and the controller is checked less;
///  - once another window covers SquareDeck's screen (or SquareDeck is minimized), it stops drawing altogether, the
///    same as behind a game (UpdateBackgroundMode), so nothing at all is drawn or animated;
///  - after a minute out of sight, it gives memory back (lean mode, see MainWindow.Memory): covers, the moving
///    background's video, the menu music player and pages kept in memory are let go.
/// It all comes back the moment SquareDeck is in front again.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _restWatch = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _inactiveSince = DateTime.MaxValue;
    private int _coveredSeconds;
    private bool _coveredByApp, _resting, _tiltWasRunning;

    /// <summary>SquareDeck is in the background and has paused what moves on its own.</summary>
    public bool Resting => _resting;

    private void InitRest()
    {
        _restWatch.Tick += (_, _) => RestTick();
        Deactivated += (_, _) =>
        {
            _inactiveSince = DateTime.UtcNow;
            _coveredSeconds = 0;
            _restWatch.Start();
        };
        Activated += (_, _) =>
        {
            _inactiveSince = DateTime.MaxValue;
            _coveredSeconds = 0;
            _restWatch.Stop();
            SetCovered(false);
            SetResting(false);
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && !IsActive) RestTick(); };
    }

    private void RestTick()
    {
        if (IsActive) { _restWatch.Stop(); return; }
        // Something SquareDeck must keep showing over another app (the launch splash, "Closing software"). If that's
        // gone on for 20 seconds with something else in front, it's let go: a hold left behind by mistake would
        // otherwise keep SquareDeck drawing behind the game the whole time it's played.
        if (_holdVisible > 0)
        {
            if (DateTime.UtcNow - _inactiveSince < TimeSpan.FromSeconds(20)) { _coveredSeconds = 0; SetCovered(false); SetResting(false); return; }
            ReleaseHold();
        }
        if (DateTime.UtcNow - _inactiveSince >= TimeSpan.FromSeconds(4) || WindowState == WindowState.Minimized) SetResting(true);
        bool covered = WindowState == WindowState.Minimized || WindowActivator.FrontCovers(this);
        _coveredSeconds = covered ? _coveredSeconds + 1 : 0;
        // A few seconds first: an app flashing up over SquareDeck for a moment doesn't make it blink.
        SetCovered(WindowState == WindowState.Minimized || _coveredSeconds >= 3);
    }

    private void SetCovered(bool covered)
    {
        if (covered == _coveredByApp) return;
        _coveredByApp = covered;
        UpdateBackgroundMode();
    }

    private void SetResting(bool rest)
    {
        if (rest == _resting) return;
        _resting = rest;
        try
        {
            if (rest)
            {
                _tiltWasRunning = _tiltTimer.IsEnabled;
                _tiltTimer.Stop();
                if (_companion != null) { _companionTimer.Stop(); _companionFrames.Stop(); }
                if (!_backgroundMode) _padTimer.Interval = TimeSpan.FromMilliseconds(50); // still quick enough for the Guide button
            }
            else
            {
                if (_tiltWasRunning) _tiltTimer.Start();
                _tiltWasRunning = false;
                if (_companion != null && !_companionLeaving && !_companionAsleep) _companionTimer.Start(); // still asleep while a game runs
                if (!_backgroundMode) _padTimer.Interval = TimeSpan.FromMilliseconds(16);
            }
            PauseAmbient(rest || _backgroundMode);
            foreach (var screen in _screens.ToList()) screen.OnAppRest(rest);
        }
        catch (Exception ex) { CrashLog.Write("Resting in the background", ex); }
    }
}
