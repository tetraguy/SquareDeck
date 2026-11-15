using System.Text;

namespace SquareDeck.Services;

/// <summary>A node in a Valve KeyValues (.vdf / .acf) text file.</summary>
public sealed class VdfNode
{
    public string? Value { get; set; }
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public VdfNode? this[string key] => Children.TryGetValue(key, out var n) ? n : null;

    /// <summary>Walks a path of keys; null if any step is missing.</summary>
    public VdfNode? Path(params string[] keys)
    {
        VdfNode? node = this;
        foreach (var k in keys)
        {
            node = node?[k];
            if (node == null) return null;
        }
        return node;
    }
}

public static class Vdf
{
    public static VdfNode Parse(string text)
    {
        int pos = 0;
        var root = new VdfNode();
        ParseObject(text, ref pos, root, topLevel: true);
        return root;
    }

    private static void ParseObject(string s, ref int pos, VdfNode into, bool topLevel)
    {
        while (true)
        {
            var key = NextToken(s, ref pos, out var keyQuoted);
            if (key == null) return;                       // end of file
            if (key == "}" && !keyQuoted) { if (topLevel) continue; return; }
            if (key == "{" && !keyQuoted) continue;        // malformed; skip

            var value = NextToken(s, ref pos, out var valueQuoted);
            if (value == null) return;

            var child = new VdfNode();
            if (value == "{" && !valueQuoted) ParseObject(s, ref pos, child, topLevel: false);
            else child.Value = value;
            into.Children[key] = child;
        }
    }

    private static string? NextToken(string s, ref int pos, out bool quoted)
    {
        quoted = false;
        while (pos < s.Length)
        {
            char c = s[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
            {
                while (pos < s.Length && s[pos] != '\n') pos++;
                continue;
            }
            break;
        }
        if (pos >= s.Length) return null;

        if (s[pos] == '{' || s[pos] == '}') return s[pos++].ToString();

        var sb = new StringBuilder();
        if (s[pos] == '"')
        {
            quoted = true;
            pos++;
            while (pos < s.Length && s[pos] != '"')
            {
                if (s[pos] == '\\' && pos + 1 < s.Length)
                {
                    pos++;
                    sb.Append(s[pos] switch { 'n' => '\n', 't' => '\t', var other => other });
                }
                else sb.Append(s[pos]);
                pos++;
            }
            pos++; // closing quote
            return sb.ToString();
        }

        while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '{' && s[pos] != '}' && s[pos] != '"')
            sb.Append(s[pos++]);
        return sb.ToString();
    }
}
