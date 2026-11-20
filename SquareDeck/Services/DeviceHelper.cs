using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace SquareDeck.Services;

/// <summary>
/// The device helper: a small script, installed once with one Windows permission prompt, that changes handheld
/// settings needing administrator rights, without asking every time. It's run by the "SquareDeck\DeviceHelper"
/// scheduled task (like the TDP helper) and only does what's on its list:
///  - Lenovo Legion Go: Legion mode, power limits, fan boost, the 80% charge limit, and reading temperatures and fans
///    (Lenovo's WMI methods);
///  - MSI Claw: user scenario, power limits, fan boost and the charge limit (MSI's WMI interface);
///  - Intel processors: the power limits (PL1 / PL2), through KX.exe from a HandheldCompanion install.
/// The WMI calls and values follow HandheldCompanion by Valkirie (Devices/Lenovo/LegionGo.cs, Devices/MSI/ClawA1M.cs,
/// Processors/Intel/KX.cs). HandheldCompanion is licensed CC BY-NC-SA 4.0; this file, adapted from it, is shared
/// under the same licence. KX.exe isn't included: it's copied from your own HandheldCompanion install.
/// </summary>
public static class DeviceHelper
{
    public const string TaskName = @"SquareDeck\DeviceHelper";
    private const string Marker = "SquareDeck-device-helper-v1";

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SquareDeck", "Device");

    private static string ScriptPath => Path.Combine(InstallDir, "device-helper.ps1");
    private static string RequestPath => Path.Combine(Settings.DataDir, "device-request.txt");
    private static string ResultPath => Path.Combine(Settings.DataDir, "device-result.txt");
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static (bool Installed, bool Kx) _state;
    private static DateTime _stateAt = DateTime.MinValue;

    /// <summary>Checked again at most every few seconds (it's asked often, by TDP and the settings pages).</summary>
    private static (bool Installed, bool Kx) State
    {
        get
        {
            if (DateTime.UtcNow - _stateAt < TimeSpan.FromSeconds(5)) return _state;
            _stateAt = DateTime.UtcNow;
            try
            {
                bool installed = File.Exists(ScriptPath) && File.ReadAllText(ScriptPath).Contains(Marker);
                _state = (installed, installed && File.Exists(Path.Combine(InstallDir, "KX", "KX.exe")));
            }
            catch (Exception) { _state = (false, false); }
            return _state;
        }
    }

    private static void Forget() => _stateAt = DateTime.MinValue;

    /// <summary>The helper is set up (and is this version).</summary>
    public static bool Installed => State.Installed;

    /// <summary>KX.exe is set up with the helper (for Intel power limits).</summary>
    public static bool HasKx => State.Kx;

    /// <summary>Where KX.exe is in a HandheldCompanion install on this PC, if there is one.</summary>
    public static string? FindKx()
    {
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string baseDir = Environment.GetFolderPath(root);
            if (baseDir.Length == 0) continue;
            foreach (var name in new[] { "Handheld Companion", "HandheldCompanion" })
            {
                string dir = Path.Combine(baseDir, name, "Resources", "Intel", "KX");
                if (File.Exists(Path.Combine(dir, "KX.exe"))) return dir;
            }
        }
        return null;
    }

    /// <summary>A short line about the last attempt.</summary>
    public static string LastResult { get; private set; } = "";

    /// <summary>
    /// Runs one command through the helper and returns whether it worked, with what it printed. Commands are checked
    /// again by the script itself (only known names and numbers).
    /// </summary>
    public static async Task<(bool Ok, string[] Lines)> RunAsync(string command)
    {
        if (!Installed) { LastResult = "The device helper isn't set up"; return (false, Array.Empty<string>()); }
        await Gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            await File.WriteAllTextAsync(RequestPath, command);
            var startedAt = DateTime.UtcNow;
            try { File.Delete(ResultPath); } catch (Exception) { }
            var (code, _) = await ProcessUtil.RunAsync("schtasks.exe", $"/Run /TN \"{TaskName}\"");
            if (code != 0) { LastResult = "The device helper's task is missing; set it up again"; return (false, Array.Empty<string>()); }
            for (int i = 0; i < 50; i++)
            {
                await Task.Delay(200);
                if (File.Exists(ResultPath) && File.GetLastWriteTimeUtc(ResultPath) >= startedAt.AddSeconds(-1)) break;
            }
            string text = File.Exists(ResultPath) ? await File.ReadAllTextAsync(ResultPath) : "";
            var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) { LastResult = "No answer from the device helper"; return (false, Array.Empty<string>()); }
            bool ok = lines[0].Trim() == "0";
            LastResult = ok ? "Done" : (lines.Length > 1 ? lines[1] : "The device didn't accept it");
            return (ok, lines.Skip(1).ToArray());
        }
        catch (Exception ex)
        {
            LastResult = ex.Message;
            return (false, Array.Empty<string>());
        }
        finally { Gate.Release(); }
    }

    /// <summary>Reads key=value lines from a helper answer.</summary>
    public static Dictionary<string, string> Values(string[] lines) =>
        lines.Select(l => l.Split('=', 2)).Where(p => p.Length == 2).GroupBy(p => p[0].Trim())
             .ToDictionary(g => g.Key, g => g.Last()[1].Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Installs the helper (one Windows permission prompt), with KX.exe when a folder with it is given.</summary>
    public static async Task<string?> InstallAsync(string? kxFolder)
    {
        string work = Path.Combine(Path.GetTempPath(), "SquareDeck-Device-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(work);
            string script = Path.Combine(work, "device-helper.ps1");
            await File.WriteAllTextAsync(script, HelperScript(), new UTF8Encoding(true));
            string xml = Path.Combine(work, "task.xml");
            await File.WriteAllTextAsync(xml, TaskXml(), Encoding.Unicode);
            string copyKx = kxFolder != null && File.Exists(Path.Combine(kxFolder, "KX.exe"))
                ? $"New-Item -ItemType Directory -Force -Path (Join-Path $dest 'KX') | Out-Null\nCopy-Item -Path (Join-Path '{Ps(kxFolder)}' '*') -Destination (Join-Path $dest 'KX') -Recurse -Force"
                : "";
            string install = Path.Combine(work, "install.ps1");
            await File.WriteAllTextAsync(install, $$"""
                $ErrorActionPreference = 'Stop'
                $dest = '{{Ps(InstallDir)}}'
                New-Item -ItemType Directory -Force -Path $dest | Out-Null
                Copy-Item -LiteralPath '{{Ps(script)}}' -Destination (Join-Path $dest 'device-helper.ps1') -Force
                {{copyKx}}
                schtasks.exe /Create /F /TN '{{TaskName}}' /XML '{{Ps(xml)}}' | Out-Null
                exit $LASTEXITCODE
                """, new UTF8Encoding(true));
            int code = await RunElevatedAsync(install);
            Forget();
            if (code == -1) return "Windows' permission prompt was declined, so nothing was set up.";
            if (code != 0 || !Installed) return "Setup didn't finish (code " + code + ").";
            var (ok, _) = await RunAsync("ping");
            return ok ? null : LastResult;
        }
        catch (Exception ex) { return ex.Message; }
        finally { try { Directory.Delete(work, recursive: true); } catch (Exception) { } }
    }

    public static async Task<string?> UninstallAsync()
    {
        string work = Path.Combine(Path.GetTempPath(), "SquareDeck-Device-remove.ps1");
        await File.WriteAllTextAsync(work, $$"""
            schtasks.exe /Delete /F /TN '{{TaskName}}' 2>$null | Out-Null
            Remove-Item -LiteralPath '{{Ps(InstallDir)}}' -Recurse -Force -ErrorAction SilentlyContinue
            exit 0
            """, new UTF8Encoding(true));
        int code = await RunElevatedAsync(work);
        Forget();
        try { File.Delete(work); } catch (Exception) { }
        return code == -1 ? "Windows' permission prompt was declined." : Installed ? "Some files couldn't be removed." : null;
    }

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
        catch (System.ComponentModel.Win32Exception) { return -1; }
    }

    private static string Ps(string text) => text.Replace("'", "''");

    private static string HelperScript() => $$"""
        # {{Marker}}. Written by SquareDeck. The "SquareDeck\DeviceHelper" scheduled task runs this with administrator
        # rights. It reads one command from the request file and only runs the commands below, with numbers checked.
        # Lenovo and MSI calls follow HandheldCompanion (github.com/Valkirie/HandheldCompanion).
        $request = '{{Ps(RequestPath)}}'
        $result = '{{Ps(ResultPath)}}'
        $out = New-Object System.Collections.ArrayList
        function Num($v, $min, $max) { if ($v -notmatch '^\d{1,5}$') { throw 'Bad number' }; $n = [int]$v; if ($n -lt $min -or $n -gt $max) { throw 'Out of range' }; return $n }

        # ---- Lenovo (root\WMI: LENOVO_OTHER_METHOD, LENOVO_GAMEZONE_DATA)
        function LenovoOther { Get-CimInstance -Namespace root\WMI -ClassName LENOVO_OTHER_METHOD | Select-Object -First 1 }
        function LenovoSet([uint32]$id, [int]$value) {
            Invoke-CimMethod -InputObject (LenovoOther) -MethodName SetFeatureValue -Arguments @{ IDs = $id; value = $value } | Out-Null
        }
        function LenovoGet([uint32]$id) {
            (Invoke-CimMethod -InputObject (LenovoOther) -MethodName GetFeatureValue -Arguments @{ IDs = $id }).Value
        }
        function LenovoMode([int]$mode) {
            $g = Get-CimInstance -Namespace root\WMI -ClassName LENOVO_GAMEZONE_DATA | Select-Object -First 1
            Invoke-CimMethod -InputObject $g -MethodName SetSmartFanMode -Arguments @{ Data = $mode } | Out-Null
        }
        function LenovoGetMode {
            $g = Get-CimInstance -Namespace root\WMI -ClassName LENOVO_GAMEZONE_DATA | Select-Object -First 1
            (Invoke-CimMethod -InputObject $g -MethodName GetSmartFanMode).Data
        }

        # ---- MSI (root\WMI: MSI_ACPI; 32-byte packages, first byte the data block)
        function MsiObject { Get-WmiObject -Namespace root\WMI -Class MSI_ACPI | Select-Object -First 1 }
        function MsiCall($m, [string]$method, [byte[]]$package) {
            $p = $null; $d = $null
            try { $p = $m.GetMethodParameters($method); $d = $p['Data'] } catch { }
            if ($d -eq $null) { $p = $m.InvokeMethod('Get_WMI', $null, $null); $d = $p['Data'] }
            if ($d -eq $null) { throw 'MSI WMI interface not found' }
            $d.SetPropertyValue('Bytes', $package)
            $p.SetPropertyValue('Data', $d)
            return $m.InvokeMethod($method, $p, $null)
        }
        function MsiGet($m, [string]$method, [int]$index) {
            $pkg = New-Object byte[] 32; $pkg[0] = [byte]$index
            $r = MsiCall $m $method $pkg
            $bytes = $r['Data']['Bytes']
            if ($bytes -eq $null -or $bytes.Length -lt 2 -or $bytes[0] -ne 1) { return $null }
            return ,($bytes[1..($bytes.Length - 1)])
        }
        function MsiSet($m, [int]$index, [int]$value) {
            $pkg = New-Object byte[] 32; $pkg[0] = [byte]$index; $pkg[1] = [byte]$value
            MsiCall $m 'Set_Data' $pkg | Out-Null
        }

        # ---- Intel power limits through KX.exe (MCHBAR package power limit and MSR 0x610)
        function Kx { param([string[]]$a) & (Join-Path $PSScriptRoot 'KX\KX.exe') @a 2>&1 | ForEach-Object { "$_" } }
        function KxReturn([string[]]$lines) { foreach ($l in $lines) { if ($l -match 'Return\s+(\d+)') { return [long]$Matches[1] } }; return $null }

        try {
            $parts = @((Get-Content -LiteralPath $request -Raw).Trim() -split '\s+')
            $cmd = $parts[0]
            switch ($cmd) {
                'ping' { [void]$out.Add('ok') }
                'legion-mode' { LenovoMode (Num $parts[1] 1 255) }
                'legion-tdp' {
                    $long = Num $parts[1] 3 90; $short = Num $parts[2] 3 90
                    LenovoMode 255   # Custom, so the limits stick
                    LenovoSet 0x0102FF00 $long; LenovoSet 0x0106FF00 $long
                    LenovoSet 0x0101FF00 $short; LenovoSet 0x0103FF00 $short
                }
                'legion-fanfull' { LenovoSet 0x04020000 (Num $parts[1] 0 1) }
                'legion-charge' { LenovoSet 0x03010001 (Num $parts[1] 0 1) }
                'legion-read' {
                    foreach ($pair in @(@('cputemp', 0x05040000), @('gputemp', 0x05050000), @('cpufan', 0x04030001), @('gpufan', 0x04030002), @('fanfull', 0x04020000), @('charge', 0x03010001))) {
                        try { [void]$out.Add($pair[0] + '=' + (LenovoGet $pair[1])) } catch { }
                    }
                    try { [void]$out.Add('mode=' + (LenovoGetMode)) } catch { }
                }
                'claw-tdp' {
                    $long = Num $parts[1] 3 90; $short = Num $parts[2] 3 90
                    $fast = if ($parts.Count -ge 4) { Num $parts[3] 3 90 } else { 0 }
                    $m = MsiObject
                    MsiSet $m 80 $long; MsiSet $m 81 $short
                    if ($fast -gt 0) { MsiSet $m 82 $fast }
                }
                'claw-mode' {
                    $mode = Num $parts[1] 0 6
                    $m = MsiObject
                    $ap = MsiGet $m 'Get_AP' 0
                    if ($ap -eq $null -or ($ap[2] -band 128) -eq 0) { throw 'This Claw does not report user scenarios' }
                    $v = $ap[2] -band 195; $v = $v -bor 192; $v = $v -band 252; $v = $v + $mode
                    MsiSet $m 210 $v
                }
                'claw-fanfull' {
                    $on = Num $parts[1] 0 1
                    $m = MsiObject
                    $d = MsiGet $m 'Get_Data' 152; $b = if ($d -ne $null) { [int]$d[0] } else { 0 }
                    if ($on -eq 1) { $b = $b -bor 128 } else { $b = $b -band 127 }
                    MsiSet $m 152 $b
                }
                'claw-charge' {
                    $p = Num $parts[1] 20 100
                    $m = MsiObject
                    $d = MsiGet $m 'Get_Data' 215; $cur = if ($d -ne $null) { [int]$d[0] } else { 128 }
                    $mask = $cur -band 127
                    MsiSet $m 215 ($cur - $mask + $p)
                }
                'intel-tdp' {
                    $pl1 = Num $parts[1] 3 120; $pl2 = Num $parts[2] 3 120
                    if (-not (Test-Path (Join-Path $PSScriptRoot 'KX\KX.exe'))) { throw 'KX.exe is not set up' }
                    $mchbar = $null
                    foreach ($a in @('0xfedc0000', '0xfed10000')) {
                        $r = KxReturn (Kx @('/rdmem32', $a))
                        if ($r -ne $null -and $r -ne 4294967295) { $mchbar = $a.Substring(0, 6) + '59'; break }
                    }
                    if ($mchbar -eq $null) { throw 'Could not find the power limit register' }
                    $h1 = '{0:X3}' -f ($pl1 * 8); $h2 = '{0:X3}' -f ($pl2 * 8)
                    $null = Kx @('/wrmem16', ($mchbar + 'a0'), ('0x8' + $h1))
                    $null = Kx @('/wrmem16', ($mchbar + 'a4'), ('0x8' + $h2))
                    $null = Kx @('/wrmsr', '0x610', ('0x00438' + $h2), ('00DD8' + $h1))
                    [void]$out.Add("pl1=$pl1"); [void]$out.Add("pl2=$pl2")
                }
                default { throw 'Unknown command' }
            }
            Set-Content -LiteralPath $result -Value (@('0') + $out)
        } catch {
            Set-Content -LiteralPath $result -Value @('1', $_.Exception.Message)
        }
        """;

    private static string TaskXml()
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        string script = System.Security.SecurityElement.Escape(ScriptPath);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>SquareDeck: changes handheld settings that need administrator rights (power modes, fans, charge limit, Intel power limits).</Description></RegistrationInfo>
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
