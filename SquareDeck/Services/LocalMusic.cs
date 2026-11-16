using System.IO;
using System.Text.Json;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace SquareDeck.Services;

/// <summary>One song from your music folder.</summary>
public sealed record LocalTrack(string Path, string Title, string Artist, string Album, int TrackNo, double Seconds, long Stamp)
{
    public string Duration => Seconds <= 0 ? "" : TimeSpan.FromSeconds(Seconds).ToString(Seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
}

/// <summary>
/// The Music page's own player for a folder of songs (MP3, M4A, AAC, WMA, WAV, FLAC). It plays through Windows'
/// media player, so it shows in Windows' media controls, answers the keyboard's media keys, and appears in the
/// launcher's "now playing" banner like any other player. It keeps playing while you browse the launcher, and pauses
/// when a game starts.
/// </summary>
public static class LocalMusic
{
    private static readonly string[] Types = { ".mp3", ".m4a", ".aac", ".wma", ".wav", ".flac" };
    private const int MaxFiles = 8000;

    private static Settings? _settings;
    private static MediaPlayer? _player;
    private static List<LocalTrack> _queue = new();
    private static List<int> _order = new(); // play order (shuffled or not) of indexes into _queue
    private static int _position = -1;        // where in _order we are
    private static readonly Dictionary<string, byte[]?> ArtCache = new();
    private static System.Windows.Threading.Dispatcher? _ui;

    public static List<LocalTrack> Tracks { get; private set; } = new();
    public static bool Scanning { get; private set; }
    public static LocalTrack? Current { get; private set; }
    public static bool IsPlaying => _player?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
    public static bool Shuffle => _settings?.LocalShuffle ?? false;
    public static string Repeat => _settings?.LocalRepeat ?? "off"; // off, all, one

    /// <summary>The track list changed (a scan finished).</summary>
    public static event Action? LibraryChanged;

    /// <summary>Playing, paused, or a new song (raised on the UI thread).</summary>
    public static event Action? StateChanged;

    private static string IndexPath => System.IO.Path.Combine(Settings.DataDir, "music-index.json");

    public static void Init(Settings settings)
    {
        _settings = settings;
        _ui = System.Windows.Application.Current?.Dispatcher;
        try
        {
            if (File.Exists(IndexPath)) Tracks = JsonSerializer.Deserialize<List<LocalTrack>>(File.ReadAllText(IndexPath)) ?? new();
        }
        catch (Exception) { Tracks = new(); }
    }

    public static TimeSpan Position => _player?.PlaybackSession.Position ?? TimeSpan.Zero;
    public static TimeSpan Length => _player?.PlaybackSession.NaturalDuration ?? TimeSpan.Zero;

    // ================================================================ the folder

    /// <summary>
    /// Reads the folder (and the folders inside it). Song names, artists and albums come from the files' tags;
    /// they're remembered between visits so only new or changed files are read again.
    /// </summary>
    public static async Task ScanAsync(CancellationToken ct = default)
    {
        string folder = _settings?.MusicFolder ?? "";
        if (Scanning || folder.Length == 0 || !Directory.Exists(folder)) return;
        Scanning = true;
        try
        {
            var known = Tracks.ToDictionary(t => t.Path, StringComparer.OrdinalIgnoreCase);
            var result = await Task.Run(async () =>
            {
                var files = new List<FileInfo>();
                try
                {
                    foreach (var path in Directory.EnumerateFiles(folder, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                    {
                        if (!Types.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant())) continue;
                        files.Add(new FileInfo(path));
                        if (files.Count >= MaxFiles) break;
                    }
                }
                catch (Exception) { }

                var list = new List<LocalTrack>();
                using var gate = new SemaphoreSlim(4);
                var tasks = files.Select(async f =>
                {
                    long stamp = f.LastWriteTimeUtc.Ticks;
                    if (known.TryGetValue(f.FullName, out var old) && old.Stamp == stamp) return old;
                    await gate.WaitAsync(ct);
                    try { return await ReadTagsAsync(f.FullName, stamp); }
                    finally { gate.Release(); }
                }).ToList();
                foreach (var t in tasks)
                {
                    try { list.Add(await t); } catch (Exception) { }
                }
                return list.OrderBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase)
                           .ThenBy(t => t.Album, StringComparer.CurrentCultureIgnoreCase)
                           .ThenBy(t => t.TrackNo).ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            }, ct);
            Tracks = result;
            try
            {
                Directory.CreateDirectory(Settings.DataDir);
                await File.WriteAllTextAsync(IndexPath, JsonSerializer.Serialize(Tracks), ct);
            }
            catch (Exception) { }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Scanning = false;
            LibraryChanged?.Invoke();
        }
    }

    /// <summary>The song's tags, or a guess from its file name ("Artist - Title.mp3").</summary>
    private static async Task<LocalTrack> ReadTagsAsync(string path, long stamp)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        string title = name, artist = "", album = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) ?? "";
        int trackNo = 0;
        double seconds = 0;
        int dash = name.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) { artist = name[..dash].Trim(); title = name[(dash + 3)..].Trim(); }
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var music = await file.Properties.GetMusicPropertiesAsync();
            if (!string.IsNullOrWhiteSpace(music.Title)) title = music.Title;
            if (!string.IsNullOrWhiteSpace(music.Artist)) artist = music.Artist;
            else if (!string.IsNullOrWhiteSpace(music.AlbumArtist)) artist = music.AlbumArtist;
            if (!string.IsNullOrWhiteSpace(music.Album)) album = music.Album;
            trackNo = (int)music.TrackNumber;
            seconds = music.Duration.TotalSeconds;
        }
        catch (Exception) { }
        return new LocalTrack(path, title, artist.Length > 0 ? artist : "Unknown artist", album, trackNo, seconds, stamp);
    }

    /// <summary>The album art in a song file (null if it has none). Shared per album.</summary>
    public static async Task<byte[]?> ArtAsync(LocalTrack track)
    {
        string key = track.Artist + "|" + track.Album;
        if (ArtCache.TryGetValue(key, out var cached)) return cached;
        byte[]? bytes = null;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(track.Path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.MusicView, 480);
            if (thumb != null && thumb.Type == ThumbnailType.Image)
            {
                using var input = thumb.AsStreamForRead();
                using var ms = new MemoryStream();
                await input.CopyToAsync(ms);
                bytes = ms.Length > 0 ? ms.ToArray() : null;
            }
        }
        catch (Exception) { }
        ArtCache[key] = bytes;
        return bytes;
    }

    // ================================================================ playing

    private static MediaPlayer Player()
    {
        if (_player != null) return _player;
        _player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Media, Volume = Math.Clamp((_settings?.LocalVolume ?? 80) / 100.0, 0, 1) };
        // Windows' media controls (and the keyboard's media keys) can skip songs too.
        _player.CommandManager.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        _player.CommandManager.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        _player.CommandManager.NextReceived += (_, e) => { e.Handled = true; OnUi(() => Next()); };
        _player.CommandManager.PreviousReceived += (_, e) => { e.Handled = true; OnUi(() => Previous()); };
        _player.MediaEnded += (_, _) => OnUi(SongEnded);
        _player.MediaFailed += (_, _) => OnUi(() => Next());
        _player.PlaybackSession.PlaybackStateChanged += (_, _) => OnUi(() => StateChanged?.Invoke());
        return _player;
    }

    private static void OnUi(Action action)
    {
        if (_ui == null || _ui.CheckAccess()) action();
        else _ui.BeginInvoke(action);
    }

    /// <summary>Plays a list of songs, starting at one of them.</summary>
    public static void PlayList(IReadOnlyList<LocalTrack> songs, int start)
    {
        if (songs.Count == 0) return;
        _queue = songs.ToList();
        BuildOrder(Math.Clamp(start, 0, _queue.Count - 1));
        _ = PlayCurrentAsync();
    }

    private static void BuildOrder(int first)
    {
        _order = Enumerable.Range(0, _queue.Count).ToList();
        if (Shuffle)
        {
            var rng = new Random();
            _order.Remove(first);
            for (int i = _order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (_order[i], _order[j]) = (_order[j], _order[i]); }
            _order.Insert(0, first);
            _position = 0;
        }
        else _position = first;
    }

    private static async Task PlayCurrentAsync()
    {
        if (_position < 0 || _position >= _order.Count) return;
        var track = _queue[_order[_position]];
        Current = track;
        StateChanged?.Invoke();
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(track.Path);
            var item = new MediaPlaybackItem(Windows.Media.Core.MediaSource.CreateFromStorageFile(file));
            // What Windows' media controls (and the launcher's banner) show.
            var display = item.GetDisplayProperties();
            display.Type = Windows.Media.MediaPlaybackType.Music;
            display.MusicProperties.Title = track.Title;
            display.MusicProperties.Artist = track.Artist;
            display.MusicProperties.AlbumTitle = track.Album;
            try
            {
                var thumb = await file.GetThumbnailAsync(ThumbnailMode.MusicView, 300);
                if (thumb != null && thumb.Type == ThumbnailType.Image)
                    display.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(thumb);
            }
            catch (Exception) { }
            item.ApplyDisplayProperties(display);
            var player = Player();
            player.Source = item;
            player.Play();
        }
        catch (Exception)
        {
            // An unreadable file: on to the next one.
            if (_order.Count > 1) Next();
        }
        StateChanged?.Invoke();
    }

    private static void SongEnded()
    {
        if (Repeat == "one") { _ = PlayCurrentAsync(); return; }
        if (_position + 1 >= _order.Count && Repeat != "all") { Current = Current; StateChanged?.Invoke(); return; }
        Next();
    }

    public static void TogglePlay()
    {
        if (_player == null || Current == null)
        {
            if (Tracks.Count > 0) PlayList(Tracks, 0);
            return;
        }
        if (IsPlaying) _player.Pause(); else _player.Play();
    }

    public static void Pause()
    {
        if (IsPlaying) _player!.Pause();
    }

    public static void Next()
    {
        if (_order.Count == 0) return;
        _position = (_position + 1) % _order.Count;
        _ = PlayCurrentAsync();
    }

    /// <summary>Back to the start of the song, or to the one before if it has only just started.</summary>
    public static void Previous()
    {
        if (_order.Count == 0) return;
        if (Position > TimeSpan.FromSeconds(3) && _player != null) { _player.PlaybackSession.Position = TimeSpan.Zero; return; }
        _position = (_position - 1 + _order.Count) % _order.Count;
        _ = PlayCurrentAsync();
    }

    public static void Seek(TimeSpan to)
    {
        if (_player == null) return;
        var length = Length;
        if (length > TimeSpan.Zero && to > length) to = length - TimeSpan.FromSeconds(1);
        _player.PlaybackSession.Position = to < TimeSpan.Zero ? TimeSpan.Zero : to;
    }

    public static void SetVolume(int percent)
    {
        if (_settings != null) { _settings.LocalVolume = Math.Clamp(percent, 0, 100); _settings.Save(); }
        if (_player != null) _player.Volume = Math.Clamp(percent, 0, 100) / 100.0;
    }

    public static void ToggleShuffle()
    {
        if (_settings == null) return;
        _settings.LocalShuffle = !_settings.LocalShuffle;
        _settings.Save();
        if (_order.Count > 0) BuildOrder(_order[_position]); // the song playing stays first
        StateChanged?.Invoke();
    }

    public static void CycleRepeat()
    {
        if (_settings == null) return;
        _settings.LocalRepeat = _settings.LocalRepeat switch { "off" => "all", "all" => "one", _ => "off" };
        _settings.Save();
        StateChanged?.Invoke();
    }

    /// <summary>The songs coming up next, in play order.</summary>
    public static IEnumerable<LocalTrack> UpNext(int count) =>
        _order.Count == 0 ? Enumerable.Empty<LocalTrack>()
            : Enumerable.Range(1, Math.Min(count, _order.Count - 1)).Select(i => _queue[_order[(_position + i) % _order.Count]]);
}
