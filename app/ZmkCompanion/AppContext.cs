using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
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
    private readonly HotkeyManager        _hotkeys   = new();

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
        _tray.HelpRequested += () => ShowWelcome(force: true);
        _tray.ExportSettingsRequested += OnExportSettings;
        _tray.ImportSettingsRequested += OnImportSettings;
        _tray.CheckUpdatesRequested += () => _ = CheckForUpdatesAsync(manual: true);
        _hotkeys.PageRequested += OnPageHotkey;

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
                    _compositor.ClearFullScreenCache(); // don't let a page cycling back later resurrect this
                    DebugLog.Log($"drain: CLEAR (skipped={skipped})");
                    await _compositor.ClearTextOverrideAsync();
                    DebugLog.Log($"drain: CLEAR done in {sw.ElapsedMilliseconds}ms err={_ble.LastBitmapError ?? "(none)"}");
                }
                else if (_compositor.TextMode != ExternalTextMode.FullScreen)
                {
                    // TextMode is CellGrid (active page has an {ext.text.N} row) or
                    // None (active page references neither token). Store the value
                    // either way, same as UpdateCustom already does unconditionally
                    // for {custom.NAME} above: a different page elsewhere in the
                    // cycle may use {ext.text.N}, and it should show this text next
                    // time it's active rather than only if this exact page happened
                    // to be the one on screen when the text arrived. UpdateExternalText
                    // raises Changed, which OnStateChanged picks up normally (harmless
                    // no-op re-render of whatever the active page's own tokens are if
                    // it doesn't reference ext.text.N itself).
                    string text = _liveState.ExpandEscaped(latest);
                    DebugLog.Log($"drain: SEND (cell-grid ext.text.N, mode={_compositor.TextMode}) len={latest.Length} skipped={skipped}");
                    _liveState.UpdateExternalText(text);

                    // The active page doesn't want a FullScreen override right now,
                    // but a different configured page might (the "frase del dia" case:
                    // text arrives while Reloj/Clima/whatever is on screen). Keep that
                    // page's bitmap warm so LoadPageAsync's cache hit fires once it's
                    // that page's turn, instead of falling through to a raw, tier-clipped
                    // render of the ExternalText we just stored above. Only the first
                    // match is used, deliberately, {custom.NAME} is the recommended tool
                    // for more than one independent FullScreen page, see user_guide.md.
                    int fsPageIndex = _settings.DisplayPages.FindIndex(p =>
                        CellGridCompositor.ModeFor(p.Rows) == ExternalTextMode.FullScreen);
                    if (fsPageIndex >= 0)
                    {
                        var fsPages  = BitmapTextRenderer.RenderPages(text);
                        var fsFrames = fsPages.Select(p => (Frame: BitmapFrame.Pack(p.Bitmap), p.CharCount)).ToList();
                        foreach (var (pageBmp, _) in fsPages) pageBmp.Dispose();
                        _compositor.CacheFullScreenFrames(fsPageIndex, fsFrames);
                        DebugLog.Log($"drain: pre-cached FullScreen bitmap for page {fsPageIndex} (not the active page)");
                    }

                    DebugLog.Log($"drain: SEND done in {sw.ElapsedMilliseconds}ms (cell-grid path)");
                }
                else // ExternalTextMode.FullScreen
                {
                    string text = _liveState.ExpandEscaped(latest);
                    DebugLog.Log($"drain: SEND len={latest.Length} skipped={skipped}");
                    // Isolated from the overall `sw` below to tell GDI+ render cost (this
                    // process, synchronous, runs before the first await) apart from BLE
                    // cost (bleMs, already logged separately) - see whether dense Unicode
                    // glyphs (block/box-drawing chars) trigger slow font-fallback in
                    // RenderPages/Pack vs. plain ASCII text.
                    var renderSw = System.Diagnostics.Stopwatch.StartNew();
                    var pages = BitmapTextRenderer.RenderPages(text);
                    var frames = pages.Select(p => (Frame: BitmapFrame.Pack(p.Bitmap), p.CharCount)).ToList();
                    foreach (var (pageBmp, _) in pages) pageBmp.Dispose();
                    long renderMs = renderSw.ElapsedMilliseconds;
                    await _compositor.ShowPersistentTextAsync(frames, preferSpeed: true, cachePageIndex: _activePage);
                    _liveState.UpdateExternalText(text); // after _textOverride=true: Changed fires on UI thread, OnStateChanged exits early
                    DebugLog.Log($"drain: SEND done in {sw.ElapsedMilliseconds}ms " +
                        $"renderMs={renderMs} bleMs={_ble.LastSendMs} chunks={_ble.LastChunkCount} " +
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

        AutoStartManager.LaunchAll(_settings.AutoStartEntries);
        RegisterPageHotkeys();
        _ = CheckForUpdatesAsync(manual: false);

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

            // A long outage (keyboard taken out of range overnight, PC left running) has
            // nothing left to gain from ticking the page cycle every few seconds - the
            // compositor already stops sending on disconnect, this just stops the cycle
            // from advancing _activePage in the background too. OnConnected's own
            // RestartPageCycle() resumes it normally once reconnected, no extra state to
            // restore here. Doesn't touch the actual reconnect congestion (that's the BLE
            // stack settling after a resume/unlock, out of this app's control), only avoids
            // pointless background work during a known-long disconnection.
            if (!healthy && _pageCycleCts is not null &&
                _ble.LastDisconnectAt is { } disconnectedSince &&
                DateTime.Now - disconnectedSince > TimeSpan.FromMinutes(5))
            {
                DebugLog.Log("watchdog: disconnected >5min, pausing page cycle");
                _pageCycleCts.Cancel();
                _pageCycleCts.Dispose();
                _pageCycleCts = null;
            }

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

        ShowWelcome(force: false);
    }

    // ── Welcome / help screen ────────────────────────────────────────────────

    private static string CurrentVersionString
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return $"{v?.Major ?? 0}.{v?.Minor ?? 0}.{v?.Build ?? 0}";
        }
    }

    // force=false (startup): only shows if the user hasn't dismissed this
    // exact version yet, so bumping <Version> in the .csproj on a release
    // re-surfaces it, nothing else triggers that automatically.
    // force=true (tray "Help…"): always shows, regardless of dismissal.
    private void ShowWelcome(bool force)
    {
        if (!force && _settings.WelcomeDismissedVersion == CurrentVersionString) return;

        using var form = new WelcomeForm();
        form.ShowDialog();
        if (form.DontShowAgain)
        {
            _settings.WelcomeDismissedVersion = CurrentVersionString;
            _settings.Save();
        }
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
                // "GB,KC" → independent per-team feeds, plus the league-wide (unfiltered) feed
                // under target "". One paginated walk per league resolves every target together
                // (see SportsFeature.FetchWeekBasedMultiAsync) instead of one walk per team.
                string teamsCsv = _settings.SportsTeams.TryGetValue(lg.EspnPath, out var t) ? t : "";
                var teams = teamsCsv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.ToUpper())
                    .Distinct()
                    .ToList();

                var live = await SportsFeature.FetchLiveMultiAsync(lg, teams);
                var next = await SportsFeature.FetchScheduleMultiAsync(lg, teams);
                var last = await SportsFeature.FetchResultsMultiAsync(lg, teams);

                foreach (var target in new[] { "" }.Concat(teams))
                {
                    // Primary: live > next scheduled > last result.
                    SportsGame? primary = live.GetValueOrDefault(target)
                        ?? next.GetValueOrDefault(target)
                        ?? last.GetValueOrDefault(target);

                    var snap     = BuildSportsSnapshot(lg, primary,                        target);
                    var snapNext = BuildSportsSnapshot(lg, next.GetValueOrDefault(target), target);
                    var snapLast = BuildSportsSnapshot(lg, last.GetValueOrDefault(target), target);

                    // "" → {sports.*:NFL} (league-wide). "GB" → {sports.*:NFL.GB} (team-specific).
                    string key = target.Length == 0 ? lg.ShortName : $"{lg.ShortName}.{target}";
                    _liveState.UpdateSports(key,           snap);
                    _liveState.UpdateSports(key + "_next", snapNext);
                    _liveState.UpdateSports(key + "_last", snapLast);

                    // "default*" mirrors the first league's unfiltered (league-wide) feed, so an
                    // unqualified {sports.*} token means exactly the same thing as {sports.*:<liga>}
                    // for the first configured league.
                    if (first && target.Length == 0)
                    {
                        _liveState.UpdateSports("default",      snap);
                        _liveState.UpdateSports("default_next", snapNext);
                        _liveState.UpdateSports("default_last", snapLast);
                    }
                }
                first = false;
            }
            catch (Exception ex)
            {
                DebugLog.Log($"sports[{lg.ShortName}] RefreshSportsAsync failed: {ex.GetType().Name} {ex.Message}");
            }
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
            _ = _compositor.LoadPageAsync(page, _activePage);
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
        DebugLog.Log($"OnStatusChanged: usb={usb} profile={profile} bondMask={bondMask:X2} " +
            $"layer={layer} wpm={wpm} layerName='{layerName}'");
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

    // manual=false (startup): silent balloon only, and only for an update the
    // user hasn't already been shown (UpdateCheckDismissedVersion). If the
    // check fails outright (offline, proxy, GitHub rate limit) it just says
    // nothing, a background check has no one waiting on it. manual=true (tray
    // menu "Check for updates…"): a MessageBox, not a balloon, always shows
    // something, an update available (bypassing the "already shown" gate,
    // asked for right now), up to date, or that the check itself failed.
    // A user who clicked and asked needs a guaranteed answer either way, a
    // balloon that silently doesn't render isn't one - confirmed on a real
    // v1.0.1 install: Connect/Disconnect balloons showed fine, but "Check
    // for updates…" produced nothing at all, the exact failure mode a
    // MessageBox can't have.
    private async Task CheckForUpdatesAsync(bool manual)
    {
        UpdateChecker.UpdateInfo? update;
        try
        {
            update = await UpdateChecker.CheckAsync();
        }
        catch
        {
            if (manual)
                MessageBox.Show(Strings.UpdateCheckFailedDialog, "ZMK Companion",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (update is not { } info)
        {
            if (manual)
                MessageBox.Show(Strings.UpToDateDialog, "ZMK Companion",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (manual)
        {
            var result = MessageBox.Show(Strings.UpdateAvailableDialog(info.Version), "ZMK Companion",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (result == DialogResult.Yes)
                try { Process.Start(new ProcessStartInfo { FileName = info.Url, UseShellExecute = true }); }
                catch { /* best effort - worst case the user opens the releases page manually */ }
            _settings.UpdateCheckDismissedVersion = info.Version;
            _settings.Save();
        }
        else if (_settings.UpdateCheckDismissedVersion != info.Version)
        {
            _settings.UpdateCheckDismissedVersion = info.Version;
            _settings.Save();
            _tray.ShowUpdateAvailable(info.Version, info.Url);
        }
    }

    // Bundles settings.json + every Library preset + the recommended
    // Auto-start scripts folder into one .zip, so a user can carry their
    // whole setup between two machines (work/home) without hand-copying
    // three different folders. Scripts referenced by an absolute path
    // outside ScriptsDir can't be detected reliably from a free-form shell
    // command string, so those just don't get bundled - see
    // CollectPortabilityWarnings for the best-effort warning about it.
    private void OnExportSettings()
    {
        _settings.Save(); // make sure the on-disk file matches what's in memory

        using var dlg = new SaveFileDialog
        {
            Title    = Strings.ExportSettingsDialogTitle,
            Filter   = "ZIP (*.zip)|*.zip",
            FileName = $"{Strings.ExportSettingsDefaultName}-{DateTime.Now:yyyyMMdd}.zip",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        string stagingDir = Path.Combine(Path.GetTempPath(), "ZmkCompanionExport_" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(stagingDir);

            if (File.Exists(AppSettings.SettingsPath))
                File.Copy(AppSettings.SettingsPath, Path.Combine(stagingDir, "settings.json"));

            if (Directory.Exists(AppSettings.LibraryDir))
            {
                string libOut = Path.Combine(stagingDir, "library");
                Directory.CreateDirectory(libOut);
                foreach (var f in Directory.GetFiles(AppSettings.LibraryDir, "*.json"))
                    File.Copy(f, Path.Combine(libOut, Path.GetFileName(f)));
            }

            if (Directory.Exists(AppSettings.ScriptsDir))
            {
                string scriptsOut = Path.Combine(stagingDir, "scripts");
                Directory.CreateDirectory(scriptsOut);
                foreach (var f in Directory.GetFiles(AppSettings.ScriptsDir))
                    File.Copy(f, Path.Combine(scriptsOut, Path.GetFileName(f)));
            }

            if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
            ZipFile.CreateFromDirectory(stagingDir, dlg.FileName);

            var warnings = CollectPortabilityWarnings();
            if (warnings.Count > 0)
                MessageBox.Show(Strings.ExportSettingsPortabilityWarning(string.Join("\n", warnings)),
                    "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            MessageBox.Show(Strings.ExportSettingsDone(dlg.FileName),
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Strings.SettingsIoError(ex.Message),
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { Directory.Delete(stagingDir, true); } catch { /* best effort cleanup */ }
        }
    }

    // Best-effort only: flags enabled entries whose Command doesn't mention
    // "scripts\" or "scripts/" anywhere, across both the live settings and
    // every saved Library preset. Can't reliably parse an arbitrary shell
    // command line for "is this a file path", so this is a heuristic
    // reminder, not a guarantee the flagged ones will actually break.
    private List<string> CollectPortabilityWarnings()
    {
        var offenders = new List<string>();

        void Check(IEnumerable<AutoStartEntry> entries)
        {
            foreach (var e in entries)
                if (e.Enabled && e.Command.Trim().Length > 0 &&
                    !e.Command.Contains("scripts\\", StringComparison.OrdinalIgnoreCase) &&
                    !e.Command.Contains("scripts/", StringComparison.OrdinalIgnoreCase))
                    offenders.Add(e.Name);
        }

        Check(_settings.AutoStartEntries);

        if (Directory.Exists(AppSettings.LibraryDir))
        {
            foreach (var f in Directory.GetFiles(AppSettings.LibraryDir, "*.json"))
            {
                try
                {
                    var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var snap = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(f), opts);
                    if (snap != null) Check(snap.AutoStartEntries);
                }
                catch { /* a malformed library file isn't this warning's problem */ }
            }
        }

        return offenders.Distinct().ToList();
    }

    // Imports a .zip made by OnExportSettings: settings.json overwrites the
    // live one outright, Library presets and scripts are copied in
    // additively (same filename overwrites, anything else already there is
    // left alone) rather than wiping the destination folders first, so
    // importing on a machine that already has its own presets/scripts
    // doesn't silently delete them.
    private void OnImportSettings()
    {
        using var dlg = new OpenFileDialog
        {
            Title  = Strings.ImportSettingsDialogTitle,
            Filter = "ZIP (*.zip)|*.zip",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        if (MessageBox.Show(Strings.ImportSettingsConfirm, "ZMK Companion",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        string stagingDir = Path.Combine(Path.GetTempPath(), "ZmkCompanionImport_" + Guid.NewGuid());
        try
        {
            ZipFile.ExtractToDirectory(dlg.FileName, stagingDir);

            string incomingSettings = Path.Combine(stagingDir, "settings.json");
            if (File.Exists(incomingSettings))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppSettings.SettingsPath)!);
                File.Copy(incomingSettings, AppSettings.SettingsPath, overwrite: true);
            }

            string incomingLib = Path.Combine(stagingDir, "library");
            if (Directory.Exists(incomingLib))
            {
                Directory.CreateDirectory(AppSettings.LibraryDir);
                foreach (var f in Directory.GetFiles(incomingLib, "*.json"))
                    File.Copy(f, Path.Combine(AppSettings.LibraryDir, Path.GetFileName(f)), overwrite: true);
            }

            string incomingScripts = Path.Combine(stagingDir, "scripts");
            if (Directory.Exists(incomingScripts))
            {
                Directory.CreateDirectory(AppSettings.ScriptsDir);
                foreach (var f in Directory.GetFiles(incomingScripts))
                    File.Copy(f, Path.Combine(AppSettings.ScriptsDir, Path.GetFileName(f)), overwrite: true);
            }

            MessageBox.Show(Strings.ImportSettingsDone, "ZMK Companion",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Strings.SettingsIoError(ex.Message),
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { Directory.Delete(stagingDir, true); } catch { /* best effort cleanup */ }
        }
    }

    internal void ApplyDisplayPages(List<CellGridPage> pages, bool cycle)
    {
        _settings.DisplayPages      = pages;
        _settings.CycleDisplayPages = cycle;
        _settings.Save();
        LoadPage(_activePage < pages.Count ? _activePage : 0);
        RestartPageCycle();
        RegisterPageHotkeys(); // page count may have changed (added/removed pages)
        _ = RefreshWeatherAsync();
        _ = RefreshSportsAsync();
    }

    // ── Page hotkeys (F13-F21, see HotkeyManager) ─────────────────────────────

    private void RegisterPageHotkeys()
    {
        int n = Math.Min(_settings.DisplayPages.Count, HotkeyManager.MaxPages);
        var failed = _hotkeys.RegisterAll(_settings.DisplayPages.Count);
        var ok = Enumerable.Range(1, n).Except(failed).ToList();
        DebugLog.Log($"HotkeyManager: registered [{string.Join(", ", ok.Select(HotkeyManager.LabelFor))}]" +
            (failed.Count > 0
                ? $", FAILED (already claimed by another app) [{string.Join(", ", failed.Select(HotkeyManager.LabelFor))}]"
                : ""));
    }

    // Logs unconditionally, even for an out-of-range id, so the debug log can
    // distinguish "WM_HOTKEY never arrived at all" from "arrived but this
    // page no longer exists" (e.g. pages were removed after the keymap was
    // flashed with a stale binding).
    private void OnPageHotkey(int pageIndex)
    {
        DebugLog.Log($"hotkey: WM_HOTKEY received for page index {pageIndex}");
        if (pageIndex < 0 || pageIndex >= _settings.DisplayPages.Count)
        {
            DebugLog.Log($"hotkey: ignored, no page at index {pageIndex} (pageCount={_settings.DisplayPages.Count})");
            return;
        }
        DebugLog.Log($"hotkey: jump to page {pageIndex} ('{_settings.DisplayPages[pageIndex].Name}')");
        LoadPage(pageIndex);
        RestartPageCycle();
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
            _hotkeys.Dispose();
            _compositor.Dispose();
            _tray.Dispose();
            _ble.Dispose();
        }
        base.Dispose(disposing);
    }
}
