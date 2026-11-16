using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace SquareDeck.Services;

/// <summary>One screen attached to the PC.</summary>
public sealed record MonitorInfo(string Device, int Left, int Top, int Width, int Height, bool Primary, int Number)
{
    public string Label => $"Display {Number}  \u00B7  {Width} \u00D7 {Height}" + (Primary ? "  (Windows main display)" : "");
}

/// <summary>Lists the PC's screens and moves the launcher between them.</summary>
public static class Monitors
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    public static List<MonitorInfo> List()
    {
        var found = new List<MonitorInfo>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref Rect _, IntPtr _) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    // "\\.\DISPLAY2" -> 2, the number Windows shows in its own display settings.
                    var digits = new string(info.Device.Where(char.IsDigit).ToArray());
                    int.TryParse(digits, out int number);
                    found.Add(new MonitorInfo(info.Device, info.Monitor.Left, info.Monitor.Top,
                        info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top,
                        (info.Flags & 1) != 0, number));
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception) { }
        return found.OrderBy(m => m.Number).ToList();
    }

    /// <summary>The saved display if it is still connected; otherwise null (use wherever Windows puts the window).</summary>
    public static MonitorInfo? Find(string device) =>
        string.IsNullOrEmpty(device) ? null : List().FirstOrDefault(m => string.Equals(m.Device, device, StringComparison.OrdinalIgnoreCase));

    /// <summary>Moves a window onto a display, keeping it full screen if it was.</summary>
    public static bool MoveTo(Window window, MonitorInfo monitor)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        try
        {
            bool full = window.WindowState == WindowState.Maximized;
            // A maximized window can't be moved: restore it, place it on the display, then maximize there.
            if (full) window.WindowState = WindowState.Normal;
            int width = Math.Min(1280, monitor.Width), height = Math.Min(720, monitor.Height);
            const uint NoZOrder = 0x0004, NoActivate = 0x0010;
            SetWindowPos(handle, IntPtr.Zero, monitor.Left + (monitor.Width - width) / 2, monitor.Top + (monitor.Height - height) / 2,
                width, height, NoZOrder | NoActivate);
            if (full) window.WindowState = WindowState.Maximized;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>A pop-up some other app showed through Windows.</summary>
public sealed record WindowsToast(uint Id, string App, string Title, string Text, DateTime When, string IconPath = "");

/// <summary>
/// Reads the notifications in Windows' own notification center, so they can be shown in the launcher too.
/// Windows has to allow it (Settings > Privacy and security > Notifications); <see cref="Status"/> says how that went.
/// </summary>
public static class WindowsNotifications
{
    /// <summary>"unknown", "allowed", "denied" or "unsupported".</summary>
    public static string Status { get; private set; } = "unknown";

    private static readonly HashSet<uint> Seen = new();
    private static bool _baselined;

    /// <summary>
    /// The sending app's own icon (from Windows), saved once per app so the notification center can show it.
    /// Empty when Windows doesn't have one.
    /// </summary>
    private static async Task<string> AppIconAsync(Windows.UI.Notifications.UserNotification n, string app)
    {
        if (app.Length == 0) return "";
        try
        {
            string safe = string.Concat(app.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            string folder = System.IO.Path.Combine(Settings.DataDir, "app-icons");
            string path = System.IO.Path.Combine(folder, safe + ".png");
            if (System.IO.File.Exists(path)) return path;
            var logo = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64));
            if (logo == null) return "";
            using var stream = await logo.OpenReadAsync();
            using var input = stream.AsStreamForRead();
            using var ms = new System.IO.MemoryStream();
            await input.CopyToAsync(ms);
            if (ms.Length == 0) return "";
            System.IO.Directory.CreateDirectory(folder);
            await System.IO.File.WriteAllBytesAsync(path, ms.ToArray());
            return path;
        }
        catch (Exception) { return ""; }
    }

    /// <summary>Asks Windows for permission (it may show a prompt the first time).</summary>
    public static async Task<bool> RequestAccessAsync()
    {
        try
        {
            var listener = Windows.UI.Notifications.Management.UserNotificationListener.Current;
            var access = await listener.RequestAccessAsync();
            Status = access == Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed ? "allowed" : "denied";
        }
        catch (Exception)
        {
            Status = "unsupported"; // older Windows, or it refuses apps that weren't installed from a package
        }
        return Status == "allowed";
    }

    /// <summary>
    /// Notifications that arrived since the last call. The first call only notes what is already there,
    /// so old notifications aren't replayed when the launcher starts.
    /// </summary>
    public static async Task<List<WindowsToast>> TakeNewAsync()
    {
        var fresh = new List<WindowsToast>();
        if (Status == "unknown") await RequestAccessAsync();
        if (Status != "allowed") return fresh;
        try
        {
            var listener = Windows.UI.Notifications.Management.UserNotificationListener.Current;
            var all = await listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
            var present = new HashSet<uint>();
            foreach (var n in all)
            {
                present.Add(n.Id);
                if (!Seen.Add(n.Id) || !_baselined) continue;

                string app = "";
                try { app = n.AppInfo?.DisplayInfo?.DisplayName ?? ""; } catch (Exception) { }
                if (app.Contains("SquareDeck", StringComparison.OrdinalIgnoreCase)) continue;

                string title = "", text = "";
                try
                {
                    var binding = n.Notification?.Visual?.GetBinding(Windows.UI.Notifications.KnownNotificationBindings.ToastGeneric);
                    var lines = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    if (lines is { Count: > 0 })
                    {
                        title = lines[0];
                        text = string.Join(" ", lines.Skip(1));
                    }
                }
                catch (Exception) { }
                if (title.Length == 0 && app.Length == 0) continue;
                string icon = await AppIconAsync(n, app);
                if (title.Length == 0) { title = app; app = ""; }
                fresh.Add(new WindowsToast(n.Id, app, title, text, n.CreationTime.LocalDateTime, icon));
            }
            Seen.IntersectWith(present); // forget ones the user has dismissed, so the set doesn't grow forever
            _baselined = true;
        }
        catch (UnauthorizedAccessException)
        {
            Status = "denied";
        }
        catch (Exception)
        {
            // A hiccup talking to Windows: try again next time.
        }
        return fresh;
    }
}

/// <summary>A game on the user's Steam wishlist.</summary>
public sealed record WishlistItem(int AppId, string Name, string Price, int DiscountPercent, DateTime Added);

/// <summary>Reads the signed-in Steam user's wishlist.</summary>
public static class SteamWishlist
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// The newest items on the wishlist with names and prices. Null when it can't be read (no Steam account
    /// found, offline, or the profile's game details are private).
    /// </summary>
    public static async Task<(List<WishlistItem> Items, int Total)?> GetAsync(string apiKey, int max, CancellationToken ct)
    {
        var steamId = SteamLibrary.GetSteamId64();
        if (steamId == null) return null;
        try
        {
            string url = "https://api.steampowered.com/IWishlistService/GetWishlist/v1/?steamid=" + steamId +
                         (string.IsNullOrWhiteSpace(apiKey) ? "" : "&key=" + Uri.EscapeDataString(apiKey.Trim()));
            var entries = ParseList(await Http.GetStringAsync(url, ct).ConfigureAwait(false));
            if (entries == null) return null;

            var newest = entries.OrderByDescending(e => e.Added).Take(max).ToList();
            using var gate = new SemaphoreSlim(4);
            var items = await Task.WhenAll(newest.Select(async e =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var json = await Http.GetStringAsync(
                        $"https://store.steampowered.com/api/appdetails?appids={e.AppId}&filters=basic,price_overview", ct).ConfigureAwait(false);
                    return ParseApp(json, e.AppId, e.Added);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    return new WishlistItem(e.AppId, "App " + e.AppId, "", 0, e.Added);
                }
                finally { gate.Release(); }
            })).ConfigureAwait(false);
            return (items.ToList(), entries.Count);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Just the app ids and dates on the wishlist (one quick request). Null if it can't be read.</summary>
    public static async Task<List<(int AppId, DateTime Added)>?> GetIdsAsync(string apiKey, CancellationToken ct)
    {
        var steamId = SteamLibrary.GetSteamId64();
        if (steamId == null) return null;
        try
        {
            string url = "https://api.steampowered.com/IWishlistService/GetWishlist/v1/?steamid=" + steamId +
                         (string.IsNullOrWhiteSpace(apiKey) ? "" : "&key=" + Uri.EscapeDataString(apiKey.Trim()));
            return ParseList(await Http.GetStringAsync(url, ct).ConfigureAwait(false));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Name and current price of one game. Null if the store didn't answer.</summary>
    public static async Task<WishlistItem?> GetDetailsAsync(int appId, DateTime added, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync(
                $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic,price_overview", ct).ConfigureAwait(false);
            return ParseApp(json, appId, added);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public static List<(int AppId, DateTime Added)>? ParseList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var response)) return null;
        var list = new List<(int, DateTime)>();
        if (!response.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("appid", out var id) || !id.TryGetInt32(out int appId)) continue;
            long added = item.TryGetProperty("date_added", out var d) && d.TryGetInt64(out var seconds) ? seconds : 0;
            list.Add((appId, DateTimeOffset.FromUnixTimeSeconds(added).LocalDateTime));
        }
        return list;
    }

    public static WishlistItem ParseApp(string json, int appId, DateTime added)
    {
        using var doc = JsonDocument.Parse(json);
        string name = "App " + appId, price = "";
        int discount = 0;
        if (doc.RootElement.TryGetProperty(appId.ToString(), out var app) && app.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("name", out var n)) name = n.GetString() ?? name;
            if (data.TryGetProperty("is_free", out var free) && free.ValueKind == JsonValueKind.True) price = "Free";
            if (data.TryGetProperty("price_overview", out var p))
            {
                if (p.TryGetProperty("final_formatted", out var f)) price = f.GetString() ?? price;
                if (p.TryGetProperty("discount_percent", out var dp)) dp.TryGetInt32(out discount);
            }
            else if (price.Length == 0 && data.TryGetProperty("release_date", out var rd) &&
                     rd.TryGetProperty("coming_soon", out var soon) && soon.ValueKind == JsonValueKind.True)
            {
                price = "Coming soon";
            }
        }
        return new WishlistItem(appId, name, price, discount, added);
    }
}
