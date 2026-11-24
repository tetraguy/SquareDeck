using System.IO;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Windows.Management.Deployment;

namespace SquareDeck.Services;

/// <summary>
/// Installs SquareDeck's Xbox Game Bar widget from the Widget folder next to SquareDeck (the installer puts the widget
/// package there: the .msix, its .cer and the Dependencies folder that Visual Studio's "Create App Packages" makes).
/// At start, if the widget isn't installed, or an older one is, SquareDeck installs the one in the folder.
///
/// The package is installed for this Windows user with Windows' own package manager (no administrator needed). Its
/// certificate has to be trusted by the PC first (Local Machine > Trusted People), which takes one administrator
/// prompt the first time; a package signed by a certificate Windows already trusts needs none.
/// </summary>
public static class WidgetInstaller
{
    public const string PackageName = "SquareDeck.GameBarWidget";

    /// <summary>The Widget folder where SquareDeck is installed.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Widget");

    /// <summary>A widget package found in the Widget folder.</summary>
    public sealed record Package(string Path, Version Version, string Architecture, string? Certificate, IReadOnlyList<string> Dependencies);

    public static bool Busy { get; private set; }

    /// <summary>
    /// The installed widget packages for this Windows user, by name only: the publisher is whatever certificate the
    /// package was signed with (Package.appxmanifest's Publisher), so it isn't fixed here.
    /// </summary>
    private static IEnumerable<Windows.ApplicationModel.Package> Installed() =>
        new PackageManager().FindPackagesForUser(string.Empty).Where(p => p.Id.Name == PackageName);

    /// <summary>The installed widget's version for this Windows user, or null when it isn't installed.</summary>
    public static Version? InstalledVersion()
    {
        try
        {
            Version? best = null;
            foreach (var p in Installed())
            {
                var v = p.Id.Version;
                var version = new Version(v.Major, v.Minor, v.Build, v.Revision);
                if (best == null || version > best) best = version;
            }
            return best;
        }
        catch (Exception) { return null; }
    }

    /// <summary>The installed widget's package family name (for opening it in Game Bar), or null when it isn't installed.</summary>
    public static string? FamilyName()
    {
        try { return Installed().FirstOrDefault()?.Id.FamilyName; }
        catch (Exception) { return null; }
    }

    /// <summary>The newest widget package in the Widget folder (any depth), or null when there's none.</summary>
    public static Package? Find()
    {
        try
        {
            if (!Directory.Exists(Folder)) return null;
            Package? best = null;
            foreach (var file in Directory.EnumerateFiles(Folder, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".msix" or ".msixbundle" or ".appx" or ".appxbundle")) continue;
                if (InDependencies(file)) continue;
                if (ReadIdentity(file) is not { } id || id.Name != PackageName) continue;
                if (best != null && id.Version <= best.Version) continue;
                string dir = Path.GetDirectoryName(file)!;
                best = new Package(file, id.Version, id.Architecture, FindCertificate(dir), FindDependencies(dir, id.Architecture));
            }
            return best;
        }
        catch (Exception ex) { CrashLog.Write("Widget package", ex); return null; }
    }

    /// <summary>The bundled package when the widget isn't installed at all (start-up asks first), else null.</summary>
    public static Package? Missing() => InstalledVersion() == null ? Find() : null;

    /// <summary>One step of an install, for the install screen: which step, how it's going, a line about it, and how far (0–100).</summary>
    public sealed record Step(string Id, string State, string Detail, double? Percent = null);

    /// <summary>The steps, in order, with their names on the install screen.</summary>
    public static readonly (string Id, string Name)[] Steps =
    {
        ("check", "Checking the package"), ("trust", "Trusting its certificate"), ("close", "Closing Game Bar"),
        ("install", "Installing"), ("verify", "Making sure it's there"),
    };

    private static string LogFile => Path.Combine(Settings.DataDir, "widget-install.log");

    /// <summary>Each step goes into widget-install.log (the last 200 lines), so a stuck install shows where it stopped.</summary>
    private static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            var lines = File.Exists(LogFile) ? File.ReadAllLines(LogFile).TakeLast(199).ToList() : new List<string>();
            lines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}");
            File.WriteAllLines(LogFile, lines);
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Installs (or updates) the widget from the Widget folder, step by step: check the package, trust its certificate
    /// (one administrator prompt the first time, at most two minutes), close Game Bar so nothing holds the old widget
    /// open (Windows otherwise waits for it, which looked like a frozen install), install with Windows' own progress
    /// (at most four minutes), then check it's there. Cancel stops at any step. Every step is logged.
    /// </summary>
    public static async Task<(bool Ok, string Message)> InstallAsync(Package? package = null, IProgress<Step>? progress = null, CancellationToken cancel = default)
    {
        if (Busy) return (false, "The widget is already being installed.");
        package ??= Find();
        if (package == null) return (false, "There's no widget package in " + Folder + ".");
        Busy = true;
        string current = "check";
        void Report(string id, string state, string detail, double? percent = null)
        {
            current = id;
            if (state != "progress") Log($"{id}: {state}  {detail}");
            progress?.Report(new Step(id, state, detail, percent));
        }
        try
        {
            // 1. The package.
            Report("check", "active", "Reading " + Path.GetFileName(package.Path));
            var installed = await Task.Run(InstalledVersion, cancel);
            Report("check", "done", installed == null ? $"Version {package.Version}, new on this PC" : $"Version {installed} → {package.Version}");

            // 2. Its certificate, trusted by the PC (one administrator prompt, the first time).
            if (package.Certificate == null) Report("trust", "skipped", "The package carries no certificate file");
            else if (await Task.Run(() => Trusted(package.Certificate), cancel)) Report("trust", "skipped", "Windows already trusts it");
            else
            {
                Report("trust", "active", "Windows is asking for an administrator's permission. If you don't see it, press the Windows key: it may be waiting in the taskbar.");
                var trust = Maintenance.RunElevatedAsync($"certutil -addstore TrustedPeople \"{package.Certificate}\"");
                var waited = await Task.WhenAny(trust, Task.Delay(TimeSpan.FromMinutes(2), cancel));
                cancel.ThrowIfCancellationRequested();
                if (waited != trust)
                {
                    Report("trust", "failed", "The administrator prompt wasn't answered in two minutes");
                    return (false, "Windows' administrator prompt wasn't answered. It may be waiting in the taskbar: answer it there, then try again.");
                }
                if (!Trusted(package.Certificate))
                {
                    Report("trust", "failed", "The administrator prompt was declined");
                    return (false, "Windows has to trust the widget's certificate first, and the administrator prompt was declined. Try again and choose Yes.");
                }
                Report("trust", "done", "Trusted on this PC");
            }

            // 3. Game Bar (and the old widget inside it) closed, so Windows isn't left waiting for them to let go.
            Report("close", "active", "Closing Game Bar and the old widget");
            int closed = await Task.Run(CloseGameBar, cancel);
            Report("close", "done", closed > 0 ? "Closed. Game Bar opens again with Win + G or the Xbox button" : "Game Bar wasn't open");

            // 4. Installed by Windows' package manager, off the interface thread, with its progress and a time limit.
            Report("install", "active", "Windows is installing the widget", 0);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(TimeSpan.FromMinutes(4));
            var deploy = new Progress<DeploymentProgress>(p => Report("install", "progress", "Windows is installing the widget", Math.Clamp((double)p.percentage, 0, 100)));
            var dependencies = package.Dependencies.Select(d => new Uri(d)).ToList();
            DeploymentResult result;
            try
            {
                result = await Task.Run(async () => await new PackageManager().AddPackageAsync(new Uri(package.Path), dependencies,
                    DeploymentOptions.ForceApplicationShutdown | DeploymentOptions.ForceUpdateFromAnyVersion).AsTask(limit.Token, deploy), limit.Token);
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                Report("install", "failed", "Windows didn't finish in four minutes");
                return (false, "Windows didn't finish installing the widget in four minutes. Restarting the PC usually clears what it was waiting for; then try again.");
            }
            if (!string.IsNullOrWhiteSpace(result.ErrorText))
                Log("install: Windows said  " + result.ErrorText.Trim());
            Report("install", "done", "Windows finished");

            // 5. There, and at least this version.
            Report("verify", "active", "Looking for it");
            bool there = InstalledVersion() is { } now && now >= package.Version;
            if (!there)
            {
                string why = string.IsNullOrWhiteSpace(result.ErrorText) ? result.ExtendedErrorCode?.Message ?? "no reason given" : result.ErrorText.Trim();
                Report("verify", "failed", why);
                return (false, "Windows couldn't install the widget: " + why);
            }
            Report("verify", "done", "Version " + package.Version + " is installed");
            return (true, "Installed version " + package.Version + ".");
        }
        catch (OperationCanceledException)
        {
            Report(current, "failed", "Cancelled");
            return (false, "Cancelled. The widget can be installed any time from Settings > Controller and Game Bar widget > Install the widget.");
        }
        catch (Exception ex)
        {
            CrashLog.Write("Widget install", ex);
            Report(current, "failed", ex.Message);
            return (false, "Windows couldn't install the widget: " + ex.Message);
        }
        finally { Busy = false; }
    }

    /// <summary>Closes Game Bar and the widget's own process (Game Bar starts again by itself). How many were closed.</summary>
    private static int CloseGameBar()
    {
        int closed = 0;
        foreach (var name in new[] { "GameBar", "GameBarFTServer", "SquareDeck.Widget" })
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
            {
                try { p.Kill(); p.WaitForExit(3000); closed++; }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
        }
        if (closed > 0) Thread.Sleep(800); // a moment for Windows to see them gone
        return closed;
    }

    // ---------------------------------------------------------------- reading the folder

    private static bool InDependencies(string file) =>
        file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part.Equals("Dependencies", StringComparison.OrdinalIgnoreCase)
            || part.Equals("TelemetryDependencies", StringComparison.OrdinalIgnoreCase));

    /// <summary>The package's name, version and processor from its manifest.</summary>
    private static (string Name, Version Version, string Architecture)? ReadIdentity(string file)
    {
        try
        {
            using var zip = ZipFile.OpenRead(file);
            var entry = zip.GetEntry("AppxManifest.xml") ?? zip.GetEntry("AppxMetadata/AppxBundleManifest.xml");
            if (entry == null) return null;
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            var identity = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");
            if (identity == null) return null;
            string name = (string?)identity.Attribute("Name") ?? "";
            if (!Version.TryParse((string?)identity.Attribute("Version"), out var version)) return null;
            string arch = (string?)identity.Attribute("ProcessorArchitecture") ?? "neutral";
            return (name, version, arch.ToLowerInvariant());
        }
        catch (Exception) { return null; }
    }

    /// <summary>The .cer next to the package (as "Create App Packages" leaves it), else any in the Widget folder.</summary>
    private static string? FindCertificate(string dir)
    {
        string? near = Directory.EnumerateFiles(dir, "*.cer").FirstOrDefault();
        return near ?? Directory.EnumerateFiles(Folder, "*.cer", SearchOption.AllDirectories).FirstOrDefault();
    }

    /// <summary>The framework packages it needs (VCLibs, .NET Native) for its processor: Dependencies\x64 and so on.</summary>
    private static IReadOnlyList<string> FindDependencies(string dir, string architecture)
    {
        string arch = architecture == "neutral" ? (Environment.Is64BitOperatingSystem ? "x64" : "x86") : architecture;
        foreach (var root in new[] { Path.Combine(dir, "Dependencies"), Path.Combine(Folder, "Dependencies") })
        {
            string folder = Path.Combine(root, arch);
            if (!Directory.Exists(folder)) continue;
            return Directory.EnumerateFiles(folder).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".appx" or ".msix").ToList();
        }
        // Anywhere under the Widget folder, in a Dependencies\<processor> folder.
        return Directory.EnumerateDirectories(Folder, arch, SearchOption.AllDirectories)
            .Where(d => Path.GetFileName(Path.GetDirectoryName(d) ?? "").Equals("Dependencies", StringComparison.OrdinalIgnoreCase))
            .SelectMany(d => Directory.EnumerateFiles(d))
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".appx" or ".msix")
            .ToList();
    }

    /// <summary>Whether the PC trusts a certificate for packages: Local Machine's Trusted People or Trusted Root.</summary>
    private static bool Trusted(string cerPath)
    {
        try
        {
            using var cert = new X509Certificate2(cerPath);
            foreach (var name in new[] { StoreName.TrustedPeople, StoreName.Root })
            {
                using var store = new X509Store(name, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                if (store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false).Count > 0) return true;
            }
            return false;
        }
        catch (Exception) { return false; }
    }
}
