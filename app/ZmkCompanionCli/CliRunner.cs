using System.IO.Pipes;

namespace ZmkCompanionCli;

internal static class CliRunner
{
    private const string PipeName = "ZmkCompanionPipe";

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
            if (await reader.ReadLineAsync() != "READY") return 1;

            string? line;
            while ((line = Console.ReadLine()) != null)
                await writer.WriteLineAsync($"LINE\t{line}");

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
        new(pipe, System.Text.Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

    private static StreamReader Reader(NamedPipeClientStream pipe) =>
        new(pipe, System.Text.Encoding.UTF8, leaveOpen: true);

    private static int TrayNotRunning()
    {
        Console.Error.WriteLine("zkc: tray app not running — launch ZmkCompanion from the Start menu first.");
        return 1;
    }

    private static int Err(string msg) { Console.Error.WriteLine($"zkc: {msg}"); return 1; }

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
