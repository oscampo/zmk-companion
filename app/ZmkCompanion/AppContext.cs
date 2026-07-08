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
    // Named {custom.NAME} channel updates from `zkc --set`, same thread-handoff
    // reason as _textQueue: LiveState.UpdateCustom must only run on the UI thread.
    private readonly ConcurrentQueue<(string Name, string Value)> _customQueue = new();
    private System.Windows.Forms.Timer? _drainTimer;
    private bool _overrideInFlight; // guard: only one bitmap send at a time

    // Display page cycling.
    private int _activePage;
    private CancellationTokenSource? _pageCycleCts;

    // Background pollers feeding LiveState bindings.
    private System.Windows.Forms.Timer? _weatherTimer;
    private System.Windows.Forms.Timer? _sportsTimer;
    private System.Windows.Forms.Timer? _heartbeatTimer;
    private System.Windows.Forms.Timer? _connectionWatchdog;

    // Custom-token staleness balloons (see CustomTokenDef.StaleAfterSeconds).
    // _staleWarned tracks which names already got a balloon for the CURRENT
    // stale episode, so re-checking every tick doesn't repeat it every 30s;
    // cleared once fresh data arrives again so a future episode re-warns.
    private System.Windows.Forms.Timer? _staleCheckTimer;
    private readonly HashSet<string> _staleWarned = new(StringComparer.OrdinalIgnoreCase);

    // Tracks last pomodoro phase so tray only rebuilds on phase change, not every second.
    private PomodoroPhase _lastTrayPhase = PomodoroPhase.Done;

    public ZmkAppContext()
    {
        DebugLog.Reset();
        _settings   = AppSettings.Load();
        Strings.SetLanguage(_settings.Language == "en" ? AppLanguage.En : AppLanguage.Es);
        _ble        = new BleService();
        _tray       = new TrayIcon(_ble);
        _compositor = new CellGridCompositor(_ble, _liveState);

        _pipe = new PipeServer();

        _ble.Connected              += OnConnected;
        _ble.Disconnected           += OnDisconnected;
        _ble.BatteryLevelChanged    += OnBatteryLevelChanged;
        _ble.StatusChanged          += OnStatusChanged;
        _tray.ExitRequested           += OnExit;
        _tray.CanvasEditorRequested   += OnDisplayEditor;
        _tray.CustomTokensRequested   += OnCustomTokens;
        _tray.PomodoroToggleRequested += OnPomodoroToggle;
        _tray.PomodoroConfigRequested += OnPomodoroConfig;
        _tray.ManualReconnectRequested += OnManualReconnect;
        _tray.ManualDisconnectRequested += OnManualDisconnect;
        _tray.LanguageChangeRequested += OnLanguageChanged;

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
            // Custom named tokens are unrelated to the bitmap-override machinery
            // below (no full-screen mode, no page routing - they're just plain
            // LiveState values like {weather.temp}), so this runs unconditionally,
            // not gated by _overrideInFlight. Coalesce to the latest value per
            // name in case several arrived since the last tick.
            if (!_customQueue.IsEmpty)
            {
                var latestByName = new Dictionary<string, string>();
                while (_customQueue.TryDequeue(out var kv)) latestByName[kv.Name] = kv.Value;
                foreach (var (name, value) in latestByName)
                    _liveState.UpdateCustom(name, value);
            }

            if (_overrideInFlight) return; // previous BLE send still in progress — skip

            // Keep only the latest queued item; discard older ones (stale clock frames, etc.)
            string? latest = null;
            int skipped = -1;
            while (_textQueue.TryDequeue(out string? t)) { latest = t; skipped++; }
            if (latest is null) return;

            _overrideInFlight = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (string.IsNullOrEmpty(latest))
                {
                    _liveState.UpdateExternalText(""); // clear before restore so cell-grid renders fresh state
                    DebugLog.Log($"drain: CLEAR (skipped={skipped})");
                    await _compositor.ClearTextOverrideAsync();
                    DebugLog.Log($"drain: CLEAR done in {sw.ElapsedMilliseconds}ms err={_ble.LastBitmapError ?? "(none)"}");
                }
                else if (_compositor.TextMode == ExternalTextMode.None)
                {
                    // Active page references neither {ext.text} nor {ext.text.N}:
                    // it doesn't want CLI text at all. Drop it, leave the page's
                    // own content (weather, sports, whatever) untouched. Deliberately
                    // not calling UpdateExternalText here either, so a later page
                    // that does use {ext.text.N} shows the last value that was
                    // actually displayed somewhere, not whatever arrived while it
                    // was being ignored.
                    DebugLog.Log($"drain: SEND ignored (active page has no ext.text token) len={latest.Length} skipped={skipped}");
                }
                else if (_compositor.TextMode == ExternalTextMode.CellGrid)
                {
                    // Active page has an {ext.text.N} row: route straight to the
                    // cell-grid (positioned), never the full-frame bitmap override.
                    // UpdateExternalText raises Changed, which OnStateChanged picks
                    // up normally since _textOverride was never set for this path.
                    string text = _liveState.ExpandEscaped(latest);
                    DebugLog.Log($"drain: SEND (cell-grid ext.text.N) len={latest.Length} skipped={skipped}");
                    _liveState.UpdateExternalText(text);
                    DebugLog.Log($"drain: SEND done in {sw.ElapsedMilliseconds}ms (cell-grid path)");
                }
                else // ExternalTextMode.FullScreen
                {
                    string text = _liveState.ExpandEscaped(latest);
                    DebugLog.Log($"drain: SEND len={latest.Length} skipped={skipped}");
                    var pages = BitmapTextRenderer.RenderPages(text);
                    var frames = pages.Select(p => (Frame: BitmapFrame.Pack(p.Bitmap), p.CharCount)).ToList();
                    foreach (var (pageBmp, _) in pages) pageBmp.Dispose();
                    await _compositor.ShowPersistentTextAsync(frames, preferSpeed: true);
                    _liveState.UpdateExternalText(text); // after _textOverride=true: Changed fires on UI thread, OnStateChanged exits early
                    DebugLog.Log($"drain: SEND done in {sw.ElapsedMilliseconds}ms " +
                        $"bleMs={_ble.LastSendMs} chunks={_ble.LastChunkCount} " +
                        $"mtu={_ble.LastMtu} withResp={_ble.LastWithResponse} " +
                        $"err={_ble.LastBitmapError ?? "(none)"}");
                }
            }
            catch (Exception ex) { DebugLog.Log($"drain: exception {ex.Message}"); }
            finally { _overrideInFlight = false; }
        };
        _drainTimer.Start();

        _pipe.Start(text =>
        {
            // UpdateExternalText is called on the UI thread inside the drain timer (after
            // _textOverride=true) to avoid triggering cell-grid DrainAsync concurrently with
            // the bitmap send, which would saturate the BLE queue on the first streaming frame.
            //
            // SignalTextIncoming, in contrast, runs right here on the pipe thread — ahead of
            // the drain timer's own WM_TIMER tick, which a busy heartbeat redraw can starve for
            // seconds. It lets an in-progress cell-grid render abort immediately.
            //
            // Only signal when the drain timer will actually take the bitmap-override
            // branch (TextMode.FullScreen). For CellGrid, setting _overridePending
            // here would abort the very render we want. For None, the text is
            // dropped entirely and nothing would ever clear the flag back to false.
            if (!string.IsNullOrEmpty(text) && _compositor.TextMode == ExternalTextMode.FullScreen)
                _compositor.SignalTextIncoming();
            _textQueue.Enqueue(text);
            return Task.FromResult(true);
        },
        (name, value) =>
        {
            // Just a queue enqueue here too (pipe thread); _liveState.UpdateCustom
            // itself only ever runs from the drain timer, on the UI thread.
            _customQueue.Enqueue((name, value));
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
            if (healthy || _reconnecting || _manualDisconnect) return;
            DebugLog.Log($"watchdog: unhealthy link (IsConnected={_ble.IsConnected} HasCellGridChar={_ble.HasCellGridChar}) — forcing reconnect");
            _reconnecting = true;
            _ = ReconnectAsync();
        };
        _connectionWatchdog.Start();

        // 30s resolution is coarse relative to typical thresholds (minutes),
        // fine enough to not matter, cheap enough to just always run.
        _staleCheckTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _staleCheckTimer.Tick += (_, _) => CheckStaleCustomTokens();
        _staleCheckTimer.Start();

        _ = ConnectLoopAsync(_cts.Token);
    }

    // ── Custom token staleness ────────────────────────────────────────────────

    private void CheckStaleCustomTokens()
    {
        foreach (var def in _settings.CustomTokens)
        {
            if (def.StaleAfterSeconds <= 0) continue; // staleness check disabled for this token
            if (!_liveState.TryGetCustomAge(def.Name, out var age)) continue; // never SET yet, not stale

            bool isStale = age.TotalSeconds > def.StaleAfterSeconds;
            if (isStale)
            {
                if (_staleWarned.Add(def.Name)) // first tick past threshold for this episode
                    _tray.ShowError(Strings.StaleTokenTitle,
                        Strings.StaleTokenBody(def.Name, FormatAge(age)));
            }
            else
            {
                _staleWarned.Remove(def.Name); // fresh again - next time it goes stale, warn again
            }
        }
    }

    private static string FormatAge(TimeSpan age) =>
        age.TotalMinutes < 1 ? $"{(int)age.TotalSeconds}s"
        : age.TotalHours  < 1 ? $"{(int)age.TotalMinutes}min"
        : $"{(int)age.TotalHours}h";

    // ── Live data pollers ─────────────────────────────────────────────────────

    private async Task RefreshWeatherAsync()
    {
        var cities = _settings.WeatherCities.Count > 0 ? _settings.WeatherCities : [""];
        bool first = true;
        foreach (var city in cities)
        {
            try
            {
                var data = await WeatherFeature.FetchWeatherAsync(city);
                string tempStr = _settings.WeatherUnit == "fahrenheit"
                    ? $"{data.TempC * 9 / 5 + 32:F0}°F"
                    : $"{data.TempC:F0}°";
                string key = string.IsNullOrWhiteSpace(city) ? "default" : city;
                _liveState.UpdateWeather(key, data.Icon.ToString(), tempStr, data.City);
                // Always update "default" for the first configured city so
                // switching cities never leaves stale data behind (same reasoning
                // as RefreshSportsAsync's "default*" double-keying).
                if (first)
                {
                    _liveState.UpdateWeather("default", data.Icon.ToString(), tempStr, data.City);
                    first = false;
                }
                DebugLog.Log($"weather[{key}]: {data.City} {tempStr} wmo={data.Icon}");
            }
            catch (Exception ex) { DebugLog.Log($"weather error ({city}): {ex.Message}"); }
        }
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
                string team = _settings.SportsTeams.TryGetValue(lg.EspnPath, out var t) ? t : "";
                var live = await SportsFeature.FetchLiveAsync(lg, team);
                var next = await SportsFeature.FetchScheduleAsync(lg, team);
                var last = await SportsFeature.FetchResultsAsync(lg, team);

                // Primary: live > next scheduled > last result.
                SportsGame? primary = live.Count > 0 ? live[0] : next.Count > 0 ? next[0] : last.Count > 0 ? last[0] : null;
                var snap     = BuildSportsSnapshot(lg, primary,                          team);
                var snapNext = BuildSportsSnapshot(lg, next.Count > 0 ? next[0] : null, team);
                var snapLast = BuildSportsSnapshot(lg, last.Count > 0 ? last[0] : null, team);

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

    private static SportsSnapshot BuildSportsSnapshot(SportsLeague league, SportsGame? g, string team = "")
    {
        if (g is null)
            return new SportsSnapshot
            {
                Sport    = league.Sport.ToString(),
                League   = league.ShortName,
                Team     = team,
                LiveGame = "No games",
                Summary  = "No games",
            };

        bool isLive = g.StatusState == "in";

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
            Team      = string.IsNullOrWhiteSpace(team) ? g.Home : team,
            Away      = g.Away,
            Home      = g.Home,
            Score     = g.StatusState == "post" ? $"{g.AwayScore}-{g.HomeScore}" : "",
            Marker    = marker,
            Scheduled = g.StatusState == "pre" ? g.StatusDetail : "",
            LiveGame  = isLive ? $"{g.Home} {g.Away}" : "No games",
            LiveScore = isLive ? $"{g.HomeScore} - {g.AwayScore}" : "",
            LiveTime  = isLive ? g.StatusDetail : "",
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

    private void OnPomodoroConfig()
    {
        using var dlg = new PomodoroConfigDialog(
            _settings.PomodoroWorkMin,
            _settings.PomodoroBreakMin,
            _settings.PomodoroCycles,
            _settings.PomodoroLongBreakMin,
            _settings.PomodoroWorkIcon,
            _settings.PomodoroBreakIcon,
            _settings.PomodoroLongIcon);

        if (dlg.ShowDialog() != DialogResult.OK) return;

        _settings.PomodoroWorkMin      = dlg.WorkMin;
        _settings.PomodoroBreakMin     = dlg.BreakMin;
        _settings.PomodoroCycles       = dlg.Cycles;
        _settings.PomodoroLongBreakMin = dlg.LongBreakMin;
        _settings.PomodoroWorkIcon     = dlg.WorkIcon;
        _settings.PomodoroBreakIcon    = dlg.BreakIcon;
        _settings.PomodoroLongIcon     = dlg.LongIcon;
        _settings.Save();

        // If a session is in progress, restart it with the new config immediately.
        if (_pomodoro.Phase != PomodoroPhase.Done)
        {
            _pomodoro.Start(new Features.PomodoroConfig
            {
                WorkMin      = dlg.WorkMin,
                BreakMin     = dlg.BreakMin,
                Cycles       = dlg.Cycles,
                LongBreakMin = dlg.LongBreakMin,
                WorkIcon     = dlg.WorkIcon,
                BreakIcon    = dlg.BreakIcon,
                LongIcon     = dlg.LongIcon,
            });
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
        _tray.ShowBalloonTip(3000, "ZMK Companion", Strings.PomodoroCompletedBalloon, ToolTipIcon.Info);
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
                PomodoroPhase.Work      => Strings.PomodoroPhaseWork,
                PomodoroPhase.Break     => Strings.PomodoroPhaseBreak,
                PomodoroPhase.LongBreak => Strings.PomodoroPhaseLongBreak,
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
                    return new PomodoroConfig
                    {
                        WorkMin      = _settings.PomodoroWorkMin,
                        BreakMin     = _settings.PomodoroBreakMin,
                        Cycles       = _settings.PomodoroCycles,
                        LongBreakMin = _settings.PomodoroLongBreakMin,
                        WorkIcon     = _settings.PomodoroWorkIcon,
                        BreakIcon    = _settings.PomodoroBreakIcon,
                        LongIcon     = _settings.PomodoroLongIcon,
                    };
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

    private void OnStatusChanged(byte status, byte bonds, byte layer, byte wpm, string layerName)
    {
        bool usb      = (status & 0x01) != 0;
        int  profile  = (status >> 1) & 0x07;
        int  bondMask = bonds & 0x1F;
        _liveState.UpdateConnection(usb, profile, bondMask);
        // 0xFF sentinel = firmware doesn't send byte 2/3 yet (not reflashed
        // with the layer/WPM additions to 0x1526); -1 = "unknown", same
        // convention as BatteryLevel/BleProfile before their first reading.
        // wpm is passed through raw, deliberately no "hold last nonzero"
        // smoothing (see custom_status_screen.c's build_status_bytes comment
        // for why, ZMK's own decay-to-0-when-idle is a real signal we want).
        _liveState.UpdateLayer(layer == 0xFF ? -1 : layer);
        _liveState.UpdateLayerName(layerName);
        _liveState.UpdateWpm(wpm == 0xFF ? -1 : wpm);
    }

    // ── Display editor ────────────────────────────────────────────────────────

    private void OnDisplayEditor()
    {
        var form = new CellGridEditorForm(_settings, _liveState, ApplyDisplayPages);
        form.Show();
    }

    private void OnCustomTokens()
    {
        using var dlg = new CustomTokensForm(_settings.CustomTokens);
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _settings.CustomTokens = dlg.Tokens.ToList();
        _settings.Save();
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
        _manualDisconnect = false; // a fresh connection means any prior "Desconectar" intent is moot
        _tray.SetConnected(deviceName);
        _ = _ble.SendAsync(Protocol.BuildClock());
        LoadPage(_activePage);
        RestartPageCycle();
    }

    private bool _reconnecting;
    // Set by "Desconectar" so OnDisconnected/the watchdog don't immediately
    // undo it by auto-reconnecting a fraction of a second later. Cleared by
    // "Reconectar" (explicit override) or a fresh successful connection
    // (OnConnected), so a FUTURE unexpected drop still auto-reconnects
    // normally, this only suppresses the one the user just asked for.
    private bool _manualDisconnect;

    private void OnDisconnected()
    {
        DebugLog.Log($"OnDisconnected now={DateTime.Now:HH:mm:ss.fff}");
        _compositor.Stop();
        _pomodoro.Stop();
        _tray.SetDisconnected();

        if (_reconnecting || _manualDisconnect) return;
        _reconnecting = true;
        _ = ReconnectAsync();
    }

    private async Task ReconnectAsync()
    {
        try   { await ConnectLoopAsync(_cts.Token); }
        finally { _reconnecting = false; }
    }

    // "Reconectar" tray menu item. Previously called _ble.ScanAndConnectAsync()
    // directly (bypassing _reconnecting entirely), which raced against
    // whatever background retry loop OnDisconnected/the watchdog had already
    // started (ConnectToDeviceAsync's first step is DisposeDevice(), tearing
    // down state the other in-flight attempt was still using), and gave no
    // feedback either way, so a click looked like it did nothing regardless
    // of whether it silently failed or just lost the race. Routed through the
    // same guard now: at most one reconnect attempt in flight, and every
    // click gets a visible response instead of quietly no-op'ing.
    private void OnManualReconnect()
    {
        _manualDisconnect = false; // explicit override of a prior "Desconectar"
        if (_reconnecting)
        {
            _tray.ShowBalloonTip(2000, "ZMK Companion", Strings.AlreadySearchingBalloon, ToolTipIcon.Info);
            return;
        }
        _tray.ShowBalloonTip(2000, "ZMK Companion", Strings.SearchingBalloon, ToolTipIcon.Info);
        _reconnecting = true;
        _ = ReconnectAsync();
    }

    // "Desconectar" tray menu item. Sets _manualDisconnect before calling
    // _ble.Disconnect() so OnDisconnected (which Disconnect() triggers, same
    // as an unexpected drop) sees it and skips auto-reconnecting.
    private void OnManualDisconnect()
    {
        _manualDisconnect = true;
        _ble.Disconnect();
    }

    // "Idioma" tray submenu. Strings.SetLanguage already fired inside TrayIcon
    // (it owns the rebuild), this just persists the choice across restarts.
    private void OnLanguageChanged(AppLanguage lang)
    {
        _settings.Language = lang == AppLanguage.En ? "en" : "es";
        _settings.Save();
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
