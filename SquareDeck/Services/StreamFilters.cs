using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace SquareDeck.Services;

/// <summary>
/// Experimental "Streaming service filters" (8.5.89): the streaming services (Netflix, Pluto TV, Hulu, Prime Video,
/// DeckTube and the rest) look like an old TV. It's drawn inside the page itself, so it covers the video, full screen
/// too: a CRT with scanlines, a phosphor mask and a curved glass edge; a worn VHS tape with noise, a rolling tracking
/// band and the VCR's on-screen "PLAY"; a black-and-white set; old film with scratches and dust; or a late-night
/// cable channel. It can switch on like an old set, with the line opening up from the middle.
///
/// 8.5.91: the page gets the drawing code once and the look as settings, so a change of look, strength or the switch
/// itself reaches a service that's already open (they stay open in the background) at once, and every later page in it.
/// </summary>
public static class StreamFilters
{
    public const string Lab = "streamfx";

    public static readonly (string Id, string Name, string About)[] Looks =
    {
        ("crt", "CRT television", "Scanlines, a phosphor mask, a soft glow and the curved glass of a tube TV"),
        ("vhs", "VHS tape", "Washed-out colors, tape noise, a rolling tracking band and the VCR's PLAY and clock on screen"),
        ("bw", "Black and white", "A black-and-white set: grain, a dark vignette and the odd flicker"),
        ("film", "Old film", "Sepia, scratches and dust, the frame weaving gently in the gate"),
        ("cable", "Late-night cable", "Warm, faded colors and a little bloom, like a channel at 2 a.m."),
    };

    public static readonly (string Id, string Name)[] Strengths = { ("subtle", "Subtle"), ("normal", "Normal"), ("strong", "Strong") };

    public static bool On => Labs.On(Lab);
    public static string Look => Labs.Get("streamfx.look", "crt");
    public static string Strength => Labs.Get("streamfx.strength", "normal");
    public static bool SwitchOn => Labs.Get("streamfx.poweron", "1") == "1";
    public static bool VcrText => Labs.Get("streamfx.osd", "1") == "1";
    /// <summary>Music services too (they're mostly not video, so off by default).</summary>
    public static bool OnMusic => Labs.Get("streamfx.music", "0") == "1";

    public static bool AppliesTo(bool music) => On && (!music || OnMusic);

    /// <summary>A setting changed: the open services take it at once.</summary>
    public static event Action? Changed;

    static StreamFilters()
    {
        Labs.Changed += id => { if (id == Lab) Notify(); };
    }

    public static void Notify() => Changed?.Invoke();

    private static double D(double v) => Math.Round(v, 3);

    /// <summary>The look as settings for the page's drawing code, or "null" for none.</summary>
    public static string Config(bool music)
    {
        if (!AppliesTo(music)) return "null";
        string look = Look;
        double k = Strength switch { "subtle" => 0.55, "strong" => 1.5, _ => 1.0 };
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string filter = look switch
        {
            "vhs" => $"saturate({F(1 + 0.35 * k)}) contrast({F(1 + 0.06 * k)}) brightness({F(1 + 0.03 * k)}) blur({F(0.45 * k)}px) hue-rotate(-6deg)",
            "bw" => $"grayscale(1) contrast({F(1 + 0.25 * k)}) brightness({F(1 - 0.05 * k)}) blur({F(0.25 * k)}px)",
            "film" => $"sepia({F(Math.Min(1, 0.55 * k))}) contrast({F(1 + 0.12 * k)}) brightness({F(1 - 0.04 * k)}) saturate({F(1 - 0.2 * k)})",
            "cable" => $"sepia({F(0.22 * k)}) saturate({F(1 - 0.12 * k)}) contrast({F(1 - 0.06 * k)}) brightness({F(1 + 0.05 * k)}) blur({F(0.3 * k)}px)",
            _ => $"contrast({F(1 + 0.12 * k)}) saturate({F(1 + 0.18 * k)}) brightness({F(1 + 0.04 * k)}) blur({F(0.3 * k)}px)",
        };
        var c = new JsonObject
        {
            ["look"] = look,
            ["filter"] = filter,
            // Scanline darkness, the phosphor mask, the vignette, the grain and the flicker, each 0 to 1.
            ["scan"] = D(look switch { "crt" => 0.30, "bw" => 0.22, "vhs" => 0.12, "cable" => 0.08, _ => 0 } * k),
            ["mask"] = D(look == "crt" ? 0.7 * k : 0),
            ["vignette"] = D(look switch { "film" => 0.75, "bw" => 0.7, "crt" => 0.6, "cable" => 0.4, _ => 0.45 } * Math.Min(1.3, k)),
            ["grain"] = D(look switch { "vhs" => 0.16, "film" => 0.14, "bw" => 0.12, "cable" => 0.06, _ => 0.04 } * k),
            ["flicker"] = D(look switch { "bw" => 0.06, "film" => 0.05, "crt" => 0.025, _ => 0.015 } * k),
            ["curve"] = look is "crt" or "bw" ? (int)(26 * Math.Min(1.4, k)) : look == "film" ? 10 : 0,
            ["tint"] = look switch { "cable" => "rgba(255,170,90,0.07)", "film" => "rgba(120,80,20,0.07)", "vhs" => "rgba(80,0,140,0.05)", _ => "transparent" },
            ["tracking"] = look == "vhs",
            ["scratches"] = look == "film",
            ["osd"] = look == "vhs" && VcrText,
            ["weave"] = look == "film" && k > 1.2,
            ["power"] = SwitchOn,
        };
        return c.ToJsonString();
    }

    /// <summary>
    /// Sets a streaming page up for the filters: the drawing code for every page it opens, with the look in use, kept
    /// up to date while it's open. <paramref name="alive"/> says whether the page is still there.
    /// </summary>
    public static async Task AttachAsync(CoreWebView2 core, bool music, Func<bool> alive)
    {
        string? id = null;
        async Task Register()
        {
            if (id != null) core.RemoveScriptToExecuteOnDocumentCreated(id);
            id = await core.AddScriptToExecuteOnDocumentCreatedAsync(Engine + "\nwindow.__sdTvApply(" + Config(music) + ");");
        }
        await Register();
        Action? changed = null;
        changed = () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (!alive()) { Changed -= changed; return; }
            try
            {
                await Register();
                await core.ExecuteScriptAsync(Engine + "\nwindow.__sdTvApply(" + Config(music) + ");"); // the page open now
            }
            catch (Exception) { Changed -= changed; }
        }));
        Changed += changed;
    }

    /// <summary>
    /// The drawing code: defines window.__sdTvApply(settings) in the top frame, which (re)draws the TV over the page, or
    /// takes it away for null. The grain is a fine noise texture made once and shifted about, not redrawn, so it stays
    /// cheap and sharp.
    /// </summary>
    public const string Engine = """
        (() => {
          if (window !== window.top || window.__sdTvApply) return;
          let tv = null, style = null, timers = [], tiles = null, first = true;
          const clear = () => { timers.forEach(t => clearInterval(t)); timers = []; if (tv) tv.remove(); if (style) style.remove(); tv = style = null; };
          const noise = () => {
            const c = document.createElement('canvas'); c.width = c.height = 160;
            const g = c.getContext('2d'), img = g.createImageData(160, 160), d = img.data;
            for (let i = 0; i < d.length; i += 4) { const v = Math.random() * 255 | 0; d[i] = d[i + 1] = d[i + 2] = v; d[i + 3] = 255; }
            g.putImageData(img, 0, 0);
            return c.toDataURL();
          };
          const build = (c) => {
            clear();
            if (!c || !document.documentElement) return;
            if (!tiles) tiles = [noise(), noise(), noise()];
            style = document.createElement('style');
            style.textContent = `
              body, :fullscreen { filter: ${c.filter} !important; }
              ${c.weave ? 'body { animation: sdWeave 0.9s steps(6) infinite; } @keyframes sdWeave { 0%,100% { transform: translate(0,0); } 30% { transform: translate(0.6px,-0.8px); } 60% { transform: translate(-0.5px,0.5px); } }' : ''}
              #sd-tv { position: fixed; inset: 0; pointer-events: none; z-index: 2147483647; overflow: hidden; border-radius: ${c.curve}px; contain: strict; }
              #sd-tv > div { position: absolute; inset: 0; }
              #sd-tv .tint { background: ${c.tint}; mix-blend-mode: soft-light; }
              #sd-tv .grain { inset: -160px; background-repeat: repeat; opacity: ${c.grain}; mix-blend-mode: overlay; will-change: transform; }
              #sd-tv .scan { background: repeating-linear-gradient(0deg, rgba(0,0,0,${c.scan}) 0px, rgba(0,0,0,${c.scan}) 1px, transparent 1px, transparent 3px); }
              #sd-tv .mask { opacity: ${c.mask}; background: repeating-linear-gradient(90deg, rgba(255,0,0,0.07) 0px, rgba(0,255,0,0.07) 1px, rgba(0,0,255,0.07) 2px, transparent 3px); }
              #sd-tv .vig { border-radius: ${c.curve}px; background: radial-gradient(ellipse at center, transparent 55%, rgba(0,0,0,${c.vignette}) 100%);
                            box-shadow: inset 0 0 ${c.curve * 3}px rgba(0,0,0,${Math.min(0.9, c.vignette)}); }
              #sd-tv .flick { background: #fff; opacity: 0; animation: sdFlick 0.13s steps(2) infinite; }
              @keyframes sdFlick { 0% { opacity: 0; } 50% { opacity: ${c.flicker}; } 100% { opacity: 0; } }
              #sd-tv .track { bottom: auto; height: 9%; top: -10%; filter: blur(1px); animation: sdTrack 9s linear infinite;
                              background: linear-gradient(180deg, transparent, rgba(255,255,255,0.10) 40%, rgba(255,255,255,0.18) 50%, rgba(255,255,255,0.10) 60%, transparent); }
              @keyframes sdTrack { 0% { top: -10%; } 70% { top: 105%; } 100% { top: 105%; } }
              #sd-tv .scratch { left: 50%; right: auto; width: 1px; background: rgba(255,255,240,0.35); opacity: 0; }
              #sd-tv .osd { inset: auto; color: #f4f4f4; font: bold 30px/1 'Consolas', 'Courier New', monospace; letter-spacing: 2px;
                            text-shadow: 2px 0 rgba(255,0,80,0.6), -2px 0 rgba(0,200,255,0.6), 0 0 6px rgba(255,255,255,0.5); }
              #sd-tv .osd.play { top: 5%; left: 5%; animation: sdOsd 6s forwards; }
              #sd-tv .osd.clock { bottom: 6%; left: 5%; font-size: 24px; }
              #sd-tv .osd.sp { top: 5%; right: 5%; font-size: 24px; }
              @keyframes sdOsd { 0%, 85% { opacity: 1; } 100% { opacity: 0; } }
              #sd-on { position: fixed; inset: 0; z-index: 2147483647; pointer-events: none; background: #000; display: flex; align-items: center; justify-content: center; }
              #sd-on i { display: block; width: 100%; height: 2px; background: #fff; box-shadow: 0 0 30px 8px rgba(255,255,255,0.9); animation: sdOn 0.75s cubic-bezier(.2,.8,.2,1) forwards; }
              @keyframes sdOn { 0% { transform: scale(0.02, 1); } 45% { transform: scale(1, 1); } 100% { transform: scale(1, 600); opacity: 0; } }
              #sd-on.done { animation: sdFade 0.35s forwards; }
              @keyframes sdFade { to { opacity: 0; } }
            `;
            (document.head || document.documentElement).appendChild(style);
            tv = document.createElement('div');
            tv.id = 'sd-tv';
            // Built element by element (some services refuse innerHTML under their Trusted Types policy).
            const el = (cls, text) => { const d = document.createElement('div'); d.className = cls; if (text) d.textContent = text; tv.appendChild(d); return d; };
            el('tint');
            const grain = el('grain');
            el('scan'); el('mask');
            if (c.tracking) el('track');
            const scratches = c.scratches ? [el('scratch'), el('scratch')] : [];
            el('flick'); el('vig');
            if (c.osd) { el('osd play', 'PLAY ▶'); el('osd sp', 'SP'); el('osd clock'); }
            (document.fullscreenElement || document.documentElement).appendChild(tv);
            // Grain: one of three noise textures, moved to a random spot about 12 times a second.
            let n = 0;
            if (c.grain > 0) timers.push(setInterval(() => {
              n = (n + 1) % tiles.length;
              grain.style.backgroundImage = 'url(' + tiles[n] + ')';
              grain.style.transform = 'translate(' + (Math.random() * 160 | 0) + 'px,' + (Math.random() * 160 | 0) + 'px)';
            }, 85));
            scratches.forEach(s => timers.push(setInterval(() => {
              s.style.opacity = Math.random() < 0.35 ? (0.3 + Math.random() * 0.6) : 0;
              s.style.left = (Math.random() * 100) + '%';
            }, 120 + Math.random() * 200)));
            const clock = tv.querySelector('.osd.clock');
            if (clock) {
              const tick = () => {
                const d = new Date(), m = ['JAN','FEB','MAR','APR','MAY','JUN','JUL','AUG','SEP','OCT','NOV','DEC'][d.getMonth()];
                let h = d.getHours(); const ap = h >= 12 ? 'PM' : 'AM'; h = h % 12 || 12;
                clock.textContent = ap + ' ' + h + ':' + String(d.getMinutes()).padStart(2, '0') + '  ' + m + '. ' + d.getDate() + ' ' + d.getFullYear();
              };
              tick(); timers.push(setInterval(tick, 10000));
            }
            // The set switching on: a bright line opening up from the middle (once, as the service opens).
            if (c.power && first && !sessionStorage.getItem('sdTvOn')) {
              sessionStorage.setItem('sdTvOn', '1');
              const on = document.createElement('div');
              on.id = 'sd-on';
              on.appendChild(document.createElement('i'));
              document.documentElement.appendChild(on);
              setTimeout(() => on.classList.add('done'), 700);
              setTimeout(() => on.remove(), 1100);
            }
            first = false;
          };
          // Full screen: only the full-screen element is shown, so the TV goes inside it, and back out after.
          document.addEventListener('fullscreenchange', () => {
            if (!tv) return;
            const host = document.fullscreenElement || document.documentElement;
            if (tv.parentNode !== host) host.appendChild(tv);
          });
          let pending = null;
          window.__sdTvApply = (c) => {
            if (document.documentElement && document.readyState !== 'loading') { build(c); return; }
            pending = c;
            if (document.documentElement) build(c);
            document.addEventListener('DOMContentLoaded', () => build(pending), { once: true });
          };
        })();
        """;
}
