using System.IO;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// SquareDeck as the home app of Xbox mode (Windows' gaming full screen experience), without AnyFSE.
///
/// Windows only offers packaged apps that declare the windows.gamingApp extension and the gamingHome capability
/// (see HomeApp\AppxManifest.xml, copied next to the program as AppxManifest.xml). SquareDeck registers its own
/// folder as that package with Add-AppxPackage -Register, so Windows starts this same SquareDeck.exe. The capability
/// belongs to Microsoft, so outside the Store Windows only accepts it with Developer Mode on (the same as AnyFSE and
/// the other home apps that aren't Microsoft's). Nothing is copied or installed elsewhere, and turning it off removes
/// the registration and gives Windows back the home app it had.
/// </summary>
public static class HomeApp
{
    public const string PackageName = "SquareDeck.HomeApp";
    public const string AppId = "App";
    public const string XboxApp = "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";
    public const string WindowsSettingsPage = "ms-settings:gaming-fullscreen";

    private const string GamingKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration";

    public static string Folder => AppContext.BaseDirectory.TrimEnd('\\');
    public static string ManifestPath => Path.Combine(Folder, "AppxManifest.xml");

    /// <summary>The package files were built next to the program.</summary>
    public static bool FilesPresent =>
        File.Exists(ManifestPath) && File.Exists(Path.Combine(Folder, "CustomCapability.SCCD")) &&
        File.Exists(Path.Combine(Folder, "SquareDeck.exe"));

    /// <summary>Xbox mode exists from Windows 11 24H2 (build 26100).</summary>
    public static bool WindowsSupports => Environment.OSVersion.Version.Build >= 26100;

    /// <summary>Settings > System > For developers > Developer Mode.</summary>
    public static bool DeveloperMode
    {
        get
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
                return key?.GetValue("AllowDevelopmentWithoutDevLicense") is int v && v != 0;
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>The home app Windows starts in Xbox mode ("" or null: none).</summary>
    public static string? CurrentHomeApp
    {
        get
        {
            try { return Registry.GetValue(GamingKey, "GamingHomeApp", null) as string; }
            catch (Exception) { return null; }
        }
    }

    /// <summary>True when Windows' home app is SquareDeck's package.</summary>
    public static bool IsHomeApp => CurrentHomeApp?.StartsWith(PackageName + "_", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Windows' own switch: open Xbox mode when you sign in.</summary>
    public static bool StartsAtSignIn
    {
        get => AnyFse.StartsAtSignIn;
        set => AnyFse.StartsAtSignIn = value;
    }

    public sealed record Registration(string FamilyName, string Location)
    {
        public string Aumid => FamilyName + "!" + AppId;

        /// <summary>The registration points at this copy of SquareDeck (not one in a folder that has since moved).</summary>
        public bool IsThisCopy => string.Equals(Location.TrimEnd('\\'), Folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>SquareDeck's package as Windows has it registered, or null.</summary>
    public static async Task<Registration?> FindAsync()
    {
        var output = await ProcessUtil.RunPowerShellAsync(
            $"$p = Get-AppxPackage -Name '{PackageName}' | Select-Object -First 1; " +
            "if ($p) { 'FAMILY=' + $p.PackageFamilyName; 'LOCATION=' + $p.InstallLocation }").ConfigureAwait(false);
        string? family = null, location = null;
        foreach (var line in output.Split('\n').Select(l => l.Trim()))
        {
            if (line.StartsWith("FAMILY=")) family = line[7..];
            else if (line.StartsWith("LOCATION=")) location = line[9..];
        }
        return family is { Length: > 0 } ? new Registration(family, location ?? "") : null;
    }

    /// <summary>
    /// Registers this folder as SquareDeck's package (replacing a registration from another folder) and returns it.
    /// Throws with Windows' own explanation if it refuses.
    /// </summary>
    public static async Task<Registration> RegisterAsync(bool mayAskForAdmin = true)
    {
        if (!FilesPresent)
            throw new InvalidOperationException("The home app files (AppxManifest.xml and CustomCapability.SCCD) aren't next to SquareDeck.exe. " +
                                                "Rebuild SquareDeck so they're copied there.");
        if (await FindAsync().ConfigureAwait(false) is { IsThisCopy: false })
            await UnregisterAsync().ConfigureAwait(false); // SquareDeck has moved: the old folder's registration goes

        string script = "try { Add-AppxPackage -Register '" + Ps(ManifestPath) + "' -ForceUpdateFromAnyVersion -ErrorAction Stop; 'REGISTERED' } " +
                        "catch { 'ERROR=' + $_.Exception.Message }";
        var output = await ProcessUtil.RunPowerShellAsync(script).ConfigureAwait(false);
        // "Access is denied": some devices only let Windows' app installer read the folder with administrator rights.
        // Windows asks for permission, and the package is still registered for you (the same account).
        if (!output.Contains("REGISTERED") && IsAccessDenied(output) && mayAskForAdmin)
            output = await RunElevatedAsync(script).ConfigureAwait(false);
        if (!output.Contains("REGISTERED"))
        {
            var error = output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("ERROR="))?[6..];
            throw new InvalidOperationException(Explain(error ?? output.Trim()));
        }
        return await FindAsync().ConfigureAwait(false)
               ?? throw new InvalidOperationException("Windows registered the package but doesn't list it. Try again after restarting.");
    }

    /// <summary>Removes SquareDeck's package registration (the program and its settings stay as they are).</summary>
    public static Task UnregisterAsync() =>
        ProcessUtil.RunPowerShellAsync($"Get-AppxPackage -Name '{PackageName}' | Remove-AppxPackage -ErrorAction SilentlyContinue");

    /// <summary>
    /// Makes SquareDeck Windows' Xbox mode home app, remembering the one it replaces. Windows protects this setting on
    /// some devices: then it's tried again with administrator rights (Windows asks). False if it still can't be
    /// changed, and SquareDeck has to be picked in Windows' own Xbox mode settings, where it's now listed.
    /// </summary>
    public static async Task<bool> MakeHomeAppAsync(Registration package, Settings settings)
    {
        var current = CurrentHomeApp;
        if (!string.IsNullOrEmpty(current) && !current.StartsWith(PackageName + "_", StringComparison.OrdinalIgnoreCase))
            settings.PreviousHomeApp = current;
        if (SetHomeAppValue(package.Aumid)) return true;
        await RunElevatedAsync(SetValueScript(package.Aumid)).ConfigureAwait(false);
        return IsHomeApp;
    }

    private static bool SetHomeAppValue(string value)
    {
        try
        {
            Registry.SetValue(GamingKey, "GamingHomeApp", value, RegistryValueKind.String);
            return string.Equals(CurrentHomeApp, value, StringComparison.OrdinalIgnoreCase);
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Security.SecurityException) { return false; }
        catch (IOException) { return false; }
    }

    private static string SetValueScript(string value) =>
        "try { $k = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\GamingConfiguration'; " +
        "if (-not (Test-Path $k)) { New-Item -Path $k -Force | Out-Null }; " +
        "Set-ItemProperty -Path $k -Name 'GamingHomeApp' -Value '" + Ps(value) + "' -Type String -ErrorAction Stop; 'DONE' } " +
        "catch { 'ERROR=' + $_.Exception.Message }";

    private static bool IsAccessDenied(string text) =>
        text.Contains("0x80070005") || text.Contains("access is denied", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("access denied", StringComparison.OrdinalIgnoreCase);

    private static string Ps(string text) => text.Replace("'", "''");

    /// <summary>
    /// Runs a PowerShell script with administrator rights (Windows asks first) and returns what it printed.
    /// "ERROR=The administrator prompt was declined." when the answer is no.
    /// </summary>
    private static async Task<string> RunElevatedAsync(string script)
    {
        Directory.CreateDirectory(Settings.DataDir);
        string id = Guid.NewGuid().ToString("N")[..8];
        string scriptFile = Path.Combine(Settings.DataDir, $"homeapp-{id}.ps1"), resultFile = Path.Combine(Settings.DataDir, $"homeapp-{id}.txt");
        await File.WriteAllTextAsync(scriptFile,
            "$ProgressPreference='SilentlyContinue'\r\n$out = & { " + script + " } 2>&1 | Out-String\r\n" +
            "Set-Content -LiteralPath '" + Ps(resultFile) + "' -Value $out -Encoding UTF8\r\n").ConfigureAwait(false);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptFile}\"")
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return "ERROR=PowerShell couldn't be started with administrator rights.";
            await process.WaitForExitAsync().ConfigureAwait(false);
            return File.Exists(resultFile) ? await File.ReadAllTextAsync(resultFile).ConfigureAwait(false) : "ERROR=No answer came back.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "ERROR=The administrator prompt was declined.";
        }
        finally
        {
            try { File.Delete(scriptFile); } catch (Exception) { }
            try { File.Delete(resultFile); } catch (Exception) { }
        }
    }

    /// <summary>Gives Windows back the home app it had before (the Xbox app if there was none).</summary>
    public static async Task RestoreHomeAppAsync(Settings settings)
    {
        if (!IsHomeApp) return;
        var previous = string.IsNullOrEmpty(settings.PreviousHomeApp) ? XboxApp : settings.PreviousHomeApp;
        if (!SetHomeAppValue(previous)) await RunElevatedAsync(SetValueScript(previous)).ConfigureAwait(false);
    }

    /// <summary>
    /// Run at startup when the home app is on: if SquareDeck has been moved or updated in another folder, the package
    /// is registered again from here, so Xbox mode keeps starting the copy you use.
    /// </summary>
    public static async Task KeepRegisteredAsync(Settings settings)
    {
        if (!settings.HomeAppOn || !FilesPresent || !WindowsSupports) return;
        try
        {
            var found = await FindAsync().ConfigureAwait(false);
            if (found is { IsThisCopy: true } && IsHomeApp) return;
            if (!DeveloperMode) return; // can't register again without it; the settings page explains
            var package = found is { IsThisCopy: true } ? found : await RegisterAsync(mayAskForAdmin: false).ConfigureAwait(false);
            // No administrator prompt at startup: only the quiet way. The settings page can do the rest.
            if (!IsHomeApp)
            {
                var current = CurrentHomeApp;
                if (!string.IsNullOrEmpty(current) && !current.StartsWith(PackageName + "_", StringComparison.OrdinalIgnoreCase))
                    settings.PreviousHomeApp = current;
                SetHomeAppValue(package.Aumid);
            }
        }
        catch (Exception) { }
    }

    /// <summary>Windows' deployment errors, in plain words where they're common.</summary>
    private static string Explain(string error)
    {
        if (error.Contains("prompt was declined"))
            return "Windows asked for administrator permission and it was declined. SquareDeck needs it once on this device to register.";
        if (IsAccessDenied(error))
            return "Windows refused access, even with administrator rights. Check that SquareDeck's folder isn't read-only or on a removable " +
                   "or network drive, and that you're signed in with the account that will use Xbox mode.\n\n" + error;
        if (error.Contains("0x80073CFF") || error.Contains("developer", StringComparison.OrdinalIgnoreCase))
            return "Windows needs Developer Mode on to accept SquareDeck as a home app. Turn it on in Settings > System > For developers, then try again.\n\n" + error;
        if (error.Contains("0x80073D1F") || error.Contains("0x80073CF6") || error.Contains("NTFS", StringComparison.OrdinalIgnoreCase))
            return "Windows can only register apps on an NTFS drive. Move SquareDeck to a folder on such a drive (for example under your user folder), then try again.\n\n" + error;
        if (error.Contains("0x8007007A"))
            return "Windows couldn't read CustomCapability.SCCD. Rebuild SquareDeck so a fresh copy is placed next to the program.\n\n" + error;
        return error.Length > 0 ? error : "Windows didn't say why.";
    }
}
