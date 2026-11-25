using System.IO;
using SquareDeck.Services;
using System.Text.RegularExpressions;

namespace SquareDeck;

/// <summary>
/// Which controller button does what, and how buttons are drawn in the hints at the bottom of the screen.
/// The map is four letters, the buttons for Select, Back, Details and Options in that order ("ABXY" by default).
/// </summary>
public static class Hints
{
    public static string Map { get; set; } = "ABXY";

    public static bool IsValidMap(string? map) =>
        map is { Length: 4 } && map.OrderBy(c => c).SequenceEqual("ABXY");

    public static char Select => Map[0];
    public static char Back => Map[1];
    public static char Details => Map[2];
    public static char Options => Map[3];

    /// <summary>Controller families whose button names can be shown.</summary>
    public static readonly (string Id, string Name)[] Styles =
    {
        ("auto", "Match the controller"), ("xbox", "Xbox"), ("playstation", "PlayStation"), ("switch", "Switch")
    };

    /// <summary>"xbox", "playstation" or "switch" (with "Match the controller", the family of the one connected).</summary>
    public static string Style { get; private set; } = "xbox";

    /// <summary>True when the style follows the connected controller.</summary>
    public static bool Auto { get; private set; }

    /// <summary>With "Match the controller": looks at what's connected again (every few seconds).</summary>
    public static void Refresh()
    {
        if (!Auto) return;
        string now = ControllerFamily.Detect();
        if (now == Style) return;
        Style = now;
        Changed?.Invoke();
    }

    /// <summary>True while the keyboard or mouse was used last, so hints show keys instead of buttons.</summary>
    public static bool Keyboard { get; private set; }

    /// <summary>Raised when the style, the map or the input device changes, so hint bars redraw.</summary>
    public static event Action? Changed;

    public static void SetStyle(string style)
    {
        Auto = style == "auto";
        if (Auto) style = ControllerFamily.Detect();
        style = Styles.Any(s => s.Id == style) ? style : "xbox";
        if (style == Style) return;
        Style = style;
        Changed?.Invoke();
    }

    /// <summary>How the buttons are drawn: plain names on a pill, or one of the built-in picture sets.</summary>
    /// <remarks>"oem" (the handheld's own buttons, as printed on it) is offered only on a handheld that has a set.</remarks>
    public static (string Id, string Name)[] Looks => OemGlyphs.Available
        ? new[] { ("text", "Simple"), ("default", "Controller icons"), ("pixel", "Pixel art"), ("oem", Handheld.DisplayName + " buttons") }
        : new[] { ("text", "Simple"), ("default", "Controller icons"), ("pixel", "Pixel art") };

    /// <summary>"text", "default", "pixel" or "oem" (folders in Glyphs\ next to the program).</summary>
    public static string Look { get; private set; } = "text";

    public static void SetLook(string look)
    {
        look = Looks.Any(l => l.Id == look) ? look : "text";
        if (look == Look) return;
        Look = look;
        Changed?.Invoke();
    }

    public static string GlyphsFolder => Path.Combine(AppContext.BaseDirectory, "Glyphs");

    /// <summary>
    /// The picture for a role ("Select", "Back"...) or shoulder button ("LB"...) in the chosen look, for the device in
    /// use. Null with the simple look, or when the set has no picture for it (the name on a pill is shown instead).
    /// </summary>
    /// <summary>
    /// A controller button's picture in a given look, for previews (A, B, X, Y by Xbox position; LB, RB, LT, RT), with
    /// the controller in use now. Null for "text" (drawn as a plain letter) or when there's no picture.
    /// </summary>
    public static string? PreviewFile(string look, string button)
    {
        if (look == "text") return null;
        if (look == "oem") { if (OemGlyphs.File(button) is { } own) return own; look = "default"; }
        string family = look == "pixel" && !Directory.Exists(Path.Combine(GlyphsFolder, "pixel", Style)) ? "xbox" : Style;
        var path = Path.Combine(GlyphsFolder, look, family, button + ".png");
        if (!File.Exists(path)) path = Path.Combine(GlyphsFolder, look, "xbox", button + ".png");
        return File.Exists(path) ? path : null;
    }

    public static string? GlyphFile(string token)
    {
        if (Look == "text") return null;
        string family, name;
        if (Keyboard)
        {
            // The picture sets have keys for everything the hints use.
            family = Look == "pixel" ? "keyboard" : Theme.CurrentIsDark ? "keyboard-light" : "keyboard-dark";
            name = token switch
            {
                "Select" => "Enter", "Back" => "Esc", "Details" => "KeyX", "Options" => "KeyY",
                "LB" => "PgUp", "RB" => "PgDn", "LT" => "BracketL", "RT" => "BracketR", _ => ""
            };
        }
        else
        {
            // Files are named by the Xbox position (A bottom, B right, X left, Y top), whatever the controller.
            family = Style;
            name = token switch
            {
                "Select" => Select.ToString(), "Back" => Back.ToString(), "Details" => Details.ToString(), "Options" => Options.ToString(),
                "LB" or "RB" or "LT" or "RT" => token, _ => ""
            };
        }
        if (name.Length == 0) return null;
        string look = Look;
        if (look == "oem")
        {
            // The handheld's own pictures while its built-in controller (an Xbox-style pad) is in use; another
            // controller (a PlayStation or Switch one) gets the standard icons.
            if (!Keyboard && family == "xbox" && OemGlyphs.File(name) is { } own) return own;
            look = "default";
            if (Keyboard) family = Theme.CurrentIsDark ? "keyboard-light" : "keyboard-dark";
        }
        var path = Path.Combine(GlyphsFolder, look, family, name + ".png");
        return File.Exists(path) ? path : null;
    }

    public static void SetKeyboard(bool keyboard)
    {
        if (keyboard == Keyboard) return;
        Keyboard = keyboard;
        Changed?.Invoke();
    }

    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>What a face button (named as on an Xbox pad: A, B, X, Y) is called on the chosen controller.</summary>
    public static string ButtonName(char xboxButton) => Style switch
    {
        // Same positions, different names: bottom, right, left, top.
        "playstation" => xboxButton switch { 'A' => "\u2715", 'B' => "\u25CB", 'X' => "\u25A1", 'Y' => "\u25B3", _ => xboxButton.ToString() },
        "switch" => xboxButton switch { 'A' => "B", 'B' => "A", 'X' => "Y", 'Y' => "X", _ => xboxButton.ToString() },
        _ => xboxButton.ToString()
    };

    /// <summary>Shoulder buttons and triggers ("LB", "RB", "LT", "RT") on the chosen controller.</summary>
    public static string ShoulderName(string xboxName) => Style switch
    {
        "playstation" => xboxName switch { "LB" => "L1", "RB" => "R1", "LT" => "L2", "RT" => "R2", _ => xboxName },
        "switch" => xboxName switch { "LB" => "L", "RB" => "R", "LT" => "ZL", "RT" => "ZR", _ => xboxName },
        _ => xboxName
    };

    /// <summary>The label drawn for a role ("Select", "Back", "Details", "Options") or a shoulder button, for the device in use.</summary>
    public static string KeyLabel(string token)
    {
        if (Keyboard)
        {
            return token switch
            {
                "Select" => "Enter", "Back" => "Esc", "Details" => "X", "Options" => "Y",
                "LB" => "PgUp", "RB" => "PgDn", "LT" => "[", "RT" => "]",
                _ => token
            };
        }
        return token switch
        {
            "Select" => ButtonName(Select), "Back" => ButtonName(Back),
            "Details" => ButtonName(Details), "Options" => ButtonName(Options),
            _ => ShoulderName(token)
        };
    }

    private static readonly Regex Token = new(@"\{(Select|Back|Details|Options)\}|\b(LB|RB|LT|RT)\b", RegexOptions.Compiled);

    /// <summary>A hint line as plain text, with the current button or key names filled in.</summary>
    public static string Format(string template) =>
        Token.Replace(template, m => KeyLabel(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value));

    /// <summary>One hint: the pieces of its key part (button pills and plain words) and what it does.</summary>
    /// <remarks>Image is the button's picture in the chosen look (null: draw the name on a pill).</remarks>
    public sealed record Item(List<(string Text, bool IsKey, string? Image)> Keys, string Label);

    /// <summary>
    /// Splits a hint line such as "{Select}  Open      LT / RT  Page" into its hints. Hints are separated by
    /// three or more spaces; the keys and the label within one by two.
    /// </summary>
    public static List<Item> Parse(string template)
    {
        var items = new List<Item>();
        foreach (var chunk in Regex.Split(template.Trim(), @"\s{3,}"))
        {
            if (chunk.Length == 0) continue;
            var parts = Regex.Split(chunk, @"\s{2}");
            string keyPart = parts[0];
            string label = parts.Length > 1 ? string.Join(" ", parts.Skip(1)).Trim() : "";
            if (parts.Length == 1 && !Token.IsMatch(keyPart)) { label = keyPart; keyPart = ""; }

            var keys = new List<(string, bool, string?)>();
            int at = 0;
            foreach (Match m in Token.Matches(keyPart))
            {
                if (m.Index > at) keys.Add((keyPart[at..m.Index].Trim(), false, null));
                string token = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                keys.Add((KeyLabel(token), true, GlyphFile(token)));
                at = m.Index + m.Length;
            }
            if (at < keyPart.Length) keys.Add((keyPart[at..].Trim(), false, null));
            keys.RemoveAll(k => k.Item1.Length == 0);
            items.Add(new Item(keys, label));
        }
        return items;
    }
}
