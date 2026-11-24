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

    /// <summary>Installs (or updates) the widget from the Widget folder.</summary>
    public static async Task<(bool Ok, string Message)> InstallAsync(Package? package = null)
    {
        if (Busy) return (false, "The widget is already being installed.");
        package ??= Find();
        if (package == null) return (false, "There's no widget package in " + Folder + ".");
        Busy = true;
        try
        {
            // The package's certificate, trusted by the PC (one administrator prompt, the first time).
            if (package.Certificate != null && !Trusted(package.Certificate))
            {
                await Maintenance.RunElevatedAsync($"certutil -addstore TrustedPeople \"{package.Certificate}\"").ConfigureAwait(false);
                if (!Trusted(package.Certificate))
                    return (false, "Windows has to trust the widget's certificate first, and the administrator prompt was declined. Try again from Settings > Controller > Xbox Game Bar widget.");
            }

            var dependencies = package.Dependencies.Select(d => new Uri(d)).ToList();
            var result = await new PackageManager().AddPackageAsync(new Uri(package.Path), dependencies,
                DeploymentOptions.ForceApplicationShutdown | DeploymentOptions.ForceUpdateFromAnyVersion);
            bool installed = InstalledVersion() is { } now && now >= package.Version;
            if (!installed)
                return (false, "Windows couldn't install the widget: " + (string.IsNullOrWhiteSpace(result.ErrorText) ? result.ExtendedErrorCode?.Message ?? "no reason given" : result.ErrorText.Trim()));
            return (true, "Installed version " + package.Version + ".");
        }
        catch (Exception ex)
        {
            CrashLog.Write("Widget install", ex);
            return (false, "Windows couldn't install the widget: " + ex.Message);
        }
        finally { Busy = false; }
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
