using System.Collections.Concurrent;
using System.Windows.Forms;
using ZmkCompanion.Core;
using ZmkCompanion.Features;
using ZmkCompanion.UI;

namespace ZmkCompanion;

sealed class ZmkAppContext : ApplicationContext
{
    private readonly AppSettings          _settings;
    private readonly BleService           _ble;
    private readonly TrayIcon             _tray;
    private readonly CellGridCompositor   _compositor;
    private readonly PipeServer           _pipe;
    private readonly LiveState            _liveState = new();
    private readonly PomodoroFeature      _pomodoro  = new();

    private readonly CancellationTokenSource _cts = new();

    // Pipe callbacks run on the thread pool; compositor/WinForms timers need STA.
    private readonly ConcurrentQueue<string> _textQueue = new();
    private System.Windows.Forms.Timer? _drainTimer;

    // Display page cycling.
    private int _activePage;
    private CancellationTokenSource? _pageCycleCts;

    // Background pollers feeding LiveState bindings.
    private System.Windows.Forms.Timer? _weatherTimer;
    private System.Windows.Forms.Timer? _sportsTimer;
    private System.Windows.Forms.Timer? _heartbeatTimer;
    private System.Windows.Forms.Timer? _connectionWatchdog;

    // Tracks last pomodoro phase so tray only rebuilds on phase change, not every second.
    private PomodoroPhase _lastTrayPhase = PomodoroPhase.Done;

    public ZmkAppContext()
    {
        DebugLog.Reset();
        _settings   = AppSettings.Load();
        _ble        = new BleService();
        _tray       = new TrayIcon(_ble);
        _compositor = new CellGridCompositor(_ble, _liveState);

        _pipe = new PipeServer();

        _ble.Connected              += OnConnected;
        _ble.Disconnected           += OnDisconnected;
        _ble.BatteryLevelChanged    += OnBatteryLevelChanged;
        _ble.StatusChanged          += OnStatusChanged;
        _tray.ExitRequested         += OnExit;
        _tray.CanvasEditorRequested += OnDisplayEditor;
        _tray.PomodoroToggleRequested += OnPomodoroToggle;

        _pomodoro.StateChanged     += OnPomodoroStateChanged;
        _pomodoro.SessionCompleted += OnPomodoroCompleted;

        Application.Idle += OnFirstIdle;
    }

    private void OnFirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnFirstIdle;
        _ble.SetUiContext(SynchronizationContext.Current!);

        _drainTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _drainTimer.Tick += async (_, _) =>
        {
            while (_textQueue.TryDequeue(out string? text))
            {
                try
                {
                    using var bmp = BitmapTextRenderer.Render(text!);
                    byte[] frame  = BitmapFrame.Pack(bmp);
                    await _compositor.ShowTemporaryAsync(frame, TimeSpan.FromSeconds(30));
                }
                catch { }
            }
        };
        _drainTimer.Start();

        _pipe.Start(text =>
        {
            _liveState.UpdateExternalText(text);
            _textQueue.Enqueue(text);
            return Task.FromResult(true);
        });

        _weatherTimer = new System.Windows.Forms.Timer { Interval = 10 * 60_000 };
        _weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();
        _weatherTimer.Start();
        _ = RefreshWeatherAsync();

        _sportsTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _sportsTimer.Tick += async (_, _) => await RefreshSportsAsync();
        _sportsTimer.Start();
        _ = RefreshSportsAsync();

        _heartbeatTimer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _heartbeatTimer.Tick += (_, _) =>
        {
            if (!_ble.IsConnected) return;
            _ = _compositor.ForceRedrawAsync();
        };
        _heartbeatTimer.Start();

        _connectionWatchdog = new System.Windows.Forms.Timer { Interval = 10_000 };
        _connectionWatchdog.Tick += (_, _) =>
        {
            bool healthy = _ble.IsConnected && _ble.HasCellGridChar;
            if (healthy || _reconnecting) return;
            DebugLog.Log($"watchdog: unhealthy link (IsConnected={_ble.IsConnected} HasCellGridChar={_ble.HasCellGridChar}) — forcing reconnect");
            _reconnecting = true;
            _ = ReconnectAsync();
        };
        _connectionWatchdog.Start();

        _ = ConnectLoopAsync(_cts.Token);
    }

    // ── Live data pollers ─────────────────────────────────────────────────────

    private async Task RefreshWeatherAsync()
    {
        try
        {
            var data = await WeatherFeature.FetchWeatherAsync(_settings.City);
            _liveState.UpdateWeather(data.Icon.ToString(), $"{data.TempC:F0}°", data.City);
            DebugLog.Log($"weather: {data.City} {data.TempC:F0}° wmo={data.Icon}");
        }
        catch (Exception ex) { DebugLog.Log($"weather error: {ex.Message}"); }
    }

    private async Task RefreshSportsAsync()
    {
        var leagues = _settings.SelectedLeagues.Count > 0
            ? _settings.SelectedLeagues.Select(SportsFeature.FindOrCreate).ToList()
            : [SportsFeature.DefaultLeague];

        bool first = true;
        foreach (var lg in leagues)
        {
            try
            {
                var live = await SportsFeature.FetchLiveAsync(lg, _settings.SportsTeam);
                var next = await SportsFeature.FetchScheduleAsync(lg, _settings.SportsTeam);
                var last = await SportsFeature.FetchResultsAsync(lg, _settings.SportsTeam);

                // Primary: live > next scheduled > last result.
                SportsGame? primary = live.Count > 0 ? live[0] : next.Count > 0 ? next[0] : last.Count > 0 ? last[0] : null;
                var snap     = BuildSportsSnapshot(lg, primary);
                var snapNext = BuildSportsSnapshot(lg, next.Count > 0 ? next[0] : null);
                var snapLast = BuildSportsSnapshot(lg, last.Count > 0 ? last[0] : null);

                _liveState.UpdateSports(lg.ShortName,           snap);
                _liveState.UpdateSports(lg.ShortName + "_next", snapNext);
                _liveState.UpdateSports(lg.ShortName + "_last", snapLast);

                // Always update "default*" for the first league so switching leagues
                // never leaves stale data from the previous league in the live state.
                if (first)
                {
                    _liveState.UpdateSports("default",      snap);
                    _liveState.UpdateSports("default_next", snapNext);
                    _liveState.UpdateSports("default_last", snapLast);
                    first = false;
                }
            }
            catch { }
        }
    }

    private SportsSnapshot BuildSportsSnapshot(SportsLeague league, SportsGame? g)
    {
        if (g is null)
            return new SportsSnapshot
            {
                Sport   = league.Sport.ToString(),
                League  = league.ShortName,
                Team    = _settings.SportsTeam,
                Game    = "No games",
                Summary = "No games",
            };

        string game = g.StatusState == "pre"
            ? $"{g.Away} @ {g.Home}"
            : $"{g.Away} {g.AwayScore}-{g.HomeScore} {g.Home}";

        string marker = g.StatusState switch
        {
            "in"   => "", // nf-fa-bolt (live)
            "post" => "", // nf-fa-trophy (final)
            _      => "",
        };

        return new SportsSnapshot
        {
            Sport     = g.Sport.ToString(),
            League    = league.ShortName,
            Team      = string.IsNullOrWhiteSpace(_settings.SportsTeam) ? g.Home : _settings.SportsTeam,
            Away      = g.Away,
            Home      = g.Home,
            Score     = g.StatusState == "post" ? $"{g.AwayScore}-{g.HomeScore}" : "",
            Game      = game,
            Marker    = marker,
            Time      = g.StatusState == "in"  ? g.StatusDetail : "",
            Scheduled = g.StatusState == "pre" ? g.StatusDetail : "",
            Summary   = SportsFeature.FormatGame(g),
        };
    }

    // ── Pomodoro ──────────────────────────────────────────────────────────────

    private void OnPomodoroToggle()
    {
        if (_pomodoro.Phase != PomodoroPhase.Done)
        {
            _pomodoro.Stop();
        }
        else
        {
            var wcfg = FindPomodoroConfig();
            if (wcfg == null) return;
            _pomodoro.Start(wcfg);
        }
    }

    private void OnPomodoroStateChanged()
    {
        var (time, phase, bar, icon, cycle) = _pomodoro.GetDisplayState();
        _liveState.UpdatePomodoro(time, phase, bar, icon, cycle);

        if (_pomodoro.Phase != _lastTrayPhase)
        {
            _lastTrayPhase = _pomodoro.Phase;
            UpdateTrayPomodoro();
        }
    }

    private void OnPomodoroCompleted()
    {
        _lastTrayPhase = PomodoroPhase.Done;
        UpdateTrayPomodoro();
        _tray.ShowBalloonTip(3000, "ZMK Companion", "¡Sesión Pomodoro completada!", ToolTipIcon.Info);
    }

    private void UpdateTrayPomodoro()
    {
        bool hasPomodoro = _settings.DisplayPages.Any(p =>
            p.Rows.Any(r => r.Template.Contains("{pomodoro.", StringComparison.OrdinalIgnoreCase)));
        _tray.HasPomodoroWidget = hasPomodoro;

        if (_pomodoro.Phase != PomodoroPhase.Done)
        {
            string phaseLabel = _pomodoro.Phase switch
            {
                PomodoroPhase.Work      => "Work",
                PomodoroPhase.Break     => "Break",
                PomodoroPhase.LongBreak => "Long Break",
                _                       => "",
            };
            int m = _pomodoro.SecondsRemaining / 60, s = _pomodoro.SecondsRemaining % 60;
            _tray.SetPomodoroState(true, $"Pomodoro  [{phaseLabel} {m:D2}:{s:D2}]");
        }
        else
        {
            _tray.SetPomodoroState(false, null);
        }
    }

    private PomodoroConfig? FindPomodoroConfig()
    {
        foreach (var page in _settings.DisplayPages)
            foreach (var row in page.Rows)
                if (row.Template.Contains("{pomodoro.", StringComparison.OrdinalIgnoreCase))
                    return new PomodoroConfig(); // default timings; editor sets them per-row
        return null;
    }

    // ── Display pages ─────────────────────────────────────────────────────────

    private void LoadPage(int index)
    {
        if (_settings.DisplayPages.Count == 0)
            _settings.DisplayPages.Add(new CellGridPage());
        _activePage = Math.Clamp(index, 0, _settings.DisplayPages.Count - 1);
        var page = _settings.DisplayPages[_activePage];
        DebugLog.Log($"LoadPage({_activePage}) name='{page.Name}' rows={page.Rows.Count} " +
                     $"connected={_ble.IsConnected} now={DateTime.Now:HH:mm:ss.fff}");
        if (_ble.IsConnected)
            _ = _compositor.LoadPageAsync(page);
        UpdateTrayPomodoro();
    }

    private void RestartPageCycle()
    {
        _pageCycleCts?.Cancel();
        _pageCycleCts?.Dispose();
        _pageCycleCts = null;
        DebugLog.Log($"RestartPageCycle: CycleDisplayPages={_settings.CycleDisplayPages} pageCount={_settings.DisplayPages.Count}");
        if (!_settings.CycleDisplayPages || _settings.DisplayPages.Count < 2) return;

        _pageCycleCts = new CancellationTokenSource();
        _ = RunPageCycleAsync(_pageCycleCts.Token);
    }

    private async Task RunPageCycleAsync(CancellationToken ct)
    {
        DebugLog.Log("RunPageCycleAsync: started");
        while (!ct.IsCancellationRequested)
        {
            int durationMs = Math.Max(2, _settings.DisplayPages[_activePage].DurationSeconds) * 1000;
            DebugLog.Log($"cycle: page {_activePage} dwelling {durationMs}ms");
            try   { await Task.Delay(durationMs, ct); }
            catch (OperationCanceledException) { break; }
            if (ct.IsCancellationRequested) break;
            int next = (_activePage + 1) % _settings.DisplayPages.Count;
            DebugLog.Log($"cycle: switching page {_activePage} -> {next}");
            LoadPage(next);
        }
        DebugLog.Log("RunPageCycleAsync: stopped");
    }

    // ── BLE status events ─────────────────────────────────────────────────────

    private void OnBatteryLevelChanged(byte level) => _liveState.UpdateBattery(level, false);

    private void OnStatusChanged(byte status, byte bonds)
    {
        bool usb      = (status & 0x01) != 0;
        int  profile  = (status >> 1) & 0x07;
        int  bondMask = bonds & 0x1F;
        _liveState.UpdateConnection(usb, profile, bondMask);
    }

    // ── Display editor ────────────────────────────────────────────────────────

    private void OnDisplayEditor()
    {
        var form = new CellGridEditorForm(_settings, _liveState, ApplyDisplayPages);
        form.Show();
    }

    internal void ApplyDisplayPages(List<CellGridPage> pages, bool cycle)
    {
        _settings.DisplayPages      = pages;
        _settings.CycleDisplayPages = cycle;
        _settings.Save();
        LoadPage(_activePage < pages.Count ? _activePage : 0);
        RestartPageCycle();
        _ = RefreshWeatherAsync();
        _ = RefreshSportsAsync();
    }

    // ── Connection lifecycle ──────────────────────────────────────────────────

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool found = await _ble.ScanAndConnectAsync(ct);
            if (found || ct.IsCancellationRequested) break;
            await Task.Delay(15_000, ct).ContinueWith(_ => { });
        }
    }

    private void OnConnected(string deviceName)
    {
        DebugLog.Log($"OnConnected: {deviceName} now={DateTime.Now:HH:mm:ss.fff}");
        _tray.SetConnected(deviceName);
        _ = _ble.SendAsync(Protocol.BuildClock());
        LoadPage(_activePage);
        RestartPageCycle();
    }

    private bool _reconnecting;

    private void OnDisconnected()
    {
        DebugLog.Log($"OnDisconnected now={DateTime.Now:HH:mm:ss.fff}");
        _compositor.Stop();
        _pomodoro.Stop();
        _tray.SetDisconnected();

        if (_reconnecting) return;
        _reconnecting = true;
        _ = ReconnectAsync();
    }

    private async Task ReconnectAsync()
    {
        try   { await ConnectLoopAsync(_cts.Token); }
        finally { _reconnecting = false; }
    }

    private void OnExit()
    {
        _cts.Cancel();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _cts.Dispose();
            _drainTimer?.Stop();
            _drainTimer?.Dispose();
            _pageCycleCts?.Cancel();
            _pageCycleCts?.Dispose();
            _weatherTimer?.Stop();
            _weatherTimer?.Dispose();
            _sportsTimer?.Stop();
            _sportsTimer?.Dispose();
            _heartbeatTimer?.Stop();
            _heartbeatTimer?.Dispose();
            _connectionWatchdog?.Stop();
            _connectionWatchdog?.Dispose();
            _pipe.Dispose();
            _pomodoro.Dispose();
            _compositor.Dispose();
            _tray.Dispose();
            _ble.Dispose();
        }
        base.Dispose(disposing);
    }
}
