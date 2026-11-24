using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Windows.Data.Json;

namespace SquareDeck.Widget.Link
{
    /// <summary>
    /// The line to SquareDeck: the named pipe SquareDeck opens for its widget (Services/WidgetBridge.cs in the launcher).
    /// One JSON object per line each way. It connects by itself, and again whenever SquareDeck restarts.
    ///
    /// A UWP app can't use NamedPipeClientStream, so the pipe is opened with CreateFileW and read as a file, as
    /// GoTweaks by corando98 does it (github.com/corando98/GoTweaks, MIT). SquareDeck lets sandboxed apps in.
    /// </summary>
    public sealed class DeckLink : IDisposable
    {
        private const string PipePath = @"\\.\pipe\SquareDeck.Widget";
        private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, OpenExisting = 3, Overlapped = 0x40000000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        private readonly object _writeLock = new object();
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private FileStream _stream;
        private volatile bool _connected;

        /// <summary>A message from SquareDeck (on a background thread).</summary>
        public event Action<JsonObject> Message;
        /// <summary>Connected or lost (on a background thread).</summary>
        public event Action<bool> ConnectionChanged;

        public bool IsConnected => _connected;

        public void Start() => Task.Run(() => RunAsync(_life.Token));

        /// <summary>Try again straight away (after starting SquareDeck).</summary>
        public void Retry() => _retryNow = true;
        private volatile bool _retryNow;

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                SafeFileHandle handle = CreateFileW(PipePath, GenericRead | GenericWrite, 0, IntPtr.Zero, OpenExisting, Overlapped, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    // Not running (or the widget connection is off in SquareDeck's settings): look again in a moment.
                    for (int i = 0; i < 20 && !_retryNow && !ct.IsCancellationRequested; i++) await Task.Delay(100).ConfigureAwait(false);
                    _retryNow = false;
                    continue;
                }
                try
                {
                    // Overlapped, so a write can go out while a read is waiting (a plain handle does one at a time).
                    _stream = new FileStream(handle, FileAccess.ReadWrite, 4096, true);
                    _connected = true;
                    ConnectionChanged?.Invoke(true);
                    Send(new JsonObject { ["op"] = JsonValue.CreateStringValue("hello") });
                    using (var reader = new StreamReader(_stream, new UTF8Encoding(false), false, 8192, true))
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            string line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (line == null) break; // SquareDeck closed
                            if (line.Length == 0) continue;
                            JsonObject message;
                            if (!JsonObject.TryParse(line, out message)) continue;
                            try { Message?.Invoke(message); } catch (Exception) { }
                        }
                    }
                }
                catch (Exception) { }
                finally
                {
                    lock (_writeLock)
                    {
                        try { _stream?.Dispose(); } catch (Exception) { }
                        _stream = null;
                    }
                    if (_connected)
                    {
                        _connected = false;
                        ConnectionChanged?.Invoke(false);
                    }
                }
                try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>Sends one request (dropped quietly when SquareDeck isn't there).</summary>
        public void Send(JsonObject message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message.Stringify() + "\n");
            Task.Run(() =>
            {
                lock (_writeLock)
                {
                    try
                    {
                        if (_stream == null) return;
                        _stream.Write(bytes, 0, bytes.Length);
                        _stream.Flush();
                    }
                    catch (Exception) { }
                }
            });
        }

        public void Send(string op) => Send(new JsonObject { ["op"] = JsonValue.CreateStringValue(op) });

        public void Send(string op, string name, double value)
        {
            var o = new JsonObject { ["op"] = JsonValue.CreateStringValue(op) };
            o[name] = JsonValue.CreateNumberValue(value);
            Send(o);
        }

        public void Send(string op, string name, string value)
        {
            var o = new JsonObject { ["op"] = JsonValue.CreateStringValue(op) };
            o[name] = JsonValue.CreateStringValue(value);
            Send(o);
        }

        public void Dispose()
        {
            _life.Cancel();
            lock (_writeLock)
            {
                try { _stream?.Dispose(); } catch (Exception) { }
                _stream = null;
            }
        }
    }
}
