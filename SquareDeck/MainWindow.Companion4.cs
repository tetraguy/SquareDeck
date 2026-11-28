using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// The companion, livelier still (9.0.17):
/// - It naps when the handheld sleeps (or its screen turns off), and wakes up 15 seconds after the handheld does, once
///   SquareDeck is the window in front. Poke it while it naps and it mumbles.
/// - Picking it up: four ways of hanging on (swinging, flailing, spinning, or just chilling), it notices when you shake
///   it or hold it a long time, and five ways of landing (a bounce, a superhero landing, a headstand, a splat from up
///   high, and a tumble when you fling it).
/// - Ten new things it does around the launcher on its own: sitting on a ledge, a paper plane, chasing a butterfly,
///   pushing against the page, hide and seek, a trampoline, a lasso, sweeping up, balancing a star and a catnap.
/// - More to say, and new sounds (snore, yawn, whistle, splat, flutter, ding).
/// </summary>
public partial class MainWindow
{
    // ================================================================ napping with the handheld

    private bool _companionNap, _companionNapHooked;
    private DateTime? _companionNapResumeAt;
    private readonly DispatcherTimer _companionNapCheck = new() { Interval = TimeSpan.FromSeconds(1) };
    private IntPtr _displayNotify;
    private int _companionNapTicks;

    private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
    private const int WM_POWERBROADCAST = 0x0218, PBT_POWERSETTINGCHANGE = 0x8013;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    /// <summary>Listens for the handheld going to sleep and its screen turning off and on (once).</summary>
    private void HookCompanionNap()
    {
        if (_companionNapHooked) return;
        _companionNapHooked = true;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnCompanionPowerMode;
        _companionNapCheck.Tick += (_, _) => CompanionNapCheck();
        Activated += (_, _) => CompanionNapCheck();
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero && HwndSource.FromHwnd(handle) is { } source)
            {
                source.AddHook(CompanionPowerHook);
                var guid = ConsoleDisplayState;
                _displayNotify = RegisterPowerSettingNotification(handle, ref guid, 0);
            }
        }
        catch (Exception) { }
        Closed += (_, _) =>
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnCompanionPowerMode;
            _companionNapCheck.Stop();
            if (_displayNotify != IntPtr.Zero) { UnregisterPowerSettingNotification(_displayNotify); _displayNotify = IntPtr.Zero; }
        };
    }

    private void OnCompanionPowerMode(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend) Dispatcher.BeginInvoke(() => CompanionNapStart());
        else if (e.Mode == Microsoft.Win32.PowerModes.Resume) Dispatcher.BeginInvoke(() => CompanionNapResumed());
    }

    /// <summary>The screen turning off counts as going to sleep (handhelds often only turn the screen off), and on again as waking.</summary>
    private IntPtr CompanionPowerHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_POWERBROADCAST || wParam.ToInt32() != PBT_POWERSETTINGCHANGE || lParam == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            var guid = Marshal.PtrToStructure<Guid>(lParam);
            if (guid != ConsoleDisplayState) return IntPtr.Zero;
            int state = Marshal.ReadInt32(lParam, 20); // 0 off, 1 on, 2 dimmed
            if (state == 0) CompanionNapStart();
            else if (state == 1) CompanionNapResumed();
        }
        catch (Exception) { }
        return IntPtr.Zero;
    }

    /// <summary>The handheld is going to sleep: so does the companion (drawn asleep right away, so that's how you find it).</summary>
    private void CompanionNapStart()
    {
        _companionNapResumeAt = null;
        if (_companion == null || _companionAsleep || _companionNap) return;
        _companionNap = true;
        _companionNapTicks = 0;
        _companionQueue.Clear();
        _companionAction = null;
        _touchDragging = false;
        _touchDown = null;
        CompanionResetPose();
        _companionFx?.Children.Clear();
        if (_companionBubble != null) { _companionBubble.BeginAnimation(OpacityProperty, null); _companionBubble.Opacity = 0; }
        _companionDrawn = "";
        CompanionTick();
    }

    /// <summary>The handheld woke: the companion sleeps on for 15 seconds more, then gets up once SquareDeck is in front.</summary>
    private void CompanionNapResumed()
    {
        if (!_companionNap) return;
        _companionNapResumeAt = DateTime.Now;
        _companionNapCheck.Start();
    }

    private void CompanionNapCheck()
    {
        if (!_companionNap) { _companionNapCheck.Stop(); return; }
        if (_companionNapResumeAt is not { } at || DateTime.Now - at < TimeSpan.FromSeconds(15)) return;
        if (!IsActive || WindowState == WindowState.Minimized) return; // wakes when you come back to SquareDeck
        _companionNapCheck.Stop();
        CompanionNapEnd();
    }

    private void CompanionNapEnd()
    {
        _companionNap = false;
        _companionNapResumeAt = null;
        _companionQueue.Clear();
        _companionScale.ScaleY = 1;
        _companionDrawn = "";
        if (_companion == null || _companionAsleep) return;
        CompanionReact("wakeup");
    }

    /// <summary>One tick while napping: eyes shut, slow breathing, a z now and then and a little snore.</summary>
    private void CompanionNapTick(string key)
    {
        if (_companionSprite == null) return;
        _companionNapTicks++;
        string drawn = key + "|nap";
        if (drawn != _companionDrawn)
        {
            _companionDrawn = drawn;
            CompanionArt.Draw(_companionSprite, CompanionArt.SpeciesId, CompanionArt.ColorHex, "sleepy", false, Labs.Outfit, 0, false);
        }
        if (Theme.ReduceMotion) return;
        if (!_touchDragging) _companionMove.Y = 4; // curled up a little lower
        _companionScale.ScaleY = _companionNapTicks % 6 < 3 ? 0.95 : 1.0;
        if (_companionNapTicks % 4 == 0) Sparkles(_companionNapTicks % 8 == 0 ? "Z" : "z", 1, "#AAB4C8", rise: true);
        if (_companionNapResumeAt != null && _companionNapTicks % 14 == 0) CompanionSounds.Effect("snore");
    }

    private static readonly string[] NapMumbles =
    {
        "Mmh… five more minutes…", "Zzz… no… I'm awake… zzz", "*snore*", "Shh… dreaming about loot.", "Not now… boss fight… zzz",
        "Mmf… who turned the lights on?", "Zzz… one more level…", "Five… more… frames…",
    };

    /// <summary>While napping only waking up plays; a poke, a hug or a carry just gets a sleepy mumble.</summary>
    private bool CompanionNapBlocks(string action)
    {
        if (!_companionNap || action == "wakeup") return false;
        if (action is "boop" or "hug" or "pet" or "dizzy" or "drop")
        {
            if (action == "drop") { _companionMove.BeginAnimation(TranslateTransform.YProperty, null); _companionMove.Y = 4; }
            _companionNapMumble = true;
            CompanionSay(NapMumbles[Random.Shared.Next(NapMumbles.Length)]);
            _companionNapMumble = false;
        }
        return true;
    }

    private bool _companionNapMumble;

    private bool CompanionNapQuiet => _companionNap && !_companionNapMumble;

    private static string WakeLine() => new[]
    {
        "*yaaawn* Good morning! …Is it morning?", "Mmh… did I miss anything? Did you play without me?", "Rebooting companion… done!",
        "I was dreaming about 100% completion. So close.", "Okay, okay, I'm up! Who moved my pillow?", "Nap complete. Energy: 100%.",
        "Huh? Oh! Hi! I was just… resting my eyes.", "Did the handheld sleep well too? Great. Same.",
    }[Random.Shared.Next(8)];

    // ================================================================ picking it up and putting it down

    private int _grabStyle, _grabId, _dragTurns, _dragDir;
    private double _flingVx, _dragLastX;
    private DateTime _dragLastAt;
    private bool _grabShook;

    /// <summary>Called as it's carried: how fast it's moving (for a fling) and whether it's being shaken.</summary>
    private void CompanionDragMoved(Point p)
    {
        var now = DateTime.Now;
        double dt = (now - _dragLastAt).TotalSeconds;
        if (dt > 0.005)
        {
            double vx = (p.X - _dragLastX) / dt;
            _flingVx = _flingVx * 0.5 + vx * 0.5;
            int dir = Math.Abs(p.X - _dragLastX) < 6 ? 0 : Math.Sign(p.X - _dragLastX);
            if (dir != 0 && _dragDir != 0 && dir != _dragDir && ++_dragTurns >= 6) _grabShook = true;
            if (dir != 0) _dragDir = dir;
            _dragLastX = p.X;
            _dragLastAt = now;
        }
        // Leans with the way it's carried (the flailing and spinning styles move on their own).
        if (_grabStyle is 0 or 3 && !Theme.ReduceMotion)
            _companionMove.X = Math.Clamp(-_flingVx / 120, -10, 10);
    }

    /// <summary>Picked up: one of four ways to hang on.</summary>
    private void CompanionDangle()
    {
        int id = ++_grabId;
        _grabStyle = Random.Shared.Next(4);
        _grabShook = false;
        _dragTurns = _dragDir = 0;
        _flingVx = 0;
        _dragLastAt = DateTime.Now;
        _dragLastX = _touchDown?.X ?? 0;
        switch (_grabStyle)
        {
            case 0: // swinging
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-14, 14, TimeSpan.FromSeconds(0.35)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.08, TimeSpan.FromSeconds(0.2)));
                _companionLines["dangle"] = new[] { "Whoa, put me down!", "Wheee! Where are we going?", "I'm flying!", "Careful, I'm fragile! (I'm not.)" }[Random.Shared.Next(4)];
                CompanionSounds.Effect("slide");
                break;
            case 1: // flailing
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(0.07)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1.06, TimeSpan.FromSeconds(0.12)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                Prop("!", "#FF6B6B", 22, 70, 12, 1.2);
                _companionLines["dangle"] = new[] { "Put me down put me down put me down!", "I'm not a claw-machine prize!", "Mayday! Mayday!", "Unhand me, giant!" }[Random.Shared.Next(4)];
                CompanionSounds.Effect("giggle");
                break;
            case 2: // spinning slowly
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
                _companionLines["dangle"] = new[] { "The world is spinning! Or is that me?", "Weeeeeee!", "This is my ride now.", "Round and round we go!" }[Random.Shared.Next(4)];
                CompanionSounds.Effect("whoosh");
                break;
            default: // just chilling
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-4, 4, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
                Prop("♪", "#C3A6FF", 18, 70, 20, 1.6);
                _companionLines["dangle"] = new[] { "Just hanging out.", "Is this the express elevator?", "Take me to the shiny games.", "First class, please." }[Random.Shared.Next(4)];
                CompanionSounds.Effect("whistle");
                break;
        }
        // Held a while: it comments.
        Later(4.5, () =>
        {
            if (id != _grabId || !_touchDragging) return;
            CompanionSay(_grabShook ? "I'm a companion, not a snow globe!" : new[] { "My arms are getting tired. Wait, do I have arms?", "Are we there yet?", "You can put me down any time now.", "Okay, I live up here now." }[Random.Shared.Next(4)]);
        });
        Later(11, () => { if (id == _grabId && _touchDragging) CompanionSay(new[] { "Still here. Still dangling.", "I can see my house from here!", "Is this a hostage situation?" }[Random.Shared.Next(3)]); });
    }

    /// <summary>Put down: a fling tumbles, a high drop splats, a shaken one is dizzy, otherwise one of four landings.</summary>
    private void CompanionLand()
    {
        _grabId++;
        double lift = _companionMove.Y;
        _companionTurn.BeginAnimation(RotateTransform.AngleProperty, null);
        _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _companionMove.BeginAnimation(TranslateTransform.XProperty, null);
        _companionTurn.Angle = 0;
        _companionScale.ScaleX = 1;
        _companionMove.X = 0;
        var bounce = new BounceEase { Bounces = 2, Bounciness = 2.5, EasingMode = EasingMode.EaseOut };
        void Fall(double seconds = 0.35) => _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(lift, 0, TimeSpan.FromSeconds(seconds)) { EasingFunction = bounce });

        if (Math.Abs(_flingVx) > 900 && (DateTime.Now - _dragLastAt).TotalSeconds < 0.15)
        {
            // Flung: tumbles further along the bottom, rolling.
            double bx = CompanionBase().X;
            double to = Math.Clamp(_companionPos.X + Math.Clamp(_flingVx * 0.22, -320, 320), bx - CompanionRange(), bx);
            CompanionGlideX(to, 0.8, new QuadraticEase { EasingMode = EasingMode.EaseOut });
            _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, _flingVx < 0 ? -720 : 720, TimeSpan.FromSeconds(0.8)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            Fall(0.5);
            Later(0.8, () => Sparkles("∴", 5, "#C8B89A"));
            CompanionSounds.Effect("whoosh");
            CompanionSounds.Effect("thud", 750);
            _companionLines["drop"] = new[] { "Wheee—oof!", "Yeet! …I've been yeeted.", "Strike! Wait, I'm the ball?", "Ten points for distance!" }[Random.Shared.Next(4)];
            return;
        }
        if (_grabShook)
        {
            Fall();
            _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-10, 10, TimeSpan.FromSeconds(0.18)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4), BeginTime = TimeSpan.FromSeconds(0.35) });
            for (int i = 0; i < 3; i++) Later(0.3 + i * 0.25, () => Prop("✶", "#FFD84D", 14, 30 + Random.Shared.Next(40), 6, 0.8));
            CompanionSounds.Effect("boing", 200);
            _companionLines["drop"] = new[] { "I'm a companion, not a snow globe! @_@", "Everything's… wobbly…", "Which way is up?", "Do that again and I'm telling." }[Random.Shared.Next(4)];
            return;
        }
        if (lift < -100)
        {
            // From up high: a splat, then it peels itself up.
            _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(lift, 0, TimeSpan.FromSeconds(0.25)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0, 1), (0.25, 1), (0.3, 1.4), (1.0, 1.4), (1.3, 1)));
            _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1.05), (0.25, 1.05), (0.3, 0.5), (1.0, 0.5), (1.3, 1.1), (1.5, 1)));
            Later(0.3, () => Sparkles("✶", 5, "#FFD84D"));
            CompanionSounds.Effect("splat", 230);
            _companionLines["drop"] = new[] { "I'm okay! …I think.", "That was a big drop!", "Who turned up the gravity?", "Splat. Ow. Splat.", "Note to self: learn to fly." }[Random.Shared.Next(5)];
            return;
        }
        switch (Random.Shared.Next(4))
        {
            case 0: // the classic bounce
                Fall();
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1.05), (0.35, 0.8), (0.6, 1.05), (0.8, 1)));
                Later(0.3, () => Sparkles("∴", 4, "#C8B89A"));
                CompanionSounds.Effect("boing", 200);
                _companionLines["drop"] = new[] { "Nailed the landing!", "Oof! New spot!", "Ten out of ten.", "I'll just live here now." }[Random.Shared.Next(4)];
                break;
            case 1: // superhero landing
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(lift, 0, TimeSpan.FromSeconds(0.2)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (0.2, 1), (0.25, 0.7), (1.0, 0.7), (1.2, 1.1), (1.35, 1)));
                Later(0.22, () => { var l = Prop("≈", "#C8B89A", 20, 0, 80, 0.8); var r = Prop("≈", "#C8B89A", 20, 90, 80, 0.8);
                    l?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(0, -30, TimeSpan.FromSeconds(0.6))); r?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(90, 120, TimeSpan.FromSeconds(0.6))); });
                CompanionSounds.Effect("thud", 180);
                _companionLines["drop"] = new[] { "Superhero landing!", "Bad for the knees. Worth it.", "Did you see that? Tell me you saw that." }[Random.Shared.Next(3)];
                break;
            case 2: // lands on its head, then flips over
                Fall(0.3);
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.25, 180), (0.9, 180), (1.25, 360)));
                Later(1.25, () => { _companionTurn.BeginAnimation(RotateTransform.AngleProperty, null); _companionTurn.Angle = 0; });
                CompanionSounds.Effect("pop", 250);
                CompanionSounds.Effect("boing", 900);
                _companionLines["drop"] = new[] { "Stuck the landing! Sort of.", "Upside-down mode: activated.", "I meant to do that." }[Random.Shared.Next(3)];
                break;
            default: // tiptoe landing
                _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(lift, 0, TimeSpan.FromSeconds(0.6)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
                _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0.6, 1), (0.7, 1.08), (0.85, 1)));
                Later(0.6, () => Sparkles("✧", 3, "#FFFFFF"));
                CompanionSounds.Effect("ding", 550);
                _companionLines["drop"] = new[] { "Graceful as a cat.", "Light as a feather!", "And the judges say… perfect!" }[Random.Shared.Next(3)];
                break;
        }
    }

    // ================================================================ playing around the launcher

    private static double? CompanionPlaySeconds(string action) => action switch
    {
        "wakeup" => 3.2, "ledge" => 4.2, "paperplane" => 3, "butterfly" => 4, "pushpage" => 3, "hideseek" => 3.4, "trampoline" => 2.8,
        "lasso" => 2.8, "sweep" => 3.2, "balance" => 3.2, "catnap" => 4.5,
        _ => null,
    };

    private static string? CompanionPlayMood(string? action) => action switch
    {
        "wakeup" => "bored",
        "catnap" => "sleepy",
        "trampoline" or "lasso" or "butterfly" => "dance",
        "ledge" or "paperplane" or "sweep" or "balance" or "hideseek" => "happy",
        "pushpage" => "worried",
        _ => null,
    };

    private static string? CompanionPlaySay(string action) => action switch
    {
        "wakeup" => WakeLine(),
        "ledge" => new[] { "Nice view from up here!", "I'm the king of the screen!", "Don't look down. Don't look down." }[Random.Shared.Next(3)],
        "paperplane" => new[] { "Airmail!", "Flight 001, cleared for takeoff.", "Fold, fold, whoosh!" }[Random.Shared.Next(3)],
        "butterfly" => new[] { "Come back, little buddy!", "Ooh, pretty! Wait up!", "So close… so close!" }[Random.Shared.Next(3)],
        "pushpage" => new[] { "Who put this page here?", "Hnnngh… it won't budge!", "Moving furniture. Please hold." }[Random.Shared.Next(3)],
        "hideseek" => new[] { "Found me!", "You'll never find me! …Oh.", "Hide and seek champion!" }[Random.Shared.Next(3)],
        "trampoline" => new[] { "Boing! Boing! BOING!", "Higher! Higher!", "I can touch the top bar!" }[Random.Shared.Next(3)],
        "lasso" => new[] { "Yeehaw!", "Gonna wrangle me a game.", "Giddy up!" }[Random.Shared.Next(3)],
        "sweep" => new[] { "Tidying up your library.", "So much dust behind these tiles.", "Clean screen, clean mind." }[Random.Shared.Next(3)],
        "balance" => new[] { "Look, no hands!", "Steady… steady…", "Circus audition, take one." }[Random.Shared.Next(3)],
        "catnap" => new[] { "Just resting my eyes…", "Zzz… quick nap…", "Wake me if something shiny happens." }[Random.Shared.Next(3)],
        _ => null,
    };

    /// <summary>The moves and props for the new acts.</summary>
    private void CompanionPlayAnimate(string action)
    {
        if (_companionSprite == null) return;
        switch (action)
        {
            case "wakeup":
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 0.95), (0.6, 1.25), (1.4, 1.25), (1.8, 1)));
                _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0, 1), (0.6, 0.85), (1.4, 0.85), (1.8, 1)));
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((1.9, 0), (2.1, -14), (2.4, 0)));
                Later(1.9, () => Sparkles("☼", 4, "#FFD84D", rise: true));
                CompanionSounds.Effect("yawn");
                CompanionSounds.Effect("chime", 1900);
                Later(1.8, () => { _companionAction = "wave"; _companionDrawn = ""; CompanionTick(); });
                break;
            case "ledge":
                // Hops up onto an invisible ledge, sits swinging its feet, hops back down.
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.3, -70), (0.4, -60), (3.5, -60), (3.9, 0)));
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0.5, 0), (1, -5), (1.5, 5), (2, -5), (2.5, 5), (3, 0)));
                Prop("▁▁▁▁", "#8A94A6", 18, 10, 60, 3.6)?.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(130, 128, TimeSpan.FromSeconds(0.2)));
                CompanionSounds.Effect("boing");
                CompanionSounds.Effect("boing", 3500);
                break;
            case "paperplane":
            {
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.3, 12), (0.5, -10), (0.8, 0)));
                var plane = Prop("✈", "#FFFFFF", 22, 40, 30, 2.8);
                plane?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(40, -520, TimeSpan.FromSeconds(2.4)) { BeginTime = TimeSpan.FromSeconds(0.4), EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } });
                plane?.BeginAnimation(Canvas.TopProperty, Keys((0, 30), (0.4, 30), (1.2, -60), (2, -10), (2.8, -90)));
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(2.8);
                CompanionSounds.Effect("whoosh", 400);
                break;
            }
            case "butterfly":
            {
                var fly = Prop("🦋", "#C3A6FF", 18, 10, 20, 4, emoji: true);
                fly?.BeginAnimation(Canvas.LeftProperty, Keys((0, 10), (1, -90), (2, -180), (2.6, -120), (3.4, -260)));
                fly?.BeginAnimation(Canvas.TopProperty, Keys((0, 20), (0.5, 0), (1, 30), (1.5, -10), (2, 20), (2.6, -20), (3.4, -80)));
                _companionMove.BeginAnimation(TranslateTransform.XProperty, Keys((0, 0), (1.2, -70), (2.2, -150), (2.8, -150), (3.9, 0)));
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.3, -8), (0.6, 0), (0.9, -8), (1.2, 0), (1.5, -8), (1.8, 0), (2.1, -12), (2.4, 0)));
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(2.8);
                CompanionSounds.Effect("flutter");
                break;
            }
            case "pushpage":
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0), (0.3, -14), (2.6, -14), (3, 0)));
                _companionMove.BeginAnimation(TranslateTransform.XProperty, Keys((0, 0), (0.3, -10), (0.8, -12), (1.3, -9), (1.8, -13), (2.3, -10), (3, 0)));
                _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(3);
                Later(1.2, () => Prop("💧", "#7FB8FF", 14, 70, 20, 1.2, emoji: true));
                break;
            case "hideseek":
                if (_companion != null)
                    _companion.BeginAnimation(OpacityProperty, Keys((0, 1), (0.4, 0.12), (2.6, 0.12), (2.8, 1)));
                Later(1.0, () => Prop("?", "#AAB4C8", 22, 40, 30, 1.4));
                Later(2.8, () => { Sparkles("✦", 5, "#FFD84D"); CompanionSounds.Effect("ding"); });
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((2.7, 0), (2.9, -18), (3.2, 0)));
                break;
            case "trampoline":
            {
                Prop("⌣", "#8A94A6", 30, 30, 92, 2.8);
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.35, -30), (0.7, 0), (1.15, -55), (1.6, 0), (2.15, -90), (2.75, 0)));
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, Keys((1.7, 0), (2.4, 360)));
                Later(2.75, () => { _companionTurn.BeginAnimation(RotateTransform.AngleProperty, null); _companionTurn.Angle = 0; });
                for (int i = 0; i < 3; i++) CompanionSounds.Effect("boing", i * 450);
                break;
            }
            case "lasso":
            {
                var loop = Prop("◯", "#E0B070", 30, 20, -24, 2.6);
                if (loop != null)
                {
                    var spin = new ScaleTransform(1, 0.35);
                    loop.RenderTransform = spin;
                    spin.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1.2, TimeSpan.FromSeconds(0.25)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5) });
                }
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-6, 6, TimeSpan.FromSeconds(0.25)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5) });
                CompanionSounds.Effect("whistle");
                break;
            }
            case "sweep":
            {
                var broom = Prop("🧹", "#C8A060", 22, 70, 50, 3.2, emoji: true);
                broom?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(60, 90, TimeSpan.FromSeconds(0.4)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) });
                _companionMove.BeginAnimation(TranslateTransform.XProperty, Keys((0, 0), (1.5, -40), (3, 0)));
                for (int i = 0; i < 4; i++) Later(0.4 + i * 0.7, () => Sparkles("∴", 2, "#C8B89A"));
                break;
            }
            case "balance":
            {
                var star = Prop("★", "#FFD84D", 22, 40, -10, 3.2);
                if (star != null)
                {
                    var tilt = new RotateTransform();
                    star.RenderTransform = tilt;
                    tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-25, 25, TimeSpan.FromSeconds(0.5)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3), EasingFunction = new SineEase() });
                    star.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(34, 46, TimeSpan.FromSeconds(0.5)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3), EasingFunction = new SineEase() });
                }
                _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(6, -6, TimeSpan.FromSeconds(0.5)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3), EasingFunction = new SineEase() });
                Later(3.0, () => CompanionSounds.Effect("ding"));
                break;
            }
            case "catnap":
                _companionMove.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.4, 4), (4.1, 4), (4.4, 0)));
                _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0.4, 1), (1.2, 0.95), (2, 1), (2.8, 0.95), (3.6, 1)));
                for (int i = 0; i < 4; i++) Later(0.5 + i * 0.9, () => Sparkles("z", 1, "#AAB4C8", rise: true));
                CompanionSounds.Effect("snore", 800);
                Later(4.1, () => Prop("!", "#FFD84D", 20, 70, 14, 0.6));
                break;
        }
    }

    /// <summary>What it does on its own, everywhere it goes, plus more to say on Home.</summary>
    private static (string[] Idle, string[] Lines)? CompanionPlayFor(Screen? screen) => screen switch
    {
        null => (new[] { "ledge", "paperplane", "butterfly", "pushpage", "hideseek", "trampoline", "lasso", "sweep", "balance", "catnap" },
            new[] { "I've been practicing my idle animation. How is it?", "Rumor has it there's a secret in the Labs.", "Every tile here is my favorite. Don't tell the others.",
                    "If games were snacks, you'd have a buffet.", "I'd play too, but my thumbs are… theoretical.", "Battery check! …Yep, still a battery.",
                    "Is it a 'play something new' day or a 'comfort game' day?", "I'm on break. My break is forever. I love this job." }),
        SettingsScreen => (new[] { "sweep", "balance" }, new[] { "Tweaking things? I love a good tweak.", "Every toggle is a tiny adventure." }),
        LibraryScreen or Switch2LibraryScreen => (new[] { "sweep", "hideseek", "ledge" }, new[] { "I dusted shelf three. You're welcome.", "Some of these haven't been opened. They're lonely." }),
        _ => (new[] { "balance", "paperplane", "catnap" }, Array.Empty<string>()),
    };

    private static readonly string[] MoreTickleComebacks =
    {
        "I'm filing a complaint with the Labs.", "One more poke and I start narrating your life.", "Is this a rhythm game? Am I the rhythm game?",
        "My poke meter is full. Please insert coin.", "You poke, I pout. That's the deal.",
    };

    private static readonly string[] MoreIdleLines =
    {
        "I'll be over here, practicing my dramatic sigh. *sigh*", "Is the controller okay? Blink if you need help.", "Loading patience… 3%…",
        "I made up a game: it's called 'Waiting'. I'm winning.",
    };
}
