using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32.SafeHandles;

namespace SquareDeck.Services;

/// <summary>
/// The ROG Ally family's firmware (ASUS's ATKACPI driver): power limits, ASUS operating modes, fan curves, the fan
/// reading and the battery charge limit.
///
/// Ported from HandheldCompanion by Valkirie (https://github.com/Valkirie/HandheldCompanion,
/// Devices/ASUS/AsusACPI.cs and ROGAlly.cs), which in turn follows G-Helper. The device ids and the fan curve
/// layout (8 temperatures, then 8 fan duties) are theirs.
///
/// HandheldCompanion is licensed CC BY-NC-SA 4.0 (https://creativecommons.org/licenses/by-nc-sa/4.0/); this file,
/// being adapted from it, is shared under the same licence.
/// </summary>
public static class AsusAcpi
{
    private const string FileName = @"\\.\ATKACPI";
    private const uint ControlCode = 0x0022240C;
    private const uint Dsts = 0x53545344, Devs = 0x53564544;

    public const uint CpuFanCurve = 0x00110024, GpuFanCurve = 0x00110025, MidFanCurve = 0x00110032;
    public const uint CpuFan = 0x00110013;
    public const uint BatteryLimit = 0x00120057;
    public const uint PptSustained = 0x001200A3, PptSlow = 0x001200A0, PptFast = 0x001200C1;
    public const uint PerformanceMode = 0x00120075;

    // ASUS's operating modes as the firmware numbers them.
    public const int ModePerformance = 0, ModeTurbo = 1, ModeSilent = 2;

    /// <summary>The fan curves the firmware ships with (HandheldCompanion: defaultCPUFan / defaultGPUFan).</summary>
    public static readonly byte[] DefaultCpuCurve = { 0x3A, 0x3D, 0x40, 0x44, 0x48, 0x4D, 0x51, 0x62, 0x08, 0x11, 0x16, 0x1A, 0x22, 0x29, 0x30, 0x45 };
    public static readonly byte[] DefaultGpuCurve = { 0x3A, 0x3D, 0x40, 0x44, 0x48, 0x4D, 0x51, 0x62, 0x0C, 0x16, 0x1D, 0x1F, 0x26, 0x2D, 0x34, 0x4A };

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, uint inputSize, byte[] output, uint outputSize, ref uint returned, IntPtr overlapped);

    private static readonly object Lock = new();
    private static SafeFileHandle? _handle;
    private static bool _tried;

    /// <summary>ASUS's firmware driver is there and could be opened (an Ally, or another ASUS with Armoury Crate's driver).</summary>
    public static bool Available
    {
        get
        {
            lock (Lock)
            {
                if (_handle is { IsInvalid: false, IsClosed: false }) return true;
                if (_tried) return false;
                _tried = true;
                try
                {
                    const uint Read = 0x80000000, Write = 0x40000000, ShareRead = 1, ShareWrite = 2, OpenExisting = 3, Normal = 0x80;
                    var h = CreateFile(FileName, Read | Write, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, Normal, IntPtr.Zero);
                    if (h.IsInvalid) { h.Dispose(); return false; }
                    _handle = h;
                    return true;
                }
                catch (Exception) { return false; }
            }
        }
    }

    private static byte[] Call(uint method, byte[] args)
    {
        var input = new byte[8 + args.Length];
        var output = new byte[16];
        BitConverter.GetBytes(method).CopyTo(input, 0);
        BitConverter.GetBytes((uint)args.Length).CopyTo(input, 4);
        args.CopyTo(input, 8);
        lock (Lock)
        {
            if (_handle is not { IsInvalid: false, IsClosed: false }) return output;
            uint returned = 0;
            try { DeviceIoControl(_handle, ControlCode, input, (uint)input.Length, output, (uint)output.Length, ref returned, IntPtr.Zero); }
            catch (Exception) { }
        }
        return output;
    }

    private static int Set(uint device, int value)
    {
        if (!Available) return -1;
        var args = new byte[8];
        BitConverter.GetBytes(device).CopyTo(args, 0);
        BitConverter.GetBytes((uint)value).CopyTo(args, 4);
        return BitConverter.ToInt32(Call(Devs, args), 0);
    }

    private static int Set(uint device, byte[] parameters)
    {
        if (!Available) return -1;
        var args = new byte[4 + parameters.Length];
        BitConverter.GetBytes(device).CopyTo(args, 0);
        parameters.CopyTo(args, 4);
        return BitConverter.ToInt32(Call(Devs, args), 0);
    }

    private static int Get(uint device)
    {
        if (!Available) return -1;
        var args = new byte[8];
        BitConverter.GetBytes(device).CopyTo(args, 0);
        return BitConverter.ToInt32(Call(Dsts, args), 0) - 65536;
    }

    // CPU core configuration (Armoury Crate's "CPU Core Configuration"): values are 0x0E0P, E-cores in the high byte,
    // P-cores in the low byte. The endpoints were documented by G-Helper; SquareDeck's own code.
    public const uint CoresNow = 0x001200D2, CoresMax = 0x001200D3, CoresMin = 0x001200D4;

    /// <summary>(E-cores, P-cores) from one of the core endpoints, or null when the firmware doesn't offer it.</summary>
    public static (int E, int P)? Cores(uint device = CoresNow)
    {
        int value = Get(device);
        if (value < 0) return null;
        return ((value >> 8) & 0xFF, value & 0xFF);
    }

    /// <summary>Asks the firmware for this many cores from the next restart. At least one P-core is always kept.</summary>
    public static bool SetCores(int e, int p)
    {
        if (!Available || e is < 0 or > 16 || p is < 1 or > 16) return false;
        return Set(CoresNow, (e << 8) | p) >= 0;
    }

    /// <summary>The power limits in watts: sustained (SPL), slow (sPPT) and fast (fPPT) boost.</summary>
    public static bool SetPowerLimits(int sustained, int slow, int fast)
    {
        if (!Available) return false;
        // The firmware may hold a limit under the one above it, so they're written top down, then once more bottom up:
        // whichever way the limits move, all three end up where they should be.
        int a = Set(PptFast, fast), b = Set(PptSlow, slow), c = Set(PptSustained, sustained);
        Set(PptSlow, slow);
        Set(PptFast, fast);
        // A negative answer is the firmware saying no (or not offering that limit).
        return a >= 0 && b >= 0 && c >= 0;
    }

    /// <summary>Silent, Performance or Turbo, the same as Armoury Crate's operating modes.</summary>
    public static bool SetMode(int mode) => Available && Set(PerformanceMode, mode) >= 0;

    // The startup chime ("POST sound"), the same switch as the BIOS's and Armoury Crate SE's. It's kept by the firmware,
    // so it stays as set across restarts and needs no reapplying. Device id as documented by Linux's asus-armoury driver.
    public const uint BootSoundDevice = 0x00130022;

    /// <summary>Whether the Ally plays its sound when it turns on; null when the firmware doesn't offer the switch.</summary>
    public static bool? BootSound
    {
        get
        {
            if (!Available) return null;
            int v = Get(BootSoundDevice); // presence bit taken off: 0 or 1 when it's there
            return v is 0 or 1 ? v == 1 : null;
        }
    }

    /// <summary>Turns the startup sound on or off. True when the firmware took it.</summary>
    public static bool SetBootSound(bool on) => Available && Set(BootSoundDevice, on ? 1 : 0) >= 0 && BootSound == on;

    /// <summary>Stops charging at this percentage (100 = charge fully).</summary>
    public static bool SetChargeLimit(int percent) => Available && percent is >= 20 and <= 100 && Set(BatteryLimit, percent) >= 0;

    /// <summary>A fan curve (8 temperatures in °C, then 8 duties in %) on all three fans.</summary>
    public static bool SetFanCurve(byte[] curve)
    {
        if (!Available || curve.Length != 16) return false;
        var c = (byte[])curve.Clone();
        // Some firmware misbehaves at exactly 100% (HandheldCompanion, after G-Helper): cap at 99.
        for (int i = 8; i < 16; i++) c[i] = Math.Min((byte)99, c[i]);
        Set(CpuFanCurve, c);
        Set(GpuFanCurve, c);
        Set(MidFanCurve, c);
        return true;
    }

    /// <summary>Back to the firmware's own curves.</summary>
    public static void ResetFanCurves()
    {
        if (!Available) return;
        Set(CpuFanCurve, (byte[])DefaultCpuCurve.Clone());
        Set(GpuFanCurve, (byte[])DefaultGpuCurve.Clone());
        Set(MidFanCurve, (byte[])DefaultCpuCurve.Clone());
    }

    // Temperatures and the other fans (as G-Helper reads them).
    public const uint CpuTemp = 0x00120094, GpuTemp = 0x00120097, GpuFan = 0x00110014;

    /// <summary>A temperature in °C from the firmware, or null.</summary>
    public static int? Temperature(uint device)
    {
        if (!Available) return null;
        int value = Get(device);
        return value is > 0 and < 120 ? value : null;
    }

    /// <summary>A fan's speed in RPM (the firmware reports hundreds of RPM), or null.</summary>
    public static int? FanRpm(uint device)
    {
        if (!Available) return null;
        int value = Get(device);
        return value is >= 0 and <= 150 ? value * 100 : null;
    }

    /// <summary>The CPU fan's speed in percent, or null when it can't be read.</summary>
    public static int? FanPercent()
    {
        if (!Available) return null;
        int value = Get(CpuFan);
        return value is >= 0 and <= 100 ? value : null;
    }
}

/// <summary>
/// The Ally's controller and lights, through its own HID collections: report 0x5A (the controller: game or mouse mode,
/// back-button keys, switching the built-in controller off) and report 0x5D (the RGB rings around the sticks).
///
/// The messages are HandheldCompanion's (Devices/ASUS/ROGAlly.cs: AuraMessage, ConfigureController, XBoxController,
/// SetLedBrightness). Which collection is which is found the same way: whichever answers a feature request with that
/// report id.
/// </summary>
public static class AllyHid
{
    public const byte InputId = 0x5A, AuraId = 0x5D;

    [StructLayout(LayoutKind.Sequential)]
    private struct HidCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)] public ushort[] Counts;
    }

    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HidCaps caps);
    [DllImport("hid.dll")] private static extern bool HidD_GetFeature(SafeFileHandle device, byte[] buffer, int length);
    [DllImport("hid.dll")] private static extern bool HidD_SetFeature(SafeFileHandle device, byte[] buffer, int length);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, int length, out int written, IntPtr overlapped);

    private sealed class Device
    {
        public required SafeFileHandle Handle;
        public int FeatureLength, OutputLength;
    }

    private static readonly object Lock = new();
    private static Device? _input, _aura;
    private static DateTime _lastScan;

    private static Device? Open(string path)
    {
        const uint Read = 0x80000000, Write = 0x40000000, ShareRead = 1, ShareWrite = 2, OpenExisting = 3;
        var handle = CreateFile(path, Read | Write, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); return null; }
        var caps = new HidCaps();
        if (HidD_GetPreparsedData(handle, out var data))
        {
            try { HidP_GetCaps(data, ref caps); }
            finally { HidD_FreePreparsedData(data); }
        }
        return new Device { Handle = handle, FeatureLength = caps.FeatureReportByteLength, OutputLength = caps.OutputReportByteLength };
    }

    private static bool Answers(Device d, byte id)
    {
        if (d.FeatureLength <= 1) return false;
        var buffer = new byte[d.FeatureLength];
        buffer[0] = id;
        return HidD_GetFeature(d.Handle, buffer, buffer.Length);
    }

    /// <summary>Finds the two collections (again at most every 10 seconds, so a missing one doesn't cost every call).</summary>
    private static void Scan()
    {
        if ((_input != null && _aura != null) || DateTime.UtcNow - _lastScan < TimeSpan.FromSeconds(10)) return;
        _lastScan = DateTime.UtcNow;
        foreach (var path in AllyButtons.AsusHidPaths())
        {
            if (_input != null && _aura != null) break;
            Device? d = null;
            try
            {
                d = Open(path);
                if (d == null) continue;
                if (_input == null && Answers(d, InputId)) { _input = d; d = null; }
                else if (_aura == null && Answers(d, AuraId)) { _aura = d; d = null; }
            }
            catch (Exception) { }
            finally { d?.Handle.Dispose(); }
        }
    }

    /// <summary>Looks for the collections again on the next call (one went missing or a write failed).</summary>
    public static void Rescan()
    {
        lock (Lock) { _lastScan = DateTime.MinValue; _inputAwake = _auraAwake = false; }
    }

    // ---------------------------------------------------------------- waking the controller without ASUS's service

    // The Ally's controller chip starts out ignoring light commands and keeping its extra-button codes to itself until
    // the PC introduces itself. ASUS Optimization does that when it starts; without it, SquareDeck does. The messages
    // are the ASUS keyboard handshake ("ASUS Tech.Inc." after the report id, then "which functions do you have"), and
    // for the controller collection the two commands that take it out of its out-of-the-box mode. Byte values as
    // documented by the Linux hid-asus driver and asusctl; HandheldCompanion and G-Helper talk to the same reports.
    private static bool _inputAwake, _auraAwake;

    /// <summary>SquareDeck did the introduction itself (ASUS Optimization wasn't running), for the light log.</summary>
    public static bool WokeItself { get; private set; }

    private static byte[] Hello(byte id)
    {
        var text = System.Text.Encoding.ASCII.GetBytes("ASUS Tech.Inc.");
        var m = new byte[text.Length + 2];
        m[0] = id;
        text.CopyTo(m, 1);
        return m;
    }

    /// <summary>
    /// Introduces SquareDeck to the controller and lights once (again after sleep or a rescan), when ASUS
    /// Optimization isn't running to do it, so the stick lights take changes and the extra buttons send their codes.
    /// </summary>
    public static void EnsureAwake()
    {
        lock (Lock)
        {
            Scan();
            if ((_input == null || _inputAwake) && (_aura == null || _auraAwake)) return;
            bool asus = false;
            try { asus = AsusOptimization.Running; } catch (Exception) { }
            if (asus) { _inputAwake = _input != null; _auraAwake = _aura != null; return; } // ASUS's service has done it
            if (_input != null && !_inputAwake)
            {
                Feature(_input, Hello(InputId));
                Feature(_input, new byte[] { InputId, 0x05, 0x20, 0x31, 0x00, 0x08 });
                Feature(_input, new byte[] { InputId, 0xBA, 0xC5, 0xC4 });
                Feature(_input, new byte[] { InputId, 0xD0, 0x8F, 0x01 }); // out-of-the-box mode off
                Feature(_input, new byte[] { InputId, 0xD0, 0x85, 0xFF });
                _inputAwake = true;
            }
            if (_aura != null && !_auraAwake)
            {
                Feature(_aura, new byte[] { AuraId, 0xB9 });
                Feature(_aura, Hello(AuraId));
                Feature(_aura, new byte[] { AuraId, 0x05, 0x20, 0x31, 0x00, 0x08 });
                _auraAwake = true;
            }
            WokeItself = true;
        }
    }

    public static bool HasLights { get { lock (Lock) { Scan(); return _aura != null; } } }

    /// <summary>Which collections were found, for the light log.</summary>
    public static string Describe()
    {
        lock (Lock)
        {
            Scan();
            int paths = AllyButtons.AsusHidPaths().Count;
            string Of(Device? d) => d == null ? "not found" : $"found (feature {d.FeatureLength}, output {d.OutputLength})";
            return $"{paths} ASUS HID paths, controller {Of(_input)}, lights {Of(_aura)}" + (WokeItself ? ", woken by SquareDeck" : "");
        }
    }
    public static bool HasController { get { lock (Lock) { Scan(); return _input != null; } } }

    private static byte[] Pad(byte[] message, int length)
    {
        var buffer = new byte[Math.Max(length, message.Length)];
        message.CopyTo(buffer, 0);
        return buffer;
    }

    private static bool Feature(Device? d, byte[] message)
    {
        if (d == null || d.FeatureLength <= 0) return false;
        try { return HidD_SetFeature(d.Handle, Pad(message, d.FeatureLength), d.FeatureLength); }
        catch (Exception) { return false; }
    }

    /// <summary>An output report when the collection has them (as HandheldCompanion's WriteReport), else a feature report.</summary>
    private static bool Output(Device? d, byte[] message)
    {
        if (d == null) return false;
        if (d.OutputLength <= 0) return Feature(d, message);
        try { return WriteFile(d.Handle, Pad(message, d.OutputLength), d.OutputLength, out _, IntPtr.Zero) || Feature(d, message); }
        catch (Exception) { return false; }
    }

    private static void Drop(ref Device? d)
    {
        try { d?.Handle.Dispose(); } catch (Exception) { }
        d = null;
        _inputAwake = _input != null && _inputAwake;
        _auraAwake = _aura != null && _auraAwake;
    }

    // ---------------------------------------------------------------- lights

    public enum AuraMode : byte { Solid = 0, Breathing = 1, Wheel = 2, Rainbow = 3, Wave = 4 }
    public enum AuraSpeed : byte { Slow = 0xEB, Medium = 0xF5, Fast = 0xE1 }
    public enum Zone : byte { All = 0, LeftOuter = 1, LeftInner = 2, RightInner = 3, RightOuter = 4 }

    private static byte[] AuraMessage(AuraMode mode, Color one, Color two, AuraSpeed speed, Zone zone) => new byte[]
    {
        AuraId, 0xB3, (byte)zone, (byte)mode, one.R, one.G, one.B, (byte)speed, 0,
        mode == AuraMode.Breathing ? (byte)1 : (byte)0, two.R, two.G, two.B, 0, 0, 0, 0
    };

    private static readonly byte[] Apply = { AuraId, 0xB4 };
    private static readonly byte[] Commit = { AuraId, 0xB5, 0, 0, 0 };

    /// <summary>One effect on both rings. Breathing alternates between the two colors.</summary>
    public static bool SetLights(AuraMode mode, Color one, Color two, AuraSpeed speed)
    {
        lock (Lock)
        {
            Scan();
            if (_aura == null) return false;
            bool ok = Output(_aura, AuraMessage(mode, one, two, speed, Zone.All)) && Output(_aura, Apply) && Output(_aura, Commit);
            if (!ok) Drop(ref _aura);
            return ok;
        }
    }

    /// <summary>
    /// A solid color per stick. Each zone message is followed by "apply" (as asusctl does for the Ally): without it the
    /// controller keeps only the last zone it was sent, so just one stick changed. The whole ring is set to the left
    /// color first, so both sticks light even if a zone is ignored, then "set" saves it.
    /// </summary>
    public static bool SetSticks(Color left, Color right)
    {
        lock (Lock)
        {
            Scan();
            if (_aura == null) return false;
            bool ok = Output(_aura, AuraMessage(AuraMode.Solid, left, left, AuraSpeed.Slow, Zone.All)) && Output(_aura, Apply);
            if (ok)
            {
                foreach (var (zone, color) in new[] { (Zone.LeftOuter, left), (Zone.LeftInner, left), (Zone.RightInner, right), (Zone.RightOuter, right) })
                {
                    ok &= Output(_aura, AuraMessage(AuraMode.Solid, color, color, AuraSpeed.Slow, zone));
                    ok &= Output(_aura, Apply);
                }
                ok &= Output(_aura, Commit);
            }
            if (!ok) Drop(ref _aura);
            return ok;
        }
    }

    /// <summary>
    /// One color on the whole ring, shown straight away but not saved to the controller (no "set"): for effects
    /// SquareDeck animates itself, which change many times a second.
    /// </summary>
    public static bool SetLive(Color color)
    {
        lock (Lock)
        {
            Scan();
            if (_aura == null) return false;
            bool ok = Output(_aura, AuraMessage(AuraMode.Solid, color, color, AuraSpeed.Slow, Zone.All)) && Output(_aura, Apply);
            if (!ok) Drop(ref _aura);
            return ok;
        }
    }

    /// <summary>A color per zone (left outer, left inner, right inner, right outer), shown straight away, not saved.</summary>
    public static bool SetZonesLive(IReadOnlyList<Color> zones)
    {
        if (zones.Count < 4) return false;
        lock (Lock)
        {
            Scan();
            if (_aura == null) return false;
            bool ok = true;
            var order = new[] { Zone.LeftOuter, Zone.LeftInner, Zone.RightInner, Zone.RightOuter };
            for (int z = 0; z < 4 && ok; z++)
                ok = Output(_aura, AuraMessage(AuraMode.Solid, zones[z], zones[z], AuraSpeed.Slow, order[z])) && Output(_aura, Apply);
            if (!ok) Drop(ref _aura);
            return ok;
        }
    }

    /// <summary>Brightness 0 (off) to 3.</summary>
    public static bool SetBrightness(int level)
    {
        lock (Lock)
        {
            Scan();
            return Feature(_aura, new byte[] { AuraId, 0xBA, 0xC5, 0xC4, (byte)Math.Clamp(level, 0, 3) });
        }
    }

    // ---------------------------------------------------------------- the Xbox button's light (ROG Xbox Ally)

    // The lights' power states: one mask for when the rings may light (Armoury Crate SE's "Boot up, shut down",
    // "Awake" and "Sleep"), and on the ROG Xbox Ally whether the Xbox button glows. Bits as G-Helper (Aura.cs,
    // ApplyAllyPower) and the Linux hid-asus-ally driver use them; the Xbox button's bit and the other two commands
    // are the ones Armoury Crate SE sends, captured over USB and checked on an X20 by ally-x20-steamos:
    //   power mask  5A D1 09 01 <mask>     feature report
    //   breathing   5A D1 21 02 01|00 08   feature report (the Xbox button)
    //   brightness  5D D1 20 01 0..3       output report (the Xbox button: 0 is dark, 3 the brightest)
    // The Xbox button's brightness and breathing don't survive a power-off or sleep: back to 3 and steady.

    [Flags]
    public enum LightPower : byte { Boot = 0x01, Awake = 0x02, Sleep = 0x04, Shutdown = 0x08, XboxButton = 0x10 }

    /// <summary>When the lights may light (and whether the Xbox button glows).</summary>
    public static bool SetLightPower(LightPower mask)
    {
        lock (Lock)
        {
            Scan();
            return Feature(_input, new byte[] { InputId, 0xD1, 0x09, 0x01, (byte)mask });
        }
    }

    /// <summary>The Xbox button's light: brightness 0 (dark) to 3, breathing or steady.</summary>
    public static bool SetXboxLightLevel(int level, bool breathing)
    {
        lock (Lock)
        {
            Scan();
            bool ok = Feature(_input, new byte[] { InputId, 0xD1, 0x21, 0x02, breathing ? (byte)1 : (byte)0, 0x08 });
            // Armoury Crate sends the level after the effect, so the light takes both.
            ok &= Output(_aura, new byte[] { AuraId, 0xD1, 0x20, 0x01, (byte)Math.Clamp(level, 0, 3) });
            return ok;
        }
    }

    // ---------------------------------------------------------------- controller

    private static readonly byte[] ModeGame = { 0x5A, 0xD1, 0x01, 0x01, 0x01 };
    private static readonly byte[] ModeMouse = { 0x5A, 0xD1, 0x01, 0x01, 0x03 };

    // HandheldCompanion's ConfigureController sequence: every control back to its default, M1/M2 either as ASUS
    // ships them or as F18 / F17, then the commit messages.
    private static readonly byte[][] Defaults =
    {
        new byte[] { 0x5A, 0xD1, 0x02, 0x01, 0x2C, 0x01, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x19, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x03, 0x8C, 0x88, 0x76 },
        new byte[] { 0x5A, 0xD1, 0x02, 0x02, 0x2C, 0x01, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x02, 0x82, 0x23, 0x00, 0x00, 0x00, 0x01, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x02, 0x82, 0x0D },
        new byte[] { 0x5A, 0xD1, 0x02, 0x03, 0x2C, 0x01, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x08 },
        new byte[] { 0x5A, 0xD1, 0x02, 0x04, 0x2C, 0x01, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x06 },
        new byte[] { 0x5A, 0xD1, 0x02, 0x05, 0x2C, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x16, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x02, 0x82, 0x31 },
        new byte[] { 0x5A, 0xD1, 0x02, 0x06, 0x2C, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x02, 0x82, 0x4D, 0x00, 0x00, 0x00, 0x01, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x1E },
        new byte[] { 0x5A, 0xD1, 0x02, 0x07, 0x2C, 0x01, 0x11, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x12 },
    };
    private static readonly byte[] BackDefault = { 0x5A, 0xD1, 0x02, 0x08, 0x2C, 0x02, 0x00, 0x8E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x8E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x8F };
    private static readonly byte[] BackAsKeys = { 0x5A, 0xD1, 0x02, 0x08, 0x2C, 0x02, 0x00, 0x28, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x30 };
    private static readonly byte[] Triggers = { 0x5A, 0xD1, 0x02, 0x09, 0x2C, 0x01, 0x0D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x0E };
    // The commit messages (as HandheldCompanion sends them): turbo off, then vibration strength and the stick and trigger
    // dead zones. Those three are SquareDeck's own settings (ASUS's defaults: 100% vibration, no dead zones).
    private static byte[][] CommitController => new[]
    {
        new byte[] { 0x5A, 0xD1, 0x0F, 0x20 }, VibrationMessage, StickMessage, TriggerMessage,
    };

    /// <summary>The controller's own tuning, kept so the commit after a layout change sends it too. Percentages.</summary>
    private static (int Left, int Right) _vibration = (100, 100);
    private static (int Inner, int Outer) _sticks = (0, 100), _triggers = (0, 100);

    private static byte P(int percent) => (byte)Math.Clamp(percent, 0, 100);
    private static byte[] VibrationMessage => new byte[] { 0x5A, 0xD1, 0x06, 0x02, P(_vibration.Left), P(_vibration.Right) };
    private static byte[] StickMessage => new byte[] { 0x5A, 0xD1, 0x04, 0x04, P(_sticks.Inner), P(_sticks.Outer), P(_sticks.Inner), P(_sticks.Outer) };
    private static byte[] TriggerMessage => new byte[] { 0x5A, 0xD1, 0x05, 0x04, P(_triggers.Inner), P(_triggers.Outer), P(_triggers.Inner), P(_triggers.Outer) };

    /// <summary>
    /// The built-in controller's own tuning, in its firmware (so it works in every game, not just SquareDeck):
    /// rumble strength of the left and right motors, and the inner (dead zone) and outer (reach full at) limits of
    /// both sticks and both triggers, in percent.
    /// </summary>
    public static bool SetTuning(int vibration, int stickInner, int stickOuter, int triggerInner, int triggerOuter)
    {
        lock (Lock)
        {
            _vibration = (vibration, vibration);
            _sticks = (stickInner, Math.Max(stickInner + 5, stickOuter));
            _triggers = (triggerInner, Math.Max(triggerInner + 5, triggerOuter));
            Scan();
            if (_input == null) return false;
            bool ok = Feature(_input, VibrationMessage) & Feature(_input, StickMessage) & Feature(_input, TriggerMessage);
            if (!ok) Drop(ref _input);
            return ok;
        }
    }

    /// <summary>
    /// The controller's layout back to ASUS's defaults, in game or mouse mode, with M1 / M2 sending F18 / F17 when
    /// <paramref name="backKeys"/> is set (so SquareDeck, or a game, can use them). This undoes custom mappings made in
    /// Armoury Crate SE.
    /// </summary>
    public static bool ConfigureController(bool backKeys, bool mouse = false)
    {
        lock (Lock)
        {
            Scan();
            if (_input == null) return false;
            bool ok = Feature(_input, mouse ? ModeMouse : ModeGame);
            if (!ok) { Drop(ref _input); return false; }
            foreach (var m in Defaults) Feature(_input, m);
            Feature(_input, backKeys ? BackAsKeys : BackDefault);
            Feature(_input, Triggers);
            foreach (var m in CommitController) Feature(_input, m);
            return true;
        }
    }

    /// <summary>Game mode (a controller) or mouse mode (the sticks and buttons drive the mouse).</summary>
    public static bool SetMouseMode(bool mouse)
    {
        lock (Lock)
        {
            Scan();
            bool ok = Feature(_input, mouse ? ModeMouse : ModeGame);
            if (!ok) Drop(ref _input);
            return ok;
        }
    }

    /// <summary>Switches the built-in Xbox controller off (when another one is used) or back on.</summary>
    public static bool SetBuiltInController(bool on)
    {
        lock (Lock)
        {
            Scan();
            return Feature(_input, new byte[] { 0x5A, 0xD1, 0x0B, 0x01, on ? (byte)0x01 : (byte)0x02 });
        }
    }
}
