using System.IO;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>
/// Each game's main genre (the first one its Steam store page lists), for grouping the library. Looked up once in the
/// background and kept in genres.json. Games that aren't from Steam use the same game's Steam page when it's sold there.
/// </summary>
public static class GenreCache
{
    private static Dictionary<string, string>? _genres;
    private static readonly object Lock = new();
    private static bool _busy;
    private static string FilePath => Path.Combine(Settings.DataDir, "genres.json");

    /// <summary>Raised (on a worker thread) when lookups have found more genres.</summary>
    public static event Action? Updated;

    private static Dictionary<string, string> All
    {
        get
        {
            lock (Lock)
            {
                if (_genres != null) return _genres;
                try { _genres = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) : null; }
                catch (Exception) { }
                return _genres ??= new();
            }
        }
    }

    /// <summary>The main genre, "" when there's none to find, or null while it's not known yet.</summary>
    public static string? Of(Game g)
    {
        lock (Lock) return All.TryGetValue(g.Key, out var genre) ? genre : null;
    }

    /// <summary>Looks up the genres not known yet (a few at a time).</summary>
    public static async Task EnsureAsync(IEnumerable<Game> games)
    {
        var missing = games.Where(g => g.IsGame && Of(g) == null).Select(g => (g.Key, g.AppId, g.Name, g.IsSteam)).ToList();
        lock (Lock)
        {
            if (_busy || missing.Count == 0) return;
            _busy = true;
        }
        try
        {
            int found = 0;
            using var gate = new SemaphoreSlim(3);
            await Task.WhenAll(missing.Select(async m =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    int appId = m.IsSteam ? m.AppId : await GameLookup.FindSteamMatchAsync(m.Name, CancellationToken.None).ConfigureAwait(false);
                    string genre = "";
                    if (appId > 0)
                    {
                        var details = await SteamStoreClient.GetDetailsAsync(appId, CancellationToken.None).ConfigureAwait(false);
                        if (details == null) return; // offline: try again another time
                        genre = details.Genres.Split(',').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "";
                    }
                    lock (Lock) All[m.Key] = genre;
                    if (Interlocked.Increment(ref found) % 15 == 0) { Save(); Updated?.Invoke(); }
                }
                catch (Exception) { }
                finally { gate.Release(); }
            })).ConfigureAwait(false);
            Save();
            Updated?.Invoke();
        }
        finally
        {
            lock (Lock) _busy = false;
        }
    }

    private static void Save()
    {
        try
        {
            string json;
            lock (Lock) json = JsonSerializer.Serialize(All);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception) { }
    }
}
