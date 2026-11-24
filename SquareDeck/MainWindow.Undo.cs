using System.Reflection;
using System.Text.Json;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// Undo after a big change (a new theme, a section put back to its defaults): for a minute, Settings shows "Undo …" at
/// the top of every section, and a notice says so.
/// </summary>
public partial class MainWindow
{
    public sealed record PendingUndo(string What, Action Undo, DateTime Until);

    private PendingUndo? _undo;

    /// <summary>The change that can still be undone (null when there's none, or it's too late).</summary>
    public PendingUndo? Undoable => _undo != null && DateTime.Now < _undo.Until ? _undo : null;

    public void OfferUndo(string what, Action undo)
    {
        _undo = new PendingUndo(what, undo, DateTime.Now.AddMinutes(1));
        ShowToast("UNDO", what, "Changed your mind? Undo it from the top of Settings within a minute.", "", "info", remember: false);
    }

    public void RunUndo()
    {
        if (Undoable is not { } u) return;
        _undo = null;
        try { u.Undo(); }
        catch (Exception ex) { CrashLog.Write("Undo", ex); }
        ShowToast("UNDO", "Undone", u.What, "", "info", remember: false);
    }

    /// <summary>Every setting as it is now, to put back later.</summary>
    public string SnapshotSettings() => JsonSerializer.Serialize(Settings);

    /// <summary>Puts every setting back as it was in <paramref name="snapshot"/>, then redraws and re-applies.</summary>
    public void RestoreSettings(string snapshot)
    {
        var old = JsonSerializer.Deserialize<Settings>(snapshot);
        if (old == null) return;
        foreach (var p in typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                try { p.SetValue(Settings, p.GetValue(old)); } catch (Exception) { }
        Settings.Save();
        Looks.Select(Settings.LookId);
        ApplyAfterReset();
    }

    /// <summary>After settings were put back (a reset or an undo): the look, the home screen and the hardware follow.</summary>
    public void ApplyAfterReset()
    {
        try
        {
            UiSound.Configure(Settings);
            ApplyLook();
            ApplySettings();
            ApplyDynamicBackground();
            ApplyAllyButtons();
            if (AllyControl.IsAlly)
            {
                AllyControl.ApplyLights(force: true);
                AllyControl.ApplyXboxLight(force: true);
                _ = Task.Run(() => { try { AllyControl.ApplyPower(); AllyControl.ApplyController(); } catch (Exception) { } });
            }
            if (TdpControl.Ready) _ = TdpControl.ApplyAsync(quiet: true);
        }
        catch (Exception ex) { CrashLog.Write("Apply after reset", ex); }
    }
}
