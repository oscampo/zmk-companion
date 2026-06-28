using System.IO.Pipes;

namespace ZmkCompanion.Core;

// Named pipe server that lets the zkc CLI relay text to the running tray app.
// Protocol (line-based UTF-8):
//   SEND\t<text>  → OK
//   WATCH         → READY, then reads LINE\t<text> lines until client disconnects
//   PING          → PONG
//
// PipeServer does NOT call BleService directly. All BLE writes are done by the
// _sendText delegate provided by AppContext, which dispatches to the UI thread.
internal sealed class PipeServer : IDisposable
{
    internal const string PipeName = "ZmkCompanionPipe";
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "zkc-debug.log");

    private readonly CancellationTokenSource _cts = new();
    private Func<string, Task<bool>>? _sendText;

    internal void Start(Func<string, Task<bool>>? sendText = null)
    {
        _sendText = sendText;
        // Task.Run ensures ServeAsync and all its continuations (including HandleAsync)
        // run on the thread pool, not the UI thread. Without this, Start() called from
        // OnFirstIdle would cause ServeAsync continuations to post back to the UI
        // SynchronizationContext, and the TCS await inside _sendText would deadlock:
        // the UI thread suspends waiting for the TCS while the work that completes
        // the TCS is queued on the same UI thread.
        _ = Task.Run(() => ServeAsync(_cts.Token));
        Log("[TRAY] PipeServer started on thread pool.");
    }

    private async Task ServeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            try
            {
                Log("[TRAY] Waiting for client connection...");
                await pipe.WaitForConnectionAsync(ct);
                Log("[TRAY] Client connected. Spawning HandleAsync.");
                _ = HandleAsync(pipe, ct);
            }
            catch (OperationCanceledException) { await pipe.DisposeAsync(); break; }
            catch (Exception ex)               { Log($"[TRAY] ServeAsync error: {ex.Message}"); await pipe.DisposeAsync(); }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var _p = pipe;
        using var reader = new StreamReader(pipe, System.Text.Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, System.Text.Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        try
        {
            Log("[TRAY] HandleAsync: reading command...");
            string? cmd = await reader.ReadLineAsync(ct);
            Log($"[TRAY] HandleAsync: command = '{cmd ?? "<null>"}'");
            if (cmd is null) return;

            if (cmd.StartsWith("SEND\t"))
            {
                string text = cmd[5..].Replace("\\n", "\n");
                Log($"[TRAY] Calling _sendText('{text}')...");
                bool ok = await Send(text);
                Log($"[TRAY] _sendText returned: {ok}. Writing response...");
                await writer.WriteLineAsync(ok ? "OK" : "ERR not connected or send failed");
                Log("[TRAY] Response written.");
            }
            else if (cmd == "WATCH")
            {
                await writer.WriteLineAsync("READY");
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                    if (line.StartsWith("LINE\t"))
                        await Send(line[5..].Replace("\\n", "\n"));
            }
            else if (cmd == "PING")
            {
                await writer.WriteLineAsync("PONG");
            }
        }
        catch (Exception ex) { Log($"[TRAY] HandleAsync exception: {ex.Message}"); }
    }

    private Task<bool> Send(string text) =>
        _sendText is not null ? _sendText(text) : Task.FromResult(false);

    private static void Log(string msg)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
        try { File.AppendAllText(LogFile, line + Environment.NewLine); } catch { }
    }

    public void Dispose() { _cts.Cancel(); _cts.Dispose(); }
}
