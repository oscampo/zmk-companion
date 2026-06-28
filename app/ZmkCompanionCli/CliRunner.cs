using System.IO.Pipes;
using System.Text;

namespace ZmkCompanionCli;

internal static class CliRunner
{
    private const string PipeName = "ZmkCompanionPipe";
    // Separate log from tray (zkc-tray.log) — avoids cross-process file-lock contention.
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "zkc-cli.log");
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

        // Everything else is treated as the message text
        return await SendAsync(string.Join(" ", args));
    }

    // ── Modes ─────────────────────────────────────────────────────────────────

    private static async Task<int> SendAsync(string text)
    {
        try
        {
            Log($"[CLI] Connecting to pipe '{PipeName}'...");
            using var pipe = Connect(out bool connected);
            if (!connected) { Log("[CLI] Pipe connect TIMEOUT — tray not running"); return TrayNotRunning(); }
            Log($"[CLI] Connected. Sending: SEND\\t{text}");

            using var writer = Writer(pipe);
            using var reader = Reader(pipe);

            await writer.WriteLineAsync($"SEND\t{text}");
            Log("[CLI] Command written. Waiting for response...");

            string? response = await reader.ReadLineAsync();
            Log($"[CLI] Response received: '{response ?? "<null>"}'");

            if (response is null)  return Err("no response from tray app (pipe closed unexpectedly)");
            if (response == "OK") { Console.WriteLine("Sent."); return 0; }
            return Err(response.Length > 4 ? response[4..] : "send failed");
        }
        catch (Exception ex) { Log($"[CLI] Exception: {ex}"); return Err(ex.Message); }
    }

    private static async Task<int> WatchAsync()
    {
        try
        {
            Log($"[CLI] WATCH mode. Connecting to pipe '{PipeName}'...");
            using var pipe = Connect(out bool connected);
            if (!connected) { Log("[CLI] Pipe connect TIMEOUT — tray not running"); return TrayNotRunning(); }
            Log("[CLI] Connected. Sending WATCH...");

            using var writer = Writer(pipe);
            using var reader = Reader(pipe);

            await writer.WriteLineAsync("WATCH");
            Log("[CLI] Waiting for READY...");
            string? ready = await reader.ReadLineAsync();
            Log($"[CLI] Got: '{ready}'");
            if (ready != "READY") return 1;

            string? line;
            while ((line = Console.ReadLine()) != null)
            {
                Log($"[CLI] Sending LINE: {line}");
                await writer.WriteLineAsync($"LINE\t{line}");
            }

            return 0;
        }
        catch (Exception ex) { Log($"[CLI] Exception: {ex}"); return Err(ex.Message); }
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

    private static void Log(string msg)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
        Console.Error.WriteLine(line);
        try { File.AppendAllText(LogFile, line + Environment.NewLine); } catch { }
    }

    private static void PrintHelp() => Console.WriteLine("""
        zkc — ZMK Keyboard Companion CLI

        Usage:
          zkc "text"        Send text to the keyboard display
          zkc --watch       Read lines from stdin and send each one
          zkc -w            Alias for --watch
          zkc --help        Show this help

        Examples:
          zkc "Hola mundo"
          zkc "Line1\nLine2\nLine3"
          echo "ARG 1-0 FRA" | zkc --watch
          curl -s api.example.com/score | zkc --watch

        Notes:
          Use \n for line breaks (max 3 lines, 64 bytes UTF-8 total).
          The ZMK Companion tray app must be running.
        """);
}
