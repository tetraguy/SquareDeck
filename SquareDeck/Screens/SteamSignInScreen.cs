using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Signs in to Steam to get the Steam Web API key: Steam's own sign-in page, which shows a QR code to scan with the
/// Steam app on a phone (or a name and password), then Steam's API key page, where SquareDeck reads the key, or
/// registers one for "localhost" when there isn't one yet (if Steam asks, that's approved in the Steam app too).
/// The key is saved to Settings. Sign-ins are kept in SquareDeck's own folder, apart from Edge and Steam.
/// </summary>
public sealed class SteamSignInScreen : Screen
{
    private const string LoginUrl = "https://steamcommunity.com/login/home/?goto=dev%2Fapikey";
    private const string KeyUrl = "https://steamcommunity.com/dev/apikey";

    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Microsoft.Web.WebView2.Wpf.WebView2 _web = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x17, 0x1A, 0x21) };
    private readonly TextBlock _status = Ui.Text("Opening Steam's sign-in…", 16, "SubtleBrush");
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private static Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment>? _environment;
    private bool _registering, _finished;
    private DateTime _registeredAt;

    /// <summary>True once a key was saved.</summary>
    public Task<bool> Result => _done.Task;

    public SteamSignInScreen()
    {
        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        // The top: what to do, and how it's going.
        var top = new Grid { Margin = new Thickness(36, 22, 36, 16) };
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel();
        words.Children.Add(Ui.Text("Sign in with Steam", 30, "FgBrush", bold: true));
        var how = Ui.Text("Open the Steam app on your phone, tap the shield (Steam Guard), then \"Scan a QR code\" and point it at the code below. " +
                          "SquareDeck then gets your free Steam Web API key by itself, for Friends and every game you own.", 16, "SubtleBrush");
        how.TextWrapping = TextWrapping.Wrap;
        how.Margin = new Thickness(0, 6, 0, 6);
        words.Children.Add(how);
        _status.TextWrapping = TextWrapping.Wrap;
        words.Children.Add(_status);
        top.Children.Add(words);
        var cancel = Ui.PanelButton("", "Cancel");
        cancel.VerticalAlignment = VerticalAlignment.Top;
        cancel.Margin = new Thickness(20, 0, 0, 0);
        cancel.Click += (_, _) => Host.Pop(this);
        Grid.SetColumn(cancel, 1);
        top.Children.Add(cancel);
        root.Children.Add(top);

        var frame = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, Margin = new Thickness(36, 0, 36, 30), Child = _web };
        Grid.SetRow(frame, 1);
        root.Children.Add(frame);
        Content = root;
        _poll.Tick += async (_, _) => await CheckAsync();
    }

    public override async void OnShown()
    {
        try
        {
            _environment ??= Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.DataDir, "SteamSignIn"));
            await _web.EnsureCoreWebView2Async(await _environment);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            _web.NavigationCompleted += async (_, _) => await CheckAsync();
            // Links to anywhere else (store pages, help) open nothing here.
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.Navigate(LoginUrl);
            _poll.Start();
        }
        catch (Exception)
        {
            _status.Text = "Signing in needs Microsoft Edge WebView2, which isn't available. Use \"Enter my key\" instead.";
        }
    }

    public override void OnClosed()
    {
        _poll.Stop();
        try { _web.Dispose(); } catch (Exception) { }
        _done.TrySetResult(_finished);
    }

    // The page is a window of its own, which would sit over a pop-up opened over this one.
    public override void OnCovered() => _web.Visibility = Visibility.Hidden;
    public override void OnResumed() => _web.Visibility = Visibility.Visible;

    public override void FocusDefault() => _web.Focus();

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Back) { Host.Pop(this); return true; }
        return false;
    }

    /// <summary>Where the page is: signing in still, or on the key page (read the key, or register one).</summary>
    private async Task CheckAsync()
    {
        if (_finished || _web.CoreWebView2 == null) return;
        var uri = _web.Source;
        if (uri == null) return;
        bool keyPage = uri.Host.EndsWith("steamcommunity.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith("/dev/apikey", StringComparison.OrdinalIgnoreCase);
        if (!keyPage)
        {
            if (uri.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
                _status.Text = "Waiting for you to scan the code (or sign in with your name and password)…";
            else if (uri.Host.EndsWith("steamcommunity.com", StringComparison.OrdinalIgnoreCase) && !_registering && !uri.AbsolutePath.StartsWith("/dev", StringComparison.OrdinalIgnoreCase))
                _web.CoreWebView2.Navigate(KeyUrl); // signed in, but Steam went elsewhere: on to the key page
            return;
        }

        string json;
        try
        {
            json = await _web.CoreWebView2.ExecuteScriptAsync(
                "(function(){var t=document.body?document.body.innerText:'';var m=t.match(/Key:\\s*([0-9A-Fa-f]{32})/);" +
                "return JSON.stringify({key:m?m[1]:'',form:!!document.querySelector(\"form[action*='registerkey'],form[action*='requestkey']\")," +
                "limited:/granted access to Steam Web API keys|limited user account/i.test(t),signin:!!document.querySelector(\"a[href*='/login']\")&&!/Key:/.test(t)," +
                "confirm:/confirm|mobile|Steam Guard/i.test(t)})})()");
        }
        catch (Exception) { return; }
        PageState? state;
        try { state = JsonSerializer.Deserialize<PageState>(JsonSerializer.Deserialize<string>(json) ?? "{}", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (Exception) { return; }
        if (state == null) return;

        if (state.Key.Length == 32)
        {
            _finished = true;
            _poll.Stop();
            Host.Settings.SteamWebApiKey = state.Key.ToUpperInvariant();
            Host.Settings.Save();
            UiSound.Select();
            Host.ShowToast("STEAM", "Signed in, key saved", "Friends and every game you own are on now.", "", "info");
            Host.Pop(this);
            return;
        }
        if (state.Limited)
        {
            _poll.Stop();
            _status.Text = "Steam only gives API keys to accounts that have spent at least $5 in its store. Once yours has, try again.";
            return;
        }
        if (state.Form && !_registering)
        {
            // No key yet: register one for "localhost" (any name works for a key used on this PC), agreeing to the terms.
            _registering = true;
            _registeredAt = DateTime.UtcNow;
            _status.Text = "Getting your key… If Steam asks, approve it in the Steam app on your phone.";
            try
            {
                await _web.CoreWebView2.ExecuteScriptAsync(
                    "(function(){var f=document.querySelector(\"form[action*='registerkey'],form[action*='requestkey']\");if(!f)return;" +
                    "var d=f.querySelector(\"input[name='domain']\");if(d)d.value='localhost';" +
                    "var a=f.querySelector(\"input[name='agreeToTerms']\");if(a)a.checked=true;" +
                    "var b=f.querySelector(\"input[type='submit'],button[type='submit'],#submitBtn\");if(b)b.click();else f.submit();})()");
            }
            catch (Exception) { }
            return;
        }
        if (_registering)
        {
            // Waiting for the key (or the phone's approval): look at the key page again now and then.
            _status.Text = state.Confirm
                ? "Approve the key in the Steam app on your phone (Steam Guard > confirmations). SquareDeck is waiting…"
                : "Getting your key…";
            if ((DateTime.UtcNow - _registeredAt).TotalSeconds > 6 && (DateTime.UtcNow - _registeredAt).TotalSeconds % 8 < 2.1)
                _web.CoreWebView2.Navigate(KeyUrl);
            if ((DateTime.UtcNow - _registeredAt).TotalMinutes > 4)
            {
                _poll.Stop();
                _status.Text = "Steam didn't give a key this time. Try again, or get one at " + KeyUrl + " and choose \"Enter my key\".";
            }
        }
    }

    private sealed class PageState
    {
        public string Key { get; set; } = "";
        public bool Form { get; set; }
        public bool Limited { get; set; }
        public bool Signin { get; set; }
        public bool Confirm { get; set; }
    }
}
