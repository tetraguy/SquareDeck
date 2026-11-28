using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Experimental "Cover styles" (8.5.81): the cut, pose, frame and icon shape for the Home row and the library, with a
/// row of your own covers at the top showing the choices as they're made.
/// </summary>
public sealed class CoverStyleScreen : ListScreen
{
    private string _where = "home"; // home or lib

    public CoverStyleScreen() : base("Cover styles") { }

    private bool EditingLibrary => _where == "lib";
    private string Key => EditingLibrary && !CoverStyles.LibraryMatchesHome ? "lib" : "home";

    protected override void Build()
    {
        var s = Host.Settings;
        var look = CoverStyles.Read(Key);
        AddPreview(look);

        AddRow("Editing", EditingLibrary ? "Game library" : "Home row", "\uE8A9", click: () => { _where = EditingLibrary ? "home" : "lib"; Rebuild(); },
            subtitle: "Select to switch between the Home row and the game library");
        if (EditingLibrary)
        {
            AddRow("Library matches Home", CoverStyles.LibraryMatchesHome ? "On" : "Off", "\uE8EF", click: () =>
            {
                bool same = CoverStyles.LibraryMatchesHome;
                if (same)
                {
                    // Starting its own: from the Home row's choices.
                    var home = CoverStyles.Read("home");
                    Labs.Put("coverstyle.lib.shape", home.Shape, save: false);
                    Labs.Put("coverstyle.lib.pose", home.Pose, save: false);
                    Labs.Put("coverstyle.lib.frame", home.Frame, save: false);
                    Labs.Put("coverstyle.lib.color", home.FrameColor, save: false);
                    Labs.Put("coverstyle.lib.straighten", home.Straighten ? "1" : "0", save: false);
                    Labs.Put("coverstyle.lib.icon", home.Icon, save: false);
                }
                Labs.Put("coverstyle.libsame", same ? "0" : "1");
                Rebuild();
            }, subtitle: "The library uses the Home row's choices. Off: it has its own");
        }

        AddHeader("Shape");
        if (EditingLibrary)
            AddRow("Art", s.LibraryView switch { "grid" => "Tall", "header" => "Wide", _ => "Square" }, "\uE91B", click: () =>
            {
                s.LibraryView = s.LibraryView switch { "square" => "grid", "grid" => "header", _ => "square" };
                s.Save();
                Rebuild();
            }, subtitle: "Square covers, tall 2:3 posters or wide header capsules (also in Settings > Games and library)");
        else
            AddRow("Art", UiStyles.TileArts.FirstOrDefault(x => x.Id == s.TileArt).Name ?? "Default", "\uE91B", click: async () =>
            {
                int c = await Host.AskAsync("Art", "How games look on the home row (also in Settings > Home screen).", UiStyles.TileArts.Select(x => x.Name).ToArray());
                if (c < 0) return;
                s.TileArt = UiStyles.TileArts[c].Id;
                s.Save();
                Host.ApplySettings();
                Changed();
            }, subtitle: "Square, tall or wide");
        if (!(EditingLibrary && CoverStyles.LibraryMatchesHome))
        {
            {
                Choice("Cut", "shape", look.Shape, CoverStyles.Shapes, "\uE7B8");
                Choice("Pose", "pose", look.Pose, CoverStyles.Poses, "\uE7AD");
                if (look.Pose != "flat")
                    AddRow("Straighten when highlighted", look.Straighten ? "On" : "Off", "\uE73E",
                        click: () => Put("straighten", look.Straighten ? "0" : "1"),
                        subtitle: "The highlighted cover snaps flat and faces you");
                AddHeader("Frame");
                Choice("Frame", "frame", look.Frame, CoverStyles.Frames, "\uE91B");
                if (look.Frame is "thin" or "thick" or "double" or "glow" or "gradient" or "dashed" or "neon" or "pixel")
                    AddRow("Frame color", CoverStyles.FrameColors.FirstOrDefault(x => x.Id == look.FrameColor).Name ?? "Highlight color", "\uE790", click: async () =>
                    {
                        int c = await Host.AskAsync("Frame color", null, CoverStyles.FrameColors.Select(x => x.Name).ToArray());
                        if (c >= 0) Put("color", CoverStyles.FrameColors[c].Id);
                    });
                AddHeader("Icons");
                Choice("Icon shape", "icon", look.Icon, CoverStyles.IconShapes, "\uE8B9");
                AddNote(EditingLibrary
                    ? "Icons: the small covers in the List view."
                    : "Icons: apps on the Home row that show their program icon, and the running game's icon beside the clock.");
            }
        }
        if (EditingLibrary && CoverStyles.LibraryMatchesHome)
            AddNote("Turn off “Library matches Home” to give the library its own cut, pose, frame and icons. Fanned and Lean in become " +
                    "Scattered and Flat there, since the library wraps into rows.");
        else if (!EditingLibrary)
            AddNote("The “Continue where you left off” tile and the Add and Library tiles keep their own look. Reduce motion and " +
                    "Performance mode keep the shapes and frames but stop the Gentle bob.");
    }

    private void Choice(string label, string key, string current, (string Id, string Name, string About)[] options, string glyph)
    {
        var chosen = options.FirstOrDefault(o => o.Id == current);
        AddRow(label, chosen.Name ?? options[0].Name, glyph, click: async () =>
        {
            int c = await Host.AskAsync(label, null, options.Select(o => o.Name).ToArray());
            if (c >= 0) Put(key, options[c].Id);
        }, subtitle: chosen.About ?? options[0].About);
    }

    private void Put(string key, string value)
    {
        Labs.Put($"coverstyle.{Key}.{key}", value);
        Changed();
    }

    private void Changed()
    {
        Host.ApplyCoverStyle();
        Rebuild();
    }

    /// <summary>Four of your covers in the chosen look, the second one highlighted.</summary>
    private void AddPreview(CoverStyles.Look look)
    {
        var covers = Host.AllGames.Where(g => !g.UsesIcon).Select(g => g.Cover).OfType<BitmapSource>().Take(4).ToList();
        if (covers.Count == 0) return;
        string art = EditingLibrary ? Host.Settings.LibraryView switch { "grid" => "tall", "header" => "wide", _ => "square" }
                                    : Host.Settings.TileArt is "tall" or "wide" ? Host.Settings.TileArt : "square";
        double w = art == "wide" ? 230 : 150, h = art == "tall" ? 225 : art == "wide" ? 108 : 150;
        double corner = Math.Max(Theme.TileCorner, 6);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 26) };
        for (int i = 0; i < covers.Count; i++)
        {
            var face = new Grid { Width = w, Height = h };
            face.Children.Add(new Image { Source = covers[i], Stretch = Stretch.UniformToFill });
            face.Clip = CoverStyles.Outline(look.Shape, w, h, corner) ?? new RectangleGeometry(new Rect(0, 0, w, h), corner, corner);
            var host = new Grid { Width = w, Height = h, IsHitTestVisible = false };
            var ring = new Border { BorderBrush = i == 1 ? (Brush)FindResource("AccentBrush") : Brushes.Transparent, BorderThickness = new Thickness(4), CornerRadius = new CornerRadius(corner + 3), Margin = new Thickness(-6) };
            CoverStyles.Decorate(host, look, w, h, corner, look.Shape == "style" ? null : ring);
            var body = new Grid { Margin = new Thickness(14, 0, 14, 0), RenderTransformOrigin = new Point(0.5, 0.5) };
            if (look.Shape == "style") body.Children.Add(ring);
            body.Children.Add(face);
            body.Children.Add(host);
            body.RenderTransform = CoverStyles.PoseFor(look, i, 1, library: EditingLibrary) ?? Transform.Identity;
            if (i == 1) { body.LayoutTransform = new ScaleTransform(1.08, 1.08); }
            row.Children.Add(body);
        }
        Rows.Children.Add(row);
    }
}
