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

        // Re-render on each minute boundary, then every 60s.
        int msUntilNext = (60 - DateTime.Now.Second) * 1000 - DateTime.Now.Millisecond;
        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, msUntilNext) };
        _timer.Tick += async (_, _) =>
        {
            _timer!.Interval = 60_000;
            bool sent = await SendPageAsync(full: false);
            DebugLog.Log($"CellGridTest: tick ok={sent} err={_ble.LastCellGridError ?? "(none)"}");
        };
        _timer.Start();
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
