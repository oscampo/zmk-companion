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
    public event Action? CustomTokensRequested;
    public event Action? PomodoroToggleRequested;
    public event Action? PomodoroConfigRequested;
    public event Action? ManualReconnectRequested;
    public event Action? ManualDisconnectRequested;
    public event Action<AppLanguage>? LanguageChangeRequested;

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
            Text    = Strings.Searching,
            Icon    = MakeIcon(Color.OrangeRed),
        };
        _notify.ContextMenuStrip = BuildMenu();
        Strings.LanguageChanged += RebuildMenu;
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
        _notify.Text = Strings.ConnectedTo(deviceName);
        _notify.ShowBalloonTip(2000, "ZMK Companion", Strings.ConnectedBalloon(deviceName), ToolTipIcon.Info);
        RebuildMenu();
    }

    public void SetDisconnected()
    {
        _notify.Icon = MakeIcon(Color.OrangeRed);
        _notify.Text = Strings.DisconnectedTray;
        _notify.ShowBalloonTip(2000, "ZMK Companion", Strings.KeyboardDisconnected, ToolTipIcon.Warning);
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
            : Strings.NotConnected)
        { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        strip.Items.Add(header);
        strip.Items.Add(new ToolStripSeparator());

        strip.Items.Add(new ToolStripMenuItem(Strings.CanvasMenu, null, (_, _) => CanvasEditorRequested?.Invoke())
            { Enabled = connected });
        // No BLE connection needed - this just declares names/categories for the
        // {custom.NAME} picker, values only ever come from `zkc --set` later.
        strip.Items.Add(new ToolStripMenuItem(Strings.CustomTokensMenu, null,
            (_, _) => CustomTokensRequested?.Invoke()));

        strip.Items.Add(new ToolStripSeparator());

        string pomText = _pomodoroRunning
            ? (_pomodoroLabel ?? Strings.PomodoroStop)
            : Strings.PomodoroStart;
        strip.Items.Add(new ToolStripMenuItem(pomText, null, (_, _) => PomodoroToggleRequested?.Invoke())
            { Enabled = connected && HasPomodoroWidget });
        strip.Items.Add(new ToolStripMenuItem(Strings.PomodoroConfigMenu, null,
            (_, _) => PomodoroConfigRequested?.Invoke()));

        strip.Items.Add(new ToolStripSeparator());

        if (!connected)
            strip.Items.Add(new ToolStripMenuItem(Strings.Reconnect, null, (_, _) => ManualReconnectRequested?.Invoke()));
        else
            strip.Items.Add(new ToolStripMenuItem(Strings.Disconnect, null, (_, _) => ManualDisconnectRequested?.Invoke()));

        strip.Items.Add(new ToolStripSeparator());

        var langMenu = new ToolStripMenuItem(Strings.LanguageMenu);
        langMenu.DropDownItems.Add(new ToolStripMenuItem("Español", null, (_, _) => OnLanguageSelected(AppLanguage.Es))
            { Checked = Strings.Current == AppLanguage.Es });
        langMenu.DropDownItems.Add(new ToolStripMenuItem("English", null, (_, _) => OnLanguageSelected(AppLanguage.En))
            { Checked = Strings.Current == AppLanguage.En });
        strip.Items.Add(langMenu);

        strip.Items.Add(new ToolStripSeparator());

        strip.Items.Add(new ToolStripMenuItem(Strings.DebugLogMenu, null, (_, _) => OnDebugLog()));
        strip.Items.Add(new ToolStripMenuItem(Strings.AboutMenu, null, (_, _) => OnAbout()));
        strip.Items.Add(new ToolStripMenuItem(Strings.ExitMenu, null, (_, _) => ExitRequested?.Invoke()));
    }

    private void OnLanguageSelected(AppLanguage lang)
    {
        Strings.SetLanguage(lang); // RebuildMenu fires via LanguageChanged subscription
        LanguageChangeRequested?.Invoke(lang);
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private void OnDebugLog()
    {
        try
        {
            if (!File.Exists(DebugLog.Path))
            {
                MessageBox.Show(Strings.NoDebugLogYet, "ZMK Companion",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = DebugLog.Path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Strings.CouldNotOpenLog(ex.Message, DebugLog.Path),
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OnAbout()
    {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        MessageBox.Show(
            Strings.AboutBody(ver?.Major ?? 0, ver?.Minor ?? 0, ver?.Build ?? 0),
            Strings.AboutTitle,
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
        Strings.LanguageChanged -= RebuildMenu;
        _notify.Visible = false;
        _notify.Dispose();
    }
}
