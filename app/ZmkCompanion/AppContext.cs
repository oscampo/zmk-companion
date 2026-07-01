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

    // Canvas page cycling — a self-pacing async loop rather than a fixed-interval
    // Timer, so a slow BLE link stretches dwell time instead of piling up more
    // page switches than the link can actually drain (see RunPageCycleAsync).
    private int _activePage;
    private CancellationTokenSource? _pageCycleCts;

    // Background pollers feeding LiveState bindings ({weather.*}, {sports...}).
    private System.Windows.Forms.Timer? _weatherTimer;
    private System.Windows.Forms.Timer? _sportsTimer;

    // Safety net: forces a redraw periodically so a single silently-failed BLE
    // write (e.g. the clock's once-a-minute tick) can't leave the display
    // stuck on stale content indefinitely.
    private System.Windows.Forms.Timer? _heartbeatTimer;

    // Clock sync sent over the legacy 0x1524 text characteristic — a ~15-byte
    // write, effectively instant regardless of MTU/page-cycle load. Decoupled
    // from the bitmap/canvas pipeline entirely: firmware that understands this
    // message ("T:<unix>:A|H", see firmware/custom_status_screen.c for the
    // reference free-running-clock implementation) can keep its own clock
    // accurate independent of how backed-up the bitmap send pipeline gets.
    private System.Windows.Forms.Timer? _clockSyncTimer;

    // Debug logs showed long stretches where _ble.IsConnected read false (the
    // bitmap characteristic gone, every send failing with "char is null") but
    // BleService.Disconnected NEVER fired — Windows' ConnectionStatusChanged
    // event apparently doesn't reliably cover every way this link degrades.
    // Since reconnection was entirely event-driven, the app just sat there
    // with a dead link, LoadPage skipping StartAll() every cycle (IsConnected
    // false), and the display frozen forever — which looks exactly like the
    // clock "stopping" rather than drifting. This watchdog checks connection
    // health directly and kicks off a reconnect regardless of whether the
    // event ever tells us to.
    private System.Windows.Forms.Timer? _connectionWatchdog;

    public ZmkAppContext()
    {
        DebugLog.Reset();
        _settings = AppSettings.Load();
        _ble      = new BleService();
        _tray     = new TrayIcon(_ble, _settings);

        _compositor    = new DisplayCompositor(_ble);
        _tray.Compositor = _compositor;
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

        // Unconditional periodic refresh of whatever page is currently active —
        // the same principle the old (pre-canvas) ClockFeature used: a single
        // persistent timer that just resends a fresh frame on a fixed cadence,
        // completely independent of any per-widget state. That's what made the
        // old clock mode reliable — it never depended on a widget's own timer
        // surviving a page switch. LabelWidget's {time} refresh, by contrast,
        // lives INSIDE the widget and gets disposed/recreated every time
        // Rebuild() runs (i.e. every page cycle switch), which is what caused
        // the residual staleness during cycling: the widget only resyncs when
        // its own page becomes active. This heartbeat plugs that gap without
        // needing {time} duplicated on every page or any firmware change —
        // diagnostics confirmed transport is fast (30-50ms/frame) and the real
        // freeze bug was the connection watchdog gap now fixed above, so a
        // short interval here is cheap and safe.
        _heartbeatTimer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _heartbeatTimer.Tick += (_, _) =>
        {
            if (!_ble.IsConnected) return;
            _compositor.ForceRedraw();
        };
        _heartbeatTimer.Start();

        _clockSyncTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _clockSyncTimer.Tick += (_, _) =>
        {
            if (!_ble.IsConnected) return;
            DebugLog.Log($"clockSyncTimer tick now={DateTime.Now:HH:mm:ss.fff}");
            _ = _ble.SendAsync(Protocol.BuildClock());
        };
        _clockSyncTimer.Start();

        _connectionWatchdog = new System.Windows.Forms.Timer { Interval = 10_000 };
        _connectionWatchdog.Tick += (_, _) =>
        {
            bool healthy = _ble.IsConnected && _ble.HasBitmapChar;
            if (healthy || _reconnecting) return;
            DebugLog.Log($"watchdog: unhealthy link (IsConnected={_ble.IsConnected} HasBitmapChar={_ble.HasBitmapChar}) — forcing reconnect");
            _reconnecting = true;
            _ = ReconnectAsync();
        };
        _connectionWatchdog.Start();

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
        var page = _settings.Pages[_activePage];
        DebugLog.Log($"LoadPage({_activePage}) name='{page.Name}' widgets={page.Widgets.Count} " +
                     $"connected={_ble.IsConnected} now={DateTime.Now:HH:mm:ss.fff}");
        _compositor.Rebuild(page.Widgets.Select(CreateWidget));
        if (_ble.IsConnected) _compositor.StartAll();
    }

    private void RestartPageCycle()
    {
        _pageCycleCts?.Cancel();
        _pageCycleCts?.Dispose();
        _pageCycleCts = null;
        DebugLog.Log($"RestartPageCycle: CyclePages={_settings.CyclePages} pageCount={_settings.Pages.Count}");
        if (!_settings.CyclePages || _settings.Pages.Count < 2) return;

        _pageCycleCts = new CancellationTokenSource();
        _ = RunPageCycleAsync(_pageCycleCts.Token);
    }

    // Each iteration: wait for the current page's frame to actually finish
    // transmitting, dwell for its configured DurationSeconds, then switch to
    // the next page. Because the wait happens BEFORE the dwell countdown, a
    // slow BLE send extends how long the page stays on screen instead of the
    // next switch firing on schedule regardless — the previous fixed-interval
    // Timer could queue up page switches faster than frames could actually be
    // sent, and that backlog was very likely why the clock fell behind whenever
    // cycling was on.
    private async Task RunPageCycleAsync(CancellationToken ct)
    {
        DebugLog.Log("RunPageCycleAsync: started");
        while (!ct.IsCancellationRequested)
        {
            var idleSw = System.Diagnostics.Stopwatch.StartNew();
            await _compositor.WaitForIdleAsync();
            int durationMs = Math.Max(2, _settings.Pages[_activePage].DurationSeconds) * 1000;
            DebugLog.Log($"cycle: page {_activePage} idle after {idleSw.ElapsedMilliseconds}ms, dwelling {durationMs}ms");
            try   { await Task.Delay(durationMs, ct); }
            catch (OperationCanceledException) { break; }
            if (ct.IsCancellationRequested) break;
            int next = (_activePage + 1) % _settings.Pages.Count;
            DebugLog.Log($"cycle: switching page {_activePage} -> {next}");
            LoadPage(next);
        }
        DebugLog.Log("RunPageCycleAsync: stopped");
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
        DebugLog.Log($"OnConnected: {deviceName} now={DateTime.Now:HH:mm:ss.fff}");
        _tray.SetConnected(deviceName);
        _compositor.StartAll();
        _ = _ble.SendAsync(Protocol.BuildClock());
    }

    private bool _reconnecting;

    private void OnDisconnected()
    {
        DebugLog.Log($"OnDisconnected now={DateTime.Now:HH:mm:ss.fff} (display frozen until reconnect)");
        _compositor.StopAll();
        _tray.SetDisconnected();

        // The display is frozen (StopAll) for the entire gap until reconnected,
        // so any delay here shows up as apparent "clock drift" if the link ever
        // drops. This used to wait a flat 10s before even trying once — now it
        // reuses the startup retry loop, which attempts immediately and keeps
        // retrying every 15s until reconnected.
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
            _clockSyncTimer?.Stop();
            _clockSyncTimer?.Dispose();
            _connectionWatchdog?.Stop();
            _connectionWatchdog?.Dispose();
            _pipe.Dispose();
            _compositor.Dispose();
            _tray.Dispose();
            _ble.Dispose();
        }
        base.Dispose(disposing);
    }
}
