using System.Reflection;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>
/// Which settings a Settings section changes (SettingsSections.Map.cs, generated from SettingsScreens.cs: the properties
/// its rows assign, per part and group), which of them differ from the default, and putting a section back to the
/// defaults with a way to undo it. Keys, tokens and things you made (styles, favorites, pins) are never in the map.
/// To regenerate the map after adding settings rows, run tools/gen-settings-sections.py from the project folder.
/// </summary>
public static partial class SettingsSections
{
    private static readonly Settings Defaults = new();

    private static PropertyInfo? Prop(string name) => typeof(Settings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

    private static string Json(object? value)
    {
        try { return JsonSerializer.Serialize(value); }
        catch (Exception) { return value?.ToString() ?? "null"; }
    }

    /// <summary>The settings a section's parts and groups change (null groups: all of that part).</summary>
    public static List<string> Properties(IEnumerable<(string Part, string[]? Groups)> parts)
    {
        var names = new HashSet<string>();
        foreach (var (part, groups) in parts)
        {
            if (!Map.TryGetValue(part, out var byGroup)) continue;
            foreach (var (group, props) in byGroup)
                if (groups == null || groups.Contains(group) || group.Length == 0)
                    foreach (var p in props) names.Add(p);
        }
        return names.Where(n => Prop(n) is { CanWrite: true }).OrderBy(n => n).ToList();
    }

    /// <summary>Those of <paramref name="names"/> that differ from a fresh install's.</summary>
    public static List<string> Changed(Settings s, IEnumerable<string> names) =>
        names.Where(n => Prop(n) is { } p && Json(p.GetValue(s)) != Json(p.GetValue(Defaults))).ToList();

    /// <summary>What a reset replaced, to put back with Undo.</summary>
    public sealed class Snapshot
    {
        internal readonly Dictionary<string, string> Values = new();
        public int Count => Values.Count;
    }

    /// <summary>Puts <paramref name="names"/> back to the defaults (deep copies, so lists aren't shared) and returns what they were.</summary>
    public static Snapshot Reset(Settings s, IEnumerable<string> names)
    {
        var snap = new Snapshot();
        foreach (var n in names)
        {
            if (Prop(n) is not { } p) continue;
            snap.Values[n] = Json(p.GetValue(s));
            try { p.SetValue(s, JsonSerializer.Deserialize(Json(p.GetValue(Defaults)), p.PropertyType)); }
            catch (Exception) { }
        }
        s.Save();
        return snap;
    }

    /// <summary>Puts back what a reset replaced.</summary>
    public static void Undo(Settings s, Snapshot snap)
    {
        foreach (var (n, json) in snap.Values)
        {
            if (Prop(n) is not { } p) continue;
            try { p.SetValue(s, JsonSerializer.Deserialize(json, p.PropertyType)); }
            catch (Exception) { }
        }
        s.Save();
    }
}
