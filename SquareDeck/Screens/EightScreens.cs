using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;
using Path = System.IO.Path;

namespace SquareDeck.Screens;

// The pages of the experimental features added in 8.0 (Settings > Experimental), and their parts in other pages.

// ==================================================================== Settings > Experimental: each feature's page

public sealed partial class SettingsScreen
{
    /// <summary>The category on show, so crash recovery can bring it back.</summary>
    internal string CurrentCategory => _category;

    /// <summary>The widget's wattage looks (Experimental), as the widget names them.</summary>
    public static readonly (string Id, string Name, string About)[] PowerLooks =
    {
        ("bar", "Slim bar", "One thin bar with the watts in the middle, minus and plus on each side"),
        ("mini", "Mini dial", "A small ring with the number inside, beside the four presets"),
        ("number", "Number only", "Just \u201C15 W\u201D with arrows; select the number for the full dial"),
        ("chips", "Preset chips", "A row of preset buttons (10, 15, 20, 25 W\u2026) and a custom chip for exact watts"),
        ("dial", "Full dial", "The big dial and preset cards, as before"),
    };

    private async Task PickPowerLookAsync()
    {
        string now = Labs.Has("w.powerlook") ? Labs.Get("w.powerlook") : "bar";
        var names = PowerLooks.Select(l => (l.Id == now ? "\u2713  " : "") + l.Name + "  \u2014  " + l.About).ToArray();
        int pick = await Host.AskAsync("Widget: wattage look", "How the TDP shows in the widget's Power card. The widget changes within a second.", names);
        if (pick < 0 || pick >= PowerLooks.Length) return;
        Labs.Put("w.powerlook", PowerLooks[pick].Id);
        if (!Labs.On("wpowerlook")) Labs.Set("wpowerlook", true);
        Rebuild();
    }

    private Action? EightPage(string id) => id switch
    {
        "fpscap" => () => Host.Push(new FpsCapScreen()),
        "docked" => () => Host.Push(new DockedScreen()),
        "sessiontimer" => () => Host.Push(new SessionTimerScreen()),
        "trailerpreview" => () => Host.Push(new TrailerPreviewScreen()),
        "friendswidget" => () => Host.Push(new FriendsWidgetScreen()),
        "rascraper" => () => Host.Push(new ScreenScraperScreen()),
        "rashaders" => () => Host.Push(new RetroLooksScreen()),
        "gamepass" => () => Host.Push(new GamePassScreen()),
        "padtest" => () => Host.Push(new ControllerTesterScreen()),
        "updateguard" => () => Host.Push(new UpdateGuardScreen()),
        "drivercheck" => () => Host.Push(new DriverCheckScreen()),
        "startupapps" => () => Host.Push(new StartupAppsScreen()),
        "twitch" => () => { var t = StreamingService.All.First(s => s.Id == "twitch"); _ = StreamingScreen.OpenAsync(Host, t); },
        "dailypick" => () => _ = ShowDailyPickAsync(),
        "visualmodes" => () => Host.Push(new VisualModesScreen()),
        "wpowerlook" => () => _ = PickPowerLookAsync(),
        "cpucores" => () => Host.Push(new CpuCoresScreen()),
        "fancurve" => () => Host.Push(new CustomFanScreen()),
        _ => null
    };

    private async Task ShowDailyPickAsync()
    {
        if (Eight.DailyPick(Host.AllGames, Host.Playtime, Host.Library) is not { } pick)
        {
            await Host.AskAsync("Daily pick", "Nothing to suggest yet: play a few games first, and come back after a few weeks.", "OK");
            return;
        }
        int c = await Host.AskAsync("Today's pick: " + pick.Game.Name, pick.Why, "Open its page", "Not now");
        if (c == 0) Host.Push(new DetailsScreen(pick.Game));
    }
}

/// <summary>A list page with one choice that cycles through options: shared by the small option pages below.</summary>
public abstract class EightOptionsScreen : ListScreen
{
    protected EightOptionsScreen(string title) : base(title) { }

    /// <summary>A row whose value steps through <paramref name="options"/> (id, label), saved under a Labs key.</summary>
    protected void Cycle(string label, string key, string fallback, (string Id, string Name)[] options, string glyph, string? subtitle = null)
    {
        string now = Labs.Get(key, fallback);
        int index = Math.Max(0, Array.FindIndex(options, o => o.Id == now));
        AddRow(label, options[index].Name, glyph, click: () => { Labs.Put(key, options[(index + 1) % options.Length].Id); Rebuild(); }, subtitle: subtitle);
    }

    protected void Switch(string label, string key, bool fallback, string glyph, string? subtitle = null)
    {
        bool on = Labs.Get(key, fallback ? "1" : "0") == "1";
        AddRow(label, on ? "On" : "Off", glyph, click: () => { Labs.Put(key, on ? "0" : "1"); Rebuild(); }, subtitle: subtitle);
    }
}

public sealed class FpsCapScreen : EightOptionsScreen
{
    public FpsCapScreen() : base("Frame rate cap per game") { }

    protected override void Build()
    {
        string? method = AmdAdlx.CapMethod();
        AddNote(method != null
            ? $"Caps use Radeon's own limiter ({method}), set when a game starts and put back when it closes. 40 FPS on a 120 Hz screen is the battery sweet spot: smooth, even frame pacing for far less power than 60."
            : "No AMD frame limiter was found on this PC (it needs AMD Radeon graphics and its driver), so caps can't be applied here.");
        var options = new List<(string, string)> { ("0", "Off") };
        options.AddRange(Eight.FpsSteps.Select(f => (f.ToString(), f + " FPS")));
        Cycle("Cap for every game", "fps.default", "0", options.ToArray(), "", "A game's own cap, in its game profile, comes first");
        AddHeader("Each game");
        AddNote("Open a game's options, then Game profile: “Frame rate cap” sets its own (or none). Games with their own:");
        int n = 0;
        foreach (var g in Host.AllGames.Where(g => g.IsGame && Eight.FpsChoice(g.Key).Length > 0).OrderBy(g => g.Name))
        {
            string c = Eight.FpsChoice(g.Key);
            var game = g;
            AddRow(g.Name, c == "off" ? "No cap" : c + " FPS", "", click: () => Host.Push(new GameProfileScreen(game)));
            n++;
        }
        if (n == 0) AddRow("None yet", null, "");
    }
}

public sealed class DockedScreen : EightOptionsScreen
{
    public DockedScreen() : base("Docked / TV mode") { }

    protected override void Build()
    {
        bool external = Eight.ExternalDisplay();
        AddRow(external ? "A TV or monitor is connected" : "Using the built-in screen", null, external ? "" : "",
            subtitle: external ? "TV mode is on while it stays connected" : "TV mode turns on by itself when a TV or monitor is connected");
        AddHeader("When docked");
        Switch("Bigger text and controls", "docked.text", true, "", "130%, easier to read from the couch");
        Switch("More power", "docked.power", true, "", TdpControl.Ready ? "The highest TDP while docked (it's plugged in)" : "Needs TDP control (Settings > Power)");
        Switch("Hide the mouse pointer", "docked.cursor", true, "", "Controller only");
        var tv = Eight.TvSoundOutput();
        Switch("Sound on the TV", "docked.audio", true, "", tv is { } t ? "Switches to " + t.Name : "Switches to the TV's sound (HDMI / DisplayPort) when it's found");
        AddNote("Everything is put back when you unplug.");
    }
}

public sealed class SessionTimerScreen : EightOptionsScreen
{
    public SessionTimerScreen() : base("Session timer and break reminder") { }

    protected override void Build()
    {
        Cycle("Remind me after", "timer.minutes", "120", new[] { ("0", "Off"), ("45", "45 minutes"), ("60", "1 hour"), ("90", "1 hour 30 minutes"), ("120", "2 hours"), ("180", "3 hours") }, "");
        Switch("Remind me again after the same time", "timer.repeat", true, "");
        Cycle("Battery reminder", "timer.battery", "20", new[] { ("0", "Off"), ("30", "At 30%"), ("20", "At 20%"), ("15", "At 15%"), ("10", "At 10%") }, "",
            "Shown once a session, when the battery gets this low and it isn't charging");
        AddNote("Reminders appear over the game without pausing it or taking the controller.");
    }
}

public sealed class TrailerPreviewScreen : EightOptionsScreen
{
    public TrailerPreviewScreen() : base("Trailer previews on Home") { }

    protected override void Build()
    {
        Cycle("Start after", "trailer.delay", "2", new[] { ("1", "1 second"), ("2", "2 seconds"), ("3", "3 seconds"), ("5", "5 seconds") }, "");
        Cycle("How strong", "trailer.opacity", "0.7", new[] { ("0.45", "Subtle"), ("0.7", "Normal"), ("0.95", "Full") }, "");
        AddNote("Steam games only, with a trailer Steam offers as a video file. Always muted; it stops as soon as you move, open a page or start a game, and in Performance mode.");
    }
}

public sealed class FriendsWidgetScreen : EightOptionsScreen
{
    public FriendsWidgetScreen() : base("Friends playing on Home") { }

    protected override void Build()
    {
        if (Host.Settings.SteamWebApiKey.Length == 0)
            AddRow("Needs your Steam Web API key", null, "", subtitle: "Add it in Settings > Accounts (it's how SquareDeck sees your friends)");
        Cycle("Where", "friends.corner", "top", new[] { ("top", "Top right"), ("bottom", "Bottom right") }, "", "Takes effect the next time SquareDeck starts");
        AddNote("Shows up to four Steam friends who are in a game, refreshed every two minutes. Xbox friends need a Microsoft sign-in that isn't offered to other apps, so they can't be shown.");
    }
}

// ==================================================================== a game's profile: frame cap, launch options, scripts

public sealed partial class GameProfileScreen
{
    private void AddEightRows()
    {
        bool fps = Labs.On("fpscap"), opts = Labs.On("launchopts"), scripts = Labs.On("scripts");
        if (!fps && !opts && !scripts) return;
        string key = _game.Key;
        AddHeader("Experimental");
        if (fps)
        {
            string c = Eight.FpsChoice(key);
            int all = int.TryParse(Labs.Get("fps.default"), out var a) ? a : 0;
            AddRow("Frame rate cap", c == "off" ? "No cap" : c.Length > 0 ? c + " FPS" : "Default (" + (all > 0 ? all + " FPS" : "none") + ")", "", click: async () =>
            {
                var labels = new List<string> { "Default (" + (all > 0 ? all + " FPS" : "none") + ")", "No cap" };
                labels.AddRange(Eight.FpsSteps.Select(f => f + " FPS" + (f == 40 ? "  ·  battery sweet spot" : "")));
                int choice = await Host.AskAsync("Frame rate cap – " + _game.Name, AmdAdlx.CapMethod() is { } m ? "Uses " + m + "." : "No AMD frame limiter found on this PC.", labels.ToArray());
                if (choice < 0) return;
                Eight.SetFpsChoice(key, choice == 0 ? "" : choice == 1 ? "off" : Eight.FpsSteps[choice - 2].ToString());
                Rebuild();
            });
        }
        if (opts)
        {
            AddRow("Launch options", Eight.Args(key).Length > 0 ? Eight.Args(key) : "None", "", click: async () =>
            {
                var text = await Host.PromptAsync("Launch options – " + _game.Name, "Passed to the game when it starts, for example -dx11 -skipintro.", Eight.Args(key));
                if (text == null) return;
                Eight.SetArgs(key, text);
                Rebuild();
            }, subtitle: _game.IsSteam ? "Passed through Steam, after the options set in Steam itself" : null);
            if (!_game.IsSteam && !_game.LaunchPath.Contains("://") && !_game.LaunchPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                AddRow("Environment variables", Eight.Env(key).Length > 0 ? Eight.Env(key) : "None", "", click: async () =>
                {
                    var text = await Host.PromptAsync("Environment variables – " + _game.Name, "NAME=value, separated by ; (for example DXVK_HUD=fps;PROTON_LOG=1).", Eight.Env(key));
                    if (text == null) return;
                    Eight.SetEnv(key, text);
                    Rebuild();
                });
                AddRow("Run as administrator", Eight.Admin(key) ? "On" : "Off", "", click: () => { Eight.SetAdmin(key, !Eight.Admin(key)); Rebuild(); },
                    subtitle: "Windows asks to allow it each time it starts");
            }
            else AddNote(_game.IsSteam ? "Environment variables and “Run as administrator” aren't available for Steam games: Steam starts them itself."
                                       : "Environment variables and “Run as administrator” need a game started from its own program.");
        }
        if (scripts)
        {
            AddRow("Before it starts", Eight.PreScript(key).Length > 0 ? Eight.PreScript(key) : "Nothing", "", click: () => _ = ScriptAsync(before: true),
                subtitle: "A program or a command, run hidden");
            if (Eight.PreScript(key).Length > 0)
                AddRow("Wait for it to finish first", Eight.WaitForPre(key) ? "On (up to a minute)" : "Off", "", click: () => { Eight.SetWaitForPre(key, !Eight.WaitForPre(key)); Rebuild(); });
            AddRow("After it closes", Eight.PostScript(key).Length > 0 ? Eight.PostScript(key) : "Nothing", "", click: () => _ = ScriptAsync(before: false));
        }
    }

    private async Task ScriptAsync(bool before)
    {
        string key = _game.Key, now = before ? Eight.PreScript(key) : Eight.PostScript(key);
        int how = await Host.AskAsync(before ? "Before " + _game.Name + " starts" : "After " + _game.Name + " closes", null,
            "Pick a program or script file", "Type a command", now.Length > 0 ? "Remove it" : "Cancel");
        string? value = null;
        if (how == 0 && await Host.PickFileAsync("Pick a program or script", new[] { ".exe", ".bat", ".cmd", ".ps1", ".lnk" }) is { } file)
            value = file.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ? $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{file}\"" : $"\"{file}\"";
        else if (how == 1) value = await Host.PromptAsync("Command", "Run with the command prompt, for example: taskkill /im OneDrive.exe", now);
        else if (how == 2 && now.Length > 0) value = "";
        if (value == null) return;
        if (before) Eight.SetPreScript(key, value); else Eight.SetPostScript(key, value);
        Rebuild();
    }
}

// ==================================================================== Library: smart collections and microSD cards

public sealed partial class LibraryScreen
{
    private void AddEightChips(List<Game> everything, Action<string, string, int?, string?> add)
    {
        bool smart = Labs.On("smartcollections"), cards = Labs.On("microsd");
        if (!smart && !cards) return;
        if (_chips.Children.Count == 0) add("all", "Everything", null, null);
        if (smart)
        {
            var members = Eight.ShelfMembers(Host.AllGames, Host.Playtime, Host.Library);
            foreach (var shelf in Eight.Shelves)
                if (members.TryGetValue(shelf.Id, out var set) && set.Count > 0) add("smart:" + shelf.Id, shelf.Name, set.Count, shelf.Glyph);
        }
        if (cards)
            foreach (var card in Eight.KnownCards(Host.AllGames))
                add("card:" + card.Id, card.Label + (card.Present ? "" : " (out)"), card.Games.Count, "");
    }

    private string EightFilterName(string filter)
    {
        if (filter.StartsWith("smart:")) return Eight.Shelves.FirstOrDefault(s => s.Id == filter[6..])?.Name ?? "Smart collection";
        var card = Eight.KnownCards(Host.AllGames).FirstOrDefault(c => c.Id == filter[5..]);
        return card == null ? "microSD" : card.Label + (card.Present ? "" : " (not inserted)");
    }

    private bool EightMatches(Game g, string filter)
    {
        if (filter.StartsWith("smart:"))
            return Eight.ShelfMembers(Host.AllGames, Host.Playtime, Host.Library).TryGetValue(filter[6..], out var set) && set.Contains(g.Key);
        var card = Eight.KnownCards(Host.AllGames).FirstOrDefault(c => c.Id == filter[5..]);
        return card != null && card.Games.Contains(g.Key);
    }
}

// ==================================================================== Game Pass

public sealed class GamePassScreen : ListScreen
{
    public GamePassScreen() : base("Game Pass") { }

    private void Open(string url) => Host.Push(BrowserScreen.Open(Host, url));

    protected override void Build()
    {
        AddNote("Microsoft keeps its Game Pass lists on Xbox's own site (it has no list other apps may read), so these open there, inside SquareDeck.");
        AddHeader("Coming and going");
        AddRow("Recently added and leaving soon", null, "", click: () => Open("https://www.xbox.com/en-US/xbox-game-pass/games"),
            subtitle: "Xbox's Game Pass catalog: pick “Recently added” or “Leaving soon” (games leaving in the next two weeks)");
        AddRow("This month's announcements", null, "", click: () => Open("https://news.xbox.com/en-us/tag/xbox-game-pass/"),
            subtitle: "Xbox Wire: what's coming, with the dates games leave");
        AddRow("PC Game Pass", null, "", click: () => Open("https://www.xbox.com/en-US/xbox-game-pass/pc-game-pass"));
        AddHeader("Play");
        var xbox = Host.AllGames.Where(g => g.IsGame && g.Source == "Xbox").OrderByDescending(g => g.RecentAt).Take(8).ToList();
        foreach (var g in xbox)
        {
            var game = g;
            AddRow(g.Name, null, "", click: () => Host.Push(new DetailsScreen(game)), subtitle: "Installed from the Xbox app");
        }
        AddRow("Open the Xbox app", null, "", click: () => ProcessUtil.OpenOutside("xbox:"));
    }
}

// ==================================================================== Retro Lounge

public sealed partial class RetroLoungeScreen
{
    private void AddSurprise(List<RetroGame> all)
    {
        if (!Labs.On("rasurprise") || all.Count == 0) return;
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        Body.Children.Add(bar);
        Chip(bar, "", "Surprise me", () =>
        {
            var pick = all[Random.Shared.Next(all.Count)];
            Host.Push(new RetroGameScreen(pick));
        }, "surprise");
        Chip(bar, "", "Surprise me and play", () =>
        {
            var ready = all.Where(g => RetroLounge.SystemOf(g.System) is { } s && RetroLounge.Ready(RetroLounge.PlayerFor(s, g))).ToList();
            if (ready.Count == 0) { _ = Host.AskAsync("Surprise me", "Nothing can play yet: get an emulator in Retro Lounge > Emulators.", "OK"); return; }
            _ = RetroUi.PlayAsync(Host, ready[Random.Shared.Next(ready.Count)]);
        }, "surprise-play");
    }
}

public sealed partial class RetroGameScreen
{
    private void AddEightRows()
    {
        bool states = Labs.On("rastates") && RetroExtras.EmulatorIdFor(_game) == "retroarch";
        bool scrape = Labs.On("rascraper");
        if (!states && !scrape) return;
        if (states)
        {
            var list = RetroEight.States(_game);
            AddHeader("Save states");
            if (list.Count == 0) AddRow("No save states yet", null, "", subtitle: "Save one in RetroArch (its menu, or the hotkeys above) and it shows here");
            foreach (var st in list.Take(6)) AddStateRow(st);
            if (list.Count > 6) AddRow("All " + list.Count + " save states", null, "", click: () => Host.Push(new SaveStatesScreen(_game)));
        }
        if (scrape)
        {
            AddRow("Get its box art and screenshot", RetroEight.Cover(_game) != null ? "Downloaded" : null, "", click: async () =>
            {
                if (!RetroEight.Ready) { Host.Push(new ScreenScraperScreen()); return; }
                var r = await RetroEight.ScrapeAsync(new[] { _game }, onlyMissing: false, null, CancellationToken.None);
                await Host.AskAsync("ScreenScraper", r.Error ?? (r.Covers + r.Snaps > 0 ? "Done: the new pictures show next time the page opens." : "ScreenScraper doesn't have this one."), "OK");
                Rebuild();
            }, subtitle: "From ScreenScraper");
        }
    }

    internal void AddStateRow(RetroEight.SaveState st)
    {
        Image? icon = null;
        if (st.Picture != null && ImageLoader.FromFile(st.Picture, 160) is { } pic) icon = new Image { Source = pic, Width = 64, Height = 48, Stretch = Stretch.UniformToFill };
        string slot = st.Slot < 0 ? "Auto save" : "Slot " + st.Slot;
        AddRow(slot, st.When.ToString("MMM d, h:mm tt"), icon == null ? "" : null, icon: icon, click: async () =>
        {
            if (st.Slot < 0)
            {
                await Host.AskAsync("Auto save", "RetroArch loads its auto save by itself when “Load state automatically” is on in its settings. Pressing Play starts the game.", "OK");
                return;
            }
            RetroEight.PendingSlot = st.Slot;
            await RetroUi.PlayAsync(Host, _game);
        }, subtitle: st.Slot < 0 ? null : "Start the game from this save");
    }
}

public sealed class SaveStatesScreen : ListScreen
{
    private readonly RetroGame _game;
    public SaveStatesScreen(RetroGame game) : base("Save states – " + game.Title) { _game = game; }

    protected override void Build()
    {
        foreach (var st in RetroEight.States(_game))
        {
            var s = st;
            Image? icon = null;
            if (s.Picture != null && ImageLoader.FromFile(s.Picture, 160) is { } pic) icon = new Image { Source = pic, Width = 64, Height = 48, Stretch = Stretch.UniformToFill };
            AddRow(s.Slot < 0 ? "Auto save" : "Slot " + s.Slot, s.When.ToString("MMM d, h:mm tt"), icon == null ? "" : null, icon: icon, click: async () =>
            {
                if (s.Slot < 0) return;
                RetroEight.PendingSlot = s.Slot;
                await RetroUi.PlayAsync(Host, _game);
            });
        }
    }
}

public sealed class ScreenScraperScreen : ListScreen
{
    private string _status = "";
    private CancellationTokenSource? _cts;

    public ScreenScraperScreen() : base("Box art from ScreenScraper") { }

    public override void OnClosed() => _cts?.Cancel();

    protected override void Build()
    {
        AddNote("ScreenScraper is a free, community-run database of game art. It needs a free account, and a developer ID " +
                "and password that ScreenScraper gives to each app (ask for one on its forum). Pictures are kept on this PC.");
        AddHeader("Account");
        AddRow("Username", RetroEight.User.Length > 0 ? RetroEight.User : "Not set", "", click: async () =>
        {
            if (await Host.PromptAsync("ScreenScraper username", null, RetroEight.User) is { } v) { Labs.Put("ss.user", v.Trim()); Rebuild(); }
        });
        AddRow("Password", RetroEight.Password.Length > 0 ? "Saved" : "Not set", "", click: async () =>
        {
            if (await Host.PromptSecretAsync("ScreenScraper password", null) is { } v) { Labs.Put("ss.password", v); Rebuild(); }
        });
        AddRow("Developer ID", RetroEight.DevId.Length > 0 ? "Saved" : "Not set", "", click: async () =>
        {
            if (await Host.PromptAsync("ScreenScraper developer ID", null, RetroEight.DevId) is { } v) { Labs.Put("ss.devid", v.Trim()); Rebuild(); }
        });
        AddRow("Developer password", RetroEight.DevPassword.Length > 0 ? "Saved" : "Not set", "", click: async () =>
        {
            if (await Host.PromptSecretAsync("ScreenScraper developer password", null) is { } v) { Labs.Put("ss.devpassword", v); Rebuild(); }
        });
        AddRow("Create a free account", null, "", click: () => Host.Push(BrowserScreen.Open(Host, "https://www.screenscraper.fr/")));

        AddHeader("Get pictures");
        var games = RetroLounge.Visible();
        int missing = games.Count(g => RetroEight.Cover(g) == null);
        if (_status.Length > 0) AddRow(_status, null, "");
        if (_cts != null) AddRow("Stop", null, "", click: () => _cts?.Cancel());
        else
        {
            AddRow("Games without ScreenScraper art", missing + " games", "", click: () => _ = RunAsync(games, onlyMissing: true),
                subtitle: "About a second each, as ScreenScraper asks; you can leave this page");
            AddRow("Every game again", games.Count + " games", "", click: () => _ = RunAsync(games, onlyMissing: false));
        }
    }

    private async Task RunAsync(List<RetroGame> games, bool onlyMissing)
    {
        if (!RetroEight.Ready) { await Host.AskAsync("ScreenScraper", "Enter your username, password and the developer ID and password first.", "OK"); return; }
        _cts = new CancellationTokenSource();
        var progress = new Progress<(int Done, int Total, string Name)>(p => { _status = $"{p.Done} of {p.Total}" + (p.Name.Length > 0 ? "  ·  " + p.Name : ""); if (Host.IsTop(this)) Rebuild(); });
        try
        {
            var r = await Task.Run(() => RetroEight.ScrapeAsync(games, onlyMissing, progress, _cts.Token));
            _status = r.Error ?? $"Done: {r.Covers} covers and {r.Snaps} screenshots. {r.Missed} not found.";
        }
        catch (OperationCanceledException) { _status = "Stopped."; }
        finally { _cts = null; }
        if (Host.IsTop(this)) Rebuild();
    }
}

public sealed class RetroLooksScreen : ListScreen
{
    public RetroLooksScreen() : base("Shaders and bezels per console") { }

    protected override void Build()
    {
        if (RetroLounge.InstalledFor("retroarch") == null) { AddNote("These need RetroArch: get it in Retro Lounge > Emulators."); return; }
        var (folder, _) = RetroEight.ShaderKind();
        AddNote("For games played in RetroArch. Shaders come from RetroArch's own shaders folder (" + folder.Replace("shaders_", "") +
                " shaders, for its video driver). Bezels are the overlays in its overlays folder: RetroArch's Online Updater can download a pack of them.");
        var systems = RetroLounge.Visible().Select(g => g.System).Distinct().Select(RetroLounge.SystemOf).Where(s => s != null).Select(s => s!).OrderBy(s => s.Name).ToList();
        if (systems.Count == 0) AddRow("No retro games yet", null, "");
        foreach (var s in systems)
        {
            var system = s;
            AddHeader(s.Name);
            string shader = RetroEight.Shader(s.Id), overlay = RetroEight.Overlay(s.Id);
            AddRow("Shader", shader.Length == 0 ? "RetroArch's setting" : shader == "none" ? "None" : Path.GetFileNameWithoutExtension(shader), "",
                click: () => Host.Push(new RetroLookPickerScreen(system, shader: true)));
            AddRow("Bezel", overlay.Length == 0 ? "RetroArch's setting" : overlay == "none" ? "None" : Path.GetFileNameWithoutExtension(overlay), "",
                click: () => Host.Push(new RetroLookPickerScreen(system, shader: false)));
        }
    }

    public override void OnResumed() { base.OnResumed(); Rebuild(); }
}

public sealed class RetroLookPickerScreen : ListScreen
{
    private readonly RetroSystem _system;
    private readonly bool _shader;

    public RetroLookPickerScreen(RetroSystem system, bool shader) : base((shader ? "Shader – " : "Bezel – ") + system.Short)
    {
        _system = system;
        _shader = shader;
    }

    private void Choose(string value)
    {
        if (_shader) RetroEight.SetShader(_system.Id, value); else RetroEight.SetOverlay(_system.Id, value);
        Host.Pop(this);
    }

    protected override void Build()
    {
        string now = _shader ? RetroEight.Shader(_system.Id) : RetroEight.Overlay(_system.Id);
        string Mark(string v) => v == now ? "✓" : "";
        AddRow("RetroArch's own setting", Mark(""), "", click: () => Choose(""));
        AddRow("None", Mark("none"), "", click: () => Choose("none"));
        var all = _shader ? RetroEight.ShaderPresets() : RetroEight.Overlays();
        if (_shader && RetroEight.Suggested.TryGetValue(_system.Id, out var suggested))
        {
            var found = suggested.Where(all.Contains).ToList();
            if (found.Count > 0)
            {
                AddHeader("Suggested for " + _system.Short);
                foreach (var f in found) { var v = f; AddRow(Path.GetFileNameWithoutExtension(f), Mark(f), "", click: () => Choose(v), subtitle: f); }
            }
        }
        AddHeader(_shader ? "All shaders" : "All bezels");
        if (all.Count == 0) AddNote(_shader ? "No shaders found: RetroArch's Online Updater can download them (Update Slang Shaders / GLSL Shaders)." : "No bezels found: RetroArch's Online Updater > Update Overlays downloads a set.");
        foreach (var f in all.Take(300)) { var v = f; AddRow(Path.GetFileNameWithoutExtension(f), Mark(f), null, click: () => Choose(v), subtitle: Path.GetDirectoryName(f)?.Replace('\\', '/')); }
    }
}

// ==================================================================== controller tester

/// <summary>
/// Every button, trigger and stick, live. All buttons are tested here instead of moving around, so hold B (or press
/// View and Menu together) to leave. Pulling both triggers all the way rumbles.
/// </summary>
public sealed class ControllerTesterScreen : Screen, IPagePad
{
    /// <summary>The page reads the controller itself: SquareDeck's own button handling stays out of the way.</summary>
    public bool PadInPage => true;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Dictionary<ushort, Border> _buttons = new();
    private readonly Rectangle _lt = new(), _rt = new();
    private readonly Ellipse _ls = Dot(), _rs = Dot();
    private readonly TextBlock _lsText = Ui.Text("", 14, "SubtleBrush"), _rsText = Ui.Text("", 14, "SubtleBrush"), _status = Ui.Text("", 15, "SubtleBrush");
    private DateTime? _bHeld;
    private DateTime _rumbled;
    private double _restL, _restR;
    private const double Pad = 130;

    private static Ellipse Dot()
    {
        var e = new Ellipse { Width = 18, Height = 18 };
        e.SetResourceReference(Shape.FillProperty, "AccentBrush");
        return e;
    }

    public ControllerTesterScreen()
    {
        Border Key(string label, ushort mask, double width = 64)
        {
            var b = new Border { Width = width, Height = 44, CornerRadius = new CornerRadius(10), Margin = new Thickness(4), BorderThickness = new Thickness(2) };
            b.SetResourceReference(Border.BorderBrushProperty, "SubtleBrush");
            b.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            // On a handheld with its own set: the button as it's printed on the device.
            string own = label switch { "▲" or "▼" or "◀" or "▶" or "L3" or "R3" => "", _ => label };
            if (own.Length > 0 && OemGlyphs.Load(OemGlyphs.File(own)) is { } glyph)
                b.Child = new Image { Source = glyph, Height = 32, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            else
            {
                var t = Ui.Text(label, 15, "FgBrush", bold: true);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.VerticalAlignment = VerticalAlignment.Center;
                b.Child = t;
            }
            _buttons[mask] = b;
            return b;
        }
        StackPanel Row(params UIElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var i in items) p.Children.Add(i);
            return p;
        }
        FrameworkElement Trigger(string label, Rectangle fill)
        {
            var g = new Grid { Width = 150, Height = 26, Margin = new Thickness(8) };
            var track = new Border { CornerRadius = new CornerRadius(13) };
            track.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            g.Children.Add(track);
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.RadiusX = fill.RadiusY = 13;
            fill.Width = 0;
            fill.SetResourceReference(Shape.FillProperty, "AccentBrush");
            g.Children.Add(fill);
            var t = Ui.Text(label, 13, "FgBrush", bold: true);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(t);
            return g;
        }
        FrameworkElement Stick(string label, Ellipse dot, TextBlock text, ushort click)
        {
            var canvas = new Canvas { Width = Pad, Height = Pad };
            var ring = new Ellipse { Width = Pad, Height = Pad, StrokeThickness = 2 };
            ring.SetResourceReference(Shape.StrokeProperty, "FgBrush");
            ring.SetResourceReference(Shape.FillProperty, "CircleBrush");
            canvas.Children.Add(ring);
            canvas.Children.Add(dot);
            var p = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
            var head = Row(Ui.Text(label, 15, "FgBrush", bold: true));
            p.Children.Add(head);
            canvas.Margin = new Thickness(0, 8, 0, 6);
            p.Children.Add(canvas);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            p.Children.Add(text);
            p.Children.Add(Row(Key(label.StartsWith("Left") ? "L3" : "R3", click)));
            return p;
        }

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(Row(Trigger("LT", _lt), Key("LB", 0x0100, 90), new Border { Width = 120 }, Key("RB", 0x0200, 90), Trigger("RT", _rt)));
        // The middle: View / Guide / Menu, under a drawing of the handheld when there is one (so the test matches the
        // device in your hands), sits between the two sticks.
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        if (OemGlyphs.Load(OemGlyphs.Controller) is { } device)
        {
            var picture = new Image { Source = device, Width = 340, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            center.Children.Add(picture);
            var name = Ui.Text(Handheld.DisplayName, 14, "SubtleBrush");
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.Margin = new Thickness(0, 4, 0, 6);
            center.Children.Add(name);
        }
        center.Children.Add(Row(Key("View", 0x0020, 72), Key("Guide", 0x0400, 72), Key("Menu", 0x0010, 72)));
        var middle = Row(
            new StackPanel { Children = { Row(Key("▲", 0x0001)), Row(Key("◀", 0x0004), new Border { Width = 72 }, Key("▶", 0x0008)), Row(Key("▼", 0x0002)) }, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center },
            Stick("Left stick", _ls, _lsText, 0x0040),
            center,
            Stick("Right stick", _rs, _rsText, 0x0080),
            new StackPanel { Children = { Row(Key("Y", 0x8000)), Row(Key("X", 0x4000), new Border { Width = 72 }, Key("B", 0x2000)), Row(Key("A", 0x1000)) }, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        middle.Margin = new Thickness(0, 14, 0, 10);
        body.Children.Add(middle);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.TextAlignment = TextAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.MaxWidth = 900;
        body.Children.Add(_status);
        var fit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = body };
        SetPage("Controller tester", fit, "Hold B (or View + Menu) to leave      Both triggers fully: rumble");
        _timer.Tick += (_, _) => Tick();
    }

    public override void OnShown() { base.OnShown(); _timer.Start(); }
    public override void OnClosed() => _timer.Stop();
    public override bool OnAction(NavAction action) => true; // every button is being tested here
    public override bool OnRightStick(double x, double y, bool pressed) => true;

    private void Tick()
    {
        var s = Host.PadState;
        if (!s.Connected) { _status.Text = "No controller found. Connect one, or check it's switched to controller mode."; return; }
        foreach (var (mask, b) in _buttons)
        {
            bool down = (s.Buttons & mask) != 0;
            if (down) b.SetResourceReference(Border.BackgroundProperty, "AccentBrush"); else b.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        }
        _lt.Width = 150 * s.LeftTrigger / 255.0;
        _rt.Width = 150 * s.RightTrigger / 255.0;
        void Place(Ellipse dot, TextBlock text, short x, short y, ref double rest)
        {
            double fx = x / 32768.0, fy = y / 32768.0, m = Math.Sqrt(fx * fx + fy * fy);
            Canvas.SetLeft(dot, Pad / 2 + fx * Pad / 2 - 9);
            Canvas.SetTop(dot, Pad / 2 - fy * Pad / 2 - 9);
            // Off-center at rest (small readings only): the drift check, smoothed.
            if (m < 0.25) rest = rest * 0.9 + m * 0.1;
            text.Text = $"X {fx * 100:+0;-0;0}%  Y {fy * 100:+0;-0;0}%  ·  at rest {rest * 100:0}%";
        }
        Place(_ls, _lsText, s.LeftX, s.LeftY, ref _restL);
        Place(_rs, _rsText, s.RightX, s.RightY, ref _restR);
        string Verdict(double v) => v < 0.06 ? "fine" : v < 0.12 ? "a little drift" : "noticeable drift";
        _status.Text = $"Leave the sticks alone for a few seconds to check drift: left {Verdict(_restL)}, right {Verdict(_restR)}. Up to about 6% is normal.";

        if (s.LeftTrigger > 245 && s.RightTrigger > 245 && DateTime.Now - _rumbled > TimeSpan.FromSeconds(1.2))
        {
            _rumbled = DateTime.Now;
            Host.RumbleTest(100);
        }
        bool view = (s.Buttons & 0x0020) != 0, menu = (s.Buttons & 0x0010) != 0;
        if ((s.Buttons & 0x2000) != 0) _bHeld ??= DateTime.Now; else _bHeld = null;
        if ((view && menu) || (_bHeld is DateTime held && DateTime.Now - held > TimeSpan.FromSeconds(1.5)))
        {
            _timer.Stop();
            Host.Pop(this);
        }
    }
}

// ==================================================================== Windows Update guard

public sealed class UpdateGuardScreen : ListScreen
{
    private bool? _pending;

    public UpdateGuardScreen() : base("Windows Update guard") { }

    public override void OnShown()
    {
        base.OnShown();
        _ = Task.Run(WindowsUpdate.RestartPending).ContinueWith(t => { _pending = t.IsCompletedSuccessfully && t.Result; Rebuild(); }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    protected override void Build()
    {
        AddRow(_pending switch { true => "A restart is waiting", false => "No restart waiting", _ => "Checking…" }, null, _pending == true ? "" : "",
            subtitle: _pending == true ? "Restart from the Power menu when it suits you" : null);
        var hours = Eight.ActiveHours();
        AddRow("Active hours", hours is (int a, int b) ? $"{Hour(a)} to {Hour(b)}" : "Not set", "", click: () => _ = SetHoursAsync(),
            subtitle: "Windows doesn't restart by itself between these hours. Pick the hours you usually play");
        var paused = Eight.PausedUntil();
        AddRow("Pause updates for a week", paused is DateTime p ? "Paused until " + p.ToString("MMM d") : null, "", click: () => _ = PauseAsync(),
            subtitle: "Windows' own pause, as in Settings > Windows Update");
        AddRow("Open Windows Update", null, "", click: () => ProcessUtil.OpenOutside("ms-settings:windowsupdate"));
        AddNote("While a game runs, SquareDeck checks every ten minutes and tells you if Windows wants to restart. Changing active hours or pausing asks Windows for permission.");
    }

    private static string Hour(int h) => DateTime.Today.AddHours(h).ToString("h tt");

    private async Task SetHoursAsync()
    {
        (int, int)[] presets = { (8, 2), (10, 4), (12, 6), (6, 0) };
        int c = await Host.AskAsync("Active hours", "Windows allows up to 18 hours.", presets.Select(p => $"{Hour(p.Item1)} to {Hour(p.Item2)}").ToArray());
        if (c < 0) return;
        bool ok = await Eight.SetActiveHoursAsync(presets[c].Item1, presets[c].Item2);
        if (!ok) await Host.AskAsync("Active hours", "That didn't go through (Windows' permission was refused, or the setting is managed by your organization).", "OK");
        Rebuild();
    }

    private async Task PauseAsync()
    {
        bool ok = await Eight.PauseUpdatesAsync(7);
        if (!ok) await Host.AskAsync("Pause updates", "That didn't go through (Windows' permission was refused, or the setting is managed by your organization).", "OK");
        Rebuild();
    }
}

// ==================================================================== driver check

public sealed class DriverCheckScreen : ListScreen
{
    public DriverCheckScreen() : base("Driver check") { }

    protected override void Build()
    {
        AddNote("Versions and dates as Windows has them. AMD and ASUS don't publish their newest versions in a way apps can read, " +
                "so each row opens the official download page to compare. ASUS's own updates also come through Armoury Crate's Update Center.");
        var drivers = Eight.Drivers();
        foreach (var group in drivers.GroupBy(d => d.Kind).OrderBy(g => g.Key switch { "graphics" => 0, "bios" => 1, _ => 2 }))
        {
            AddHeader(group.Key switch { "graphics" => "Graphics", "bios" => "BIOS", _ => "ASUS drivers" });
            foreach (var d in group.OrderBy(d => d.Name))
            {
                var driver = d;
                int? age = d.Date is DateTime when ? (int)(DateTime.Now - when).TotalDays : null;
                string old = age is > 180 ? "  ·  over 6 months old: worth checking" : "";
                AddRow(d.Name, d.Version, age is > 180 ? "" : "", click: () => WebLinks.Open(Eight.DownloadPage(driver), Host),
                    subtitle: (d.Date is DateTime dt ? dt.ToString("MMM d, yyyy") : "Date unknown") + (d.Maker.Length > 0 ? "  ·  " + d.Maker : "") + old);
            }
        }
        if (drivers.Count == 0) AddRow("Couldn't read the driver list", null, "");
    }
}

// ==================================================================== startup apps

public sealed class StartupAppsScreen : ListScreen
{
    public StartupAppsScreen() : base("Startup apps") { }

    protected override void Build()
    {
        AddNote("Apps that start when you sign in. Turning one off doesn't uninstall it, and it can be turned back on here or in Task Manager. Fewer apps at sign-in means a faster start.");
        var apps = Eight.StartupApps();
        foreach (var group in apps.GroupBy(a => a.Where))
        {
            AddHeader(group.Key);
            foreach (var a in group)
            {
                var app = a;
                AddRow(a.Name, a.Enabled ? "On" : "Off", a.Enabled ? "" : "", click: async () =>
                {
                    if (!app.CanChange)
                    {
                        if (await Host.AskAsync(app.Name, "This one starts for everyone on this PC, so Windows' own settings change it (they ask for permission).", "Open Startup apps", "Cancel") == 0)
                            ProcessUtil.OpenOutside("ms-settings:startupapps");
                        return;
                    }
                    if (!Eight.SetStartupApp(app, !app.Enabled)) await Host.AskAsync(app.Name, "That couldn't be changed.", "OK");
                    Rebuild();
                }, subtitle: a.Command.Length > 90 ? a.Command[..90] + "…" : a.Command);
            }
        }
        if (apps.Count == 0) AddRow("Nothing starts with Windows", null, "");
        AddRow("Open Windows' Startup apps", null, "", click: () => ProcessUtil.OpenOutside("ms-settings:startupapps"));
    }
}

// ==================================================================== visual modes (ASUS GameVisual)

public sealed class VisualModesScreen : ListScreen
{
    public VisualModesScreen() : base("Visual modes") { }

    private string? _message;

    protected override void Build()
    {
        if (!VisualModes.Available)
        {
            AddNote("ASUS's display tool wasn't found on this PC. It comes with the ASUS System Control Interface driver and needs the ASUS Optimization service, both part of Armoury Crate's install on ASUS handhelds and laptops.");
            return;
        }
        if (_message != null) AddNote(_message);

        bool? oled = AmdOled.IsOn();
        if (oled != null)
        {
            AddHeader("OLED screen");
            AddRow("AMD OLED Power Optimization", oled == true ? "On" : "Off", "\uE7F4",
                click: oled == true ? (Action)(async () =>
                {
                    if (await Host.AskAsync("Turn off OLED Power Optimization?",
                        "AMD's driver manages the OLED panel's colors to save a little battery, and while it does, visual modes do nothing. Turning it off lets your mode apply. It can be turned back on in AMD Software: Display.",
                        "Turn off", "Cancel") != 0) return;
                    if (!AmdOled.TurnOff()) { _message = "AMD's driver didn't accept the change. Try AMD Software: Display > OLED Power Optimization."; Rebuild(); return; }
                    _message = await VisualModes.ApplyAsync(VisualModes.SavedMode, VisualModes.SavedTemperature) ?? "OLED Power Optimization is off, and your visual mode is applied.";
                    Rebuild();
                }) : null,
                subtitle: oled == true ? "Visual modes have no effect while it's on. Select to turn it off" : "Off, so visual modes apply");
        }

        int current = VisualModes.SavedMode, temp = VisualModes.SavedTemperature;
        AddHeader("Mode");
        foreach (var m in VisualModes.Modes)
        {
            var mode = m;
            AddRow(m.Name, m.Id == current ? "\u2713" : null, "\uE790", click: async () =>
            {
                _message = await VisualModes.ApplyAsync(mode.Id, temp);
                Rebuild();
            }, subtitle: m.About);
        }

        var uses = VisualModes.Modes.First(x => x.Id == current).UsesTemperature;
        AddHeader("Color temperature");
        int index = Math.Max(0, Array.FindIndex(VisualModes.Temperatures, t => t.Value == temp));
        if (index < 0 || VisualModes.Temperatures[index].Value != temp) index = Array.FindIndex(VisualModes.Temperatures, t => t.Value == VisualModes.Neutral);
        AddRow("Color temperature", uses ? VisualModes.Temperatures[index].Name : "Not used by " + VisualModes.Modes.First(x => x.Id == current).Name, "\uE706",
            click: uses ? (Action)(async () =>
            {
                var next = VisualModes.Temperatures[(index + 1) % VisualModes.Temperatures.Length];
                _message = await VisualModes.ApplyAsync(current, next.Value);
                Rebuild();
            }) : null,
            subtitle: uses ? "Select to step from warmest to coldest" : "Eyecare and Disabled set their own");

        AddNote("Your mode is put back each time SquareDeck starts. Visual modes don't work while HDR is on, or when only an external display is in use. Armoury Crate may change the mode too: the last one set wins.");
    }
}

// ==================================================================== CPU core configuration (ASUS firmware)

public sealed class CpuCoresScreen : ListScreen
{
    public CpuCoresScreen() : base("CPU core configuration") { }

    private int? _p, _e;   // the choice on this page, before Apply
    private string? _message;

    protected override void Build()
    {
        var now = AsusAcpi.Cores();
        var max = AsusAcpi.Cores(AsusAcpi.CoresMax);
        if (now is not { } cur || max is not { } top)
        {
            AddNote("This device's firmware doesn't offer core configuration (it needs an ASUS handheld or laptop whose BIOS has it, and the ASUS System Control Interface driver).");
            return;
        }
        int pMax = top.P > 0 ? top.P : Math.Max(cur.P, 1), eMax = top.E > 0 ? top.E : cur.E;
        int pMin = 1, eMin = 0;
        if (AsusAcpi.Cores(AsusAcpi.CoresMin) is { } low && low.P > 0) { pMin = Math.Min(low.P, pMax); eMin = Math.Min(low.E, eMax); }
        int p = Math.Clamp(_p ?? cur.P, pMin, pMax), e = Math.Clamp(_e ?? cur.E, eMin, eMax);

        AddNote(_message ?? $"Enabled now: {cur.P} P-cores and {cur.E} E-cores, of {pMax} and {eMax}. P-cores are the fast ones; E-cores are smaller and use less power. Fewer cores can stretch the battery in light or older games; demanding games want them all.");
        AddHeader("Cores");
        AddRow("P-cores", p.ToString(), "\uE950", click: () => { _p = p >= pMax ? pMin : p + 1; Rebuild(); }, subtitle: $"Select to change ({pMin}\u2013{pMax})");
        if (eMax > 0)
            AddRow("E-cores", e.ToString(), "\uE950", click: () => { _e = e >= eMax ? eMin : e + 1; Rebuild(); }, subtitle: $"Select to change ({eMin}\u2013{eMax})");
        bool changed = p != cur.P || e != cur.E;
        AddRow("Apply", changed ? $"{p} P + {e} E" : null, "\uE73E", click: changed ? (Action)(async () =>
        {
            int c = await Host.AskAsync("Apply and restart?",
                $"The firmware enables {p} P-cores and {e} E-cores from the next restart. Save your work first. To undo, come back here and choose {pMax} and {eMax}.",
                "Apply and restart now", "Apply, restart later", "Cancel");
            if (c == 2 || c < 0) return;
            if (!AsusAcpi.SetCores(e, p)) { _message = "The firmware didn't accept that. Nothing was changed."; Rebuild(); return; }
            if (c == 0) { Power.Restart(); return; }
            _p = _e = null;
            _message = $"Saved: {p} P-cores and {e} E-cores from the next restart.";
            Rebuild();
        }) : null, subtitle: changed ? "Needs a restart" : "Change a count above first");
        AddRow("All cores", $"{pMax} P + {eMax} E", "\uE777", click: () => { _p = pMax; _e = eMax; Rebuild(); }, subtitle: "Back to the full CPU (then Apply)");
        AddNote("The setting lives in the device's firmware, so it stays even if SquareDeck is closed or uninstalled, and Armoury Crate shows the same choice.");
    }
}

// ==================================================================== custom fan curve (ASUS firmware)

public sealed class CustomFanScreen : ListScreen
{
    public CustomFanScreen() : base("Custom fan curve") { }

    private byte[] _curve = AllyControl.CustomCurve;
    private string? _message;
    private const int MinTemp = 30, MaxTemp = 100;

    protected override void Build()
    {
        if (!AsusAcpi.Available)
            AddNote("SquareDeck couldn't reach ASUS's firmware driver (ATKACPI), so the curve can be drawn but not applied on this PC.");
        AddNote(_message ?? "Eight points: the fan speed at each temperature. Highlight a point, then \u2190 \u2192 changes its fan speed and LB / RB its temperature (or select it to step the speed up). Speeds never go down as it gets hotter.");
        Rows.Children.Add(Chart());

        bool inUse = Host.Settings.AllyFan == "custom";
        AddRow(inUse ? "In use" : "Use this curve", inUse ? "\u2713" : null, "\uE9CA", click: () =>
        {
            AllyControl.CustomCurve = _curve;
            Host.Settings.AllyFan = "custom";
            Host.Settings.Save();
            _ = Task.Run(AllyControl.ApplyPower);
            _message = "Applied. Fans follow your curve now, and again each time SquareDeck starts. A game profile's own fan curve still comes first while that game runs.";
            Rebuild();
        }, subtitle: inUse ? "Changes below are applied as you make them" : "Makes Custom the fan curve in Settings");

        AddHeader("Points");
        for (int i = 0; i < 8; i++)
        {
            int index = i;
            var row = AddRow($"Point {i + 1}", $"{_curve[i]} \u00B0C  \u2192  {(_curve[i + 8] == 0 ? "Off" : _curve[i + 8] + "%")}", "\uE9CA",
                click: () => Change(index, speed: _curve[index + 8] >= 95 ? -_curve[index + 8] : 5, temp: 0));
            row.Tag = index;
        }
        AddHeader("Start from");
        foreach (var preset in AllyControl.FanCurves.Where(f => f.Duties != null))
        {
            var p = preset;
            AddRow(p.Name, null, "\uE72C", click: () =>
            {
                var c = new byte[16];
                Array.Copy(AsusAcpi.DefaultCpuCurve, c, 8);
                Array.Copy(p.Duties!, 0, c, 8, 8);
                _curve = c;
                Save();
                Rebuild();
            }, subtitle: p.About);
        }
        AddNote("The firmware caps speeds at 99%. Running too quiet makes the APU hotter, and it slows itself down to stay safe; Windows and Armoury Crate can't harm it either way.");
    }

    public override bool OnAction(NavAction action)
    {
        if (Keyboard.FocusedElement is Button { Tag: int index })
        {
            switch (action)
            {
                case NavAction.Left: Change(index, -5, 0); return true;
                case NavAction.Right: Change(index, 5, 0); return true;
                case NavAction.LB: Change(index, 0, -2); return true;
                case NavAction.RB: Change(index, 0, 2); return true;
            }
        }
        return base.OnAction(action);
    }

    private void Change(int i, int speed, int temp)
    {
        var c = (byte[])_curve.Clone();
        // Temperatures stay in order, at least 2 °C apart; speeds never drop as it gets hotter.
        int lo = i == 0 ? MinTemp : c[i - 1] + 2, hi = i == 7 ? MaxTemp : c[i + 1] - 2;
        c[i] = (byte)Math.Clamp(c[i] + temp, lo, Math.Max(lo, hi));
        c[i + 8] = (byte)Math.Clamp(c[i + 8] + speed, 0, 100);
        for (int k = i + 1; k < 8; k++) c[k + 8] = Math.Max(c[k + 8], c[i + 8]);
        for (int k = i - 1; k >= 0; k--) c[k + 8] = Math.Min(c[k + 8], c[i + 8]);
        _curve = c;
        Save();
        Rebuild();
    }

    private void Save()
    {
        AllyControl.CustomCurve = _curve;
        if (Host.Settings.AllyFan == "custom") _ = Task.Run(AllyControl.ApplyPower);
    }

    /// <summary>The curve as a graph: temperature across (20–110 °C), fan speed up (0–100%).</summary>
    private UIElement Chart()
    {
        const double W = 900, H = 300, L = 60, B = 36, T = 14, R = 20;
        double X(double t) => L + (t - 20) / 90 * (W - L - R);
        double Y(double d) => T + (1 - d / 100) * (H - T - B);
        var canvas = new Canvas { Width = W, Height = H };
        Brush faint = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        for (int t = 20; t <= 110; t += 10)
        {
            canvas.Children.Add(new Line { X1 = X(t), X2 = X(t), Y1 = T, Y2 = H - B, Stroke = faint, StrokeThickness = 1 });
            var label = new TextBlock { Text = t.ToString(), FontSize = 14, Opacity = 0.7 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
            Canvas.SetLeft(label, X(t) - 9); Canvas.SetTop(label, H - B + 6);
            canvas.Children.Add(label);
        }
        for (int d = 0; d <= 100; d += 25)
        {
            canvas.Children.Add(new Line { X1 = L, X2 = W - R, Y1 = Y(d), Y2 = Y(d), Stroke = faint, StrokeThickness = 1 });
            var label = new TextBlock { Text = d == 0 ? "Off" : d + "%", FontSize = 14, Opacity = 0.7 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
            Canvas.SetLeft(label, 6); Canvas.SetTop(label, Y(d) - 10);
            canvas.Children.Add(label);
        }
        var line = new Polyline { StrokeThickness = 3 };
        line.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        for (int i = 0; i < 8; i++) line.Points.Add(new Point(X(_curve[i]), Y(Math.Min((int)_curve[i + 8], 100))));
        canvas.Children.Add(line);
        int focused = Keyboard.FocusedElement is Button { Tag: int f } ? f : -1;
        for (int i = 0; i < 8; i++)
        {
            double size = i == focused ? 18 : 12;
            var dot = new Ellipse { Width = size, Height = size };
            dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
            Canvas.SetLeft(dot, X(_curve[i]) - size / 2); Canvas.SetTop(dot, Y(Math.Min((int)_curve[i + 8], 100)) - size / 2);
            canvas.Children.Add(dot);
        }
        var title = new TextBlock { Text = "Fan speed by temperature (\u00B0C)", FontSize = 15, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 6) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 16), HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(title);
        panel.Children.Add(canvas);
        return panel;
    }
}
