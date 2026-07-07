using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;

namespace ZmkCompanion.Core;

// Named pipe server that lets the zkc CLI relay text to the running tray app.
// Protocol (line-based UTF-8, no BOM):
//   SEND\t<text>         → OK   (unnamed ExternalText channel, {ext.text}/{ext.text.N})
//   SET\t<name>\t<value> → OK   (named {custom.<name>} channel)
//   WATCH                → READY, then LINE\t<text> lines target ExternalText
//   WATCH\t<name>        → READY, then LINE\t<text> lines target custom <name>
//   PING                 → PONG
// <name> must match ^[a-z0-9_]+$ or the command errors out instead of silently
// no-opping, so a scripting typo is visible immediately at the CLI, not just
// as a blank spot on the display.
internal sealed class PipeServer : IDisposable
{
    internal const string PipeName = "ZmkCompanionPipe";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Regex ValidTokenName = new("^[a-z0-9_]+$", RegexOptions.Compiled);

    private readonly CancellationTokenSource _cts = new();
    private Func<string, Task<bool>>? _sendText;
    private Func<string, string, Task<bool>>? _setCustom;

    internal void Start(Func<string, Task<bool>>? sendText = null,
                         Func<string, string, Task<bool>>? setCustom = null)
    {
        _sendText  = sendText;
        _setCustom = setCustom;
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
                string text = UnescapeNewlines(cmd[5..]);
                bool ok = await Send(text);
                await writer.WriteLineAsync(ok ? "OK" : "ERR not connected or send failed");
            }
            else if (cmd.StartsWith("SET\t"))
            {
                string rest = cmd[4..];
                int tab = rest.IndexOf('\t');
                if (tab < 0) { await writer.WriteLineAsync("ERR malformed SET"); return; }
                string name = rest[..tab];
                if (!ValidTokenName.IsMatch(name))
                {
                    await writer.WriteLineAsync($"ERR invalid token name '{name}' (use a-z, 0-9, _)");
                    return;
                }
                string value = UnescapeNewlines(rest[(tab + 1)..]);
                bool ok = await SetCustom(name, value);
                await writer.WriteLineAsync(ok ? "OK" : "ERR not connected or send failed");
            }
            else if (cmd == "WATCH" || cmd.StartsWith("WATCH\t"))
            {
                string? target = cmd.Length > 5 ? cmd[6..] : null;
                if (target is not null && !ValidTokenName.IsMatch(target))
                {
                    await writer.WriteLineAsync($"ERR invalid token name '{target}' (use a-z, 0-9, _)");
                    return;
                }
                await writer.WriteLineAsync("READY");
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                    if (line.StartsWith("LINE\t"))
                    {
                        string text = UnescapeNewlines(line[5..]);
                        DebugLog.Log($"pipe: LINE received target={target ?? "(ext.text)"} len={text.Length} " +
                            $"preview='{text.Replace("\n","\\n")[..Math.Min(40,text.Length)]}'");
                        if (target is null) await Send(text);
                        else await SetCustom(target, text);
                    }
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

    private Task<bool> SetCustom(string name, string value) =>
        _setCustom is not null ? _setCustom(name, value) : Task.FromResult(false);

    // "\n" -> real newline, "\\n" -> literal "\n" text (2 chars). A plain
    // .Replace("\\n", "\n") can't tell them apart, "\n" is a substring of "\\n"
    // starting at its second backslash, so "\\n" would silently become "\" plus
    // a real newline instead of the literal text "\n".
    //
    // Deliberately does NOT have a generic "any \\ collapses to \" rule: this
    // text is passed through LiveState.ExpandEscaped() later (in AppContext's
    // drain timer), which has its own "\\{ -> literal \{" unescaping for
    // \{token\}. A generic rule here would consume one layer of that escaping
    // before ExpandEscaped ever saw it, e.g. "\\{battery.percent\}" (meant as
    // literal text) would get collapsed to "\{battery.percent\}" by this method
    // first, which ExpandEscaped would then wrongly resolve as a live token.
    // Only "\n"/"\\n" are this method's concern; every other backslash sequence,
    // including "\\{", passes through untouched for the next stage to interpret.
    private static string UnescapeNewlines(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('\\'))
            return text;

        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                if (text[i + 1] == 'n') { sb.Append('\n'); i += 2; continue; }
                if (text[i + 1] == '\\' && i + 2 < text.Length && text[i + 2] == 'n')
                {
                    sb.Append("\\n"); // literal 2-char text, not a real newline
                    i += 3;
                    continue;
                }
                sb.Append('\\'); // not our escape form, leave untouched for later stages
                i += 1;
                continue;
            }
            sb.Append(text[i]);
            i += 1;
        }
        return sb.ToString();
    }

    public void Dispose() { _cts.Cancel(); _cts.Dispose(); }
}
