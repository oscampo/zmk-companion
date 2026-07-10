using System.Drawing;
using System.Drawing.Text;

namespace ZmkCompanion.Core;

// Which of the {ext.text} family of tokens (if any) the active page's rows
// reference, decided once per LoadPageAsync. See CellGridCompositor.TextMode.
enum ExternalTextMode { None, FullScreen, CellGrid }

// Primary display compositor using the cell-grid protocol (0x1527).
// Renders each row's template to per-glyph bitmaps, diffs against the
// last-sent state, and only transmits cells that actually changed.
// The clock timer recomputes its interval from the wall clock on every
// tick so single-tick jitter never accumulates into a drifted display.
sealed class CellGridCompositor : IDisposable
{
    private readonly BleService _ble;
    private readonly LiveState  _state;

    private List<CellGridRow>                        _rows    = [];
    private readonly Dictionary<(int, int), byte[]> _sent    = new();
    private System.Windows.Forms.Timer?              _clockTimer;
    private bool   _textOverride;       // persistent CLI text is displayed; cell-grid suspended
    // Multi-page text override (see BitmapTextRenderer.RenderPages): when the
    // text doesn't fit on one screen, _textPageTimer alternates through
    // _textOverridePages, holding each page for a duration proportional to
    // how much text it holds (a page with "battery 87%" doesn't need the same
    // reading time as a page full of a dense paragraph) — see PageDurationMs.
    // A fresh ShowPersistentTextAsync call (new text arriving) always resets
    // to page 0 and restarts the timer — deliberately not "smart" about
    // in-flight `--watch` streams, so a fast stream just never advances past
    // page 0 rather than flickering through pages the reader can't keep up
    // with anyway.
    //
    // [Suposición] Reading-speed constants, not validated against real usage
    // yet: ~60ms/char (~200wpm-ish for average word length) with a floor so
    // near-empty pages don't flash by and a ceiling so one page never hogs
    // the display indefinitely.
    private const int PageFloorMs   = 2500;
    private const int PageCeilingMs = 15000;
    private const int PageMsPerChar = 60;
    private List<(byte[] Frame, int CharCount)> _textOverridePages = [];
    private int           _textOverridePageIndex;
    private System.Windows.Forms.Timer? _textPageTimer;

    // Last FullScreen override frames per page index, so a page cycling back
    // into view after showing something else (or after LoadPageAsync's own
    // reset below) redisplays the last text it was sent instead of falling
    // through to cell-grid's raw, clipped {ext.text} rendering. Keyed by
    // page index (not identity/name) to match how AppContext already tracks
    // the active page; a settings reload naturally orphans stale entries,
    // harmless since they're just never looked up again.
    private readonly Dictionary<int, List<(byte[] Frame, int CharCount)>> _fullScreenCache = new();

    private static int PageDurationMs(int charCount) =>
        Math.Clamp(PageFloorMs + charCount * PageMsPerChar, PageFloorMs, PageCeilingMs);
    // Set eagerly from the pipe thread the instant text arrives, before the drain timer's own
    // tick runs. WinForms timers fire via WM_TIMER, which Windows only synthesizes once the
    // message queue is empty — a heartbeat redraw chaining many awaited BLE writes keeps the
    // queue busy and can starve the drain timer for its whole duration. Checking this volatile
    // flag (instead of only _textOverride) lets an in-progress cell-grid render loop abort on
    // its very next iteration, independent of whether the drain timer gets to run at all.
    private volatile bool _overridePending;
    private byte[] _textOverrideFrame  = [];
    private int  _tickCount;
    private bool _sendInFlight;
    private bool _sendQueued;

    // Serializes every 0x1527 (cell-grid) BLE write sequence across the three
    // independent triggers that can each start one: the page-cycle timer
    // (LoadPageAsync), the 15s heartbeat (ForceRedrawAsync), and the clock
    // tick. None of these previously waited on each other, so under load
    // (many rows, several background pollers pushing Changed events) their
    // writes could interleave and queue up behind each other indefinitely —
    // observed in the field as page-cycle dwell time increasingly eaten by
    // a CLEAR+LAYOUT handshake that took seconds to even start, eventually
    // exceeding the whole dwell budget. Page loads must eventually run
    // (RunGatedAsync waits), but the heartbeat/clock tick are opportunistic
    // safety-net redraws that should skip rather than queue up and make the
    // backlog worse (TryRunGatedAsync). ShowTemporaryAsync/ShowPersistentTextAsync
    // are deliberately NOT gated here: they can hold for a multi-second
    // Task.Delay, and blocking page loads for that whole duration would be a
    // new stall that doesn't exist today — a known residual gap, not solved
    // by this pass.
    private readonly SemaphoreSlim _bleGate = new(1, 1);

    private async Task RunGatedAsync(Func<Task> body)
    {
        await _bleGate.WaitAsync();
        try { await body(); }
        finally { _bleGate.Release(); }
    }

    private async Task TryRunGatedAsync(Func<Task> body, string skipLabel)
    {
        if (!await _bleGate.WaitAsync(0))
        {
            DebugLog.Log($"CellGridCompositor: {skipLabel} skipped, BLE gate busy");
            return;
        }
        try { await body(); }
        finally { _bleGate.Release(); }
    }

    public bool Running { get; private set; }

    // How the active page wants piped/CLI text (zkc) displayed, decided purely
    // by which token(s) its rows reference:
    //   CellGrid   - has {ext.text.N}: goes through the normal positioned render.
    //   FullScreen - has bare {ext.text} (no CellGrid row on the same page,
    //                CellGrid takes priority if both appear): full-frame bitmap
    //                override, replacing the whole page for as long as text keeps
    //                arriving.
    //   None       - neither token present: page doesn't want CLI text at all,
    //                incoming text is dropped, page's own content is untouched.
    public ExternalTextMode TextMode { get; private set; }

    public CellGridCompositor(BleService ble, LiveState state)
    {
        _ble   = ble;
        _state = state;
    }

    // ── Page management ───────────────────────────────────────────────────────

    public Task LoadPageAsync(CellGridPage page, int pageIndex) => RunGatedAsync(async () =>
    {
        Stop();
        if (!_ble.IsConnected) return;
        if (!_ble.HasCellGridChar)
        {
            DebugLog.Log("CellGridCompositor: 0x1527 char not found — firmware too old");
            return;
        }

        _rows          = page.Rows.Select(r => r.Clone()).ToList();
        _textOverride  = false;
        _textOverrideFrame = [];
        // Bare {ext.text} has no trailing dot; {ext.text.N} does, this substring
        // check alone is enough to tell the two token forms apart. CellGrid wins
        // if a page somehow has both (unlikely, undefined by design otherwise).
        bool hasIndexed = _rows.Any(r =>
            r.Template.Contains("ext.text.", StringComparison.OrdinalIgnoreCase));
        bool hasBare = _rows.Any(r =>
            r.Template.Contains("{ext.text}", StringComparison.OrdinalIgnoreCase));
        TextMode = hasIndexed ? ExternalTextMode.CellGrid
                 : hasBare    ? ExternalTextMode.FullScreen
                              : ExternalTextMode.None;
        _sent.Clear();
        _tickCount = 0;
        Running    = true;
        _state.Changed += OnStateChanged;

        // This page wants {ext.text} and already has a bitmap on file from a
        // previous send: redisplay it instead of falling through to a fresh
        // cell-grid render, which would show the raw, clipped ExternalText
        // string in whatever tier the row uses. Skips CLEAR+LAYOUT_v2 entirely,
        // ShowPersistentTextAsync's own CLEAR (entering=true, since _textOverride
        // was just reset above) is enough to erase any leftover LVGL cell
        // objects before the bitmap overwrites the whole frame.
        if (TextMode == ExternalTextMode.FullScreen &&
            _fullScreenCache.TryGetValue(pageIndex, out var cached))
        {
            DebugLog.Log($"CellGridCompositor: restoring cached FullScreen override for page {pageIndex}");
            await ShowPersistentTextAsync(cached, preferSpeed: false);
            return;
        }

        var layoutArgs = _rows.Select(r => {
            var t = CellGridProtocol.Tiers[r.TierId];
            return ((byte)t.W, (byte)t.H, (byte)1);
        }).ToArray();
        bool ok = await _ble.SendCellGridAsync(CellGridProtocol.BuildClear())
               && await _ble.SendCellGridAsync(CellGridProtocol.BuildLayoutV2(layoutArgs));
        DebugLog.Log($"CellGridCompositor: CLEAR+LAYOUT_v2 ok={ok} err={_ble.LastCellGridError ?? "(none)"}");
        if (!ok) return;

        await RenderAndSendAsync(full: true);

        if (_rows.Any(r => LiveState.HasTimeBind(r.Template)))
            StartClockTimer();
    });

    public void Stop()
    {
        if (!Running) return;
        Running = false;
        _state.Changed -= OnStateChanged;
        _clockTimer?.Stop();
        _clockTimer?.Dispose();
        _clockTimer = null;
        _textPageTimer?.Stop();
        _textPageTimer?.Dispose();
        _textPageTimer = null;
        _rows.Clear();
        TextMode = ExternalTextMode.None;
        _overridePending = false;
        DebugLog.Log("CellGridCompositor: stopped");
    }

    // Called from the pipe thread the moment text arrives, ahead of the drain timer's own
    // tick, so any in-progress cell-grid render (e.g. the heartbeat's periodic full redraw)
    // aborts immediately instead of running to completion first.
    public void SignalTextIncoming() => _overridePending = true;

    // ── Temporary full-frame display (pipe text) ──────────────────────────────

    // Pauses cell-grid, shows a full-frame bitmap for `duration`, then
    // restores the cell-grid page with a full refresh.
    public async Task ShowTemporaryAsync(byte[] frame, TimeSpan duration)
    {
        _clockTimer?.Stop();

        await _ble.SendBitmapAsync(frame);

        await Task.Delay(duration);

        if (!Running) return;

        // Re-establish cell-grid mode: firmware bitmap buffer was overwritten.
        var layoutArgs = _rows.Select(r => {
            var t = CellGridProtocol.Tiers[r.TierId];
            return ((byte)t.W, (byte)t.H, (byte)1);
        }).ToArray();
        await _ble.SendCellGridAsync(CellGridProtocol.BuildClear());
        await _ble.SendCellGridAsync(CellGridProtocol.BuildLayoutV2(layoutArgs));
        _sent.Clear();
        await RenderAndSendAsync(full: true);

        if (_clockTimer != null)
        {
            ScheduleNextClockTick(DateTime.Now);
            _clockTimer.Start();
        }
    }

    // Shows one or more full-frame bitmap "pages" and keeps the first until new
    // text, a page change, or (if there's more than one page) the page timer
    // advances to the next one. Suspends cell-grid updates for the duration of
    // the override. preferSpeed=true uses WriteWithoutResponse (~30-50ms) for
    // live streaming.
    public async Task ShowPersistentTextAsync(List<(byte[] Frame, int CharCount)> frames, bool preferSpeed = false,
                                              int? cachePageIndex = null)
    {
        _clockTimer?.Stop();
        _textPageTimer?.Stop();
        _textPageTimer?.Dispose();
        _textPageTimer = null;

        // Only set when this is a genuine new arrival from AppContext (the
        // page that was active when the text landed), not when LoadPageAsync
        // calls this to restore an already-cached override, that would just
        // rewrite the same entry with itself.
        if (cachePageIndex is int idx) _fullScreenCache[idx] = frames;

        bool entering = !_textOverride; // true on first call: transitioning from cell-grid to bitmap
        _textOverride          = true;
        _overridePending       = false;
        _textOverridePages     = frames;
        _textOverridePageIndex = 0;
        _textOverrideFrame     = frames.Count > 0 ? frames[0].Frame : [];
        // On first entry: clear LVGL cell objects so they stop rendering over the bitmap.
        // Subsequent streaming updates skip CLEAR (no new cell objects arrive once _textOverride
        // is set, and sending CLEAR+bitmap every second adds unnecessary ~150ms overhead).
        if (entering && _ble.HasCellGridChar)
            await _ble.SendCellGridAsync(CellGridProtocol.BuildClear());
        await _ble.SendBitmapAsync(_textOverrideFrame, preferSpeed);

        if (frames.Count > 1)
        {
            _textPageTimer = new System.Windows.Forms.Timer { Interval = PageDurationMs(frames[0].CharCount) };
            _textPageTimer.Tick += async (_, _) => await AdvanceTextPageAsync(preferSpeed);
            _textPageTimer.Start();
        }
    }

    private async Task AdvanceTextPageAsync(bool preferSpeed)
    {
        if (!_textOverride || _textOverridePages.Count <= 1 || _textPageTimer is null) return;
        _textPageTimer.Stop(); // reentrancy guard: interval is about to change, avoid a stray tick mid-send
        _textOverridePageIndex = (_textOverridePageIndex + 1) % _textOverridePages.Count;
        var page = _textOverridePages[_textOverridePageIndex];
        _textOverrideFrame     = page.Frame;
        await _ble.SendBitmapAsync(_textOverrideFrame, preferSpeed);
        if (_textOverride && _textPageTimer != null)
        {
            _textPageTimer.Interval = PageDurationMs(page.CharCount);
            _textPageTimer.Start();
        }
    }

    // Forgets every page's cached FullScreen override (zkc "" clears everything,
    // not just what's on screen right now, so a page cycling back later doesn't
    // resurrect text the user explicitly told the app to drop).
    public void ClearFullScreenCache() => _fullScreenCache.Clear();

    // Restores cell-grid after a persistent text override.
    public Task ClearTextOverrideAsync()
    {
        if (!_textOverride || !Running) { _textOverride = false; _overridePending = false; return Task.CompletedTask; }
        return RunGatedAsync(async () =>
        {
            _textPageTimer?.Stop();
            _textPageTimer?.Dispose();
            _textPageTimer = null;
            _textOverride      = false;
            _overridePending   = false;
            _textOverrideFrame = [];
            _textOverridePages = [];

            var layoutArgs = _rows.Select(r => {
                var t = CellGridProtocol.Tiers[r.TierId];
                return ((byte)t.W, (byte)t.H, (byte)1);
            }).ToArray();
            await _ble.SendCellGridAsync(CellGridProtocol.BuildClear());
            await _ble.SendCellGridAsync(CellGridProtocol.BuildLayoutV2(layoutArgs));
            _sent.Clear();
            await RenderAndSendAsync(full: true);
            if (_clockTimer != null) { ScheduleNextClockTick(DateTime.Now); _clockTimer.Start(); }
        });
    }

    // Forces a full re-render of all cells (safety net for silent BLE failures).
    // Opportunistic: skips instead of queuing if a page load or another
    // heartbeat/clock-tick redraw is already in flight (see _bleGate).
    public Task ForceRedrawAsync()
    {
        if (!Running) return Task.CompletedTask;
        if (_textOverride)
        {
            // Use WriteWithoutResponse: WriteWithResponse here blocks the BLE queue for 5-7s,
            // blanking the display during the send and serializing streaming frames behind it.
            // Not gated: a different characteristic (bitmap, not cell-grid), see _bleGate's comment.
            return _ble.SendBitmapAsync(_textOverrideFrame, preferSpeed: true);
        }
        return TryRunGatedAsync(async () =>
        {
            _sent.Clear();
            await RenderAndSendAsync(full: true);
        }, "heartbeat ForceRedraw");
    }

    // ── Clock timer ───────────────────────────────────────────────────────────

    private void StartClockTimer()
    {
        _clockTimer = new System.Windows.Forms.Timer();
        _clockTimer.Tick += async (_, _) =>
        {
            // Guard: Stop() may have disposed _clockTimer before this lambda runs.
            if (!Running || _clockTimer is null) return;
            var tickTime = DateTime.Now; // capture BEFORE any await
            _clockTimer.Stop();         // stop BEFORE first await (reentrancy guard)
            bool full = ++_tickCount % 10 == 0;
            DebugLog.Log($"CellGridCompositor: clock tick tickTime={tickTime:HH:mm:ss.fff} full={full}");
            // Opportunistic like the heartbeat: skip this tick rather than queue
            // behind a slow page load, next tick (≤60s away) tries again.
            await TryRunGatedAsync(() => RenderAndSendAsync(full), "clock tick");
            // Guard: Stop() may have been called during the await above.
            if (!Running || _clockTimer is null) return;
            ScheduleNextClockTick(tickTime);
            _clockTimer.Start();
        };
        ScheduleNextClockTick(DateTime.Now);
        _clockTimer.Start();
    }

    private void ScheduleNextClockTick(DateTime t)
    {
        int ms = (60 - t.Second) * 1000 - t.Millisecond;
        _clockTimer!.Interval = Math.Max(250, ms);
    }

    // ── State-change driven updates ───────────────────────────────────────────

    private void OnStateChanged()
    {
        if (_textOverride) return; // persistent CLI text is displayed; ignore state changes
        if (_sendInFlight) { _sendQueued = true; return; }
        _sendInFlight = true;
        _ = DrainAsync();
    }

    private async Task DrainAsync()
    {
        try
        {
            do
            {
                _sendQueued = false;
                // Waits (not skip): unlike the heartbeat/clock tick this reflects
                // genuinely new data (weather/sports/etc. just changed), and
                // _sendQueued above already coalesces repeated Changed events
                // that arrive while this is waiting/running.
                await RunGatedAsync(() => RenderAndSendAsync(full: false));
            } while (_sendQueued);
        }
        finally { _sendInFlight = false; }
    }

    // ── Render ────────────────────────────────────────────────────────────────

    // Reset per RenderAndSendAsync call, incremented by SendRowAsync — lets the
    // completion/abort log below report how many of the page's cells actually
    // went over BLE, not just how long the pass took (measures both the "is
    // the sequential cell-by-cell reveal really this slow" question and, once
    // full is true, roughly how many round trips a full page load costs).
    private int _cellsSentThisRender;

    private async Task RenderAndSendAsync(bool full)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int rowCount = _rows.Count;
        _cellsSentThisRender = 0;

        // Full renders go as one 0x1525 bitmap blob instead of one 0x1527 CELL
        // message per glyph: measured ~35-40ms per acked cell write vs. ~50-70ms
        // for an ENTIRE frame, so a 45-66 cell page (1.6-2.5s) collapses to
        // roughly the cost of one CLI text send. Verified against the firmware
        // source (zmk-companion-template/config/custom_status_screen.c) that both paths
        // write into the same shared canvas buffer and invalidate the same LVGL
        // object — no per-cell widgets to lose sync with, so this doesn't
        // conflict with the partial-diff path below, which still needs the
        // prior CLEAR+LAYOUT_v2 (sent by the caller) for its row/col mapping.
        if (full)
        {
            bool ok = await RenderFullPageBitmapAsync();
            DebugLog.Log($"CellGridCompositor: render done full=True (bitmap) rows={rowCount} " +
                $"ok={ok} elapsed={sw.ElapsedMilliseconds}ms");
            return;
        }

        bool use24h = !Protocol.Detect12h();
        for (int rowIdx = 0; rowIdx < _rows.Count; rowIdx++)
        {
            if (_textOverride || _overridePending)
            {
                DebugLog.Log($"CellGridCompositor: render ABORTED (override) full={full} " +
                    $"row={rowIdx}/{rowCount} cellsSent={_cellsSentThisRender} elapsed={sw.ElapsedMilliseconds}ms");
                return; // bitmap override activated (or about to) — abort cell sends
            }
            var    row  = _rows[rowIdx];
            var    tier = CellGridProtocol.Tiers[row.TierId];
            var    cfg  = MakeLabelConfig(row);
            string text = _state.Expand(row.Template, use24h, cfg);
            var    fs   = row.Bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;
            await SendRowAsync(rowIdx, tier, text, row.Align, row.SplitHalf, fs, row.AntiAlias, row.FontVariant, full);
        }
        DebugLog.Log($"CellGridCompositor: render done full={full} rows={rowCount} " +
            $"cellsSent={_cellsSentThisRender} elapsed={sw.ElapsedMilliseconds}ms");
    }

    // Composites the whole page (every row/cell) into one native-resolution
    // (68x160) bitmap and sends it as a single 0x1525 frame, seeding _sent with
    // each cell's actual rendered bits so the next partial (full=false) render
    // diffs correctly against what's now really on screen instead of an empty
    // cache (which would otherwise resend everything on the very next tick).
    private async Task<bool> RenderFullPageBitmapAsync()
    {
        bool use24h = !Protocol.Detect12h();
        using var bmp = BitmapFrame.CreateCanvas();
        using var g   = Graphics.FromImage(bmp);
        g.Clear(Color.Black);

        int yOff = 0;
        _sent.Clear();
        for (int rowIdx = 0; rowIdx < _rows.Count; rowIdx++)
        {
            if (_textOverride || _overridePending) return false;
            var    row  = _rows[rowIdx];
            var    tier = CellGridProtocol.Tiers[row.TierId];
            var    cfg  = MakeLabelConfig(row);
            string text = _state.Expand(row.Template, use24h, cfg);
            var    fs   = row.Bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;

            string[] glyphs = SplitElements(text);
            int count = Math.Min(glyphs.Length, tier.Cols);
            int start = row.Align switch
            {
                "right"  => tier.Cols - count,
                "center" => (tier.Cols - count) / 2,
                _        => 0,
            };

            int rowBytes = (tier.W + 7) / 8;
            for (int col = 0; col < tier.Cols; col++)
            {
                int gi = col - start;
                byte[] cellBits = (gi >= 0 && gi < count)
                    ? (row.SplitHalf != SplitHalf.None
                        ? CellGridRenderer.RenderCellSplit(tier, glyphs[gi], row.SplitHalf, fs, row.AntiAlias, row.FontVariant)
                        : CellGridRenderer.RenderCell(tier, glyphs[gi], fs, row.AntiAlias, row.FontVariant))
                    : new byte[tier.Bytes];
                _sent[(rowIdx, col)] = cellBits;

                int cellX = col * tier.W;
                for (int py = 0; py < tier.H; py++)
                    for (int px = 0; px < tier.W; px++)
                    {
                        int bx = cellX + px, by = yOff + py;
                        // Guard against a page whose row heights sum past 160px
                        // (OnApply rejects this on save, but a hand-edited or
                        // otherwise stale settings.json could still load one) —
                        // SetPixel throws out of bounds, RenderPreview has the
                        // same guard for the same reason.
                        if (bx >= BitmapFrame.Width || by >= BitmapFrame.Height) continue;
                        if ((cellBits[py * rowBytes + px / 8] & (0x80 >> (px % 8))) != 0)
                            bmp.SetPixel(bx, by, Color.White);
                    }
            }
            yOff += tier.H;
        }

        byte[] frame = BitmapFrame.Pack(bmp);
        return await _ble.SendBitmapAsync(frame, preferSpeed: true);
    }

    private static LabelConfig? MakeLabelConfig(CellGridRow row)
    {
        if (row.NumericStyle == "text" && row.AlphaStyle == "text") return null;
        return new LabelConfig { NumericStyle = row.NumericStyle, AlphaStyle = row.AlphaStyle };
    }

    private async Task SendRowAsync(int rowIdx, CellTier tier, string text, string align, SplitHalf split, System.Drawing.FontStyle fontStyle, bool antiAlias, FontVariant variant, bool full)
    {
        string[] glyphs   = SplitElements(text);
        int      count    = Math.Min(glyphs.Length, tier.Cols);
        int      start    = align switch
        {
            "right"  => tier.Cols - count,
            "center" => (tier.Cols - count) / 2,
            _        => 0,
        };

        var blank = new byte[tier.Bytes]; // all-zeros = black cell

        // Render cells right-to-left: on a partial update "11:49"→"11:50"
        // a reader sees the past time ("11:40") rather than a future one ("11:59").
        for (int col = tier.Cols - 1; col >= 0; col--)
        {
            if (_textOverride || _overridePending) return; // bitmap override activated (or about to) mid-row — abort
            int gi = col - start;
            byte[] cell = (gi >= 0 && gi < count)
                ? (split != SplitHalf.None
                    ? CellGridRenderer.RenderCellSplit(tier, glyphs[gi], split, fontStyle, antiAlias, variant)
                    : CellGridRenderer.RenderCell(tier, glyphs[gi], fontStyle, antiAlias, variant))
                : blank;

            var key = (rowIdx, col);
            if (!full && _sent.TryGetValue(key, out var prev) && prev.AsSpan().SequenceEqual(cell))
                continue;
            if (await _ble.SendCellGridAsync(CellGridProtocol.BuildCell(rowIdx, col, cell)))
            {
                _sent[key] = cell;
                _cellsSentThisRender++;
            }
        }
    }

    // ── Preview rendering (used by CellGridEditorForm) ────────────────────────

    // Renders the page at `scale` × native size into a Bitmap. Draws cell
    // grid lines and tier-boundary lines. The bitmap is owned by the caller.
    public static Bitmap RenderPreview(IReadOnlyList<CellGridRow> rows, LiveState state, int scale = 3)
    {
        int W = BitmapFrame.Width  * scale;
        int H = BitmapFrame.Height * scale;
        var bmp = new Bitmap(W, H);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        bool use24h = !Protocol.Detect12h();
        using var gridPen = new Pen(Color.FromArgb(60, 255, 255, 255));
        using var tierPen = new Pen(Color.FromArgb(120, 255, 165, 0)); // orange tier boundary

        int yOff = 0;
        for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
        {
            var    row  = rows[rowIdx];
            var    tier = CellGridProtocol.Tiers[row.TierId];
            var    cfg  = MakeLabelConfig(row);
            string text = state.Expand(row.Template, use24h, cfg);
            var    fs   = row.Bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;

            string[] glyphs = SplitElements(text);
            int count = Math.Min(glyphs.Length, tier.Cols);
            int start = row.Align switch
            {
                "right"  => tier.Cols - count,
                "center" => (tier.Cols - count) / 2,
                _        => 0,
            };

            // Render each glyph into its cell then blit scaled into preview.
            for (int col = 0; col < tier.Cols; col++)
            {
                int gi = col - start;
                byte[] cellBits = (gi >= 0 && gi < count)
                    ? (row.SplitHalf != SplitHalf.None
                        ? CellGridRenderer.RenderCellSplit(tier, glyphs[gi], row.SplitHalf, fs, row.AntiAlias, row.FontVariant)
                        : CellGridRenderer.RenderCell(tier, glyphs[gi], fs, row.AntiAlias, row.FontVariant))
                    : new byte[tier.Bytes];

                // Unpack 1bpp cell into preview pixels.
                int cellX = col * tier.W * scale;
                int cellY = yOff * scale;
                int rowBytes = (tier.W + 7) / 8;
                for (int py = 0; py < tier.H; py++)
                    for (int px = 0; px < tier.W; px++)
                    {
                        bool lit = (cellBits[py * rowBytes + px / 8] & (0x80 >> (px % 8))) != 0;
                        if (!lit) continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int bx = cellX + px * scale + sx;
                                int by = cellY + py * scale + sy;
                                if (bx < W && by < H) bmp.SetPixel(bx, by, Color.White);
                            }
                    }

                // Cell column grid line.
                if (col > 0)
                    g.DrawLine(gridPen, cellX, cellY, cellX, cellY + tier.H * scale);
            }

            // Tier-boundary line above the row.
            if (rowIdx > 0)
                g.DrawLine(tierPen, 0, yOff * scale, W, yOff * scale);

            yOff += tier.H;
        }

        // Remaining space indicator.
        int remaining = BitmapFrame.Height - yOff;
        if (remaining > 0)
        {
            int yRem = yOff * scale;
            g.DrawLine(tierPen, 0, yRem, W, yRem);
            using var font = new Font("Consolas", 7f * scale / 3f, GraphicsUnit.Pixel);
            g.DrawString($"{remaining}px libre", font, Brushes.DimGray, 2, yRem + 2);
        }

        return bmp;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string[] SplitElements(string s)
    {
        var list = new System.Collections.Generic.List<string>(s.Length);
        int i    = 0;
        while (i < s.Length)
        {
            if (i + 1 < s.Length && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
            { list.Add(s.Substring(i, 2)); i += 2; }
            else
            { list.Add(s.Substring(i, 1)); i++; }
        }
        return list.ToArray();
    }

    public void Dispose()
    {
        Stop();
        _bleGate.Dispose();
    }
}
