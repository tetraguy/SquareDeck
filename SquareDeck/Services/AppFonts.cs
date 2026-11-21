using System.IO;
using System.Windows.Media;

namespace SquareDeck.Services;

/// <summary>
/// Fonts: the ones Windows has, by name ("Georgia"), and the ones that come with SquareDeck in its Fonts folder,
/// written "file:(file name)#(family)[#(other name)…]". A font file can be known by more than one family name (its
/// newer "typographic" name and its older one), so each is tried in turn; Windows' own font is the last fallback.
/// </summary>
public static class AppFonts
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Fonts");

    public static FontFamily Family(string? font)
    {
        if (string.IsNullOrWhiteSpace(font)) return new FontFamily("Segoe UI");
        if (!font.StartsWith("file:", StringComparison.Ordinal)) return new FontFamily(font);
        var parts = font[5..].Split('#');
        string file = parts[0];
        if (parts.Length < 2 || !File.Exists(Path.Combine(Folder, file))) return new FontFamily("Segoe UI");
        // "./File.ttf#Name A, ./File.ttf#Name B, Segoe UI" (WPF tries each name in turn).
        string list = string.Join(", ", parts.Skip(1).Select(name => "./" + file + "#" + name)) + ", Segoe UI";
        return new FontFamily(new Uri(Folder.TrimEnd('\\', '/') + "/"), list);
    }
}
