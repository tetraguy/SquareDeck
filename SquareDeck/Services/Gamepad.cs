using System.Runtime.InteropServices;

namespace SquareDeck.Services;

/// <summary>
/// Inputs the launcher understands. X and Y are the "Details" and "Options" actions; which physical
/// button triggers each of the four face actions is set by <see cref="Hints.Map"/>.
/// </summary>
public enum NavAction { Left, Right, Up, Down, Accept, Back, X, Y, Start, View, LB, RB, LT, RT }

/// <summary>Minimal XInput: d-pad / left stick with hold-to-repeat, face buttons, and rumble.</summary>
public sealed class Gamepad
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
        public uint Reserved; // the extended call below writes four extra bytes
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputBattery
    {
        public byte Type, Level;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputVibration
    {
        public ushort LeftMotor, RightMotor;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint index, out XInputState state);

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputSetState(uint index, ref XInputVibration vibration);

    // Same as XInputGetState but also reports the Guide button. Exported by number only.
    [DllImport("xinput1_4.dll", EntryPoint = "#100")]
    private static extern uint XInputGetStateEx(uint index, out XInputState state);

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetBatteryInformation(uint index, byte deviceType, out XInputBattery battery);

    private const ushort DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008;
    private const ushort StartButton = 0x0010, ViewButton = 0x0020, LeftShoulder = 0x0100, RightShoulder = 0x0200;
    private const ushort Guide = 0x0400, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
    /// <summary>How far a stick must move to count as a direction in SquareDeck (49% by default; Experimental > Stick check changes it).</summary>
    public static short StickThreshold { get; set; } = 16000;

    // Holding a direction: a short pause, then steady steps that speed up the longer it's held (long lists and rows).
    // Accessibility (Settings > Accessibility > Holding a direction): "normal", "slow" (a longer pause, slower steps
    // that don't speed up) or "off" (one step per press).
    public static string RepeatPace { get; set; } = "normal";
    private static bool Slow => RepeatPace == "slow";
    private static TimeSpan FirstRepeat => TimeSpan.FromMilliseconds(Slow ? 650 : 320);
    private static TimeSpan NextRepeat => TimeSpan.FromMilliseconds(Slow ? 260 : 85);
    private static TimeSpan FastRepeat => TimeSpan.FromMilliseconds(Slow ? 260 : 55);
    private const int RepeatsBeforeFast = 8;
    private int _repeats;

    // Asking an empty controller slot is slow in XInput (it looks for the device each time), and it was done for all
    // four slots sixty times a second on the interface's thread. Empty slots are looked at again once a second.
    private readonly DateTime[] _emptyUntil = new DateTime[4];
    private static readonly TimeSpan EmptyRecheck = TimeSpan.FromSeconds(1);

    private bool _available = true;
    private uint _index;
    private ushort _previous;
    private NavAction? _heldDirection;
    private bool _waitForNeutral;
    private DateTime _nextRepeatAt;
    private int _rumbleToken;
    private bool _extended = true;
    private bool _leftTriggerHeld, _rightTriggerHeld;

    /// <summary>Right stick up/down from the last poll, -1 (down) to 1 (up); 0 inside the dead zone.</summary>
    public double RightStickY { get; private set; }

    /// <summary>Right stick sideways, -1 to 1 (0 inside the dead zone). Used as a mouse in the browser.</summary>
    public double RightStickX { get; private set; }

    /// <summary>True while the right stick is pressed in (R3).</summary>
    public bool RightStickPressed { get; private set; }

    /// <summary>Everything a controller reports, for the controller test page.</summary>
    public readonly record struct RawState(bool Connected, ushort Buttons, byte LeftTrigger, byte RightTrigger,
        short LeftX, short LeftY, short RightX, short RightY);

    /// <summary>Current state of the first connected controller.</summary>
    public RawState GetRaw()
    {
        if (!_available) return default;
        try
        {
            for (uint i = 0; i < 4; i++)
            {
                if (GetState(i, out var state) != 0) continue;
                var p = state.Gamepad;
                return new RawState(true, p.Buttons, p.LeftTrigger, p.RightTrigger, p.ThumbLX, p.ThumbLY, p.ThumbRX, p.ThumbRY);
            }
        }
        catch (Exception) { }
        return default;
    }
    private bool _summonHeld;
    private bool _guideDown, _guideSpoiled;

    private uint GetState(uint index, out XInputState state)
    {
        const uint NotConnected = 1167;
        var now = DateTime.UtcNow;
        if (index < 4 && now < _emptyUntil[index]) { state = default; return NotConnected; }
        uint result = NotConnected;
        bool done = false;
        if (_extended)
        {
            try { result = XInputGetStateEx(index, out state); done = true; }
            catch (EntryPointNotFoundException) { _extended = false; state = default; }
        }
        else state = default;
        if (!done) result = XInputGetState(index, out state);
        if (index < 4) _emptyUntil[index] = result == NotConnected ? now + EmptyRecheck : DateTime.MinValue;
        return result;
    }

    private static ushort Mask(char button) => button switch { 'A' => A, 'B' => B, 'X' => X, _ => Y };

    /// <summary>Call on a timer. Returns actions triggered since the last poll.</summary>
    public IReadOnlyList<NavAction> Poll()
    {
        var actions = new List<NavAction>();
        if (!_available) return actions;

        ushort buttons = 0;
        bool leftTrigger = false, rightTrigger = false;
        RightStickY = 0;
        RightStickX = 0;
        RightStickPressed = false;
        try
        {
            // Every connected controller counts, not just the first: while a game runs, Steam (or another tool) can
            // add a virtual controller in front of the real one, and the first slot may then never move.
            bool found = false;
            for (uint i = 0; i < 4; i++)
            {
                if (GetState(i, out var state) != 0) continue;
                var pad = state.Gamepad;
                buttons |= pad.Buttons;
                leftTrigger |= pad.LeftTrigger > 120;
                rightTrigger |= pad.RightTrigger > 120;
                if (RightStickY == 0 && Math.Abs((int)pad.ThumbRY) > 9000) RightStickY = pad.ThumbRY / 32767.0;
                if (RightStickX == 0 && Math.Abs((int)pad.ThumbRX) > 9000) RightStickX = pad.ThumbRX / 32767.0;
                RightStickPressed |= (pad.Buttons & 0x0080) != 0;
                if (pad.ThumbLX < -StickThreshold) buttons |= DpadLeft;
                if (pad.ThumbLX > StickThreshold) buttons |= DpadRight;
                if (pad.ThumbLY > StickThreshold) buttons |= DpadUp;
                if (pad.ThumbLY < -StickThreshold) buttons |= DpadDown;
                if (!found || pad.Buttons != 0) _index = i; // the one being used (for rumble and the battery)
                found = true;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            _available = false;
            return actions;
        }

        ushort pressed = (ushort)(buttons & ~_previous);
        _previous = buttons;

        // Face buttons follow the user's mapping (Settings > Controller buttons).
        if ((pressed & Mask(Hints.Select)) != 0) actions.Add(NavAction.Accept);
        if ((pressed & Mask(Hints.Back)) != 0) actions.Add(NavAction.Back);
        if ((pressed & Mask(Hints.Details)) != 0) actions.Add(NavAction.X);
        if ((pressed & Mask(Hints.Options)) != 0) actions.Add(NavAction.Y);
        if ((pressed & StartButton) != 0) actions.Add(NavAction.Start);
        if ((pressed & ViewButton) != 0) actions.Add(NavAction.View);
        if ((pressed & LeftShoulder) != 0) actions.Add(NavAction.LB);
        if ((pressed & RightShoulder) != 0) actions.Add(NavAction.RB);

        // Triggers are analog, so treat a firm squeeze as a press.
        if (leftTrigger && !_leftTriggerHeld) actions.Add(NavAction.LT);
        if (rightTrigger && !_rightTriggerHeld) actions.Add(NavAction.RT);
        _leftTriggerHeld = leftTrigger;
        _rightTriggerHeld = rightTrigger;

        NavAction? direction =
            (buttons & DpadLeft) != 0 ? NavAction.Left :
            (buttons & DpadRight) != 0 ? NavAction.Right :
            (buttons & DpadUp) != 0 ? NavAction.Up :
            (buttons & DpadDown) != 0 ? NavAction.Down : null;

        var now = DateTime.UtcNow;
        if (direction == null)
        {
            _heldDirection = null;
            _waitForNeutral = false;
        }
        else if (_waitForNeutral)
        {
            // Direction was already held when we regained focus; ignore until released.
        }
        else if (direction != _heldDirection)
        {
            _heldDirection = direction;
            _repeats = 0;
            _nextRepeatAt = now + FirstRepeat;
            actions.Add(direction.Value);
        }
        else if (RepeatPace != "off" && now >= _nextRepeatAt)
        {
            _repeats++;
            _nextRepeatAt = now + (_repeats >= RepeatsBeforeFast ? FastRepeat : NextRepeat);
            actions.Add(direction.Value);
        }

        return actions;
    }

    /// <summary>Forget held buttons, e.g. after the window was in the background.</summary>
    public void Reset()
    {
        _previous = 0xFFFF; // treat everything as already held so nothing fires on return
        _heldDirection = null;
        _waitForNeutral = true;
        _leftTriggerHeld = _rightTriggerHeld = true;
        RightStickY = 0;
    }

    /// <summary>
    /// For use while the launcher is in the background: true once each time the Guide button,
    /// or View and Menu together, is pressed.
    /// </summary>
    /// <summary>"default" (Guide, or View + Menu), "guide", "view+menu", "l3+r3" or "lb+rb+view".</summary>
    public string SummonCombo { get; set; } = "default";

    /// <summary>True when the summon shortcut is the Guide button on its own (not a combination).</summary>
    private bool GuideSummons => SummonCombo is "guide" or "default";

    private bool SummonHeld(ushort b)
    {
        const ushort LeftStick = 0x0040, RightStick = 0x0080;
        bool Both(ushort x, ushort y) => (b & x) != 0 && (b & y) != 0;
        return SummonCombo switch
        {
            "guide" => false, // handled on release, see PollSummon
            "view+menu" => Both(StartButton, ViewButton),
            "l3+r3" => Both(LeftStick, RightStick),
            "lb+rb+view" => Both(LeftShoulder, RightShoulder) && (b & ViewButton) != 0,
            _ => Both(StartButton, ViewButton)
        };
    }

    public bool PollSummon()
    {
        if (!_available) return false;
        bool held = false, guideTap = false;
        try
        {
            ushort b = 0;
            bool trigger = false, any = false;
            for (uint i = 0; i < 4; i++)
            {
                if (GetState(i, out var state) != 0) continue;
                b |= state.Gamepad.Buttons;
                trigger |= state.Gamepad.LeftTrigger > 120 || state.Gamepad.RightTrigger > 120;
                any = true;
            }
            if (any)
            {
                held = SummonHeld(b);
                guideTap = GuideTapped(b, trigger);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            _available = false;
            return false;
        }

        bool fired = held && !_summonHeld;
        _summonHeld = held;
        return fired || guideTap;
    }

    /// <summary>
    /// The Guide button counts only when pressed and released on its own. Held with another button it is one of
    /// Windows' Game Bar shortcuts (Guide + Y takes a screenshot, Guide + X records...), and opening the quick menu
    /// there put SquareDeck in front of the game, so the capture was named "SquareDeck" instead of the game.
    /// </summary>
    private bool GuideTapped(ushort b, bool trigger)
    {
        if (!GuideSummons) { _guideDown = false; return false; }
        bool guide = (b & Guide) != 0;
        if (guide)
        {
            if (!_guideDown) { _guideDown = true; _guideSpoiled = false; }
            if ((b & ~Guide) != 0 || trigger) _guideSpoiled = true; // another button with it: Game Bar's, not ours
            return false;
        }
        if (!_guideDown) return false;
        _guideDown = false;
        return !_guideSpoiled;
    }

    /// <summary>Battery of the first connected controller: "Wired", "Full", "Medium", "Low", "Empty", or null.</summary>
    public string? GetBattery()
    {
        if (!_available) return null;
        try
        {
            for (uint i = 0; i < 4; i++)
            {
                if (GetState(i, out _) != 0) continue;
                if (XInputGetBatteryInformation(i, 0, out var battery) != 0) return null;
                return battery.Type switch
                {
                    1 => "Wired",
                    2 or 3 => battery.Level switch { 0 => "Empty", 1 => "Low", 2 => "Medium", _ => "Full" },
                    _ => null
                };
            }
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>A short, light rumble on the controller last seen by <see cref="Poll"/>.</summary>
    /// <param name="percent">Strength, 1 to 100.</param>
    public async void Pulse(int milliseconds = 45, int percent = 60)
    {
        if (!_available || percent <= 0) return;
        // Squared so the low end of the slider is gentle; 60 lands on the original strength.
        double level = Math.Clamp(percent, 1, 100) / 100.0;
        ushort strength = (ushort)(65535 * level * level * 0.6);
        int token = ++_rumbleToken;
        try
        {
            var on = new XInputVibration { LeftMotor = strength, RightMotor = strength };
            XInputSetState(_index, ref on);
            await Task.Delay(milliseconds);
            if (token != _rumbleToken) return; // a newer pulse owns the motors now
            var off = new XInputVibration();
            XInputSetState(_index, ref off);
        }
        catch (Exception)
        {
            _available = false;
        }
    }
}
