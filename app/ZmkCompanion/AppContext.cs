using System.Collections.Concurrent;
using ZmkCompanion.Core;
using ZmkCompanion.Features;
using ZmkCompanion.Features.Widgets;
using ZmkCompanion.UI;

namespace ZmkCompanion;

sealed class ZmkAppContext : ApplicationContext
{
    private readonly AppSettings       _settings;
    private readonly BleService        _ble;
    private readonly TrayIcon          _tray;
    private readonly DisplayCompositor _compositor;
    private readonly PipeServer        _pipe;
    private readonly LiveState         _liveState = new();

    private readonly CancellationTokenSource _cts = new();

    // Pipe callbacks run on the thread pool; compositor/WinForms timers need STA.
    private readonly ConcurrentQueue<string> _textQueue = new();
    private System.Windows.Forms.Timer? _drainTimer;

    // Canvas page cycling.
    private int _activePage;
    private System.Windows.Forms.Timer? _pageTimer;

    // Background pollers feeding LiveState bindings ({weather.*}, {sports...}).
    private System.Windows.Forms.Timer? _weatherTimer;
    private System.Windows.Forms.Timer? _sportsTimer;

    // Safety net: forces a redraw periodically so a single silently-failed BLE
    // write (e.g. the clock's once-a-minute tick) can't leave the display
    // stuck on stale content indefinitely.
    private System.Windows.Forms.Timer? _heartbeatTimer;

    public ZmkAppContext()
    {
        _settings = AppSettings.Load();
        _ble      = new BleService();
        _tray     = new TrayIcon(_ble, _settings);

        _compositor = new DisplayCompositor(_ble);
        LoadPage(0);

        _pipe = new PipeServer();

        _ble.Connected              += OnConnected;
        _ble.Disconnected           += OnDisconnected;
        _ble.BatteryLevelChanged    += OnBatteryLevelChanged;
        _ble.StatusChanged          += OnStatusChanged;
        _tray.ExitRequested         += OnExit;
        _tray.CanvasEditorRequested += OnCanvasEditor;

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

        RestartPageCycle();

        // A rare last-resort safety net (e.g. a page with no clock/weather/sports
        // binding, so nothing else ever invalidates it) — not a primary corrective
        // mechanism. Kept infrequent: LiveState.Update* now only fires Changed when
        // a value actually differs, so most polls no longer force a resend, and
        // this heartbeat shouldn't add back the load that caused the clock to fall
        // behind (a full-frame BLE send is comparatively expensive; forcing one
        // every 30s regardless of whether anything changed just queued up work
        // faster than the link could drain it).
        _heartbeatTimer = new System.Windows.Forms.Timer { Interval = 5 * 60_000 };
        _heartbeatTimer.Tick += (_, _) => { if (_ble.IsConnected) _compositor.ForceRedraw(); };
        _heartbeatTimer.Start();

        _ = ConnectLoopAsync(_cts.Token);
    }

    // ── Live data pollers ────────────────────────────────────────────────────

    private async Task RefreshWeatherAsync()
    {
        try
        {
            var data = await WeatherFeature.FetchWeatherAsync(_settings.City);
            _liveState.UpdateWeather(data.Icon.ToString(), $"{data.TempC:F0}°", data.City);
        }
        catch { /* offline / bad city — keep last known value */ }
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
                var games = await SportsFeature.FetchLiveAsync(lg, _settings.SportsTeam);
                if (games.Count == 0) games = await SportsFeature.FetchScheduleAsync(lg, _settings.SportsTeam);
                if (games.Count == 0) games = await SportsFeature.FetchResultsAsync(lg, _settings.SportsTeam);

                var snapshot = BuildSportsSnapshot(lg, games.Count > 0 ? games[0] : null);
                _liveState.UpdateSports(lg.ShortName, snapshot);
                if (first) { _liveState.UpdateSports("default", snapshot); first = false; }
            }
            catch { /* offline — keep last known value for this league */ }
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
            "in"   => "", // nf-fa-bolt (live)
            "post" => "", // nf-fa-trophy (final)
            _      => "",
        };

        return new SportsSnapshot
        {
            Sport     = g.Sport.ToString(),
            League    = league.ShortName,
            Team      = string.IsNullOrWhiteSpace(_settings.SportsTeam) ? g.Home : _settings.SportsTeam,
            Game      = game,
            Marker    = marker,
            Time      = g.StatusState == "in"  ? g.StatusDetail : "",
            Scheduled = g.StatusState == "pre" ? g.StatusDetail : "",
            Summary   = SportsFeature.FormatGame(g),
        };
    }

    // ── Canvas pages ─────────────────────────────────────────────────────────

    private void LoadPage(int index)
    {
        if (_settings.Pages.Count == 0) _settings.Pages.Add(new CanvasPage());
        _activePage = Math.Clamp(index, 0, _settings.Pages.Count - 1);
        _compositor.Rebuild(_settings.Pages[_activePage].Widgets.Select(CreateWidget));
        if (_ble.IsConnected) _compositor.StartAll();
    }

    private void RestartPageCycle()
    {
        _pageTimer?.Stop(); _pageTimer?.Dispose(); _pageTimer = null;
        if (!_settings.CyclePages || _settings.Pages.Count < 2) return;

        _pageTimer = new System.Windows.Forms.Timer();
        _pageTimer.Tick += (_, _) =>
        {
            LoadPage((_activePage + 1) % _settings.Pages.Count);
            _pageTimer!.Interval = Math.Max(2, _settings.Pages[_activePage].DurationSeconds) * 1000;
        };
        _pageTimer.Interval = Math.Max(2, _settings.Pages[_activePage].DurationSeconds) * 1000;
        _pageTimer.Start();
    }

    // ── BLE status events ─────────────────────────────────────────────────────

    // Both handlers are raised on the UI thread by BleService via _uiContext.Post.

    private void OnBatteryLevelChanged(byte level)
    {
        _liveState.UpdateBattery(level, false);
        // Legacy rigid widgets still wired for backwards compat.
        foreach (var w in _compositor.Widgets.OfType<BatteryWidget>())
            w.Update(level, false);
    }

    private void OnStatusChanged(byte status, byte bonds)
    {
        bool usb      = (status & 0x01) != 0;
        int  profile  = (status >> 1) & 0x07;
        int  bondMask = bonds & 0x1F;  // bits 0-4 = profiles 1-5 bonded
        _liveState.UpdateConnection(usb, profile, bondMask);
        // Legacy rigid widgets still wired for backwards compat.
        foreach (var w in _compositor.Widgets.OfType<ConnectionWidget>())
            w.Update(usb, profile);
    }

    // ── Canvas editor ─────────────────────────────────────────────────────────

    private void OnCanvasEditor()
    {
        var form = new CanvasEditorForm(_settings, _liveState, ApplyPages);
        form.Show();
    }

    internal void ApplyPages(List<CanvasPage> pages, bool cyclePages)
    {
        _settings.Pages      = pages;
        _settings.CyclePages = cyclePages;
        _settings.Save();
        LoadPage(_activePage);
        RestartPageCycle();
    }

    private IWidget CreateWidget(WidgetPlacement p)
    {
        var bounds = p.ToRectangle();
        return p.Type switch
        {
            "label"      => new LabelWidget(_liveState)      { Bounds = bounds, Config = p.GetConfig<LabelConfig>() },
            "profilebar" => new ProfileBarWidget(_liveState) { Bounds = bounds, Config = p.GetConfig<ProfileBarConfig>() },
            // Legacy rigid widgets — still supported for saved configs.
            "battery"    => new BatteryWidget    { Bounds = bounds, Config = p.GetConfig<BatteryConfig>() },
            "connection" => new ConnectionWidget { Bounds = bounds, Config = p.GetConfig<ConnectionConfig>() },
            _            => new ClockWidget      { Bounds = bounds, Config = p.GetConfig<ClockConfig>() },
        };
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
        _tray.SetConnected(deviceName);
        _compositor.StartAll();
    }

    private void OnDisconnected()
    {
        _compositor.StopAll();
        _tray.SetDisconnected();
        _ = Task.Delay(10_000).ContinueWith(async _ =>
        {
            if (!_ble.IsConnected && !_cts.IsCancellationRequested)
                await _ble.ScanAndConnectAsync(_cts.Token);
        });
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
            _pageTimer?.Stop();
            _pageTimer?.Dispose();
            _weatherTimer?.Stop();
            _weatherTimer?.Dispose();
            _sportsTimer?.Stop();
            _sportsTimer?.Dispose();
            _heartbeatTimer?.Stop();
            _heartbeatTimer?.Dispose();
            _pipe.Dispose();
            _compositor.Dispose();
            _tray.Dispose();
            _ble.Dispose();
        }
        base.Dispose(disposing);
    }
}
