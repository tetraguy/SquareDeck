using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SquareDeck.Services;

/// <summary>An FTP server saved in the File explorer. The password is kept in Windows' Credential Manager, not here.</summary>
public sealed class FtpServer
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 21;
    public string User { get; set; } = "";
    public bool Secure { get; set; }          // FTPS (explicit TLS)
    public string Folder { get; set; } = "/";  // where it opens

    public string Root => $"ftp://{Host}:{Port}/";
    public string StartUrl => Root + string.Join("/", Folder.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString)) +
                              (Folder.Trim('/').Length > 0 ? "/" : "");
    public string CredentialTarget => $"SquareDeck:ftp:{Host}:{Port}:{User}";
}

/// <summary>
/// Network places for the File explorer: mapping Windows network shares to drive letters (and disconnecting them),
/// and FTP servers (listing, downloading, uploading, renaming, deleting, new folders).
/// </summary>
public static class NetworkPlaces
{
    // ================================================================ network drives (Windows' own "Map network drive")

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class NetResource
    {
        public int Scope;
        public int Type = 1; // RESOURCETYPE_DISK
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(NetResource resource, string? password, string? user, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    private const int ConnectUpdateProfile = 0x1; // remembered after a restart

    /// <summary>Drive letters not in use, in order.</summary>
    public static List<string> FreeLetters()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        return Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c).Where(c => !used.Contains(c)).Reverse().Select(c => c + ":").ToList();
    }

    /// <summary>Network drives in use (connected or not).</summary>
    public static List<(string Letter, string Label)> NetworkDrives() =>
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Network)
            .Select(d => (d.Name.TrimEnd('\\'), Safe(() => d.IsReady && d.VolumeLabel.Length > 0 ? d.VolumeLabel : "Network drive")))
            .ToList();

    private static string Safe(Func<string> f) { try { return f(); } catch { return "Network drive"; } }

    /// <summary>Maps a share (\\server\share) to a drive letter; user and password are optional. Throws with Windows' reason.</summary>
    public static void MapDrive(string remote, string letter, string? user, string? password, bool remember)
    {
        var resource = new NetResource { LocalName = letter, RemoteName = remote.TrimEnd('\\') };
        int result = WNetAddConnection2(resource, string.IsNullOrEmpty(password) ? null : password, string.IsNullOrEmpty(user) ? null : user,
            remember ? ConnectUpdateProfile : 0);
        if (result != 0) throw new Win32Exception(result);
    }

    public static void Disconnect(string letter)
    {
        int result = WNetCancelConnection2(letter, ConnectUpdateProfile, true);
        if (result != 0) throw new Win32Exception(result);
    }

    // ================================================================ passwords (Windows Credential Manager)

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public static void SavePassword(string target, string user, string password)
    {
        var bytes = Encoding.Unicode.GetBytes(password);
        var blob = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new Credential { Type = 1, TargetName = target, UserName = user, CredentialBlob = blob, CredentialBlobSize = bytes.Length, Persist = 2, Comment = "SquareDeck File explorer" };
            CredWrite(ref cred, 0);
        }
        finally { Marshal.FreeHGlobal(blob); }
    }

    public static string? ReadPassword(string target)
    {
        if (!CredRead(target, 1, 0, out var ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            return cred.CredentialBlobSize > 0 ? Marshal.PtrToStringUni(cred.CredentialBlob, cred.CredentialBlobSize / 2) : "";
        }
        finally { CredFree(ptr); }
    }

    public static void ForgetPassword(string target) => CredDelete(target, 1, 0);

    // ================================================================ FTP

    public sealed record FtpItem(string Name, bool IsFolder, long Size, DateTime Modified);

    public static bool IsFtp(string? path) => path != null && path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase);

    /// <summary>The saved server an address belongs to (for its user, password and security).</summary>
    public static FtpServer? ServerFor(string url, IEnumerable<FtpServer> servers)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return servers.FirstOrDefault(s => s.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase) && s.Port == uri.Port);
    }

    /// <summary>The folder above an FTP address, or null at the server's top.</summary>
    public static string? FtpParent(string url)
    {
        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;
        return $"ftp://{uri.Host}:{uri.Port}/" + string.Join("/", segments.Take(segments.Length - 1)) + (segments.Length > 1 ? "/" : "");
    }

    /// <summary>The folders from the server's top down to this address: (name, address).</summary>
    public static List<(string Name, string Url)> FtpCrumbs(string url, string serverName)
    {
        var uri = new Uri(url);
        string root = $"ftp://{uri.Host}:{uri.Port}/";
        var list = new List<(string, string)> { (serverName, root) };
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
            list.Add((Uri.UnescapeDataString(segments[i]), root + string.Join("/", segments.Take(i + 1)) + "/"));
        return list;
    }

    public static string Child(string folderUrl, string name, bool folder) =>
        (folderUrl.EndsWith('/') ? folderUrl : folderUrl + "/") + Uri.EscapeDataString(name) + (folder ? "/" : "");

    public static string NameOf(string url) => Uri.UnescapeDataString(url.TrimEnd('/').Split('/').Last());

#pragma warning disable SYSLIB0014 // FtpWebRequest is old, but it's the FTP client .NET ships with, and it works.
    private static FtpWebRequest Request(string url, string method, FtpServer? server)
    {
        var request = (FtpWebRequest)WebRequest.Create(url);
        request.Method = method;
        request.UsePassive = true;
        request.UseBinary = true;
        request.KeepAlive = false;
        request.Timeout = 20000;
        request.ReadWriteTimeout = 60000;
        request.EnableSsl = server?.Secure == true;
        string user = server?.User is { Length: > 0 } u ? u : "anonymous";
        string password = server != null && server.User.Length > 0 ? ReadPassword(server.CredentialTarget) ?? "" : "squaredeck@";
        request.Credentials = new NetworkCredential(user, password);
        return request;
    }
#pragma warning restore SYSLIB0014

    private static readonly Regex UnixLine = new(@"^([dl\-][rwxsStT\-]{9})\S*\s+\d+\s+\S+\s+\S+\s+(\d+)\s+(\w{3}\s+\d{1,2}\s+(?:\d{1,2}:\d{2}|\d{4}))\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex DosLine = new(@"^(\d{2}-\d{2}-\d{2,4})\s+(\d{1,2}:\d{2}\s*[AP]M)\s+(<DIR>|\d+)\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>What's in an FTP folder.</summary>
    public static List<FtpItem> List(string url, FtpServer? server)
    {
        var items = new List<FtpItem>();
        using (var response = (FtpWebResponse)Request(url, WebRequestMethods.Ftp.ListDirectoryDetails, server).GetResponse())
        using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Trim().Length == 0 || line.StartsWith("total ", StringComparison.OrdinalIgnoreCase)) continue;
                var unix = UnixLine.Match(line);
                if (unix.Success)
                {
                    string name = unix.Groups[4].Value;
                    bool link = unix.Groups[1].Value[0] == 'l';
                    if (link && name.Contains(" -> ")) name = name[..name.IndexOf(" -> ", StringComparison.Ordinal)];
                    if (name is "." or "..") continue;
                    DateTime.TryParseExact(Regex.Replace(unix.Groups[3].Value, @"\s+", " "), new[] { "MMM d HH:mm", "MMM d yyyy", "MMM dd HH:mm", "MMM dd yyyy" },
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var when);
                    if (when > DateTime.Now.AddDays(2)) when = when.AddYears(-1); // "Mar 3 14:20" means the last March 3rd
                    items.Add(new FtpItem(name, unix.Groups[1].Value[0] == 'd' || link, long.Parse(unix.Groups[2].Value), when));
                    continue;
                }
                var dos = DosLine.Match(line);
                if (dos.Success)
                {
                    DateTime.TryParse(dos.Groups[1].Value + " " + dos.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var when);
                    bool dir = dos.Groups[3].Value.Equals("<DIR>", StringComparison.OrdinalIgnoreCase);
                    items.Add(new FtpItem(dos.Groups[4].Value, dir, dir ? 0 : long.Parse(dos.Groups[3].Value), when));
                    continue;
                }
                // A format this doesn't know: at least show the name.
                var last = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (last != null && last is not ("." or "..")) items.Add(new FtpItem(last, false, 0, DateTime.MinValue));
            }
        }
        return items;
    }

    /// <summary>Downloads a file, or a folder with everything in it, into a folder on this PC. Returns the new path.</summary>
    public static string Download(string url, string localFolder, FtpServer? server, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string name = NameOf(url);
        if (url.EndsWith('/'))
        {
            string folder = FileOps.UniquePath(localFolder, name);
            Directory.CreateDirectory(folder);
            foreach (var item in List(url, server)) Download(Child(url, item.Name, item.IsFolder), folder, server, ct);
            return folder;
        }
        string target = FileOps.UniquePath(localFolder, name);
        using var response = (FtpWebResponse)Request(url, WebRequestMethods.Ftp.DownloadFile, server).GetResponse();
        using var input = response.GetResponseStream();
        using var output = File.Create(target);
        input.CopyTo(output);
        return target;
    }

    /// <summary>Uploads a file or folder from this PC into an FTP folder.</summary>
    public static void Upload(string localPath, string folderUrl, FtpServer? server, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string name = Path.GetFileName(localPath.TrimEnd('\\'));
        if (Directory.Exists(localPath))
        {
            string dir = Child(folderUrl, name, folder: true);
            try { MakeFolder(dir, server); } catch (WebException) { } // already there
            foreach (var entry in Directory.EnumerateFileSystemEntries(localPath)) Upload(entry, dir, server, ct);
            return;
        }
        var request = Request(Child(folderUrl, name, folder: false), WebRequestMethods.Ftp.UploadFile, server);
        using (var input = File.OpenRead(localPath))
        using (var output = request.GetRequestStream())
            input.CopyTo(output);
        using var _ = request.GetResponse();
    }

    public static void MakeFolder(string url, FtpServer? server)
    {
        using var _ = Request(url.TrimEnd('/'), WebRequestMethods.Ftp.MakeDirectory, server).GetResponse();
    }

    /// <summary>Deletes a file, or a folder and everything in it. There's no recycle bin on a server.</summary>
    public static void Delete(string url, FtpServer? server)
    {
        if (url.EndsWith('/'))
        {
            foreach (var item in List(url, server)) Delete(Child(url, item.Name, item.IsFolder), server);
            using var r = Request(url.TrimEnd('/'), WebRequestMethods.Ftp.RemoveDirectory, server).GetResponse();
            return;
        }
        using var response = Request(url, WebRequestMethods.Ftp.DeleteFile, server).GetResponse();
    }

    public static void Rename(string url, string newName, FtpServer? server)
    {
        var request = Request(url.TrimEnd('/'), WebRequestMethods.Ftp.Rename, server);
        request.RenameTo = newName;
        using var _ = request.GetResponse();
    }

    /// <summary>Checks a server can be reached and signed in to (lists its top folder).</summary>
    public static void Test(FtpServer server) => List(server.StartUrl, server);

    /// <summary>Windows' or the server's reason, in a sentence.</summary>
    public static string Reason(Exception ex) => ex switch
    {
        WebException { Response: FtpWebResponse r } => r.StatusCode switch
        {
            FtpStatusCode.NotLoggedIn => "The user name or password wasn't accepted.",
            FtpStatusCode.ActionNotTakenFileUnavailable => "That file or folder isn't there, or you don't have permission.",
            FtpStatusCode.ActionNotTakenFilenameNotAllowed => "The server doesn't allow that name.",
            _ => r.StatusDescription?.Trim() ?? ex.Message
        },
        WebException { Status: WebExceptionStatus.NameResolutionFailure } => "The server's name couldn't be found. Check the address.",
        WebException { Status: WebExceptionStatus.ConnectFailure } => "The server didn't answer. Check the address and port, and that it's on.",
        WebException { Status: WebExceptionStatus.Timeout } => "The server took too long to answer.",
        _ => ex.Message
    };
}
