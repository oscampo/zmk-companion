using System.Drawing;
using System.Windows.Forms;
using ZmkCompanion.Core;
using ZmkCompanion.Features;

namespace ZmkCompanion.UI;

// Owns the NotifyIcon and builds the context menu.
// All mutations must happen on the UI thread (ensured by callers via BleService events).
sealed class TrayIcon : IDisposable
{
    private readonly BleService _ble;
    private readonly AppSettings _settings;
    private readonly NotifyIcon _notify;
    private readonly PomodoroFeature _pomodoro;
    private readonly WeatherFeature _weather;
    private readonly NflFeature _nfl;

    private CancellationTokenSource? _nflCts;

    public event Action? ExitRequested;

    public TrayIcon(BleService ble, AppSettings settings)
    {
        _ble      = ble;
        _settings = settings;
        _pomodoro = new PomodoroFeature(ble);
        _weather  = new WeatherFeature();
        _nfl      = new NflFeature();

        // Initialize _notify first so lambdas below can safely reference it.
        _notify = new NotifyIcon
        {
            Visible = true,
            Text    = "ZMK Companion — searching…",
            Icon    = MakeIcon(Color.OrangeRed),
        };
        _notify.ContextMenuStrip = BuildMenu();
        _notify.DoubleClick += (_, _) => OnSendText();

        _pomodoro.StateChanged     += RebuildMenu;
        _pomodoro.SessionCompleted += () =>
            _notify.ShowBalloonTip(3000, "ZMK Companion", "Pomodoro session done!", ToolTipIcon.Info);
    }

    // ── Connection state ──────────────────────────────────────────────────────

    public void SetConnected(string deviceName)
    {
        _notify.Icon = MakeIcon(Color.LimeGreen);
        _notify.Text = $"ZMK Companion — {deviceName}";
        _notify.ShowBalloonTip(2000, "ZMK Companion", $"Connected to {deviceName}", ToolTipIcon.Info);
        RebuildMenu();
    }

    public void SetDisconnected()
    {
        _nflCts?.Cancel();
        _pomodoro.Stop();
        _notify.Icon = MakeIcon(Color.OrangeRed);
        _notify.Text = "ZMK Companion — disconnected";
        _notify.ShowBalloonTip(2000, "ZMK Companion", "Keyboard disconnected", ToolTipIcon.Warning);
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

        // Header — device name or status
        var header = new ToolStripLabel(_ble.DeviceName is { } name
            ? $"  {name}"
            : "  Not connected")
        { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        strip.Items.Add(header);
        strip.Items.Add(new ToolStripSeparator());

        // ── Modes ────────────────────────────────────────────────────────────
        var itemClock = new ToolStripMenuItem("Clock", null, (_, _) => OnClock())
            { Enabled = connected };
        strip.Items.Add(itemClock);

        var itemWeather = new ToolStripMenuItem("Weather", null, (_, _) => OnWeather())
            { Enabled = connected };
        strip.Items.Add(itemWeather);

        // Pomodoro submenu
        var pomSub = new ToolStripMenuItem("Pomodoro") { Enabled = connected };
        if (_pomodoro.Phase != PomodoroPhase.Done)
        {
            string label = _pomodoro.Phase switch
            {
                PomodoroPhase.Work      => "Work",
                PomodoroPhase.Break     => "Break",
                PomodoroPhase.LongBreak => "Long break",
                _                       => "",
            };
            int m = _pomodoro.SecondsRemaining / 60, s = _pomodoro.SecondsRemaining % 60;
            pomSub.Text = $"Pomodoro  [{label} {m:D2}:{s:D2}]";
            pomSub.DropDownItems.Add(new ToolStripMenuItem("Stop", null, (_, _) => OnPomodoroStop()));
        }
        else
        {
            foreach (string preset in new[] { "classic", "short", "long" })
            {
                string p = preset;
                pomSub.DropDownItems.Add(new ToolStripMenuItem(
                    char.ToUpper(p[0]) + p[1..], null, (_, _) => OnPomodoroStart(p)));
            }
            pomSub.DropDownItems.Add(new ToolStripSeparator());
            pomSub.DropDownItems.Add(new ToolStripMenuItem("Custom…", null, (_, _) => OnPomodoroCustom()));
        }
        strip.Items.Add(pomSub);

        // NFL submenu
        var nflSub = new ToolStripMenuItem("NFL") { Enabled = connected };
        nflSub.DropDownItems.Add(new ToolStripMenuItem("Last week results", null, (_, _) => OnNfl(false)));
        nflSub.DropDownItems.Add(new ToolStripMenuItem("Next week schedule", null, (_, _) => OnNfl(true)));
        if (!string.IsNullOrEmpty(_settings.NflTeam))
        {
            nflSub.DropDownItems.Add(new ToolStripSeparator());
            nflSub.DropDownItems.Add(new ToolStripMenuItem(
                $"My team: {_settings.NflTeam} (results)", null, (_, _) => OnNflTeam(false)));
            nflSub.DropDownItems.Add(new ToolStripMenuItem(
                $"My team: {_settings.NflTeam} (schedule)", null, (_, _) => OnNflTeam(true)));
        }
        strip.Items.Add(nflSub);

        strip.Items.Add(new ToolStripMenuItem("Send text…", null, (_, _) => OnSendText())
            { Enabled = connected });

        strip.Items.Add(new ToolStripSeparator());

        // ── Connection ───────────────────────────────────────────────────────
        if (!connected)
            strip.Items.Add(new ToolStripMenuItem("Reconnect", null, (_, _) => OnReconnect()));
        else
            strip.Items.Add(new ToolStripMenuItem("Disconnect", null, (_, _) => OnDisconnect())
                { Enabled = connected });

        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OnSettings()));
        strip.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke()));
    }

    // ── Menu handlers ─────────────────────────────────────────────────────────

    private void OnClock() =>
        _ = _ble.SendAsync(Protocol.BuildClock());

    private void OnWeather() =>
        _ = Task.Run(async () =>
        {
            try
            {
                var (_, summary) = await _weather.FetchAndSendAsync(_ble, _settings.City);
                _notify.ShowBalloonTip(3000, "Weather", summary, ToolTipIcon.Info);
            }
            catch (Exception ex)
            { _notify.ShowBalloonTip(4000, "Weather error", ex.Message, ToolTipIcon.Error); }
        });

    private void OnPomodoroStart(string preset)
    {
        var cfg = PomodoroConfig.Parse(preset);
        _pomodoro.Start(cfg);
    }

    private void OnPomodoroStop()
    {
        _pomodoro.Stop();
        _ = _ble.SendAsync(Protocol.BuildClock());
        RebuildMenu();
    }

    private void OnPomodoroCustom()
    {
        using var dlg = new TextInputDialog(
            "Custom Pomodoro",
            "Format: work,break,cycles[,long_break]  e.g. 25,5,4,15",
            _settings.PomodoroPreset);
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _settings.PomodoroPreset = dlg.Value;
        _settings.Save();
        OnPomodoroStart(dlg.Value);
    }

    private void OnNfl(bool nextWeek) =>
        _ = Task.Run(async () =>
        {
            _nflCts?.Cancel();
            _nflCts = new CancellationTokenSource();
            var games = nextWeek
                ? await NflFeature.FetchScheduleAsync()
                : await NflFeature.FetchResultsAsync();
            if (games.Count == 0)
                _notify.ShowBalloonTip(3000, "NFL", "No games found.", ToolTipIcon.Info);
            else
                await _nfl.CycleGamesAsync(_ble, games, _nflCts.Token);
        });

    private void OnNflTeam(bool nextWeek) =>
        _ = Task.Run(async () =>
        {
            _nflCts?.Cancel();
            _nflCts = new CancellationTokenSource();
            var games = nextWeek
                ? await NflFeature.FetchScheduleAsync(_settings.NflTeam)
                : await NflFeature.FetchResultsAsync(_settings.NflTeam);
            if (games.Count == 0)
                _notify.ShowBalloonTip(3000, "NFL", $"No games for {_settings.NflTeam}.", ToolTipIcon.Info);
            else
                await _nfl.CycleGamesAsync(_ble, games, _nflCts.Token);
        });

    private void OnSendText()
    {
        using var dlg = new TextInputDialog("Send to keyboard", "Text (up to 3 lines with \\n):", "");
        if (dlg.ShowDialog() != DialogResult.OK) return;
        string text = dlg.Value.Replace("\\n", "\n");
        _ = _ble.SendAsync(text);
    }

    private void OnReconnect() =>
        _ = Task.Run(async () => await _ble.ScanAndConnectAsync());

    private void OnDisconnect() =>
        _ = _ble.SendAsync(Protocol.BuildClear());

    private void OnSettings()
    {
        using var dlg = new SettingsDialog(_settings);
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _settings.Save();
            RebuildMenu();
        }
    }

    // ── Icon generation ───────────────────────────────────────────────────────

    // Creates a simple 16x16 filled circle icon in the given color.
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
        _nflCts?.Cancel();
        _nflCts?.Dispose();
        _pomodoro.Dispose();
        _notify.Visible = false;
        _notify.Dispose();
    }
}
