using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SquareDeck.Services;

/// <summary>
/// Discord Rich Presence (Experimental: Discord): your Discord status shows the game you're playing from SquareDeck
/// ("Playing Hades · on SquareDeck, for 23 minutes"), with its cover, or that you're browsing SquareDeck. It talks
/// to the Discord app on this PC through its local connection, so the Discord app must be running. Discord needs
/// an application ID for this: one you make for free in Discord's Developer Portal, whose name is what friends see.
/// Reading or sending Discord messages isn't something Discord allows other apps to do.
/// </summary>
public static class DiscordPresence
{
    private static NamedPipeClientStream? _pipe;
    private static string _clientId = "";
    private static string _lastKey = "";
    private static readonly SemaphoreSlim Turn = new(1, 1);

    /// <summary>Connected to the Discord app.</summary>
    public static bool Connected => _pipe?.IsConnected == true;

    /// <summary>Why it isn't showing, in a sentence (null when it is).</summary>
    public static string? Problem { get; private set; }

    /// <summary>Shows what you're doing: a game (with when it started and its cover), or browsing, or nothing (null game, idle false).</summary>
    public static async Task UpdateAsync(string clientId, string? game, DateTime? started, string? imageUrl, bool showBrowsing)
    {
        string key = $"{clientId}|{game}|{started:O}|{imageUrl}|{showBrowsing}";
        if (key == _lastKey && Connected) return;
        await Turn.WaitAsync();
        try
        {
            if (!await EnsureAsync(clientId)) return;
            JsonNode? activity = null;
            if (game != null)
            {
                var a = new JsonObject
                {
                    ["details"] = Trim(game),
                    ["state"] = "on SquareDeck",
                    ["type"] = 0,
                };
                if (started is { } s) a["timestamps"] = new JsonObject { ["start"] = new DateTimeOffset(s.ToUniversalTime()).ToUnixTimeSeconds() };
                if (imageUrl is { Length: > 0 } && imageUrl.StartsWith("https://"))
                    a["assets"] = new JsonObject { ["large_image"] = imageUrl, ["large_text"] = Trim(game) };
                activity = a;
            }
            else if (showBrowsing)
                activity = new JsonObject { ["details"] = "Browsing the library", ["state"] = "on SquareDeck", ["type"] = 0 };
            var command = new JsonObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = new JsonObject { ["pid"] = Environment.ProcessId, ["activity"] = activity },
                ["nonce"] = Guid.NewGuid().ToString()
            };
            await SendAsync(1, command.ToJsonString());
            await ReadAsync(); // Discord's answer (an error says why)
            _lastKey = key;
            Problem = null;
        }
        catch (Exception ex)
        {
            Problem = "Lost the connection to Discord: " + ex.Message;
            Close();
        }
        finally { Turn.Release(); }
    }

    /// <summary>Takes the status away and disconnects.</summary>
    public static async Task ClearAsync()
    {
        if (!Connected) { Close(); return; }
        await Turn.WaitAsync();
        try
        {
            var command = new JsonObject { ["cmd"] = "SET_ACTIVITY", ["args"] = new JsonObject { ["pid"] = Environment.ProcessId, ["activity"] = null }, ["nonce"] = Guid.NewGuid().ToString() };
            await SendAsync(1, command.ToJsonString());
        }
        catch (Exception) { }
        finally { Close(); Turn.Release(); }
    }

    private static string Trim(string s) => s.Length <= 120 ? s : s[..120];

    private static async Task<bool> EnsureAsync(string clientId)
    {
        if (Connected && clientId == _clientId) return true;
        Close();
        if (string.IsNullOrWhiteSpace(clientId) || !clientId.All(char.IsDigit))
        {
            Problem = "Add your Discord application ID first.";
            return false;
        }
        // Discord listens on the first free one of discord-ipc-0 to -9.
        for (int i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(400);
                _pipe = pipe;
                break;
            }
            catch (Exception) { pipe.Dispose(); }
        }
        if (_pipe == null)
        {
            Problem = "The Discord app isn't running on this PC. Open it and your status shows up.";
            return false;
        }
        _clientId = clientId;
        await SendAsync(0, new JsonObject { ["v"] = 1, ["client_id"] = clientId }.ToJsonString());
        var (op, body) = await ReadAsync();
        if (op != 1 || !body.Contains("READY"))
        {
            Problem = body.Contains("Invalid Client ID", StringComparison.OrdinalIgnoreCase) || body.Contains("4000")
                ? "Discord didn't accept that application ID. Check it in Discord's Developer Portal."
                : "Discord didn't answer as expected.";
            Close();
            return false;
        }
        return true;
    }

    private static async Task SendAsync(int op, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + payload.Length];
        BitConverter.GetBytes(op).CopyTo(frame, 0);
        BitConverter.GetBytes(payload.Length).CopyTo(frame, 4);
        payload.CopyTo(frame, 8);
        await _pipe!.WriteAsync(frame);
        await _pipe.FlushAsync();
    }

    private static async Task<(int Op, string Body)> ReadAsync()
    {
        var header = new byte[8];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _pipe!.ReadExactlyAsync(header, timeout.Token);
        int op = BitConverter.ToInt32(header, 0), length = BitConverter.ToInt32(header, 4);
        if (length is < 0 or > 1 << 20) throw new IOException("Discord sent something unexpected.");
        var body = new byte[length];
        await _pipe.ReadExactlyAsync(body, timeout.Token);
        return (op, Encoding.UTF8.GetString(body));
    }

    private static void Close()
    {
        try { _pipe?.Dispose(); } catch (Exception) { }
        _pipe = null;
        _lastKey = "";
    }
}
