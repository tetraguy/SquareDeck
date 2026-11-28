using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// Experimental "SquareDeck EQ" (8.5.81): SquareDeck's own equalizer, played by Equalizer APO (a free, open-source
/// system-wide equalizer for Windows, equalizerapo.sourceforge.io). SquareDeck doesn't include any of its code: it
/// writes one settings file, SquareDeck.txt, in Equalizer APO's config folder, which Equalizer APO reads and applies
/// straight away. A one-time setup (one Windows permission prompt) lets SquareDeck write that file and points
/// Equalizer APO's own config.txt at it.
///
/// On top of a 10-band EQ and its presets:
///  - a preset for each kind of output: the Ally's speakers, headphones (and Bluetooth), and a TV or monitor, switched
///    automatically when the sound moves to another device;
///  - speaker guard: on the Ally's own speakers, the deep bass they can't play is filtered out, so it doesn't make them
///    distort or rattle (and the volume can go higher cleanly);
///  - loudness: at low volume the bass and treble come up a little, since quiet sound loses them first to the ear;
///  - a preset per game (its game profile), or "Match the game", picked from its genre;
///  - automatic headroom: the EQ is turned down by as much as it boosts, so it never clips.
/// </summary>
public static class SquareEq
{
    public const string Lab = "squareeq";

    /// <summary>The 10 bands, in Hz.</summary>
    public static readonly int[] Bands = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    public sealed record Preset(string Id, string Name, string About, double[] Gains, string Group,
        double Bass = 0, double Clarity = 0, double Air = 0, int Width = 0)
    {
        public bool Mine => Id.StartsWith("mine:", StringComparison.Ordinal);
    }

    private static double[] G(params double[] g) => g;

    /// <summary>The ready-made presets, by group.</summary>
    public static readonly Preset[] BuiltIn =
    {
        new("flat", "Flat", "No change: the sound as it is", G(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), "Everyday"),
        new("allyplus", "Ally speakers+", "Made for the Ally's small speakers: no wasted deep bass, more body in the low mids, clearer voices and a little sparkle and width",
            G(-6, -4, 1, 3, 2, 0, -1, 1.5, 2, 2), "Everyday", Width: 15),
        new("clearvoices", "Clear voices", "Speech up front: dialogue, narration and callouts stand out from the music and effects",
            G(-3, -2, -1, 0, 1, 2, 3, 3, 1, 0), "Everyday", Clarity: 1),
        new("night", "Night mode", "Booms and harsh highs tamed and voices kept clear, so you can play quietly without missing anything",
            G(-6, -5, -3, -1, 0, 1, 2, 1, -1, -3), "Everyday"),
        new("easy", "Easy on the ears", "Takes the edge off bright, tiring treble for long sessions",
            G(1, 1, 0, 0, 0, 0, -1, -2, -3, -3), "Everyday"),

        new("footsteps", "Footsteps", "For shooters: quieter explosions, louder footsteps, reloads and the direction they come from",
            G(-4, -4, -3, -2, -1, 0, 2, 4, 3, 0), "Games", Clarity: 2, Width: 20),
        new("openworld", "Open world", "Spacious and airy: wind, birds, distant rivers and big skies, wider than the screen",
            G(2, 2, 1, 0, 0, 0, 1, 2, 3, 4), "Games", Width: 35),
        new("racing", "Racing", "The growl of the engine and the bite of the tyres",
            G(3, 4, 4, 2, 0, 0, 2, 1, 0, 0), "Games"),
        new("horror", "Horror", "Deep rumbles you feel, and every creak and whisper behind you",
            G(5, 4, 2, -1, -1, 0, 2, 3, 2, 0), "Games", Width: 25),
        new("action", "Action and fighting", "Punchy hits and impacts, with the clash of each blow",
            G(3, 3, 2, 1, 0, 0, 2, 3, 1, 0), "Games"),
        new("rpg", "Story and RPG", "Rich orchestral scores with voices clear on top",
            G(2, 2, 1, 0, 0, 1, 2, 2, 2, 2), "Games", Width: 15),
        new("retro", "Retro handheld", "That small, tinny speaker of the handhelds you grew up with: for Retro Lounge and pixel games",
            G(-12, -12, -8, -2, 2, 3, 2, -2, -8, -12), "Games"),

        new("cinema", "Cinematic", "Big low end and open highs for films and shows",
            G(5, 4, 2, 0, -1, -1, 0, 1, 3, 4), "Music and video", Width: 25),
        new("bass", "Bass boost", "More low end for everything",
            G(6, 5, 4, 2, 0, 0, 0, 0, 0, 0), "Music and video"),
        new("punchy", "Punchy", "A lively V: kick drums and cymbals forward",
            G(4, 3, 1, 0, -2, -2, 0, 2, 3, 3), "Music and video"),
        new("rock", "Rock", "Driving guitars and drums",
            G(4, 3, 1, -1, -1, 1, 2, 3, 3, 3), "Music and video"),
        new("electronic", "Electronic", "Deep sub-bass and crisp highs for dance and synth music",
            G(6, 5, 2, 0, -1, 0, 1, 2, 4, 4), "Music and video"),
        new("vocal", "Vocals", "Singers forward, for pop and ballads",
            G(-1, -1, 0, 1, 2, 3, 2, 1, 0, 0), "Music and video"),
        new("acoustic", "Acoustic and jazz", "Natural and warm, a touch of air",
            G(2, 2, 1, 1, 0, 0, 1, 1, 2, 1), "Music and video"),
        new("podcast", "Podcasts", "Clean, close speech with the boom and hiss taken out",
            G(-6, -4, -1, 1, 2, 3, 3, 2, 0, -2), "Music and video"),
        new("treble", "Treble boost", "Brighter and more detailed",
            G(0, 0, 0, 0, 0, 1, 2, 3, 4, 5), "Music and video"),
        new("warm", "Warm headphones", "A warm, relaxed tilt for headphones",
            G(3, 3, 2, 1, 0, 0, -1, -1, 0, 1), "Music and video"),
    };

    /// <summary>The kinds of output, each with its own preset.</summary>
    public static readonly (string Id, string Name, string Default)[] Outputs =
    {
        ("speakers", "The Ally's speakers", "allyplus"), ("headphones", "Headphones and Bluetooth", "flat"), ("tv", "TV or monitor", "flat"),
    };

    public static bool On => Labs.On(Lab);

    // ---------------------------------------------------------------- presets of your own

    private sealed class MineJson
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public double[] Gains { get; set; } = new double[10];
        public double Bass { get; set; }
        public double Clarity { get; set; }
        public double Air { get; set; }
        public int Width { get; set; }
    }

    public static List<Preset> Mine()
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<MineJson>>(Labs.Get("eq.mine", "[]")) ?? new();
            return list.Where(m => m.Gains.Length == 10)
                .Select(m => new Preset(m.Id, m.Name, "Your own", m.Gains, "Yours", m.Bass, m.Clarity, m.Air, m.Width)).ToList();
        }
        catch (JsonException) { return new(); }
    }

    /// <summary>Saves a preset of your own (replacing the one with its id), and returns it.</summary>
    public static Preset SaveMine(Preset p, string name)
    {
        var list = Mine();
        string id = p.Mine ? p.Id : "mine:" + Guid.NewGuid().ToString("N")[..8];
        var saved = p with { Id = id, Name = name, About = "Your own", Group = "Yours" };
        list.RemoveAll(x => x.Id == id);
        list.Add(saved);
        Labs.Put("eq.mine", JsonSerializer.Serialize(list.Select(x => new MineJson
            { Id = x.Id, Name = x.Name, Gains = x.Gains, Bass = x.Bass, Clarity = x.Clarity, Air = x.Air, Width = x.Width })));
        return saved;
    }

    public static void DeleteMine(string id)
    {
        var list = Mine();
        list.RemoveAll(x => x.Id == id);
        Labs.Put("eq.mine", JsonSerializer.Serialize(list.Select(x => new MineJson
            { Id = x.Id, Name = x.Name, Gains = x.Gains, Bass = x.Bass, Clarity = x.Clarity, Air = x.Air, Width = x.Width })));
        foreach (var o in Outputs) if (Labs.Get("eq." + o.Id) == id) Labs.Put("eq." + o.Id, o.Default);
    }

    public static IEnumerable<Preset> All => BuiltIn.Concat(Mine());

    public static Preset Find(string? id) => All.FirstOrDefault(p => p.Id == id) ?? BuiltIn[0];

    public static string NameOf(string? id) => id switch
    {
        null or "" => "Same as outside games",
        "auto" => "Match the game",
        _ => Find(id).Name,
    };

    // ---------------------------------------------------------------- which preset plays now

    /// <summary>The preset chosen for a kind of output.</summary>
    public static string PresetFor(string output) =>
        Labs.Get("eq." + output, Outputs.First(o => o.Id == output).Default);

    /// <summary>What kind of output a device is, from its name.</summary>
    public static string KindOf(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("hdmi") || n.Contains("displayport") || n.Contains("display audio") || n.Contains(" tv") || n.StartsWith("tv")
            || n.Contains("monitor") || n.Contains("high definition audio") && (n.Contains("nvidia") || n.Contains("amd"))) return "tv";
        if (n.Contains("headphone") || n.Contains("headset") || n.Contains("earbud") || n.Contains("earphone") || n.Contains("airpods")
            || n.Contains("buds") || n.Contains("hands-free") || n.Contains("bluetooth")) return "headphones";
        if (n.Contains("speaker")) return "speakers";
        return "headphones";
    }

    private static string _output = "speakers", _outputName = "";
    private static string? _gameEq;   // the running game's choice: null (as outside games), "auto", or a preset id
    private static string _gameGenre = "", _gameName = "";
    private static Preset? _preview;  // the editor's curve while you change it
    private static int _volumeStep = -1;

    /// <summary>The output in use now ("speakers", "headphones", "tv") and its name.</summary>
    public static (string Kind, string Name) Output => (_output, _outputName);

    /// <summary>The preset playing now, and why (for Settings).</summary>
    public static (Preset Preset, string Why) Current
    {
        get
        {
            if (_preview != null) return (_preview, "Previewing");
            if (_gameEq != null && Labs.Get("eq.games", "1") == "1")
            {
                if (_gameEq == "auto") return (Find(Suggest(_gameGenre, _gameName)), _gameName + " (matched to the game)");
                return (Find(_gameEq), _gameName + "'s profile");
            }
            return (Find(PresetFor(_output)), Outputs.First(o => o.Id == _output).Name);
        }
    }

    /// <summary>"Match the game": a preset that suits a genre (and a few names that say what they are).</summary>
    public static string Suggest(string genre, string name)
    {
        string g = (genre + " " + name).ToLowerInvariant();
        bool Has(params string[] words) => words.Any(g.Contains);
        if (Has("shooter", "fps", "battle royale", "call of duty", "valorant", "counter-strike", "apex", "fortnite", "overwatch", "halo")) return "footsteps";
        if (Has("racing", "driving", "forza", "gran turismo", "need for speed", "kart")) return "racing";
        if (Has("horror", "survival horror", "resident evil", "silent hill", "dead space")) return "horror";
        if (Has("fighting", "beat 'em up", "tekken", "street fighter", "mortal kombat")) return "action";
        if (Has("rpg", "role-playing", "jrpg", "visual novel", "adventure")) return "rpg";
        if (Has("open world", "exploration", "simulation", "survival", "sandbox")) return "openworld";
        if (Has("music", "rhythm")) return "punchy";
        if (Has("retro", "pixel")) return "retro";
        if (Has("arcade", "platformer")) return "action";
        if (Has("action")) return "action";
        return PresetFor(_output);
    }

    /// <summary>A game started or ended (from the running-game check, every second or two).</summary>
    public static void SetRunning(Game? game, GameProfile? profile)
    {
        string? eq = game == null ? null : profile?.Eq;
        string name = game?.Name ?? "";
        if (eq == _gameEq && name == _gameName) return;
        _gameEq = eq;
        _gameName = name;
        _gameGenre = game == null ? "" : GenreCache.Of(game) ?? "";
        Apply();
    }

    /// <summary>The editor's curve, heard while it's changed (null: back to normal).</summary>
    public static void Preview(Preset? p)
    {
        _preview = p;
        Apply();
    }

    /// <summary>Checked every few seconds while it's on: the output device and the volume (for loudness).</summary>
    public static void Tick()
    {
        if (!On || !Installed) return; // part of SquareDeck now (8.5.102): nothing to watch until Equalizer APO is there
        var outputs = Audio.GetOutputs();
        var now = outputs.FirstOrDefault(o => o.IsDefault);
        string kind = now.Name == null ? _output : KindOf(now.Name);
        int step = Labs.Get("eq.loudness", "0") == "1" ? Math.Clamp((Audio.GetVolume() ?? 100) / 10, 0, 10) : -1;
        if (kind == _output && (now.Name ?? "") == _outputName && step == _volumeStep) return;
        _output = kind;
        _outputName = now.Name ?? "";
        _volumeStep = step;
        Apply();
    }

    // ---------------------------------------------------------------- Equalizer APO

    private const string ApoKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\EqualizerAPO";
    public const string Download = "https://sourceforge.net/projects/equalizerapo/";

    /// <summary>Equalizer APO's official 64-bit installer (1.4.2), downloaded from SourceForge when you press Install.</summary>
    public const string InstallerUrl = "https://gigenet.dl.sourceforge.net/project/equalizerapo/1.4.2/EqualizerAPO-x64-1.4.2.exe";

    /// <summary>The installer's SHA-256 (as published with Chocolatey's package): anything else isn't run.</summary>
    public const string InstallerSha256 = "7403be7427bbe1936a40dded082829b6e217fc4f5990fee5cba501f0ae055afa";

    /// <summary>Downloads the installer to a temporary file, reporting 0 to 1; checks it's the genuine one. Returns its path.</summary>
    public static async Task<string> DownloadInstallerAsync(IProgress<double> progress, CancellationToken token)
    {
        string path = Path.Combine(Path.GetTempPath(), "SquareDeck-EqualizerAPO-x64-1.4.2.exe");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/" + Updater.Current);
        using var response = await http.GetAsync(InstallerUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? 12_600_000;
        await using (var source = await response.Content.ReadAsStreamAsync(token))
        await using (var file = File.Create(path))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), token);
                done += read;
                progress.Report(Math.Min(1, (double)done / total));
            }
        }
        await using (var check = File.OpenRead(path))
        {
            string hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(check, token)).ToLowerInvariant();
            if (hash != InstallerSha256)
            {
                try { File.Delete(path); } catch (Exception) { }
                throw new InvalidOperationException("The downloaded installer isn't the genuine Equalizer APO 1.4.2, so it wasn't run.");
            }
        }
        return path;
    }

    /// <summary>
    /// Installs Equalizer APO quietly and sets up SquareDeck EQ, in one go (one Windows permission prompt). Equalizer
    /// APO's installer opens its Device Selector on the way: the window to tick the devices it plays on. When that's
    /// closed, the Windows audio service is restarted so it starts playing without a restart. Null when it worked.
    /// </summary>
    public static async Task<string?> InstallAsync(string installer)
    {
        string script = Path.Combine(Path.GetTempPath(), "SquareDeck-EQ-install.ps1");
        string q(string s) => s.Replace("'", "''");
        await File.WriteAllTextAsync(script, $$"""
            $p = Start-Process -FilePath '{{q(installer)}}' -ArgumentList '/S' -Wait -PassThru
            $cfg = (Get-ItemProperty 'HKLM:\SOFTWARE\EqualizerAPO' -ErrorAction SilentlyContinue).ConfigPath
            if (-not $cfg) { $cfg = Join-Path $env:ProgramFiles 'EqualizerAPO\config' }
            if (-not (Test-Path -LiteralPath $cfg)) { exit 2 }
            {{SetUpCommands(only: true)}}
            Restart-Service -Name audiosrv -Force -ErrorAction SilentlyContinue
            exit 0
            """, new UTF8Encoding(true));
        int code = await RunElevatedAsync(script);
        try { File.Delete(script); } catch (Exception) { }
        try { File.Delete(installer); } catch (Exception) { }
        if (code == -1) return "Windows' permission prompt was declined.";
        if (code == 2 || !Installed) return "Equalizer APO's installer didn't finish.";
        _written = "";
        Apply();
        return Ready ? null : "Equalizer APO is installed, but SquareDeck couldn't give it the EQ. Try Set up SquareDeck EQ.";
    }

    /// <summary>The setup's PowerShell, run with administrator rights, with $cfg set to the config folder.</summary>
    private static string SetUpCommands(bool only) => $$"""
        $ours = Join-Path $cfg 'SquareDeck.txt'
        if (-not (Test-Path -LiteralPath $ours)) { Set-Content -LiteralPath $ours -Value '# SquareDeck EQ' -Encoding UTF8 }
        icacls.exe "$ours" /grant '*S-1-5-32-545:(M)' | Out-Null
        $main = Join-Path $cfg 'config.txt'
        if ((Test-Path -LiteralPath $main) -and -not (Test-Path -LiteralPath "$main.before-squaredeck")) { Copy-Item -LiteralPath $main -Destination "$main.before-squaredeck" }
        if ({{(only ? "$true" : "$false")}}) {
          Set-Content -LiteralPath $main -Value @('# Managed by SquareDeck (SquareDeck EQ). Your earlier settings are in config.txt.before-squaredeck.', 'Include: SquareDeck.txt') -Encoding UTF8
        } elseif (-not ((Get-Content -LiteralPath $main -Raw -ErrorAction SilentlyContinue) -match 'SquareDeck\.txt')) {
          Add-Content -LiteralPath $main -Value "`r`n# SquareDeck EQ`r`nInclude: SquareDeck.txt" -Encoding UTF8
        }
        """;

    /// <summary>Equalizer APO's folder, or null when it isn't installed.</summary>
    public static string? InstallDir
    {
        get
        {
            try
            {
                var dir = Registry.GetValue(ApoKey, "InstallPath", null) as string;
                if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO");
                return File.Exists(Path.Combine(dir, "Editor.exe")) || Directory.Exists(Path.Combine(dir, "config")) ? dir : null;
            }
            catch (Exception) { return null; }
        }
    }

    public static bool Installed => InstallDir != null;

    /// <summary>Equalizer APO's config folder.</summary>
    public static string? ConfigDir
    {
        get
        {
            try
            {
                var dir = Registry.GetValue(ApoKey, "ConfigPath", null) as string;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                return InstallDir is { } install ? Path.Combine(install, "config") : null;
            }
            catch (Exception) { return null; }
        }
    }

    private static string? OurFile => ConfigDir is { } dir ? Path.Combine(dir, "SquareDeck.txt") : null;

    /// <summary>Set up: config.txt plays SquareDeck.txt, and SquareDeck can write it.</summary>
    public static bool Ready
    {
        get
        {
            try
            {
                if (OurFile is not { } ours || !File.Exists(ours)) return false;
                var main = Path.Combine(ConfigDir!, "config.txt");
                if (!File.Exists(main) || !File.ReadAllText(main).Contains("SquareDeck.txt", StringComparison.OrdinalIgnoreCase)) return false;
                using var test = new FileStream(ours, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                return true;
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>
    /// The one-time setup (one Windows permission prompt): makes SquareDeck.txt, lets you (and so SquareDeck) write it,
    /// and points config.txt at it. With <paramref name="only"/>, config.txt plays SquareDeck's EQ alone (the rest of
    /// Equalizer APO's own settings are kept in a backup, config.txt.before-squaredeck). Null when it worked.
    /// </summary>
    public static async Task<string?> SetUpAsync(bool only)
    {
        if (ConfigDir is not { } dir) return "Equalizer APO isn't installed.";
        string script = Path.Combine(Path.GetTempPath(), "SquareDeck-EQ-setup.ps1");
        string q(string s) => s.Replace("'", "''");
        await File.WriteAllTextAsync(script, $$"""
            $cfg = '{{q(dir)}}'
            {{SetUpCommands(only)}}
            exit 0
            """, new UTF8Encoding(true));
        int code = await RunElevatedAsync(script);
        try { File.Delete(script); } catch (Exception) { }
        if (code == -1) return "Windows' permission prompt was declined.";
        _written = "";
        Apply();
        return Ready ? null : "Equalizer APO's config folder couldn't be changed.";
    }

    /// <summary>Gives Equalizer APO back its own settings (config.txt as it was before setup). Null when it worked.</summary>
    public static async Task<string?> UndoSetUpAsync()
    {
        if (ConfigDir is not { } dir) return "Equalizer APO isn't installed.";
        string script = Path.Combine(Path.GetTempPath(), "SquareDeck-EQ-undo.ps1");
        string q(string s) => s.Replace("'", "''");
        await File.WriteAllTextAsync(script, $$"""
            $cfg = '{{q(dir)}}'
            $main = Join-Path $cfg 'config.txt'
            if (Test-Path -LiteralPath "$main.before-squaredeck") { Copy-Item -LiteralPath "$main.before-squaredeck" -Destination $main -Force; Remove-Item -LiteralPath "$main.before-squaredeck" -Force }
            else { (Get-Content -LiteralPath $main) | Where-Object { $_ -notmatch 'SquareDeck' } | Set-Content -LiteralPath $main -Encoding UTF8 }
            Remove-Item -LiteralPath (Join-Path $cfg 'SquareDeck.txt') -Force -ErrorAction SilentlyContinue
            exit 0
            """, new UTF8Encoding(true));
        int code = await RunElevatedAsync(script);
        try { File.Delete(script); } catch (Exception) { }
        return code == -1 ? "Windows' permission prompt was declined." : null;
    }

    /// <summary>Opens Equalizer APO's Device Selector, to add the Ally's speakers, headphones or a TV to it.</summary>
    public static bool OpenDeviceSelector()
    {
        if (InstallDir is not { } dir) return false;
        string exe = Path.Combine(dir, "DeviceSelector.exe");
        return File.Exists(exe) && ProcessUtil.Start(exe);
    }

    private static async Task<int> RunElevatedAsync(string scriptPath)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"")
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(psi);
            if (process == null) return -1;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    }

    // ---------------------------------------------------------------- writing it

    private static string _written = "";

    /// <summary>Writes what should play now (only when it changed: Equalizer APO reloads the file each time).</summary>
    public static void Apply()
    {
        if (OurFile is not { } ours || !File.Exists(ours)) return;
        string text = On ? Build(Current.Preset, _output == "speakers" && Labs.Get("eq.guard", "1") == "1", _volumeStep,
                                  int.TryParse(Labs.Get("eq.balance", "0"), out var b) ? b : 0)
                         : "# SquareDeck EQ is off\r\n";
        if (text == _written) return;
        try
        {
            File.WriteAllText(ours, text, new UTF8Encoding(false));
            _written = text;
        }
        catch (Exception) { }
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Equalizer APO's commands for a preset (with the speaker guard, loudness and balance).</summary>
    public static string Build(Preset p, bool guard, int volumeStep, int balance)
    {
        var b = new StringBuilder();
        b.Append("# Written by SquareDeck (SquareDeck EQ): ").Append(p.Name).Append(". Changes here are replaced.\r\n");
        double bass = p.Bass, air = p.Air;
        if (volumeStep is >= 0 and < 8)
        {
            // Loudness: the quieter it is, the more the bass and treble come back (up to +6 dB and +3 dB at the lowest volume).
            double k = (8 - volumeStep) / 8.0;
            bass += 6 * k;
            air += 3 * k;
        }
        b.Append("Preamp: -").Append(F(Headroom(p with { Bass = bass, Air = air }, guard))).Append(" dB\r\n");
        if (guard) b.Append("Filter: ON HP Fc 100 Hz Q 0.71\r\n");
        b.Append("GraphicEQ: ").Append(string.Join("; ", Bands.Select((f, i) => f + " " + F(p.Gains[i])))).Append("\r\n");
        if (Math.Abs(bass) > 0.01) b.Append("Filter: ON LSC Fc ").Append(guard ? "160" : "105").Append(" Hz Gain ").Append(F(bass)).Append(" dB Q 0.71\r\n");
        if (Math.Abs(p.Clarity) > 0.01) b.Append("Filter: ON PK Fc 3000 Hz Gain ").Append(F(p.Clarity)).Append(" dB Q 1\r\n");
        if (Math.Abs(air) > 0.01) b.Append("Filter: ON HSC Fc 10000 Hz Gain ").Append(F(air)).Append(" dB Q 0.71\r\n");
        if (p.Width <= -100) b.Append("Copy: L=0.5*L+0.5*R R=0.5*L+0.5*R\r\n");
        else if (p.Width != 0)
        {
            // Width: each side minus a little of the other (wider), or plus (narrower).
            double k = p.Width / 100.0 * 0.45, self = 1 + k;
            string other = k >= 0 ? "+-" + F(k) : "+" + F(-k);
            b.Append("Copy: L=").Append(F(self)).Append("*L").Append(other).Append("*R R=").Append(F(self)).Append("*R").Append(other).Append("*L\r\n");
        }
        if (balance != 0)
        {
            // Balance: the other side turned down (up to 12 dB at full).
            b.Append("Channel: ").Append(balance > 0 ? "L" : "R").Append("\r\nPreamp: -").Append(F(Math.Abs(balance) * 0.12)).Append(" dB\r\nChannel: all\r\n");
        }
        return b.ToString();
    }

    /// <summary>How far to turn the EQ down so its biggest boost doesn't clip.</summary>
    public static double Headroom(Preset p, bool guard)
    {
        double peak = 0;
        for (double f = 20; f <= 20000; f *= 1.06) peak = Math.Max(peak, Response(p, f, guard));
        if (p.Width > 0) peak += 20 * Math.Log10(1 + p.Width / 100.0 * 0.45);
        return Math.Round(Math.Max(0, peak) + 0.5, 1);
    }

    // ---------------------------------------------------------------- the curve, for drawing and headroom

    /// <summary>The EQ's gain at a frequency, in dB (as Equalizer APO plays it, near enough).</summary>
    public static double Response(Preset p, double f, bool guard = false)
    {
        double db = Graphic(p.Gains, f);
        if (Math.Abs(p.Bass) > 0.01) db += Shelf(f, guard ? 160 : 105, p.Bass, low: true);
        if (Math.Abs(p.Clarity) > 0.01) db += Peak(f, 3000, p.Clarity, 1);
        if (Math.Abs(p.Air) > 0.01) db += Shelf(f, 10000, p.Air, low: false);
        if (guard) db += HighPass(f, 100);
        return db;
    }

    /// <summary>GraphicEQ: straight lines between the bands on a log frequency scale, flat beyond them.</summary>
    private static double Graphic(double[] g, double f)
    {
        if (f <= Bands[0]) return g[0];
        if (f >= Bands[^1]) return g[^1];
        for (int i = 0; i < Bands.Length - 1; i++)
            if (f <= Bands[i + 1])
            {
                double t = Math.Log(f / Bands[i]) / Math.Log((double)Bands[i + 1] / Bands[i]);
                return g[i] + (g[i + 1] - g[i]) * t;
            }
        return 0;
    }

    private const double Fs = 48000;

    private static double Magnitude(double b0, double b1, double b2, double a0, double a1, double a2, double f)
    {
        double w = 2 * Math.PI * f / Fs, c1 = Math.Cos(w), c2 = Math.Cos(2 * w), s1 = Math.Sin(w), s2 = Math.Sin(2 * w);
        double nr = b0 + b1 * c1 + b2 * c2, ni = -(b1 * s1 + b2 * s2);
        double dr = a0 + a1 * c1 + a2 * c2, di = -(a1 * s1 + a2 * s2);
        return 10 * Math.Log10((nr * nr + ni * ni) / (dr * dr + di * di));
    }

    // Biquads from Robert Bristow-Johnson's Audio EQ Cookbook (the formulas Equalizer APO uses too).
    private static double Peak(double f, double f0, double gain, double q)
    {
        double A = Math.Pow(10, gain / 40), w = 2 * Math.PI * f0 / Fs, alpha = Math.Sin(w) / (2 * q), c = Math.Cos(w);
        return Magnitude(1 + alpha * A, -2 * c, 1 - alpha * A, 1 + alpha / A, -2 * c, 1 - alpha / A, f);
    }

    private static double Shelf(double f, double f0, double gain, bool low)
    {
        double A = Math.Pow(10, gain / 40), w = 2 * Math.PI * f0 / Fs, c = Math.Cos(w), alpha = Math.Sin(w) / (2 * 0.71), sa = 2 * Math.Sqrt(A) * alpha;
        return low
            ? Magnitude(A * ((A + 1) - (A - 1) * c + sa), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - sa),
                        (A + 1) + (A - 1) * c + sa, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - sa, f)
            : Magnitude(A * ((A + 1) + (A - 1) * c + sa), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - sa),
                        (A + 1) - (A - 1) * c + sa, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - sa, f);
    }

    private static double HighPass(double f, double f0)
    {
        double w = 2 * Math.PI * f0 / Fs, c = Math.Cos(w), alpha = Math.Sin(w) / (2 * 0.71);
        return Magnitude((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + alpha, -2 * c, 1 - alpha, f);
    }
}
