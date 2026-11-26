using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// TDP control for AMD handheld chips (Ryzen Z2 Extreme and friends): the processor's power limit, CPU boost, the
/// maximum CPU speed, Windows' power mode and a temperature limit, plus per-game values from game profiles.
///
/// How it works: the power limit is set with RyzenAdj (github.com/FlyGoat/RyzenAdj), which needs administrator rights.
/// Rather than asking for permission every time, a one-time setup (one Windows permission prompt) installs RyzenAdj and
/// a small script into Program Files (which only administrators can change) and registers a scheduled task,
/// "SquareDeck\ApplyTdp", that runs that script with administrator rights in the background, without a window.
/// SquareDeck writes the wanted numbers to a request file and starts the task; the script accepts only numbers in a
/// safe range and only runs the RyzenAdj next to it.
/// </summary>
public static class TdpControl
{
    public sealed record ChipInfo(string Name, int Min, int Max, int Default, string Note, bool Known);

    public const string TaskName = @"SquareDeck\ApplyTdp";

    private static Settings? _settings;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly System.Windows.Threading.DispatcherTimer Debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private static readonly System.Windows.Threading.DispatcherTimer Reapply = new() { Interval = TimeSpan.FromSeconds(15) };
    private static bool? _lastPluggedIn;

    /// <summary>Watts chosen in the quick menu for the game that's running (until it closes).</summary>
    public static int? SessionWatts { get; private set; }

    /// <summary>The running game's profile, while it runs.</summary>
    public static GameProfile? GameProfile { get; private set; }

    /// <summary>The limit last applied, in watts (null before the first time).</summary>
    public static int? AppliedWatts { get; private set; }

    /// <summary>A short line about the last attempt ("Applied 15 W", or what went wrong).</summary>
    public static string LastResult { get; private set; } = "";

    public static event Action? Changed;

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SquareDeck", "TDP");

    private static string RequestPath => Path.Combine(Settings.DataDir, "tdp-request.txt");
    private static string ResultPath => Path.Combine(Settings.DataDir, "tdp-result.txt");

    /// <summary>RyzenAdj and the script are in place (the task is checked when it's started).</summary>
    public static bool Installed =>
        File.Exists(Path.Combine(InstallDir, "ryzenadj.exe")) && File.Exists(Path.Combine(InstallDir, "apply-tdp.ps1"));

    /// <summary>
    /// The limits go through the ROG Ally's firmware (ASUS's ATKACPI driver, as HandheldCompanion and G-Helper do)
    /// rather than RyzenAdj: chosen in Settings, or by itself when RyzenAdj isn't set up and the firmware is there.
    /// </summary>
    public static bool UsesAsus => Method == "asus";

    /// <summary>
    /// The ways this PC can set the power limit, best first: the handheld's own firmware (ROG Ally: ASUS's driver;
    /// Legion Go and MSI Claw: their WMI methods, through the device helper), then KX.exe for Intel processors
    /// (through the device helper), then RyzenAdj for AMD processors.
    /// </summary>
    public static List<string> AvailableMethods
    {
        get
        {
            // Asked many times a second by the stats and the quick menu: worked out at most every few seconds
            // (it reads the registry and looks for files).
            var now = DateTime.UtcNow;
            if (_methods != null && now - _methodsAt < TimeSpan.FromSeconds(4)) return _methods;
            _methods = FindMethods();
            _methodsAt = now;
            return _methods;
        }
    }

    private static List<string>? _methods;
    private static DateTime _methodsAt;

    /// <summary>Looks again for the ways to set the limit straight away (after setting one up or removing it).</summary>
    public static void RecheckMethods() => _methods = null;

    private static List<string> FindMethods()
    {
        {
            var list = new List<string>();
            var vendor = Handheld.Vendor;
            if (vendor == HandheldVendor.Asus && AllyControl.IsAlly && AsusAcpi.Available) list.Add("asus");
            if (vendor == HandheldVendor.Lenovo && DeviceHelper.Installed) list.Add("lenovo");
            if (vendor == HandheldVendor.Msi && DeviceHelper.Installed) list.Add("msi");
            if (IsIntel && DeviceHelper.Installed && DeviceHelper.HasKx) list.Add("kx");
            if (!IsIntel && Installed) list.Add("ryzenadj");
            return list;
        }
    }

    /// <summary>How the limit is set right now: "asus", "lenovo", "msi", "kx", "ryzenadj", or "" (not set up).</summary>
    public static string Method
    {
        get
        {
            if (_settings == null) return "";
            var options = AvailableMethods;
            if (options.Count == 0) return "";
            if (options.Contains(_settings.TdpMethod)) return _settings.TdpMethod;
            // No choice saved: RyzenAdj when it's set up (as before), else the first that works.
            if (_settings.TdpMethod.Length == 0 && options.Contains("ryzenadj")) return "ryzenadj";
            return options[0];
        }
    }

    public static string MethodName(string method) => method switch
    {
        "asus" => "ASUS firmware",
        "lenovo" => "Lenovo firmware",
        "msi" => "MSI firmware",
        "kx" => "Intel (KX)",
        "ryzenadj" => "RyzenAdj",
        _ => "Not set up"
    };

    /// <summary>An Intel processor (its limits are PL1 / PL2).</summary>
    public static bool IsIntel => CpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase);

    /// <summary>There's a way to apply limits: the handheld's firmware, KX.exe (Intel) or RyzenAdj (AMD).</summary>
    public static bool CanApply => Method.Length > 0;

    /// <summary>Set up, switched on, and on a supported chip.</summary>
    public static bool Ready => _settings is { TdpEnabled: true } && CanApply && Chip != null;

    // ================================================================ the chip

    private static ChipInfo? _chip;
    private static bool _chipChecked;

    private static string? _cpuName;

    /// <summary>The processor's name (read once: it doesn't change while SquareDeck runs).</summary>
    public static string CpuName
    {
        get
        {
            if (_cpuName != null) return _cpuName;
            try
            {
                return _cpuName = (Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                    "ProcessorNameString", "") as string ?? "").Trim();
            }
            catch (Exception) { return ""; }
        }
    }

    /// <summary>The processor and its sensible range, or null when it isn't an AMD chip RyzenAdj can drive.</summary>
    public static ChipInfo? Chip
    {
        get
        {
            if (_chipChecked) return _chip;
            _chipChecked = true;
            string name = CpuName;
            bool amd = name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
            var device = Handheld.Current;
            if (IsIntel)
            {
                // Intel: the handheld's range when it's known, else a cautious one.
                _chip = device != null
                    ? new ChipInfo(name, device.TdpMin, device.TdpMax, device.TdpDefault,
                        $"The {device.Name} runs at {device.TdpMin}–{device.TdpMax} W (from HandheldCompanion's device list).", true)
                    : new ChipInfo(name.Length > 0 ? name : "Intel processor", 8, 28, 15,
                        "This processor isn't one SquareDeck knows; the range is a cautious guess. Check your device's limits.", false);
                return _chip;
            }
            if (!amd) return _chip = null;
            bool Has(string part) => name.Contains(part, StringComparison.OrdinalIgnoreCase);
            _chip =
                Has("Z2 Extreme") ? new ChipInfo("AMD Ryzen Z2 Extreme", 5, 35, 20,
                    "AMD rates the Z2 Extreme at 15–35 W (28 W typical). Most handhelds also run fine lower, down to about 8 W.", true) :
                Has("Z1 Extreme") ? new ChipInfo("AMD Ryzen Z1 Extreme", 5, 30, 15, "AMD rates the Z1 Extreme at 9–30 W.", true) :
                Has("Z2 A") || Has("Z2A") ? new ChipInfo("AMD Ryzen Z2 A", 4, 20, 10, "AMD rates the Z2 A at 6–20 W.", true) :
                Has("Z2 Go") ? new ChipInfo("AMD Ryzen Z2 Go", 5, 30, 15, "AMD rates the Z2 Go at 15–30 W.", true) :
                Has("Z2") ? new ChipInfo("AMD Ryzen Z2", 5, 30, 15, "AMD rates the Z2 at 15–30 W.", true) :
                Has("Z1") ? new ChipInfo("AMD Ryzen Z1", 5, 30, 15, "AMD rates the Z1 at 9–30 W.", true) :
                new ChipInfo(name.Length > 0 ? name : "AMD processor", 5, 30, 15,
                    "This processor isn't one SquareDeck knows; the range is a cautious guess. Check your device's limits.", false);
            // A recognized handheld knows its own range (from HandheldCompanion's device list): use it where the chip is
            // unknown, and never go above what the device is built for.
            if (device != null)
            {
                _chip = _chip.Known
                    ? _chip with { Max = Math.Min(_chip.Max, Math.Max(device.TdpMax, _chip.Min + 1)), Default = device.TdpDefault }
                    : new ChipInfo(_chip.Name, device.TdpMin, device.TdpMax, device.TdpDefault,
                        $"The {device.Name} runs at {device.TdpMin}–{device.TdpMax} W (from HandheldCompanion's device list).", true);
            }
            return _chip;
        }
    }

    public static int Clamp(int watts) => Chip is { } c ? Math.Clamp(watts, c.Min, c.Max) : watts;

    /// <summary>
    /// The slow and fast boost limits that go with a sustained limit. With the boost window on (the default since
    /// 8.5.42) the chip may burst above the limit for a moment, as it does under ASUS's own modes: at least 2 W more for
    /// the slow limit and 5 W more for the fast one (10% and 25% at higher limits), never past the chip's maximum.
    /// Without it, all three are the same and the chip can never burst, which makes everything (menus included) feel
    /// sluggish at lower limits.
    /// </summary>
    public static (int Slow, int Fast) BoostLimits(int watts)
    {
        if (_settings?.TdpBoostWindow != "short" || Chip is not { } c || watts <= 0) return (watts, watts);
        int slow = Math.Min(c.Max, Math.Max((int)Math.Round(watts * 1.1), watts + 2));
        int fast = Math.Min(c.Max, Math.Max((int)Math.Round(watts * 1.25), watts + 5));
        return (Math.Max(slow, watts), Math.Max(fast, slow));
    }

    // ================================================================ settings in effect

    public static void Init(Settings settings)
    {
        _settings = settings;
        Debounce.Tick += (_, _) => { Debounce.Stop(); _ = ApplyAsync(); };
        Reapply.Tick += (_, _) => { if (Ready) _ = ApplyAsync(quiet: true); };
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            // Sleep can put the firmware's own limit back.
            var ui = System.Windows.Application.Current?.Dispatcher;
            if (e.Mode == PowerModes.Resume && Ready) ui?.BeginInvoke(new Action(() => { _cpuApplied = null; _ = ApplyAsync(quiet: true); }));
            else if (e.Mode == PowerModes.StatusChange) ui?.BeginInvoke(new Action(CheckPower)); // charger plugged in or out
        };
        UpdateReapplyTimer();
        if (Ready) _ = ApplyAsync(quiet: true);
    }

    /// <summary>
    /// Re-applying every 15 seconds: when chosen, and always through the ROG Ally's firmware, which puts its own limit
    /// back whenever its operating mode changes (Command Center, the charger, waking from sleep). There it's three
    /// quick firmware calls; Windows' CPU settings are only written again when they change.
    /// </summary>
    public static void UpdateReapplyTimer()
    {
        if (_settings is { TdpEnabled: true } && CanApply && (_settings.TdpReapply || Method == "asus")) Reapply.Start();
        else Reapply.Stop();
    }

    /// <summary>The limit that should be in effect now: the quick menu, then the game, then plugged-in/battery.</summary>
    public static int EffectiveWatts
    {
        get
        {
            var s = _settings!;
            bool plugged = Battery.IsPluggedIn();
            int everyday = plugged && s.TdpPluggedWatts > 0 ? s.TdpPluggedWatts : s.TdpWatts;
            int watts = SessionWatts
                        ?? (GameProfile is { } g && (plugged ? (g.TdpPluggedWatts > 0 ? g.TdpPluggedWatts : g.TdpWatts) : g.TdpWatts) is > 0 and var gw
                            ? gw : everyday);
            return Clamp(watts);
        }
    }

    public static bool EffectiveBoost => GameProfile?.CpuBoost ?? _settings!.CpuBoost;
    public static int EffectiveMaxCpu => GameProfile is { MaxCpu: > 0 } g ? g.MaxCpu : _settings!.MaxCpu;
    public static string EffectivePowerMode => GameProfile?.PowerMode ?? _settings!.WindowsPowerMode;

    /// <summary>A game started (its profile, may be null) or closed (null).</summary>
    public static void SetGame(GameProfile? profile)
    {
        GameProfile = profile;
        if (profile == null) SessionWatts = null;
        if (Ready) _ = ApplyAsync(quiet: true);
    }

    /// <summary>The quick menu's slider: applied a moment after it stops moving.</summary>
    public static void SetSessionWatts(int watts)
    {
        SessionWatts = Clamp(watts);
        Debounce.Stop();
        Debounce.Start();
    }

    /// <summary>Drops the quick menu's (or the Game Bar widget's) limit: back to the game's profile or your usual one.</summary>
    public static void ClearSession()
    {
        SessionWatts = null;
        if (Ready) _ = ApplyAsync(quiet: true);
        Changed?.Invoke();
    }

    /// <summary>Called every few seconds: "plugged in" limits follow the charger.</summary>
    public static void CheckPower()
    {
        if (!Ready || (_settings!.TdpPluggedWatts <= 0 && GameProfile is not { TdpPluggedWatts: > 0 })) return;
        bool plugged = Battery.IsPluggedIn();
        if (_lastPluggedIn == plugged) return;
        _lastPluggedIn = plugged;
        _ = ApplyAsync(quiet: true);
    }

    // ================================================================ applying

    /// <summary>Applies everything in effect now. Returns false (with LastResult saying why) when it couldn't.</summary>
    public static async Task<bool> ApplyAsync(bool quiet = false)
    {
        if (_settings == null || Chip == null) return false;
        if (!await Gate.WaitAsync(0)) { Debounce.Stop(); Debounce.Start(); return false; } // one at a time; try again shortly
        try
        {
            var s = _settings;
            int watts = s.TdpEnabled ? EffectiveWatts : 0;
            ApplyPowerMode(EffectivePowerMode);

            string method = Method;
            if (method is "asus" or "lenovo" or "msi" or "kx")
            {
                // Windows' CPU settings (boost, top speed, energy preference, cores): only written when they change, so
                // re-applying the limit doesn't start powercfg over and over.
                string cpu = $"{EffectiveBoost}|{EffectiveMaxCpu}|{s.CpuEpp}|{s.CpuCores}";
                if (!quiet || cpu != _cpuApplied)
                {
                    await SetCpuWithoutHelperAsync(EffectiveBoost, EffectiveMaxCpu);
                    await ApplyCpuExtrasAsync(s);
                    _cpuApplied = cpu;
                }
                if (!s.TdpEnabled || watts <= 0) { LastResult = ""; return true; }
                var (slowW, fastW) = BoostLimits(watts);
                bool done;
                string why;
                switch (method)
                {
                    case "asus":
                        done = await Task.Run(() => AsusAcpi.SetPowerLimits(watts, slowW, fastW));
                        why = AsusAcpi.Available ? "The ASUS firmware refused the limit" : "Couldn't reach the ASUS firmware";
                        break;
                    case "lenovo":
                        done = (await DeviceHelper.RunAsync($"legion-tdp {watts} {fastW}")).Ok;
                        why = DeviceHelper.LastResult;
                        break;
                    case "msi":
                        done = (await DeviceHelper.RunAsync(IsIntel ? $"claw-tdp {watts} {fastW}" : $"claw-tdp {watts} {slowW} {fastW}")).Ok;
                        why = DeviceHelper.LastResult;
                        break;
                    default:
                        done = (await DeviceHelper.RunAsync($"intel-tdp {watts} {fastW}")).Ok;
                        why = DeviceHelper.LastResult;
                        break;
                }
                if (done) AppliedWatts = watts;
                LastResult = done
                    ? (fastW > watts ? $"Applied {watts} W, bursts to {fastW} W ({MethodName(method)})" : $"Applied {watts} W ({MethodName(method)})")
                    : "Couldn't apply: " + why;
                return done;
            }

            if (!Installed)
            {
                // Without the helper, CPU boost and speed can still be set (Windows allows that for your own plan).
                await SetCpuWithoutHelperAsync(EffectiveBoost, EffectiveMaxCpu);
                await ApplyCpuExtrasAsync(s);
                LastResult = s.TdpEnabled ? "TDP control isn't set up yet" : "";
                return false;
            }

            // Sustained (STAPM), slow and fast limits. With the short boost window the slow and fast limits sit a little
            // above the chosen one, so the chip can burst briefly (loading screens, busy scenes) and settle back.
            var (slowLimit, fastLimit) = BoostLimits(watts);
            int mw = watts * 1000, slow = slowLimit * 1000, fast = fastLimit * 1000;
            int profile = s.RyzenProfile switch { "saving" => 1, "performance" => 2, _ => 0 };
            string request = string.Join(' ', mw, fast, slow, s.TdpEnabled ? s.TdpTempLimit : 0, EffectiveBoost ? 1 : 0, EffectiveMaxCpu, profile);
            await ApplyCpuExtrasAsync(s);
            Directory.CreateDirectory(Settings.DataDir);
            await File.WriteAllTextAsync(RequestPath, request);
            var startedAt = DateTime.UtcNow;
            try { File.Delete(ResultPath); } catch (Exception) { }

            var (code, output) = await ProcessUtil.RunAsync("schtasks.exe", $"/Run /TN \"{TaskName}\"");
            if (code != 0)
            {
                LastResult = "The TDP task is missing; set up TDP control again";
                return false;
            }

            // Wait for the script to report back (it usually takes under a second).
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(200);
                if (File.Exists(ResultPath) && File.GetLastWriteTimeUtc(ResultPath) >= startedAt.AddSeconds(-1)) break;
            }
            string result = File.Exists(ResultPath) ? await File.ReadAllTextAsync(ResultPath) : "";
            var lines = result.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) { LastResult = "No answer from the TDP task"; return false; }
            bool ok = lines[0].Trim() == "0";
            if (ok)
            {
                AppliedWatts = watts > 0 ? watts : AppliedWatts;
                LastResult = watts > 0 ? $"Applied {watts} W" : "Applied";
            }
            else
            {
                string why = lines.Skip(1).FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "RyzenAdj reported an error";
                LastResult = "Couldn't apply: " + (why.Length > 90 ? why[..90] + "…" : why);
            }
            return ok;
        }
        catch (Exception ex)
        {
            LastResult = "Couldn't apply: " + ex.Message;
            return false;
        }
        finally
        {
            Gate.Release();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Energy preference and how many cores Windows may use, on the current power plan (allowed without administrator
    /// rights). Only changed when set to something other than "leave as it is".
    /// </summary>
    private static async Task ApplyCpuExtrasAsync(Settings s)
    {
        try
        {
            bool any = false;
            foreach (var which in new[] { "/setacvalueindex", "/setdcvalueindex" })
            {
                if (s.CpuEpp is >= 0 and <= 100)
                {
                    await ProcessUtil.RunAsync("powercfg.exe", $"{which} SCHEME_CURRENT SUB_PROCESSOR PERFEPP {s.CpuEpp}");
                    any = true;
                }
                int cores = Math.Clamp(s.CpuCores, 25, 100);
                if (cores < 100 || _coresChanged)
                {
                    await ProcessUtil.RunAsync("powercfg.exe", $"{which} SCHEME_CURRENT SUB_PROCESSOR CPMAXCORES {cores}");
                    any = true;
                }
            }
            _coresChanged = s.CpuCores < 100;
            if (any) await ProcessUtil.RunAsync("powercfg.exe", "/setactive SCHEME_CURRENT");
        }
        catch (Exception) { }
    }

    private static string? _cpuApplied;

    private static bool _coresChanged = true; // the first time, "all cores" is written too, undoing an earlier limit

    /// <summary>The installed helper script is from before 5.9 and can't apply the power profile.</summary>
    public static bool HelperNeedsUpdate
    {
        get
        {
            try
            {
                var script = Path.Combine(InstallDir, "apply-tdp.ps1");
                return Installed && File.Exists(script) && !File.ReadAllText(script).Contains("SquareDeck-helper-v2");
            }
            catch (Exception) { return false; }
        }
    }

    private static async Task SetCpuWithoutHelperAsync(bool boost, int maxCpu)
    {
        try
        {
            int mode = boost ? 2 : 0;
            foreach (var which in new[] { "/setacvalueindex", "/setdcvalueindex" })
            {
                await ProcessUtil.RunAsync("powercfg.exe", $"{which} SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {mode}");
                if (maxCpu is >= 5 and <= 100)
                    await ProcessUtil.RunAsync("powercfg.exe", $"{which} SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {maxCpu}");
            }
            await ProcessUtil.RunAsync("powercfg.exe", "/setactive SCHEME_CURRENT");
        }
        catch (Exception) { }
    }

    // ================================================================ Windows power mode

    public static readonly (string Id, string Name)[] WindowsPowerModes =
    {
        ("", "Don't change"), ("efficiency", "Best power efficiency"), ("balanced", "Balanced"), ("performance", "Best performance")
    };

    public static string PowerModeName(string? id) => WindowsPowerModes.FirstOrDefault(m => m.Id == (id ?? "")).Name ?? "Don't change";

    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(Guid scheme);

    /// <summary>The "Power mode" slider in Windows Settings (it works while the Balanced plan is active).</summary>
    private static void ApplyPowerMode(string? mode)
    {
        Guid? scheme = mode switch
        {
            "efficiency" => new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a"),
            "balanced" => Guid.Empty,
            "performance" => new Guid("ded574b5-45a0-4f42-8737-46345c09c238"),
            _ => null
        };
        if (scheme == null) return;
        try { PowerSetActiveOverlayScheme(scheme.Value); } catch (Exception) { }
    }

    // ================================================================ setup

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck");
        return http;
    }

    /// <summary>The copy of RyzenAdj that ships with SquareDeck (Tools\RyzenAdj next to the app).</summary>
    public static string BundledDir => Path.Combine(AppContext.BaseDirectory, "Tools", "RyzenAdj");

    public static bool HasBundled => File.Exists(Path.Combine(BundledDir, "ryzenadj.exe"));

    /// <summary>
    /// Installs RyzenAdj with the task (one Windows permission prompt): the copy that ships with SquareDeck, or the
    /// latest from GitHub if that's missing. Returns null when it worked, or what went wrong.
    /// </summary>
    public static async Task<string?> InstallAsync(Action<string>? progress = null)
    {
        string work = Path.Combine(Path.GetTempPath(), "SquareDeck-TDP-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(work);
            string source = HasBundled ? BundledDir : await DownloadRyzenAdjAsync(work, progress);
            return await InstallFromAsync(source, work, progress);
        }
        catch (HttpRequestException ex)
        {
            return "Couldn't download RyzenAdj: " + ex.Message;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (Exception) { }
        }
    }

    /// <summary>The latest Windows release from GitHub, unpacked into the work folder. Returns the folder with ryzenadj.exe.</summary>
    private static async Task<string> DownloadRyzenAdjAsync(string work, Action<string>? progress)
    {
        progress?.Invoke("Finding the latest RyzenAdj…");
        using var release = JsonDocument.Parse(await Http.GetStringAsync("https://api.github.com/repos/FlyGoat/RyzenAdj/releases/latest"));
        string? url = null, assetName = null;
        foreach (var asset in release.RootElement.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && name.Contains("win", StringComparison.OrdinalIgnoreCase))
            {
                url = asset.GetProperty("browser_download_url").GetString();
                assetName = name;
                break;
            }
        }
        if (url == null) throw new InvalidOperationException("The latest RyzenAdj release has no Windows download.");

        progress?.Invoke("Downloading " + assetName + "…");
        string zip = Path.Combine(work, "ryzenadj.zip");
        await File.WriteAllBytesAsync(zip, await Http.GetByteArrayAsync(url));
        string unpacked = Path.Combine(work, "files");
        ZipFile.ExtractToDirectory(zip, unpacked);
        string? exe = Directory.EnumerateFiles(unpacked, "ryzenadj.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (exe == null) throw new InvalidOperationException("The RyzenAdj download didn't contain ryzenadj.exe.");
        return Path.GetDirectoryName(exe)!;
    }

    private static async Task<string?> InstallFromAsync(string source, string work, Action<string>? progress)
    {
        progress?.Invoke("Waiting for Windows' permission…");
        string script = Path.Combine(work, "apply-tdp.ps1");
        await File.WriteAllTextAsync(script, ApplyScript(), new UTF8Encoding(true));
        string xml = Path.Combine(work, "task.xml");
        await File.WriteAllTextAsync(xml, TaskXml(), Encoding.Unicode);
        string install = Path.Combine(work, "install.ps1");
        await File.WriteAllTextAsync(install, $$"""
            $ErrorActionPreference = 'Stop'
            $dest = '{{Ps(InstallDir)}}'
            New-Item -ItemType Directory -Force -Path $dest | Out-Null
            Copy-Item -Path (Join-Path '{{Ps(source)}}' '*') -Destination $dest -Recurse -Force
            Copy-Item -LiteralPath '{{Ps(script)}}' -Destination (Join-Path $dest 'apply-tdp.ps1') -Force
            schtasks.exe /Create /F /TN '{{TaskName}}' /XML '{{Ps(xml)}}' | Out-Null
            exit $LASTEXITCODE
            """, new UTF8Encoding(true));

        int code = await RunElevatedAsync(install);
        RecheckMethods();
        if (code == -1) return "Windows' permission prompt was declined, so nothing was installed.";
        if (code != 0 || !Installed) return "Setup didn't finish (code " + code + ").";

        progress?.Invoke("Testing…");
        UpdateReapplyTimer();
        return await ApplyAsync() ? null : LastResult;
    }

    /// <summary>Removes RyzenAdj, the script and the task (one Windows permission prompt).</summary>
    public static async Task<string?> UninstallAsync()
    {
        string work = Path.Combine(Path.GetTempPath(), "SquareDeck-TDP-remove.ps1");
        await File.WriteAllTextAsync(work, $$"""
            schtasks.exe /Delete /F /TN '{{TaskName}}' 2>$null | Out-Null
            Remove-Item -LiteralPath '{{Ps(InstallDir)}}' -Recurse -Force -ErrorAction SilentlyContinue
            exit 0
            """, new UTF8Encoding(true));
        int code = await RunElevatedAsync(work);
        try { File.Delete(work); } catch (Exception) { }
        RecheckMethods();
        UpdateReapplyTimer();
        Changed?.Invoke();
        return code == -1 ? "Windows' permission prompt was declined." : Installed ? "Some files couldn't be removed." : null;
    }

    /// <summary>Runs a PowerShell script as administrator. -1 when the prompt is declined.</summary>
    private static async Task<int> RunElevatedAsync(string scriptPath)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"")
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(psi);
            if (process == null) return -1;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1; // the user said no
        }
    }

    /// <summary>A string for inside single quotes in PowerShell.</summary>
    private static string Ps(string text) => text.Replace("'", "''");

    private static string ApplyScript() => $$"""
        # Written by SquareDeck (SquareDeck-helper-v2). The "SquareDeck\ApplyTdp" scheduled task runs this with administrator rights.
        # It reads only numbers from the request file, checks they're in a safe range, and only runs the RyzenAdj
        # next to this script (in Program Files, which only administrators can change).
        $request = '{{Ps(RequestPath)}}'
        $result = '{{Ps(ResultPath)}}'
        try {
            $v = @((Get-Content -LiteralPath $request -Raw).Trim() -split '\s+')
            if ($v.Count -lt 6) { throw 'Bad request' }
            foreach ($x in $v) { if ($x -notmatch '^\d{1,6}$') { throw 'Bad request' } }
            $stapm = [int]$v[0]; $fast = [int]$v[1]; $slow = [int]$v[2]; $temp = [int]$v[3]; $boost = [int]$v[4]; $maxcpu = [int]$v[5]
            $code = 0
            $out = @()
            if ($stapm -gt 0) {
                foreach ($w in @($stapm, $fast, $slow)) { if ($w -lt 3000 -or $w -gt 60000) { throw 'Limit out of range' } }
                $a = @("--stapm-limit=$stapm", "--fast-limit=$fast", "--slow-limit=$slow")
                if ($temp -ge 60 -and $temp -le 105) { $a += "--tctl-temp=$temp" }
                if ($v.Count -ge 7) {
                    if ([int]$v[6] -eq 1) { $a += '--power-saving' }
                    elseif ([int]$v[6] -eq 2) { $a += '--max-performance' }
                }
                $out = & (Join-Path $PSScriptRoot 'ryzenadj.exe') @a 2>&1 | ForEach-Object { "$_" }
                $code = $LASTEXITCODE
            }
            $mode = if ($boost -eq 1) { 2 } else { 0 }
            foreach ($which in @('/setacvalueindex', '/setdcvalueindex')) {
                powercfg.exe $which SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE $mode | Out-Null
                if ($maxcpu -ge 5 -and $maxcpu -le 100) { powercfg.exe $which SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX $maxcpu | Out-Null }
            }
            powercfg.exe /setactive SCHEME_CURRENT | Out-Null
            Set-Content -LiteralPath $result -Value (@("$code") + $out)
        } catch {
            Set-Content -LiteralPath $result -Value @('1', $_.Exception.Message)
        }
        """;

    private static string TaskXml()
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        string script = System.Security.SecurityElement.Escape(Path.Combine(InstallDir, "apply-tdp.ps1"));
        // S4U: runs in the background as you, with administrator rights, without a window popping up over the game.
        // Battery conditions are off, or it would refuse to run on a handheld that isn't plugged in.
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>SquareDeck: applies the TDP limit chosen in SquareDeck (RyzenAdj).</Description></RegistrationInfo>
              <Principals>
                <Principal id="Author"><UserId>{sid}</UserId><LogonType>S4U</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>Queue</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT1M</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>powershell.exe</Command>
                  <Arguments>-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{script}"</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
