using System.Threading;
using System.IO;
using System.Media;

namespace SquareDeck.Services;

/// <summary>Short synthesized interface sounds (no audio files needed).</summary>
public static class UiSound
{
    public static bool Enabled { get; set; }

    /// <summary>Loudness of the built-in sounds, 10 to 100. Takes effect at the next <see cref="Load"/>.</summary>
    public static int VolumePercent { get; set; } = 100;

    /// <summary>Per-kind switches, on top of <see cref="Enabled"/>.</summary>
    public static bool MoveEnabled { get; set; } = true;
    public static bool SelectEnabled { get; set; } = true;
    public static bool PageEnabled { get; set; } = true;
    public static bool ToastEnabled { get; set; } = true;

    /// <summary>Applies every sound setting at once.</summary>
    public static void Configure(Settings s)
    {
        Enabled = s.UiSounds;
        VolumePercent = Math.Clamp(s.SoundVolume, 10, 100);
        MoveEnabled = s.SoundMove;
        SelectEnabled = s.SoundSelect;
        PageEnabled = s.SoundPage;
        ToastEnabled = s.SoundToast;
        Load(s.SoundSet, s.SoundFolder, s.NavSoundSet);
    }

    private enum Wave { Sine, Square, Triangle, Saw, Bell, Noise }

    private static readonly Random NoiseSource = new(7);

    /// <summary>
    /// One sound: its WAV bytes and a player that is kept for as long as the sound is in use.
    /// (A player created per press can be cleaned up by .NET while Windows is still playing from its
    /// buffer, which is what used to make sounds stop working after a while.)
    /// </summary>
    private sealed class Clip
    {
        private readonly byte[] _data;
        private SoundPlayer? _player;

        public Clip(byte[] data) => _data = data;

        /// <summary>How long the sound plays, read from its WAV header (zero if it can't be read).</summary>
        public TimeSpan Length
        {
            get
            {
                try
                {
                    if (_data.Length < 44 || _data[0] != 'R' || _data[8] != 'W') return TimeSpan.Zero;
                    int byteRate = BitConverter.ToInt32(_data, 28);
                    int pos = 12;
                    while (pos + 8 <= _data.Length)
                    {
                        int size = BitConverter.ToInt32(_data, pos + 4);
                        if (_data[pos] == 'd' && _data[pos + 1] == 'a' && _data[pos + 2] == 't' && _data[pos + 3] == 'a')
                            return byteRate > 0 ? TimeSpan.FromSeconds(Math.Min(size, _data.Length - pos - 8) / (double)byteRate) : TimeSpan.Zero;
                        pos += 8 + size + (size & 1);
                    }
                }
                catch (Exception) { }
                return TimeSpan.Zero;
            }
        }

        public void Play()
        {
            if (_data.Length == 0) return;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (_player == null)
                    {
                        _player = new SoundPlayer(new MemoryStream(_data));
                        _player.Load();
                    }
                    _player.Play();
                    return;
                }
                catch (Exception)
                {
                    _player = null; // build a fresh player and try once more
                }
            }
        }
    }

    private static readonly object Gate = new();
    private static Clip MoveClip = new(Array.Empty<byte>()), SelectClip = MoveClip, BackClip = MoveClip, ErrorClip = MoveClip,
        StartupClip = MoveClip, ToastClip = MoveClip, PageClip = MoveClip;
    private static Clip? LaunchClip;
    // Sound packs can bring their own low-battery alerts (battery_low.wav, battery_critical.wav).
    private static Clip? BatteryLowClip, BatteryCriticalClip;

    static UiSound() => Load("classic", "");

    /// <summary>Names of the built-in sound sets, for Settings.</summary>
    public static readonly (string Id, string Name)[] Sets =
    {
        ("classic", "Classic"), ("soft", "Soft"), ("retro", "Retro"), ("bubble", "Bubble"), ("glass", "Glass"),
        ("arcade", "Arcade"), ("deep", "Deep"), ("chime", "Chime"),
        // 6.25: five more, made for SquareDeck (good as navigation sounds on their own).
        ("tick", "Tick"), ("marimba", "Marimba"), ("laser", "Laser"), ("droplet", "Droplet"), ("kalimba", "Kalimba"),
        ("custom", "Custom folder")
    };

    /// <summary>File names looked for in a custom sound folder. Any that are missing use the Classic sound.</summary>
    public static readonly string[] CustomFiles = { "move.wav", "select.wav", "back.wav", "error.wav", "startup.wav", "toast.wav", "page.wav" };

    /// <summary>
    /// Switches every interface sound to a built-in set, or to WAV files from a folder. <paramref name="navSet"/> gives
    /// moving around a set of its own ("" or null = the same set).
    /// </summary>
    public static void Load(string set, string folder, string? navSet = null)
    {
        var sounds = Build(set, folder);
        if (!string.IsNullOrEmpty(navSet) && navSet != set) sounds = sounds with { Move = Build(navSet, folder).Move };
        _packLaunch = sounds.PackLaunch;
        lock (Gate)
        {
            MoveClip = new Clip(sounds.Move);
            SelectClip = new Clip(sounds.Select);
            BackClip = new Clip(sounds.Back);
            ErrorClip = new Clip(sounds.Error);
            StartupClip = new Clip(sounds.Startup);
            ToastClip = new Clip(sounds.Toast);
            PageClip = new Clip(sounds.Page);
        }
        byte[]? low = null, critical = null;
        if (SoundPacks.Find(set) is { } pack)
        {
            low = SoundPacks.ReadSound(pack, "battery_low.wav");
            critical = SoundPacks.ReadSound(pack, "battery_critical.wav") ?? low;
        }
        lock (Gate)
        {
            BatteryLowClip = low == null ? null : new Clip(low);
            BatteryCriticalClip = critical == null ? null : new Clip(critical);
        }
        // The "from the sound pack" game-start chime follows whichever pack is now selected.
        if (_chimeId == "pack") SetLaunchChime(_chimeId, _chimeFile);
    }

    private sealed record SoundBytes(byte[] Move, byte[] Select, byte[] Back, byte[] Error, byte[] Startup, byte[] Toast, byte[] Page, byte[]? PackLaunch);

    private static Clip? _previewClip;

    /// <summary>Plays one sound of a set without switching to it (choosing sounds for a style), even with sounds off.</summary>
    public static void Preview(string set, bool move)
    {
        var sounds = Build(set, "");
        _previewClip = new Clip(move ? sounds.Move : sounds.Select); // kept so it isn't cleaned up mid-sound
        Play(_previewClip, force: true);
    }

    private static SoundBytes Build(string set, string folder)
    {
        // Each set is the same family of sounds in a different voice: waveform, pitch, loudness and length.
        var (wave, pitch, volume, length) = set switch
        {
            "soft" => (Wave.Sine, 0.67, 0.75, 1.3),
            "retro" => (Wave.Square, 1.12, 0.38, 0.85),
            "bubble" => (Wave.Sine, 1.45, 0.8, 0.75),
            "glass" => (Wave.Triangle, 1.9, 0.75, 1.15),
            "arcade" => (Wave.Square, 0.84, 0.36, 1.0),
            "deep" => (Wave.Sine, 0.45, 1.1, 1.4),
            "chime" => (Wave.Triangle, 1.25, 0.9, 1.6),
            "tick" => (Wave.Triangle, 1.4, 0.6, 0.6),    // a dry mechanical click, like a clock or a keyboard
            "marimba" => (Wave.Bell, 0.9, 1.0, 1.5),     // warm wooden notes
            "laser" => (Wave.Saw, 1.3, 0.32, 0.9),       // sci-fi zaps that sweep down
            "droplet" => (Wave.Sine, 1.2, 0.85, 0.8),    // water drops that plip upward
            "kalimba" => (Wave.Bell, 1.6, 0.85, 1.3),    // bright plucked metal tines
            _ => (Wave.Sine, 1.0, 1.0, 1.0)
        };
        volume *= VolumePercent / 100.0;
        bool chirpy = set is "bubble" or "arcade"; // these glide upward on every sound

        byte[] One(double from, double to, int ms, double vol) =>
            Tone(new[] { (from * pitch, (to <= 0 ? from : to) * pitch, (int)(ms * length)) }, vol * volume, wave);
        byte[] Notes(int last, double vol, params double[] hz) =>
            Tone(hz.Select((f, i) => (f * pitch, f * pitch, (int)((i == hz.Length - 1 ? last : 110) * length))).ToArray(), vol * volume, wave);

        var move = One(880, chirpy ? 1100 : 0, 35, 0.18);
        var select = One(660, 990, 90, 0.22);
        // The new sets each give moving around (and selecting) a shape of their own.
        switch (set)
        {
            case "tick": move = Tone(new[] { (1.0, 1.0, 12) }, 0.3 * volume, Wave.Noise); select = Tone(new[] { (1.0, 1.0, 10), (1.0, 1.0, 26) }, 0.28 * volume, Wave.Noise); break;
            case "laser": move = One(1700, 650, 48, 0.2); select = One(900, 1900, 110, 0.22); break;
            case "droplet": move = One(520, 1450, 38, 0.22); select = One(700, 1900, 80, 0.24); break;
            case "marimba": move = One(784, 0, 90, 0.2); break;
            case "kalimba": move = One(987.77, 0, 80, 0.18); break;
        }
        var back = One(520, 390, 80, 0.20);
        var error = One(220, 200, 140, 0.22);
        var startup = Notes(520, 0.22, 523.25, 659.25, 783.99, 1046.5);
        var toast = Notes(420, 0.20, 783.99, 1046.5);
        var page = Tone(new[] { (698.46 * pitch, 698.46 * pitch, (int)(45 * length)), (1046.5 * pitch, 1046.5 * pitch, (int)(80 * length)) }, 0.18 * volume, wave);

        if (set == "custom" && !string.IsNullOrWhiteSpace(folder))
        {
            move = FromFile(folder, "move.wav") ?? move;
            select = FromFile(folder, "select.wav") ?? select;
            back = FromFile(folder, "back.wav") ?? back;
            error = FromFile(folder, "error.wav") ?? error;
            startup = FromFile(folder, "startup.wav") ?? startup;
            toast = FromFile(folder, "toast.wav") ?? toast;
            page = FromFile(folder, "page.wav") ?? page;
        }

        // A sound pack (Steam Deck layout). Anything the pack leaves out keeps the Classic sound.
        byte[]? packLaunch = null;
        if (SoundPacks.Find(set) is { } pack)
        {
            move = SoundPacks.ReadSound(pack, "deck_ui_navigation.wav", "deck_ui_misc_10.wav") ?? move;
            select = SoundPacks.ReadSound(pack, "deck_ui_default_activation.wav", "deck_ui_into_game_detail.wav") ?? select;
            back = SoundPacks.ReadSound(pack, "deck_ui_out_of_game_detail.wav", "deck_ui_hide_modal.wav") ?? back;
            error = SoundPacks.ReadSound(pack, "deck_ui_bumper_end_02.wav", "bumper_end.wav", "confirmation_negative.wav") ?? error;
            page = SoundPacks.ReadSound(pack, "deck_ui_tab_transition_01.wav", "deck_ui_side_menu_fly_in.wav") ?? page;
            toast = SoundPacks.ReadSound(pack, "deck_ui_toast.wav", "deck_ui_achievement_toast.wav", "deck_ui_message_toast.wav") ?? toast;
            startup = SoundPacks.ReadSound(pack, "Startup.wav", "startup.wav") ?? startup;
            packLaunch = SoundPacks.ReadSound(pack, "deck_ui_launch_game.wav");
        }
        return new SoundBytes(move, select, back, error, startup, toast, page, packLaunch);
    }

    private static byte[]? _packLaunch;
    private static string _chimeId = "rise", _chimeFile = "";

    /// <summary>The names shown in Settings for every choice: built-in sets, sound packs, then the custom folder.</summary>
    public static List<(string Id, string Name)> AllSets()
    {
        var all = Sets.Where(s => s.Id != "custom").ToList();
        all.AddRange(SoundPacks.List().Where(p => !p.Music).Select(p => (p.Id, p.Name)));
        all.Add(Sets.First(s => s.Id == "custom"));
        return all;
    }

    public static string SetName(string id) => AllSets().FirstOrDefault(s => s.Id == id).Name ?? "Classic";

    private static byte[]? FromFile(string folder, string name)
    {
        try
        {
            var path = Path.Combine(folder, name);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Move() { if (MoveEnabled) Play(MoveClip); }
    public static void Select() { if (SelectEnabled) Play(SelectClip); }
    public static void Back() { if (SelectEnabled) Play(BackClip); }
    public static void Error() => Play(ErrorClip);

    /// <summary>Moving between pages with LB / RB.</summary>
    public static void Page() { if (PageEnabled) Play(PageClip); }

    /// <summary>Achievement pop-up.</summary>
    public static void Toast() { if (ToastEnabled) Play(ToastClip); }

    /// <summary>The low-battery alert: the sound pack's own when it has one, otherwise the error sound.</summary>
    public static void Battery(bool critical)
    {
        var clip = critical ? BatteryCriticalClip : BatteryLowClip;
        if (clip != null) Play(clip, force: true);
        else Error();
    }

    /// <summary>The boot chime. Has its own setting, so it ignores <see cref="Enabled"/>.</summary>
    public static void Startup() => Play(StartupClip, force: true);

    /// <summary>How long the startup sound lasts (the menu music waits for it).</summary>
    public static TimeSpan StartupLength => StartupClip?.Length ?? TimeSpan.Zero;

    /// <summary>Game-start chimes to choose from in Settings.</summary>
    public static readonly (string Id, string Name)[] LaunchChimes =
    {
        ("off", "Off"), ("rise", "Rise"), ("sparkle", "Sparkle"), ("powerup", "Power-up"), ("pack", "From the sound pack"), ("custom", "Custom WAV file")
    };

    /// <summary>Chooses the sound played when a game starts.</summary>
    public static void SetLaunchChime(string id, string customFile)
    {
        _chimeId = id;
        _chimeFile = customFile;
        byte[]? wav = id switch
        {
            "off" => null,
            "pack" when _packLaunch != null => _packLaunch,
            "sparkle" => Tone(new (double, double, int)[] { (1318.5, 1318.5, 70), (1568, 1568, 70), (2093, 2093, 70), (2637, 2637, 420) }, 0.14, Wave.Sine),
            "powerup" => Tone(new (double, double, int)[] { (220, 880, 260), (880, 1760, 380) }, 0.09, Wave.Square),
            "custom" => ReadFile(customFile),
            _ => Tone(new (double, double, int)[] { (392, 392, 120), (587.33, 587.33, 120), (783.99, 783.99, 520) }, 0.2, Wave.Triangle)
        };
        lock (Gate) LaunchClip = wav == null ? null : new Clip(wav);
    }

    private static byte[]? ReadFile(string path)
    {
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>Played as a game starts. Has its own setting, so it ignores <see cref="Enabled"/>.</summary>
    public static void Launch()
    {
        if (LaunchClip != null) Play(LaunchClip, force: true);
    }

    private static void Play(Clip clip, bool force = false)
    {
        if (!Enabled && !force) return;
        // Starting a sound can hold the caller for a moment (Windows stops the last one first), which was felt on
        // every move of the highlight. Sounds start on a thread of their own; a newer one replaces any still waiting.
        lock (Gate)
        {
            _pending = clip;
            if (_player == null)
            {
                _player = new Thread(PlayLoop) { IsBackground = true, Name = "SquareDeck sounds", Priority = ThreadPriority.AboveNormal };
                _player.Start();
            }
        }
        Wake.Set();
    }

    private static Clip? _pending;
    private static Thread? _player;
    private static readonly AutoResetEvent Wake = new(false);

    private static void PlayLoop()
    {
        while (true)
        {
            Wake.WaitOne();
            Clip? next;
            lock (Gate) { next = _pending; _pending = null; }
            try { next?.Play(); } catch (Exception) { }
        }
    }

    /// <summary>Builds a 16-bit mono WAV: notes played one after another, each gliding in pitch and fading out.</summary>
    private static byte[] Tone((double StartHz, double EndHz, int Milliseconds)[] notes, double volume, Wave wave)
    {
        const int rate = 44100;
        int samples = notes.Sum(n => rate * n.Milliseconds / 1000);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + samples * 2);
        w.Write("WAVEfmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);      // PCM
        w.Write((short)1);      // mono
        w.Write(rate);
        w.Write(rate * 2);      // bytes per second
        w.Write((short)2);      // block align
        w.Write((short)16);     // bits per sample
        w.Write("data"u8.ToArray());
        w.Write(samples * 2);

        double phase = 0;
        foreach (var (startHz, endHz, milliseconds) in notes)
        {
            int count = rate * milliseconds / 1000;
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / count;
                double hz = startHz + (endHz - startHz) * t;
                phase += 2 * Math.PI * hz / rate;
                double envelope = Math.Min(1, i / 200.0) * Math.Pow(1 - t, 2);
                double sine = Math.Sin(phase);
                double sample = wave switch
                {
                    Wave.Square => sine >= 0 ? 1 : -1,
                    Wave.Triangle => 2 / Math.PI * Math.Asin(sine),
                    Wave.Saw => 2 * (phase / (2 * Math.PI) % 1) - 1,
                    // A struck bar: the note plus the bright, quickly fading overtones of wood or metal.
                    Wave.Bell => (sine + 0.5 * Math.Sin(phase * 2.76) * (1 - t) + 0.25 * Math.Sin(phase * 5.4) * Math.Pow(1 - t, 3)) / 1.75,
                    Wave.Noise => NoiseSource.NextDouble() * 2 - 1,
                    _ => sine
                };
                w.Write((short)(sample * envelope * volume * short.MaxValue));
            }
        }
        w.Flush();
        return ms.ToArray();
    }
}
