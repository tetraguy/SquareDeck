using System.Windows;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>What's new since the version you had: shown once after an update (it can be turned off here).</summary>
public sealed class WhatsNewScreen : ListScreen
{
    private readonly string _notes;
    private readonly Version _from;

    public WhatsNewScreen(Version from, Version to, string notes) : base("What's new in SquareDeck " + to)
    {
        _from = from;
        _notes = notes;
    }

    protected override void Build()
    {
        var s = Host.Settings;
        AddNote($"Changes since version {_from}.");
        AddRow("Got it", null, "", click: () => Host.Pop(this));
        AddRow("Show this after updates", s.WhatsNewAfterUpdate ? "On" : "Off", "", click: () =>
        {
            s.WhatsNewAfterUpdate = !s.WhatsNewAfterUpdate;
            s.Save();
            Rebuild();
        }, subtitle: "Also in Settings > Updates and data");
        AddRow("All changelogs", null, "", click: () => Host.Push(new ChangelogScreen()));
        foreach (var paragraph in Updater.PlainNotes(_notes).Split('\n'))
        {
            if (paragraph.Trim().Length == 0) continue;
            bool heading = System.Text.RegularExpressions.Regex.IsMatch(paragraph.Trim(), @"^\d+\.\d+");
            var block = Ui.Text(paragraph, heading ? 22 : 17, bold: heading);
            block.TextWrapping = TextWrapping.Wrap;
            block.TextTrimming = TextTrimming.None;
            block.Margin = new Thickness(4, heading ? 16 : 4, 12, 4);
            Rows.Children.Add(block);
        }
    }
}
