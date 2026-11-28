using System.Runtime.InteropServices;

namespace SquareDeck.Services;

/// <summary>
/// Live readings for My Device: temperatures and fans from the handheld's firmware where it has them (ROG Ally:
/// ASUS's driver), else Windows' thermal zone; battery charge,
/// power draw and health from Windows; and how fast the battery has been going down.
/// </summary>
public static class DeviceSensors
{
    public sealed record Reading(int? CpuTemp, int? GpuTemp, string TempSource, int? CpuFanRpm, int? GpuFanRpm,
        int? Percent, bool Plugged, double? DrawWatts, double? HealthPercent, double? DesignWh, double? FullWh, double? RemainingWh);

    // ---- Windows' thermal zone (ACPI), through PDH like the other counters SquareDeck reads.
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr items);

    private static IntPtr _query, _zone;
    private static bool _pdhTried;

    private static int? ThermalZone()
    {
        try
        {
            if (!_pdhTried)
            {
                _pdhTried = true;
                if (PdhOpenQuery(null, IntPtr.Zero, out _query) == 0 &&
                    PdhAddEnglishCounter(_query, @"\Thermal Zone Information(*)\Temperature", IntPtr.Zero, out _zone) != 0) _zone = IntPtr.Zero;
                PdhCollectQueryData(_query);
            }
            if (_zone == IntPtr.Zero) return null;
            PdhCollectQueryData(_query);
            // One value per thermal zone: the hottest is shown. Each item is a name pointer then the value
            // (status, padding, double), so 24 bytes on 64-bit Windows.
            const uint Double = 0x200;
            uint size = 0, count = 0;
            PdhGetFormattedCounterArray(_zone, Double, ref size, ref count, IntPtr.Zero);
            if (size == 0) return null;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(_zone, Double, ref size, ref count, buffer) != 0) return null;
                int stride = IntPtr.Size + 16;
                double best = double.MinValue;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buffer + i * stride;
                    uint status = (uint)Marshal.ReadInt32(item, IntPtr.Size);
                    if (status != 0) continue;
                    double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, IntPtr.Size + 8));
                    double c = value > 200 ? value - 273.15 : value; // Kelvin on most PCs
                    if (c is > 5 and < 120 && c > best) best = c;
                }
                return best > double.MinValue ? (int)Math.Round(best) : null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception) { return null; }
    }

    public static Reading Read()
    {
        int? cpu = null, gpu = null, cpuFan = null, gpuFan = null;
        string source = "";
        if (Handheld.Vendor == HandheldVendor.Asus && AsusAcpi.Available)
        {
            cpu = AsusAcpi.Temperature(AsusAcpi.CpuTemp);
            gpu = AsusAcpi.Temperature(AsusAcpi.GpuTemp);
            cpuFan = AsusAcpi.FanRpm(AsusAcpi.CpuFan);
            gpuFan = AsusAcpi.FanRpm(AsusAcpi.GpuFan);
            if (cpu != null) source = "ASUS firmware";
        }
        if (cpu == null) { cpu = ThermalZone(); if (cpu != null) source = "Windows thermal zone"; }

        // Battery: Windows' aggregate battery report (charge rate is negative while discharging).
        double? draw = null, health = null, design = null, full = null, remaining = null;
        try
        {
            var report = Windows.Devices.Power.Battery.AggregateBattery.GetReport();
            if (report.ChargeRateInMilliwatts is int rate) draw = rate / 1000.0;
            if (report.DesignCapacityInMilliwattHours is int d && d > 0) design = d / 1000.0;
            if (report.FullChargeCapacityInMilliwattHours is int f && f > 0) full = f / 1000.0;
            if (report.RemainingCapacityInMilliwattHours is int r && r >= 0) remaining = r / 1000.0;
            if (design > 0 && full > 0) health = Math.Min(100, full.Value / design.Value * 100);
        }
        catch (Exception) { }

        int? percent = Battery.GetPercent();
        bool plugged = Battery.IsPluggedIn();
        Note(percent, plugged);
        return new Reading(cpu, gpu, source, cpuFan, gpuFan, percent, plugged, draw, health, design, full, remaining);
    }

    // ---- how fast the battery goes down (since SquareDeck started, while unplugged)
    private static readonly List<(DateTime At, int Percent)> History = new();

    private static void Note(int? percent, bool plugged)
    {
        if (percent == null) return;
        if (plugged) { History.Clear(); return; }
        if (History.Count == 0 || History[^1].Percent != percent || (DateTime.UtcNow - History[^1].At).TotalMinutes > 2)
            History.Add((DateTime.UtcNow, percent.Value));
        if (History.Count > 400) History.RemoveAt(0);
    }

    /// <summary>Percent an hour over the last half hour on battery, once there's enough to tell (null before).</summary>
    public static double? DrainPerHour()
    {
        var since = DateTime.UtcNow.AddMinutes(-30);
        var recent = History.Where(h => h.At >= since).ToList();
        if (recent.Count < 2) return null;
        double hours = (recent[^1].At - recent[0].At).TotalHours;
        int dropped = recent[0].Percent - recent[^1].Percent;
        if (hours < 0.08 || dropped <= 0) return null;
        return dropped / hours;
    }
}
