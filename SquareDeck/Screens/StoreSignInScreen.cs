using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Signs in to Epic Games or Microsoft (Xbox) on the store's own page, in SquareDeck (9.0.8). Epic's
/// page ends on a little page with a one-time code, which SquareDeck reads; Microsoft's ends on its "desktop" page
/// with a one-time code (or the sign-in) in the address. Sign-ins are kept in SquareDeck's own folder, apart from Edge.
/// </summary>
public sealed class StoreSignInScreen : Screen
{
    private readonly string _store;
    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Microsoft.Web.WebView2.Wpf.WebView2 _web = new();
    private readonly TextBlock _status = Ui.Text("Opening the sign-in page…", 16, "SubtleBrush");
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private static Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment>? _environment;
    private bool _finished, _working, _failed;

    public Task<bool> Result => _done.Task;

    public StoreSignInScreen(string store)
    {
        _store = store;
        string title = store == "Epic" ? "Sign in to Epic Games" : "Sign in with Microsoft (Xbox)";
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        var words = new StackPanel { Margin = new Thickness(14, 4, 14, 14) };
        var how = Ui.Text(store == "Epic"
            ? "Sign in on Epic's own page. SquareDeck then shows every game on your Epic account in the library, installed or not. Installing and playing still go through the Epic Games Launcher."
            : "Sign in with the Microsoft account you use for Xbox. SquareDeck then shows your PC games (and, if you like, all of PC Game Pass) in the library. Installing goes through the Xbox app.",
            16, "SubtleBrush");
        how.TextWrapping = TextWrapping.Wrap;
        how.Margin = new Thickness(0, 0, 0, 6);
        words.Children.Add(how);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        words.Children.Add(_status);
        body.Children.Add(words);
        // The store's page sits in a card of the style's own, with the style's rim round it.
        _frame = new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, BorderThickness = new Thickness(1), Margin = new Thickness(14, 0, 14, 4), Child = _web };
        Grid.SetRow(_frame, 1);
        body.Children.Add(_frame);
        SetPage(title, body, "{Back}  Cancel");
        UseSwitch2Chrome(title, "\uE77B");
        _frame.Background = new SolidColorBrush(Switch2Settings.Card);
        _frame.BorderBrush = new SolidColorBrush(Switch2Settings.Line);
        var bg = Switch2Settings.Card;
        _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(bg.R, bg.G, bg.B);
        _poll.Tick += async (_, _) => await CheckAsync();
    }

    private readonly Border _frame;

    public override async void OnShown()
    {
        try
        {
            _environment ??= Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.DataDir, "StoreSignIn"));
            await _web.EnsureCoreWebView2Async(await _environment);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            try { core.Profile.PreferredColorScheme = Theme.CurrentIsDark ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light; } catch (Exception) { }
            core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); }; // "sign in with…" pop-ups open here
            core.NavigationStarting += async (_, e) =>
            {
                // Microsoft hands the sign-in back in the address of its desktop page: read it there.
                if (_store == "Xbox" && Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && StoreAccounts.IsXboxAnswer(uri))
                {
                    e.Cancel = true;
                    await FinishXboxAsync(uri);
                }
            };
            _web.NavigationCompleted += async (_, _) => await CheckAsync();
            core.Navigate(_store == "Epic" ? StoreAccounts.EpicLoginUrl : StoreAccounts.XboxLoginUrl);
            _poll.Start();
        }
        catch (Exception)
        {
            _status.Text = "Signing in needs Microsoft Edge WebView2, which isn't available on this PC.";
        }
    }

    public override void OnClosed()
    {
        _poll.Stop();
        try { _web.Dispose(); } catch (Exception) { }
        _done.TrySetResult(_finished);
    }

    public override void OnCovered() => _web.Visibility = Visibility.Hidden;
    public override void OnResumed() => _web.Visibility = Visibility.Visible;
    public override void FocusDefault() => _web.Focus();

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Back) { Host.Pop(this); return true; }
        return false;
    }

    private async Task CheckAsync()
    {
        if (_finished || _failed || _working || _web.CoreWebView2 == null || _web.Source is not { } uri) return;
        if (_store == "Xbox")
        {
            if (StoreAccounts.IsXboxAnswer(uri)) await FinishXboxAsync(uri);
            else _status.Text = "Waiting for you to sign in…";
            return;
        }
        // Epic: the redirect page shows a little JSON with the code.
        if (!uri.AbsolutePath.Contains("/id/api/redirect", StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = "Waiting for you to sign in…";
            return;
        }
        string code = "";
        try
        {
            var text = JsonSerializer.Deserialize<string>(await _web.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerText : ''")) ?? "";
            int start = text.IndexOf('{');
            if (start >= 0)
            {
                using var doc = JsonDocument.Parse(text[start..]);
                if (doc.RootElement.TryGetProperty("authorizationCode", out var c) && c.ValueKind == JsonValueKind.String) code = c.GetString() ?? "";
            }
        }
        catch (Exception) { }
        if (code.Length == 0) { _status.Text = "Almost there… if Epic asks anything else, answer it on the page."; return; }
        _working = true;
        _status.Text = "Signing in…";
        var error = await Task.Run(() => StoreAccounts.EpicSignInAsync(code));
        _working = false;
        Done(error);
    }

    private async Task FinishXboxAsync(Uri uri)
    {
        if (_finished || _failed || _working) return;
        _working = true;
        _status.Text = "Signed in to Microsoft. Connecting to Xbox Live…";
        var error = await Task.Run(() => StoreAccounts.XboxSignInAsync(uri));
        _working = false;
        Done(error);
    }

    private void Done(string? error)
    {
        if (error != null)
        {
            // Don't leave the page sitting on a half-finished sign-in: say what went wrong, plainly, where the page was.
            _failed = true;
            _status.Text = error;
            _poll.Stop();
            UiSound.Back();
            Host.ShowToast(_store == "Epic" ? "EPIC GAMES" : "XBOX", "Sign-in didn't finish", error, "", "info");
            try
            {
                string safe = System.Net.WebUtility.HtmlEncode(error);
                static string Css(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                string page = "<html><body style='background:" + Css(Switch2Settings.Card) + ";color:" + Css(Switch2Settings.Text)
                    + ";font-family:Segoe UI Variable Display,Segoe UI,sans-serif;display:flex;align-items:center;justify-content:center;height:90vh;margin:0'>"
                    + "<div style='max-width:640px;text-align:center'><div style='width:44px;height:4px;border-radius:2px;margin:0 auto 18px;background:" + Css(Switch2Settings.Blue) + "'></div>"
                    + "<div style='font-size:28px;font-weight:600;margin-bottom:14px'>Sign-in didn't finish</div>"
                    + $"<div style='font-size:18px;color:{Css(Switch2Settings.Subtle)};line-height:1.5'>{safe}</div>"
                    + $"<div style='font-size:15px;color:{Css(Switch2Settings.Subtle)};opacity:.75;margin-top:22px'>Press B to go back and try again.</div></div></body></html>";
                _web.CoreWebView2?.NavigateToString(page);
            }
            catch (Exception) { }
            return;
        }
        _finished = true;
        _poll.Stop();
        UiSound.Select();
        Host.ShowToast(_store == "Epic" ? "EPIC GAMES" : "XBOX", "Signed in", "Your games are being added to the library.", "", "info");
        Host.Pop(this);
    }
}
