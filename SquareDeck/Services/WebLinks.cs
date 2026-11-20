using System.Windows;
using SquareDeck.Screens;

namespace SquareDeck.Services;

/// <summary>
/// Web links (http and https) opened from anywhere in SquareDeck go to its own browser when Settings > Browser >
/// "Open web links in SquareDeck" is on, instead of Windows' default browser. Everything else (steam://, ms-settings:,
/// files, folders) still opens with Windows. <see cref="ProcessUtil.Open"/> sends links here, so every link button
/// follows the setting.
/// </summary>
public static class WebLinks
{
    private static bool? _runtime;

    /// <summary>True for an absolute http or https address.</summary>
    public static bool IsWeb(string? target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Whether the Edge WebView2 Runtime the browser needs is installed (checked once).</summary>
    public static bool BrowserAvailable
    {
        get
        {
            if (_runtime is bool known) return known;
            try { _runtime = !string.IsNullOrEmpty(Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString()); }
            catch (Exception) { _runtime = false; }
            return _runtime.Value;
        }
    }

    /// <summary>Opens a link: in SquareDeck's browser when the setting allows, otherwise with Windows.</summary>
    public static bool Open(string url, MainWindow? host = null) =>
        TryInside(url, host) || ProcessUtil.OpenOutside(url);

    /// <summary>
    /// Opens a web link in SquareDeck's browser (bringing SquareDeck to the front) if it's a web link, the setting is
    /// on and the browser can run. False means it should open the usual way.
    /// </summary>
    public static bool TryInside(string url, MainWindow? host = null)
    {
        if (!IsWeb(url)) return false;
        var app = Application.Current;
        if (app == null) return false;
        var window = host ?? (app.Dispatcher.CheckAccess() ? app.MainWindow as MainWindow : null);
        // From another thread the window can't be asked; the setting is read from the saved settings it uses.
        var settings = window?.Settings ?? MainWindow.CurrentSettings;
        if (settings == null || !settings.OpenLinksInSquareDeck || !BrowserAvailable) return false;

        void Show()
        {
            if ((host ?? app.MainWindow) is not MainWindow w) return;
            w.Push(BrowserScreen.Open(w, url));
            WindowActivator.BringToFront(w);
        }
        if (app.Dispatcher.CheckAccess()) Show();
        else app.Dispatcher.BeginInvoke(new Action(Show));
        return true;
    }
}
