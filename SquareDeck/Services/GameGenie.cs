using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SquareDeck.Services;

/// <summary>
/// Game Genie (Experimental): questions and answers about a game, written by Google Gemini from what SquareDeck
/// already knows about it (the Steam store page and Wikipedia's summary), plus answers to your own questions.
/// A game's sheet is kept on disk, so opening it again costs nothing; "More questions" and your own questions add to it.
/// </summary>
public static class GameGenie
{
    public sealed class Fact
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public sealed class Answer
    {
        public string Q { get; set; } = "";
        public string A { get; set; } = "";
        public string Category { get; set; } = "Tips";
        public bool Spoiler { get; set; }
        public bool Mine { get; set; } // asked by you, not one of the Genie's own
    }

    public sealed class Sheet
    {
        public string Game { get; set; } = "";
        public string Summary { get; set; } = "";
        public List<Fact> Facts { get; set; } = new();
        public List<Answer> Answers { get; set; } = new();
        public DateTime Made { get; set; }
    }

    public sealed class GenieException(string message) : Exception(message);

    /// <summary>The order the categories show in.</summary>
    public static readonly string[] Categories = { "Getting started", "Tips", "Story", "Secrets", "Completion", "Technical", "Your questions" };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    // Tried when the key's own list of models can't be read (newest first). Google retires models often (1.5 and 2.0
    // are gone), so the Gemini API's list is read first and the newest Flash model on it is used; the first route that
    // answers is remembered for the rest of the session.
    private static readonly string[] Models = { "gemini-flash-latest", "gemini-3.8-flash", "gemini-3.7-flash", "gemini-3.6-flash", "gemini-3.5-flash", "gemini-3-flash-preview", "gemini-2.5-flash" };
    private static (bool Vertex, string Model)? _working;
    private static List<string>? _discovered;
    private static string? _discoveredFor;

    private static Settings? _settings;
    public static void Init(Settings settings) => _settings = settings;

    /// <summary>Your own key from Settings if you set one, otherwise the one built into this copy (ApiKeys.Local.cs).</summary>
    public static string Key => _settings?.GeminiApiKey is { Length: > 0 } own ? own.Trim() : ApiKeys.Gemini;
    public static bool HasKey => Key.Length > 0;
    public static bool UsingOwnKey => _settings?.GeminiApiKey is { Length: > 0 } own && own.Trim() != ApiKeys.Gemini;

    private static string CacheDir => Path.Combine(Settings.DataDir, "GameGenie");

    private static string CacheFile(string gameKey)
    {
        string safe = string.Concat(gameKey.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(CacheDir, safe + ".json");
    }

    public static Sheet? Cached(string gameKey)
    {
        try
        {
            string file = CacheFile(gameKey);
            return File.Exists(file) ? JsonSerializer.Deserialize<Sheet>(File.ReadAllText(file), Json) : null;
        }
        catch (Exception) { return null; }
    }

    public static void Save(string gameKey, Sheet sheet)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(CacheFile(gameKey), JsonSerializer.Serialize(sheet, Json));
        }
        catch (Exception ex) { CrashLog.Write("Game Genie", ex); }
    }

    public static void Forget(string gameKey)
    {
        try { File.Delete(CacheFile(gameKey)); } catch (Exception) { }
    }

    private const string Rules =
        "You are Game Genie, a friendly expert on video games inside a game launcher on a handheld PC. Be accurate: use the facts given, and " +
        "what is widely known about the game. If you are not sure of something, say so briefly rather than inventing it. Never make up " +
        "achievement names, item names, codes or numbers. Keep answers short and practical (2 to 4 sentences), in plain English, no markdown. " +
        "Anything that gives away the story, an ending, a twist or a late surprise must be marked as a spoiler.";

    /// <summary>A new sheet for a game: a summary, key facts and about 14 questions with answers.</summary>
    public static async Task<Sheet> MakeAsync(string gameKey, string name, string known, CancellationToken ct, Action<Sheet>? progress = null)
    {
        string prompt = Rules + "\n\nGame: " + name + "\nWhat the launcher knows about it:\n" + Trim(known, 6000) + "\n\n" +
            "Write a JSON object with exactly these fields:\n" +
            "{\"summary\": \"two or three sentences on what the game is and why people play it\",\n" +
            " \"facts\": [{\"label\": \"Genre\", \"value\": \"...\"}, ...] (6 to 8 facts: genre, developer, release year, how long to beat (main story and completionist, rough hours), " +
            "players and modes, difficulty, Steam Deck or handheld friendliness, one more of your choice),\n" +
            " \"answers\": [{\"q\": \"question\", \"a\": \"answer\", \"category\": one of \"Getting started\", \"Tips\", \"Story\", \"Secrets\", \"Completion\", \"Technical\", \"spoiler\": true or false}, ...]}\n" +
            "Give 14 answers that a player would really ask, spread over the categories (at least 2 each for Getting started, Tips and Completion; " +
            "Technical covers settings, performance on a handheld PC, controls and saves). Story questions must be spoiler free unless marked as spoilers.";
        // While Gemini writes, the summary, facts and each finished answer are shown as they arrive.
        int shown = -1;
        void Partial(string text)
        {
            if (progress == null) return;
            var partial = new Sheet
            {
                Game = name,
                Summary = PartialString(text, "summary") ?? "",
                Facts = ReadFacts(PartialArray(text, "facts")),
                Answers = ReadAnswers(PartialArray(text, "answers")),
                Made = DateTime.Now
            };
            int size = partial.Answers.Count * 100 + partial.Facts.Count * 2 + (partial.Summary.Length > 0 ? 1 : 0);
            if (size == shown) return;
            shown = size;
            progress(partial);
        }
        var node = await AskJsonAsync(prompt, ct, progress == null ? null : Partial);
        var sheet = new Sheet
        {
            Game = name,
            Summary = node?["summary"]?.GetValue<string>() ?? "",
            Facts = ReadFacts(node?["facts"]),
            Answers = ReadAnswers(node?["answers"]),
            Made = DateTime.Now
        };
        if (sheet.Answers.Count == 0 && sheet.Summary.Length == 0) throw new GenieException("The Genie's answer couldn't be read. Try again.");
        Save(gameKey, sheet);
        return sheet;
    }

    /// <summary>More questions, different from the ones already on the sheet.</summary>
    public static async Task<List<Answer>> MoreAsync(string gameKey, Sheet sheet, string known, CancellationToken ct, Action<List<Answer>>? progress = null)
    {
        string asked = string.Join("\n", sheet.Answers.Select(a => "- " + a.Q));
        string prompt = Rules + "\n\nGame: " + sheet.Game + "\nWhat the launcher knows about it:\n" + Trim(known, 4000) + "\n\n" +
            "These questions are already answered:\n" + Trim(asked, 4000) + "\n\n" +
            "Write a JSON object {\"answers\": [{\"q\": ..., \"a\": ..., \"category\": one of \"Getting started\", \"Tips\", \"Story\", \"Secrets\", \"Completion\", \"Technical\", \"spoiler\": true or false}, ...]} " +
            "with 8 new, different questions players ask about this game, with answers.";
        int shown = -1;
        void Partial(string text)
        {
            if (progress == null) return;
            var found = ReadAnswers(PartialArray(text, "answers")).Where(n => !sheet.Answers.Any(a => a.Q.Equals(n.Q, StringComparison.OrdinalIgnoreCase))).ToList();
            if (found.Count == shown) return;
            shown = found.Count;
            progress(found);
        }
        var node = await AskJsonAsync(prompt, ct, progress == null ? null : Partial);
        var more = ReadAnswers(node?["answers"]).Where(n => !sheet.Answers.Any(a => a.Q.Equals(n.Q, StringComparison.OrdinalIgnoreCase))).ToList();
        sheet.Answers.AddRange(more);
        Save(gameKey, sheet);
        return more;
    }

    /// <summary>An answer to your own question, added to the sheet.</summary>
    public static async Task<Answer> AskAsync(string gameKey, Sheet sheet, string known, string question, CancellationToken ct)
    {
        string prompt = Rules + "\n\nGame: " + sheet.Game + "\nWhat the launcher knows about it:\n" + Trim(known, 4000) + "\n\n" +
            "The player asks: " + question.Trim() + "\n\n" +
            "Write a JSON object {\"a\": \"your answer (up to 6 sentences)\", \"spoiler\": true or false}. If the question isn't about this game, " +
            "answer briefly and kindly anyway.";
        var node = await AskJsonAsync(prompt, ct);
        var answer = new Answer
        {
            Q = question.Trim(),
            A = node?["a"]?.GetValue<string>()?.Trim() ?? "",
            Spoiler = node?["spoiler"] is JsonValue v && v.TryGetValue(out bool b) && b,
            Category = "Your questions",
            Mine = true
        };
        if (answer.A.Length == 0) throw new GenieException("The Genie didn't answer that one. Try asking it another way.");
        sheet.Answers.Insert(0, answer);
        Save(gameKey, sheet);
        return answer;
    }

    // ---------------------------------------------------------------- reading an answer that's still arriving

    /// <summary>A top-level string field of JSON that's still being written, once its closing quote has arrived.</summary>
    internal static string? PartialString(string text, string key)
    {
        int k = text.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (k < 0) return null;
        int colon = text.IndexOf(':', k + key.Length + 2);
        if (colon < 0) return null;
        int open = text.IndexOf('"', colon + 1);
        if (open < 0) return null;
        for (int i = open + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] != '"') continue;
            try { return JsonNode.Parse(text[open..(i + 1)])?.GetValue<string>()?.Trim(); }
            catch (Exception) { return null; }
        }
        return null;
    }

    /// <summary>The finished objects of an array field in JSON that's still being written (the one being written is left out).</summary>
    internal static JsonArray PartialArray(string text, string key)
    {
        var result = new JsonArray();
        int k = text.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (k < 0) return result;
        int open = text.IndexOf('[', k + key.Length + 2);
        if (open < 0) return result;
        bool inString = false;
        int depth = 0, start = -1;
        for (int i = open + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') { if (depth++ == 0) start = i; }
            else if (c == '}' && depth > 0 && --depth == 0 && start >= 0)
            {
                try { if (JsonNode.Parse(text[start..(i + 1)]) is JsonObject o) result.Add(o); } catch (Exception) { }
                start = -1;
            }
            else if (c == ']' && depth == 0) break;
        }
        return result;
    }

    private static List<Fact> ReadFacts(JsonNode? node) =>
        node is JsonArray a
            ? a.OfType<JsonObject>().Select(o => new Fact { Label = Str(o["label"]), Value = Str(o["value"]) }).Where(f => f.Label.Length > 0 && f.Value.Length > 0).Take(10).ToList()
            : new();

    private static List<Answer> ReadAnswers(JsonNode? node) =>
        node is JsonArray a
            ? a.OfType<JsonObject>().Select(o => new Answer
            {
                Q = Str(o["q"]),
                A = Str(o["a"]),
                Category = Categories.FirstOrDefault(c => c.Equals(Str(o["category"]), StringComparison.OrdinalIgnoreCase)) ?? "Tips",
                Spoiler = o["spoiler"] is JsonValue v && v.TryGetValue(out bool b) && b
            }).Where(x => x.Q.Length > 0 && x.A.Length > 0).ToList()
            : new();

    private static string Str(JsonNode? n)
    {
        try { return n is JsonValue v ? (v.TryGetValue(out string? s) ? s?.Trim() ?? "" : v.ToJsonString()) : ""; }
        catch (Exception) { return ""; }
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private static async Task<JsonNode?> AskJsonAsync(string prompt, CancellationToken ct, Action<string>? partial = null)
    {
        string text = await GenerateAsync(prompt, ct, partial);
        // Sometimes wrapped in a code fence despite asking for JSON.
        text = text.Trim();
        if (text.StartsWith("```")) text = text.Trim('`').Trim();
        if (text.StartsWith("json", StringComparison.OrdinalIgnoreCase)) text = text[4..];
        int start = text.IndexOf('{'), end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new GenieException("The Genie's answer couldn't be read. Try again.");
        try { return JsonNode.Parse(text[start..(end + 1)]); }
        catch (Exception) { throw new GenieException("The Genie's answer couldn't be read. Try again."); }
    }

    /// <summary>
    /// One request to Gemini. Keys that start with "AQ." are Vertex AI (Google Cloud) express-mode keys, so those try
    /// Vertex AI first; other keys (AIza…) go to the Gemini API. Each tries a few current models.
    /// </summary>
    private static async Task<string> GenerateAsync(string prompt, CancellationToken ct, Action<string>? partial = null)
    {
        string key = Key;
        if (key.Length == 0) throw new GenieException("Game Genie needs a Google Gemini API key. Add your own in Settings > Library and stores > Service keys.");
        // Speed: these are look-up answers, not puzzles, so the model's "thinking" (which can take longer than the answer
        // itself) is turned down to the least each model allows. A model that refuses the setting is asked again without it.
        string Body(string model, bool thinking)
        {
            var config = new JsonObject { ["temperature"] = 0.6, ["responseMimeType"] = "application/json", ["maxOutputTokens"] = 8192 };
            if (thinking)
                config["thinkingConfig"] = model.Contains("2.5") || model.Contains("2.0")
                    ? new JsonObject { ["thinkingBudget"] = 0 }
                    : new JsonObject { ["thinkingLevel"] = "low" };
            return new JsonObject
            {
                ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt }) }),
                ["generationConfig"] = config
            }.ToJsonString();
        }

        // The Gemini API's own list for this key: the newest Flash models it really offers come first.
        var listed = await DiscoverAsync(key, ct);
        var geminiModels = listed.Concat(Models).Distinct().ToList();

        // AI Studio now gives out "AQ." keys too, so every key tries the Gemini API first, then Vertex AI express.
        var routes = new List<(bool Vertex, string Model)>();
        if (_working is { } w) routes.Add(w);
        foreach (var model in geminiModels) if (!routes.Contains((false, model))) routes.Add((false, model));
        if (key.StartsWith("AQ.", StringComparison.Ordinal))
            foreach (var model in Models.Where(m => m != "gemini-flash-latest")) if (!routes.Contains((true, model))) routes.Add((true, model));

        // Keep the most useful reason when nothing answers: a key or quota problem says more than "model not found".
        string best = "";
        int bestRank = -1;
        void Note(int rank, string text) { if (rank > bestRank) { bestRank = rank; best = text; } }

        for (int r = 0; r < routes.Count; r++)
        {
            var (vertex, model) = routes[r];
            ct.ThrowIfCancellationRequested();
            bool thinking = !NoThinking.Contains(model);
            // Streamed, so each part of the answer can be shown as soon as it's written.
            string method = partial != null ? "streamGenerateContent" : "generateContent";
            string url = vertex
                ? $"https://aiplatform.googleapis.com/v1/publishers/google/models/{model}:{method}?{(partial != null ? "alt=sse&" : "")}key={Uri.EscapeDataString(key)}"
                : $"https://generativelanguage.googleapis.com/v1beta/models/{model}:{method}{(partial != null ? "?alt=sse" : "")}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(Body(model, thinking), Encoding.UTF8, "application/json") };
            if (!vertex) request.Headers.Add("x-goog-api-key", key);
            HttpResponseMessage response;
            try { response = await Http.SendAsync(request, partial != null ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { Note(1, "SquareDeck couldn't reach Google (" + ex.Message + "). Check the internet connection."); continue; }
            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    string text;
                    if (partial != null)
                    {
                        try { text = await ReadStreamAsync(response, partial, ct); }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception ex) { Note(1, "The connection to Google dropped (" + ex.Message + "). Try again."); continue; }
                    }
                    else text = ReadText(await response.Content.ReadAsStringAsync(ct));
                    if (text.Length > 0) { _working = (vertex, model); return text; }
                    Note(3, "Gemini didn't send an answer (it may have been blocked by its safety filter). Try again.");
                    continue;
                }
                string json = await response.Content.ReadAsStringAsync(ct);
                int code = (int)response.StatusCode;
                string message = ErrorMessage(json);
                if (code == 400 && thinking && message.Contains("thinking", StringComparison.OrdinalIgnoreCase))
                {
                    NoThinking.Add(model); // this model doesn't take the setting: the same model again, without it
                    r--;
                    continue;
                }
                string where = vertex ? "Vertex AI" : "the Gemini API";
                switch (code)
                {
                    case 404:
                        Note(0, "None of Gemini's models answered for this key (the last said: " + message + ").");
                        break;
                    case 401 when json.Contains("ACCESS_TOKEN_TYPE_UNSUPPORTED") || message.Contains("OAuth", StringComparison.OrdinalIgnoreCase):
                        Note(vertex ? 4 : 5, "Google didn't accept this \"AQ.\" key for " + where + ". Google is still switching AI Studio keys to the new AQ. kind and some " +
                                             "accounts can't use them yet. Try a key from another Google Cloud project, or a Vertex AI express key.");
                        break;
                    case 400 when message.Contains("API key", StringComparison.OrdinalIgnoreCase):
                    case 401:
                    case 403:
                        Note(vertex ? 4 : 5, "Google didn't accept the Gemini API key for " + where + (message.Length > 0 ? ": " + message : "."));
                        break;
                    case 429:
                        Note(6, "The Gemini API key has reached its limit for now. Try again in a minute." + (message.Contains("limit: 0") ? " (Google says this key's free quota is 0: it may need billing turned on.)" : ""));
                        break;
                    default:
                        Note(2, "Gemini answered with an error (" + code + (message.Length > 0 ? ": " + message : "") + ").");
                        break;
                }
                if (code == 429) break; // the same key everywhere: no point trying the rest now
            }
        }
        throw new GenieException(best.Length > 0 ? best : "Gemini didn't answer. Try again.");
    }

    /// <summary>
    /// The Flash models this key can use on the Gemini API, newest first (stable before preview), read once per key per
    /// session. Empty if the list can't be read; the built-in list is used then.
    /// </summary>
    private static async Task<List<string>> DiscoverAsync(string key, CancellationToken ct)
    {
        if (_discovered != null && _discoveredFor == key) return _discovered;
        var found = new List<string>();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000");
            request.Headers.Add("x-goog-api-key", key);
            using var response = await Http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode && JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["models"] is JsonArray models)
            {
                foreach (var m in models.OfType<JsonObject>())
                {
                    string name = (m["name"]?.GetValue<string>() ?? "").Replace("models/", "");
                    bool generates = m["supportedGenerationMethods"] is JsonArray methods && methods.Any(x => x?.GetValue<string>() == "generateContent");
                    if (!generates || !name.Contains("flash") || System.Text.RegularExpressions.Regex.IsMatch(name, "image|tts|live|audio|lite|transcribe|translate|embed|omni|thinking|exp"))
                        continue;
                    found.Add(name);
                }
                double Version(string n) => double.TryParse(System.Text.RegularExpressions.Regex.Match(n, @"gemini-(\d+(?:\.\d+)?)").Groups[1].Value,
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
                found = found.OrderByDescending(n => n.Contains("latest"))
                             .ThenByDescending(Version)
                             .ThenBy(n => n.Contains("preview"))
                             .ThenBy(n => n.Length)
                             .Take(6).ToList();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { }
        _discovered = found;
        _discoveredFor = key;
        return found;
    }

    /// <summary>Models that refused the thinking setting this session.</summary>
    private static readonly HashSet<string> NoThinking = new();

    /// <summary>
    /// Reads a streamed answer (server-sent events, one JSON chunk per "data:" line), telling <paramref name="partial"/>
    /// the text so far after each chunk.
    /// </summary>
    private static async Task<string> ReadStreamAsync(HttpResponseMessage response, Action<string> partial, CancellationToken ct)
    {
        var all = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            string chunk = ReadText(line[5..].Trim());
            if (chunk.Length == 0) continue;
            all.Append(chunk);
            try { partial(all.ToString()); } catch (Exception) { }
        }
        return all.ToString();
    }

    private static string ReadText(string json)
    {
        try
        {
            var parts = JsonNode.Parse(json)?["candidates"]?[0]?["content"]?["parts"] as JsonArray;
            return parts == null ? "" : string.Concat(parts.Select(p => p?["text"]?.GetValue<string>() ?? ""));
        }
        catch (Exception) { return ""; }
    }

    private static string ErrorMessage(string json)
    {
        try { return JsonNode.Parse(json)?["error"]?["message"]?.GetValue<string>() ?? ""; }
        catch (Exception) { return ""; }
    }
}
