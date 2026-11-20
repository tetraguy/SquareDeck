using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// The ASUS ROG Ally's extra buttons (Armoury Crate, Command Center / Library, and Mode+ combinations),
/// read from the device's own HID collection with Windows raw input.
///
/// How the right collection is found (the one whose reports start with report id 0x5A), the ROG Xbox Ally X20's
/// board id (RC74XA) and the button codes per model follow HandheldCompanion by Valkirie
/// (https://github.com/Valkirie/HandheldCompanion, Devices/ASUS/ROGAlly.cs and Devices/IDevice.cs).
///
/// The button codes, device ids and the Mode+ logic follow AnyFSE by Artem Shpynov
/// (https://github.com/ashpynov/AnyFSE, MIT licence, src/Ally/Ally.cpp). AnyFSE also injects a filter into
/// ASUS's AsusOptimization.exe so Armoury Crate SE stops reacting to the same presses; that part is
/// deliberately not copied here (it needs code injection into another company's process).
/// </summary>
public static class AllyButtons
{
    // Codes in byte 1 of the device's report (AnyFSE: Ally::EventCode).
    public const byte Release = 0, ACPress = 56, ToggleMicrophone = 124, LibraryPress = 147, ScreenShot = 160,
        ShowKeyboard = 162, ToggleRecord = 164, ModePress = 165, CCPress = 166, ACHold = 167, ACHoldRelease = 168;

    /// <summary>The report id the buttons arrive with (HandheldCompanion: ROGAlly.INPUT_HID_ID).</summary>
    public const byte InputReportId = 0x5A;

    /// <summary>The buttons that can be given an action, with the names shown in Settings.</summary>
    public static readonly (string Id, string Name)[] Buttons =
    {
        ("ac", "Armoury Crate button"), ("achold", "Armoury Crate button (hold)"), ("cc", "Command Center button"),
        ("library", "Xbox Library button"), ("mode-ac", "Mode + Armoury Crate"), ("mode-achold", "Mode + Armoury Crate (hold)"),
        ("mode-cc", "Mode + Command Center"), ("mode-library", "Mode + Xbox Library"),
        ("m1", "M1 back button"), ("m2", "M2 back button")
    };

    /// <summary>What a button can do.</summary>
    public static readonly (string Id, string Name)[] Actions =
    {
        ("", "Nothing"), ("home", "Open SquareDeck"), ("quickmenu", "SquareDeck quick menu"), ("taskswitcher", "Task switcher"),
        ("taskview", "Task view (Win+Tab)"), ("gamebar", "Game Bar"), ("commandcenter", "Command Center (Game Bar widget)"),
        ("armourycrate", "Armoury Crate SE"), ("keyboard", "On-screen keyboard"), ("screenshot", "Take a screenshot"),
        ("record", "Start or stop recording"), ("mic", "Mute or unmute the microphone"), ("taskmanager", "SquareDeck Task Manager"),
        ("nightlight", "Night light on or off"), ("desktop", "Show the desktop"),
        ("lights", "Ally lights on or off"), ("asusmode", "Next ASUS mode (Silent / Performance / Turbo)"),
        ("fan", "Next fan setting"), ("padmouse", "Controller or mouse mode"),
        ("amdoverlay", "Radeon overlay (Alt + R)"), ("amdmetrics", "Radeon performance metrics (Ctrl + Shift + O)")
    };

    /// <summary>Defaults like AnyFSE's: Command Center on AC, task switcher on hold, the launcher on CC / Library.</summary>
    public static Dictionary<string, string> DefaultBindings() => new()
    {
        ["ac"] = "commandcenter", ["achold"] = "taskswitcher", ["cc"] = "home", ["library"] = "home",
        ["mode-ac"] = "armourycrate", ["mode-achold"] = "", ["mode-cc"] = "", ["mode-library"] = "",
        ["m1"] = "", ["m2"] = ""
    };

    /// <summary>The models, for Settings > ROG Ally buttons > Which Ally is this.</summary>
    public static readonly (string Id, string Name)[] Models =
    {
        ("", "Find out by itself"), ("ROG Ally", "ROG Ally"), ("ROG Ally X", "ROG Ally X"),
        ("ROG Xbox Ally", "ROG Xbox Ally"), ("ROG Xbox Ally X", "ROG Xbox Ally X"), ("ROG Xbox Ally X20", "ROG Xbox Ally X20"),
    };

    /// <summary>Chosen in Settings when it isn't found right; "" = found by itself.</summary>
    public static string ModelOverride { get; set; } = "";

    /// <summary>Which Ally this is: chosen in Settings, else from the board and product names Windows records (AnyFSE: GetRogAllyVersion).</summary>
    public static string Model => ModelOverride.Length > 0 ? ModelOverride : DetectedModel;

    private static string? _detected;
    public static string DetectedModel
    {
        get
        {
            if (_detected != null) return _detected;
            try
            {
                const string Key = @"HKEY_LOCAL_MACHINE\SYSTEM\HardwareConfig\Current";
                var board = (Registry.GetValue(Key, "BaseBoardProduct", "") as string ?? "").Trim();
                var product = (Registry.GetValue(Key, "SystemProductName", "") as string ?? "").Trim();
                var family = (Registry.GetValue(Key, "SystemFamily", "") as string ?? "").Trim();
                string all = (board + " " + product + " " + family).ToUpperInvariant();
                _detected =
                    board.StartsWith("RC74", StringComparison.OrdinalIgnoreCase) || all.Contains("X20") ? "ROG Xbox Ally X20" :
                    board.StartsWith("RC73X", StringComparison.OrdinalIgnoreCase) || all.Contains("XBOX ALLY X") ? "ROG Xbox Ally X" :
                    board.StartsWith("RC73", StringComparison.OrdinalIgnoreCase) || all.Contains("XBOX ALLY") ? "ROG Xbox Ally" :
                    board.StartsWith("RC72", StringComparison.OrdinalIgnoreCase) || all.Contains("ALLY X") ? "ROG Ally X" :
                    board.StartsWith("RC71", StringComparison.OrdinalIgnoreCase) || all.Contains("ROG ALLY") ? "ROG Ally" : "";
            }
            catch (Exception) { _detected = ""; }
            return _detected;
        }
    }

    /// <summary>The Xbox versions have a Library button (AnyFSE: IsXBoxRogAlly).</summary>
    public static bool IsXboxAlly => Model.StartsWith("ROG Xbox");

    /// <summary>
    /// The first two Xbox Allys swap the AC and CC codes, as AnyFSE reads them. The X20 sends the Library button as its
    /// own code (147) and the other codes as on the first Ally, which is how HandheldCompanion reads every Ally.
    /// </summary>
    public static bool SwapsCodes => IsXboxAlly && Model != "ROG Xbox Ally X20";

    /// <summary>The last code the buttons sent and when (for the button test in Settings).</summary>
    public static byte LastCode { get; private set; }
    public static DateTime LastCodeAt { get; private set; }

    /// <summary>Raised for every report from the buttons, with its code (on the window's thread).</summary>
    public static event Action<byte>? Heard;

    /// <summary>A readable name for a code.</summary>
    public static string NameOf(byte code) => code switch
    {
        Release => "Button let go",
        ACPress => SwapsCodes ? "Xbox Library button" : "Armoury Crate button",
        CCPress => SwapsCodes ? "Armoury Crate button" : "Command Center button",
        ACHold => SwapsCodes ? "Xbox Library button (hold)" : "Armoury Crate button (hold)",
        ACHoldRelease => "Hold let go",
        LibraryPress => "Xbox Library button",
        ModePress => "Mode (back button)",
        ToggleMicrophone => "Microphone key",
        ScreenShot => "Screenshot key",
        ShowKeyboard => "Keyboard key",
        ToggleRecord => "Record key",
        _ => "Unknown button"
    };

    // ---------------------------------------------------------------- raw input

    private const uint RimTypeHid = 2, RidiDeviceName = 0x20000007, RidiDeviceInfo = 0x2000000B, RidInput = 0x10000003;
    private const uint RidevInputSink = 0x00000100, RidevRemove = 0x00000001;
    public const int WmInput = 0x00FF;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceList { public IntPtr Device; public uint Type; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage; public ushort Usage; public uint Flags; public IntPtr Target; }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct DeviceInfo
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(4)] public uint Type;
        [FieldOffset(8)] public uint VendorId;
        [FieldOffset(12)] public uint ProductId;
        [FieldOffset(16)] public uint Version;
        [FieldOffset(20)] public ushort UsagePage;
        [FieldOffset(22)] public ushort Usage;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([Out] RawInputDeviceList[]? list, ref uint count, uint size);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    private static IntPtr _device;
    private static readonly HashSet<IntPtr> _devices = new();
    private static readonly List<RawInputDevice> _registered = new();
    private static bool _modeHeld;

    private record struct Collection(IntPtr Device, ushort UsagePage, ushort Usage, bool Known, bool Preferred);

    /// <summary>
    /// The Ally's vendor collections that may carry the buttons: ASUS (VID 0B05) devices with PID 1ABE or 1B4C (the
    /// ROG Ally, and the Ally X and all Xbox Allys including the X20, per HandheldCompanion), or any ASUS PID when
    /// none of those are there. Only vendor-defined usage pages, so keyboards and mice are never listened to.
    /// Which one really sends the buttons is decided by the report id (0x5A) when a report comes in.
    /// </summary>
    private static List<Collection> FindCollections()
    {
        var found = new List<Collection>();
        try
        {
            uint count = 0;
            uint size = (uint)Marshal.SizeOf<RawInputDeviceList>();
            GetRawInputDeviceList(null, ref count, size);
            if (count == 0) return found;
            var list = new RawInputDeviceList[count];
            if (GetRawInputDeviceList(list, ref count, size) == uint.MaxValue) return found;

            foreach (var d in list.Where(d => d.Type == RimTypeHid))
            {
                uint chars = 0;
                GetRawInputDeviceInfo(d.Device, RidiDeviceName, IntPtr.Zero, ref chars);
                if (chars == 0) continue;
                string path;
                var buffer = Marshal.AllocHGlobal((int)chars * 2);
                try
                {
                    if (GetRawInputDeviceInfo(d.Device, RidiDeviceName, buffer, ref chars) == uint.MaxValue) continue;
                    path = (Marshal.PtrToStringUni(buffer) ?? "").ToUpperInvariant();
                }
                finally { Marshal.FreeHGlobal(buffer); }
                if (!path.Contains("VID_0B05")) continue;

                var info = new DeviceInfo { Size = 32 };
                uint infoSize = 32;
                var infoBuffer = Marshal.AllocHGlobal(32);
                try
                {
                    Marshal.StructureToPtr(info, infoBuffer, false);
                    if (GetRawInputDeviceInfo(d.Device, RidiDeviceInfo, infoBuffer, ref infoSize) == uint.MaxValue) continue;
                    info = Marshal.PtrToStructure<DeviceInfo>(infoBuffer);
                }
                finally { Marshal.FreeHGlobal(infoBuffer); }
                if (info.UsagePage < 0xFF00) continue; // vendor-defined pages only

                bool known = path.Contains("PID_1ABE") || path.Contains("PID_1B4C");
                bool preferred = known && path.Contains("MI_02") && path.Contains("COL01"); // where AnyFSE finds it
                found.Add(new Collection(d.Device, info.UsagePage, info.Usage, known, preferred));
            }
        }
        catch (Exception) { }
        // The known PIDs when they're there, else any ASUS vendor collection (a newer model).
        return found.Any(c => c.Known) ? found.Where(c => c.Known).ToList() : found;
    }

    /// <summary>The collection AnyFSE reads (MI_02, COL01 of a known PID), if it's there; reports from it are always read.</summary>
    public static IntPtr FindDevice()
    {
        var all = FindCollections();
        var preferred = all.FirstOrDefault(c => c.Preferred).Device;
        return preferred != IntPtr.Zero ? preferred : IntPtr.Zero;
    }

    public static bool IsSupported => FindCollections().Count > 0;

    /// <summary>The device paths of every collection of the Ally's ASUS HID device (for lights and controller settings).</summary>
    public static List<string> AsusHidPaths()
    {
        var paths = new List<string>();
        try
        {
            uint count = 0;
            uint size = (uint)Marshal.SizeOf<RawInputDeviceList>();
            GetRawInputDeviceList(null, ref count, size);
            if (count == 0) return paths;
            var list = new RawInputDeviceList[count];
            if (GetRawInputDeviceList(list, ref count, size) == uint.MaxValue) return paths;
            foreach (var d in list.Where(d => d.Type == RimTypeHid))
            {
                uint chars = 0;
                GetRawInputDeviceInfo(d.Device, RidiDeviceName, IntPtr.Zero, ref chars);
                if (chars == 0) continue;
                var buffer = Marshal.AllocHGlobal((int)chars * 2);
                try
                {
                    if (GetRawInputDeviceInfo(d.Device, RidiDeviceName, buffer, ref chars) == uint.MaxValue) continue;
                    var path = Marshal.PtrToStringUni(buffer) ?? "";
                    var upper = path.ToUpperInvariant();
                    if (upper.Contains("VID_0B05") && (upper.Contains("PID_1ABE") || upper.Contains("PID_1B4C"))) paths.Add(path);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        catch (Exception) { }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Starts (or stops) receiving the buttons in a window, even while another app is in front.</summary>
    public static bool Listen(IntPtr window, bool on)
    {
        try
        {
            if (!on)
            {
                if (_registered.Count == 0) return true;
                var remove = _registered.Select(r => r with { Flags = RidevRemove, Target = IntPtr.Zero }).ToArray();
                RegisterRawInputDevices(remove, (uint)remove.Length, (uint)Marshal.SizeOf<RawInputDevice>());
                _registered.Clear();
                _devices.Clear();
                _device = IntPtr.Zero;
                return true;
            }

            var collections = FindCollections();
            if (collections.Count == 0) return false;
            var wanted = collections.Select(c => (c.UsagePage, c.Usage)).Distinct()
                .Select(u => new RawInputDevice { UsagePage = u.UsagePage, Usage = u.Usage, Flags = RidevInputSink, Target = window })
                .ToArray();
            if (!RegisterRawInputDevices(wanted, (uint)wanted.Length, (uint)Marshal.SizeOf<RawInputDevice>())) return false;
            _registered.Clear();
            _registered.AddRange(wanted);
            _devices.Clear();
            foreach (var c in collections) _devices.Add(c.Device);
            _device = FindDevice();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Turns a WM_INPUT message into a button id ("ac", "mode-cc", ...) or an extra key ("mic", "screenshot",
    /// "keyboard", "record"). Null for anything else, including other devices.
    /// </summary>
    public static string? Read(IntPtr rawInput)
    {
        if (_devices.Count == 0) return null;
        try
        {
            int headerSize = 8 + 2 * IntPtr.Size;
            uint size = 0;
            GetRawInputData(rawInput, RidInput, IntPtr.Zero, ref size, (uint)headerSize);
            if (size == 0 || size > 4096) return null;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(rawInput, RidInput, buffer, ref size, (uint)headerSize) != size) return null;
                if ((uint)Marshal.ReadInt32(buffer, 0) != RimTypeHid) return null;
                var device = Marshal.ReadIntPtr(buffer, 8);
                if (!_devices.Contains(device)) return null;
                int reportSize = Marshal.ReadInt32(buffer, headerSize);
                if (reportSize < 2) return null;
                // Button reports start with report id 0x5A (HandheldCompanion), with the code in the next byte; reports
                // from the other ASUS collections (lights, settings) are ignored.
                // The collection AnyFSE uses (MI_02, COL01) is still read whatever its report id, as before.
                if (Marshal.ReadByte(buffer, headerSize + 8) != InputReportId && device != _device) return null;
                byte code = Marshal.ReadByte(buffer, headerSize + 8 + 1);
                LastCode = code;
                LastCodeAt = DateTime.Now;
                try { Heard?.Invoke(code); } catch (Exception) { }
                return Interpret(code);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The meaning of one report code, keeping track of whether Mode is held (AnyFSE: Ally::OnInput).</summary>
    public static string? Interpret(byte code)
    {
        if (code == ModePress) { _modeHeld = true; return null; }
        if (code == Release) { _modeHeld = false; return null; }
        if (code == ACHoldRelease) return null;

        bool xbox = SwapsCodes;
        string? button = code switch
        {
            // On the first two Xbox Allys the left button sends the AC code and is the Library button; CC is the AC
            // button (AnyFSE). The X20 sends Library as 147 and AC as 56 (HandheldCompanion).
            ACPress => xbox ? "library" : "ac",
            CCPress => xbox ? "ac" : "cc",
            ACHold => "achold",
            LibraryPress => "library",
            _ => null
        };
        if (button != null) return _modeHeld ? "mode-" + button : button;

        return code switch
        {
            ToggleMicrophone => "mic",
            ScreenShot => "screenshot",
            ShowKeyboard => "keyboard",
            ToggleRecord => "record",
            _ => null
        };
    }

    // ---------------------------------------------------------------- actions that send keys

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public IntPtr Extra; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X; public int Y; public uint Data; public uint Flags; public uint Time; public IntPtr Extra; }

    // The union is as big as its largest member (the mouse one), which is what Windows expects in cbSize.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyInput Key;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion U; }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    /// <summary>Presses keys together and lets go of them, like a keyboard shortcut.</summary>
    public static void SendKeys(params ushort[] keys)
    {
        const uint KeyUp = 0x0002;
        var inputs = new List<Input>();
        foreach (var k in keys) inputs.Add(new Input { Type = 1, U = new InputUnion { Key = new KeyInput { Vk = k } } });
        foreach (var k in keys.Reverse()) inputs.Add(new Input { Type = 1, U = new InputUnion { Key = new KeyInput { Vk = k, Flags = KeyUp } } });
        try { SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>()); } catch (Exception) { }
    }
}
