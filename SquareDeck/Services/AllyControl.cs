using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SquareDeck.Services;

/// <summary>
/// Applies Settings > ROG Ally: the lights (with SquareDeck's own effects: accent color, battery level, the selected
/// game's colors, a low-battery pulse, off while playing), the ASUS operating mode, fan curves, the charge limit and
/// the controller (back-button keys, mouse mode, built-in controller). The hardware side is in AllyHardware.cs.
/// </summary>
public static class AllyControl
{
    public static readonly (string Id, string Name, string About)[] LightModes =
    {
        ("", "Leave as it is", "Armoury Crate SE (or the last setting) decides"),
        ("off", "Off", ""),
        ("solid", "One color", ""),
        ("breathing", "Breathing", "Fades between your two colors"),
        ("rainbow", "Rainbow", "Cycles through every color"),
        ("wheel", "Color wheel", "Colors spin around the sticks"),
        ("sticks", "A color per stick", "Your first color on the left stick, the second on the right"),
        ("accent", "SquareDeck's accent color", "Follows the theme, and the game's color when that's on"),
        ("battery", "Battery level", "Green when full through amber to red; breathes while charging"),
        ("game", "The selected game's colors", "Picked from the cover of the game you're on"),
    };

    public static readonly (string Hex, string Name)[] Palette =
    {
        ("#FF0033", "ROG red"), ("#FF6A00", "Orange"), ("#FFC400", "Amber"), ("#FFFF33", "Yellow"), ("#7CFF00", "Lime"),
        ("#00E05A", "Green"), ("#00D9C0", "Teal"), ("#00E5FF", "Cyan"), ("#00A0FF", "Blue"), ("#3D5AFE", "Indigo"),
        ("#9B30FF", "Purple"), ("#FF00D4", "Magenta"), ("#FF5C93", "Pink"), ("#FFFFFF", "White"), ("#107C10", "Xbox green"),
    };

    public static readonly (string Id, string Name)[] Speeds = { ("slow", "Slow"), ("medium", "Medium"), ("fast", "Fast") };

    /// <summary>The brightness levels, in percent.</summary>
    public static readonly int[] Brightness = { 5, 10, 30, 60, 100 };

    /// <summary>The saved brightness, snapped to one of the four levels.</summary>
    public static int BrightnessPercent(Settings s) =>
        Brightness.OrderBy(b => Math.Abs(b - s.AllyLightPercent)).First();

    /// <summary>
    /// A color at a brightness. The Ally's own brightness has only three steps (HandheldCompanion: 0–3), so the
    /// percentages are made by dimming the color itself, with the rings at full brightness.
    /// </summary>
    private static Color Dim(Color c, int percent)
    {
        // A channel that's on stays at least 1, so very low levels (5%) keep the color's hue instead of dropping a part.
        static byte Part(byte v, int percent) => v == 0 ? (byte)0 : (byte)Math.Max(1, Math.Round(v * percent / 100.0));
        return Color.FromRgb(Part(c.R, percent), Part(c.G, percent), Part(c.B, percent));
    }

    /// <summary>For the effects the Ally colors by itself (rainbow, wheel): the nearest of its own three steps.</summary>
    private static int HardwareLevel(int percent) => percent switch { <= 30 => 1, <= 60 => 2, _ => 3 };

    public static readonly (string Id, string Name, int Mode)[] AsusModes =
    {
        ("", "Leave as it is", -1), ("silent", "Silent", AsusAcpi.ModeSilent),
        ("performance", "Performance", AsusAcpi.ModePerformance), ("turbo", "Turbo", AsusAcpi.ModeTurbo),
    };

    /// <summary>Fan curves: the firmware's temperature points (58–98 °C) with SquareDeck's duties.</summary>
    public static readonly (string Id, string Name, string About, byte[]? Duties)[] FanCurves =
    {
        ("", "Leave as it is", "The ASUS mode decides", null),
        ("quiet", "Quiet", "Stays silent until it's warm; runs hotter", new byte[] { 0, 6, 12, 18, 26, 34, 42, 62 }),
        ("balanced", "Balanced", "ASUS's own curve", new byte[] { 8, 17, 22, 26, 34, 41, 48, 69 }),
        ("cool", "Cool", "Spins up earlier to keep it cooler", new byte[] { 20, 28, 35, 42, 50, 60, 70, 90 }),
        ("max", "Full speed", "Loud, coolest; for long sessions plugged in", new byte[] { 70, 75, 80, 85, 90, 95, 99, 99 }),
    };

    public static readonly int[] ChargeLimits = { 0, 60, 70, 80, 90, 100 };

    private static Settings? _settings;
    private static readonly DispatcherTimer Ticker = new() { Interval = TimeSpan.FromSeconds(30) };
    private static bool _playing, _lowFlash, _fullOnce, _mouse;
    private static Color? _gameColor;
    private static string _lastLights = "";

    private static bool? _isAlly;

    /// <summary>This is an ROG Ally (recognized, chosen in Settings, or its button device is there). Checked once.</summary>
    public static bool IsAlly => AllyButtons.ModelOverride.Length > 0 || (_isAlly ??= AllyButtons.Model.Length > 0 || AllyButtons.IsSupported);

    public static bool MouseMode => _mouse;

    public static void Init(Settings settings)
    {
        _settings = settings;
        if (!IsAlly) return;
        Ticker.Tick += (_, _) => Tick();
        Ticker.Start();
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        IdleTicker.Tick += (_, _) => IdleTick();
        IdleTicker.Start();
        // Off the window's thread: opening the drivers can take a moment.
        Task.Run(() =>
        {
            try { ApplyPower(); ApplyBattery(); ApplyController(); } catch (Exception) { }
        });
        ApplyLights(force: true);
    }

    /// <summary>When SquareDeck closes: the built-in controller back on, game mode, M1/M2 as ASUS ships them.</summary>
    public static void Shutdown()
    {
        if (_settings == null) return;
        try
        {
            AllyHid.SetBuiltInController(true);
            if (_settings.AllyBackKeys || _mouse) AllyHid.ConfigureController(backKeys: false, mouse: false);
        }
        catch (Exception) { }
    }

    // ================================================================ power, fans, battery

    public static void ApplyPower()
    {
        var s = _settings;
        if (s == null || !AsusAcpi.Available) return;
        var mode = AsusModes.FirstOrDefault(m => m.Id == s.AllyAsusMode);
        if (mode.Mode >= 0) AsusAcpi.SetMode(mode.Mode);
        var fan = FanCurves.FirstOrDefault(f => f.Id == s.AllyFan);
        if (fan.Duties != null)
        {
            var curve = new byte[16];
            Array.Copy(AsusAcpi.DefaultCpuCurve, curve, 8); // the temperatures
            Array.Copy(fan.Duties, 0, curve, 8, 8);
            AsusAcpi.SetFanCurve(curve);
        }
        else if (_fanTouched) AsusAcpi.ResetFanCurves();
        _fanTouched = fan.Duties != null;
        // A new ASUS mode puts the firmware's own power limits back: re-apply SquareDeck's TDP after it.
        if (mode.Mode >= 0 && TdpControl.Ready)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => _ = TdpControl.ApplyAsync(quiet: true)));
    }

    private static bool _fanTouched;

    public static void ApplyBattery()
    {
        var s = _settings;
        if (s == null || !AsusAcpi.Available) return;
        if (_fullOnce) { AsusAcpi.SetChargeLimit(100); return; }
        if (s.AllyChargeLimit > 0) AsusAcpi.SetChargeLimit(s.AllyChargeLimit);
    }

    /// <summary>Charges to 100% this once (until SquareDeck restarts), for a long trip.</summary>
    public static void ChargeFullOnce()
    {
        _fullOnce = true;
        ApplyBattery();
    }

    public static bool ChargingFullOnce => _fullOnce;

    // ================================================================ controller

    public static void ApplyController()
    {
        var s = _settings;
        if (s == null) return;
        // Only touch the controller's layout when something here asks for it; it resets Armoury Crate's mappings.
        if (s.AllyBackKeys || _backKeysWereOn) AllyHid.ConfigureController(s.AllyBackKeys, _mouse);
        _backKeysWereOn = s.AllyBackKeys;
        ApplyTuning();
    }

    /// <summary>Whether the controller tuning differs from ASUS's defaults (or was changed this run, so it's sent back).</summary>
    private static bool _tuned;

    /// <summary>Rumble strength and the stick and trigger dead zones, into the controller's firmware.</summary>
    public static bool ApplyTuning()
    {
        var s = _settings;
        if (s == null) return false;
        bool custom = s.AllyVibration != 100 || s.AllyStickInner != 0 || s.AllyStickOuter != 100 || s.AllyTriggerInner != 0 || s.AllyTriggerOuter != 100;
        if (!custom && !_tuned) return true; // nothing to change: leave the controller alone
        _tuned = custom;
        return AllyHid.SetTuning(s.AllyVibration, s.AllyStickInner, s.AllyStickOuter, s.AllyTriggerInner, s.AllyTriggerOuter);
    }

    private static bool _backKeysWereOn;

    public static bool ToggleMouseMode()
    {
        bool want = !_mouse;
        if (!AllyHid.SetMouseMode(want)) return false;
        _mouse = want;
        return true;
    }

    // ================================================================ lights

    private static AllyHid.AuraSpeed Speed => _settings?.AllyLightSpeed switch
    {
        "slow" => AllyHid.AuraSpeed.Slow,
        "fast" => AllyHid.AuraSpeed.Fast,
        _ => AllyHid.AuraSpeed.Medium
    };

    private static Color Parse(string hex, Color fallback) =>
        Theme.TryParseColor(hex, out var c) ? c : fallback;

    /// <summary>Makes a picked-from-art color bright enough to show on LEDs.</summary>
    private static Color Vivid(Color c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B));
        if (max < 1) return Color.FromRgb(255, 255, 255);
        double scale = 255 / max;
        return Color.FromRgb((byte)Math.Min(255, c.R * scale), (byte)Math.Min(255, c.G * scale), (byte)Math.Min(255, c.B * scale));
    }

    private static Color BatteryColor(int percent)
    {
        // Red (0%) → amber (50%) → green (100%).
        double t = Math.Clamp(percent / 100.0, 0, 1);
        byte r = (byte)(t < 0.5 ? 255 : 255 * (1 - t) * 2);
        byte g = (byte)(t < 0.5 ? 255 * t * 2 : 255);
        return Color.FromRgb(r, g, 0);
    }

    /// <summary>Applies the light setting. Repeated identical requests are skipped unless forced.</summary>
    public static void ApplyLights(bool force = false) => _ = ApplyLightsAsync(force);

    /// <summary>Applies the light setting; true when the lights took it (or there was nothing to change).</summary>
    public static Task<bool> ApplyLightsAsync(bool force = false)
    {
        var s = _settings;
        if (s == null || !IsAlly) return Task.FromResult(false);
        ApplyLightPower(force);
        ApplyXboxLight(force);
        string mode = s.AllyLights;
        int? battery = Battery.GetPercent();
        bool plugged = Battery.IsPluggedIn();
        _lowFlash = s.AllyLowBatteryFlash && battery is int b && b <= 15 && !plugged;

        var one = Parse(s.AllyLightColor, Color.FromRgb(255, 0, 51));
        var two = Parse(s.AllyLightColor2, Color.FromRgb(0, 160, 255));
        Color? gameColor = _gameColor;
        // Game lights: while a game runs, its profile's effect (its own colors by default).
        bool gameLights = _playing && s.AllyGameLights && _running != null;
        if (gameLights)
        {
            var p = _runningProfile;
            string chosen = p?.Lights ?? "game";
            if (chosen != "everyday")
            {
                mode = chosen;
                if (p?.LightColor is string c1) one = Parse(c1, one);
                if (p?.LightColor2 is string c2) two = Parse(c2, two);
                gameColor = _runningColor ?? one;
            }
        }
        string key;
        Func<bool> apply;
        if (_lowFlash)
        {
            key = "lowflash";
            apply = () => AllyHid.SetBrightness(3) & AllyHid.SetLights(AllyHid.AuraMode.Breathing, Color.FromRgb(255, 0, 0), Color.FromRgb(0, 0, 0), AllyHid.AuraSpeed.Fast);
        }
        else if (_idleDark)
        {
            key = "idle";
            apply = () => AllyHid.SetBrightness(0);
        }
        else if (mode.Length == 0) { _lastLights = ""; return Task.FromResult(true); } // leave them alone
        else if (mode == "off" || (_playing && s.AllyLightsOffPlaying && !gameLights))
        {
            key = "off";
            apply = () => AllyHid.SetBrightness(0);
        }
        else
        {
            int pct = BrightnessPercent(s);
            var speed = Speed;
            // Colors chosen here are dimmed to the percentage with the rings at full; rainbow and wheel use the
            // Ally's own nearest step.
            Color D(Color c) => Dim(c, pct);
            switch (mode)
            {
                case "solid": key = $"solid{one}{pct}"; apply = () => Lights(3, AllyHid.AuraMode.Solid, D(one), D(one), speed); break;
                case "breathing": key = $"breath{one}{two}{speed}{pct}"; apply = () => Lights(3, AllyHid.AuraMode.Breathing, D(one), D(two), speed); break;
                case "rainbow": key = $"rainbow{speed}{pct}"; apply = () => Lights(HardwareLevel(pct), AllyHid.AuraMode.Rainbow, one, one, speed); break;
                case "wheel": key = $"wheel{speed}{pct}"; apply = () => Lights(HardwareLevel(pct), AllyHid.AuraMode.Wheel, one, one, speed); break;
                case "sticks": key = $"sticks{one}{two}{pct}"; apply = () => AllyHid.SetBrightness(3) & AllyHid.SetSticks(D(one), D(two)); break;
                case "accent":
                    var accent = Vivid(CurrentAccent() ?? one);
                    key = $"accent{accent}{pct}";
                    apply = () => Lights(3, AllyHid.AuraMode.Solid, D(accent), D(accent), speed);
                    break;
                case "battery":
                    var color = BatteryColor(battery ?? 100);
                    key = $"battery{color}{plugged}{pct}";
                    apply = plugged
                        ? () => Lights(3, AllyHid.AuraMode.Breathing, D(color), Color.FromRgb(0, 0, 0), AllyHid.AuraSpeed.Slow)
                        : () => Lights(3, AllyHid.AuraMode.Solid, D(color), D(color), speed);
                    break;
                case "game":
                    var game = gameColor ?? one;
                    key = $"game{game}{pct}";
                    apply = () => Lights(3, AllyHid.AuraMode.Solid, D(game), D(game), speed);
                    break;
                default: return Task.FromResult(false);
            }
        }
        if (!force && key == _lastLights) return Task.FromResult(true);
        _lastLights = key;
        return Task.Run(async () =>
        {
            try
            {
                if (apply()) { if (force) LightLog(key, "ok"); return true; }
                // The light device may have been missed (or reconnected): look again and try once more.
                AllyHid.Rescan();
                await Task.Delay(400);
                if (apply()) { LightLog(key, "ok on the second try"); return true; }
                LightLog(key, "failed: " + AllyHid.Describe());
            }
            catch (Exception ex) { LightLog(key, "error: " + ex.Message); }
            if (_lastLights == key) _lastLights = ""; // so the next tick tries again
            return false;
        });
    }

    /// <summary>SquareDeck's accent color right now (the theme's, or the game's when that's on).</summary>
    private static Color? CurrentAccent()
    {
        var app = System.Windows.Application.Current;
        if (app == null) return null;
        if (app.Dispatcher.CheckAccess()) return (app.Resources["AccentBrush"] as SolidColorBrush)?.Color;
        return app.Dispatcher.Invoke(() => (app.Resources["AccentBrush"] as SolidColorBrush)?.Color);
    }

    private static bool Lights(int level, AllyHid.AuraMode mode, Color one, Color two, AllyHid.AuraSpeed speed)
    {
        bool bright = AllyHid.SetBrightness(level);
        return AllyHid.SetLights(mode, one, two, speed) && bright;
    }

    /// <summary>On or off from a button or the quick menu: off, or back to the chosen effect (one color if none).</summary>
    public static bool ToggleLights()
    {
        var s = _settings!;
        if (s.AllyLights is "" or "off")
        {
            s.AllyLights = s.AllyLightsLastOn.Length > 0 ? s.AllyLightsLastOn : "solid";
        }
        else
        {
            s.AllyLightsLastOn = s.AllyLights;
            s.AllyLights = "off";
        }
        s.Save();
        ApplyLights(force: true);
        return s.AllyLights != "off";
    }

    /// <summary>
    /// Windows 11's Dynamic Lighting ("Use Dynamic Lighting on my devices"): while it's on, Windows drives the Ally's
    /// rings itself and other apps' writes don't show.
    /// </summary>
    public static bool DynamicLightingOn
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Lighting");
                return key?.GetValue("AmbientLightingEnabled") is int on && on != 0;
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>What the lights were sent and how it went, in lights.log (kept short) for when they don't change.</summary>
    private static void LightLog(string key, string result)
    {
        try
        {
            string path = System.IO.Path.Combine(Settings.DataDir, "lights.log");
            var info = new System.IO.FileInfo(path);
            if (info.Exists && info.Length > 64 * 1024) info.Delete();
            string extra = DynamicLightingOn ? "  (Windows Dynamic Lighting is on)" : "";
            System.IO.File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {key}  {result}{extra}{Environment.NewLine}");
        }
        catch (Exception) { }
    }

    private static Game? _running;
    private static GameProfile? _runningProfile;
    private static string _runningKey = "";
    private static Color? _runningColor;

    /// <summary>A game started or closed (for "off while playing").</summary>
    public static void SetPlaying(bool playing) => SetRunning(playing ? _running : null, _runningProfile);

    /// <summary>
    /// The game that's running (null when none) and its profile, checked every second or two: game lights follow it.
    /// Its cover's main color is worked out once per game.
    /// </summary>
    public static async void SetRunning(Game? game, GameProfile? profile)
    {
        if (_settings == null || !IsAlly) return;
        string key = game == null ? "" : game.Key + "|" + profile?.Lights + "|" + profile?.LightColor + "|" + profile?.LightColor2;
        bool playing = game != null;
        if (key == _runningKey && playing == _playing) return;
        bool newGame = game?.Key != _running?.Key;
        _runningKey = key;
        _running = game;
        _runningProfile = profile;
        _playing = playing;
        if (newGame)
        {
            _runningColor = null;
            if (game?.Actual.Cover is BitmapSource cover)
            {
                ApplyLights(); // straight away with the fallback color, then with the cover's
                try
                {
                    var c = await Task.Run(() => ImageLoader.DominantColor(cover));
                    if (_running?.Key != game.Key) return; // another game since
                    _runningColor = c is Color found ? Vivid(found) : null;
                }
                catch (Exception) { }
            }
        }
        ApplyLights();
    }

    /// <summary>Applies a game profile's light effect now, while its game runs (after editing the profile).</summary>
    public static void ProfileChanged(Game game, GameProfile? profile)
    {
        if (_running?.Key == game.Key) SetRunning(_running, profile);
    }

    /// <summary>The highlighted game changed (for "the selected game's colors").</summary>
    public static async void ShowGame(Game? game)
    {
        if (_settings?.AllyLights != "game") return;
        try
        {
            Color? color = null;
            if (game?.Cover is BitmapSource cover) color = await Task.Run(() => ImageLoader.DominantColor(cover));
            _gameColor = color is Color c ? Vivid(c) : null;
            ApplyLights();
        }
        catch (Exception) { }
    }

    /// <summary>The theme's accent changed (for "SquareDeck's accent color").</summary>
    public static void AccentChanged()
    {
        if (_settings?.AllyLights == "accent") ApplyLights();
    }

    // ================================================================ the Xbox button's light

    /// <summary>Only the ROG Xbox Allys have the Xbox button (the commands were checked on the X20).</summary>
    public static bool HasXboxLight => AllyButtons.IsXboxAlly;

    public static readonly (int Level, string Name)[] XboxLightLevels = { (1, "Dim"), (2, "Medium"), (3, "Bright") };

    public static string XboxLightLevelName(int level) =>
        XboxLightLevels.FirstOrDefault(l => l.Level == Math.Clamp(level, 1, 3)).Name ?? "Bright";

    private static string _lastXbox = "";

    /// <summary>
    /// Applies the Xbox button's light settings; repeated identical requests are skipped unless forced. On or off is
    /// kept by the controller; brightness and breathing are re-sent after sleep (the controller forgets them).
    /// </summary>
    public static void ApplyXboxLight(bool force = false)
    {
        var s = _settings;
        if (s == null || !HasXboxLight) return;
        if (s.AllyXboxLight.Length == 0) { _lastXbox = ""; return; } // leave it alone (Armoury Crate SE decides)
        bool on = s.AllyXboxLight == "on";
        bool low = s.AllyXboxLightLowBattery && Battery.GetPercent() is int b && b <= 15 && !Battery.IsPluggedIn();
        bool dark = _playing && s.AllyXboxLightOffPlaying && !low;
        int level = dark ? 0 : low ? 3 : Math.Clamp(s.AllyXboxLightLevel, 1, 3);
        bool breathe = !dark && (low || s.AllyXboxLightBreathe);
        ApplyLightPower(force); // on or off is a bit in the lights' power mask
        if (!on) { _lastXbox = "off"; return; }
        string key = $"on{level}{breathe}";
        if (!force && key == _lastXbox) return;
        _lastXbox = key;
        Task.Run(async () =>
        {
            bool Send() => AllyHid.SetXboxLightLevel(level, breathe);
            try
            {
                if (Send()) return;
                AllyHid.Rescan();
                await Task.Delay(400);
                if (Send()) return;
            }
            catch (Exception) { }
            if (_lastXbox == key) _lastXbox = ""; // the next tick tries again
        });
    }

    // ================================================================ when the lights may light (Armoury Crate's power states)

    private static string _lastPower = "";

    /// <summary>
    /// The lights' power mask from Settings > ROG Ally: boot up and shut down, awake (on battery or plugged in, as
    /// SquareDeck re-sends it when the power source changes), sleep, and the Xbox button's glow. Sent only once
    /// something here (or the Xbox button light) has been chosen, so Armoury Crate SE's own setting otherwise stays.
    /// </summary>
    public static AllyHid.LightPower PowerMask(Settings s)
    {
        var mask = (AllyHid.LightPower)0;
        if (s.AllyLightBootShutdown) mask |= AllyHid.LightPower.Boot | AllyHid.LightPower.Shutdown;
        if (Battery.IsPluggedIn() ? s.AllyLightAwakePlugged : s.AllyLightAwakeBattery) mask |= AllyHid.LightPower.Awake;
        if (s.AllyLightSleep) mask |= AllyHid.LightPower.Sleep;
        // The Xbox button glows unless it was switched off (ASUS ships it on).
        if (HasXboxLight && s.AllyXboxLight != "off") mask |= AllyHid.LightPower.XboxButton;
        return mask;
    }

    public static void ApplyLightPower(bool force = false)
    {
        var s = _settings;
        if (s == null || !IsAlly) return;
        // Also while SquareDeck runs an effect: if the mask the controller keeps has "awake" off (from Armoury Crate SE,
        // or an earlier setting), the rings stay dark whatever effect is sent.
        if (!s.AllyLightPowerSet && s.AllyXboxLight.Length == 0 && s.AllyLights is "" or "off") { _lastPower = ""; return; }
        var mask = PowerMask(s);
        string key = ((byte)mask).ToString();
        if (!force && key == _lastPower) return;
        bool xboxBack = _lastPower.Length > 0 && byte.TryParse(_lastPower, out var was) && (was & 0x10) == 0 && mask.HasFlag(AllyHid.LightPower.XboxButton);
        _lastPower = key;
        Task.Run(async () =>
        {
            try
            {
                bool ok = AllyHid.SetLightPower(mask);
                if (!ok) { AllyHid.Rescan(); await Task.Delay(400); ok = AllyHid.SetLightPower(mask); }
                if (!ok && _lastPower == key) _lastPower = "";
                // The Xbox button relit: its brightness and effect again, and the rings' color (as Armoury Crate does).
                if (ok && xboxBack) { _lastXbox = ""; ApplyXboxLight(); }
            }
            catch (Exception) { if (_lastPower == key) _lastPower = ""; }
        });
    }

    // ---------------------------------------------------------------- off after a minute without input, on battery

    private static readonly DispatcherTimer IdleTicker = new() { Interval = TimeSpan.FromSeconds(2) };
    private static bool _idleDark;
    private static readonly uint[] PadPackets = new uint[4];
    private static DateTime _lastPadInput = DateTime.UtcNow;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PadState { public uint Packet; public ushort Buttons; public byte L, R; public short LX, LY, RX, RY; }

    [System.Runtime.InteropServices.DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint index, out PadState state);

    /// <summary>Seconds since the last touch, key, mouse move or controller input.</summary>
    private static double IdleSeconds()
    {
        try
        {
            for (uint i = 0; i < 4; i++)
                if (XInputGetState(i, out var pad) == 0 && pad.Packet != PadPackets[i]) { PadPackets[i] = pad.Packet; _lastPadInput = DateTime.UtcNow; }
        }
        catch (Exception) { }
        double sincePad = (DateTime.UtcNow - _lastPadInput).TotalSeconds;
        var info = new LastInputInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LastInputInfo>() };
        double other = GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) / 1000.0 : sincePad;
        return Math.Min(sincePad, other);
    }

    private static void IdleTick()
    {
        var s = _settings;
        // Only over an effect SquareDeck set ("leave as it is" has no brightness to come back to).
        bool want = s != null && s.AllyLightBatteryIdleOff && s.AllyLights is not ("" or "off") && !Battery.IsPluggedIn() && IdleSeconds() >= 60;
        if (want == _idleDark) return;
        _idleDark = want;
        ApplyLights(force: true); // dark now, or back the moment you touch it
    }

    /// <summary>After sleep the controller comes back with the light at its defaults: put SquareDeck's back.</summary>
    private static void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        // Plugged in or unplugged: "awake" may differ between the two.
        if (e.Mode == Microsoft.Win32.PowerModes.StatusChange) { ApplyLightPower(); ApplyLights(); return; }
        if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
        Task.Run(async () =>
        {
            // Give the controller a moment to start: a report sent too early is taken and then overwritten.
            await Task.Delay(TimeSpan.FromSeconds(3));
            AllyHid.Rescan();
            ApplyLightPower(force: true);
            ApplyXboxLight(force: true);
            await Task.Delay(TimeSpan.FromSeconds(5));
            ApplyXboxLight(force: true);
        });
    }

    private static void Tick()
    {
        if (_settings == null) return;
        // Battery level and low-battery pulse follow the battery; the rest only re-applies when something changed.
        ApplyLights();
    }

    // ================================================================ quick buttons

    /// <summary>Silent → Performance → Turbo → Silent. Returns the new mode's name.</summary>
    public static string NextAsusMode()
    {
        var s = _settings!;
        s.AllyAsusMode = s.AllyAsusMode switch { "silent" => "performance", "performance" => "turbo", _ => "silent" };
        s.Save();
        Task.Run(ApplyPower);
        return AsusModes.First(m => m.Id == s.AllyAsusMode).Name;
    }

    /// <summary>Quiet → Balanced → Cool → Full speed → Quiet. Returns the new curve's name.</summary>
    public static string NextFan()
    {
        var s = _settings!;
        s.AllyFan = s.AllyFan switch { "quiet" => "balanced", "balanced" => "cool", "cool" => "max", _ => "quiet" };
        s.Save();
        Task.Run(ApplyPower);
        return FanCurves.First(f => f.Id == s.AllyFan).Name;
    }
}
