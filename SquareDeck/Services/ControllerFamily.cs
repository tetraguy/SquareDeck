using System.Runtime.InteropServices;

namespace SquareDeck.Services;

/// <summary>
/// Which family of controller is connected, from the USB and Bluetooth game controllers Windows lists: Sony's
/// (PlayStation) or Nintendo's (Switch) when one is there, otherwise Xbox (the built-in controls of most handhelds,
/// and every XInput pad). A second controller plugged into a handheld is the one in use, so it wins.
/// </summary>
public static class ControllerFamily
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceList { public IntPtr Device; public uint Type; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([Out] RawInputDeviceList[]? list, ref uint count, uint size);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

    private const uint RimTypeHid = 2, RidiDeviceName = 0x20000007;

    private static string _last = "xbox";
    private static DateTime _at = DateTime.MinValue;

    /// <summary>"playstation", "switch" or "xbox". Looked up at most every three seconds.</summary>
    public static string Detect()
    {
        if (DateTime.UtcNow - _at < TimeSpan.FromSeconds(3)) return _last;
        _at = DateTime.UtcNow;
        bool sony = false, nintendo = false;
        try
        {
            uint count = 0, size = (uint)Marshal.SizeOf<RawInputDeviceList>();
            GetRawInputDeviceList(null, ref count, size);
            if (count > 0)
            {
                var list = new RawInputDeviceList[count];
                if (GetRawInputDeviceList(list, ref count, size) != uint.MaxValue)
                    foreach (var d in list.Where(d => d.Type == RimTypeHid))
                    {
                        uint chars = 0;
                        GetRawInputDeviceInfo(d.Device, RidiDeviceName, IntPtr.Zero, ref chars);
                        if (chars == 0) continue;
                        var buffer = Marshal.AllocHGlobal((int)chars * 2);
                        try
                        {
                            if (GetRawInputDeviceInfo(d.Device, RidiDeviceName, buffer, ref chars) == uint.MaxValue) continue;
                            string name = (Marshal.PtrToStringUni(buffer) ?? "").ToUpperInvariant();
                            // Sony and Nintendo vendor ids, over USB (VID_) or Bluetooth (_VID&0002…).
                            if (name.Contains("VID_054C") || name.Contains("VID&0002054C")) sony = true;
                            if (name.Contains("VID_057E") || name.Contains("VID&0002057E")) nintendo = true;
                        }
                        finally { Marshal.FreeHGlobal(buffer); }
                    }
            }
        }
        catch (Exception) { }
        _last = sony ? "playstation" : nintendo ? "switch" : "xbox";
        return _last;
    }
}
