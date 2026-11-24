using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace SquareDeck.Services;

/// <summary>
/// "Collect a report": SquareDeck's logs, the version, the device, the device health checks and the settings in one
/// zip, ready to attach to a bug report. Keys, tokens and passwords are blanked out of the settings first.
/// </summary>
public static class Diagnostics
{
    /// <summary>Setting names whose values never leave the PC (API keys, tokens, passwords).</summary>
    private static bool Secret(string name) =>
        name.Contains("Key", StringComparison.OrdinalIgnoreCase) || name.Contains("Token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Secret", StringComparison.OrdinalIgnoreCase) || name.Contains("Password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ClientId", StringComparison.OrdinalIgnoreCase) || name.Equals("Avatar", StringComparison.OrdinalIgnoreCase);

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject o)
        {
            foreach (var name in o.Select(p => p.Key).ToList())
            {
                if (Secret(name) && o[name] is JsonValue v && v.TryGetValue<string>(out var text) && text.Length > 0) o[name] = "(removed)";
                else Redact(o[name]);
            }
        }
        else if (node is JsonArray a) foreach (var item in a) Redact(item);
    }

    public static string DownloadsFolder
    {
        get
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }
    }

    /// <summary>The device, version and health in plain text (also what the report's summary.txt holds).</summary>
    public static string Summary(Settings s, List<DeviceHealth.Check> checks)
    {
        var b = new StringBuilder();
        b.AppendLine("SquareDeck " + Updater.Current);
        b.AppendLine("Report made " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        b.AppendLine("Windows " + Environment.OSVersion.Version + (Environment.Is64BitOperatingSystem ? " (64-bit)" : ""));
        b.AppendLine("Device: " + Handheld.DisplayName + (AllyButtons.Model.Length > 0 ? " (" + AllyButtons.Model + ")" : ""));
        b.AppendLine("Processor: " + TdpControl.CpuName);
        if (Battery.GetPercent() is int battery) b.AppendLine($"Battery: {battery}%" + (Battery.IsPluggedIn() ? ", plugged in" : ", on battery"));
        b.AppendLine("TDP: " + (TdpControl.Ready ? $"{TdpControl.EffectiveWatts} W via {TdpControl.MethodName(TdpControl.Method)}" : s.TdpEnabled ? "on, but can't be set" : "off"));
        b.AppendLine("Game Bar widget: " + (WidgetInstaller.InstalledVersion()?.ToString() ?? "not installed") + (WidgetInstaller.Find() is { } p ? $" (bundled {p.Version})" : ""));
        if (AllyControl.IsAlly)
        {
            b.AppendLine("Ally light controller: " + (AllyHid.HasLights ? "found" : "not found") + ", controller settings: " + (AllyHid.HasController ? "found" : "not found"));
            b.AppendLine("ASUS Optimization: " + (!AsusOptimization.Installed ? "not installed" : AsusOptimization.Enabled ? "on" : "off"));
            b.AppendLine("ASUS firmware driver (ATKACPI): " + (AsusAcpi.Available ? "available" : "not available"));
            b.AppendLine("Windows Dynamic Lighting: " + (AllyControl.DynamicLightingOn ? "on" : "off"));
        }
        b.AppendLine();
        b.AppendLine("Who's in control:");
        foreach (var o in DeviceHealth.Owners(s)) b.AppendLine($"  {o.Area}: {o.Who} ({o.Detail})");
        b.AppendLine();
        b.AppendLine("Device health:");
        if (checks.Count == 0) b.AppendLine("  Nothing to report");
        foreach (var c in checks) b.AppendLine($"  [{c.Level}] {c.Area}: {c.Title}. {c.Detail}");
        return b.ToString();
    }

    /// <summary>Makes the zip in Downloads and returns its path (or throws with the reason).</summary>
    public static async Task<string> CollectAsync(Settings s)
    {
        var checks = await Task.Run(() => DeviceHealth.Run(s));
        string summary = Summary(s, checks);
        return await Task.Run(() =>
        {
            string path = Path.Combine(DownloadsFolder, $"SquareDeck report {DateTime.Now:yyyy-MM-dd HHmm}.zip");
            if (File.Exists(path)) File.Delete(path);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            void Text(string name, string text)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                w.Write(text);
            }
            Text("summary.txt", summary);

            // The logs, as they are (the newest part of a very large one).
            foreach (var log in Directory.EnumerateFiles(Settings.DataDir, "*.log"))
            {
                try
                {
                    using var read = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    const long Max = 2 * 1024 * 1024;
                    if (read.Length > Max) read.Seek(-Max, SeekOrigin.End);
                    using var reader = new StreamReader(read);
                    Text("logs/" + Path.GetFileName(log), reader.ReadToEnd());
                }
                catch (Exception) { }
            }

            // The settings, without keys, tokens or passwords.
            try
            {
                string settingsFile = Path.Combine(Settings.DataDir, "settings.json");
                if (File.Exists(settingsFile))
                {
                    var node = JsonNode.Parse(File.ReadAllText(settingsFile));
                    Redact(node);
                    Text("settings.json", node?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) ?? "");
                }
            }
            catch (Exception ex) { Text("settings-error.txt", ex.Message); }
            return path;
        });
    }
}
