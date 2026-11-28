using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>A news post or patch notes, shown as plain text so it can be read without a browser.</summary>
public sealed class NewsReaderScreen : ListScreen
{
    private readonly NewsItem _post;

    public NewsReaderScreen(NewsItem post) : base(post.Title, "Right stick or Up / Down  Scroll        {Back}  Back")
    {
        _post = post;
    }

    protected override void Build()
    {
        AddNote((_post.Feed.Length > 0 ? _post.Feed + "  ·  " : "") + _post.Date.ToString("MMMM d, yyyy"));
        AddRow("Open the original post (pictures and videos)", glyph: "", click: () => ProcessUtil.Open(_post.Url));

        var body = _post.Body.Length > 0 ? _post.Body : _post.Snippet;
        foreach (var paragraph in body.Split('\n'))
        {
            var text = paragraph.Trim();
            if (text.Length == 0) continue;
            var block = Ui.Text(text, 19);
            block.TextWrapping = TextWrapping.Wrap;
            block.TextTrimming = TextTrimming.None;
            block.Margin = new Thickness(4, 6, 12, 6);
            Rows.Children.Add(block);
        }
    }
}

/// <summary>
/// Settings > System > Xbox mode home app: SquareDeck as what Xbox mode (Windows' gaming full screen experience)
/// opens to, registered by SquareDeck itself (Services/HomeApp). AnyFSE is still offered for those who prefer it.
/// </summary>
public sealed class HomeAppScreen : ListScreen
{
    private string? _busy;
    private HomeApp.Registration? _package;
    private bool _checked;

    public HomeAppScreen() : base("Xbox mode home app") { }

    public override void OnShown() => _ = CheckAsync();
    public override void OnResumed() => _ = CheckAsync();

    private async Task CheckAsync()
    {
        try { _package = await Task.Run(HomeApp.FindAsync); }
        catch (Exception) { _package = null; }
        _checked = true;
        if (Host.IsTop(this)) Rebuild();
    }

    protected override void Build()
    {
        var s = Host.Settings;
        bool supported = HomeApp.WindowsSupports, devMode = HomeApp.DeveloperMode, files = HomeApp.FilesPresent;
        bool on = HomeApp.IsHomeApp && _package is { IsThisCopy: true };

        AddNote("Xbox mode (the full screen experience on handhelds such as the ROG Xbox Ally) opens a home app instead of " +
                "the desktop. SquareDeck can be that app, so the device starts straight into it and the Home button in Game " +
                "Bar brings it back. No other tool is needed.");

        if (_busy != null) { AddNote(_busy); return; }

        string status = !supported ? "Needs Windows 11 24H2 or later"
                      : !files ? "Files missing"
                      : !_checked ? "Checking…"
                      : on ? "SquareDeck (on)"
                      : _package is { IsThisCopy: false } ? "Registered from another folder"
                      : HomeApp.CurrentHomeApp is { Length: > 0 } other ? (other.StartsWith("Microsoft.GamingApp") ? "Xbox app" : "Another app")
                      : "None";
        AddRow("Home app now", status, "");

        if (supported && files)
        {
            AddRow("Use SquareDeck as the Xbox mode home app", on ? "On" : "Off", "", click: () => _ = SetAsync(!on),
                subtitle: devMode || on ? "Registers this copy of SquareDeck with Windows and picks it as the home app"
                                        : "Needs Developer Mode first (below)");
            AddRow("Open Xbox mode when Windows starts", HomeApp.StartsAtSignIn ? "On" : "Off", "", click: () =>
            {
                HomeApp.StartsAtSignIn = !HomeApp.StartsAtSignIn;
                Rebuild();
            }, subtitle: "Boots straight into SquareDeck");
            AddRow("Developer Mode", devMode ? "On" : "Off", "", click: () => ProcessUtil.Open("ms-settings:developers"),
                subtitle: devMode ? "Needed while SquareDeck is being registered" : "Turn it on in Settings > System > For developers");
            AddRow("Open Xbox mode settings in Windows", glyph: "", click: () => ProcessUtil.Open(HomeApp.WindowsSettingsPage),
                subtitle: "Settings > Gaming > Xbox mode: home app, startup and startup apps");
        }

        AddHeader("How it works");
        AddNote("Windows only lists packaged apps as home apps. SquareDeck registers its own folder as a small package " +
                "(nothing is copied or installed anywhere else) and Windows then starts this same SquareDeck.exe. Your settings, " +
                "library and artwork stay where they are.");
        AddNote("The permission Windows checks for belongs to Microsoft, so apps outside the Microsoft Store can only use it with " +
                "Developer Mode on. AnyFSE and the other home apps work the same way. SquareDeck keeps working if you turn " +
                "Developer Mode off afterwards; it's only needed to register it again.");
        AddNote("If you move SquareDeck to another folder, it registers again from there the next time it starts (with Developer " +
                "Mode on). The folder must be on an NTFS drive. Turning this off removes the registration and gives Windows back the " +
                "home app it had before (the Xbox app if there was none).");
        AddNote("This folder: " + HomeApp.Folder);

        AddHeader("Other ways");
        AddRow("Use AnyFSE instead", AnyFse.IsInstalled() ? "Installed" : "Not installed", "", click: () => Host.Push(new AnyFseScreen()),
            subtitle: "A separate tool that can start any launcher from Xbox mode");
    }

    private async Task SetAsync(bool on)
    {
        var s = Host.Settings;
        if (on && !HomeApp.DeveloperMode)
        {
            if (await Host.AskAsync("Turn on Developer Mode first",
                    "Windows only accepts a home app from outside the Microsoft Store while Developer Mode is on " +
                    "(Settings > System > For developers). Turn it on, come back, and switch this on again.",
                    "Open Developer Mode settings", "Cancel") == 0)
                ProcessUtil.Open("ms-settings:developers");
            return;
        }

        _busy = on ? "Registering SquareDeck with Windows…" : "Removing SquareDeck's registration…";
        Rebuild();
        try
        {
            bool picked = true;
            if (on)
            {
                var package = await Task.Run(() => HomeApp.RegisterAsync());
                s.HomeAppOn = true;
                s.Save();
                picked = await HomeApp.MakeHomeAppAsync(package, s);
                // SquareDeck replaces AnyFSE's job: stop AnyFSE from also starting it.
                if (AnyFse.IsInstalled() && AnyFse.LaunchesUs()) AnyFse.UseUs(false);
            }
            else
            {
                await HomeApp.RestoreHomeAppAsync(s);
                await Task.Run(HomeApp.UnregisterAsync);
                s.HomeAppOn = false;
            }
            s.Save();
            _busy = null;
            await CheckAsync();
            if (on && !picked)
            {
                // Registered, but Windows wouldn't let SquareDeck pick itself: one step in Windows' own settings.
                if (await Host.AskAsync("One step left: pick SquareDeck in Windows",
                        "SquareDeck is registered, but Windows only lets its own settings change the home app on this device. " +
                        "In Settings > Gaming > Xbox mode, under \"Choose home app\", select SquareDeck.",
                        "Open Xbox mode settings", "Later") == 0)
                    ProcessUtil.Open(HomeApp.WindowsSettingsPage);
                return;
            }
            if (on && !HomeApp.StartsAtSignIn &&
                await Host.AskAsync("SquareDeck is the Xbox mode home app",
                    "Xbox mode now opens SquareDeck, and the Home button in Game Bar brings it back. Also start Xbox mode " +
                    "(and so SquareDeck) when Windows starts?", "Yes, at startup", "Not now") == 0)
            {
                HomeApp.StartsAtSignIn = true;
                Rebuild();
            }
        }
        catch (Exception ex)
        {
            _busy = null;
            Rebuild();
            await Host.AskAsync(on ? "Couldn't make SquareDeck the home app" : "Couldn't remove the registration", ex.Message, "OK");
        }
    }
}

/// <summary>AnyFSE: what it is, how to get it, and a way to open it once it is installed.</summary>
public sealed class AnyFseScreen : ListScreen
{
    private string? _busy;

    public AnyFseScreen() : base("Xbox mode and AnyFSE") { }

    public override void OnResumed() => Rebuild();

    protected override void Build()
    {
        bool installed = AnyFse.IsInstalled();

        AddNote("AnyFSE is a free tool that lets a launcher of your choice be the Home app of Windows' gaming " +
                "full-screen experience (the console-style mode on handhelds such as the ROG Ally). With it, this " +
                "launcher can be what the device opens to.");
        AddRow("Status", installed ? "Installed" : "Not installed", "");
        if (_busy != null) { AddNote(_busy); return; }

        if (installed)
        {
            // The whole job in one switch: AnyFSE starts SquareDeck, and Windows uses AnyFSE as the Xbox mode home.
            bool ours = AnyFse.LaunchesUs(), home = AnyFse.IsHomeApp();
            AddHeader("Xbox mode");
            AddRow("Use SquareDeck as the Xbox mode home app", ours ? "On" : "Off", "\uE7FC", click: () => _ = SetXboxModeAsync(!ours),
                subtitle: ours && !home ? "One step left: choose AnyFSE as the Home app in Windows" : "Starts SquareDeck when Xbox mode opens");
            AddRow("Home app in Windows", home ? "AnyFSE (done)" : "Not AnyFSE yet", "\uE713",
                click: () => ProcessUtil.Open(HomeApp.WindowsSettingsPage),
                subtitle: "Settings > Gaming > Full screen experience > Home app");
            AddRow("Open Xbox mode when Windows starts", AnyFse.StartsAtSignIn ? "On" : "Off", "\uE7E8", click: () =>
            {
                AnyFse.StartsAtSignIn = !AnyFse.StartsAtSignIn;
                Rebuild();
            });
            AddHeader("AnyFSE");
            AddRow("Open AnyFSE", glyph: "", click: Open);
            AddRow("Update AnyFSE to the latest version", glyph: "", click: Install);
        }
        else
        {
            AddRow("Download and install AnyFSE", glyph: "", click: Install);
        }
        AddRow("Open the AnyFSE releases page", glyph: "", click: () => ProcessUtil.Open(AnyFse.ReleasesPage));

        AddHeader("How to set it up");
        AddNote("1.  Install AnyFSE with the button above. It downloads AnyFSE.Installer.exe from the project's GitHub " +
                "releases and runs it. The installer needs administrator rights, so Windows will ask for permission.");
        AddNote("2.  Open AnyFSE (from here or the Start menu) and choose a custom program as the launcher. Point it at:\n" +
                (Environment.ProcessPath ?? "SquareDeck.exe"));
        AddNote("3.  In Windows, go to Settings > Gaming > Full screen experience and set AnyFSE as the Home app. " +
                "AnyFSE only works while it is selected there.");
        AddRow("Open Windows Gaming settings", glyph: "", click: () => ProcessUtil.Open(HomeApp.WindowsSettingsPage));

        AddHeader("Before you install");
        AddNote("The full-screen experience must be available on your device: a supported handheld, or enabled with a " +
                "third-party tool the AnyFSE page links to.");
        AddNote("Microsoft Defender may flag the AnyFSE installer. Its author says this is because of the process " +
                "monitoring it uses for an optional button-remapping feature. It is a separate project, not part of this " +
                "launcher, so decide for yourself whether you trust it.");
    }

    private async Task SetXboxModeAsync(bool on)
    {
        if (!AnyFse.UseUs(on))
        {
            await Host.AskAsync("Couldn't change AnyFSE's settings",
                "Its settings file wasn't found. Open AnyFSE once (so it creates them), then try again, or choose a custom launcher " +
                "in AnyFSE pointing at:\n" + (Environment.ProcessPath ?? "SquareDeck.exe"), "OK");
            return;
        }
        if (on && !AnyFse.IsHomeApp() &&
            await Host.AskAsync("Almost done", "AnyFSE will now start SquareDeck. Last step: in Windows, choose AnyFSE as the Home app " +
                "(Settings > Gaming > Full screen experience). Open that page now?", "Open Windows settings", "Later") == 0)
            ProcessUtil.Open(HomeApp.WindowsSettingsPage);
        Rebuild();
    }

    private async void Open()
    {
        if (!await AnyFse.OpenAsync())
            await Host.AskAsync("Couldn't open AnyFSE", "Its Start menu entry wasn't found. Try opening it from the Start menu.", "OK");
    }

    private async void Install()
    {
        int choice = await Host.AskAsync("Download and run the AnyFSE installer?",
            "The newest AnyFSE.Installer.exe is downloaded from github.com/ashpynov/AnyFSE and started. Windows asks for " +
            "administrator permission, and Microsoft Defender may warn about it.",
            "Cancel", "Download and install");
        if (choice != 1) return;

        _busy = "Downloading the AnyFSE installer…";
        Rebuild();
        try
        {
            var (path, version) = await Task.Run(() => AnyFse.DownloadInstallerAsync(CancellationToken.None));
            _busy = $"Running the AnyFSE {version} installer… follow its prompts.";
            Rebuild();
            if (!ProcessUtil.Start(path)) throw new InvalidOperationException("The installer was downloaded but couldn't be started.");
        }
        catch (Exception ex)
        {
            _busy = null;
            Rebuild();
            if (await Host.AskAsync("Couldn't install AnyFSE", ex.Message, "Open the releases page", "Close") == 0)
                ProcessUtil.Open(AnyFse.ReleasesPage);
            return;
        }

        // The installer runs on its own; check back when the user returns to this page.
        await Task.Delay(4000);
        _busy = null;
        if (Host.IsTop(this)) Rebuild();
    }
}

/// <summary>Larger text, high contrast, less motion, and Windows' color filters.</summary>
public sealed class AccessibilityScreen : ListScreen
{
    private static readonly int[] Sizes = { 100, 115, 130, 150 };

    public AccessibilityScreen() : base("Accessibility") { }

    protected override void Build()
    {
        var s = Host.Settings;

        AddHeader("Seeing");
        AddRow("Text and control size", s.TextScale + "%", "", click: () =>
        {
            s.TextScale = Sizes[(Array.IndexOf(Sizes, s.TextScale) + 1) % Sizes.Length];
            Apply();
            Host.Pop(this);                       // pages take the new size when they open,
            Host.Push(new AccessibilityScreen()); // so reopen this one to show it straight away
        });
        AddNote("Makes text and buttons larger on every page. The home screen keeps its layout; its tiles are already large.");

        string contrast = s.ThemeMode switch { "hc-dark" => "White on black", "hc-light" => "Black on white", _ => "Off" };
        AddRow("High contrast", contrast, "", click: () =>
        {
            // Off -> white on black -> black on white -> off (back to following Windows)
            s.ThemeMode = s.ThemeMode switch { "hc-dark" => "hc-light", "hc-light" => "system", _ => "hc-dark" };
            s.DarkTheme = Theme.ShouldBeDark(s);
            Apply();
        });
        AddNote("Pure black and white with a bright highlight and no gray text. Wallpapers and hero backgrounds are " +
                "best turned off with it (Settings).");

        AddRow("Color filters (Windows)", glyph: "", click: () => ProcessUtil.Open("ms-settings:easeofaccess-colorfilter"));
        AddNote("Grayscale, inverted and color-blindness filters are a Windows feature that applies to the whole screen, " +
                "games included. This opens its page; the shortcut Win+Ctrl+C can then switch it on and off.");

        AddHeader("Motion");
        AddRow("Reduce motion", s.ReduceMotion ? "On" : "Off", "", click: () =>
        {
            s.ReduceMotion = !s.ReduceMotion;
            Apply();
        });
        AddNote("Stops the pulsing highlight, page slides, fades, animated covers and the startup animation.");

        AddHeader("Related");
        AddRow("Vibration, sounds and the rest", glyph: "", click: () => Host.Pop(this));
    }

    private void Apply()
    {
        Host.Settings.Save();
        Host.ApplyLook();
        Host.ApplySettings();
        Rebuild();
    }
}

/// <summary>Everything about interface sounds: on / off, the set, loudness, and which kinds play.</summary>
public sealed class SoundsScreen : ListScreen
{
    private static readonly int[] Volumes = { 25, 50, 75, 100 };

    public SoundsScreen() : base("Interface sounds") { }

    protected override void Build()
    {
        var s = Host.Settings;
        static string OnOff(bool on) => on ? "On" : "Off";

        AddRow("Interface sounds", OnOff(s.UiSounds), "\uE767", click: () => Change(() => s.UiSounds = !s.UiSounds));
        AddRow("Sound set", UiSound.SetName(s.SoundSet), "\uE8D6", click: ChooseSet);
        if (SoundPacks.Find(s.SoundSet) is { } pack)
            AddNote("Sound pack \u201C" + pack.Name + "\u201D" + (pack.Author.Length > 0 ? " by " + pack.Author : "") +
                    ". Sounds a pack doesn't include use the Classic set.");
        if (s.SoundSet == "custom")
            AddNote("Custom sounds come from " + (s.SoundFolder.Length > 0 ? s.SoundFolder : "(no folder chosen)") +
                    ". Put WAV files there named " + string.Join(", ", UiSound.CustomFiles) +
                    ". Any that are missing use the Classic sound.");
        AddRow("Navigation sound", s.NavSoundSet.Length == 0 ? "Same as the sound set" : UiSound.SetName(s.NavSoundSet), "\uE7C9", click: () => _ = ChooseNavAsync());
        AddRow("Loudness", s.SoundVolume + "%", "\uE995", click: () =>
            Change(() => s.SoundVolume = Volumes[(Array.IndexOf(Volumes, s.SoundVolume) + 1) % Volumes.Length]));
        if (s.SoundSet == "custom" || s.SoundSet.StartsWith("pack:"))
            AddNote("Loudness applies to the built-in sets; sound packs and your own WAV files play as they were recorded.");

        AddHeader("Which sounds play");
        AddRow("Moving the highlight", OnOff(s.SoundMove), "\uE8AB", click: () => Change(() => s.SoundMove = !s.SoundMove));
        AddRow("Select and back", OnOff(s.SoundSelect), "\uE73E", click: () => Change(() => s.SoundSelect = !s.SoundSelect));
        AddRow("Changing page (LB / RB)", OnOff(s.SoundPage), "\uE8A9", click: () => Change(() => s.SoundPage = !s.SoundPage));
        AddRow("Pop-ups and achievements", OnOff(s.SoundToast), "\uEA8F", click: () => Change(() => s.SoundToast = !s.SoundToast));

        AddHeader("Background music");
        AddRow("Music on the launcher's screens", BackgroundMusic.NameOf(s.MenuMusic), "\uE8D6",
            click: () => _ = SettingsScreen.ChooseBackgroundMusicAsync(Host, Rebuild));
        AddNote("Plays quietly only while no game is running. Loudness and your own tracks are in Settings > Sound.");

        AddHeader("Sound packs");
        AddRow("Open the sound packs folder", glyph: "\uE8B7", click: () =>
        {
            try { Directory.CreateDirectory(SoundPacks.UserDir); } catch (Exception) { }
            ProcessUtil.Open(SoundPacks.UserDir);
        });
        AddNote("Packs made for the Steam Deck audio loader work here: put each pack's folder (with its pack.json and .wav files) " +
                "in that folder, then come back to this page.");

        AddHeader("Try them");
        AddRow("Move", glyph: "\uE768", click: UiSound.Move);
        AddRow("Back", glyph: "\uE768", click: UiSound.Back);
        AddRow("Page", glyph: "\uE768", click: UiSound.Page);
        AddRow("Pop-up", glyph: "\uE768", click: UiSound.Toast);
        AddRow("Error", glyph: "\uE768", click: UiSound.Error);
        AddRow("Startup chime", glyph: "\uE768", click: UiSound.Startup);
        AddNote("The game start chime and the startup chime have their own settings on the Settings page.");
    }

    private void Change(Action change)
    {
        change();
        Host.Settings.Save();
        UiSound.Configure(Host.Settings);
        UiSound.Select();
        Rebuild();
    }

    /// <summary>A sound of its own for moving the highlight (Tick, Marimba, Laser, Droplet, Kalimba... or any set).</summary>
    private async Task ChooseNavAsync()
    {
        var sets = UiSound.AllSets().Where(x => x.Id != "custom").ToList();
        var names = new List<string> { "Same as the sound set" };
        names.AddRange(sets.Select(x => x.Name));
        int choice = await Host.AskAsync("Navigation sound", "The sound as the highlight moves. Tick, Marimba, Laser, Droplet and Kalimba were made for this.", names.ToArray());
        if (choice < 0) return;
        var s = Host.Settings;
        s.NavSoundSet = choice == 0 ? "" : sets[choice - 1].Id;
        s.Save();
        UiSound.Configure(s);
        UiSound.Preview(s.NavSoundSet.Length > 0 ? s.NavSoundSet : s.SoundSet, move: true);
        Rebuild();
    }

    private async void ChooseSet()
    {
        SoundPacks.Refresh(); // pick up packs copied in since the launcher started
        var sets = UiSound.AllSets();
        int choice = await Host.AskAsync("Sound set", "Built-in sets first, then sound packs.", sets.Select(x => x.Name).ToArray());
        if (choice < 0) return;
        var id = sets[choice].Id;
        if (id == "custom")
        {
            var folder = await Host.PickFolderAsync("Choose the folder with your sound files");
            if (folder == null) return;
            Host.Settings.SoundFolder = folder;
        }
        Change(() => Host.Settings.SoundSet = id);
    }
}

/// <summary>
/// The ROG Ally's extra buttons: choose what each does. Reading them follows AnyFSE (MIT); see AllyButtons.
/// </summary>
public sealed class AllyButtonsScreen : ListScreen
{
    public AllyButtonsScreen() : base("ROG Ally buttons") { }

    public override void OnShown()
    {
        AllyButtons.Heard -= OnHeard;
        AllyButtons.Heard += OnHeard;
        base.OnShown();
    }

    public override void OnClosed()
    {
        AllyButtons.Heard -= OnHeard;
        base.OnClosed();
    }

    // The button test row shows each press as it comes in.
    private void OnHeard(byte code) => Dispatcher.BeginInvoke(new Action(Rebuild));

    protected override void Build()
    {
        var s = Host.Settings;
        bool supported = AllyButtons.IsSupported;
        AddRow("Remap the buttons", s.AllyRemap ? "On" : "Off", "\uE765", click: () =>
        {
            s.AllyRemap = !s.AllyRemap;
            s.Save();
            Host.ApplyAllyButtons();
            Rebuild();
        }, subtitle: (AllyButtons.Model.Length > 0 ? AllyButtons.Model : "ROG Ally") + (supported ? "" : " (button device not found)"));

        AllyButtons.ModelOverride = s.AllyModel;
        AddRow("Which Ally is this?", s.AllyModel.Length > 0 ? s.AllyModel : (AllyButtons.DetectedModel.Length > 0 ? AllyButtons.DetectedModel + " (found)" : "Not found"), "\uE7F8",
            click: async () =>
            {
                int pick = await Host.AskAsync("Which Ally is this?", "The Xbox Allys have the Xbox Library button, and their buttons send different codes, so this decides which button is which.",
                    AllyButtons.Models.Select(m => m.Name).Append("Cancel").ToArray());
                if (pick < 0 || pick >= AllyButtons.Models.Length) return;
                s.AllyModel = AllyButtons.Models[pick].Id;
                AllyButtons.ModelOverride = s.AllyModel;
                s.Save();
                Rebuild();
            }, subtitle: "Choose it if the Xbox Library button or Armoury Crate button is mixed up");

        if (s.AllyRemap || s.ExperimentAllyQuickMenu.Length > 0)
        {
            var code = AllyButtons.LastCode;
            bool heard = AllyButtons.LastCodeAt != default;
            AddRow("Button test", heard ? $"{AllyButtons.NameOf(code)} (code {code})" : "Press an Ally button", "\uE7FC",
                subtitle: heard ? "The last button SquareDeck heard, at " + AllyButtons.LastCodeAt.ToString("T") : "Shows what SquareDeck hears when you press one");
        }

        if (s.AllyRemap)
        {
            // Set up by pressing it: the binding goes to the exact code the button sends, so it works on any model.
            AddHeader("Set up by pressing the button");
            AddRow("Press a button to set it up", null, "\uE7C8", click: () => _ = LearnAsync(),
                subtitle: "Press the Library, Command Center or Armoury Crate button (or Mode + one), then choose what it does. The surest way on the ROG Xbox Ally X20");
            foreach (var (key, act) in (s.AllyCodeBindings ??= new()).OrderBy(k => k.Key))
            {
                string k = key;
                AddRow(AllyButtons.DescribeKey(k), AllyButtons.Actions.FirstOrDefault(a => a.Id == act).Name ?? "Nothing", "\uE765",
                    click: () => _ = ChooseLearnedAsync(k));
            }
            AddHeader("Buttons");
            foreach (var (id, name) in AllyButtons.Buttons)
            {
                // Every button is listed whatever the model: the Xbox Allys (the X20 too) have Command Center on the right and
                // the Library button on the left (HandheldCompanion reads the same codes on every Ally). The Armoury Crate
                // button is only on the first two Allys.
                if (id is "ac" or "mode-ac" && AllyButtons.IsXboxAlly && !AllyButtons.SwapsCodes) continue;
                // M1 / M2 only send something once they're turned on in ROG Ally controls.
                if (id is "m1" or "m2" && !s.AllyBackKeys) continue;
                string button = id;
                s.AllyBindings.TryGetValue(id, out var action);
                AddRow(name, AllyButtons.Actions.FirstOrDefault(a => a.Id == (action ?? "")).Name ?? "Nothing", id.EndsWith("library") ? "\uE8F1" : "\uE7FC",
                    click: () => _ = ChooseAsync(button, name),
                    subtitle: id.EndsWith("library") && !AllyButtons.IsXboxAlly ? "On the ROG Xbox Ally, Xbox Ally X and Xbox Ally X20" : null);
            }
            AddRow("The Ally's other keys", s.AllyExtraKeys ? "On" : "Off", "\uE765", click: () =>
            {
                s.AllyExtraKeys = !s.AllyExtraKeys;
                s.Save();
                Rebuild();
            }, subtitle: "Microphone, screenshot, keyboard and record keys");
            if (!s.AllyBackKeys)
                AddRow("M1 and M2 back buttons", "Off", "\uE7FC", click: () => Host.Push(SettingsScreen.AllyPage("shortcuts", "back")),
                    subtitle: "Turn them on in Settings > " + Handheld.DisplayName + " to give them actions here");
            AddRow("Back to the defaults", glyph: "\uE777", click: () =>
            {
                s.AllyBindings = AllyButtons.DefaultBindings();
                s.Save();
                Rebuild();
            });
        }

        AddHeader("SquareDeck widget in Game Bar");
        string quick = s.ExperimentAllyQuickMenu;
        AddRow("Open the Game Bar widget with", quick.Length == 0 ? "Off" : AllyButtons.Buttons.FirstOrDefault(b => b.Id == quick).Name ?? quick, "\uE7FC",
            click: async () =>
            {
                var options = AllyButtons.Buttons.Where(b => b.Id is not ("m1" or "m2") || s.AllyBackKeys).Prepend(("", "Off")).ToArray();
                int pick = await Host.AskAsync("Open the Game Bar widget with", null, options.Select(o => o.Item2).ToArray());
                if (pick < 0) return;
                s.ExperimentAllyQuickMenu = options[pick].Item1;
                s.Save();
                Host.ApplyAllyButtons();
                Rebuild();
            },
            subtitle: "While a game is in front, this button opens SquareDeck's widget in Xbox Game Bar. Every button can also be set to it below (SquareDeck widget). " +
                      "Holding the Armoury Crate button works well. With no game in front, the button keeps its usual job");

        AddHeader("Armoury Crate SE");
        AddNote("Armoury Crate SE and ASUS Optimization react to these buttons too, so both may respond to a press. Set the buttons to " +
                "do nothing in Armoury Crate SE (or uninstall it). AnyFSE solves this by filtering ASUS's own service; SquareDeck " +
                "doesn't, because that needs code injected into another program.");
        AddNote("Remapping works while SquareDeck is running. Hold Mode (the back button) and press another button for the Mode + actions. " +
                "This part of SquareDeck is based on AnyFSE by Artem Shpynov (MIT licence).");
    }

    /// <summary>Waits for the next Ally button press, then asks what it should do.</summary>
    private async Task LearnAsync()
    {
        var s = Host.Settings;
        Host.ApplyAllyButtons(); // make sure SquareDeck is listening
        var heard = new TaskCompletionSource<string?>();
        BusyScreen? wait = null;
        void OnCode(byte code)
        {
            if (!AllyButtons.IsPressCode(code) || AllyButtons.LastPressKey is not { } key) return;
            heard.TrySetResult(key);
        }
        AllyButtons.Heard += OnCode;
        wait = new BusyScreen("Press the Ally button you want to set up.\nHold Mode (the back button) first for a Mode + combination.", cancel: () => heard.TrySetResult(null));
        Host.Push(wait);
        var timeout = Task.Delay(TimeSpan.FromSeconds(20));
        var done = await Task.WhenAny(heard.Task, timeout);
        AllyButtons.Heard -= OnCode;
        if (Host.IsTop(wait)) Host.Pop(wait);
        string? learnedKey = done == heard.Task ? heard.Task.Result : null;
        if (learnedKey == null)
        {
            if (done == timeout)
                await Host.AskAsync("No button heard", AllyButtons.IsSupported
                    ? "SquareDeck didn't hear an Ally button. Try again, and check the button test on this page shows a code when you press it."
                    : "SquareDeck couldn't find the Ally's button device.", "OK");
            Rebuild();
            return;
        }
        await ChooseLearnedAsync(learnedKey);
    }

    private async Task ChooseLearnedAsync(string key)
    {
        var s = Host.Settings;
        s.AllyCodeBindings ??= new();
        bool known = s.AllyCodeBindings.ContainsKey(key);
        var labels = AllyButtons.Actions.Select(a => a.Name).ToList();
        if (known) labels.Add("Forget this button");
        int choice = await Host.AskAsync(AllyButtons.DescribeKey(key), "What should this button do?", labels.ToArray());
        if (choice < 0) { Rebuild(); return; }
        if (choice >= AllyButtons.Actions.Length) s.AllyCodeBindings.Remove(key);
        else s.AllyCodeBindings[key] = AllyButtons.Actions[choice].Id;
        s.Save();
        Host.ApplyAllyButtons();
        Rebuild();
    }

    private async Task ChooseAsync(string button, string name)
    {
        int choice = await Host.AskAsync(name, null, AllyButtons.Actions.Select(a => a.Name).ToArray());
        if (choice < 0) return;
        Host.Settings.AllyBindings[button] = AllyButtons.Actions[choice].Id;
        Host.Settings.Save();
        Host.ApplyAllyButtons(); // M1 / M2 are only taken from other apps while they have an action
        Rebuild();
    }
}
