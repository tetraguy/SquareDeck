using System.Text.Json;

namespace SquareDeck.Services;

public sealed record InstalledApp(string Name, string AppId);

public sealed record StoreResult(string Name, string Id);

/// <summary>Finds apps installed on this PC and installs new ones from the Microsoft Store (via winget).</summary>
public static class InstalledApps
{
    private static readonly string[] SkippedExtensions = { ".url", ".chm", ".txt", ".html", ".htm", ".pdf", ".rtf" };

    /// <summary>Everything in the Start menu: desktop programs and Microsoft Store apps.</summary>
    public static async Task<List<InstalledApp>> ScanAsync(CancellationToken ct = default)
    {
        const string script =
            "[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress";
        var (_, output) = await ProcessUtil.RunAsync("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"", ct).ConfigureAwait(false);

        var apps = new List<InstalledApp>();
        int start = output.IndexOfAny(new[] { '[', '{' });
        if (start < 0) return apps;

        using var doc = JsonDocument.Parse(output[start..]);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : new List<JsonElement> { doc.RootElement };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var name = item.TryGetProperty("Name", out var n) ? n.GetString() : null;
            var id = item.TryGetProperty("AppID", out var a) ? a.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
            if (name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
            if (id.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            if (SkippedExtensions.Any(e => id.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
            if (seen.Add(id)) apps.Add(new InstalledApp(name, id));
        }
        return apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static bool Launch(string appId)
    {
        // A program added by hand ("Choose a program…"): started directly, from its own folder.
        if (System.IO.Path.IsPathRooted(appId))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(appId) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(appId) ?? "" })?.Dispose();
                return true;
            }
            catch (Exception) { return false; }
        }
        return ProcessUtil.Open("explorer.exe", @"shell:AppsFolder\" + appId);
    }

    /// <summary>Searches the Microsoft Store catalogue through winget.</summary>
    public static async Task<List<StoreResult>> SearchStoreAsync(string query, CancellationToken ct = default)
    {
        var safe = query.Replace("\"", "");
        var (_, output) = await ProcessUtil.RunAsync("winget",
            $"search \"{safe}\" --source msstore --count 25 --accept-source-agreements --disable-interactivity", ct)
            .ConfigureAwait(false);
        return ParseWingetTable(output);
    }

    /// <summary>winget prints a fixed-width table: use the header's column positions to cut each row.</summary>
    internal static List<StoreResult> ParseWingetTable(string output)
    {
        var results = new List<StoreResult>();
        // Progress spinners are written with carriage returns; keep only what ends up on each line.
        var lines = output.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Select(l => l.Contains('\r') ? l[(l.LastIndexOf('\r') + 1)..] : l)
            .ToList();

        int headerIndex = lines.FindIndex(l => l.TrimStart().StartsWith("Name", StringComparison.Ordinal) && l.Contains(" Id"));
        if (headerIndex < 0) return results;

        var header = lines[headerIndex];
        int idCol = header.IndexOf(" Id", StringComparison.Ordinal) + 1;
        int versionCol = header.IndexOf("Version", StringComparison.Ordinal);

        foreach (var line in lines.Skip(headerIndex + 1))
        {
            if (line.Length <= idCol || line.StartsWith("---", StringComparison.Ordinal)) continue;
            var name = line[..idCol].Trim();
            var rest = versionCol > idCol && line.Length > versionCol ? line[idCol..versionCol] : line[idCol..];
            var id = rest.Trim().Split(' ')[0];
            if (name.Length > 0 && id.Length > 0) results.Add(new StoreResult(name, id));
        }
        return results;
    }

    public static async Task<bool> InstallFromStoreAsync(string id, CancellationToken ct = default)
    {
        var (exit, _) = await ProcessUtil.RunAsync("winget",
            $"install --id \"{id}\" --source msstore --accept-package-agreements --accept-source-agreements --disable-interactivity --silent",
            ct).ConfigureAwait(false);
        return exit == 0;
    }
}
