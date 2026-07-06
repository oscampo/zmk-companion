using System.IO.Pipes;
using System.Text;

namespace ZmkCompanionCli;

internal static class CliRunner
{
    private const string PipeName = "ZmkCompanionPipe";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        if (args.Length >= 1 && args[0] is "--watch" or "-w")
            return await WatchAsync();

        return await SendAsync(string.Join(" ", args));
    }

    // ── Modes ─────────────────────────────────────────────────────────────────

    private static async Task<int> SendAsync(string text)
    {
        try
        {
            using var pipe = Connect(out bool connected);
            if (!connected) return TrayNotRunning();

            using var writer = Writer(pipe);
            using var reader = Reader(pipe);

            await writer.WriteLineAsync($"SEND\t{text}");

            string? response = await reader.ReadLineAsync();
            if (response is null)  return Err("no response from tray app (pipe closed unexpectedly)");
            if (response == "OK") { Console.WriteLine("Sent."); return 0; }
            return Err(response.Length > 4 ? response[4..] : "send failed");
        }
        catch (Exception ex) { return Err(ex.Message); }
    }

    private static async Task<int> WatchAsync()
    {
        try
        {
            using var pipe = Connect(out bool connected);
            if (!connected) return TrayNotRunning();

            using var writer = Writer(pipe);
            using var reader = Reader(pipe);

            await writer.WriteLineAsync("WATCH");
            string? ready = await reader.ReadLineAsync();
            if (ready != "READY") return 1;

            // Read char-by-char so \r (carriage return) also acts as a line separator.
            // This supports scripts that use \r to overwrite the terminal line in-place
            // (e.g. print(f"\r{now}", end="", flush=True)).
            //
            // Scripts like that never send a trailing terminator for their LAST value
            // before sleeping — the \r that would flush it only arrives with the NEXT
            // print, one tick later. That makes every value appear one full tick late.
            // To avoid it, flush the buffered line early once the input goes quiet for
            // idleFlushMs: with nothing new arriving, what's buffered is the finished
            // value for this tick, not a partial write still being typed out.
            const int idleFlushMs = 150;
            var sb = new System.Text.StringBuilder();
            var charBuf = new char[1];
            Task<int> pendingRead = Console.In.ReadAsync(charBuf, 0, 1);

            while (true)
            {
                if (sb.Length > 0)
                {
                    var timeout = Task.Delay(idleFlushMs);
                    if (await Task.WhenAny(pendingRead, timeout) == timeout)
                    {
                        string idleLine = sb.ToString();
                        sb.Clear();
                        await writer.WriteLineAsync($"LINE\t{idleLine}");
                        continue;
                    }
                }

                int n = await pendingRead;
                if (n == 0) break; // EOF
                char ch = charBuf[0];
                pendingRead = Console.In.ReadAsync(charBuf, 0, 1);

                if (ch is '\n' or '\r')
                {
                    string line = sb.ToString();
                    sb.Clear();
                    if (line.Length > 0)
                        await writer.WriteLineAsync($"LINE\t{line}");
                }
                else
                {
                    sb.Append(ch);
                }
            }
            // Flush any trailing content that had no line terminator (e.g. killed mid-line)
            if (sb.Length > 0)
                await writer.WriteLineAsync($"LINE\t{sb}");

            return 0;
        }
        catch (Exception ex) { return Err(ex.Message); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static NamedPipeClientStream Connect(out bool connected)
    {
        var pipe = new NamedPipeClientStream(".", PipeName,
            PipeDirection.InOut, PipeOptions.None);
        try { pipe.Connect(3000); connected = true; }
        catch (TimeoutException) { connected = false; }
        return pipe;
    }

    private static StreamWriter Writer(NamedPipeClientStream pipe) =>
        new(pipe, Utf8NoBom, leaveOpen: true) { AutoFlush = true };

    private static StreamReader Reader(NamedPipeClientStream pipe) =>
        new(pipe, Utf8NoBom, leaveOpen: true);

    private static int TrayNotRunning()
    {
        Console.Error.WriteLine("zkc: tray app not running — launch ZmkCompanion from the Start menu first.");
        return 1;
    }

    private static int Err(string msg) { Console.Error.WriteLine($"zkc: {msg}"); return 1; }

    private static void PrintHelp() => Console.WriteLine("""
        zkc — ZMK Keyboard Companion CLI

        Usage:
          zkc "text"        Send text to the keyboard display (persists until next update)
          zkc ""            Clear the text display and restore the canvas page
          zkc --watch       Read lines from stdin and send each one live
          zkc -w            Alias for --watch
          zkc --help        Show this help

        Examples:
          zkc "Hola mundo"
          zkc "Line1\nLine2\nLine3"
          echo "score: 3-1" | zkc --watch
          python reloj.py | zkc --watch

        Notes:
          Use \n in quoted strings for multi-line text.
          --watch accepts both \n and \r as line separators, so scripts that
          use carriage-return to overwrite a terminal line work out of the box.
          The ZMK Companion tray app must be running.
        """);
}
