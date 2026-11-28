using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// The Game Bar widget's experimental cards (8.1): frame cap and Auto TDP in the Power card, the thermal warning,
/// game notes, achievements, Ask Genie, the controller, friends, notifications, downloads, invite, apps and
/// SquareDeck shortcuts, the card editor with layouts and presets per game, the style match and the companion.
/// SquareDeck sends what each needs in the widget's state and answers its requests; each is off until it's turned
/// on in Settings > Experimental.
/// </summary>
public partial class MainWindow
{
    private static readonly string[] WidgetLabs =
    {
        "wfpscap", "wautotdp", "wthermal", "wnotes", "wapps", "wcards", "wgameaware", "wpresets", "wtheme", "wcompanion", "wpowerlook", "ws2", "wfpsslider", "wfavorites",
    };

    /// <summary>Every card the widget has, in its usual order (the editor's list).</summary>
    private static readonly string[] WidgetCardIds =
    {
        "game", "media", "power", "live", "ally", "notes", "launch", "favorites", "quick",
    };

    private int _widgetCap;                    // the frame cap the widget set for this session (0: none)

    // ================================================================ the state

    private void AddWidgetEight(JsonObject state)
    {
        var on = WidgetLabs.Where(Labs.On).ToList();
        state["labs"] = new JsonArray(on.Select(x => (JsonNode)x).ToArray());
        if (on.Count == 0) return;
        if (Labs.On("ws2"))
        {
            // Experimental: the Switch 2 style widget, in the colors of the UI style in use.
            SquareDeck.Screens.Switch2Settings.Refresh();
            static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            state["s2"] = new JsonObject
            {
                ["bg"] = Hex(SquareDeck.Screens.Switch2Settings.Background), ["fg"] = Hex(SquareDeck.Screens.Switch2Settings.Text),
                ["sub"] = Hex(SquareDeck.Screens.Switch2Settings.Subtle), ["line"] = Hex(SquareDeck.Screens.Switch2Settings.Line),
                ["accent"] = Hex(SquareDeck.Screens.Switch2Settings.Blue), ["card"] = Hex(SquareDeck.Screens.Switch2Settings.Card),
            };
        }
        var running = AllGames.FirstOrDefault(g => g.IsRunning);
        string? key = running?.Key;

        // Layout: card order and hidden cards, then Live tiles (a game's preset, else its own layout, else yours).
        var preset = key != null && Labs.On("wpresets") ? WidgetPreset(key) : null;
        string? order = preset?["order"]?.GetValue<string>() ?? (key != null && Labs.On("wgameaware") ? NullIfEmpty(Labs.Get("w.order:" + key)) : null) ?? NullIfEmpty(Labs.Get("w.order"));
        string? hidden = preset?["hidden"]?.GetValue<string>() ?? (key != null && Labs.On("wgameaware") && Labs.Has("w.hidden:" + key) ? Labs.Get("w.hidden:" + key) : null) ?? Labs.Get("w.hidden");
        string? live = preset?["live"]?.GetValue<string>() ?? (key != null && Labs.On("wgameaware") ? NullIfEmpty(Labs.Get("w.live:" + key)) : null) ?? NullIfEmpty(Labs.Get("w.live"));
        if (Labs.On("wcards") || Labs.On("wgameaware") || Labs.On("wpresets"))
        {
            var ids = (order ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Where(WidgetCardIds.Contains).ToList();
            foreach (var id in WidgetCardIds) if (!ids.Contains(id)) ids.Add(id);
            state["layout"] = new JsonObject
            {
                ["order"] = new JsonArray(ids.Select(x => (JsonNode)x).ToArray()),
                ["hidden"] = new JsonArray((hidden ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => (JsonNode)x).ToArray()),
                ["preset"] = preset != null,
                ["game"] = running?.Name,
            };
        }
        if (live != null) state["live"] = live;

        if (Labs.On("wfpscap") || Labs.On("wautotdp") || Labs.On("wfpsslider"))
        {
            state["fpscap"] = _widgetCap > 0 ? _widgetCap : (_capBefore != null && key != null ? Eight.FpsCap(key) : 0);
            state["capok"] = AmdAdlx.CapMethod() != null;
        }
        if (Labs.On("wautotdp"))
            state["autotdp"] = new JsonObject { ["on"] = _autoTdpOn, ["target"] = _autoTdpTarget, ["status"] = _autoTdpStatus };
        if (Labs.On("wpowerlook")) state["powerlook"] = Labs.Has("w.powerlook") ? Labs.Get("w.powerlook") : "dial";
        if (Labs.On("wthermal")) state["templimit"] = Settings.TdpTempLimit > 0 ? Settings.TdpTempLimit : 95;
        if (Labs.On("wnotes") && key != null) state["notes"] = Library.Find(key)?.Notes ?? "";
        if (Labs.On("wfavorites"))
            state["favkey"] = string.Join("|", FavoriteGames().Select(g => g.Key + (g.IsRunning ? "*" : "") + (g.Cover != null ? "c" : "")));
        if (Labs.On("wapps")) state["appskey"] = string.Join("|", _apps.OrderByDescending(a => a.RecentAt).Take(6).Select(a => a.LaunchId));
        if (Labs.On("wtheme")) state["themekey"] = UiStyles.Current.Id + "|" + WidgetAccent() + "|" + Settings.Wallpaper;
        if (Labs.On("wcompanion"))
        {
            var (mood, says) = WidgetCompanionMood(running);
            state["companion"] = new JsonObject { ["mood"] = mood, ["says"] = says, ["name"] = CompanionArt.Name, ["key"] = CompanionArt.SpeciesId + "|" + CompanionArt.ColorHex + "|" + Labs.Outfit + "|" + mood };
        }
        AutoTdpTick(running);
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static JsonObject? WidgetPreset(string key)
    {
        try { return Labs.Get("wp:" + key) is { Length: > 0 } json ? JsonNode.Parse(json) as JsonObject : null; }
        catch (Exception) { return null; }
    }

    /// <summary>A running game's preset Quick tiles, when it has a preset with them.</summary>
    private List<string>? WidgetPresetQuick()
    {
        if (!Labs.On("wpresets") || AllGames.FirstOrDefault(g => g.IsRunning) is not { } running) return null;
        var q = WidgetPreset(running.Key)?["quick"]?.GetValue<string>();
        return string.IsNullOrEmpty(q) ? null : q.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private (string Mood, string Says) WidgetCompanionMood(Game? running)
    {
        if (Achievements.Feed.Count > 0 && DateTime.Now - Achievements.Feed.Max(f => f.When) < TimeSpan.FromMinutes(10)) return ("cheer", "Nice one! An achievement!");
        if (!Battery.IsPluggedIn() && Battery.GetPercent() is int b && b <= 20) return ("worried", $"Battery's at {b}%…");
        if (running != null && RunningMinutes(running) > 90) return ("sleepy", "Long session! Maybe a break soon?");
        if (running != null) return ("happy", "Have fun!");
        return ("happy", "");
    }

    // ================================================================ requests

    private bool WidgetEightRequest(string op, JsonObject request)
    {
        string? Str(string name) { try { return request[name]?.GetValue<string>(); } catch (Exception) { return null; } }
        int? Int(string name) { try { return request[name]?.GetValue<int>(); } catch (Exception) { try { return (int?)request[name]?.GetValue<double>(); } catch (Exception) { return null; } } }
        bool? Bool(string name) { try { return request[name]?.GetValue<bool>(); } catch (Exception) { return null; } }
        var running = AllGames.FirstOrDefault(g => g.IsRunning);
        switch (op)
        {
            case "fpscap" when Int("value") is int fps:
                _ = SetWidgetCapAsync(fps);
                // The slider has no Save button: the cap is kept for the game as you set it.
                if (Labs.On("wfpsslider") && running != null)
                {
                    Eight.SetFpsChoice(running.Key, fps > 0 ? fps.ToString() : "off");
                    if (!Labs.On("fpscap")) Labs.Set("fpscap", true);
                }
                return true;
            case "fpscapsave":
                if (running == null) { WidgetNote("Start a game first"); return true; }
                Eight.SetFpsChoice(running.Key, _widgetCap > 0 ? _widgetCap.ToString() : "off");
                if (!Labs.On("fpscap")) Labs.Set("fpscap", true);
                WidgetNote(_widgetCap > 0 ? $"{running.Name}: {_widgetCap} FPS from now on" : running.Name + ": no cap from now on");
                return true;
            case "autotdp":
                if (Bool("on") is bool turn) _autoTdpOn = turn;
                if (Int("target") is int target && target is >= 20 and <= 144) _autoTdpTarget = target;
                if (!_autoTdpOn) StopAutoTdp();
                WidgetBridge.Nudge();
                return true;
            case "noteadd" when Str("text") is { Length: > 0 } note && running != null:
            {
                var entry = Library.For(running.Key);
                string line = "• " + note.Trim() + "  (" + DateTime.Now.ToString("MMM d, h:mm tt") + ")";
                entry.Notes = string.IsNullOrWhiteSpace(entry.Notes) ? line : entry.Notes!.TrimEnd() + "\n" + line;
                Library.Save();
                WidgetNote("Note saved");
                return true;
            }
            case "apps":
                _ = SendWidgetAppsAsync();
                return true;
            case "favorites":
                _ = SendWidgetFavoritesAsync();
                return true;
            case "favlaunch" when Str("key") is { Length: > 0 } favKey:
                _ = LaunchFavoriteFromWidgetAsync(favKey, Bool("stop") == true);
                return true;
            case "launch" when Str("id") is { Length: > 0 } app:
                if (!InstalledApps.Launch(app)) WidgetNote("Couldn't start it");
                return true;
            case "layout":
                SaveWidgetLayout(running, Str("order") ?? "", Str("hidden") ?? "");
                return true;
            case "live" when Str("ids") is { Length: > 0 } ids:
                Labs.Put("w.live", ids);
                if (running != null && Labs.On("wgameaware")) Labs.Put("w.live:" + running.Key, ids);
                return true;
            case "presetsave":
                if (running == null) { WidgetNote("Start a game first"); return true; }
                var preset = new JsonObject
                {
                    ["order"] = Str("order") ?? "", ["hidden"] = Str("hidden") ?? "", ["live"] = Str("live") ?? "",
                    ["quick"] = string.Join(",", RunningProfile()?.WidgetQuick ?? Settings.WidgetQuick ?? Settings.WidgetQuickTiles.Select(t => t.Id).ToList()),
                };
                Labs.Put("wp:" + running.Key, preset.ToJsonString());
                WidgetNote("Saved to " + running.Name);
                return true;
            case "presetclear":
                if (running != null && Settings.LabValues.Remove("wp:" + running.Key)) { Settings.Save(); WidgetNote("Preset removed"); }
                return true;
            case "theme":
                SendWidgetTheme();
                return true;
            case "companion":
                SendWidgetCompanion(Str("mood") ?? "happy");
                return true;
        }
        return false;
    }

    private static void WidgetNote(string text) => WidgetBridge.Send(new JsonObject { ["type"] = "note", ["text"] = text });

    // ---- frame cap

    private async Task SetWidgetCapAsync(int fps)
    {
        if (fps <= 0)
        {
            _widgetCap = 0;
            if (_capBefore is { } before) { _capBefore = null; _capGame = null; await Task.Run(() => AmdAdlx.RestoreCap(before)); }
            WidgetNote("Frame cap off");
        }
        else
        {
            // Only for a game: set with no game running, nothing would ever lift it, and AMD's driver would keep it
            // for everything after.
            if (!AllGames.Any(g => g.IsRunning)) { WidgetNote("Start a game first"); WidgetBridge.Nudge(); return; }
            fps = Math.Max(LowestCap, fps);
            var before = await Task.Run(() => AmdAdlx.ApplyCap(fps));
            if (before == null) { WidgetNote("No AMD frame limiter on this PC"); return; }
            _capBefore ??= before; // keep what was there before the first cap, to put back
            _capGame = AllGames.FirstOrDefault(g => g.IsRunning)?.Key;
            _widgetCap = fps;
            WidgetNote(fps + " FPS cap");
        }
        WidgetBridge.Nudge();
    }

    // ---- Auto TDP

    private bool _autoTdpOn, _autoTdpCounting;
    private int _autoTdpTarget = 40;
    private string _autoTdpStatus = "";
    private DateTime _autoTdpAt;
    private int _autoTdpSteady; // three-second checks in a row on target at the same power

    /// <summary>
    /// Every 3 seconds while it's on and a game runs: one watt up when the frame rate falls short of the target, one
    /// down when there's headroom (two when far off), within the chip's range.
    /// </summary>
    private void AutoTdpTick(Game? running)
    {
        if (!_autoTdpOn || !Labs.On("wautotdp")) { if (_autoTdpCounting) StopAutoTdp(); return; }
        if (running == null) { _autoTdpStatus = "Waiting for a game"; return; }
        if (!TdpControl.Ready || TdpControl.Chip is not { } chip) { _autoTdpStatus = "Needs TDP control (Settings > Power)"; return; }
        if (!FrameCounter.Available) { _autoTdpStatus = "Needs the frame counter (Settings > Performance overlay)"; return; }
        if (!_autoTdpCounting)
        {
            _autoTdpCounting = true;
            _ = Task.Run(FrameCounter.Start);
            _autoTdpSteady = 0;
            // Remembered from last time (Settings > Power > Auto TDP learns each game): start where it held the frame rate.
            if (Settings.AutoTdpLearn && int.TryParse(Labs.Get("autotdp.w:" + running.Key + ":" + _autoTdpTarget), out int learned) && learned > 0)
            {
                TdpControl.SetSessionWatts(Math.Clamp(learned, Math.Max(chip.Min, 8), chip.Max));
                _autoTdpAt = DateTime.UtcNow; // give it a moment before judging
            }
        }
        FrameCounter.Retry(); // PresentMon quit or wasn't allowed at first: try again now and then
        // Paused by SquareDeck: no frames to judge, so leave the power where it is.
        if (IsSuspended(running)) { _autoTdpSteady = 0; _autoTdpAt = DateTime.UtcNow; return; }
        var (pid, _) = PerfOverlay.ForegroundApp();
        if (pid != 0 && pid != Environment.ProcessId) FrameCounter.TargetPid = pid;
        if (DateTime.UtcNow - _autoTdpAt < TimeSpan.FromSeconds(3)) return;
        _autoTdpAt = DateTime.UtcNow;
        if (FrameCounter.Fps is not double fps) { _autoTdpStatus = "Reading the frame rate…"; return; }
        int watts = TdpControl.EffectiveWatts, next = watts;
        if (fps < _autoTdpTarget - 2) next += fps < _autoTdpTarget - 8 ? 2 : 1;
        else if (fps > _autoTdpTarget + 4) next -= fps > _autoTdpTarget + 12 ? 2 : 1;
        // Never below 8 W (or the chip's own minimum, if higher): under that the whole device crawls, menus included.
        next = Math.Clamp(next, Math.Max(chip.Min, 8), chip.Max);
        if (next != watts) TdpControl.SetSessionWatts(next);
        _autoTdpStatus = $"{fps:0} FPS at {next} W";
        // Learning: a full minute holding the target at the same power is remembered for this game and target.
        if (Settings.AutoTdpLearn)
        {
            bool onTarget = fps >= _autoTdpTarget - 2 && fps <= _autoTdpTarget + 4 && next == watts;
            _autoTdpSteady = onTarget ? _autoTdpSteady + 1 : 0;
            if (_autoTdpSteady == 20)
            {
                string key = "autotdp.w:" + running.Key + ":" + _autoTdpTarget;
                if (Labs.Get(key) != watts.ToString()) Labs.Put(key, watts.ToString());
                _autoTdpStatus = $"{fps:0} FPS at {next} W  \u00B7  remembered";
            }
        }
    }

    private void StopAutoTdp()
    {
        bool wasRunning = _autoTdpCounting;
        if (_autoTdpCounting) { _autoTdpCounting = false; _ = Task.Run(FrameCounter.Stop); }
        _autoTdpStatus = "";
        // Turned off: back to the game's profile or your usual limit, not whatever Auto TDP had got down to.
        if (wasRunning && TdpControl.SessionWatts != null) TdpControl.ClearSession();
    }

    // ---- apps

    private async Task SendWidgetAppsAsync()
    {
        var apps = _apps.OrderByDescending(a => a.RecentAt).Take(6).ToList();
        var list = new JsonArray();
        foreach (var a in apps)
        {
            string? icon = null;
            try
            {
                BitmapSource? cover = a.Cover as BitmapSource;
                if (cover is { IsFrozen: false } && cover.CanFreeze) cover = (BitmapSource)cover.GetAsFrozen();
                icon = await Task.Run(() => Encode(cover, 64, jpeg: false));
            }
            catch (Exception) { }
            list.Add(new JsonObject { ["id"] = a.LaunchId, ["name"] = a.Name, ["icon"] = icon });
        }
        WidgetBridge.Send(new JsonObject { ["type"] = "apps", ["apps"] = list });
    }

    // ---- favorites (8.5.87)

    /// <summary>The favorite games, as the Favorites page lists them (installed first, then the most recently played).</summary>
    private List<Game> FavoriteGames() => AllGames.Select(g => g.Actual).Where(g => g.IsFavorite && !g.IsActionTile && !g.OnWishlist)
        .OrderByDescending(g => g.IsRunning).ThenByDescending(g => g.IsInstalled).ThenByDescending(g => g.RecentAt).ThenBy(g => g.Name)
        .DistinctBy(g => g.Key).Take(60).ToList(); // each game once (8.5.91)

    private async Task SendWidgetFavoritesAsync()
    {
        var list = new JsonArray();
        foreach (var g in FavoriteGames())
        {
            string? cover = null;
            try
            {
                BitmapSource? art = g.Cover as BitmapSource;
                if (art is { IsFrozen: false } && art.CanFreeze) art = (BitmapSource)art.GetAsFrozen();
                cover = await Task.Run(() => Encode(art, 112, jpeg: !g.UsesIcon));
            }
            catch (Exception) { }
            list.Add(new JsonObject { ["key"] = g.Key, ["name"] = g.Name, ["cover"] = cover, ["running"] = g.IsRunning });
        }
        WidgetBridge.Send(new JsonObject { ["type"] = "favorites", ["games"] = list });
    }

    /// <summary>
    /// A favorite chosen in the widget. Already running: back to it. Another game running: only with
    /// <paramref name="stop"/> (the widget asked first), SquareDeck comes to the front, plays its closing animation for
    /// that game and then the game starts with its own start animation, as it does from Home.
    /// </summary>
    private async Task LaunchFavoriteFromWidgetAsync(string key, bool stop)
    {
        var game = AllGames.FirstOrDefault(g => g.Key == key);
        if (game == null) { WidgetNote("That game isn't in the library any more"); return; }
        if (game.IsRunning) { ReturnToGame(game); return; }
        var running = AllGames.FirstOrDefault(g => g.IsRunning && g != game);
        if (running != null && !stop) { WidgetNote(running.Name + " is running: quit it first"); return; }
        WindowActivator.BringToFront(this);
        await Task.Delay(400); // Game Bar steps aside first
        if (running != null && !await StopGameAsync(running, confirm: false, settleMs: 1500)) { WidgetNote("Couldn't quit " + running.Name); return; }
        await ActivateAsync(game);
    }

    // ---- layout

    private void SaveWidgetLayout(Game? running, string order, string hidden)
    {
        if (running != null && Labs.On("wgameaware"))
        {
            Labs.Put("w.order:" + running.Key, order, save: false);
            Labs.Put("w.hidden:" + running.Key, hidden);
        }
        else
        {
            Labs.Put("w.order", order, save: false);
            Labs.Put("w.hidden", hidden);
        }
        WidgetBridge.Nudge();
    }

    // ---- style and companion

    private void SendWidgetTheme()
    {
        try
        {
            string Hex(string key) => Application.Current.TryFindResource(key) is SolidColorBrush b ? b.Color.ToString() : "";
            var message = new JsonObject
            {
                ["type"] = "theme", ["key"] = UiStyles.Current.Id + "|" + WidgetAccent() + "|" + Settings.Wallpaper,
                ["bg"] = Hex("BgBrush"), ["card"] = Hex("CardBrush"), ["fg"] = Hex("FgBrush"), ["subtle"] = Hex("SubtleBrush"), ["accent"] = Hex("AccentBrush"),
            };
            // The style's own font, sent as a file (the widget can't read SquareDeck's folder), and its name.
            string font = UiStyles.Current.Font ?? "";
            if (font.StartsWith("file:", StringComparison.Ordinal))
            {
                var parts = font[5..].Split('#');
                string path = Path.Combine(AppFonts.Folder, parts[0]);
                if (parts.Length > 1 && File.Exists(path) && new FileInfo(path).Length < 1_500_000)
                {
                    message["fontfile"] = parts[0];
                    message["fontname"] = parts[1];
                    message["font"] = Convert.ToBase64String(File.ReadAllBytes(path));
                }
            }
            else if (font.Length > 0) message["fontname"] = font.Split(',')[0].Trim();
            // The wallpaper as it shows on Home, small.
            if ((WallpaperLayer.Fill as ImageBrush)?.ImageSource is BitmapSource wall && WallpaperLayer.Visibility == Visibility.Visible)
            {
                if (!wall.IsFrozen && wall.CanFreeze) wall = (BitmapSource)wall.GetAsFrozen();
                message["wallpaper"] = Encode(wall, 720, jpeg: true);
            }
            WidgetBridge.Send(message);
        }
        catch (Exception ex) { CrashLog.Write("Widget theme", ex); }
    }

    private void SendWidgetCompanion(string mood)
    {
        try
        {
            const double cell = 8;
            var canvas = new Canvas { Width = 12 * cell, Height = 12 * cell };
            CompanionArt.Draw(canvas, CompanionArt.SpeciesId, CompanionArt.ColorHex, mood, false, Labs.Outfit, cell: cell);
            canvas.Measure(new Size(canvas.Width, canvas.Height));
            canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
            canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            bitmap.Freeze();
            WidgetBridge.Send(new JsonObject
            {
                ["type"] = "companion", ["key"] = CompanionArt.SpeciesId + "|" + CompanionArt.ColorHex + "|" + Labs.Outfit + "|" + mood,
                ["image"] = Encode(bitmap, (int)canvas.Width, jpeg: false),
            });
        }
        catch (Exception ex) { CrashLog.Write("Widget companion", ex); }
    }
}
