using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Experimental "About This Game": the game's store-page description as Steam shows it, with its artwork, animated
/// images and videos, under the game's banner and a strip of facts, followed by its screenshots. Drawn in a page of
/// its own, read with the controller: up / down (or the right stick) scroll, LT / RT a page at a time, B goes back.
/// Links open in SquareDeck's browser.
/// </summary>
public sealed class AboutGameScreen : Screen
{
    private readonly WebView2 _web = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1B, 0x28, 0x38) };
    private readonly StoreDetails _details;
    private readonly string _game;
    private static Task<CoreWebView2Environment>? _environment;
    private bool _ready;

    public AboutGameScreen(string game, StoreDetails details)
    {
        _game = game;
        _details = details;
        Focusable = true;
        var frame = new Border { CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 10)), ClipToBounds = true, Child = _web, Margin = new Thickness(0, 0, 0, 8) };
        frame.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x38));
        SetPage("About " + game, frame, "D-pad / right stick  Scroll      {LT}{RT}  Page up / down      {Back}  Back", wide: true);
    }

    public override async void OnShown()
    {
        base.OnShown();
        Focus();
        try
        {
            _environment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.DataDir, "Trailers"),
                new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            await _web.EnsureCoreWebView2Async(await _environment);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            // Links aren't followed in here: web links open in SquareDeck's browser instead.
            core.NavigationStarting += (_, e) =>
            {
                if (!_ready || e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
                e.Cancel = true;
                string url = e.Uri;
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) Dispatcher.BeginInvoke(new Action(() => Host.Push(BrowserScreen.Open(Host, url))));
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                string url = e.Uri;
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) Dispatcher.BeginInvoke(new Action(() => Host.Push(BrowserScreen.Open(Host, url))));
            };
            // The page never keeps the focus, so the controller keeps working here.
            _web.GotFocus += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Focus()));
            core.NavigateToString(Page());
            _ready = true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("About This Game", ex);
            await Host.AskAsync("About " + _game, "The page couldn't be shown: SquareDeck's built-in browser (WebView2) isn't available.", "OK");
            Host.Pop(this);
        }
    }

    public override void FocusDefault() => Focus();

    public override void OnClosed()
    {
        try { _web.Dispose(); } catch (Exception) { }
    }

    private void Scroll(string js)
    {
        if (!_ready) return;
        try { _ = _web.CoreWebView2?.ExecuteScriptAsync(js); } catch (Exception) { }
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Down: Scroll("window.scrollBy({top:160,behavior:'smooth'})"); return true;
            case NavAction.Up: Scroll("window.scrollBy({top:-160,behavior:'smooth'})"); return true;
            case NavAction.RT: Scroll("window.scrollBy({top:window.innerHeight*0.85,behavior:'smooth'})"); return true;
            case NavAction.LT: Scroll("window.scrollBy({top:-window.innerHeight*0.85,behavior:'smooth'})"); return true;
            case NavAction.Left or NavAction.Right or NavAction.Accept: return true;
            default: return false;
        }
    }

    public override bool OnRightStick(double y)
    {
        if (Math.Abs(y) < 0.15) return false;
        Scroll($"window.scrollBy(0,{(-y * 38).ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        return true;
    }

    // ---------------------------------------------------------------- the page

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private string Page()
    {
        var d = _details;
        var b = new StringBuilder();
        b.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width'><style>");
        b.Append(@"
html,body{margin:0;background:#1b2838;color:#acb2b8;font-family:'Motiva Sans','Segoe UI',Arial,sans-serif;font-size:15px;line-height:1.55}
::-webkit-scrollbar{width:8px}::-webkit-scrollbar-thumb{background:#3d4f63;border-radius:4px}::-webkit-scrollbar-track{background:transparent}
.wrap{max-width:1180px;margin:0 auto;padding:22px 28px 60px}
.banner{position:relative;border-radius:10px;overflow:hidden;margin-bottom:18px;box-shadow:0 8px 30px rgba(0,0,0,.45)}
.banner img{display:block;width:100%}
.banner .shade{position:absolute;inset:auto 0 0 0;padding:26px 24px 16px;background:linear-gradient(transparent,rgba(10,16,24,.92))}
.banner h1{margin:0;color:#fff;font-size:30px;font-weight:600;text-shadow:0 2px 8px rgba(0,0,0,.6)}
.grid{display:grid;grid-template-columns:minmax(0,1fr) 300px;gap:26px}
@media (max-width:980px){.grid{grid-template-columns:1fr}}
h2.section{color:#fff;font-size:15px;font-weight:400;text-transform:uppercase;letter-spacing:.06em;margin:0 0 12px;padding-bottom:6px;border-bottom:1px solid;border-image:linear-gradient(90deg,#3b6e8c,transparent) 1}
.about img,.about video{max-width:100%;height:auto;display:block;margin:12px 0;border-radius:4px}
.about h1,.about h2,.about .bb_tag{color:#fff;font-size:16px;font-weight:600;margin:22px 0 6px;text-transform:none}
.about ul{padding-left:20px}.about li{margin:3px 0}
.about a{color:#66c0f4;text-decoration:none}
.about strong,.about b{color:#d6d7d8}
.side{background:rgba(0,0,0,.2);border-radius:8px;padding:16px;align-self:start;position:sticky;top:16px}
.side .row{font-size:12px;color:#556772;text-transform:uppercase;margin:0 0 9px}
.side .row span{color:#acb2b8;text-transform:none;font-size:13px}
.side .desc{font-size:13px;color:#c6d4df;margin:0 0 14px}
.meta{display:inline-block;background:#4c6b22;color:#fff;font-weight:700;padding:2px 8px;border-radius:3px;font-size:13px}
.shots{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:10px;margin-top:10px}
.shots img{width:100%;border-radius:4px;display:block}
");
        b.Append("</style></head><body><div class='wrap'>");
        if (!string.IsNullOrEmpty(d.HeaderImage))
            b.Append("<div class='banner'><img src='").Append(E(d.HeaderImage)).Append("'><div class='shade'><h1>").Append(E(d.Name.Length > 0 ? d.Name : _game)).Append("</h1></div></div>");
        else b.Append("<h1 style='color:#fff'>").Append(E(_game)).Append("</h1>");
        b.Append("<div class='grid'><div><h2 class='section'>About This Game</h2><div class='about'>");
        b.Append(Clean(d.AboutHtml));
        b.Append("</div>");
        if (d.Screenshots.Count > 0)
        {
            b.Append("<h2 class='section' style='margin-top:30px'>Screenshots</h2><div class='shots'>");
            foreach (var s in d.Screenshots.Take(12)) b.Append("<img loading='lazy' src='").Append(E(s.Full)).Append("'>");
            b.Append("</div>");
        }
        b.Append("</div><div class='side'>");
        if (d.Description.Length > 0) b.Append("<p class='desc'>").Append(E(d.Description)).Append("</p>");
        void Row(string label, string value) { if (value.Length > 0) b.Append("<p class='row'>").Append(label).Append(": <span>").Append(E(value)).Append("</span></p>"); }
        Row("Genre", d.Genres);
        Row("Developer", d.Developers);
        Row("Publisher", d.Publishers);
        Row("Release date", d.ReleaseDate);
        if (d.AchievementTotal > 0) Row("Achievements", d.AchievementTotal.ToString());
        if (d.Metacritic is int mc) b.Append("<p class='row'>Metacritic: <span class='meta'>").Append(mc).Append("</span></p>");
        b.Append("</div></div></div>");
        // Moving images play by themselves, silently, like on the store page.
        b.Append("<script>document.querySelectorAll('video').forEach(function(v){v.muted=true;v.loop=true;v.autoplay=true;v.playsInline=true;v.removeAttribute('controls');v.play().catch(function(){});});</script>");
        b.Append("</body></html>");
        return b.ToString();
    }

    /// <summary>Steam's description HTML, minus scripts and inline handlers (the rest is shown as Steam wrote it).</summary>
    private static string Clean(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<script[\s\S]*?</script>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\son\w+\s*=\s*(""[^""]*""|'[^']*')", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Steam's own pictures sometimes come as protocol-relative or plain http links.
        return html.Replace("src=\"//", "src=\"https://").Replace("http://", "https://");
    }
}
