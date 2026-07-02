using ZmkCompanion.Core;

namespace ZmkCompanion.Features;

// A/B hardware test for the cell-grid protocol (0x1527) — drives a fixed
// two-row page (clock + date) over the new protocol while the normal
// full-frame (0x1525) pipeline is paused. This exists to validate the
// protocol end-to-end on real hardware before the Canvas Editor is
// migrated to rows/tiers; it is not the final integration.
sealed class CellGridTest : IDisposable
{
    private readonly BleService        _ble;
    private readonly DisplayCompositor _compositor;
    private System.Windows.Forms.Timer? _timer;

    // tier 4 = large_impar (13×22, 5 cols — exact fit for HH:mm)
    // tier 0 = small_impar (6×10, 11 cols) for the date line
    private static readonly CellTier ClockTier = CellGridProtocol.Tiers[4];
    private static readonly CellTier DateTier  = CellGridProtocol.Tiers[0];

    // Last bitmap sent per cell, keyed by (row, col) — the app-side diff
    // state the protocol spec assigns to the client. Reset on every start
    // (client rule 1: reconnect/start = dirty screen).
    private readonly Dictionary<(int Row, int Col), byte[]> _sent = new();

    public bool Running { get; private set; }

    public CellGridTest(BleService ble, DisplayCompositor compositor)
    {
        _ble        = ble;
        _compositor = compositor;
    }

    public async Task StartAsync()
    {
        if (Running) return;
        Running = true;

        _compositor.StopAll();
        _compositor.Paused = true;
        _sent.Clear();

        DebugLog.Log("CellGridTest: starting — CLEAR + LAYOUT + full cells");
        bool ok = await _ble.SendCellGridAsync(CellGridProtocol.BuildClear())
               && await _ble.SendCellGridAsync(CellGridProtocol.BuildLayout(
                      (ClockTier.Id, 1), (DateTier.Id, 1)))
               && await SendPageAsync(full: true);
        DebugLog.Log($"CellGridTest: initial paint ok={ok} err={_ble.LastCellGridError ?? "(none)"}");

        // Re-render on each minute boundary. Every 10th tick resends ALL
        // cells regardless of diff state: a lost firmware-side area
        // invalidation (observed once on hardware: a units digit stuck on
        // the previous glyph for one minute despite an ATT ack) leaves a
        // stale cell that diff-only updates would never touch again if its
        // content stopped changing. The periodic full pass bounds any such
        // staleness to ~10 minutes at a cost of a handful of tiny writes.
        //
        // IMPORTANT: the interval is recomputed from the wall clock on
        // EVERY tick (see ScheduleNextTick), not fixed at 60_000ms after
        // the first alignment. A second hardware run showed exactly why
        // that matters: System.Windows.Forms.Timer has no guaranteed
        // sub-second precision, and a single tick that fires a couple of
        // seconds early (observed at 06:26:58 instead of 06:27:00) used to
        // permanently shift every subsequent tick to that same offset,
        // since "always add 60000ms" never looks at the real clock again.
        // The visible symptom was a rock-steady ~1-minute-behind display
        // that "transitioned instantly" each minute (because the tick was
        // firing 1-2s before the real rollover, so the correct value for
        // the *previous* minute kept showing for nearly a full minute
        // before the next early tick caught up) — a scheduling bug, not a
        // BLE or firmware one. Recomputing every tick means any single
        // tick's jitter is bounded to well under a second and never
        // accumulates.
        _timer = new System.Windows.Forms.Timer();
        int tick = 0;
        _timer.Tick += async (_, _) =>
        {
            // MUST stop the timer synchronously before the first await. The
            // interval scheduled right at a :00 boundary is intentionally
            // tiny (a handful of ms) so the tick lands as close to the real
            // second as possible — but that also means if we leave the
            // timer running, Windows keeps posting WM_TIMER at that same
            // tiny interval for as long as this async handler is suspended
            // awaiting BLE writes, and WinForms Timer does NOT wait for a
            // previous async Tick handler to finish before dispatching the
            // next one. Confirmed on hardware: this produced a storm of
            // 150+ overlapping SendPageAsync calls in ~5 seconds right at
            // the 07:00:00 boundary, racing each other and writing digit
            // cells out of order — the visible symptom was a transient
            // "7:09" ghost value during the 6:59->7:00 transition. Stopping
            // here guarantees no reentrant tick can fire until we
            // explicitly restart below, once the send has actually
            // finished and the next interval is computed from a fresh
            // DateTime.Now.
            _timer!.Stop();
            bool fullPass = ++tick % 10 == 0;
            bool sent = await SendPageAsync(full: fullPass);
            DebugLog.Log($"CellGridTest: tick ok={sent} full={fullPass} err={_ble.LastCellGridError ?? "(none)"}");
            ScheduleNextTick();
            _timer.Start();
        };
        ScheduleNextTick();
        _timer.Start();
    }

    private void ScheduleNextTick()
    {
        int msUntilNext = (60 - DateTime.Now.Second) * 1000 - DateTime.Now.Millisecond;
        // Floor at 250ms rather than 1ms: with the Stop()/Start() guard above
        // an overlapping fire is already impossible, but a near-zero interval
        // computed right at the rollover instant would still cause back-to-back
        // sequential passes within the same second for no benefit.
        _timer!.Interval = Math.Max(250, msUntilNext);
    }

    public void Stop()
    {
        if (!Running) return;
        Running = false;
        _timer?.Stop(); _timer?.Dispose(); _timer = null;
        _compositor.Paused = false;
        _compositor.StartAll();   // resume 0x1525 pipeline; full frame repaints over the test
        DebugLog.Log("CellGridTest: stopped, full-frame pipeline resumed");
    }

    // Renders both rows and sends cells; when full=false only cells whose
    // bitmap differs from the last sent one go out (steady-state diffing).
    private async Task<bool> SendPageAsync(bool full)
    {
        var now = DateTime.Now;
        string clock = now.ToString(Protocol.Detect12h() ? "h:mm" : "HH:mm").PadLeft(5);
        string date  = now.ToString("ddd dd").ToUpper();

        bool allOk = true;
        allOk &= await SendRowAsync(0, ClockTier, clock, full);
        allOk &= await SendRowAsync(1, DateTier,  date,  full);
        return allOk;
    }

    private async Task<bool> SendRowAsync(int row, CellTier tier, string text, bool full)
    {
        var cells = CellGridRenderer.RenderText(tier, text);
        bool allOk = true;
        for (int col = 0; col < cells.Length && col < tier.Cols; col++)
        {
            var key = (row, col);
            if (!full && _sent.TryGetValue(key, out var prev) && prev.AsSpan().SequenceEqual(cells[col]))
                continue;
            if (await _ble.SendCellGridAsync(CellGridProtocol.BuildCell(row, col, cells[col])))
                _sent[key] = cells[col];
            else
                allOk = false;
        }
        return allOk;
    }

    public void Dispose() => Stop();
}
