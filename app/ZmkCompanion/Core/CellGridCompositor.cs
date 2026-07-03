using System.Drawing;
using System.Drawing.Text;

namespace ZmkCompanion.Core;

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
    private int  _tickCount;
    private bool _sendInFlight;
    private bool _sendQueued;

    public bool Running { get; private set; }

    public CellGridCompositor(BleService ble, LiveState state)
    {
        _ble   = ble;
        _state = state;
    }

    // ── Page management ───────────────────────────────────────────────────────

    public async Task LoadPageAsync(CellGridPage page)
    {
        Stop();
        if (!_ble.IsConnected) return;
        if (!_ble.HasCellGridChar)
        {
            DebugLog.Log("CellGridCompositor: 0x1527 char not found — firmware too old");
            return;
        }

        _rows      = page.Rows.Select(r => r.Clone()).ToList();
        _sent.Clear();
        _tickCount = 0;
        Running    = true;
        _state.Changed += OnStateChanged;

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
    }

    public void Stop()
    {
        if (!Running) return;
        Running = false;
        _state.Changed -= OnStateChanged;
        _clockTimer?.Stop();
        _clockTimer?.Dispose();
        _clockTimer = null;
        _rows.Clear();
        DebugLog.Log("CellGridCompositor: stopped");
    }

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

    // Forces a full re-render of all cells (safety net for silent BLE failures).
    public async Task ForceRedrawAsync()
    {
        if (!Running) return;
        _sent.Clear();
        await RenderAndSendAsync(full: true);
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
            await RenderAndSendAsync(full);
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
                await RenderAndSendAsync(full: false);
            } while (_sendQueued);
        }
        finally { _sendInFlight = false; }
    }

    // ── Render ────────────────────────────────────────────────────────────────

    private async Task RenderAndSendAsync(bool full)
    {
        bool use24h = !Protocol.Detect12h();
        for (int rowIdx = 0; rowIdx < _rows.Count; rowIdx++)
        {
            var    row  = _rows[rowIdx];
            var    tier = CellGridProtocol.Tiers[row.TierId];
            string text = _state.Expand(row.Template, use24h, null);
            await SendRowAsync(rowIdx, tier, text, row.Align, full);
        }
    }

    private async Task SendRowAsync(int rowIdx, CellTier tier, string text, string align, bool full)
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
            int gi = col - start;
            byte[] cell = (gi >= 0 && gi < count)
                ? CellGridRenderer.RenderCell(tier, glyphs[gi])
                : blank;

            var key = (rowIdx, col);
            if (!full && _sent.TryGetValue(key, out var prev) && prev.AsSpan().SequenceEqual(cell))
                continue;
            if (await _ble.SendCellGridAsync(CellGridProtocol.BuildCell(rowIdx, col, cell)))
                _sent[key] = cell;
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
            string text = state.Expand(row.Template, use24h, null);

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
                    ? CellGridRenderer.RenderCell(tier, glyphs[gi])
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

    public void Dispose() => Stop();
}
