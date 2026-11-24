using System.IO;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>
/// Game Genie, ready before it's asked: games added to the library from now on get their answers written in the
/// background, one at a time, so Ask Genie opens with everything there. Careful with the shared Gemini quota:
/// only new games (not the first scan of a library), at most 20 a day (the rest carry over), and never while a game
/// is running, offline or in battery saver. Opening a game's details page also starts its answers, so they're
/// usually done by the time Ask Genie is pressed. Errors are quiet: that game is tried again later.
/// Settings: Library and stores > Prepare Genie answers for new games.
/// </summary>
public static class GeniePrep
{
    public const int DailyLimit = 20;
    /// <summary>Answers older than this are written again when asked for.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromDays(90);
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(45);

    private static Settings? _settings;
    private static Func<bool> _playing = () => false;
    private static Func<string, Game?> _find = _ => null;
    private static State _state = new();
    private static readonly object Lock = new();
    private static readonly Dictionary<string, Task<GameGenie.Sheet?>> Running = new();
    private static bool _started;

    private static string StateFile => Path.Combine(Settings.DataDir, "GameGenie", "prep.json");

    private sealed class State
    {
        public List<string> Seen { get; set; } = new();   // every game key the library has had
        public List<string> Queue { get; set; } = new();  // new games still to prepare
        public Dictionary<string, int> Tries { get; set; } = new();
        public string Day { get; set; } = "";
        public int Today { get; set; }
        public int Warmed { get; set; }
    }

    /// <summary>Starts the background helper (once). <paramref name="playing"/>: whether a game is running now.</summary>
    public static void Start(Settings settings, Func<bool> playing, Func<string, Game?> find)
    {
        _settings = settings;
        _playing = playing;
        _find = find;
        if (_started) return;
        _started = true;
        try
        {
            if (File.Exists(StateFile)) _state = JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile)) ?? new State();
        }
        catch (Exception) { _state = new State(); }
        _ = Task.Run(LoopAsync);
    }

    public static bool Enabled => _settings?.GeniePrepare != false;

    /// <summary>How many games have answers saved.</summary>
    public static int Ready
    {
        get
        {
            try { return Directory.Exists(Path.Combine(Settings.DataDir, "GameGenie")) ? Directory.GetFiles(Path.Combine(Settings.DataDir, "GameGenie"), "*.json").Count(f => !f.EndsWith("prep.json", StringComparison.OrdinalIgnoreCase)) : 0; }
            catch (Exception) { return 0; }
        }
    }

    public static int Waiting { get { lock (Lock) return _state.Queue.Count; } }

    /// <summary>
    /// The library as it is now. The first time, every game is just noted (no flood of requests for a whole library);
    /// after that, games that weren't there before join the queue.
    /// </summary>
    public static void NoteLibrary(IEnumerable<Game> games)
    {
        var keys = games.Where(g => g.IsGame).Select(g => g.Key).Distinct().ToList();
        if (keys.Count == 0) return;
        lock (Lock)
        {
            bool first = _state.Seen.Count == 0;
            var seen = _state.Seen.ToHashSet();
            var fresh = keys.Where(k => !seen.Contains(k)).ToList();
            _state.Seen.AddRange(fresh);
            // A whole launcher's worth at once (another store switched on) is a library, not new games: noted only.
            bool bulk = fresh.Count > 15;
            if (!first && !bulk)
                foreach (var key in fresh)
                    if (GameGenie.Cached(key) == null && !_state.Queue.Contains(key)) _state.Queue.Add(key);
            if (fresh.Count > 0) SaveState();
        }
    }

    /// <summary>Answers being written for a game right now, to wait for instead of asking twice.</summary>
    public static Task<GameGenie.Sheet?>? Pending(string gameKey)
    {
        lock (Lock) return Running.TryGetValue(gameKey, out var task) && !task.IsCompleted ? task : null;
    }

    /// <summary>Whether saved answers are recent enough to show as they are.</summary>
    public static bool Fresh(GameGenie.Sheet sheet) => DateTime.Now - sheet.Made < FreshFor;

    /// <summary>
    /// A head start from the game's details page: starts its answers now, with what the page knows about it, unless
    /// they're saved already or being written. Head starts have a daily limit of their own (twice the background one): you're about to ask.
    /// </summary>
    public static void Warm(Game game, string known)
    {
        if (!Enabled || !GameGenie.HasKey || !game.IsGame || !NetworkInterface.GetIsNetworkAvailable()) return;
        if (GameGenie.Cached(game.Key) is { } sheet && Fresh(sheet)) return;
        lock (Lock)
        {
            if (Running.TryGetValue(game.Key, out var running) && !running.IsCompleted) return;
            // Browsing many pages shouldn't drain the shared quota: head starts have a (higher) daily limit of their own.
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_state.Day != today) { _state.Day = today; _state.Today = 0; _state.Warmed = 0; }
            if (_state.Warmed >= DailyLimit * 2) return;
            _state.Warmed++;
            SaveState();
        }
        _ = Prepare(game.Key, game.Name, known);
    }

    private static Task<GameGenie.Sheet?> Prepare(string key, string name, string known)
    {
        lock (Lock)
        {
            if (Running.TryGetValue(key, out var existing) && !existing.IsCompleted) return existing;
            var task = Task.Run(async () =>
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                    return (GameGenie.Sheet?)await GameGenie.MakeAsync(key, name, known, timeout.Token);
                }
                catch (Exception) { return null; } // quiet: tried again later
            });
            Running[key] = task;
            return task;
        }
    }

    private static async Task LoopAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(90)); // let SquareDeck settle after starting
        while (true)
        {
            try { await StepAsync(); } catch (Exception) { }
            await Task.Delay(Gap);
        }
    }

    private static async Task StepAsync()
    {
        if (!Enabled || !GameGenie.HasKey) return;
        if (_playing() || Battery.SaverOn() || !NetworkInterface.GetIsNetworkAvailable()) return;
        string? key;
        lock (Lock)
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_state.Day != today) { _state.Day = today; _state.Today = 0; _state.Warmed = 0; }
            if (_state.Today >= DailyLimit || _state.Queue.Count == 0) return;
            key = _state.Queue[0];
        }
        var game = _find(key);
        if (game == null || (GameGenie.Cached(key) is { } saved && Fresh(saved)))
        {
            lock (Lock) { _state.Queue.Remove(key); SaveState(); }
            return;
        }
        var sheet = await Prepare(key, game.Name, "Title: " + game.Name + Environment.NewLine + "Store: " + game.SourceName);
        lock (Lock)
        {
            _state.Queue.Remove(key);
            _state.Today++;
            if (sheet == null)
            {
                // Try again later (at the back), up to three times.
                int tries = _state.Tries.GetValueOrDefault(key) + 1;
                _state.Tries[key] = tries;
                if (tries < 3) _state.Queue.Add(key);
            }
            else _state.Tries.Remove(key);
            SaveState();
        }
    }

    private static void SaveState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(_state));
        }
        catch (Exception) { }
    }
}
