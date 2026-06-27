using System.IO.Pipes;

namespace ZmkCompanion.Core;

// Named pipe server that lets the zkc CLI relay text to the running tray app.
// Protocol (line-based UTF-8):
//   SEND\t<text>  → OK
//   WATCH         → READY, then reads LINE\t<text> lines until client disconnects
//   PING          → PONG
internal sealed class PipeServer : IDisposable
{
    internal const string PipeName = "ZmkCompanionPipe";

    private readonly BleService _ble;
    private readonly CancellationTokenSource _cts = new();

    internal PipeServer(BleService ble) => _ble = ble;

    internal void Start() => _ = ServeAsync(_cts.Token);

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
                await _ble.SendAsync(cmd[5..].Replace("\\n", "\n"));
                await writer.WriteLineAsync("OK");
            }
            else if (cmd == "WATCH")
            {
                await writer.WriteLineAsync("READY");
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                    if (line.StartsWith("LINE\t"))
                        await _ble.SendAsync(line[5..].Replace("\\n", "\n"));
            }
            else if (cmd == "PING")
            {
                await writer.WriteLineAsync("PONG");
            }
        }
        catch { }
    }

    public void Dispose() { _cts.Cancel(); _cts.Dispose(); }
}
