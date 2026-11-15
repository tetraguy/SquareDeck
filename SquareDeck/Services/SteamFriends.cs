using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>A Steam friend. State: 0 offline, 1 online, 2 busy, 3 away, 4 snooze, 5/6 looking to trade/play.</summary>
public sealed record Friend(string Id, string Name, int State, string? Game, int? GameAppId, string Avatar)
{
    public bool IsOnline => State != 0;
    public bool InGame => !string.IsNullOrEmpty(Game);

    public string Status => InGame ? "Playing " + Game : State switch
    {
        0 => "Offline",
        2 => "Busy",
        3 => "Away",
        4 => "Snooze",
        _ => "Online"
    };
}

/// <summary>Friends list from the Steam Web API. Needs the user's own API key.</summary>
public static class SteamFriends
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private const string Api = "https://api.steampowered.com/";

    /// <summary>Friends, in-game first, then online, then offline. On failure the list is null and Error says why.</summary>
    public static async Task<(List<Friend>? Friends, string? Error)> GetAsync(string apiKey, CancellationToken ct)
    {
        var steamId = SteamLibrary.GetSteamId64();
        if (steamId == null) return (null, "Couldn't tell which Steam account is signed in on this PC. Open Steam and sign in.");

        try
        {
            var key = Uri.EscapeDataString(apiKey.Trim());
            using var listResp = await Http.GetAsync(
                $"{Api}ISteamUser/GetFriendList/v1/?key={key}&steamid={steamId}&relationship=friend", ct).ConfigureAwait(false);
            if (listResp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (null, "Steam didn't accept the request. Check the API key, and that your friends list isn't set to private.");
            listResp.EnsureSuccessStatusCode();

            var ids = new List<string>();
            using (var doc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)))
            {
                if (doc.RootElement.TryGetProperty("friendslist", out var fl) && fl.TryGetProperty("friends", out var arr))
                    foreach (var f in arr.EnumerateArray())
                        if (f.TryGetProperty("steamid", out var id) && id.GetString() is { Length: > 0 } s) ids.Add(s);
            }

            var friends = new List<Friend>();
            // The API takes up to 100 ids per call.
            foreach (var chunk in ids.Chunk(100))
            {
                var json = await Http.GetStringAsync(
                    $"{Api}ISteamUser/GetPlayerSummaries/v2/?key={key}&steamids={string.Join(",", chunk)}", ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("response", out var r) || !r.TryGetProperty("players", out var players)) continue;
                foreach (var p in players.EnumerateArray())
                {
                    string Str(string name) =>
                        p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    int state = p.TryGetProperty("personastate", out var st) && st.TryGetInt32(out var n) ? n : 0;
                    var game = Str("gameextrainfo");
                    friends.Add(new Friend(Str("steamid"), Str("personaname"), state,
                        game.Length > 0 ? game : null,
                        int.TryParse(Str("gameid"), out var appId) ? appId : null,
                        Str("avatarmedium")));
                }
            }

            return (friends
                .OrderByDescending(f => f.InGame)
                .ThenByDescending(f => f.IsOnline)
                .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(), null);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException)
        {
            return (null, "Couldn't reach Steam. Check the internet connection.");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
