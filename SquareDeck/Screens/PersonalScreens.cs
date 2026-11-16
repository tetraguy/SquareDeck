using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Ready-made looks, plus wallpaper, tile corners and sound set to mix your own.</summary>
public sealed class ThemeScreen : ListScreen
{
    public ThemeScreen() : base("Themes") { }

    public override void OnResumed() => Rebuild();

    protected override void Build()
    {
        var s = Host.Settings;

        AddHeader("Ready-made looks");
        foreach (var preset in Theme.Presets)
        {
            var p = preset;
            string details = (p.Mode switch { "dark" => "Dark", "oled" => "Black", _ => "Light" }) + ", " +
                             Theme.CornerStyles.First(c => c.Radius == p.Corner).Name.ToLowerInvariant() + " tiles, " +
                             UiSound.Sets.First(x => x.Id == p.Sounds).Name.ToLowerInvariant() + " sounds";
            AddRow(p.Name, details, "", click: () =>
            {
                s.ThemeMode = p.Mode;
                s.DarkTheme = p.Mode != "light";
                s.AccentColor = p.Accent;
                s.Wallpaper = p.Wallpaper;
                s.TileCorner = p.Corner;
                s.SoundSet = p.Sounds;
                s.ThemePalette = p.Palette;
                Apply();
            });
        }

        AddHeader("Make your own");
        AddRow("Wallpaper", WallpaperLabel(s.Wallpaper), "", click: () => Host.Push(new WallpaperScreen()));
        AddRow("Color theme", Theme.Palettes.TryGetValue(s.ThemePalette, out var pal) ? pal.Name : "Standard", "\uE790", click: () =>
        {
            // Standard -> GTA VI -> ... -> Standard
            var ids = new List<string> { "" };
            ids.AddRange(Theme.Palettes.Keys);
            s.ThemePalette = ids[(ids.IndexOf(s.ThemePalette) + 1) % ids.Count];
            Apply();
        }, subtitle: "Background and button colors (styles with their own colors keep them)");
        AddRow("Tile corners", Theme.CornerStyles.FirstOrDefault(c => c.Radius == s.TileCorner).Name ?? "Custom", "", click: () =>
        {
            // Square -> Rounded -> Round -> Square
            int index = Array.FindIndex(Theme.CornerStyles, c => c.Radius == s.TileCorner);
            s.TileCorner = Theme.CornerStyles[(index + 1) % Theme.CornerStyles.Length].Radius;
            Apply();
        });
        AddRow("Sound set", UiSound.SetName(s.SoundSet), "", click: ChooseSounds);
        if (s.SoundSet == "custom")
            AddNote("Custom sounds come from " + (s.SoundFolder.Length > 0 ? s.SoundFolder : "(no folder chosen)") +
                    ". Put WAV files there named " + string.Join(", ", UiSound.CustomFiles) +
                    ". Any that are missing use the Classic sound.");
        if (!s.UiSounds) AddNote("Interface sounds are currently turned off in Settings.");
    }

    private static string WallpaperLabel(string setting)
    {
        if (string.IsNullOrEmpty(setting)) return "None";
        if (setting.StartsWith("preset:", StringComparison.Ordinal) && int.TryParse(setting[7..], out var i) &&
            i >= 0 && i < Theme.Wallpapers.Length) return Theme.Wallpapers[i].Name;
        if (setting.StartsWith("image:", StringComparison.Ordinal))
            return Theme.PictureWallpapers.FirstOrDefault(w => w.File == setting[6..]).Name ?? "Picture";
        return "Custom image";
    }

    private void Apply()
    {
        Host.Settings.Save();
        Host.ApplyLook();
        UiSound.Select();
        Rebuild();
    }

    private void ChooseSounds() => Host.Push(new SoundsScreen());
}

/// <summary>Background for the home screen.</summary>
public sealed class WallpaperScreen : GridScreen
{
    public WallpaperScreen() : base("Wallpaper")
    {
        AddTile(GlyphBox(208, 117, ""), "None", 208, () => Pick(""));
        for (int i = 0; i < Theme.Wallpapers.Length; i++)
        {
            string setting = "preset:" + i;
            var swatch = new Border { Width = 208, Height = 117, Background = Theme.WallpaperBrush(setting) };
            AddTile(swatch, Theme.Wallpapers[i].Name, 208, () => Pick(setting));
        }
        foreach (var (name, file) in Theme.PictureWallpapers)
        {
            string setting = "image:" + file;
            var swatch = new Border { Width = 208, Height = 117, Background = Theme.WallpaperBrush(setting) };
            AddTile(swatch, name, 208, () => Pick(setting));
        }
        AddTile(GlyphBox(208, 117, ""), "Choose image…", 208, Browse);
    }

    private void Pick(string setting)
    {
        Host.Settings.Wallpaper = setting;
        Host.Settings.Save();
        Host.ApplyLook();
        Host.Pop(this);
    }

    private async void Browse()
    {
        var picked = await Host.PickFileAsync("Choose a wallpaper", new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" });
        if (picked == null) return;
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            foreach (var old in Directory.EnumerateFiles(Settings.DataDir, "wallpaper_*"))
            {
                try { File.Delete(old); } catch (IOException) { }
            }
            var dest = Path.Combine(Settings.DataDir, "wallpaper_" + DateTime.Now.Ticks + Path.GetExtension(picked));
            File.Copy(picked, dest, overwrite: true);
            Pick(dest);
        }
        catch (Exception ex)
        {
            _ = Host.AskAsync("Couldn't use that image", ex.Message, "OK");
        }
    }
}

/// <summary>Your own rating, status and notes for one game.</summary>
public sealed class GameNotesScreen : ListScreen
{
    public static readonly string[] Statuses = { "Playing", "Finished", "Dropped", "Backlog" };

    private readonly Game _game;

    public GameNotesScreen(Game game) : base("Rating and notes – " + game.Name)
    {
        _game = game;
    }

    private ItemOverride Mine => Host.Library.For(_game.Key);

    protected override void Build()
    {
        var mine = Host.Library.Find(_game.Key);
        AddRow("My rating", mine is { Rating: > 0 } ? Ui.Stars(mine.Rating) : "Not rated", "", click: Rate);
        AddRow("Status", string.IsNullOrWhiteSpace(mine?.Status) ? "Not set" : mine.Status, "", click: SetStatus);
        AddRow("Notes", string.IsNullOrWhiteSpace(mine?.Notes) ? "None" : "Edit", "", click: EditNotes);
        if (!string.IsNullOrWhiteSpace(mine?.Notes)) AddNote(mine.Notes);
        AddNote("Rating and status can be used to filter and sort the game library.");
    }

    private void Saved()
    {
        Host.Library.Save();
        Host.LibraryEdited();
        Rebuild();
    }

    private async void Rate()
    {
        var labels = Enumerable.Range(1, 5).Reverse().Select(Ui.Stars).Append("Not rated").ToArray();
        int choice = await Host.AskAsync("Rate " + _game.Name, null, labels);
        if (choice < 0) return;
        Mine.Rating = choice == 5 ? 0 : 5 - choice;
        Saved();
    }

    private async void SetStatus()
    {
        int choice = await Host.AskAsync("Status of " + _game.Name, null, Statuses.Append("Not set").ToArray());
        if (choice < 0) return;
        Mine.Status = choice < Statuses.Length ? Statuses[choice] : null;
        Saved();
    }

    private async void EditNotes()
    {
        var text = await Host.PromptAsync("Notes for " + _game.Name,
            "Anything you want to remember: where you left off, settings, tips. Leave it empty to remove the note.",
            Host.Library.Find(_game.Key)?.Notes ?? "");
        if (text == null) return;
        Mine.Notes = text.Trim().Length == 0 ? null : text.Trim();
        Saved();
    }
}

/// <summary>Achievements the launcher has seen you unlock, newest first.</summary>
public sealed class UnlocksScreen : ListScreen
{
    public UnlocksScreen() : base("Recent achievements", "{Back}  Back") { }

    protected override void Build()
    {
        var feed = Host.Achievements.Feed;
        if (feed.Count == 0)
        {
            AddNote("Nothing here yet. When you close a Steam game, the launcher checks whether you unlocked anything " +
                    "while playing and lists it here.");
            AddNote("This needs “Game details” set to Public in your Steam profile's privacy settings, and only " +
                    "covers sessions played while the launcher is open.");
            return;
        }

        foreach (var day in feed.Take(120).GroupBy(e => e.When.Date))
        {
            AddHeader(day.Key == DateTime.Today ? "Today" : day.Key == DateTime.Today.AddDays(-1) ? "Yesterday" : day.Key.ToString("MMMM d"));
            foreach (var e in day)
            {
                var entry = e;
                var icon = new Image();
                if (entry.Icon.Length > 0) _ = LoadIconAsync(icon, entry.Icon);
                AddRow(entry.Name + "  –  " + entry.Game, Ui.Ago(entry.When), icon: icon, click: () =>
                    _ = Host.AskAsync(entry.Name, entry.Game + (entry.Description.Length > 0 ? "\n\n" + entry.Description : ""), "OK"));
            }
        }
    }

    private static async Task LoadIconAsync(Image view, string url) =>
        view.Source = await Task.Run(() => ImageLoader.FromUrlAsync(url, 64));
}
