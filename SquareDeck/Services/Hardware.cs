using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>What the processor, memory and graphics are (read once): names, cores, capacity, memory speed and type.</summary>
public sealed class HardwareInfo
{
    public string CpuName = "";
    public int Cores, Threads;
    public int CpuMaxMhz;
    public ulong MemoryBytes;          // installed (from the memory modules), else what Windows can use
    public int MemorySpeed;            // MT/s the memory runs at (configured), else its rated speed
    public string MemoryType = "";     // DDR4, LPDDR5, …
    public int MemoryModules;
    public List<(string Name, ulong Memory)> Gpus = new();
    public List<(string Name, string Driver, string Maker)> Npus = new(); // neural processors (AI accelerators), if any
}

/// <summary>
/// Reads the hardware facts without WMI: the processor from the registry and Windows' processor topology, the memory
/// modules from the firmware's SMBIOS tables, and the graphics adapters from their driver entries in the registry.
/// </summary>
public static class Hardware
{
    private static HardwareInfo? _info;

    public static HardwareInfo Info => _info ??= Read();

    private static HardwareInfo Read()
    {
        var info = new HardwareInfo { Threads = Environment.ProcessorCount };
        try
        {
            using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            info.CpuName = (cpu?.GetValue("ProcessorNameString") as string ?? "").Trim();
            if (cpu?.GetValue("~MHz") is int mhz) info.CpuMaxMhz = mhz;
        }
        catch (Exception) { }
        try { info.Cores = CountCores(); } catch (Exception) { }
        if (info.Cores <= 0) info.Cores = info.Threads;
        try { ReadMemoryModules(info); } catch (Exception) { }
        if (info.MemoryBytes == 0 && MemoryStatus() is { } status) info.MemoryBytes = status.Total;
        try { ReadGpus(info); } catch (Exception) { }
        try { ReadNpus(info); } catch (Exception) { }
        return info;
    }

    // ---- processor cores

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

    /// <summary>Physical cores (RelationProcessorCore entries).</summary>
    private static int CountCores()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length);
        if (length == 0) return 0;
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(0, buffer, ref length)) return 0;
            int count = 0;
            for (uint offset = 0; offset < length;)
            {
                // Each entry: Relationship (4), Size (4), …
                int size = Marshal.ReadInt32(buffer, (int)offset + 4);
                if (size <= 0) break;
                count++;
                offset += (uint)size;
            }
            return count;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // ---- memory

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    /// <summary>Memory Windows can use, and how much of it is free right now.</summary>
    public static (ulong Total, ulong Available)? MemoryStatus()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (status.TotalPhys, status.AvailPhys) : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[]? buffer, uint size);

    /// <summary>The memory modules (SMBIOS type 17): size, type and speed.</summary>
    private static void ReadMemoryModules(HardwareInfo info)
    {
        const uint Rsmb = 0x52534D42; // 'RSMB'
        uint size = GetSystemFirmwareTable(Rsmb, 0, null, 0);
        if (size == 0) return;
        var data = new byte[size];
        if (GetSystemFirmwareTable(Rsmb, 0, data, size) == 0) return;
        int length = BitConverter.ToInt32(data, 4);
        int i = 8, end = Math.Min(data.Length, 8 + length);
        ulong total = 0;
        int speed = 0;
        string type = "";
        while (i + 4 <= end)
        {
            byte kind = data[i], formatted = data[i + 1];
            if (formatted < 4 || kind == 127) break; // end of table
            if (kind == 17 && formatted >= 0x17 && i + formatted <= end)
            {
                ushort raw = BitConverter.ToUInt16(data, i + 0x0C);
                ulong bytes = 0;
                if (raw == 0x7FFF && formatted >= 0x20) bytes = (ulong)BitConverter.ToUInt32(data, i + 0x1C) * 1024 * 1024;
                else if (raw is not (0 or 0xFFFF)) bytes = (raw & 0x8000) != 0 ? (ulong)(raw & 0x7FFF) * 1024 : (ulong)raw * 1024 * 1024;
                if (bytes > 0)
                {
                    total += bytes;
                    info.MemoryModules++;
                    int rated = BitConverter.ToUInt16(data, i + 0x15);
                    int configured = formatted >= 0x22 ? BitConverter.ToUInt16(data, i + 0x20) : 0;
                    int now = configured is > 0 and < 0xFFFF ? configured : rated is > 0 and < 0xFFFF ? rated : 0;
                    speed = Math.Max(speed, now);
                    if (type.Length == 0) type = MemoryTypeName(data[i + 0x12]);
                }
            }
            // Skip the formatted part, then the strings (each ends in 0; the set ends in a second 0).
            int j = i + formatted;
            while (j + 1 < end && !(data[j] == 0 && data[j + 1] == 0)) j++;
            i = j + 2;
        }
        info.MemoryBytes = total;
        info.MemorySpeed = speed;
        info.MemoryType = type;
    }

    private static string MemoryTypeName(byte code) => code switch
    {
        0x12 => "DDR", 0x13 => "DDR2", 0x18 => "DDR3", 0x1A => "DDR4", 0x1B => "LPDDR", 0x1C => "LPDDR2",
        0x1D => "LPDDR3", 0x1E => "LPDDR4", 0x22 => "DDR5", 0x23 => "LPDDR5", 0x24 => "HBM3", _ => ""
    };

    // ---- neural processor

    /// <summary>
    /// NPUs (Intel AI Boost, AMD Ryzen AI, Qualcomm Hexagon): their drivers register under Windows' "Neural processors"
    /// device class (and older ones under "Compute accelerators"). None found: the list stays empty.
    /// </summary>
    private static void ReadNpus(HardwareInfo info)
    {
        // Every device class whose name says it holds neural processors or compute accelerators.
        using var classes = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class");
        if (classes == null) return;
        foreach (var classId in classes.GetSubKeyNames())
        {
            try { ReadNpuClass(classes, classId, info); } catch (Exception) { } // some classes can't be read without administrator rights
        }
    }

    private static void ReadNpuClass(RegistryKey classes, string classId, HardwareInfo info)
    {
        {
            using var root = classes.OpenSubKey(classId);
            string kind = root?.GetValue("Class") as string ?? "";
            if (root == null || !(kind.Contains("Neural", StringComparison.OrdinalIgnoreCase) || kind.Equals("ComputeAccelerator", StringComparison.OrdinalIgnoreCase))) return;
            foreach (var name in root.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                using var key = root.OpenSubKey(name);
                if (key?.GetValue("DriverDesc") is not string desc || desc.Length == 0) continue;
                if (info.Npus.Any(n => n.Name == desc)) continue;
                info.Npus.Add((desc, key.GetValue("DriverVersion") as string ?? "", key.GetValue("ProviderName") as string ?? ""));
            }
        }
    }

    // ---- graphics

    /// <summary>Display adapters from their driver entries, with dedicated video memory.</summary>
    private static void ReadGpus(HardwareInfo info)
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (root == null) return;
        foreach (var name in root.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            using var key = root.OpenSubKey(name);
            if (key?.GetValue("DriverDesc") is not string desc || desc.Length == 0) continue;
            if (desc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || desc.Contains("Remote", StringComparison.OrdinalIgnoreCase)
                || desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || desc.Contains("Parsec", StringComparison.OrdinalIgnoreCase)) continue;
            ulong memory = key.GetValue("HardwareInformation.qwMemorySize") switch
            {
                long l => (ulong)l,
                byte[] b when b.Length >= 8 => BitConverter.ToUInt64(b, 0),
                _ => key.GetValue("HardwareInformation.MemorySize") switch
                {
                    int n => (uint)n,
                    long l => (ulong)l,
                    byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                    _ => 0UL
                }
            };
            if (!info.Gpus.Any(g => g.Name == desc)) info.Gpus.Add((desc, memory));
        }
    }
}

/// <summary>
/// Live processor speed and use, and graphics use, from Windows' performance counters (the same ones Task Manager
/// reads). Call <see cref="Sample"/> about once a second; the first call only primes the counters.
/// </summary>
public sealed class HardwareMonitor : IDisposable
{
    private IntPtr _query, _cpuUse, _cpuPerf, _gpu, _diskBusy, _diskRead, _diskWrite, _npu;
    private bool _primed;

    public HardwareMonitor()
    {
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; return; }
            if (PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out _cpuUse) != 0)
                PdhAddEnglishCounter(_query, @"\Processor(_Total)\% Processor Time", IntPtr.Zero, out _cpuUse);
            PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out _cpuPerf);
            PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpu);
            // The system drive: how busy it is, and how fast it's reading and writing.
            string drive = (System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").TrimEnd('\\');
            PdhAddEnglishCounter(_query, $@"\LogicalDisk({drive})\% Idle Time", IntPtr.Zero, out _diskBusy);
            PdhAddEnglishCounter(_query, $@"\LogicalDisk({drive})\Disk Read Bytes/sec", IntPtr.Zero, out _diskRead);
            PdhAddEnglishCounter(_query, $@"\LogicalDisk({drive})\Disk Write Bytes/sec", IntPtr.Zero, out _diskWrite);
            // The NPU's load, where Windows has a counter for it (newer versions of Windows 11).
            if (Hardware.Info.Npus.Count > 0 && PdhAddEnglishCounter(_query, @"\NPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _npu) != 0) _npu = IntPtr.Zero;
            PdhCollectQueryData(_query);
        }
        catch (Exception) { _query = IntPtr.Zero; }
    }

    /// <summary>Processor use (%), its speed now (MHz), and graphics use (%); null where Windows can't say.</summary>
    // Windows' counters must not be read from two threads at once, or closed while being read: doing either can
    // crash the whole app (it happened when the quick menu closed mid-reading). Everything goes through this lock.
    private readonly object _gate = new();

    public (double? Cpu, int? CpuMhz, double? Gpu) Sample()
    {
        if (!Monitor.TryEnter(_gate)) return (null, null, null); // a reading is already under way: skip this one
        try { return SampleLocked(); }
        finally { Monitor.Exit(_gate); }
    }

    private (double? Cpu, int? CpuMhz, double? Gpu) SampleLocked()
    {
        if (_query == IntPtr.Zero) return (null, null, null);
        try
        {
            if (PdhCollectQueryData(_query) != 0) return (null, null, null);
            if (!_primed) { _primed = true; return (null, null, null); }
            double? cpu = Value(_cpuUse) is double u ? Math.Clamp(u, 0, 100) : null;
            int? mhz = Value(_cpuPerf) is double p && Hardware.Info.CpuMaxMhz > 0 ? (int)Math.Round(Hardware.Info.CpuMaxMhz * p / 100) : null;
            double? gpu = Total(_gpu) is double g ? Math.Clamp(g, 0, 100) : null;
            DiskBusy = Value(_diskBusy) is double idle ? Math.Clamp(100 - idle, 0, 100) : null;
            DiskRead = Value(_diskRead);
            DiskWrite = Value(_diskWrite);
            NpuBusy = Total(_npu) is double n ? Math.Clamp(n, 0, 100) : null;
            return (cpu, mhz, gpu);
        }
        catch (Exception) { return (null, null, null); }
    }

    /// <summary>From the last <see cref="Sample"/>: how busy the system drive is (%), and its reads / writes in bytes a second.</summary>
    public double? DiskBusy { get; private set; }
    public double? DiskRead { get; private set; }
    public double? DiskWrite { get; private set; }
    /// <summary>How busy the NPU is (%), when Windows can say.</summary>
    public double? NpuBusy { get; private set; }

    private static double? Value(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        return PdhGetFormattedCounterValue(counter, FmtDouble | FmtNoCap100, out _, out var value) == 0 && value.Status == 0 ? value.Value : null;
    }

    /// <summary>The sum over every instance of a wildcard counter (all apps' 3D work on the graphics chip).</summary>
    private static double? Total(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        uint size = 0;
        int status = PdhGetFormattedCounterArray(counter, FmtDouble | FmtNoCap100, ref size, out uint count, IntPtr.Zero);
        if (status != MoreData || size == 0) return count == 0 && status == 0 ? 0 : null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, FmtDouble | FmtNoCap100, ref size, out count, buffer) != 0) return null;
            int itemSize = IntPtr.Size + Marshal.SizeOf<CounterValue>();
            double sum = 0;
            for (int k = 0; k < count; k++)
            {
                var item = Marshal.PtrToStructure<CounterValue>(buffer + k * itemSize + IntPtr.Size);
                if (item.Status == 0) sum += item.Value;
            }
            return sum;
        }
        finally { Marshal.FreeHGlobal(buffer); }
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

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr query);
}

/// <summary>The network SquareDeck is using to reach the internet: the adapter, its kind and speed, and live traffic.</summary>
public sealed class NetworkMonitor
{
    private System.Net.NetworkInformation.NetworkInterface? _adapter;
    private long _received = -1, _sent = -1;
    private DateTime _at;

    public string Name { get; private set; } = "";
    public string Kind { get; private set; } = "";       // Wi-Fi, Ethernet, …
    public string Network { get; private set; } = "";    // the Wi-Fi network's name
    public int? Bars { get; private set; }               // Wi-Fi signal, 0–5
    public long LinkBitsPerSecond { get; private set; }
    public string Address { get; private set; } = "";

    /// <summary>Download and upload in bits a second since the last call (null on the first call or with no network).</summary>
    public (double? Down, double? Up) Sample()
    {
        try
        {
            if (_adapter == null || _adapter.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) Find();
            if (_adapter == null) return (null, null);
            var stats = _adapter.GetIPStatistics();
            var now = DateTime.UtcNow;
            (double?, double?) result = (null, null);
            if (_received >= 0)
            {
                double seconds = Math.Max(0.2, (now - _at).TotalSeconds);
                result = (Math.Max(0, (stats.BytesReceived - _received) * 8 / seconds), Math.Max(0, (stats.BytesSent - _sent) * 8 / seconds));
            }
            _received = stats.BytesReceived;
            _sent = stats.BytesSent;
            _at = now;
            return result;
        }
        catch (Exception) { return (null, null); }
    }

    /// <summary>The adapter that's up and has a default gateway (Wi-Fi before others when there are two), and what it's connected to.</summary>
    private void Find()
    {
        _received = _sent = -1;
        _adapter = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                        && n.NetworkInterfaceType is not (System.Net.NetworkInformation.NetworkInterfaceType.Loopback or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                        && n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any)))
            .OrderByDescending(n => n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
            .FirstOrDefault();
        if (_adapter == null) { Name = Kind = Network = Address = ""; Bars = null; LinkBitsPerSecond = 0; return; }
        Name = _adapter.Description;
        Kind = _adapter.NetworkInterfaceType switch
        {
            System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            System.Net.NetworkInformation.NetworkInterfaceType.Ethernet or System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet
                or System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT => "Ethernet",
            System.Net.NetworkInformation.NetworkInterfaceType.Wwanpp or System.Net.NetworkInformation.NetworkInterfaceType.Wwanpp2 => "Mobile",
            _ => "Network"
        };
        LinkBitsPerSecond = _adapter.Speed;
        Address = _adapter.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString() ?? "";
        try
        {
            var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            Network = profile?.ProfileName ?? "";
            Bars = profile is { IsWlanConnectionProfile: true } ? profile.GetSignalBars() : null;
        }
        catch (Exception) { Network = ""; Bars = null; }
    }
}
