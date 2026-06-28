using System.IO.Pipes;
using System.Text;

namespace ZmkCompanion.Core;

// Named pipe server that lets the zkc CLI relay text to the running tray app.
// Protocol (line-based UTF-8, no BOM):
//   SEND\t<text>  → OK
//   WATCH         → READY, then reads LINE\t<text> lines until client disconnects
//   PING          → PONG
internal sealed class PipeServer : IDisposable
{
    internal const string PipeName = "ZmkCompanionPipe";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly CancellationTokenSource _cts = new();
    private Func<string, Task<bool>>? _sendText;

    internal void Start(Func<string, Task<bool>>? sendText = null)
    {
        _sendText = sendText;
        _ = Task.Run(() => ServeAsync(_cts.Token));
    }

    private async Task ServeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // PipeOptions.None (synchronous I/O): the kernel buffer accepts client
            // writes unconditionally, without waiting for a pending server read.
            var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.None);
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                _ = HandleAsync(pipe, ct);
            }
            catch (OperationCanceledException) { await pipe.DisposeAsync(); break; }
            catch { await pipe.DisposeAsync(); }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var _p = pipe;
        using var reader = new StreamReader(pipe, Utf8NoBom, leaveOpen: true);
        using var writer = new StreamWriter(pipe, Utf8NoBom, leaveOpen: true) { AutoFlush = true };

        try
        {
            string? cmd = await reader.ReadLineAsync(ct);
            if (cmd is null) return;

            if (cmd.StartsWith("SEND\t"))
            {
                string text = cmd[5..].Replace("\\n", "\n");
                bool ok = await Send(text);
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
