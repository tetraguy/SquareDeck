using System.Runtime.InteropServices;

namespace SquareDeck.Services;

/// <summary>
/// The processor's own power use right now (the whole APU: processor and graphics), from Windows' "Energy Meter"
/// performance counters, which read the chip's energy counters (RAPL) on processors that report them. Null where
/// Windows doesn't offer them. Read about once a second; the first read only primes the counter.
/// </summary>
public sealed class PowerMeter : IDisposable
{
    private IntPtr _query, _counter;
    private readonly object _gate = new();

    public PowerMeter()
    {
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; return; }
            if (PdhAddEnglishCounter(_query, @"\Energy Meter(*)\Power", IntPtr.Zero, out _counter) != 0) _counter = IntPtr.Zero;
            PdhCollectQueryData(_query);
        }
        catch (Exception) { _query = IntPtr.Zero; _counter = IntPtr.Zero; }
    }

    /// <summary>Watts used by the processor package now, or null.</summary>
    public double? Watts()
    {
        lock (_gate)
        {
            if (_query == IntPtr.Zero || _counter == IntPtr.Zero) return null;
            try
            {
                if (PdhCollectQueryData(_query) != 0) return null;
                uint size = 0;
                int status = PdhGetFormattedCounterArray(_counter, FmtDouble | FmtNoCap100, ref size, out uint count, IntPtr.Zero);
                if (status != MoreData || size == 0) return null;
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (PdhGetFormattedCounterArray(_counter, FmtDouble | FmtNoCap100, ref size, out count, buffer) != 0) return null;
                    int itemSize = IntPtr.Size + Marshal.SizeOf<CounterValue>();
                    double? best = null;
                    int rank = -1;
                    for (int k = 0; k < count; k++)
                    {
                        string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer + k * itemSize)) ?? "";
                        var item = Marshal.PtrToStructure<CounterValue>(buffer + k * itemSize + IntPtr.Size);
                        if (item.Status != 0) continue;
                        // The package (processor and graphics together) first, then the processor, then the SoC.
                        int r = name.Contains("PKG", StringComparison.OrdinalIgnoreCase) || name.Contains("Package", StringComparison.OrdinalIgnoreCase) ? 3
                              : name.Contains("SOC", StringComparison.OrdinalIgnoreCase) || name.Contains("APU", StringComparison.OrdinalIgnoreCase) ? 2
                              : name.Contains("CPU", StringComparison.OrdinalIgnoreCase) || name.Contains("CORE", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                        if (r > rank) { rank = r; best = item.Value; }
                    }
                    if (best is not double v || rank < 0) return null;
                    // The counter is in milliwatts.
                    double watts = v / 1000.0;
                    return watts is > 0 and < 300 ? watts : null;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            catch (Exception) { return null; }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    private const uint FmtDouble = 0x00000200, FmtNoCap100 = 0x00008000;
    private const int MoreData = unchecked((int)0x800007D2);

    [StructLayout(LayoutKind.Explicit)]
    private struct CounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);
}
