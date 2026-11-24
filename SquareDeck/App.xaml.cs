using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace SquareDeck;

public partial class App : Application
{
    /// <summary>Started a second time: the copy already running brings itself to the front instead.</summary>
    public static readonly int ShowExistingMessage = RegisterWindowMessage("SquareDeck.ShowExisting");

    /// <summary>The Game Bar widget pressed "Start SquareDeck" while SquareDeck was already open.</summary>
    public static readonly int WidgetWakeMessage = RegisterWindowMessage("SquareDeck.WidgetWake");

    /// <summary>Passed when SquareDeck restarts itself, so the new copy waits for the old one to close.</summary>
    public const string RestartArgument = "--restart";

    private Mutex? _single;
    private bool _owns;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    public App()
    {
        // Touch: Windows' modern pointer input instead of the old tablet stack, for quicker, smoother touch scrolling.
        AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true);
    }

    /// <summary>Closes other SquareDeck processes older than this one; true if any were closed.</summary>
    private static bool ReplaceOlderCopies()
    {
        bool replaced = false;
        int me = Environment.ProcessId;
        var mine = SquareDeck.Services.Updater.Current;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("SquareDeck"))
        {
            try
            {
                if (p.Id == me || p.MainModule?.FileName is not { } path) continue;
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                if (new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart) >= mine) continue;
                p.Kill();
                p.WaitForExit(5000);
                replaced = true;
            }
            catch (Exception) { } // another user's, or already gone
            finally { p.Dispose(); }
        }
        return replaced;
    }

    /// <summary>
    /// Where Windows asked SquareDeck to go when it started it: "library" (Game Bar's Library button, when SquareDeck is
    /// Xbox mode's home app), "home" (its Home button, or SquareDeck started again as the home app), or "" (nothing).
    /// </summary>
    public static string StartRequest { get; private set; } = "";

    /// <summary>
    /// Reads how SquareDeck was started: its command line, and (when Windows starts it as Xbox mode's home app) the
    /// activation Windows hands the package, each written to launches.log so what Game Bar sends can be checked.
    /// </summary>
    private static string ReadRequest(string[] args)
    {
        var seen = new List<string>(args);
        try
        {
            // Started as the packaged home app: Windows' own description of the start (a launch with arguments, or a link).
            var activated = global::Windows.ApplicationModel.AppInstance.GetActivatedEventArgs();
            if (activated != null)
            {
                seen.Add("kind=" + activated.Kind);
                if (activated is global::Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch && !string.IsNullOrEmpty(launch.Arguments)) seen.Add("args=" + launch.Arguments);
                if (activated is global::Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs link && link.Uri != null) seen.Add("uri=" + link.Uri);
            }
        }
        catch (Exception) { } // not started as a package
        string all = string.Join(" ", seen);
        try
        {
            string log = System.IO.Path.Combine(SquareDeck.Services.Settings.DataDir, "launches.log");
            System.IO.Directory.CreateDirectory(SquareDeck.Services.Settings.DataDir);
            var lines = System.IO.File.Exists(log) ? System.IO.File.ReadAllLines(log).TakeLast(40).ToList() : new List<string>();
            lines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {(all.Length == 0 ? "(no arguments)" : all)}");
            System.IO.File.WriteAllLines(log, lines);
        }
        catch (Exception) { }
        if (all.Contains("library", StringComparison.OrdinalIgnoreCase)) return "library";
        if (all.Contains("background", StringComparison.OrdinalIgnoreCase) || all.Contains(RestartArgument)) return "";
        return SquareDeck.Services.HomeApp.IsHomeApp ? "home" : "";
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartRequest = ReadRequest(e.Args);
        // Only one SquareDeck at a time (per Windows user).
        _single = new Mutex(true, @"Local\SquareDeck.SingleInstance", out bool created);
        _owns = created;
        if (!created)
        {
            bool restarting = e.Args.Contains(RestartArgument);
            try { _owns = _single.WaitOne(restarting ? TimeSpan.FromSeconds(15) : TimeSpan.FromMilliseconds(800)); }
            catch (AbandonedMutexException) { _owns = true; } // the earlier copy crashed: take over
        }
        // A newer SquareDeck takes over from an older copy that's still open (after a rebuild or an update) instead of
        // handing over to it, so the newest build is always the one running.
        if (!_owns && ReplaceOlderCopies())
        {
            try { _owns = _single.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { _owns = true; }
        }
        // Started by the Game Bar widget (squaredeck:background): start out of the way, so the game stays in front.
        bool background = e.Args.Any(a => a.StartsWith("squaredeck:", StringComparison.OrdinalIgnoreCase) && a.Contains("background", StringComparison.OrdinalIgnoreCase));
        if (!_owns && background)
        {
            // Already running: it reopens its connection for the widget (and turns it on), and stays where it is.
            PostMessage((IntPtr)0xFFFF, WidgetWakeMessage, IntPtr.Zero, IntPtr.Zero);
            Shutdown();
            return;
        }
        if (!_owns)
        {
            // Let the running copy take the foreground, ask it to come forward, and leave.
            const int AnyProcess = -1;
            AllowSetForegroundWindow(AnyProcess);
            // 1: go Home, 2: open the Library (Game Bar's buttons in Xbox mode), 0: just come forward.
            PostMessage((IntPtr)0xFFFF, ShowExistingMessage, (IntPtr)(StartRequest == "library" ? 2 : StartRequest == "home" ? 1 : 0), IntPtr.Zero);
            Shutdown();
            return;
        }

        // Anything that goes wrong is written to crash.log. Errors on the interface thread are caught, so one
        // broken page or menu doesn't take the whole launcher down.
        DispatcherUnhandledException += (_, args) =>
        {
            SquareDeck.Services.CrashLog.Write("Interface", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) SquareDeck.Services.CrashLog.Write("Background", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            SquareDeck.Services.CrashLog.Write("Task", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        // The squaredeck: link (the Game Bar widget's "Start SquareDeck"), registered at every start, so it points at
        // this copy even after SquareDeck moves, and whether or not the widget connection is on.
        SquareDeck.Services.WidgetBridge.RegisterProtocol();
        SquareDeck.Services.Perf.CapFrameRate(SquareDeck.Services.Settings.Load()); // must happen before any animation exists
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        // Minimized straight after showing (WPF refuses to show a maximized window without activating it).
        if (background)
            window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => window.WindowState = WindowState.Minimized));
        // Started by Game Bar's Library button: the Library opens once the launcher has settled.
        else if (StartRequest == "library") window.GameBarGo("library", fresh: true);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_owns)
        {
            try { _single?.ReleaseMutex(); } catch (Exception) { }
        }
        _single?.Dispose();
        base.OnExit(e);
    }
}
