using System.IO;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>
/// Saving SquareDeck's own files without risking them: the new text goes to a temporary file beside the real one,
/// which then takes its place in one step. If the battery runs out or the app is closed mid-save, the old file is
/// still whole (a half-written settings.json used to mean every setting back to the default).
/// </summary>
public static class SafeFile
{
    /// <summary>Indented JSON, made once (a new options object each save made every save slower).</summary>
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static void WriteAllText(string path, string text)
    {
        string temp = path + ".saving";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
