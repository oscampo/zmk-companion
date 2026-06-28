using System.IO.Pipes;
using System.Text;

namespace ZmkCompanion.Core;

// Named pipe server that lets the zkc CLI relay text to the running tray app.
// Protocol (line-based UTF-8, no BOM):
//   SEND\t<text>  → OK
//   WATCH         → READY, then reads LINE\t<text> lines until client disconnects
//   PING          → PONG
//
// PipeServer does NOT call BleService directly. All BLE writes are done by the
// _sendText delegate provided by AppContext, which dispatches to the UI thread.
internal sealed class PipeServer : IDisposable
{
    internal const string PipeName = "ZmkCompanionPipe";
    // Separate log file from CLI (zkc-cli.log) to eliminate file-lock contention.
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "zkc-tray.log");
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly CancellationTokenSource _cts = new();
    private Func<string, Task<bool>>? _sendText;

    internal void Start(Func<string, Task<bool>>? sendText = null)
    {
        _sendText = sendText;
        _ = Task.Run(() => ServeAsync(_cts.Token));
        Log("[TRAY] PipeServer started.");
    }

    private async Task ServeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // PipeOptions.None (synchronous I/O) avoids overlapped-read timing
            // issues where the client's WriteFile blocks until the server posts
            // a kernel-level ReadFile. Sync pipes use the kernel buffer
            // unconditionally, so the client write completes immediately.
            var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.None);
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
        Log("[TRAY] HandleAsync: ENTRY");
        using var _p = pipe;
        using var reader = new StreamReader(pipe, Utf8NoBom, leaveOpen: true);
        using var writer = new StreamWriter(pipe, Utf8NoBom, leaveOpen: true) { AutoFlush = true };

        try
        {
            Log("[TRAY] HandleAsync: reading command...");
            string? cmd = await reader.ReadLineAsync(ct);
            Log($"[TRAY] HandleAsync: command='{cmd ?? "<null>"}'");
            if (cmd is null) return;

            if (cmd.StartsWith("SEND\t"))
            {
                string text = cmd[5..].Replace("\\n", "\n");
                Log($"[TRAY] HandleAsync: calling sendText('{text}')");
                bool ok = await Send(text);
                Log($"[TRAY] HandleAsync: sendText={ok} — writing response");
                await writer.WriteLineAsync(ok ? "OK" : "ERR not connected or send failed");
                Log("[TRAY] HandleAsync: response written.");
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
        catch (Exception ex) { Log($"[TRAY] HandleAsync exception: {ex.GetType().Name}: {ex.Message}"); }
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
