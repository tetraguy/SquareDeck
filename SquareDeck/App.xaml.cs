using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace SquareDeck;

public partial class App : Application
{
    /// <summary>Started a second time: the copy already running brings itself to the front instead.</summary>
    public static readonly int ShowExistingMessage = RegisterWindowMessage("SquareDeck.ShowExisting");

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

    protected override void OnStartup(StartupEventArgs e)
    {
        // Only one SquareDeck at a time (per Windows user).
        _single = new Mutex(true, @"Local\SquareDeck.SingleInstance", out bool created);
        _owns = created;
        if (!created)
        {
            bool restarting = e.Args.Contains(RestartArgument);
            try { _owns = _single.WaitOne(restarting ? TimeSpan.FromSeconds(15) : TimeSpan.FromMilliseconds(800)); }
            catch (AbandonedMutexException) { _owns = true; } // the earlier copy crashed: take over
        }
        if (!_owns)
        {
            // Let the running copy take the foreground, ask it to come forward, and leave.
            const int AnyProcess = -1;
            AllowSetForegroundWindow(AnyProcess);
            PostMessage((IntPtr)0xFFFF, ShowExistingMessage, IntPtr.Zero, IntPtr.Zero);
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
        SquareDeck.Services.Perf.CapFrameRate(SquareDeck.Services.Settings.Load()); // must happen before any animation exists
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
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
