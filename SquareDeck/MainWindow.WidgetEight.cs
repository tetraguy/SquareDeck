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
        "wfpscap", "wautotdp", "wthermal", "wnotes", "wach", "wgenie", "wpad", "wfriends", "winbox", "wdownloads",
        "wparty", "wapps", "wshortcuts", "wcards", "wgameaware", "wpresets", "wtheme", "wcompanion",
    };

    /// <summary>Every card the widget has, in its usual order (the editor's list).</summary>
    private static readonly string[] WidgetCardIds =
    {
        "game", "media", "power", "live", "ally", "notes", "achievements", "genie", "controller", "friends", "inbox", "downloads", "launch", "quick",
    };

    private int _widgetCap;                    // the frame cap the widget set for this session (0: none)
    private List<Friend>? _widgetFriends;
    private DateTime _widgetFriendsAt = DateTime.MinValue;
    private bool _genieBusy;

    // ================================================================ the state

    private void AddWidgetEight(JsonObject state)
    {
        var on = WidgetLabs.Where(Labs.On).ToList();
        state["labs"] = new JsonArray(on.Select(x => (JsonNode)x).ToArray());
        if (on.Count == 0) return;
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

        if (Labs.On("wfpscap") || Labs.On("wautotdp"))
        {
            state["fpscap"] = _widgetCap > 0 ? _widgetCap : (_capBefore != null && key != null ? Eight.FpsCap(key) : 0);
            state["capok"] = AmdAdlx.CapMethod() != null;
        }
        if (Labs.On("wautotdp"))
            state["autotdp"] = new JsonObject { ["on"] = _autoTdpOn, ["target"] = _autoTdpTarget, ["status"] = _autoTdpStatus };
        if (Labs.On("wthermal")) state["templimit"] = Settings.TdpTempLimit > 0 ? Settings.TdpTempLimit : 95;
        if (Labs.On("wnotes") && key != null) state["notes"] = Library.Find(key)?.Notes ?? "";
        if (Labs.On("wpad"))
            state["pad"] = new JsonObject { ["battery"] = ControllerBattery, ["connected"] = PadState.Connected, ["rumble"] = Math.Max(0, Settings.RumbleStrength) };
        if (Labs.On("wfriends"))
        {
            if (DateTime.Now - _widgetFriendsAt > TimeSpan.FromMinutes(2) && Settings.SteamWebApiKey.Length > 0)
            {
                _widgetFriendsAt = DateTime.Now;
                _ = Task.Run(async () =>
                {
                    var (friends, _) = await SteamFriends.GetAsync(Settings.SteamWebApiKey, CancellationToken.None);
                    if (friends != null) _widgetFriends = friends;
                });
            }
            var list = (_widgetFriends ?? new List<Friend>()).Where(f => f.IsOnline).OrderByDescending(f => f.InGame).ThenBy(f => f.Name).Take(8);
            state["friends"] = new JsonArray(list.Select(f => (JsonNode)new JsonObject { ["id"] = f.Id, ["name"] = f.Name, ["status"] = f.Status, ["playing"] = f.InGame }).ToArray());
            state["friendsready"] = Settings.SteamWebApiKey.Length > 0;
        }
        if (Labs.On("winbox"))
            state["inbox"] = new JsonArray(Notifications.Items.Take(8).Select(n => (JsonNode)new JsonObject
            {
                ["title"] = n.Title, ["text"] = n.Text, ["when"] = n.When.Date == DateTime.Today ? n.When.ToString("h:mm tt") : n.When.ToString("MMM d"), ["kind"] = n.Kind,
            }).ToArray());
        if (Labs.On("wdownloads"))
        {
            var items = new List<JsonNode>();
            foreach (var g in _steam.Where(g => g.UpdateText.Length > 0 && (g.UpdateText.Contains('%') || g.UpdateText.Contains("queued", StringComparison.OrdinalIgnoreCase) || g.UpdateText.StartsWith("Updating", StringComparison.Ordinal))).Take(6))
                items.Add(new JsonObject { ["name"] = g.Name, ["text"] = g.UpdateText });
            if (_installing != null) items.Add(new JsonObject { ["name"] = _installing.Name, ["text"] = "Installing through Steam" });
            foreach (var g in InstallQueue.Take(5)) items.Add(new JsonObject { ["name"] = g.Name, ["text"] = "Waiting in SquareDeck's queue" });
            state["downloads"] = new JsonArray(items.ToArray());
            state["queue"] = InstallQueue.Count + (_installing != null ? 1 : 0);
        }
        if (Labs.On("wparty") && running != null) state["party"] = running.IsSteam ? "steam" : "xbox";
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
            case "achievements":
                if (running is { IsSteam: true }) _ = SendWidgetAchievementsAsync(running);
                else WidgetBridge.Send(new JsonObject { ["type"] = "achievements", ["none"] = running == null ? "No game running" : "Achievements are shown for Steam games" });
                return true;
            case "genie" when Str("q") is { Length: > 0 } question:
                _ = WidgetGenieAsync(running, question);
                return true;
            case "rumble" when Int("value") is int level:
                Settings.RumbleStrength = Math.Clamp(level, 0, 100);
                Settings.Save();
                if (level > 0) RumbleTest(level);
                return true;
            case "drift":
                _ = WidgetDriftAsync();
                return true;
            case "chat" when Str("id") is { Length: > 0 } friend:
                ProcessUtil.OpenOutside("steam://friends/message/" + friend);
                return true;
            case "steamdownloads":
                ProcessUtil.OpenOutside("steam://open/downloads");
                return true;
            case "clearqueue":
                ClearInstallQueue();
                WidgetNote("SquareDeck's download queue stopped");
                return true;
            case "party":
                if (running is { IsSteam: true }) { ProcessUtil.OpenOutside("steam://open/friends"); WidgetNote("Steam's friends list: right-click a friend to invite"); }
                else WidgetNote("Open Game Bar's Xbox Social widget to invite friends");
                return true;
            case "apps":
                _ = SendWidgetAppsAsync();
                return true;
            case "launch" when Str("id") is { Length: > 0 } app:
                if (!InstalledApps.Launch(app)) WidgetNote("Couldn't start it");
                return true;
            case "go" when Str("page") is { Length: > 0 } page:
                WidgetGo(page);
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
        if (!_autoTdpCounting) { _autoTdpCounting = true; _ = Task.Run(FrameCounter.Start); }
        var (pid, _) = PerfOverlay.ForegroundApp();
        if (pid != 0 && pid != Environment.ProcessId) FrameCounter.TargetPid = pid;
        if (DateTime.UtcNow - _autoTdpAt < TimeSpan.FromSeconds(3)) return;
        _autoTdpAt = DateTime.UtcNow;
        if (FrameCounter.Fps is not double fps) { _autoTdpStatus = "Reading the frame rate…"; return; }
        int watts = TdpControl.EffectiveWatts, next = watts;
        if (fps < _autoTdpTarget - 2) next += fps < _autoTdpTarget - 8 ? 2 : 1;
        else if (fps > _autoTdpTarget + 4) next -= fps > _autoTdpTarget + 12 ? 2 : 1;
        next = Math.Clamp(next, chip.Min, chip.Max);
        if (next != watts) TdpControl.SetSessionWatts(next);
        _autoTdpStatus = $"{fps:0} FPS at {next} W";
    }

    private void StopAutoTdp()
    {
        if (_autoTdpCounting) { _autoTdpCounting = false; _ = Task.Run(FrameCounter.Stop); }
        _autoTdpStatus = "";
    }

    // ---- achievements

    private async Task SendWidgetAchievementsAsync(Game game)
    {
        var list = await Labs.AchievementsAsync(game.AppId, CancellationToken.None);
        var message = new JsonObject { ["type"] = "achievements", ["game"] = game.Name };
        if (list == null || list.Count == 0) message["none"] = "No achievements found (the Steam profile needs to be public)";
        else
        {
            message["unlocked"] = list.Count(a => a.Unlocked);
            message["total"] = list.Count;
            message["next"] = new JsonArray(list.Where(a => !a.Unlocked).OrderBy(a => a.Description.Length == 0).Take(5)
                .Select(a => (JsonNode)new JsonObject { ["name"] = a.Name, ["desc"] = a.Description }).ToArray());
        }
        WidgetBridge.Send(message);
    }

    // ---- Ask Genie

    private async Task WidgetGenieAsync(Game? game, string question)
    {
        void Reply(string? answer, string? error, bool spoiler = false) =>
            WidgetBridge.Send(new JsonObject { ["type"] = "genie", ["q"] = question, ["a"] = answer, ["error"] = error, ["spoiler"] = spoiler });
        if (game == null) { Reply(null, "Start a game first: Genie answers about the game you're playing."); return; }
        if (!GameGenie.HasKey) { Reply(null, "Game Genie needs a Google Gemini key (Settings > Experimental > Game Genie)."); return; }
        if (_genieBusy) { Reply(null, "Genie is still thinking about the last question."); return; }
        _genieBusy = true;
        try
        {
            StoreDetails? details = game.IsSteam ? await SteamStoreClient.GetDetailsAsync(game.AppId, CancellationToken.None) : null;
            string known = GameGenieScreen.Known(game, details, null);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var sheet = GameGenie.Cached(game.Key) ?? await GameGenie.MakeAsync(game.Key, game.Name, known, cts.Token);
            var answer = await GameGenie.AskAsync(game.Key, sheet, known, question, cts.Token);
            Reply(answer.A, null, answer.Spoiler);
        }
        catch (Exception ex) { Reply(null, ex is GameGenie.GenieException g ? g.Message : "Genie couldn't answer right now."); }
        finally { _genieBusy = false; }
    }

    // ---- controller drift

    private async Task WidgetDriftAsync()
    {
        WidgetNote("Checking: keep your thumbs off the sticks");
        double left = 0, right = 0;
        for (int i = 0; i < 30; i++)
        {
            var p = PadState;
            left = Math.Max(left, Math.Sqrt(Math.Pow(p.LeftX / 32768.0, 2) + Math.Pow(p.LeftY / 32768.0, 2)));
            right = Math.Max(right, Math.Sqrt(Math.Pow(p.RightX / 32768.0, 2) + Math.Pow(p.RightY / 32768.0, 2)));
            await Task.Delay(100);
        }
        string Verdict(double v) => v < 0.06 ? "fine" : v < 0.12 ? "a little drift" : "drift";
        WidgetNote($"Left {left * 100:0}% ({Verdict(left)}), right {right * 100:0}% ({Verdict(right)})");
    }

    // ---- apps, pages

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

    private void WidgetGo(string page)
    {
        WindowActivator.BringToFront(this);
        while (_screens.Count > 0 && _screens[^1] is not BusyScreen && !(_screens[^1] is MenuScreen { Mandatory: true })) Pop(_screens[^1]);
        if (_screens.Count > 0) return;
        SetMode(HomeView.Games);
        switch (page)
        {
            case "library": Push(new LibraryScreen()); break;
            case "retro": Push(new RetroLoungeScreen()); break;
            case "captures": Push(new CapturesScreen()); break;
            case "settings": Push(new SettingsScreen()); break;
        }
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
