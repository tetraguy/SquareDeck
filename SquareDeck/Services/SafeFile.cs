using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace SquareDeck.Services;

/// <summary>
/// Saving SquareDeck's own files without risking them: the new text goes to a temporary file beside the real one,
/// which then takes its place in one step. If the battery runs out or the app is closed mid-save, the old file is
/// still whole (a half-written settings.json used to mean every setting back to the default).
/// </summary>
public static class SafeFile
{
    /// <summary>Indented JSON, made once (a new options object each save made every save slower).</summary>
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // One file written at a time (the background writer and a save made straight away never share the temporary file).
    private static readonly object WriteLock = new();

    public static void WriteAllText(string path, string text)
    {
        lock (WriteLock)
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            string temp = path + ".saving";
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
        }
    }

    // ---------------------------------------------------------------- saving soon (9.0.1)
    //
    // Settings, the library, play time and achievements used to be turned into JSON and written to disk on the
    // interface's thread every time anything changed: a run of changes (a slider, a style with its colors, sounds and
    // music) wrote the same file many times, and a slow disk or a virus scan held up the button press each time.
    // Now a save on the interface's thread is gathered for a moment: the JSON is made once, on the interface's thread
    // (where those objects are changed), and the file is written on a background thread. Everything waiting is written
    // when SquareDeck closes, and before a backup reads the files.

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Func<string>> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static DispatcherTimer? _timer;
    private static DateTime _firstPending;
    private static Task _writes = Task.CompletedTask;
    private static bool _hooked;

    private static readonly TimeSpan Gather = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MostBehind = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Saves <paramref name="path"/> with the text <paramref name="make"/> gives, shortly. On the interface's thread the
    /// saves of the next moment are gathered into one; anywhere else (or while closing) it's written straight away.
    /// </summary>
    public static void WriteSoon(string path, Func<string> make)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || !dispatcher.CheckAccess())
        {
            lock (Gate) Pending.Remove(path); // this save is newer than any waiting one
            WriteAllText(path, make());
            return;
        }
        bool overdue;
        lock (Gate)
        {
            if (Pending.Count == 0) _firstPending = DateTime.UtcNow;
            Pending[path] = make;
            overdue = DateTime.UtcNow - _firstPending > MostBehind; // a steady stream of changes still gets written
        }
        Hook(dispatcher);
        if (_timer == null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = Gather };
            _timer.Tick += (_, _) => WriteDue();
        }
        if (overdue) { WriteDue(); return; }
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Makes the waiting saves' JSON (on the interface's thread) and writes them on a background thread.</summary>
    private static void WriteDue()
    {
        _timer?.Stop();
        var texts = MakeWaiting();
        if (texts.Count == 0) return;
        lock (Gate)
        {
            _writes = _writes.ContinueWith(_ =>
            {
                foreach (var (path, text) in texts)
                {
                    try { WriteAllText(path, text); } catch (Exception) { }
                }
            }, TaskScheduler.Default);
        }
    }

    private static List<(string Path, string Text)> MakeWaiting()
    {
        KeyValuePair<string, Func<string>>[] waiting;
        lock (Gate)
        {
            waiting = Pending.ToArray();
            Pending.Clear();
        }
        var texts = new List<(string, string)>(waiting.Length);
        foreach (var w in waiting)
        {
            try { texts.Add((w.Key, w.Value())); } catch (Exception) { }
        }
        return texts;
    }

    /// <summary>Writes every waiting save now and waits until all are on disk. Safe from any thread.</summary>
    public static void Flush()
    {
        _timer?.Stop();
        List<(string Path, string Text)> texts;
        var dispatcher = Application.Current?.Dispatcher;
        bool any;
        lock (Gate) any = Pending.Count > 0;
        if (!any) texts = new();
        else if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            // The JSON is made where the objects are changed; if the interface's thread doesn't answer, here.
            texts = new();
            try
            {
                texts = dispatcher.Invoke(MakeWaiting, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2)) ?? new();
            }
            catch (Exception) { }
            if (texts.Count == 0) texts = MakeWaiting();
        }
        else texts = MakeWaiting();
        Task writes;
        lock (Gate) writes = _writes;
        try { writes.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        foreach (var (path, text) in texts)
        {
            try { WriteAllText(path, text); } catch (Exception) { }
        }
    }

    /// <summary>Forgets the waiting saves (a reset or a restored backup is about to replace the files on disk) and
    /// waits for any write already under way.</summary>
    public static void Discard()
    {
        _timer?.Stop();
        Task writes;
        lock (Gate)
        {
            Pending.Clear();
            writes = _writes;
        }
        try { writes.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
    }

    private static void Hook(Dispatcher dispatcher)
    {
        if (_hooked) return;
        _hooked = true;
        dispatcher.ShutdownStarted += (_, _) => Flush();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }
}
