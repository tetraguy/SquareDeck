using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// How the companion arrives when it's turned on and leaves when it's turned off, a different way each time. It
/// drops from the sky, pops up from below, runs in, teleports, parachutes, spins in, rockets in or wakes up; it
/// sinks away, blasts off, vanishes in a poof, walks off, floats away on a balloon, spins out, dozes off or beams
/// up. Each list is shuffled and played through before any repeats, so the same one never comes twice in a row.
/// </summary>
public partial class MainWindow
{
    private const int CompanionHellos = 8, CompanionByes = 8;
    private readonly List<int> _helloBag = new(), _byeBag = new();
    private int _lastHello = -1, _lastBye = -1;
    /// <summary>Counts goodbyes, so one that's interrupted (turned back on mid-goodbye) doesn't remove the new one.</summary>
    private int _companionByeId;
    private bool _companionLeaving;

    /// <summary>The next one from a shuffled bag; refilled when empty, never starting with the one just played.</summary>
    private static int NextFromBag(List<int> bag, int count, ref int last)
    {
        if (bag.Count == 0)
        {
            bag.AddRange(Enumerable.Range(0, count).OrderBy(_ => Random.Shared.Next()));
            if (bag.Count > 1 && bag[0] == last) (bag[0], bag[^1]) = (bag[^1], bag[0]);
        }
        last = bag[0];
        bag.RemoveAt(0);
        return last;
    }

    /// <summary>Puts the sprite back to standing still where it belongs (before an arrival, and after leaving).</summary>
    private void CompanionResetPose()
    {
        _companionMove.BeginAnimation(TranslateTransform.XProperty, null);
        _companionMove.BeginAnimation(TranslateTransform.YProperty, null);
        _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _companionTurn.BeginAnimation(RotateTransform.AngleProperty, null);
        _companionMove.X = _companionMove.Y = 0;
        _companionScale.ScaleX = _companionScale.ScaleY = 1;
        _companionTurn.Angle = 0;
        if (_companion != null) { _companion.BeginAnimation(OpacityProperty, null); _companion.Opacity = 1; }
    }

    private static DoubleAnimation Go(double from, double to, double seconds, IEasingFunction? ease = null, double delay = 0) =>
        new(from, to, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease, BeginTime = TimeSpan.FromSeconds(delay) };

    private static EasingFunctionBase Out => new QuadraticEase { EasingMode = EasingMode.EaseOut };
    private static EasingFunctionBase In => new QuadraticEase { EasingMode = EasingMode.EaseIn };

    // ================================================================ hello

    /// <summary>Arrives on screen one of eight ways, then waves and says hello.</summary>
    private void CompanionHello()
    {
        if (_companion == null) return;
        CompanionResetPose();
        int which = NextFromBag(_helloBag, CompanionHellos, ref _lastHello);
        (double seconds, string line) = which switch
        {
            0 => (1.6, "Hi there!"),
            1 => (1.4, "Peekaboo!"),
            2 => (1.6, "Hey hey, I'm here!"),
            3 => (1.3, "Ta-da!"),
            4 => (2.4, "Wheee! Hello!"),
            5 => (1.4, "I'm back!"),
            6 => (1.5, "Zoom! Made it!"),
            _ => (2.2, "Good to see you!"),
        };
        // Waving the whole time (and anything else it wants to do waits until it's in).
        CompanionReact("wave");
        _companionActionUntil = DateTime.Now.AddSeconds(seconds + 1.2);
        if (Theme.ReduceMotion) { CompanionSay(line); return; }

        var mx = TranslateTransform.XProperty;
        var my = TranslateTransform.YProperty;
        var sx = ScaleTransform.ScaleXProperty;
        var sy = ScaleTransform.ScaleYProperty;
        var angle = RotateTransform.AngleProperty;
        switch (which)
        {
            case 0: // drops from the sky and bounces on landing
                _companionMove.BeginAnimation(my, Go(-420, 0, 1.1, new BounceEase { Bounces = 3, Bounciness = 2.4, EasingMode = EasingMode.EaseOut }));
                _companionScale.BeginAnimation(sy, Go(0.75, 1, 0.3, new ElasticEase { Oscillations = 2 }, 0.45));
                Later(0.5, () => Sparkles("✦", 8, "#FFD84D"));
                break;
            case 1: // pops up from below the edge
                _companionMove.BeginAnimation(my, Go(110, 0, 0.7, new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }));
                _companionScale.BeginAnimation(sx, Go(0.6, 1, 0.7, new ElasticEase { Oscillations = 2, Springiness = 4 }));
                Sparkles("!", 3, "#FFFFFF", rise: true);
                break;
            case 2: // runs in from the right, hopping, and skids to a stop
                _companionMove.BeginAnimation(mx, Go(320, 0, 1.1, new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut }));
                _companionMove.BeginAnimation(my, new DoubleAnimation(0, -14, TimeSpan.FromSeconds(0.14)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4), EasingFunction = Out });
                _companionTurn.BeginAnimation(angle, Go(-14, 0, 0.5, Out, 0.9));
                Later(1.0, () => Sparkles("💨", 3, "#C8D0DC"));
                break;
            case 3: // teleports in: flickers, then pops to full size in a ring of sparkles
            {
                var flicker = new DoubleAnimationUsingKeyFrames();
                foreach (var (t, v) in new[] { (0.0, 0.0), (0.1, 0.8), (0.2, 0.1), (0.3, 0.9), (0.4, 0.2), (0.55, 1.0) })
                    flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
                _companion.BeginAnimation(OpacityProperty, flicker);
                _companionScale.BeginAnimation(sx, Go(0.1, 1, 0.6, new ElasticEase { Oscillations = 2, Springiness = 3 }, 0.4));
                _companionScale.BeginAnimation(sy, Go(1.6, 1, 0.6, new ElasticEase { Oscillations = 2, Springiness = 3 }, 0.4));
                Sparkles("✧", 12, "#C3A6FF");
                break;
            }
            case 4: // parachutes down, swinging gently
                _companionMove.BeginAnimation(my, Go(-380, 0, 2.2, new SineEase { EasingMode = EasingMode.EaseOut }));
                _companionTurn.BeginAnimation(angle, new DoubleAnimation(-12, 12, TimeSpan.FromSeconds(0.55)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                Sparkles("☁", 3, "#E6ECF5", rise: true);
                Later(2.2, () => Sparkles("✦", 5, "#FFD84D"));
                break;
            case 5: // spins in, growing from nothing
                _companionScale.BeginAnimation(sx, Go(0, 1, 1.0, new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }));
                _companionScale.BeginAnimation(sy, Go(0, 1, 1.0, new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }));
                _companionTurn.BeginAnimation(angle, Go(-720, 0, 1.0, Out));
                Sparkles("★", 10, "#FFC93C");
                break;
            case 6: // rockets in from the bottom right and lands with a puff
                _companionMove.BeginAnimation(mx, Go(260, 0, 0.9, Out));
                _companionMove.BeginAnimation(my, Go(160, 0, 0.9, Out));
                _companionTurn.BeginAnimation(angle, Go(35, 0, 0.9, Out));
                Sparkles("🔥", 4, "#FF8A3C");
                Later(0.9, () => Sparkles("💨", 4, "#C8D0DC"));
                break;
            default: // already there, asleep: z's, a stretch, a happy jump
                _companionScale.BeginAnimation(sy, Go(0.55, 1, 0.5, new ElasticEase { Oscillations = 3, Springiness = 4 }, 1.1));
                _companionScale.BeginAnimation(sx, Go(1.25, 1, 0.5, new ElasticEase { Oscillations = 3, Springiness = 4 }, 1.1));
                _companionScale.ScaleY = 0.55; _companionScale.ScaleX = 1.25;
                Sparkles("z", 3, "#AAB4C8", rise: true);
                _companionMove.BeginAnimation(my, new DoubleAnimation(0, -22, TimeSpan.FromSeconds(0.22)) { BeginTime = TimeSpan.FromSeconds(1.6), AutoReverse = true, EasingFunction = Out });
                Later(1.7, () => Sparkles("♥", 5, "#FF5A7A", rise: true));
                break;
        }
        Later(Math.Min(seconds, 1.0), () => CompanionSay(line));
    }

    // ================================================================ goodbye

    /// <summary>Leaves one of eight ways, then <paramref name="gone"/> takes it off the screen.</summary>
    private void CompanionGoodbye(Action gone)
    {
        if (_companion == null) { gone(); return; }
        int id = ++_companionByeId;
        _companionLeaving = true;
        // Nothing else starts while it says goodbye.
        _companionTimer.Stop();
        _companionQueue.Clear();
        CompanionResetPose();
        int which = NextFromBag(_byeBag, CompanionByes, ref _lastBye);
        string line = which switch
        {
            0 => "Bye bye!", 1 => "To the stars!", 2 => "Poof!", 3 => "See ya later!",
            4 => "Up, up and away!", 5 => "Wheee… bye!", 6 => "Nap time… zzz", _ => "Beam me up!",
        };
        CompanionReact(which == 6 ? "sleep" : "bye");
        _companionActionUntil = DateTime.Now.AddSeconds(4);
        CompanionSay(line);
        void Finish(double seconds) => Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            if (id != _companionByeId || !_companionLeaving) return; // turned back on in the meantime
            _companionLeaving = false;
            gone();
            CompanionResetPose();
        });
        if (Theme.ReduceMotion) { Finish(1.6); return; }

        var mx = TranslateTransform.XProperty;
        var my = TranslateTransform.YProperty;
        var sx = ScaleTransform.ScaleXProperty;
        var sy = ScaleTransform.ScaleYProperty;
        var angle = RotateTransform.AngleProperty;
        switch (which)
        {
            case 0: // waves, then sinks down out of sight
                _companionMove.BeginAnimation(my, Go(0, 120, 0.8, In, 1.4));
                _companion.BeginAnimation(OpacityProperty, Go(1, 0, 0.4, null, 1.9));
                break;
            case 1: // crouches, then blasts off upward
                _companionScale.BeginAnimation(sy, Go(1, 0.7, 0.4, Out, 0.6));
                _companionMove.BeginAnimation(my, Go(0, -520, 0.9, In, 1.0));
                Later(1.0, () => { Sparkles("🔥", 5, "#FF8A3C"); Sparkles("💨", 4, "#C8D0DC"); });
                break;
            case 2: // swells, then vanishes in a burst of sparkles
                _companionScale.BeginAnimation(sx, Go(1, 1.3, 0.3, Out, 1.0));
                _companionScale.BeginAnimation(sy, Go(1, 1.3, 0.3, Out, 1.0));
                _companion.BeginAnimation(OpacityProperty, Go(1, 0, 0.12, null, 1.3));
                Later(1.3, () => { Sparkles("✦", 14, "#FFD84D"); Sparkles("✧", 8, "#C3A6FF"); });
                break;
            case 3: // turns and walks off to the right, hopping
                _companionScale.BeginAnimation(sx, Go(1, -1, 0.2, null, 0.9)); // faces the way it goes
                _companionMove.BeginAnimation(mx, Go(0, 340, 1.3, In, 1.0));
                _companionMove.BeginAnimation(my, new DoubleAnimation(0, -12, TimeSpan.FromSeconds(0.15)) { BeginTime = TimeSpan.FromSeconds(1.0), AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) });
                break;
            case 4: // floats away on a balloon, swaying
                Later(0.3, () => Sparkles("🎈", 1, "#FF5A7A"));
                _companionMove.BeginAnimation(my, Go(0, -460, 2.0, new SineEase { EasingMode = EasingMode.EaseIn }, 0.5));
                _companionTurn.BeginAnimation(angle, new DoubleAnimation(-10, 10, TimeSpan.FromSeconds(0.5)) { BeginTime = TimeSpan.FromSeconds(0.5), AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) });
                _companion.BeginAnimation(OpacityProperty, Go(1, 0, 0.6, null, 2.0));
                break;
            case 5: // spins faster and faster down to nothing
                _companionTurn.BeginAnimation(angle, Go(0, 1080, 1.4, In, 0.7));
                _companionScale.BeginAnimation(sx, Go(1, 0, 1.4, In, 0.7));
                _companionScale.BeginAnimation(sy, Go(1, 0, 1.4, In, 0.7));
                Later(0.8, () => Sparkles("★", 10, "#FFC93C"));
                break;
            case 6: // dozes off and fades away
                _companionScale.BeginAnimation(sy, Go(1, 0.8, 1.0, Out, 0.3));
                Sparkles("z", 4, "#AAB4C8", rise: true);
                _companion.BeginAnimation(OpacityProperty, Go(1, 0, 1.2, null, 1.2));
                break;
            default: // beamed up: flickers, squeezes to a line and is gone
            {
                _companionScale.BeginAnimation(sx, Go(1, 0.05, 0.5, In, 1.0));
                _companionScale.BeginAnimation(sy, Go(1, 1.8, 0.5, In, 1.0));
                _companionMove.BeginAnimation(my, Go(0, -60, 0.5, In, 1.0));
                var flicker = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(1.0) };
                foreach (var (t, v) in new[] { (0.0, 1.0), (0.1, 0.3), (0.2, 0.9), (0.3, 0.2), (0.4, 0.7), (0.55, 0.0) })
                    flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
                _companion.BeginAnimation(OpacityProperty, flicker);
                Later(1.0, () => Sparkles("✧", 10, "#7FFFE9", rise: true));
                break;
            }
        }
        // Off the screen once it's gone and its goodbye has been read.
        Finish(3.3);
    }
}
