using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquareDeck.Services;

/// <summary>
/// The handheld's own button pictures and a drawing of the device (Glyphs\oem\&lt;device&gt;\), from victor-borges'
/// "Handheld Controller Glyphs" (MIT License), drawn to PNG for SquareDeck. Face buttons, bumpers and triggers look the
/// way they're printed on the device (the ROG Xbox Ally's colored ABXY, MSI's and Lenovo's own...), and the controller
/// tester shows the device itself.
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

    /// <summary>The set that fits a brand and model name, or "" (the sets cover the families, not every variant).</summary>
    internal static string Match(string brand, string name)
    {
        string n = name.ToUpperInvariant(), b = brand.ToUpperInvariant();
        bool Has(string s) => n.Contains(s, StringComparison.Ordinal);
        if (b == "ASUS" || Has("ROG")) return Has("XBOX") ? "asus-rog-xbox-ally" : "asus-rog-ally";
        if (b == "LENOVO" || Has("LEGION"))
            return Has("GO S") ? "lenovo-legion-go-s" : Has("GO 2") ? "lenovo-legion-go-2" : Has("LEGION GO") ? "lenovo-legion-go" : "";
        if (b == "MSI" || Has("CLAW")) return Has("A8") ? "msi-claw-a8" : Has("CLAW") ? "msi-claw" : "";
        if (b == "AYANEO" || Has("AYANEO"))
        {
            if (Has("AYANEO 3")) return "ayaneo-3";
            if (Has("AYANEO 2") && !Has("2021") || Has("GEEK")) return "ayaneo-2";
            if (Has("AIR")) return "ayaneo-air";
            if (Has("KUN")) return "ayaneo-kun";
            if (Has("SLIDE")) return "ayaneo-slide";
            if (Has("NEXT")) return "ayaneo-next";
            if (Has("NEO") || Has("2021")) return "ayaneo-neo";
            return "";
        }
        if (b == "GPD")
            return Has("MAX 2") ? "gpd-win-max-2" : Has("MINI") ? "gpd-win-mini" : Has("WIN 4") ? "gpd-win-4" : Has("WIN 3") ? "gpd-win-3" : "";
        if (b == "ONEXPLAYER" || Has("ONEX"))
            return Has("F1") ? "onexplayer-f1" : Has("G1") ? "onexplayer-g1" : Has("X1") ? "onexplayer-x1" : Has("ONEXPLAYER 2") ? "onexplayer-2" : "";
        if (b == "AOKZOE" || Has("AOKZOE")) return "aokzoe-a1";
        if (b == "AYN" || Has("LOKI")) return "ayn-loki";
        if (b == "ZOTAC" || Has("ZOTAC")) return "zotac-zone";
        if (b == "ANBERNIC" || Has("WIN600")) return "anbernic-win600";
        if (b == "TECNO" || Has("POCKET GO")) return "tecno-pocket-go";
        if (b == "TECTOY" || Has("ZEENIX")) return "tectoy-zeenix";
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
