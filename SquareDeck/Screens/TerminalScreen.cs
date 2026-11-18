using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// A command prompt or PowerShell inside the launcher: the output scrolls above, a command line sits at the bottom.
/// The shell runs as a hidden process with its input and output connected here, so ordinary commands work (dir,
/// ipconfig, ping, Get-Process…), but full-screen programs that draw their own screen (edit, vim, some installers)
/// don't. Nothing runs as administrator.
///
/// Controls: A (or X) types a command, Y has a list of handy commands, LT / RT scroll the output, Start stops the
/// command that's running (by restarting the shell), View clears the screen, and B leaves (the shell closes).
/// With a keyboard: Enter runs, Up / Down go through earlier commands, Ctrl+C stops.
/// </summary>
public sealed class TerminalScreen : Screen
{
    private const string Marker = "__SQUAREDECK_PWD__:";
    private const int MaxChars = 200_000; // older output is trimmed so a chatty command can't fill the memory

    private readonly bool _powerShell;
    private readonly TextBox _output = new();
    private readonly TextBox _input = new();
    private readonly TextBlock _prompt = new();
    private readonly TextBlock _state = Ui.Text("", 14, "SubtleBrush");
    private readonly List<string> _history = new();
    private readonly StringBuilder _pending = new();
    private readonly DispatcherTimer _flush = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly object _lock = new();
    private Process? _shell;
    private string _cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private int _historyIndex;
    private bool _busy, _closed;

    public TerminalScreen(bool powerShell)
    {
        _powerShell = powerShell;
        var mono = new FontFamily("Cascadia Mono, Consolas, Courier New");
        var green = new SolidColorBrush(Color.FromRgb(0x7C, 0xE3, 0x8B));

        _output.IsReadOnly = true;
        _output.FontFamily = mono;
        _output.FontSize = 15;
        _output.TextWrapping = TextWrapping.Wrap;
        _output.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _output.Background = Brushes.Transparent;
        _output.BorderThickness = new Thickness(0);
        _output.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
        _output.Padding = new Thickness(18, 14, 18, 8);
        _output.Focusable = false;
        _output.IsTabStop = false;

        _prompt.FontFamily = mono;
        _prompt.FontSize = 15;
        _prompt.Foreground = green;
        _prompt.VerticalAlignment = VerticalAlignment.Center;
        _prompt.Margin = new Thickness(18, 0, 8, 0);
        _input.FontFamily = mono;
        _input.FontSize = 15;
        _input.Background = Brushes.Transparent;
        _input.BorderThickness = new Thickness(0);
        _input.Foreground = Brushes.White;
        _input.CaretBrush = green;
        _input.VerticalContentAlignment = VerticalAlignment.Center;
        _input.PreviewKeyDown += Input_KeyDown;

        var inputRow = new DockPanel { Height = 44 };
        DockPanel.SetDock(_prompt, Dock.Left);
        inputRow.Children.Add(_prompt);
        inputRow.Children.Add(_input);
        var inputBar = new Border
        {
            Child = inputRow, Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x16, 0x18, 0x1D)),
            BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))
        };

        // The console: a dark panel with a title strip like a window's.
        var title = new DockPanel { Height = 40, Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x22, 0x28)) };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in new[] { Color.FromRgb(0xFF, 0x5F, 0x57), Color.FromRgb(0xFE, 0xBC, 0x2E), Color.FromRgb(0x28, 0xC8, 0x40) })
            dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 11, Height = 11, Fill = new SolidColorBrush(c), Margin = new Thickness(0, 0, 7, 0) });
        DockPanel.SetDock(dots, Dock.Left);
        title.Children.Add(dots);
        _state.VerticalAlignment = VerticalAlignment.Center;
        _state.Margin = new Thickness(0, 0, 16, 0);
        DockPanel.SetDock(_state, Dock.Right);
        title.Children.Add(_state);
        var name = new TextBlock { Text = powerShell ? "Windows PowerShell" : "Command Prompt", FontFamily = mono, FontSize = 14,
                                   Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xBC, 0xC6)), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(name);

        var console = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        console.Children.Add(title);
        DockPanel.SetDock(inputBar, Dock.Bottom);
        console.Children.Add(inputBar);
        console.Children.Add(_output);
        var frame = new Border
        {
            Child = console, CornerRadius = new CornerRadius(Math.Clamp(Theme.TileCorner, 0, 14)), ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0D, 0x10)),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF))
        };

        SetPage("Terminal", frame,
            "{Select}  Type a command      {Options}  Handy commands      LT / RT  Scroll      View  Clear      Start  Stop      {Back}  Close", wide: true);
        _flush.Tick += (_, _) => Flush();
    }

    public override void OnShown()
    {
        Start();
        _flush.Start();
    }

    public override void OnClosed()
    {
        _closed = true;
        _flush.Stop();
        Kill();
    }

    public override void FocusDefault() => _input.Focus();

    // ---------------------------------------------------------------- the shell

    private void Start()
    {
        Kill();
        try
        {
            string exe, args;
            if (_powerShell)
            {
                // PowerShell 7 when it's installed, else the one built into Windows. "-Command -" reads commands from here.
                exe = FindOnPath("pwsh.exe") ?? "powershell.exe";
                args = "-NoLogo -NoProfile -NoExit -ExecutionPolicy Bypass -Command -";
            }
            else
            {
                exe = "cmd.exe";
                args = "/Q /K chcp 65001>nul";
            }
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Directory.Exists(_cwd) ? _cwd : Environment.CurrentDirectory,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            var shell = new Process { StartInfo = psi, EnableRaisingEvents = true };
            shell.OutputDataReceived += (_, e) => { if (e.Data != null) Received(e.Data); };
            shell.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(e.Data + "\n"); };
            shell.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (_closed || !ReferenceEquals(shell, _shell)) return;
                Append("\n[The shell closed. Press Start to open a new one.]\n");
                _busy = false;
                ShowState();
            });
            shell.Start();
            shell.BeginOutputReadLine();
            shell.BeginErrorReadLine();
            _shell = shell;
            if (_powerShell) shell.StandardInput.WriteLine("[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'");
            Append((_powerShell ? "Windows PowerShell" : "Command Prompt") + " in SquareDeck. Type a command and press Enter (or A).\n\n");
            AskForFolder();
            _busy = true;
            ShowState();
        }
        catch (Exception ex)
        {
            Append("Couldn't start the shell: " + ex.Message + "\n");
        }
    }

    private void Kill()
    {
        var shell = _shell;
        _shell = null;
        if (shell == null) return;
        try { if (!shell.HasExited) shell.Kill(entireProcessTree: true); } catch (Exception) { }
        shell.Dispose();
    }

    private static string? FindOnPath(string file)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try { var p = Path.Combine(dir.Trim(), file); if (File.Exists(p)) return p; } catch (Exception) { }
        }
        return null;
    }

    /// <summary>After each command the shell prints its folder after a marker: that's how we know it has finished.</summary>
    private void AskForFolder()
    {
        try
        {
            _shell?.StandardInput.WriteLine(_powerShell
                ? $"Write-Output (\"{Marker}\" + (Get-Location).Path)"
                : $"echo {Marker}%CD%");
        }
        catch (Exception) { }
    }

    private void Received(string line)
    {
        int at = line.IndexOf(Marker, StringComparison.Ordinal);
        if (at >= 0)
        {
            if (at > 0) Append(line[..at] + "\n");
            string folder = line[(at + Marker.Length)..].Trim();
            Dispatcher.BeginInvoke(() =>
            {
                if (folder.Length > 0) _cwd = folder;
                _busy = false;
                ShowState();
            });
            return;
        }
        // cmd echoes its prompt before our marker line; that's noise here.
        if (!_powerShell && line.EndsWith(">", StringComparison.Ordinal) && line.Length > 2 && line[1] == ':' && line.Trim() == line) return;
        Append(line + "\n");
    }

    private void Append(string text)
    {
        lock (_lock) _pending.Append(text);
    }

    /// <summary>Output arrives line by line on another thread; it's added to the screen in batches, which is much faster.</summary>
    private void Flush()
    {
        string text;
        lock (_lock)
        {
            if (_pending.Length == 0) return;
            text = _pending.ToString();
            _pending.Clear();
        }
        bool atEnd = _output.VerticalOffset + _output.ViewportHeight >= _output.ExtentHeight - 4;
        _output.AppendText(text);
        if (_output.Text.Length > MaxChars) _output.Text = "…\n" + _output.Text[^(MaxChars * 3 / 4)..];
        if (atEnd) _output.ScrollToEnd();
    }

    private void ShowState()
    {
        _prompt.Text = (_powerShell ? "PS " : "") + _cwd + ">";
        _state.Text = _shell == null || _shell.HasExited ? "Closed" : _busy ? "Running…" : "Ready";
    }

    private void Run(string command)
    {
        command = command.TrimEnd();
        if (_shell == null || _shell.HasExited) Start();
        if (command.Length > 0 && (_history.Count == 0 || _history[^1] != command)) _history.Add(command);
        _historyIndex = _history.Count;
        Append(_prompt.Text + " " + command + "\n");
        if (command.Equals("cls", StringComparison.OrdinalIgnoreCase) || command.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            return;
        }
        if (command.Equals("exit", StringComparison.OrdinalIgnoreCase)) { Host.Pop(this); return; }
        try
        {
            _shell!.StandardInput.WriteLine(command);
            AskForFolder();
            _busy = true;
            ShowState();
        }
        catch (Exception ex) { Append(ex.Message + "\n"); }
    }

    private void Clear()
    {
        lock (_lock) _pending.Clear();
        _output.Clear();
    }

    /// <summary>A command that won't finish: the shell is replaced (redirected shells can't be sent Ctrl+C).</summary>
    private void Stop()
    {
        Append("^C\n");
        Start();
    }

    // ---------------------------------------------------------------- input

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control && _input.SelectionLength == 0:
                e.Handled = true;
                Stop();
                break;
            case Key.L when Keyboard.Modifiers == ModifierKeys.Control:
                e.Handled = true;
                Clear();
                break;
        }
    }

    private async Task TypeAsync()
    {
        if (TouchKeyboard.UseWindows)
        {
            _input.Focus();
            await TouchKeyboard.ShowAsync();
            return;
        }
        var typed = await Host.PromptAsync(_powerShell ? "PowerShell" : "Command Prompt", "Type a command", _input.Text, "Run");
        if (typed == null) return;
        _input.Clear();
        Run(typed);
    }

    private async Task HandyAsync()
    {
        var list = _powerShell
            ? new (string Label, string Command)[]
            {
                ("Network addresses", "Get-NetIPConfiguration"), ("Ping the internet", "Test-Connection 1.1.1.1 -Count 4"),
                ("Files in this folder", "Get-ChildItem"), ("Biggest running programs", "Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 15 Name,Id,@{n='MB';e={[int]($_.WorkingSet64/1MB)}}"),
                ("Disk space", "Get-PSDrive -PSProvider FileSystem"), ("Windows version", "Get-ComputerInfo OsName,OsVersion,CsProcessors"),
                ("Battery report (saved to your folder)", "powercfg /batteryreport /output \"$HOME\\battery-report.html\""),
                ("Go to my folder", "Set-Location $HOME"),
            }
            : new (string Label, string Command)[]
            {
                ("Network addresses", "ipconfig"), ("Ping the internet", "ping 1.1.1.1 -n 4"), ("Files in this folder", "dir"),
                ("Running programs", "tasklist"), ("Disk space", "wmic logicaldisk get caption,freespace,size"),
                ("Windows version", "ver"), ("Battery report (saved to your folder)", "powercfg /batteryreport /output \"%USERPROFILE%\\battery-report.html\""),
                ("Go to my folder", "cd /d %USERPROFILE%"),
            };
        var options = list.Select(c => (c.Label, "")).Append(("Clear the screen", "")).Append(("Stop what's running", "")).ToArray();
        int choice = await Host.AskWithIconsAsync("Handy commands", "Runs it here, as if you'd typed it.", options);
        if (choice < 0) return;
        if (choice < list.Length) Run(list[choice].Command);
        else if (choice == list.Length) Clear();
        else Stop();
    }

    /// <summary>Up / Down on the command line: earlier commands, like any shell.</summary>
    private void History(int step)
    {
        if (_history.Count == 0) return;
        _historyIndex = Math.Clamp(_historyIndex + step, 0, _history.Count);
        _input.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
        _input.CaretIndex = _input.Text.Length;
    }

    public override bool OnAction(NavAction action)
    {
        // On the command line (Enter, Up and Down from a keyboard arrive here too).
        if (_input.IsKeyboardFocused)
        {
            switch (action)
            {
                case NavAction.Accept when _input.Text.Length > 0:
                    var text = _input.Text;
                    _input.Clear();
                    Run(text);
                    return true;
                case NavAction.Up: History(-1); return true;
                case NavAction.Down: History(+1); return true;
            }
        }
        switch (action)
        {
            case NavAction.Accept:
            case NavAction.X:
                _ = TypeAsync();
                return true;
            case NavAction.Y: _ = HandyAsync(); return true;
            case NavAction.LT: _output.PageUp(); return true;
            case NavAction.RT: _output.PageDown(); return true;
            case NavAction.Up: _output.LineUp(); _output.LineUp(); return true;
            case NavAction.Down: _output.LineDown(); _output.LineDown(); return true;
            case NavAction.View: Clear(); return true;
            case NavAction.Start: Stop(); return true;
        }
        return false;
    }
}
