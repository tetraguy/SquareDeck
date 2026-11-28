using System.Runtime.InteropServices;
using Windows.Devices.Sensors;

namespace SquareDeck.Services;

/// <summary>
/// 9.0.13: gyro aiming, as Armoury Crate's Gamepad Mode > Gyro offers it. Turning the Ally left and right (or tilting
/// it sideways) and tilting it up and down moves the mouse, so games that aim with a mouse aim with the gyro. It reads
/// the Ally's own motion sensor through Windows (the same one the home screen's parallax uses), takes off the resting
/// reading measured in Calibration > Gyro, ignores the dead zone, and only aims while the chosen button is held (or
/// always), and, unless told otherwise, only while a game is running.
/// </summary>
public static class GyroAim
{
    private static Gyrometer? _gyro;
    private static Settings? _settings;
    private static readonly Gamepad Pad = new();
    private static double _carryX, _carryY;
    private static DateTimeOffset _last;

    /// <summary>The latest reading, degrees per second (x pitch, y yaw, z roll), with the resting reading taken off.</summary>
    public static (double X, double Y, double Z) Latest { get; private set; }

    /// <summary>The latest raw reading (for calibration).</summary>
    public static (double X, double Y, double Z) Raw { get; private set; }

    public static bool Available
    {
        get
        {
            try { return (_gyro ??= Gyrometer.GetDefault()) != null; }
            catch (Exception) { return false; }
        }
    }

    public static readonly (string Id, string Name)[] Buttons =
    {
        ("always", "Always on"), ("lb", "While LB is held"), ("rb", "While RB is held"), ("lt", "While LT is held"), ("rt", "While RT is held"),
        ("ls", "While the left stick is pressed"), ("rs", "While the right stick is pressed"), ("lbrb", "While LB or RB is held"),
    };

    /// <summary>Starts or stops reading the sensor, as the settings say (also keeps it reading while Calibration measures).</summary>
    public static void Configure(Settings settings, bool measuring = false)
    {
        _settings = settings;
        bool want = measuring || settings.GyroMode == "mouse";
        try
        {
            _gyro ??= Gyrometer.GetDefault();
            if (_gyro == null) return;
            _gyro.ReadingChanged -= OnReading;
            if (!want) return;
            _gyro.ReportInterval = Math.Max(_gyro.MinimumReportInterval, 8u);
            _gyro.ReadingChanged += OnReading;
        }
        catch (Exception) { }
    }

    private static void OnReading(Gyrometer sender, GyrometerReadingChangedEventArgs e)
    {
        var s = _settings;
        if (s == null) return;
        var r = e.Reading;
        Raw = (r.AngularVelocityX, r.AngularVelocityY, r.AngularVelocityZ);
        Latest = (r.AngularVelocityX - s.GyroBiasX, r.AngularVelocityY - s.GyroBiasY, r.AngularVelocityZ - s.GyroBiasZ);
        var now = r.Timestamp;
        double dt = _last == default ? 0 : Math.Clamp((now - _last).TotalSeconds, 0, 0.05);
        _last = now;
        if (s.GyroMode != "mouse" || dt <= 0) return;
        if (s.GyroOnlyInGames && !AllyControl.Playing) return;
        if (!Held(s.GyroButton)) { _carryX = _carryY = 0; return; }

        // Degrees per second: yaw (or roll) moves left and right, pitch up and down.
        double horizontal = s.GyroHorizontal == "roll" ? -Latest.Z : -Latest.Y, vertical = -Latest.X;
        double dead = s.GyroDeadzone * 0.1; // 0 to 10 degrees per second
        horizontal = Math.Abs(horizontal) <= dead ? 0 : horizontal - Math.Sign(horizontal) * dead;
        vertical = Math.Abs(vertical) <= dead ? 0 : vertical - Math.Sign(vertical) * dead;
        if (s.GyroInvertX) horizontal = -horizontal;
        if (s.GyroInvertY) vertical = -vertical;
        double pixelsPerDegree = 12.0 * Math.Clamp(s.GyroSensitivity, 10, 400) / 100.0;
        _carryX += horizontal * dt * pixelsPerDegree;
        _carryY += vertical * dt * pixelsPerDegree;
        int dx = (int)Math.Truncate(_carryX), dy = (int)Math.Truncate(_carryY);
        if (dx == 0 && dy == 0) return;
        _carryX -= dx;
        _carryY -= dy;
        Move(dx, dy);
    }

    private static bool Held(string button)
    {
        if (button == "always") return true;
        var raw = Pad.GetRaw();
        if (!raw.Connected) return false;
        return button switch
        {
            "lb" => (raw.Buttons & 0x0100) != 0,
            "rb" => (raw.Buttons & 0x0200) != 0,
            "lbrb" => (raw.Buttons & 0x0300) != 0,
            "ls" => (raw.Buttons & 0x0040) != 0,
            "rs" => (raw.Buttons & 0x0080) != 0,
            "lt" => raw.LeftTrigger > 60,
            "rt" => raw.RightTrigger > 60,
            _ => true
        };
    }

    // ---------------------------------------------------------------- the mouse

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int Dx, Dy; public uint Data, Flags, Time; public IntPtr Extra; }

    [StructLayout(LayoutKind.Explicit)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public MouseInput Mouse; // 8 on 64-bit: the union starts after the type and padding
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    private static void Move(int dx, int dy)
    {
        try
        {
            var input = new Input { Type = 0, Mouse = new MouseInput { Dx = dx, Dy = dy, Flags = 0x0001 } }; // MOUSEEVENTF_MOVE
            SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
        }
        catch (Exception) { }
    }
}
