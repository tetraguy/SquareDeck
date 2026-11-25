using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquareDeck.Services;

/// <summary>
/// What an Advanced style is made of: a folder of artwork in Skins\ (one per color variant when it has some) and
/// the variants to choose from, each with the highlight color that goes with it.
/// </summary>
public sealed record AdvancedSkin(string Folder, (string Id, string Name, string Accent)[] Variants)
{
    public AdvancedSkin(string folder) : this(folder, Array.Empty<(string, string, string)>()) { }

    /// <summary>Choosing the style asks which color first (Spectrum), instead of starting with the last one used.</summary>
    public bool AskColor { get; init; }
}

/// <summary>
/// The artwork of the Advanced style in use, loaded from Skins\(folder)\(variant)\: panel, button, dialog and round
/// button pictures drawn as nine-slice frames, switch and check pictures, a cursor and home button icons, plus
/// skin.json with each picture's slice (in its own pixels), the drawing scale and whether it is pixel art.
/// </summary>
public static class Skins
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Skins");

    public sealed class Loaded
    {
        public string Folder = "";
        public ImageSource? Panel, Button, Dialog, Round, ToggleOn, ToggleOff, CheckOn, CheckOff, Cursor;
        public Thickness PanelSlice, ButtonSlice, DialogSlice;
        public double Scale = 1, CursorScale = 1;
        /// <summary>How rounded the round buttons' picture is, as a share of its width (0 = square); below 0 it's a circle.</summary>
        public double RoundCorner = -1;
        /// <summary>Extra room at the top of dialogs, for artwork with a title bar the text mustn't sit on.</summary>
        public double DialogPad;
        /// <summary>Color for the round-button symbols the kit has no icon for, so they match its own icons (null = keep theirs).</summary>
        public Brush? GlyphColor;
        /// <summary>Text color over the pop-up art's title bar (skin.json "titleText"), e.g. white on Retro Windows' navy bar.</summary>
        public Brush? TitleText;
        public bool Pixel, IconPixel;
        public readonly Dictionary<string, ImageSource> Icons = new();
    }

    /// <summary>The artwork in use, or null with a Basic style.</summary>
    public static Loaded? Current { get; private set; }

    private static readonly Dictionary<string, Loaded?> Cache = new();

    /// <summary>The folder an Advanced style's artwork is read from, with its variant.</summary>
    public static string FolderFor(AdvancedSkin skin, string? variant)
    {
        string v = skin.Variants.Length == 0 ? "" : skin.Variants.Any(x => x.Id == variant) ? variant! : skin.Variants[0].Id;
        return v.Length == 0 ? Path.Combine(Root, skin.Folder) : Path.Combine(Root, skin.Folder, v);
    }

    public static Loaded? Load(string folder)
    {
        if (Cache.TryGetValue(folder, out var known)) return known;
        Loaded? skin = null;
        try
        {
            var json = Path.Combine(folder, "skin.json");
            if (File.Exists(json))
            {
                skin = new Loaded { Folder = folder };
                using var doc = JsonDocument.Parse(File.ReadAllText(json));
                var root = doc.RootElement;
                Thickness Slice(string name) =>
                    root.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() == 4
                        ? new Thickness(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble(), a[3].GetDouble()) : new Thickness(0);
                double Number(string name, double fallback) => root.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : fallback;
                bool Flag(string name) => root.TryGetProperty(name, out var f) && f.ValueKind == JsonValueKind.True;
                skin.PanelSlice = Slice("panel");
                skin.ButtonSlice = Slice("button");
                skin.DialogSlice = Slice("dialog");
                skin.Scale = Number("scale", 1);
                skin.Pixel = Flag("pixel");
                skin.CursorScale = Number("cursorScale", skin.Pixel ? skin.Scale : 1);
                skin.IconPixel = Flag("iconPixel");
                skin.RoundCorner = Number("roundCorner", -1);
                skin.DialogPad = Number("dialogPad", 0) * skin.Scale / 2;
                if (root.TryGetProperty("titleText", out var tt) && tt.ValueKind == JsonValueKind.String)
                {
                    try { var brush = (Brush)new BrushConverter().ConvertFromString(tt.GetString()!)!; brush.Freeze(); skin.TitleText = brush; }
                    catch (Exception) { }
                }
                if (root.TryGetProperty("glyphColor", out var gc) && gc.ValueKind == JsonValueKind.String)
                {
                    try { var brush = (Brush)new BrushConverter().ConvertFromString(gc.GetString()!)!; brush.Freeze(); skin.GlyphColor = brush; }
                    catch (Exception) { }
                }
                ImageSource? Picture(string name)
                {
                    var path = Path.Combine(folder, name + ".png");
                    if (!File.Exists(path)) return null;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                skin.Panel = Picture("panel");
                skin.Button = Picture("button");
                skin.Dialog = Picture("dialog");
                skin.Round = Picture("round");
                skin.ToggleOn = Picture("toggle_on");
                skin.ToggleOff = Picture("toggle_off");
                skin.CheckOn = Picture("check_on");
                skin.CheckOff = Picture("check_off");
                skin.Cursor = Picture("cursor");
                var icons = Path.Combine(folder, "icons");
                if (Directory.Exists(icons))
                    foreach (var file in Directory.EnumerateFiles(icons, "*.png"))
                        if (Picture(Path.Combine("icons", Path.GetFileNameWithoutExtension(file))) is { } icon)
                            skin.Icons[Path.GetFileNameWithoutExtension(file)] = icon;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Advanced style artwork", ex);
            skin = null;
        }
        Cache[folder] = skin;
        return skin;
    }

    /// <summary>
    /// Puts the style's artwork into the shared resources the controls' templates read (or clears it for a Basic
    /// style), and makes the fills it replaces see-through.
    /// </summary>
    public static void Apply(UiStyle style, ResourceDictionary res, Brush circle)
    {
        var skin = style.Skin is { } advanced ? Load(FolderFor(advanced, style.SkinVariant)) : null;
        // A style of your own can borrow another style's home button icons on their own (My own style).
        if (skin == null && !string.IsNullOrEmpty(style.IconSkin) && Load(style.IconSkin) is { } donor) skin = IconsOnly(donor);
        Current = skin;
        res["SkinPanel"] = skin?.Panel;
        res["SkinPanelSlice"] = skin?.PanelSlice ?? new Thickness(0);
        res["SkinButton"] = skin?.Button;
        res["SkinButtonSlice"] = skin?.ButtonSlice ?? new Thickness(0);
        res["SkinDialog"] = skin?.Dialog;
        res["SkinDialogSlice"] = skin?.DialogSlice ?? new Thickness(0);
        res["SkinRound"] = skin?.Round;
        res["SkinScale"] = skin?.Scale ?? 1.0;
        res["SkinPixel"] = skin?.Pixel ?? false;
        res["SkinRoundScaling"] = skin?.Pixel == true ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;
        // Where a picture is drawn, the plain fill behind it goes.
        res["SkinRowFill"] = skin?.Panel != null ? Brushes.Transparent : circle;
        res["SkinButtonFill"] = skin?.Button != null ? Brushes.Transparent : circle;
        res["SkinDialogFill"] = skin?.Dialog != null ? Brushes.Transparent : res["DialogBrush"];
        ApplyCursor(skin);
    }

    private static readonly Dictionary<string, Loaded> IconCache = new();

    /// <summary>Just a kit's icons (and their color for the symbols it has none for), without its panels, buttons or pointer.</summary>
    private static Loaded IconsOnly(Loaded donor)
    {
        if (IconCache.TryGetValue(donor.Folder, out var known)) return known;
        var icons = new Loaded { Folder = donor.Folder + "#icons", IconPixel = donor.IconPixel, GlyphColor = donor.GlyphColor };
        foreach (var kv in donor.Icons) icons.Icons[kv.Key] = kv.Value;
        IconCache[donor.Folder] = icons;
        return icons;
    }

    private static Cursor? _cursor;
    private static string? _cursorFolder;

    /// <summary>The style's own mouse pointer, while it's in use.</summary>
    private static void ApplyCursor(Loaded? skin)
    {
        try
        {
            if (skin?.Cursor is not BitmapSource picture) { Mouse.OverrideCursor = null; _cursorFolder = null; return; }
            if (_cursorFolder != skin.Folder) { _cursor = MakeCursor(picture, skin.Pixel ? Math.Max(1, skin.CursorScale) : 1); _cursorFolder = skin.Folder; }
            Mouse.OverrideCursor = _cursor;
        }
        catch (Exception) { Mouse.OverrideCursor = null; }
    }

    /// <summary>A Windows cursor made from a picture (a .cur holding the PNG), with its hot spot in the top left corner.</summary>
    private static Cursor MakeCursor(BitmapSource picture, double scale)
    {
        BitmapSource image = picture;
        int size = (int)Math.Round(Math.Max(picture.PixelWidth, picture.PixelHeight) * scale);
        size = Math.Clamp(size, 16, 64);
        double k = (double)size / Math.Max(picture.PixelWidth, picture.PixelHeight);
        if (Math.Abs(k - 1) > 0.01)
        {
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            using (var dc = visual.RenderOpen()) dc.DrawImage(picture, new Rect(0, 0, picture.PixelWidth * k, picture.PixelHeight * k));
            var target = new RenderTargetBitmap((int)Math.Ceiling(picture.PixelWidth * k), (int)Math.Ceiling(picture.PixelHeight * k), 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            image = target;
        }
        var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(png);
        byte[] data = png.ToArray();
        var cur = new MemoryStream();
        using (var w = new BinaryWriter(cur, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((short)0); w.Write((short)2); w.Write((short)1);                     // header: a cursor with one image
            w.Write((byte)Math.Min(255, image.PixelWidth)); w.Write((byte)Math.Min(255, image.PixelHeight));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)1);                                        // hot spot (x, y)
            w.Write(data.Length); w.Write(22);                                           // size and offset of the PNG
            w.Write(data);
        }
        cur.Position = 0;
        return new Cursor(cur);
    }
}
