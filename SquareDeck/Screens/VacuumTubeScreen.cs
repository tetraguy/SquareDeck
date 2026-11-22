using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// VacuumTube, full screen (Experimental, from the Music page): YouTube's TV interface, made for a controller, with
/// VacuumTube's ad blocking, SponsorBlock, DeArrow, dislikes and its own settings page (Y, or YouTube's settings).
///
/// The page reads the controller itself, through VacuumTube's controller support, so every button does what it does on
/// a console; SquareDeck keeps out of the way while it does (<see cref="PadInPage"/>). If that's off, or with no
/// controller, SquareDeck sends the same console button codes itself. Leaving, back to the Music page: View + Menu
/// together, the "SquareDeck" button that shows in the corner with touch or a mouse, Ctrl+Q, or YouTube's own Exit
/// (B from the home row), which VacuumTube turns into closing the window.
/// </summary>
public sealed class VacuumTubeScreen : Screen, IPagePad
{
    private static Task<CoreWebView2Environment>? _environment;

    private readonly WebView2 _web = new() { Visibility = Visibility.Hidden, DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x28, 0x28, 0x28) };
    private readonly Grid _splash = new();
    private readonly TextBlock _status = Ui.Text("Opening VacuumTube…", 18, "SubtleBrush");
    private readonly Border _spinnerRing = new() { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), BorderThickness = new Thickness(4) };
    private DateTime _padAliveAt = DateTime.MinValue;
    private DateTime _viewAt = DateTime.MinValue, _startAt = DateTime.MinValue;
    private bool _started, _ready, _disposed;

    /// <summary>The page is reading the controller itself, so SquareDeck's own controller handling waits.</summary>
    public bool PadInPage => _ready && !_disposed && DateTime.UtcNow - _padAliveAt < TimeSpan.FromSeconds(2.5);

    public VacuumTubeScreen()
    {
        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)) };

        // While Leanback loads: VacuumTube's icon, a ring turning, and what's happening.
        var middle = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (File.Exists(VacuumTube.IconPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 256;
                bmp.UriSource = new Uri(VacuumTube.IconPath);
                bmp.EndInit();
                bmp.Freeze();
                middle.Children.Add(new Image { Source = bmp, Width = 128, Height = 128, Margin = new Thickness(0, 0, 0, 26) });
            }
            catch (Exception) { }
        }
        _spinnerRing.BorderBrush = new LinearGradientBrush(Color.FromRgb(0xFF, 0x33, 0x33), Color.FromArgb(0, 0xFF, 0x33, 0x33), 0);
        _spinnerRing.RenderTransformOrigin = new Point(0.5, 0.5);
        _spinnerRing.RenderTransform = new RotateTransform();
        middle.Children.Add(_spinnerRing);
        _status.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.TextAlignment = TextAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.MaxWidth = 760;
        _status.Margin = new Thickness(0, 24, 0, 0);
        middle.Children.Add(_status);
        _splash.Children.Add(middle);

        root.Children.Add(_web);
        root.Children.Add(_splash);
        Content = root;
    }

    public override void OnShown()
    {
        if (_started) return;
        _started = true;
        if (!Theme.ReduceMotion)
            _spinnerRing.RenderTransform.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        if (!VacuumTube.Installed)
        {
            Fail("VacuumTube's files are missing from SquareDeck's folder (VacuumTube\\vacuumtube.js). Reinstalling SquareDeck puts them back.");
            return;
        }
        try
        {
            // Its own folder (sign-in, history), and videos may start without a click first, as in VacuumTube.
            _environment ??= CoreWebView2Environment.CreateAsync(null, VacuumTube.DataFolder,
                new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            await _web.EnsureCoreWebView2Async(await _environment);
            if (_disposed) return;
            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.UserAgent = VacuumTube.UserAgent;

            // What VacuumTube's Electron side does: present as a Cobalt TV app (user agent and client hints), and let the
            // page's own scripts, styles and the SponsorBlock / DeArrow / dislike servers past YouTube's content policy.
            await core.CallDevToolsProtocolMethodAsync("Emulation.setUserAgentOverride", VacuumTube.UserAgentOverrideJson);
            await core.CallDevToolsProtocolMethodAsync("Page.setBypassCSP", "{\"enabled\":true}");

            await core.AddScriptToExecuteOnDocumentCreatedAsync(VacuumTube.BootScript());
            await core.AddScriptToExecuteOnDocumentCreatedAsync(await File.ReadAllTextAsync(VacuumTube.ScriptPath));

            core.WebMessageReceived += (_, e) =>
            {
                string message;
                try { message = e.TryGetWebMessageAsString(); } catch (Exception) { return; }
                OnPageMessage(message);
            };
            // YouTube's Exit, which VacuumTube turns into closing the window.
            core.WindowCloseRequested += (_, _) => { if (!_disposed && Host.IsTop(this)) { UiSound.Back(); Host.Pop(this); } };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true; // never a second window
                if (IsYouTube(e.Uri)) core.Navigate(e.Uri);
            };
            // Leanback and Google's sign-in only; anything else is stopped (VacuumTube is a YouTube app, not a browser).
            _web.NavigationStarting += (_, e) => { if (!IsYouTube(e.Uri)) e.Cancel = true; };
            _web.NavigationCompleted += (_, _) => Reveal();
            core.ProcessFailed += (_, e) =>
            {
                if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited)
                    Dispatcher.BeginInvoke(() => { if (!_disposed) Fail("VacuumTube stopped unexpectedly. Open it again from the Music page."); });
            };
            core.Navigate(VacuumTube.LeanbackUrl);
        }
        catch (Exception ex)
        {
            _environment = null;
            CrashLog.Write("VacuumTube", ex);
            Fail("VacuumTube couldn't open. It needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11 and current " +
                 "Windows 10 (microsoft.com/edge/webview2).\n\n" + ex.Message);
        }
    }

    private static bool IsYouTube(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return false;
        string host = uri.Host.ToLowerInvariant();
        return new[] { "youtube.com", "google.com", "googlevideo.com", "ytimg.com", "ggpht.com", "gstatic.com", "googleusercontent.com", "youtube-nocookie.com" }
            .Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }

    private void OnPageMessage(string message)
    {
        if (message == "sd:alive") { _padAliveAt = DateTime.UtcNow; return; }
        if (message == "sd:leave") { Leave(); return; } // View + Menu, the corner button, or Ctrl+Q
        if (message.StartsWith("vt:config|", StringComparison.Ordinal)) { VacuumTube.SaveConfig(message["vt:config|".Length..]); return; }
        if (message.StartsWith("vt:open|", StringComparison.Ordinal) && Host.IsTop(this))
        {
            // VacuumTube's About page links (its GitHub page and releases): in SquareDeck's own browser.
            string url = message["vt:open|".Length..];
            if (url.StartsWith(VacuumTube.ProjectUrl, StringComparison.OrdinalIgnoreCase)) Host.Push(new BrowserScreen(url));
        }
    }

    /// <summary>Back to the Music page (VacuumTube closes).</summary>
    private void Leave()
    {
        if (_disposed || !Host.IsTop(this)) return;
        UiSound.Back();
        Host.Pop(this);
    }

    /// <summary>Shows the page once Leanback has loaded.</summary>
    private async void Reveal()
    {
        if (_ready || _disposed) return;
        _ready = true;
        await Task.Delay(350); // the first frame of Leanback, not a blank page
        if (_disposed) return;
        _splash.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
        await Task.Delay(220);
        _splash.Visibility = Visibility.Collapsed;
        _spinnerRing.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        if (_disposed || !Host.IsTop(this)) return;
        _web.Visibility = Visibility.Visible;
        _web.Focus();
    }

    private void Fail(string text)
    {
        _status.Text = text;
        _spinnerRing.Visibility = Visibility.Collapsed;
        _web.Visibility = Visibility.Hidden;
    }

    // The page is a window of its own and would cover a pop-up drawn over it, so it steps aside for them.
    public override void OnCovered() => _web.Visibility = Visibility.Hidden;

    public override void OnResumed()
    {
        if (!_ready || _disposed) return;
        _web.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => _web.Focus(), DispatcherPriority.Input);
    }

    public override void OnClosed()
    {
        if (_disposed) return;
        _disposed = true;
        try { _web.Dispose(); } catch (Exception) { }
    }

    public override void FocusDefault() => _web.Focus();

    /// <summary>
    /// The console button codes Leanback understands (Starboard: 32768 + the standard gamepad button), sent the way
    /// VacuumTube's controller support sends them, for when the page isn't reading the controller itself.
    /// </summary>
    private static int? ButtonCode(NavAction action) => action switch
    {
        NavAction.Accept => 32768,
        NavAction.Back => 32769,
        NavAction.X => 32770,
        NavAction.Y => 32771,
        NavAction.LB => 32772,
        NavAction.RB => 32773,
        NavAction.LT => 32774,
        NavAction.RT => 32775,
        NavAction.Up => 32780,
        NavAction.Down => 32781,
        NavAction.Left => 32782,
        NavAction.Right => 32783,
        _ => null
    };

    public override bool OnAction(NavAction action)
    {
        if (!_ready || _disposed)
        {
            // Still opening (or it failed): B goes back to the Music page.
            if (action == NavAction.Back) { UiSound.Back(); Host.Pop(this); }
            return true;
        }
        if (!_web.IsKeyboardFocusWithin) _web.Focus();
        // View + Menu together goes back to SquareDeck, as it does when the page reads the controller itself.
        if (action == NavAction.View) _viewAt = DateTime.UtcNow;
        if (action == NavAction.Start) _startAt = DateTime.UtcNow;
        if (action is NavAction.View or NavAction.Start && (_viewAt - _startAt).Duration() < TimeSpan.FromMilliseconds(400)) { Leave(); return true; }
        if (ButtonCode(action) is int code)
        {
            _ = _web.CoreWebView2?.ExecuteScriptAsync(
                $"(function(){{['keydown','keyup'].forEach(function(n){{var e=new Event(n);e.keyCode={code};document.dispatchEvent(e);}});}})()");
        }
        return true; // everything belongs to YouTube while it's open
    }

    public override bool OnRightStick(double x, double y, bool pressed) => true;
}

/// <summary>A page that reads the controller itself while it says so; SquareDeck's own controller handling then waits.</summary>
public interface IPagePad
{
    bool PadInPage { get; }
}
