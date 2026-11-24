using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Networking.Connectivity;

namespace SquareDeck.Services;

/// <summary>
/// What the Wi-Fi and Bluetooth pickers need beyond NetworkService: airplane mode, which Wi-Fi networks Windows has
/// saved, and connecting a paired Bluetooth device again.
/// </summary>
public static class Wireless
{
    // ------------------------------------------------------------ airplane mode

    // Windows' own radio manager, the one the Action Center's Airplane mode button uses (RMapi.dll, "RadioManagementAPI").
    // It has no published header; the interface is as tools such as AutoHotkey and Rainmeter plugins call it. "System
    // radio state" on means the radios may be on, so airplane mode is its opposite.
    [ComImport, Guid("DB3AFBFB-08E6-46C6-AA70-BF9A34C30AB7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IRadioManager
    {
        [PreserveSig] int IsRMSupported(out uint state);
        [PreserveSig] int GetUIRadioInstances([MarshalAs(UnmanagedType.IUnknown)] out object instances);
        [PreserveSig] int GetSystemRadioState(out int enabled, out int policyEnabled, out int changeReason);
        [PreserveSig] int SetSystemRadioState(int enabled);
        [PreserveSig] int Refresh();
        [PreserveSig] int OnHardwareSliderChange(int a, int b);
    }

    private static readonly Guid RadioManagerClass = new("581333F6-28DB-41BE-BC7A-FF201F12F3F6");

    private static T? OnMta<T>(Func<T?> work)
    {
        // The radio manager is happiest called from a thread of its own (not the window's).
        T? result = default;
        var thread = new Thread(() => { try { result = work(); } catch (Exception) { } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(5));
        return result;
    }

    private static IRadioManager? Manager()
    {
        var type = Type.GetTypeFromCLSID(RadioManagerClass, throwOnError: false);
        return type == null ? null : Activator.CreateInstance(type) as IRadioManager;
    }

    /// <summary>True when airplane mode is on, null when Windows' radio manager couldn't be asked.</summary>
    public static bool? AirplaneMode => OnMta<bool?>(() =>
    {
        var manager = Manager();
        if (manager == null) return null;
        try { return manager.GetSystemRadioState(out int enabled, out _, out _) == 0 ? enabled == 0 : null; }
        finally { Marshal.ReleaseComObject(manager); }
    });

    /// <summary>Turns airplane mode on or off, as the Action Center's button does. False when it couldn't.</summary>
    public static bool SetAirplaneMode(bool on) => OnMta(() =>
    {
        var manager = Manager();
        if (manager == null) return false;
        try { return manager.SetSystemRadioState(on ? 0 : 1) == 0; }
        finally { Marshal.ReleaseComObject(manager); }
    });

    // ------------------------------------------------------------ Wi-Fi

    /// <summary>The Wi-Fi networks Windows has a profile for (it knows their password).</summary>
    public static HashSet<string> SavedNetworks()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var profile in NetworkInformation.GetConnectionProfiles())
                if (profile.IsWlanConnectionProfile && !string.IsNullOrEmpty(profile.ProfileName)) names.Add(profile.ProfileName);
        }
        catch (Exception) { }
        return names;
    }

    // ------------------------------------------------------------ Bluetooth

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SystemTime { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothDeviceInfo
    {
        public uint Size;
        public ulong Address;
        public uint ClassOfDevice;
        public int Connected, Remembered, Authenticated;
        public SystemTime LastSeen, LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothGetDeviceInfo(IntPtr radio, ref BluetoothDeviceInfo info);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothSetServiceState(IntPtr radio, ref BluetoothDeviceInfo info, ref Guid service, uint flags);

    // The audio services: headphones and speakers (A2DP), and headsets for calls (hands-free, headset).
    private static readonly Guid[] AudioServices =
    {
        new("0000110B-0000-1000-8000-00805F9B34FB"), // audio sink
        new("0000111E-0000-1000-8000-00805F9B34FB"), // hands-free
        new("00001108-0000-1000-8000-00805F9B34FB"), // headset
    };

    /// <summary>
    /// Connects a paired device again. Windows has no "connect" call for apps; for headphones, speakers and headsets
    /// switching their audio services off and on makes Windows reconnect them (what several Bluetooth tray tools do).
    /// Controllers, keyboards and mice connect by themselves once they're on. Null on success, else what to say.
    /// </summary>
    public static async Task<string?> ConnectAsync(string deviceId)
    {
        ulong address;
        try
        {
            using var device = await BluetoothDevice.FromIdAsync(deviceId);
            if (device == null) return "Turn the device on and bring it close: it connects by itself.";
            address = device.BluetoothAddress;
        }
        catch (Exception) { return "Turn the device on and bring it close: it connects by itself."; }

        return await Task.Run(() =>
        {
            var info = new BluetoothDeviceInfo { Size = (uint)Marshal.SizeOf<BluetoothDeviceInfo>(), Address = address, Name = "" };
            if (BluetoothGetDeviceInfo(IntPtr.Zero, ref info) != 0) return "Windows couldn't find that device. Is Bluetooth on?";
            bool audio = false;
            foreach (var service in AudioServices)
            {
                var id = service;
                if (BluetoothSetServiceState(IntPtr.Zero, ref info, ref id, 0) != 0) continue; // not a service it has
                audio = true;
                Thread.Sleep(400);
                id = service;
                BluetoothSetServiceState(IntPtr.Zero, ref info, ref id, 1);
            }
            return audio ? null : "It connects by itself when it's on and near. Controllers: hold the pairing or Xbox button.";
        });
    }
}
