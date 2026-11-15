using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// The ASUS ROG Ally's extra buttons (Armoury Crate, Command Center / Library, and Mode+ combinations),
/// read from the device's own HID collection with Windows raw input.
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
        ShowKeyboard = 162, ToggleRecord = 164, ModePress = 165, CCPress = 166, ACHold = 167;

    /// <summary>The buttons that can be given an action, with the names shown in Settings.</summary>
    public static readonly (string Id, string Name)[] Buttons =
    {
        ("ac", "Armoury Crate button"), ("achold", "Armoury Crate button (hold)"), ("cc", "Command Center button"),
        ("library", "Library button (Xbox Ally)"), ("mode-ac", "Mode + Armoury Crate"), ("mode-achold", "Mode + Armoury Crate (hold)"),
        ("mode-cc", "Mode + Command Center"), ("mode-library", "Mode + Library")
    };

    /// <summary>What a button can do.</summary>
    public static readonly (string Id, string Name)[] Actions =
    {
        ("", "Nothing"), ("home", "Open SquareDeck"), ("quickmenu", "SquareDeck quick menu"), ("taskswitcher", "Task switcher"),
        ("taskview", "Task view (Win+Tab)"), ("gamebar", "Game Bar"), ("commandcenter", "Command Center (Game Bar widget)"),
        ("armourycrate", "Armoury Crate SE"), ("keyboard", "On-screen keyboard"), ("screenshot", "Take a screenshot"),
        ("record", "Start or stop recording"), ("mic", "Mute or unmute the microphone"), ("taskmanager", "SquareDeck Task Manager"),
        ("nightlight", "Night light on or off"), ("desktop", "Show the desktop")
    };

    /// <summary>Defaults like AnyFSE's: Command Center on AC, task switcher on hold, the launcher on CC / Library.</summary>
    public static Dictionary<string, string> DefaultBindings() => new()
    {
        ["ac"] = "commandcenter", ["achold"] = "taskswitcher", ["cc"] = "home", ["library"] = "home",
        ["mode-ac"] = "armourycrate", ["mode-achold"] = "", ["mode-cc"] = "", ["mode-library"] = ""
    };

    /// <summary>Which Ally this is, from the board name Windows records (AnyFSE: GetRogAllyVersion).</summary>
    public static string Model
    {
        get
        {
            try
            {
                var board = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\HardwareConfig\Current", "BaseBoardProduct", "") as string ?? "";
                return board switch
                {
                    "RC73XA" => "ROG Xbox Ally X",
                    "RC73YA" => "ROG Xbox Ally",
                    "RC72LA" => "ROG Ally X",
                    "RC71L" => "ROG Ally",
                    _ => ""
                };
            }
            catch (Exception) { return ""; }
        }
    }

    /// <summary>The Xbox versions swap the two buttons' codes (AnyFSE: IsXBoxRogAlly).</summary>
    public static bool IsXboxAlly => Model.StartsWith("ROG Xbox");

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
    private static RawInputDevice _registered;
    private static bool _modeHeld;

    /// <summary>The Ally's button collection, if this PC is an Ally (VID 0B05, PID 1ABE or 1B4C, interface 2, collection 1).</summary>
    public static IntPtr FindDevice()
    {
        try
        {
            uint count = 0;
            uint size = (uint)Marshal.SizeOf<RawInputDeviceList>();
            GetRawInputDeviceList(null, ref count, size);
            if (count == 0) return IntPtr.Zero;
            var list = new RawInputDeviceList[count];
            if (GetRawInputDeviceList(list, ref count, size) == uint.MaxValue) return IntPtr.Zero;

            foreach (var d in list.Where(d => d.Type == RimTypeHid))
            {
                uint chars = 0;
                GetRawInputDeviceInfo(d.Device, RidiDeviceName, IntPtr.Zero, ref chars);
                if (chars == 0) continue;
                var buffer = Marshal.AllocHGlobal((int)chars * 2);
                try
                {
                    if (GetRawInputDeviceInfo(d.Device, RidiDeviceName, buffer, ref chars) == uint.MaxValue) continue;
                    var path = (Marshal.PtrToStringUni(buffer) ?? "").ToUpperInvariant();
                    if (path.Contains("VID_0B05") && (path.Contains("PID_1ABE") || path.Contains("PID_1B4C")) &&
                        path.Contains("MI_02") && path.Contains("COL01"))
                        return d.Device;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        catch (Exception) { }
        return IntPtr.Zero;
    }

    public static bool IsSupported => FindDevice() != IntPtr.Zero;

    /// <summary>Starts (or stops) receiving the buttons in a window, even while another app is in front.</summary>
    public static bool Listen(IntPtr window, bool on)
    {
        try
        {
            if (!on)
            {
                if (_device == IntPtr.Zero) return true;
                var remove = _registered;
                remove.Flags = RidevRemove;
                remove.Target = IntPtr.Zero;
                RegisterRawInputDevices(new[] { remove }, 1, (uint)Marshal.SizeOf<RawInputDevice>());
                _device = IntPtr.Zero;
                return true;
            }

            var device = FindDevice();
            if (device == IntPtr.Zero) return false;
            var info = new DeviceInfo { Size = 32 };
            uint size = 32;
            var buffer = Marshal.AllocHGlobal(32);
            try
            {
                Marshal.StructureToPtr(info, buffer, false);
                if (GetRawInputDeviceInfo(device, RidiDeviceInfo, buffer, ref size) == uint.MaxValue) return false;
                info = Marshal.PtrToStructure<DeviceInfo>(buffer);
            }
            finally { Marshal.FreeHGlobal(buffer); }

            _registered = new RawInputDevice { UsagePage = info.UsagePage, Usage = info.Usage, Flags = RidevInputSink, Target = window };
            if (!RegisterRawInputDevices(new[] { _registered }, 1, (uint)Marshal.SizeOf<RawInputDevice>())) return false;
            _device = device;
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
        if (_device == IntPtr.Zero) return null;
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
                if (device != _device) return null;
                int reportSize = Marshal.ReadInt32(buffer, headerSize);
                if (reportSize < 2) return null;
                byte code = Marshal.ReadByte(buffer, headerSize + 8 + 1);
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

        bool xbox = IsXboxAlly;
        string? button = code switch
        {
            // On the Xbox Allys the left button sends the AC code and is the Library button; CC is the AC button.
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
