namespace SquareDeck.Services;

/// <summary>
/// Keys for online services that are built into this copy of SquareDeck but kept out of the source code: they live in
/// ApiKeys.Local.cs next to this file, which git ignores (see .gitignore), so a public copy of the code never carries
/// them. Without that file the build still works; features that need a key then ask for one in their settings.
///
/// ApiKeys.Local.cs looks like this:
///     namespace SquareDeck.Services;
///     public static partial class ApiKeys
///     {
///         static partial void LoadLocal(ref string gemini) { gemini = "your key"; }
///     }
/// </summary>
public static partial class ApiKeys
{
    static ApiKeys()
    {
        string gemini = "";
        LoadLocal(ref gemini);
        Gemini = gemini;
    }

    static partial void LoadLocal(ref string gemini);

    /// <summary>Google Gemini, for Game Genie (Experimental). Empty when this copy has none built in.</summary>
    public static string Gemini { get; }
}
