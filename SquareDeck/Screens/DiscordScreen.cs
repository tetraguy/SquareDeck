using System.IO;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Discord (Experimental): your Discord status from SquareDeck (Rich Presence) and quick ways into Discord. Discord
/// doesn't let other apps read your friends list or messages, so those stay in Discord itself, one press away.
/// </summary>
public sealed class DiscordScreen : ListScreen
{
    public DiscordScreen() : base("Discord") { }

    public override void OnResumed() => Rebuild();

    private static bool AppInstalled =>
        Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord")) ||
        Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscordPTB")) ||
        Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscordCanary"));

    protected override void Build()
    {
        bool app = AppInstalled;
        AddHeader("Open Discord");
        if (app)
        {
            AddRow("Open the Discord app", null, "", click: () => Open("discord://-/channels/@me"), subtitle: "Your friends and direct messages");
            AddRow("Friends", null, "", click: () => Open("discord://-/channels/@me"));
            AddRow("Join a server with an invite…", null, "", click: () => _ = InviteAsync());
        }
        AddRow("Discord in SquareDeck's browser", null, "", click: () => Host.Push(BrowserScreen.Open(Host, "https://discord.com/app")),
            subtitle: app ? "Without leaving SquareDeck" : "The Discord app isn't installed, so it opens on the web");
        if (!app) AddRow("Get the Discord app", null, "", click: () => Host.Push(BrowserScreen.Open(Host, "https://discord.com/download")));

        AddHeader("Your Discord status");
        bool presence = Labs.Get("discord.presence", "1") == "1";
        string appId = Labs.Get("discord.app", "");
        AddRow("Show what I'm playing", OnOff(presence && appId.Length > 0), "", click: () =>
        {
            if (appId.Length == 0) { _ = SetAppIdAsync(); return; }
            Labs.Put("discord.presence", presence ? "0" : "1");
            Labs.Set("discord", true); // re-applies it
            Rebuild();
        }, subtitle: "\"Playing Hades on SquareDeck\", with the game's picture and how long you've played");
        AddRow("Also when I'm browsing", OnOff(Labs.Get("discord.browsing", "1") == "1"), "", click: () =>
        {
            Labs.Put("discord.browsing", Labs.Get("discord.browsing", "1") == "1" ? "0" : "1");
            _ = Host.DiscordTickAsync();
            Rebuild();
        }, subtitle: "\"Browsing the library on SquareDeck\" between games");
        AddRow("Discord application ID", appId.Length > 0 ? appId : "Not set", "", click: () => _ = SetAppIdAsync(),
            subtitle: "Needed by Discord for any app to show a status. The application's name is what friends see");
        if (DiscordPresence.Problem is { } problem && presence) AddNote(problem);
        else if (DiscordPresence.Connected) AddNote("Connected to the Discord app: your status is showing.");
        AddNote("To get an application ID: open discord.com/developers/applications, choose New Application, name it SquareDeck " +
                "(friends see \"Playing SquareDeck\"), and copy its Application ID. The Discord app must be running on this PC for your status to show. " +
                "Discord doesn't let other apps read your friends list or messages, so those open in Discord.");
        AddRow("Open Discord's Developer Portal", null, "", click: () => Host.Push(BrowserScreen.Open(Host, "https://discord.com/developers/applications")));
    }

    private static string OnOff(bool on) => on ? "On" : "Off";

    private void Open(string link)
    {
        try { ProcessUtil.Open(link); }
        catch (Exception) { Host.Push(BrowserScreen.Open(Host, "https://discord.com/app")); }
    }

    private async Task InviteAsync()
    {
        var code = await Host.PromptAsync("Join a server", "The invite link or code, like discord.gg/abc123.", "", "Open");
        if (string.IsNullOrWhiteSpace(code)) return;
        string c = code.Trim().TrimEnd('/');
        c = c[(c.LastIndexOf('/') + 1)..];
        Open("discord://-/invite/" + Uri.EscapeDataString(c));
    }

    private async Task SetAppIdAsync()
    {
        var id = await Host.PromptAsync("Discord application ID", "A long number from Discord's Developer Portal (Applications > your app > Application ID).",
            Labs.Get("discord.app", ""), "Save");
        if (id == null) return;
        id = new string(id.Where(char.IsDigit).ToArray());
        Labs.Put("discord.app", id);
        if (id.Length > 0) Labs.Put("discord.presence", "1");
        Labs.Set("discord", true);
        await Task.Delay(1500);
        Rebuild();
    }
}
