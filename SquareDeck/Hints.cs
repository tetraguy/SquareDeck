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
        ("xbox", "Xbox"), ("playstation", "PlayStation"), ("switch", "Switch")
    };

    /// <summary>"xbox", "playstation" or "switch".</summary>
    public static string Style { get; private set; } = "xbox";

    /// <summary>True while the keyboard or mouse was used last, so hints show keys instead of buttons.</summary>
    public static bool Keyboard { get; private set; }

    /// <summary>Raised when the style, the map or the input device changes, so hint bars redraw.</summary>
    public static event Action? Changed;

    public static void SetStyle(string style)
    {
        style = Styles.Any(s => s.Id == style) ? style : "xbox";
        if (style == Style) return;
        Style = style;
        Changed?.Invoke();
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
    public sealed record Item(List<(string Text, bool IsKey)> Keys, string Label);

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

            var keys = new List<(string, bool)>();
            int at = 0;
            foreach (Match m in Token.Matches(keyPart))
            {
                if (m.Index > at) keys.Add((keyPart[at..m.Index].Trim(), false));
                keys.Add((KeyLabel(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value), true));
                at = m.Index + m.Length;
            }
            if (at < keyPart.Length) keys.Add((keyPart[at..].Trim(), false));
            keys.RemoveAll(k => k.Item1.Length == 0);
            items.Add(new Item(keys, label));
        }
        return items;
    }
}
