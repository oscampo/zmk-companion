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
                await pipe.WaitForConnectionAsync(ct);
                _ = HandleAsync(pipe, ct);   // handle concurrently; next iteration creates a new server instance
            }
            catch (OperationCanceledException) { await pipe.DisposeAsync(); break; }
            catch                              { await pipe.DisposeAsync(); }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var _p = pipe;
        using var reader = new StreamReader(pipe, System.Text.Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, System.Text.Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        try
        {
            string? cmd = await reader.ReadLineAsync(ct);
            if (cmd is null) return;

            if (cmd.StartsWith("SEND\t"))
            {
                bool ok = await Send(cmd[5..].Replace("\\n", "\n"));
                await writer.WriteLineAsync(ok ? "OK" : "ERR not connected or send failed");
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
        catch { }
    }

    private Task<bool> Send(string text) =>
        _sendText is not null ? _sendText(text) : Task.FromResult(false);

    public void Dispose() { _cts.Cancel(); _cts.Dispose(); }
}
