using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// SquareDeck's side of the Xbox Game Bar widget (Services/WidgetBridge.cs): what the widget is told, and what it may ask.
/// Everything here runs on the interface thread.
/// </summary>
public partial class MainWindow
{
    /// <summary>The quick buttons the widget may press (the same actions as the quick menu and the Ally's buttons).</summary>
    private static readonly HashSet<string> WidgetActions = new()
    {
        "perf", "refresh", "nightlight", "hdr", "dnd", "wifi", "bluetooth", "keyboard", "taskmanager", "lights", "padmouse",
        "playpause", "screenoff", "amdoverlay", "amdmetrics", "home", "mic", "desktop", "cpuboost", "ris", "chill", "ledlevel", "airplane",
    };

    private DateTime _widgetSlowAt = DateTime.MinValue;
    private bool? _widgetNight, _widgetHdr, _widgetRis, _widgetChill;
    private int? _widgetHz;
    private JsonArray? _widgetRates;

    private void StartWidgetBridge()
    {
        WidgetBridge.Snapshot = WidgetSnapshot;
        WidgetBridge.Handle = WidgetRequest;
        WidgetBridge.OnUi = work =>
        {
            try { return Dispatcher.Invoke(work, System.Windows.Threading.DispatcherPriority.Background); }
            catch (Exception) { return null; } // closing
        };
        if (Settings.GameBarWidget) WidgetBridge.Start();
        TdpControl.Changed += WidgetBridge.Nudge;

        // The widget package in the Widget folder next to SquareDeck (bundled by the installer): offered a little after
        // start when it isn't installed, and updated quietly when an older one is.
        _ = OfferWidgetAsync();
        _ = StartupHealthCheckAsync(); // Device health: a notice if something needs fixing
        _ = ShowWhatsNewAsync();       // the new version's changes, once, after an update
    }

    private JsonObject WidgetSnapshot()
    {
        var s = Settings;
        var state = new JsonObject
        {
            ["version"] = Updater.Current.ToString(),
            ["device"] = Handheld.IsHandheld ? Handheld.DisplayName : Environment.MachineName,
            // You, in the widget's header: your name, and your profile picture (sent when the widget asks for this key).
            ["user"] = UserName.Display,
            ["avatar"] = s.Avatar + "|" + WidgetAccent(),
            ["accent"] = WidgetAccent(),
            ["background"] = s.WidgetBackground,
            ["quick"] = new JsonArray((WidgetPresetQuick() ?? RunningProfile()?.WidgetQuick ?? s.WidgetQuick ?? Settings.WidgetQuickTiles.Select(t => t.Id).ToList()).Select(x => (JsonNode)x).ToArray()),
            ["sections"] = new JsonArray((s.WidgetSections ?? new List<string>(Settings.WidgetSectionIds)).Select(x => (JsonNode)x).ToArray()),
        };

        // The game that's running: its name, how long it's been played, and whether it has its own power profile.
        // Its pictures go separately (WidgetArt), once per game, when the widget asks.
        if (AllGames.FirstOrDefault(g => g.IsRunning) is { } running)
        {
            var profile = Library.Find(running.Key)?.Profile;
            var game = new JsonObject
            {
                ["name"] = running.Name, ["key"] = running.Key,
                ["profile"] = profile is { TdpWatts: > 0 },
                ["hours"] = Math.Round(Playtime.TotalSeconds(running.Key) / 3600, 1),
                ["close"] = s.WidgetCloseGame,
            };
            if (_sessionStart.TryGetValue(running.Key, out var started)) game["minutes"] = (int)(DateTime.Now - started).TotalMinutes;
            state["game"] = game;
        }
        else if (s.WidgetCloseGame && WindowActivator.FrontApp() is { } front)
            state["game"] = new JsonObject { ["name"] = front.Title, ["key"] = "", ["profile"] = false, ["close"] = true, ["other"] = true };

        // Power (TDP): the range, four steps across it (as the quick menu has them) and what's in effect.
        var tdp = new JsonObject { ["ready"] = TdpControl.Ready, ["enabled"] = s.TdpEnabled, ["canApply"] = TdpControl.CanApply };
        if (TdpControl.Chip is { } chip)
        {
            int At(double part) => (int)Math.Round(chip.Min + (chip.Max - chip.Min) * part);
            tdp["chip"] = chip.Name;
            tdp["min"] = chip.Min;
            tdp["max"] = chip.Max;
            tdp["presets"] = new JsonArray(At(0.15), At(0.45), At(0.75), chip.Max);
            tdp["watts"] = TdpControl.EffectiveWatts;
            tdp["session"] = TdpControl.SessionWatts != null;
            tdp["profile"] = RunningProfile() is { TdpWatts: > 0 };
            tdp["method"] = TdpControl.MethodName(TdpControl.Method);
            tdp["result"] = TdpControl.LastResult;
        }
        state["tdp"] = tdp;

        if (Battery.GetPercent() is int battery) state["battery"] = battery;
        state["plugged"] = Battery.IsPluggedIn();
        state["saver"] = Battery.SaverOn();
        if (Battery.GetTimeRemaining() is { } left && !Battery.IsPluggedIn()) state["left"] = (int)left.TotalMinutes;
        if (SquareDeck.Screens.FrameCounter.Fps is double fps) state["fps"] = Math.Round(fps);
        if (AsusAcpi.Available && AsusAcpi.FanRpm(AsusAcpi.CpuFan) is int fan) state["fan"] = fan;
        if (Audio.GetVolume() is int volume) state["volume"] = volume;
        state["muted"] = Audio.GetMute();
        if (Brightness.Supported != false && Brightness.Last is int level) state["brightness"] = level;

        // The ROG Ally: its operating mode, fans, lights and controller mode.
        if (AllyControl.IsAlly)
        {
            var ally = new JsonObject
            {
                ["asus"] = AsusAcpi.Available, ["mode"] = s.AllyAsusMode, ["fan"] = s.AllyFan,
                ["lights"] = s.AllyLights is not ("" or "off"), ["mouse"] = AllyControl.MouseMode,
            };
            if (AsusAcpi.Available && AsusAcpi.FanRpm(AsusAcpi.CpuFan) is int rpm) ally["rpm"] = rpm;
            state["ally"] = ally;
        }

        // Things that are slower to read: every few seconds.
        if (DateTime.Now - _widgetSlowAt > TimeSpan.FromSeconds(4))
        {
            _widgetSlowAt = DateTime.Now;
            _ = RefreshWidgetRadiosAsync();
            try { _widgetNight = NightLight.IsOn; } catch (Exception) { _widgetNight = null; }
            try { _widgetHdr = Hdr.Get(); } catch (Exception) { _widgetHdr = null; }
            if (AdrenalineHook.HasAmdGraphics)
            {
                _widgetRis = AmdAdlx.ImageSharpening;
                _widgetChill = AmdAdlx.Chill;
            }
            try
            {
                var now = DisplaySettings.Current();
                _widgetHz = now?.RefreshRate;
                _widgetRates = now == null ? null : new JsonArray(DisplaySettings.All().Where(m => m.Width == now.Width && m.Height == now.Height)
                    .Select(m => m.RefreshRate).Distinct().OrderBy(r => r).Select(r => (JsonNode)r).ToArray());
            }
            catch (Exception) { _widgetHz = null; }
        }
        state["toggles"] = new JsonObject
        {
            ["perf"] = s.OsdEnabled, ["nightlight"] = _widgetNight, ["hdr"] = _widgetHdr, ["dnd"] = s.ToastsSuppressed,
            ["boost"] = s.PerformanceMode != "off",
            ["cpuboost"] = TdpControl.Ready ? TdpControl.EffectiveBoost : null,
            ["ris"] = _widgetRis, ["chill"] = _widgetChill,
        };
        AddWidgetRadioToggles((JsonObject)state["toggles"]!);
        // The Ally's light brightness, for the LED tile (off, or a percentage).
        if (AllyControl.IsAlly) state["toggles"]!["ledlevel"] = s.AllyLights is "" or "off" ? 0 : AllyControl.BrightnessPercent(s);
        // What SquareDeck plays (Settings > Experimental > Media controls in the widget): read every second or two.
        if (Labs.On("widgetmedia"))
        {
            if (DateTime.Now - _widgetMediaAt > TimeSpan.FromSeconds(1.5)) { _widgetMediaAt = DateTime.Now; _ = RefreshWidgetMediaAsync(); }
            if (_widgetMedia != null) state["media"] = _widgetMedia.DeepClone();
        }
        if (_widgetHz is int hz) state["refresh"] = hz;
        if (_widgetRates != null) state["rates"] = _widgetRates.DeepClone();
        AddWidgetEight(state); // Experimental (8.1): the widget's new cards and their data
        return state;
    }

    private void WidgetRequest(string op, JsonObject request)
    {
        int? Int(string name) { try { return request[name]?.GetValue<int>(); } catch (Exception) { try { return (int?)request[name]?.GetValue<double>(); } catch (Exception) { return null; } } }
        string? Str(string name) { try { return request[name]?.GetValue<string>(); } catch (Exception) { return null; } }
        bool? Bool(string name) { try { return request[name]?.GetValue<bool>(); } catch (Exception) { return null; } }
        var s = Settings;
        if (WidgetEightRequest(op, request)) return; // Experimental (8.1)
        switch (op)
        {
            case "tdp" when Int("watts") is int watts && TdpControl.Ready:
                // With the game's own profile on, the widget edits that profile; otherwise it's a change for this session.
                if (RunningProfile() is { TdpWatts: > 0 } own)
                {
                    own.TdpWatts = TdpControl.Clamp(watts);
                    if (own.TdpPluggedWatts > 0) own.TdpPluggedWatts = own.TdpWatts;
                    Library.Save();
                    TdpControl.SetGame(own);
                    TdpControl.ClearSession();
                }
                else TdpControl.SetSessionWatts(watts);
                break;
            case "gameprofile" when Bool("on") is bool on && AllGames.FirstOrDefault(g => g.IsRunning) is { } game && TdpControl.Ready:
            {
                // On: the game keeps its own TDP (what's in effect now), every time it starts. Off: back to your usual one.
                var profile = Library.For(game.Key).Profile ??= new GameProfile();
                profile.TdpWatts = on ? TdpControl.Clamp(TdpControl.EffectiveWatts) : 0;
                profile.TdpPluggedWatts = 0;
                Library.Save();
                TdpControl.SetGame(profile.HasPower ? profile : null);
                TdpControl.ClearSession();
                WidgetBridge.Note(on ? $"{game.Name} keeps {profile.TdpWatts} W" : "Back to your usual power");
                break;
            }
            case "media" when Str("cmd") is { } cmd && Labs.On("widgetmedia"):
                _ = WidgetMediaCommandAsync(cmd);
                break;
            case "mediaart" when Str("key") is { Length: > 0 } mediaKey && Labs.On("widgetmedia"):
                _ = SendWidgetMediaArtAsync(mediaKey);
                break;
            case "closegame":
                _ = CloseGameFromWidgetAsync(Bool("bar") == true);
                break;
            case "art" when Str("key") is { Length: > 0 } key:
                _ = SendWidgetArtAsync(key);
                break;
            case "avatar":
                SendWidgetAvatar();
                break;
            case "tdpstep" when Int("delta") is int delta && TdpControl.Ready:
                TdpControl.SetSessionWatts(TdpControl.EffectiveWatts + Math.Clamp(delta, -10, 10));
                break;
            case "tdpreset" when TdpControl.Ready:
                TdpControl.ClearSession();
                WidgetBridge.Note(TdpControl.GameProfile != null ? "Back to the game's profile" : "Back to your usual limit");
                break;
            case "volume" when Int("value") is int v:
                Audio.SetVolume(Math.Clamp(v, 0, 100));
                if (v > 0 && Audio.GetMute()) Audio.SetMute(false);
                break;
            case "mute":
                Audio.SetMute(Bool("on") ?? !Audio.GetMute());
                break;
            case "brightness" when Int("value") is int b:
                _ = Brightness.SetAsync(Math.Clamp(b, 0, 100));
                break;
            case "asusmode" when Str("id") is { } mode && AllyControl.AsusModes.Any(m => m.Id == mode) && AsusAcpi.Available:
                s.AllyAsusMode = mode;
                s.Save();
                Task.Run(AllyControl.ApplyPower);
                WidgetBridge.Note("ASUS mode: " + AllyControl.AsusModes.First(m => m.Id == mode).Name);
                break;
            case "fan" when Str("id") is { } fan && AllyControl.FanCurves.Any(f => f.Id == fan) && AsusAcpi.Available:
                s.AllyFan = fan;
                s.Save();
                Task.Run(AllyControl.ApplyPower);
                WidgetBridge.Note("Fans: " + AllyControl.FanCurves.First(f => f.Id == fan).Name);
                break;
            case "refresh" when Int("hz") is int wanted:
                try
                {
                    if (DisplaySettings.Current() is { } now && now.RefreshRate != wanted)
                        WidgetBridge.Note(DisplaySettings.Apply(now with { RefreshRate = wanted }, keep: true) ? $"{wanted} Hz" : "Couldn't change the refresh rate");
                }
                catch (Exception) { WidgetBridge.Note("Couldn't change the refresh rate"); }
                _widgetSlowAt = DateTime.MinValue;
                break;
            case "action" when Str("id") is { } id && WidgetActions.Contains(id):
                RunAllyAction(id);
                _widgetSlowAt = DateTime.MinValue; // night light and HDR are read again at once
                break;
            case "open":
                RunAllyAction("home");
                break;
            case var radio when WidgetRadioOps.Contains(radio):
                WidgetRadioRequest(op, request);
                break;
            case "quicktiles" when request["ids"] is JsonArray ids:
            {
                // Edit in the widget's Quick card: which tiles show, in order (only ones the widget has).
                var known = Settings.WidgetQuickTiles.Select(t => t.Id).ToHashSet();
                var chosen = ids.Select(n => { try { return n?.GetValue<string>(); } catch (Exception) { return null; } })
                    .Where(x => x != null && known.Contains(x)).Select(x => x!).Distinct().ToList();
                // While a game with its own tiles runs, Edit changes that game's tiles; otherwise the usual ones.
                if (RunningProfile() is { WidgetQuick: not null } gameTiles) { gameTiles.WidgetQuick = chosen; Library.Save(); }
                else { s.WidgetQuick = chosen; s.Save(); }
                break;
            }
            case "power" when Str("id") is { } what && what is "lock" or "sleep" or "hibernate" or "restart" or "shutdown":
                // A moment first, so Game Bar can close before the PC locks, sleeps or turns off.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(700);
                    Dispatcher.Invoke(() =>
                    {
                        switch (what)
                        {
                            case "lock": Power.Lock(); break;
                            case "sleep": Power.Sleep(); break;
                            case "hibernate": Power.Hibernate(); break;
                            case "restart": Power.Restart(); break;
                            case "shutdown": Power.ShutDown(); break;
                        }
                    });
                });
                break;
        }
    }

    // ---------------------------------------------------------------- the widget's media row (experimental)

    private JsonObject? _widgetMedia;
    private DateTime _widgetMediaAt = DateTime.MinValue;
    private bool _widgetMediaBusy;
    private string _widgetMediaSource = "";
    private readonly Dictionary<string, JsonObject> _widgetMediaArt = new();

    /// <summary>
    /// What SquareDeck itself is playing, best first: its Spotify player, its music player, then a page of its own
    /// (YouTube Music, Apple Music and the rest play in SquareDeck's built-in browser, which Windows lists as SquareDeck).
    /// Music from other apps isn't SquareDeck's, so the widget's row stays hidden for it.
    /// </summary>
    private async Task RefreshWidgetMediaAsync()
    {
        if (_widgetMediaBusy) return;
        _widgetMediaBusy = true;
        try
        {
            JsonObject Make(string title, string artist, bool playing, string source) => new()
            {
                ["title"] = title, ["artist"] = artist, ["playing"] = playing, ["source"] = source,
                ["key"] = source + "|" + title + "|" + artist,
            };
            JsonObject? media = null;
            if (SpotifyPlayer.IsRunning && SpotifyPlayer.Track is { } song) media = Make(song.Title, song.Artist, SpotifyPlayer.IsPlaying, "spotify");
            else if (LocalMusic.Current is { } track) media = Make(track.Title, track.Artist, LocalMusic.IsPlaying, "local");
            else if (await MediaService.GetAsync(includeThumbnail: false) is { } now && now.Title.Length > 0 &&
                     (now.App.Contains("SquareDeck", StringComparison.OrdinalIgnoreCase) || now.App.Contains("WebView", StringComparison.OrdinalIgnoreCase)))
                media = Make(now.Title, now.Artist, now.IsPlaying, "page");
            _widgetMediaSource = media?["source"]?.GetValue<string>() ?? "";
            if (!JsonNode.DeepEquals(media, _widgetMedia)) { _widgetMedia = media; WidgetBridge.Nudge(); }
        }
        catch (Exception) { }
        finally { _widgetMediaBusy = false; }
    }

    private async Task WidgetMediaCommandAsync(string cmd)
    {
        switch (_widgetMediaSource, cmd)
        {
            case ("local", "toggle"): LocalMusic.TogglePlay(); break;
            case ("local", "next"): LocalMusic.Next(); break;
            case ("local", "previous"): LocalMusic.Previous(); break;
            case ("spotify", "toggle"): SpotifyPlayer.TogglePlay(); break;
            case (_, "toggle"): await MediaService.PlayPauseAsync(); break;
            case (_, "next"): await MediaService.NextAsync(); break;
            case (_, "previous"): await MediaService.PreviousAsync(); break;
        }
        await Task.Delay(350);
        _widgetMediaAt = DateTime.MinValue; // read again straight away
        await RefreshWidgetMediaAsync();
    }

    /// <summary>The cover art of what's playing, sent once per song when the widget asks.</summary>
    private async Task SendWidgetMediaArtAsync(string key)
    {
        if (_widgetMediaArt.TryGetValue(key, out var ready)) { WidgetBridge.Send(ready); return; }
        BitmapSource? art = null;
        try
        {
            if (key.StartsWith("spotify|") && SpotifyPlayer.Track?.ArtUrl is { Length: > 0 } url)
                art = await ImageLoader.FromUrlAsync(url, 320) as BitmapSource;
            else if (key.StartsWith("local|") && LocalMusic.Current is { } track && await LocalMusic.ArtAsync(track) is { Length: > 0 } bytes)
                art = ImageLoader.FromBytes(bytes, 320) as BitmapSource;
            else if (await MediaService.GetAsync(includeThumbnail: true) is { Thumbnail: { Length: > 0 } thumb })
                art = ImageLoader.FromBytes(thumb, 320) as BitmapSource;
        }
        catch (Exception) { }
        if (art is { IsFrozen: false } && art.CanFreeze) art = (BitmapSource)art.GetAsFrozen();
        var message = new JsonObject { ["type"] = "mediaart", ["key"] = key, ["art"] = await Task.Run(() => Encode(art, 240, jpeg: true)) };
        if (_widgetMediaArt.Count > 30) _widgetMediaArt.Clear();
        if (art != null) _widgetMediaArt[key] = message;
        WidgetBridge.Send(message);
    }

    /// <summary>The running game's own profile, if it has one.</summary>
    private GameProfile? RunningProfile() =>
        AllGames.FirstOrDefault(g => g.IsRunning) is { } g ? Library.Find(g.Key)?.Profile : null;

    /// <summary>Opens SquareDeck's widget in Game Bar (Settings > Controller > Open the widget now).</summary>
    public void RunWidgetAction() => OpenWidget();

    private string? _widgetColorKey, _widgetGameColor;

    /// <summary>The widget's highlight color, as Settings > Controller > Widget colors says.</summary>
    private string WidgetAccent()
    {
        static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        string style = Application.Current.Resources["AccentBrush"] is SolidColorBrush a ? Hex(Readable(a.Color)) : "#0AB9C9";
        switch (Settings.WidgetTheme)
        {
            case "style": return style;
            case "game":
                if (AllGames.FirstOrDefault(g => g.IsRunning) is not { } running) return style;
                if (_widgetColorKey != running.Key)
                {
                    _widgetColorKey = running.Key;
                    _widgetGameColor = null;
                    try { if (running.Cover is BitmapSource cover && ImageLoader.DominantColor(cover) is Color c) _widgetGameColor = Hex(Readable(c)); }
                    catch (Exception) { }
                }
                return _widgetGameColor ?? style;
            default: return "#0AB9C9";
        }
    }

    /// <summary>A color bright enough to read as a highlight on the widget's dark background.</summary>
    private static Color Readable(Color c)
    {
        double lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        if (lum >= 0.45) return c;
        double mix = Math.Min(0.7, 0.45 - lum + 0.25);
        return Color.FromRgb((byte)(c.R + (255 - c.R) * mix), (byte)(c.G + (255 - c.G) * mix), (byte)(c.B + (255 - c.B) * mix));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    /// <summary>Win + G: closes Game Bar's overlay when it's open (the same keys that opened it).</summary>
    private static void ToggleGameBar()
    {
        const byte Win = 0x5B, G = 0x47;
        const uint Up = 0x2;
        keybd_event(Win, 0, 0, UIntPtr.Zero);
        keybd_event(G, 0, 0, UIntPtr.Zero);
        keybd_event(G, 0, Up, UIntPtr.Zero);
        keybd_event(Win, 0, Up, UIntPtr.Zero);
    }

    /// <summary>
    /// Close game, from the widget: like closing it from SquareDeck. Game Bar closes (when it's open, not just the widget
    /// pinned), SquareDeck comes forward over the game, and its "Closing software" animation plays while the game is
    /// stopped, then Home.
    /// </summary>
    private async Task CloseGameFromWidgetAsync(bool barOpen)
    {
        try
        {
            if (AllGames.FirstOrDefault(g => g.IsRunning) is { } running)
            {
                if (barOpen)
                {
                    ToggleGameBar();
                    await Task.Delay(450); // its overlay fades out
                }
                WindowActivator.ShowOnTop(this);
                await Task.Delay(120);
                await StopGameAsync(running, confirm: false);
            }
            else if (WindowActivator.FrontApp() is { } front) await WindowActivator.CloseAppAsync(front);
        }
        catch (Exception ex) { CrashLog.Write("Widget close game", ex); }
    }

    private readonly Dictionary<string, JsonObject> _widgetArt = new();

    /// <summary>
    /// The running game's pictures for the widget's game card: its icon and a wide picture behind it (the hero, else the
    /// cover), small, as text. The widget can't read SquareDeck's picture folder from its sandbox, so they go down the pipe.
    /// </summary>
    private async Task SendWidgetArtAsync(string key)
    {
        if (_widgetArt.TryGetValue(key, out var ready)) { WidgetBridge.Send(ready); return; }
        if (AllGames.FirstOrDefault(g => g.Key == key) is not { } game) return;
        BitmapSource? cover = game.Cover as BitmapSource;
        if (cover is { IsFrozen: false } && cover.CanFreeze) cover = (BitmapSource)cover.GetAsFrozen();
        string apiKey = Settings.SteamGridDbApiKey;
        var art = await Task.Run(async () =>
        {
            BitmapSource? icon = null, hero = null;
            try
            {
                using var client = new SteamGridDbClient(apiKey);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                if (await client.GetIconAsync(game, cts.Token) is { } iconPath) icon = ImageLoader.FromFile(iconPath, 128) as BitmapSource;
                if (await client.GetHeroAsync(game, cts.Token) is { } heroPath) hero = ImageLoader.FromFile(heroPath, 720) as BitmapSource;
            }
            catch (Exception) { }
            return new JsonObject
            {
                ["type"] = "art", ["key"] = key,
                ["icon"] = Encode(icon ?? cover, 128, jpeg: false),
                ["hero"] = Encode(hero ?? cover, 640, jpeg: true),
            };
        });
        if (_widgetArt.Count > 20) _widgetArt.Clear();
        _widgetArt[key] = art;
        WidgetBridge.Send(art);
    }

    /// <summary>Your profile picture, drawn as on Home (a preset, your own picture or your Steam one), for the widget's header.</summary>
    private void SendWidgetAvatar()
    {
        try
        {
            const int size = 128;
            var face = Avatars.Build(Settings.Avatar, size);
            face.Measure(new Size(size, size));
            face.Arrange(new Rect(0, 0, size, size));
            face.UpdateLayout();
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(face);
            bitmap.Freeze();
            WidgetBridge.Send(new JsonObject
            {
                ["type"] = "avatar", ["key"] = Settings.Avatar + "|" + WidgetAccent(), ["image"] = Encode(bitmap, size, jpeg: false),
            });
        }
        catch (Exception ex) { CrashLog.Write("Widget avatar", ex); }
    }

    private static string? Encode(BitmapSource? source, int width, bool jpeg)
    {
        if (source == null) return null;
        try
        {
            BitmapSource image = source;
            if (source.PixelWidth > width)
            {
                double scale = width / (double)source.PixelWidth;
                image = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            }
            BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = 82 } : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception) { return null; }
    }

    private bool _widgetOffered;

    /// <summary>
    /// The start-up offer: if the widget isn't installed, asks whether to install it now, later (asked again next
    /// time) or never (and says where to find it in Settings). Waits until no game is running and SquareDeck is in front.
    /// </summary>
    private async Task OfferWidgetAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        // An older widget than the one SquareDeck ships with: the update is required (the two talk to each other).
        var installed = await Task.Run(WidgetInstaller.InstalledVersion);
        var bundled = await Task.Run(WidgetInstaller.Find);
        if (installed != null && bundled != null && installed < bundled.Version)
        {
            await WaitForQuietAsync();
            await RequireWidgetUpdateAsync(installed, bundled);
            return;
        }

        if (!Settings.GameBarWidget || Settings.WidgetPromptIgnored || _widgetOffered) return;
        var package = await Task.Run(WidgetInstaller.Missing);
        if (package == null) return;
        await WaitForQuietAsync();
        _widgetOffered = true;
        int choice = await AskAsync("Install the Game Bar widget?",
            "SquareDeck comes with a widget for Xbox Game Bar (Win + G, or the Xbox button): the TDP, sound, brightness, the game's own power profile, " +
            "the ROG Ally's modes and quick toggles while you play. It isn't installed yet. The first time, Windows asks an administrator to trust it.",
            "Install", "Later", "Don't ask again");
        if (choice == 0) await InstallWidgetWithScreenAsync(package, update: false);
        else if (choice == 2)
        {
            Settings.WidgetPromptIgnored = true;
            Settings.Save();
            await AskAsync("Game Bar widget", "SquareDeck won't ask again. To install it later, go to Settings > Controller and Game Bar widget > Install the widget.", "OK");
        }
        // Later (or B): asked again the next time SquareDeck starts.
    }

    /// <summary>Waits until no game is running, SquareDeck is in front, and no setup or other question is open.</summary>
    private async Task WaitForQuietAsync()
    {
        while (AllGames.Any(g => g.IsRunning) || !IsActive || _screens.Any(s => s is SquareDeck.Screens.OnboardingScreen) || _screens.Count > 0 && _screens[^1].IsDialog)
            await Task.Delay(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// The required widget update: a pop-up B can't close, with only Update. If the update fails (an administrator
    /// prompt declined, say), it says why and offers to try again, or to be asked again next time SquareDeck starts, so a
    /// failure can never lock the launcher.
    /// </summary>
    private async Task RequireWidgetUpdateAsync(Version installed, WidgetInstaller.Package bundled)
    {
        await AskMandatoryAsync("Game Bar widget update",
            $"A required update for SquareDeck's Game Bar widget is ready: version {bundled.Version} (you have {installed}). " +
            "This version of SquareDeck needs it, so the widget and the launcher keep working together.", "Update now");
        // The install screen shows each step, and after a failure offers Try again or "Ask me next time".
        await InstallWidgetWithScreenAsync(bundled, update: true, mandatory: true);
    }

    /// <summary>Installs or updates the widget on the step-by-step install screen; the result once it closes.</summary>
    public async Task<(bool Ok, string Message)> InstallWidgetWithScreenAsync(WidgetInstaller.Package package, bool update, bool mandatory = false)
    {
        var screen = new SquareDeck.Screens.WidgetInstallScreen(package, update, mandatory);
        Push(screen);
        return await screen.Result;
    }
}
