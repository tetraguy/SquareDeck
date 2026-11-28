using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;

namespace SquareDeck.Services;

/// <summary>
/// The connection to SquareDeck's Xbox Game Bar widget (the Widget folder in the repository). The widget runs
/// sandboxed inside Game Bar and can't set the TDP itself, so SquareDeck does it: it listens on a named pipe that
/// sandboxed apps may open, sends the widget what's going on about once a second (TDP, stats, sound, brightness,
/// the ROG Ally's modes, the quick toggles) and carries out what the widget asks.
///
/// The way through the sandbox (a pipe that "ALL APPLICATION PACKAGES" may open, reached from the widget with
/// CreateFileW) follows GoTweaks by corando98 (github.com/corando98/GoTweaks, MIT).
///
/// The messages are one JSON object per line. The widget sends {"op": "...", ...}; SquareDeck sends
/// {"type": "state", ...} and {"type": "note", "text": "..."}.
/// </summary>
public static class WidgetBridge
{
    public const string PipeName = "SquareDeck.Widget";
    private const int MaxClients = 4;

    /// <summary>What to send: built on the interface thread by the main window.</summary>
    public static Func<JsonObject>? Snapshot;
    /// <summary>Carries out one request from the widget (on the interface thread).</summary>
    public static Action<string, JsonObject>? Handle;
    /// <summary>Runs something on the interface thread and waits for it.</summary>
    public static Func<Func<JsonObject?>, JsonObject?>? OnUi;

    private static CancellationTokenSource? _cts;
    private static readonly List<Client> Clients = new();
    private static HardwareMonitor? _monitor;
    private static PowerMeter? _power;
    private static DeviceSensors.Reading? _sensors;
    private static DateTime _sensorsAt;
    private static readonly SemaphoreSlim Kick = new(0, 1);
    private static int _pipeErrorLogged;

    public static bool Running => _cts != null;
    public static int Connected { get { lock (Clients) return Clients.Count; } }

    private sealed class Client
    {
        public required NamedPipeServerStream Pipe;
        public readonly object WriteLock = new();
    }

    public static void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        RegisterProtocol();
        for (int i = 0; i < MaxClients; i++) _ = Task.Run(() => AcceptLoop(ct));
        _ = Task.Run(() => PushLoop(ct));
    }

    public static void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        lock (Clients)
        {
            foreach (var c in Clients) { try { c.Pipe.Dispose(); } catch (Exception) { } }
            Clients.Clear();
        }
    }

    /// <summary>Starts again from scratch (the widget's "Start SquareDeck" while SquareDeck is already open).</summary>
    public static void Restart()
    {
        Stop();
        _pipeErrorLogged = 0;
        Start();
    }

    /// <summary>Sends fresh state straight away (after a change), instead of at the next tick.</summary>
    public static void Nudge()
    {
        try { if (Kick.CurrentCount == 0) Kick.Release(); } catch (SemaphoreFullException) { }
    }

    /// <summary>Sends one message to every connected widget (the game's pictures, say).</summary>
    public static void Send(JsonObject message)
    {
        if (Connected > 0) Broadcast(message);
    }

    /// <summary>A short line for the widget to show ("120 Hz", "Lights off").</summary>
    /// <summary>9.0.21: every note, also for SquareDeck's own quick menu.</summary>
    public static event Action<string>? Noted;

    public static void Note(string text)
    {
        try { Noted?.Invoke(text); } catch (Exception) { }
        if (Connected == 0) return;
        Broadcast(new JsonObject { ["type"] = "note", ["text"] = text });
        Nudge();
    }

    /// <summary>
    /// The pipe: the user and sandboxed apps (ALL APPLICATION PACKAGES, which is how the Game Bar widget is seen)
    /// may read and write it. Nothing else on the PC can.
    /// </summary>
    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        if (WindowsIdentity.GetCurrent().User is { } me)
            security.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier("S-1-15-2-1"), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        try { security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier("S-1-15-2-2"), PipeAccessRights.ReadWrite, AccessControlType.Allow)); }
        catch (Exception) { } // not on older Windows
        // Exactly as GoTweaks does it (and it works there): the access list alone, nothing else asked of Windows.
        return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, MaxClients, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            4096, 4096, security);
    }

    private static async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try { pipe = CreatePipe(); }
            catch (Exception ex)
            {
                // Written once, not every few seconds from every listener.
                if (Interlocked.Exchange(ref _pipeErrorLogged, 1) == 0) CrashLog.Write("Widget pipe", ex);
                try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { return; }
                continue;
            }
            try { await pipe.WaitForConnectionAsync(ct); }
            catch (Exception) { pipe.Dispose(); if (ct.IsCancellationRequested) return; continue; }
            var client = new Client { Pipe = pipe };
            lock (Clients) Clients.Add(client);
            Nudge();
            await ReadLoop(client, ct);
            lock (Clients) Clients.Remove(client);
            try { pipe.Dispose(); } catch (Exception) { }
        }
    }

    private static async Task ReadLoop(Client client, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(client.Pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            while (!ct.IsCancellationRequested && client.Pipe.IsConnected)
            {
                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                if (line.Length == 0 || line.Length > 8192) continue;
                JsonObject? request;
                try { request = JsonNode.Parse(line) as JsonObject; } catch (Exception) { continue; }
                if (request?["op"]?.GetValue<string>() is not { Length: > 0 } op) continue;
                if (op == "hello") { Nudge(); continue; }
                try { OnUi?.Invoke(() => { Handle?.Invoke(op, request); return null; }); }
                catch (Exception ex) { CrashLog.Write("Widget request " + op, ex); }
                Nudge();
            }
        }
        catch (Exception) { } // the widget closed
    }

    /// <summary>About once a second while a widget is connected (at once after a change): the state, with fresh stats.</summary>
    private static async Task PushLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Kick.WaitAsync(TimeSpan.FromSeconds(1), ct); }
            catch (OperationCanceledException) { return; }
            if (Connected == 0)
            {
                _monitor?.Dispose();
                _monitor = null;
                _power?.Dispose();
                _power = null;
                continue;
            }
            try
            {
                if (Snapshot == null || OnUi == null) continue;
                var state = OnUi(() => Snapshot());
                if (state == null) continue;
                try
                {
                    _monitor ??= new HardwareMonitor();
                    var (cpu, mhz, gpu) = _monitor.Sample();
                    if (cpu is double c) state["cpu"] = Math.Round(c);
                    if (gpu is double g) state["gpu"] = Math.Round(g);
                    if (mhz is int m) state["mhz"] = m;
                }
                catch (Exception) { }
                // More for the Live card's choices: the processor's power use, temperatures, memory and battery draw.
                try
                {
                    _power ??= new PowerMeter();
                    if (_power.Watts() is double w) state["apu"] = Math.Round(w, 1);
                    if (DateTime.UtcNow - _sensorsAt > TimeSpan.FromSeconds(2))
                    {
                        _sensorsAt = DateTime.UtcNow;
                        _sensors = DeviceSensors.Read();
                    }
                    if (_sensors is { } r)
                    {
                        if (r.CpuTemp is int cpuTemp) state["cputemp"] = cpuTemp;
                        if (r.GpuTemp is int gt) state["gputemp"] = gt;
                        if (!r.Plugged && r.DrawWatts is double d && d < 0) state["draw"] = Math.Round(-d, 1);
                    }
                    if (Hardware.MemoryStatus() is { Total: > 0 } mem) state["ram"] = Math.Round(100.0 * (mem.Total - mem.Available) / mem.Total);
                }
                catch (Exception) { }
                state["type"] = "state";
                Broadcast(state);
            }
            catch (Exception ex) { CrashLog.Write("Widget state", ex); }
        }
    }

    private static void Broadcast(JsonObject message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message.ToJsonString() + "\n");
        List<Client> clients;
        lock (Clients) clients = Clients.ToList();
        foreach (var c in clients)
        {
            try
            {
                lock (c.WriteLock)
                {
                    c.Pipe.Write(bytes, 0, bytes.Length);
                    c.Pipe.Flush();
                }
            }
            catch (Exception) { try { c.Pipe.Dispose(); } catch (Exception) { } }
        }
    }

    /// <summary>
    /// squaredeck: links start SquareDeck (the widget's "Start SquareDeck" button opens squaredeck:background,
    /// which starts it minimized so the game stays in front). Registered for this Windows user only.
    /// </summary>
    public static void RegisterProtocol()
    {
        try
        {
            string exe = Environment.ProcessPath ?? "";
            if (exe.Length == 0) return;
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\squaredeck");
            key.SetValue("", "URL:SquareDeck");
            key.SetValue("URL Protocol", "");
            using var icon = key.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{exe}\",0");
            using var command = key.CreateSubKey(@"shell\open\command");
            string wanted = $"\"{exe}\" \"%1\"";
            if (command.GetValue("") as string != wanted) command.SetValue("", wanted);
        }
        catch (Exception) { }
    }
}
