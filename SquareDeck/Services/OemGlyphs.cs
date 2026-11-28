using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquareDeck.Services;

/// <summary>
/// The handheld's own button pictures and a drawing of the device (Glyphs\oem\&lt;device&gt;\), from victor-borges'
/// "Handheld Controller Glyphs" (MIT License), drawn to PNG for SquareDeck. Face buttons, bumpers and triggers look the
/// way they're printed on the device (the ROG Xbox Ally's colored ABXY), and the controller tester shows the device
/// itself. Only the ROG Ally family's sets ship (8.5.81: SquareDeck is made for ASUS's handhelds).
/// </summary>
public static class OemGlyphs
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Glyphs", "oem");

    private static string? _id;
    private static bool _looked;

    /// <summary>The picture set for this handheld ("asus-rog-xbox-ally"...), or null when there isn't one.</summary>
    public static string? DeviceId
    {
        get
        {
            if (_looked) return _id;
            _looked = true;
            try
            {
                string id = Match(Handheld.Brand, Handheld.DisplayName);
                _id = id.Length > 0 && Directory.Exists(Path.Combine(Root, id)) ? id : null;
            }
            catch (Exception) { _id = null; }
            return _id;
        }
    }

    /// <summary>The set that fits a brand and model name, or "" (the ROG Ally and ROG Xbox Ally families).</summary>
    internal static string Match(string brand, string name)
    {
        string n = name.ToUpperInvariant(), b = brand.ToUpperInvariant();
        bool Has(string s) => n.Contains(s, StringComparison.Ordinal);
        if (b == "ASUS" || Has("ROG")) return Has("XBOX") ? "asus-rog-xbox-ally" : "asus-rog-ally";
        return "";
    }

    public static bool Available => DeviceId != null;

    /// <summary>A button's picture ("A", "B", "X", "Y" by Xbox position; "LB", "RT", "View", "Menu", "Guide", "QAM", "L4"...).</summary>
    public static string? File(string button)
    {
        if (DeviceId is not { } id) return null;
        var path = Path.Combine(Root, id, button + ".png");
        return System.IO.File.Exists(path) ? path : null;
    }

    /// <summary>The drawing of the whole device, for the controller tester.</summary>
    public static string? Controller => File("controller");

    public static ImageSource? Load(string? path)
    {
        if (path == null) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception) { return null; }
    }
}
