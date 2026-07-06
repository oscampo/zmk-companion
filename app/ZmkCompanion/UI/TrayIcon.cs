using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using ZmkCompanion.Core;

namespace ZmkCompanion.UI;

// Owns the NotifyIcon and builds the context menu.
// All mutations must happen on the UI thread (ensured by callers via BleService events).
sealed class TrayIcon : IDisposable
{
    private readonly BleService   _ble;
    private readonly NotifyIcon   _notify;

    // Set post-construction (compositor is created after TrayIcon).
    internal DisplayCompositor? Compositor { get; set; }

    public event Action? ExitRequested;
    public event Action? CanvasEditorRequested;
    public event Action? PomodoroToggleRequested;
    public event Action? PomodoroConfigRequested;

    // Controlled by AppContext: reflects whether any page has a Pomodoro widget.
    public bool HasPomodoroWidget { get; set; }

    private bool    _pomodoroRunning;
    private string? _pomodoroLabel;   // null = not running; non-null = displayed in menu item

    public TrayIcon(BleService ble)
    {
        _ble    = ble;

        _notify = new NotifyIcon
        {
            Visible = true,
            Text    = "ZMK Companion — searching…",
            Icon    = MakeIcon(Color.OrangeRed),
        };
        _notify.ContextMenuStrip = BuildMenu();
    }

    // ── Pomodoro state (set by AppContext) ────────────────────────────────────

    // running=true + label = pomodoro running (label shown in menu item).
    // running=false = idle or no widget configured.
    public void SetPomodoroState(bool running, string? label)
    {
        _pomodoroRunning = running;
        _pomodoroLabel   = label;
        RebuildMenu();
    }

    // ── Connection state ──────────────────────────────────────────────────────

    public void ShowBalloonTip(int ms, string title, string text, ToolTipIcon icon) =>
        _notify.ShowBalloonTip(ms, title, text, icon);

    public void ShowError(string title, string message) =>
        _notify.ShowBalloonTip(5000, title, message, ToolTipIcon.Error);

    public void SetConnected(string deviceName)
    {
        _notify.Icon = MakeIcon(Color.LimeGreen);
        _notify.Text = $"ZMK Companion — {deviceName}";
        _notify.ShowBalloonTip(2000, "ZMK Companion", $"Conectado a {deviceName}", ToolTipIcon.Info);
        RebuildMenu();
    }

    public void SetDisconnected()
    {
        _notify.Icon = MakeIcon(Color.OrangeRed);
        _notify.Text = "ZMK Companion — desconectado";
        _notify.ShowBalloonTip(2000, "ZMK Companion", "Teclado desconectado", ToolTipIcon.Warning);
        RebuildMenu();
    }

    // ── Menu ──────────────────────────────────────────────────────────────────

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        PopulateMenu(menu);
        return menu;
    }

    private void RebuildMenu()
    {
        if (_notify.ContextMenuStrip is not { } strip) return;
        strip.Items.Clear();
        PopulateMenu(strip);
    }

    private void PopulateMenu(ContextMenuStrip strip)
    {
        bool connected = _ble.IsConnected;

        var header = new ToolStripLabel(_ble.DeviceName is { } name
            ? $"  {name}"
            : "  No conectado")
        { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        strip.Items.Add(header);
        strip.Items.Add(new ToolStripSeparator());

        strip.Items.Add(new ToolStripMenuItem("Canvas…", null, (_, _) => CanvasEditorRequested?.Invoke())
            { Enabled = connected });

        strip.Items.Add(new ToolStripSeparator());

        string pomText = _pomodoroRunning
            ? (_pomodoroLabel ?? "Pomodoro — Detener")
            : "Pomodoro — Iniciar";
        strip.Items.Add(new ToolStripMenuItem(pomText, null, (_, _) => PomodoroToggleRequested?.Invoke())
            { Enabled = connected && HasPomodoroWidget });
        strip.Items.Add(new ToolStripMenuItem("Configurar Pomodoro…", null,
            (_, _) => PomodoroConfigRequested?.Invoke()));

        strip.Items.Add(new ToolStripSeparator());

        if (!connected)
            strip.Items.Add(new ToolStripMenuItem("Reconectar", null, (_, _) => OnReconnect()));
        else
            strip.Items.Add(new ToolStripMenuItem("Desconectar", null, (_, _) => OnDisconnect()));

        strip.Items.Add(new ToolStripSeparator());

        strip.Items.Add(new ToolStripMenuItem("Debug Log", null, (_, _) => OnDebugLog()));
        strip.Items.Add(new ToolStripMenuItem("Acerca de…", null, (_, _) => OnAbout()));
        strip.Items.Add(new ToolStripMenuItem("Salir", null, (_, _) => ExitRequested?.Invoke()));
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private void OnReconnect() =>
        _ = Task.Run(async () => await _ble.ScanAndConnectAsync());

    private void OnDisconnect() =>
        _ = _ble.SendAsync(Protocol.BuildClear());

    private void OnDebugLog()
    {
        try
        {
            if (!File.Exists(DebugLog.Path))
            {
                MessageBox.Show("Aún no hay log de debug.", "ZMK Companion",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = DebugLog.Path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir el log: {ex.Message}\n\nRuta: {DebugLog.Path}",
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OnAbout()
    {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        MessageBox.Show(
            $"ZMK Companion  v{ver?.Major}.{ver?.Minor}.{ver?.Build}\n\n" +
            "Muestra información personalizada en la pantalla OLED\n" +
            "de tu teclado ZMK vía BLE.\n\n" +
            "github.com/oscampo/zmk-companion",
            "Acerca de ZMK Companion",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // ── Icon generation ───────────────────────────────────────────────────────

    private static Icon MakeIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 13, 13);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
    }
}
