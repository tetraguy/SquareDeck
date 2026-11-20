using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquareDeck.Services;

/// <summary>One note in a game's notebook. The rich text itself is in its own file (see <see cref="GameNotebook"/>).</summary>
public sealed class NoteEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = "Untitled note";
    public string Preview { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Updated { get; set; } = DateTime.Now;
    public bool Pinned { get; set; }
    public string Color { get; set; } = "slate";
    public int Words { get; set; }
    public int Pictures { get; set; }
    public int Checked { get; set; }
    public int Checks { get; set; }
}

/// <summary>
/// Each game's notebook: any number of rich-text notes with pictures, kept in Notes\&lt;game&gt;\ in the data folder.
/// index.json lists the notes; each note's text and pictures are one XAML package (&lt;id&gt;.xamlpkg), and a small
/// thumbnail of its first picture (&lt;id&gt;.jpg) is kept for the note cards.
/// </summary>
public static class GameNotebook
{
    public static readonly (string Id, string Name, string Hex)[] Colors =
    {
        ("slate", "Plain", ""), ("sun", "Sunshine", "#FFD166"), ("mint", "Mint", "#5FD3A0"), ("sky", "Sky", "#5DB8FF"),
        ("rose", "Rose", "#FF7A93"), ("lavender", "Lavender", "#A98BFF"), ("peach", "Peach", "#FF9F5A"),
    };

    public static Color? ColorOf(string id) =>
        Colors.FirstOrDefault(c => c.Id == id).Hex is { Length: > 0 } hex ? (Color)ColorConverter.ConvertFromString(hex) : null;

    private static string Root => Path.Combine(Settings.DataDir, "Notes");

    public static string Folder(Game game)
    {
        var safe = new string(game.Key.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ':' ? '_' : c).ToArray());
        return Path.Combine(Root, safe);
    }

    public static string DocumentPath(Game game, NoteEntry note) => Path.Combine(Folder(game), note.Id + ".xamlpkg");
    public static string ThumbPath(Game game, NoteEntry note) => Path.Combine(Folder(game), note.Id + ".jpg");

    public static List<NoteEntry> Load(Game game)
    {
        try
        {
            var path = Path.Combine(Folder(game), "index.json");
            if (File.Exists(path)) return JsonSerializer.Deserialize<List<NoteEntry>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception) { }
        return new();
    }

    public static void Save(Game game, List<NoteEntry> notes)
    {
        try
        {
            Directory.CreateDirectory(Folder(game));
            File.WriteAllText(Path.Combine(Folder(game), "index.json"), JsonSerializer.Serialize(notes, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
    }

    /// <summary>Pinned first, then the most recently changed.</summary>
    public static IEnumerable<NoteEntry> Ordered(IEnumerable<NoteEntry> notes) =>
        notes.OrderByDescending(n => n.Pinned).ThenByDescending(n => n.Updated);

    public static void Delete(Game game, List<NoteEntry> notes, NoteEntry note)
    {
        notes.RemoveAll(n => n.Id == note.Id);
        foreach (var file in new[] { DocumentPath(game, note), ThumbPath(game, note) })
            try { File.Delete(file); } catch (Exception) { }
        Save(game, notes);
    }

    /// <summary>
    /// The old single plain-text note (from before notebooks) becomes the first note, once.
    /// Returns true if something was moved over (the caller saves the library).
    /// </summary>
    public static bool Migrate(Game game, LibraryStore library)
    {
        var mine = library.Find(game.Key);
        if (mine == null || string.IsNullOrWhiteSpace(mine.Notes)) return false;
        var notes = Load(game);
        var note = new NoteEntry { Title = "My notes", Created = DateTime.Now, Updated = DateTime.Now };
        var doc = new FlowDocument();
        foreach (var line in mine.Notes.Replace("\r", "").Split('\n')) doc.Blocks.Add(new Paragraph(new Run(line)));
        WriteDocument(game, note, doc);
        notes.Add(note);
        Save(game, notes);
        mine.Notes = null;
        return true;
    }

    // ------------------------------------------------------------------ documents

    public static FlowDocument ReadDocument(Game game, NoteEntry note)
    {
        var doc = new FlowDocument();
        var path = DocumentPath(game, note);
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                new TextRange(doc.ContentStart, doc.ContentEnd).Load(stream, DataFormats.XamlPackage);
            }
        }
        catch (Exception) { }
        return doc;
    }

    /// <summary>Saves the text and pictures, and refreshes the note's preview, counts and thumbnail.</summary>
    public static void WriteDocument(Game game, NoteEntry note, FlowDocument doc)
    {
        Directory.CreateDirectory(Folder(game));
        var path = DocumentPath(game, note);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(stream, DataFormats.XamlPackage);
        File.Move(temp, path, overwrite: true);

        var text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var joined = string.Join("  ·  ", lines);
        note.Preview = joined.Length > 220 ? joined[..220] + "…" : joined;
        note.Words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetterOrDigit));
        note.Checks = lines.Count(l => l.StartsWith('☐') || l.StartsWith('☑'));
        note.Checked = lines.Count(l => l.StartsWith('☑'));
        var pictures = Images(doc).ToList();
        note.Pictures = pictures.Count;
        note.Updated = DateTime.Now;

        var thumb = ThumbPath(game, note);
        try
        {
            if (pictures.FirstOrDefault()?.Source is BitmapSource first)
            {
                double scale = Math.Min(1, 360.0 / Math.Max(1, first.PixelWidth));
                var small = new TransformedBitmap(first, new ScaleTransform(scale, scale));
                var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
                encoder.Frames.Add(BitmapFrame.Create(small));
                using var fs = File.Create(thumb);
                encoder.Save(fs);
            }
            else if (File.Exists(thumb)) File.Delete(thumb);
        }
        catch (Exception) { }
    }

    /// <summary>Every picture in a note, in order.</summary>
    public static IEnumerable<System.Windows.Controls.Image> Images(FlowDocument doc)
    {
        foreach (var block in doc.Blocks)
            foreach (var image in Images(block)) yield return image;
    }

    private static IEnumerable<System.Windows.Controls.Image> Images(Block block)
    {
        switch (block)
        {
            case Paragraph p:
                foreach (var inline in p.Inlines)
                    if (inline is InlineUIContainer { Child: System.Windows.Controls.Image img }) yield return img;
                break;
            case BlockUIContainer { Child: System.Windows.Controls.Image img }:
                yield return img;
                break;
            case List list:
                foreach (var item in list.ListItems)
                    foreach (var b in item.Blocks)
                        foreach (var img in Images(b)) yield return img;
                break;
            case Section section:
                foreach (var b in section.Blocks)
                    foreach (var img in Images(b)) yield return img;
                break;
        }
    }
}
