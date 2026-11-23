using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SquareDeck.Services;

/// <summary>
/// VacuumTube inside SquareDeck (Experimental): YouTube Leanback, the TV and console version of YouTube, with
/// VacuumTube's enhancements (ad blocking, SponsorBlock, DeArrow, Return YouTube Dislike, controller and touch support
/// and its own settings page). VacuumTube (github.com/shy1132/VacuumTube, MIT licence, by shy) is an Electron app; its
/// page scripts are bundled into VacuumTube\vacuumtube.js (tools\vacuumtube\build.js) and SquareDeck plays the part
/// of its Electron side: the identity YouTube sees, the content-policy changes and the settings file.
/// </summary>
public static class VacuumTube
{
    /// <summary>The VacuumTube release the bundled page scripts come from.</summary>
    public const string Version = "2.1.2";
    public const string ProjectUrl = "https://github.com/shy1132/VacuumTube";
    public const string LeanbackUrl = "https://www.youtube.com/tv";

    private const string AppId = "rocks.shy.VacuumTube";
    private const string CobaltVersion = "27.lts.3.1040976-gold";
    private const string CobaltMajor = "27";
    private const string V8Version = "13.8.258.54";
    private const int StarboardVersion = 18;

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "VacuumTube");
    public static string ScriptPath => Path.Combine(Folder, "vacuumtube.js");
    public static string IconPath => Path.Combine(Folder, "icon.png");
    /// <summary>DeckTube's own logo (SquareDeck's frame, YouTube's red and play button); VacuumTube's icon if it's missing.</summary>
    public static string LogoPath => File.Exists(Path.Combine(Folder, "decktube.png")) ? Path.Combine(Folder, "decktube.png") : IconPath;
    /// <summary>The name SquareDeck shows. VacuumTube (by shy, MIT) powers it, and the identity YouTube sees stays VacuumTube's.</summary>
    public const string Name = "DeckTube";
    public static bool Installed => File.Exists(ScriptPath);

    /// <summary>Its sign-in, history and settings, kept apart from the other web pages in SquareDeck.</summary>
    public static string DataFolder => Path.Combine(Settings.DataDir, "VacuumTube");
    private static string ConfigFile => Path.Combine(DataFolder, "config.json");

    private static bool Arm => RuntimeInformation.OSArchitecture == Architecture.Arm64;

    /// <summary>
    /// Leanback only opens for the TV and console apps, which run on Cobalt, so this is a Cobalt user agent in
    /// exactly the form VacuumTube builds (its src/ua.js): YouTube reads it as brand "VacuumTube", platform DESKTOP.
    /// </summary>
    public static string UserAgent =>
        $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) Cobalt/{CobaltVersion} (unlike Gecko) v8/{V8Version}-jit gles " +
        $"Starboard/{StarboardVersion}, VacuumTube_DESKTOP_{(Arm ? "arm64" : "x8664")}_{DateTime.Now.Year}/{Version} " +
        $"(VacuumTube, Windows) {AppId}/{Version}";

    /// <summary>The DevTools call that makes navigator.userAgent, navigator.userAgentData and the client hints match.</summary>
    public static string UserAgentOverrideJson
    {
        get
        {
            string full = string.Join('.', CobaltVersion.Split('-')[0].Split('.').Take(3));
            var o = new
            {
                userAgent = UserAgent,
                userAgentMetadata = new
                {
                    brands = new[] { new { brand = "VacuumTube", version = CobaltMajor } },
                    fullVersionList = new[] { new { brand = "VacuumTube", version = full } },
                    fullVersion = full,
                    platform = "Starboard",
                    platformVersion = "",
                    architecture = Arm ? "arm" : "x86",
                    model = "",
                    mobile = false,
                    bitness = "64",
                    wow64 = false
                }
            };
            return JsonSerializer.Serialize(o);
        }
    }

    /// <summary>The script that runs before VacuumTube's own: its saved settings and version.</summary>
    public static string BootScript()
    {
        var boot = new JsonObject { ["config"] = LoadConfig(), ["version"] = Version + " (in SquareDeck)", ["deeplink"] = null };
        return "window.__sdvtBoot = " + boot.ToJsonString() + ";";
    }

    /// <summary>VacuumTube's settings as it last saved them (only what was changed from its defaults, or everything).</summary>
    public static JsonObject LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigFile) && JsonNode.Parse(File.ReadAllText(ConfigFile)) is JsonObject o) return o;
        }
        catch (Exception ex) { CrashLog.Write("VacuumTube settings", ex); }
        return new JsonObject();
    }

    /// <summary>Saves the settings the page sends (the whole config, as VacuumTube keeps it).</summary>
    public static void SaveConfig(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject) return;
            Directory.CreateDirectory(DataFolder);
            string temp = ConfigFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, ConfigFile, true);
        }
        catch (Exception ex) { CrashLog.Write("VacuumTube settings", ex); }
    }

    /// <summary>The switches shown on the Music page's VacuumTube tab (key, name, what it does, VacuumTube's default).</summary>
    public static readonly (string Key, string Name, string About, bool Default)[] Switches =
    {
        ("adblock", "Block ads", "Video and feed ads, blocked without YouTube noticing", true),
        ("sponsorblock", "SponsorBlock", "Skips sponsored segments, using the community's list (sponsor.ajay.app)", false),
        ("dearrow", "DeArrow", "Less sensational titles and thumbnails, from the community (dearrow.ajay.app)", false),
        ("dislikes", "Return YouTube Dislike", "Shows rough dislike counts again (returnyoutubedislike.com)", false),
        ("hide_shorts", "Hide Shorts", "Leaves Shorts off the home page", false),
        ("block_continue_watching", "No \"Continue watching?\"", "Stops the prompt that pauses after a while without touching anything", false),
    };

    public static bool Get(string key, bool fallback)
    {
        var config = LoadConfig();
        return config[key] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
    }

    public static void Set(string key, bool value)
    {
        var config = LoadConfig();
        config[key] = value;
        SaveConfig(config.ToJsonString());
    }
}
