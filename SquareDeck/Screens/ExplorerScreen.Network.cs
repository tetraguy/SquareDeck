using System.IO;
using System.Windows;
using System.Windows.Controls;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The File explorer's network places: Windows network shares (map to a drive letter, disconnect, or open a
/// \\server\share address directly) and FTP servers (saved on This PC and in the sidebar, browsed like folders).
/// </summary>
public sealed partial class ExplorerScreen
{
    private FtpServer? Server(string url) => NetworkPlaces.ServerFor(url, _settings.FtpServers);

    private string ServerNameFor(string url) => Server(url)?.Name is { Length: > 0 } name ? name : new Uri(url).Host;

    private static List<Entry> ReadFtp(string folder, bool showHidden, IReadOnlyList<FtpServer> servers)
    {
        var server = NetworkPlaces.ServerFor(folder, servers);
        return NetworkPlaces.List(folder, server)
            .Where(i => showHidden || !i.Name.StartsWith('.'))
            .Select(i => new Entry(NetworkPlaces.Child(folder, i.Name, i.IsFolder), i.Name, i.IsFolder, i.Name.StartsWith('.'), i.Modified, i.Size))
            .ToList();
    }

    private List<Entry> ReadFtp(string folder, bool showHidden) => ReadFtp(folder, showHidden, _settings.FtpServers.ToList());

    // ================================================================ This PC and the sidebar

    /// <summary>On This PC, under the drives: saved FTP servers, and cards to add network places.</summary>
    private void AddNetworkCards()
    {
        var heading = Ui.Text("Network", 20, "FgBrush", bold: true);
        heading.Width = 1400; // its own line in the wrap panel
        heading.Margin = new Thickness(8, 18, 0, 10);
        _gridPanel.Children.Add(heading);
        foreach (var server in _settings.FtpServers)
            NetCard("\uE968", server.Name, (server.Secure ? "FTPS  ·  " : "FTP  ·  ") + server.Host + (server.User.Length > 0 ? "  ·  " + server.User : ""),
                new Entry(server.StartUrl, server.Name, true, false, DateTime.MinValue, 0));
        NetCard("\uE710", "Add an FTP server", "Browse files on a server, NAS or console", new Entry("action:ftp", "Add an FTP server", false, false, DateTime.MinValue, 0));
        NetCard("\uE8CE", "Map a network drive", "Give a shared folder its own drive letter", new Entry("action:map", "Map a network drive", false, false, DateTime.MinValue, 0));
        NetCard("\uE774", "Open a network location", @"Go to \\computer\share or ftp://…", new Entry("action:open", "Open a network location", false, false, DateTime.MinValue, 0));
    }

    private void NetCard(string glyph, string title, string detail, Entry entry)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Width = 300 };
        var icon = Ui.Text(glyph, 30, "AccentBrush");
        icon.FontFamily = Ui.IconFont;
        icon.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(icon);
        var names = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = 250 };
        var name = Ui.Text(title, 17, "FgBrush", bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        names.Children.Add(name);
        var sub = Ui.Text(detail, 13, "SubtleBrush");
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        names.Children.Add(sub);
        head.Children.Add(names);
        var box = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Child = head };
        box.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var button = Ui.Tile(box);
        button.Margin = new Thickness(0, 0, 8, 8);
        Hook(button, entry);
        _gridPanel.Children.Add(button);
        _shown.Add((entry, button, new Image(), new Border()));
    }

    private void BuildNetworkSidebar()
    {
        if (_settings.FtpServers.Count == 0) return;
        Section("NETWORK");
        foreach (var server in _settings.FtpServers) Nav("\uE968", server.Name, server.StartUrl);
    }

    // ================================================================ menus

    /// <summary>The main menu's network items.</summary>
    private void AddNetworkMenu(List<(string Label, string Glyph, Func<Task> Run)> actions)
    {
        actions.Add(("Add an FTP server…", "\uE968", () => EditFtpAsync(null)));
        actions.Add(("Map a network drive…", "\uE8CE", MapDriveAsync));
        if (NetworkPlaces.NetworkDrives().Count > 0) actions.Add(("Disconnect a network drive…", "\uE8CD", DisconnectDriveAsync));
        actions.Add(("Open a network location…", "\uE774", OpenLocationAsync));
        if (NetworkPlaces.IsFtp(_folder)) actions.Add(("Refresh", "\uE72C", () => LoadAsync(keepFocus: true)));
    }

    private Task RunNetworkActionAsync(string action) => action switch
    {
        "action:ftp" => EditFtpAsync(null),
        "action:map" => MapDriveAsync(),
        "action:open" => OpenLocationAsync(),
        _ => Task.CompletedTask
    };

    /// <summary>X on a network card, a server or a file on a server. False when it's an ordinary file or folder.</summary>
    private async Task<bool> NetworkActionsAsync(Entry? focused)
    {
        if (focused == null) return false;
        if (focused.Path.StartsWith("action:")) { await RunNetworkActionAsync(focused.Path); return true; }
        if (_folder == null && NetworkPlaces.IsFtp(focused.Path))
        {
            var server = Server(focused.Path);
            if (server == null) return true;
            int pick = await Host.AskWithIconsAsync(server.Name, server.Root, ("Open", "\uE8DA"), ("Change the server's details", "\uE70F"), ("Remove this server", "\uE74D"));
            if (pick == 0) NavigateTo(server.StartUrl);
            else if (pick == 1) await EditFtpAsync(server);
            else if (pick == 2 && await Host.AskAsync("Remove " + server.Name + "?", "Its saved password is forgotten too. Files on the server aren't touched.", "Cancel", "Remove") == 1)
            {
                NetworkPlaces.ForgetPassword(server.CredentialTarget);
                _settings.FtpServers.Remove(server);
                _settings.Save();
                await LoadAsync(keepFocus: false);
            }
            return true;
        }
        if (!NetworkPlaces.IsFtp(focused.Path)) return false;

        var targets = Targets(focused);
        bool single = targets.Count == 1;
        var first = targets[0];
        var actions = new List<(string Label, string Glyph, Func<Task> Run)>();
        if (single) actions.Add(("Open", "\uE8E5", () => { Open(first); return Task.CompletedTask; }));
        actions.Add(("Download to Downloads", "\uE896", () => RunAsync("Downloading…", () =>
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            foreach (var t in targets) NetworkPlaces.Download(t.Path, downloads, Server(t.Path));
        })));
        actions.Add((_checked.Contains(first.Path) && single ? "Unselect" : "Select", "\uE73A", () => { foreach (var t in targets) ToggleCheck(t); return Task.CompletedTask; }));
        actions.Add(("Copy (paste into a folder on this PC to download)", "\uE8C8", () => { SetClipboard(targets, cut: false); return Task.CompletedTask; }));
        actions.Add(("Cut (to move off the server)", "\uE8C6", () => { SetClipboard(targets, cut: true); return Task.CompletedTask; }));
        if (_clipboard.Count > 0 && single && first.IsFolder)
            actions.Add(($"Paste into \"{first.Name}\"", "\uE77F", () => PasteAsync(first.Path)));
        if (single) actions.Add(("Rename", "\uE8AC", () => RenameAsync(first)));
        actions.Add(("Delete from the server", "\uE74D", () => DeleteAsync(targets)));
        if (single) actions.Add(("Copy the address", "\uE71B", () => { CopyPath(Uri.UnescapeDataString(first.Path)); return Task.CompletedTask; }));
        actions.Add(("Properties", "\uE946", () => PropertiesAsync(targets)));
        int choice = await Host.AskWithIconsAsync(single ? first.Name : $"{targets.Count} items selected", "On " + ServerNameFor(first.Path),
            actions.Select(a => (a.Label, a.Glyph)).ToArray());
        if (choice >= 0) await actions[choice].Run();
        return true;
    }

    // ================================================================ FTP files

    /// <summary>Opens a file on a server: a copy is downloaded, then opened like any other file.</summary>
    private async Task OpenFtpFileAsync(Entry entry)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SquareDeck", "FTP", Math.Abs(entry.Path.GetHashCode()).ToString());
        string? local = null;
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.EnumerateFiles(folder)) { try { File.Delete(old); } catch (IOException) { } }
            await Host.RunBusyAsync("Downloading " + entry.Name + "…", () => Task.Run(() => local = NetworkPlaces.Download(entry.Path, folder, Server(entry.Path))));
        }
        catch (Exception ex)
        {
            await Host.AskAsync("Couldn't download it", NetworkPlaces.Reason(ex), "OK");
            return;
        }
        if (local == null) return;
        if (FileOpener.TryOpen(Host, local, new[] { local })) return;
        try { ProcessUtil.Open(local); }
        catch (Exception ex) { await Host.AskAsync("Couldn't open it", ex.Message, "OK"); }
    }

    /// <summary>Copies or moves between this PC and a server (either way, or server to server through a temporary copy).</summary>
    private void FtpTransfer(List<string> sources, string destination, bool move)
    {
        var servers = _settings.FtpServers.ToList();
        FtpServer? S(string url) => NetworkPlaces.ServerFor(url, servers);
        foreach (var source in sources)
        {
            bool fromFtp = NetworkPlaces.IsFtp(source), toFtp = NetworkPlaces.IsFtp(destination);
            if (fromFtp && toFtp)
            {
                string temp = Path.Combine(Path.GetTempPath(), "SquareDeck", "FTP", "transfer");
                Directory.CreateDirectory(temp);
                string local = NetworkPlaces.Download(source, temp, S(source));
                NetworkPlaces.Upload(local, destination, S(destination));
                try { if (Directory.Exists(local)) Directory.Delete(local, true); else File.Delete(local); } catch (IOException) { }
            }
            else if (fromFtp) NetworkPlaces.Download(source, destination, S(source));
            else if (toFtp) NetworkPlaces.Upload(source, destination, S(destination));
            else { FileOps.Transfer(new[] { source }, destination, move, CancellationToken.None); continue; }
            // Moving: the original goes once the copy is made (to the Recycle Bin when it was on this PC).
            if (move)
            {
                if (fromFtp) NetworkPlaces.Delete(source, S(source));
                else FileOps.Recycle(new[] { source });
            }
        }
    }

    // ================================================================ adding and changing servers

    private async Task EditFtpAsync(FtpServer? editing)
    {
        string startAddress = editing == null ? "" : editing.Host + (editing.Port != 21 ? ":" + editing.Port : "") + (editing.Folder.Trim('/').Length > 0 ? "/" + editing.Folder.Trim('/') : "");
        var address = await Host.PromptAsync(editing == null ? "Add an FTP server" : "Server address",
            "The server's address, like ftp.example.com, 192.168.1.20:2121 or 192.168.1.20/games", startAddress, "Next");
        if (string.IsNullOrWhiteSpace(address)) return;
        string text = address.Trim();
        bool secure = text.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) || editing?.Secure == true;
        text = System.Text.RegularExpressions.Regex.Replace(text, "^ftps?://", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        string folder = "/";
        int slash = text.IndexOf('/');
        if (slash >= 0) { folder = "/" + text[(slash + 1)..].Trim('/'); text = text[..slash]; }
        int port = 21;
        int colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out var p) && p is > 0 and < 65536) { port = p; text = text[..colon]; }
        if (text.Length == 0 || Uri.CheckHostName(text) == UriHostNameType.Unknown)
        {
            await Host.AskAsync("That address doesn't look right", "Type the server's name or IP address, like ftp.example.com or 192.168.1.20.", "OK");
            return;
        }

        var user = await Host.PromptAsync("User name", "Leave it empty to sign in as a guest (anonymous FTP).", editing?.User ?? "", "Next");
        if (user == null) return;
        user = user.Trim();
        string? password = null;
        if (user.Length > 0)
        {
            password = await Host.PromptSecretAsync("Password for " + user, editing != null ? "Leave it empty to keep the saved password." : "Saved in Windows' Credential Manager, not in SquareDeck's settings.", "Next");
            if (password == null) return;
        }
        int security = await Host.AskAsync("Connection", "FTPS encrypts the password and files on the way. Most home servers use plain FTP.",
            secure ? "FTPS (encrypted)" : "Plain FTP", secure ? "Plain FTP" : "FTPS (encrypted)");
        if (security < 0) return;
        secure = secure ? security == 0 : security == 1;
        var name = await Host.PromptAsync("Name", "What to call it on This PC and in the sidebar.", editing?.Name ?? text, "Save");
        if (string.IsNullOrWhiteSpace(name)) return;

        var server = editing ?? new FtpServer();
        string oldTarget = server.CredentialTarget;
        server.Name = name.Trim();
        server.Host = text;
        server.Port = port;
        server.User = user;
        server.Secure = secure;
        server.Folder = folder;
        if (user.Length > 0 && (password!.Length > 0 || editing == null || oldTarget != server.CredentialTarget))
        {
            if (editing != null && oldTarget != server.CredentialTarget && password!.Length == 0) password = NetworkPlaces.ReadPassword(oldTarget) ?? "";
            NetworkPlaces.SavePassword(server.CredentialTarget, user, password!);
        }
        if (editing != null && oldTarget != server.CredentialTarget) NetworkPlaces.ForgetPassword(oldTarget);

        // Check it works before saving, but let it be saved anyway (a server that's off right now).
        try { await Host.RunBusyAsync("Connecting to " + server.Name + "…", () => Task.Run(() => NetworkPlaces.Test(server))); }
        catch (Exception ex)
        {
            if (await Host.AskAsync("Couldn't connect", NetworkPlaces.Reason(ex), "Save it anyway", "Cancel") != 0)
            {
                if (editing == null) NetworkPlaces.ForgetPassword(server.CredentialTarget);
                return;
            }
        }
        if (editing == null) _settings.FtpServers.Add(server);
        _settings.Save();
        if (_folder == null) await LoadAsync(keepFocus: true);
        else BuildSidebar();
        if (editing == null && await Host.AskAsync(server.Name + " is saved", "It's on This PC and in the sidebar.", "Open it now", "Not now") == 0)
            NavigateTo(server.StartUrl);
    }

    // ================================================================ Windows network shares

    private async Task MapDriveAsync()
    {
        var remote = await Host.PromptAsync("Map a network drive", @"The shared folder's address, like \\DESKTOP\Games or \\192.168.1.20\media", @"\\", "Next");
        if (string.IsNullOrWhiteSpace(remote)) return;
        remote = remote.Trim().Replace('/', '\\');
        if (!remote.StartsWith(@"\\")) remote = @"\\" + remote.TrimStart('\\');
        if (remote.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            await Host.AskAsync("Add the shared folder's name", @"The address needs the computer and the folder, like \\DESKTOP\Games.", "OK");
            return;
        }
        var letters = NetworkPlaces.FreeLetters().Take(8).ToArray();
        if (letters.Length == 0) { await Host.AskAsync("No drive letters left", "Every letter from D to Z is in use.", "OK"); return; }
        int pick = await Host.AskAsync("Drive letter", "Which letter it gets in This PC (and in Windows).", letters);
        if (pick < 0) return;
        int account = await Host.AskAsync("Sign in", "Shared folders on another PC usually need that PC's user name and password.",
            "Use my Windows sign-in", "Sign in with another account");
        if (account < 0) return;
        string? user = null, password = null;
        if (account == 1)
        {
            user = await Host.PromptAsync("User name", @"For another PC, its own user name (or PCNAME\name).", "", "Next");
            if (string.IsNullOrWhiteSpace(user)) return;
            password = await Host.PromptSecretAsync("Password for " + user.Trim(), "Windows remembers it for this drive.", "Connect");
            if (password == null) return;
        }
        string letter = letters[pick];
        string share = remote;
        try
        {
            await Host.RunBusyAsync("Connecting " + letter + "…", () => Task.Run(() => NetworkPlaces.MapDrive(share, letter, user?.Trim(), password, remember: true)));
        }
        catch (Exception ex)
        {
            string reason = ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1219 }
                ? "This PC is already connected to that computer with a different account. Disconnect its other drives first, or use the same account."
                : ex.Message;
            await Host.AskAsync("Couldn't map the drive", reason, "OK");
            return;
        }
        Host.ShowToast("Network drive", letter + " is connected", share, "", "info");
        if (_folder == null) await LoadAsync(keepFocus: true);
        else BuildSidebar();
        NavigateTo(letter + "\\");
    }

    private async Task DisconnectDriveAsync()
    {
        var drives = NetworkPlaces.NetworkDrives();
        if (drives.Count == 0) return;
        int pick = await Host.AskAsync("Disconnect a network drive", "The shared folder isn't touched; it just stops being a drive here.",
            drives.Select(d => $"{d.Letter}  {d.Label}").ToArray());
        if (pick < 0) return;
        try { NetworkPlaces.Disconnect(drives[pick].Letter); }
        catch (Exception ex) { await Host.AskAsync("Couldn't disconnect it", ex.Message, "OK"); return; }
        if (_folder != null && _folder.StartsWith(drives[pick].Letter, StringComparison.OrdinalIgnoreCase)) NavigateTo(null);
        else await LoadAsync(keepFocus: false);
    }

    private async Task OpenLocationAsync()
    {
        var where = await Host.PromptAsync("Open a network location", @"A shared folder (\\DESKTOP\Games) or an FTP address (ftp://192.168.1.20).", @"\\", "Open");
        if (string.IsNullOrWhiteSpace(where)) return;
        where = where.Trim();
        if (where.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(where, UriKind.Absolute, out var uri)) { await Host.AskAsync("That address doesn't look right", null, "OK"); return; }
            NavigateTo($"ftp://{uri.Host}:{uri.Port}{(uri.AbsolutePath.EndsWith('/') ? uri.AbsolutePath : uri.AbsolutePath + "/")}");
            return;
        }
        where = where.Replace('/', '\\');
        if (!where.StartsWith(@"\\") && !(where.Length >= 2 && where[1] == ':')) where = @"\\" + where.TrimStart('\\');
        string target = where;
        bool ok = false;
        await Host.RunBusyAsync("Looking for " + target + "…", async () => ok = await Task.Run(() => Directory.Exists(target)));
        if (!ok)
        {
            await Host.AskAsync("Couldn't open it", @"The folder couldn't be reached. Check the address (\\computer\share), that the other PC is on and sharing, and your sign-in. " +
                "If it needs a password, use \"Map a network drive\" to sign in.", "OK");
            return;
        }
        NavigateTo(target);
    }
}
