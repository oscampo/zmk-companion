using ZmkCompanion.Core;

namespace ZmkCompanion.Features;

// Cell-grid clock page (0x1527): renders clock + date as per-glyph cells so
// only changed characters are sent each minute, reducing BLE payload and
// eliminating the torn-frame artifact seen with full-frame updates.
//
// Layout: row 0 = tier 4 (large_impar 13×22, 5 cols) for HH:mm;
//         row 1 = tier 0 (small_impar  6×10, 11 cols) for "ddd dd".
//
// The full-frame (0x1525) pipeline is paused while this is active; it is
// resumed when Stop() is called or the page changes.
sealed class CellGridTest : IDisposable
{
    private readonly BleService        _ble;
    private readonly DisplayCompositor _compositor;
    private System.Windows.Forms.Timer? _timer;

    private static readonly CellTier ClockTier = CellGridProtocol.Tiers[4]; // large_impar 13×22
    private static readonly CellTier DateTier  = CellGridProtocol.Tiers[0]; // small_impar  6×10

    // App-side diff state: last bitmap sent per (row, col).
    // Reset on Start (client rule: reconnect/start = dirty screen).
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

        // Timer recomputed from wall clock on every tick — never fixed at 60_000ms.
        // A single tick that fires a couple of seconds early (observed on hardware)
        // used to permanently shift all subsequent ticks, producing a rock-steady
        // ~1-minute-behind display. Recomputing on every tick bounds any single
        // tick's jitter to well under a second and prevents accumulation.
        //
        // Every 10th tick sends ALL cells regardless of diff state. A lost
        // firmware-side area invalidation (observed once on hardware: a units digit
        // stuck for one minute) leaves a stale cell that diff-only updates would
        // never touch again. The periodic full pass bounds such staleness to ~10 min.
        _timer = new System.Windows.Forms.Timer();
        int tick = 0;
        _timer.Tick += async (_, _) =>
        {
            // Capture BEFORE any await. ScheduleNextTick anchors to this
            // timestamp to avoid the :59→:00 boundary skip bug: a tick
            // completing at :00.002 would see second==0 and schedule 60s out,
            // silently skipping the current minute. Using tickTime avoids that.
            var tickTime = DateTime.Now;

            // MUST stop synchronously before the first await. WinForms Timer
            // does not wait for the previous async Tick handler before firing
            // the next one; at a tiny interval near a :00 boundary this caused
            // a storm of 150+ overlapping SendPageAsync calls on hardware.
            _timer!.Stop();
            bool fullPass = ++tick % 10 == 0;
            DebugLog.Log($"CellGridTest: tick tickTime={tickTime:HH:mm:ss.fff} full={fullPass}");
            await SendPageAsync(full: fullPass);
            ScheduleNextTick(tickTime);
            _timer.Start();
        };
        ScheduleNextTick(DateTime.Now);
        _timer.Start();
    }

    private void ScheduleNextTick(DateTime tickTime)
    {
        int msUntilNext = (60 - tickTime.Second) * 1000 - tickTime.Millisecond;
        _timer!.Interval = Math.Max(250, msUntilNext);
    }

    public void Stop()
    {
        if (!Running) return;
        Running = false;
        _timer?.Stop(); _timer?.Dispose(); _timer = null;
        _compositor.Paused = false;
        _compositor.StartAll();
        DebugLog.Log("CellGridTest: stopped, full-frame pipeline resumed");
    }

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
        int colCount = Math.Min(cells.Length, tier.Cols);
        // Write right-to-left: a partial update "11:49"→"11:50" briefly shows
        // "11:40" (past) rather than "11:59" (future). Unambiguously stale is
        // better than ambiguously ahead.
        for (int col = colCount - 1; col >= 0; col--)
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
