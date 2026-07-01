using System.Drawing;
using System.Drawing.Text;

namespace ZmkCompanion.Core;

// Owns the 68×160 canvas. When any widget fires Invalidated, re-renders all
// widgets and sends the resulting bitmap to the keyboard via BleService.
// All methods must be called on the UI thread (widgets use WinForms timers).
sealed class DisplayCompositor : IDisposable
{
    private readonly BleService _ble;
    private readonly List<IWidget> _widgets = [];

    // Single-shot timer used to resume widgets after ShowTemporaryAsync.
    private System.Windows.Forms.Timer? _resumeTimer;

    // Guards every SendBitmapAsync call site (render pump + ShowTemporaryAsync).
    // Concurrent chunked BLE writes on the same characteristic interleave and
    // produce torn frames on the display, so only one send may be in flight.
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public DisplayCompositor(BleService ble) => _ble = ble;

    public IReadOnlyList<IWidget> Widgets => _widgets;

    // ── Widget management ─────────────────────────────────────────────────────

    public void Add(IWidget widget)
    {
        _widgets.Add(widget);
        widget.Invalidated += OnInvalidated;
    }

    public void Remove(IWidget widget)
    {
        widget.Invalidated -= OnInvalidated;
        _widgets.Remove(widget);
    }

    // Replaces all widgets, disposing the old ones. Does not start the new ones.
    public void Rebuild(IEnumerable<IWidget> newWidgets)
    {
        StopAll();
        foreach (var w in _widgets) { w.Invalidated -= OnInvalidated; w.Dispose(); }
        _widgets.Clear();
        foreach (var w in newWidgets) Add(w);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartAll() { foreach (var w in _widgets) w.Start(); }

    public void StopAll()
    {
        _resumeTimer?.Stop(); _resumeTimer?.Dispose(); _resumeTimer = null;
        foreach (var w in _widgets) w.Stop();
    }

    // ── Temporary frame (CLI text, one-shot displays) ─────────────────────────

    // Stops widget timers, sends frame, then resumes after duration.
    public async Task ShowTemporaryAsync(byte[] frame, TimeSpan duration)
    {
        foreach (var w in _widgets) w.Stop();
        _resumeTimer?.Stop(); _resumeTimer?.Dispose(); _resumeTimer = null;

        await _sendLock.WaitAsync();
        try   { await _ble.SendBitmapAsync(frame); }
        finally { _sendLock.Release(); }

        _resumeTimer = new System.Windows.Forms.Timer { Interval = (int)duration.TotalMilliseconds };
        _resumeTimer.Tick += (_, _) =>
        {
            _resumeTimer!.Stop(); _resumeTimer.Dispose(); _resumeTimer = null;
            StartAll();
        };
        _resumeTimer.Start();
    }

    // ── Render ────────────────────────────────────────────────────────────────

    // Coalesces bursts of Invalidated (e.g. StartAll firing one per widget, or
    // several widgets updating around the same time) into a single re-render+
    // send. A render is "in flight" from the moment it starts until the BLE
    // send completes — invalidations arriving during that window only set
    // _renderQueued, so at most one extra render+send runs after the current
    // one finishes (always capturing the latest state at that point), instead
    // of spawning a new concurrent task per event and building up a backlog of
    // stale frames that slowly drains out over several BLE round-trips.
    private bool _renderInFlight;
    private bool _renderQueued;

    private void OnInvalidated()
    {
        if (_renderInFlight) { _renderQueued = true; return; }
        _renderInFlight = true;
        _ = RenderAndSendAsync();
    }

    // Forces a render+send even with no pending widget invalidation. Used as a
    // periodic safety net: if a single BLE write silently fails (no exception,
    // just GattCommunicationStatus != Success), the frame that failed is never
    // retried on its own — nothing invalidates again until the next natural
    // event (e.g. the clock's once-a-minute tick), so the display can be stuck
    // showing stale content for a while. A periodic ForceRedraw call bounds
    // how long that staleness can last.
    public void ForceRedraw() => OnInvalidated();

    // Waits until any in-flight render+send has actually finished transmitting,
    // instead of just assuming it did. Used by page cycling so the next page's
    // dwell time is counted from when its frame really landed on the keyboard —
    // if BLE is slow, this naturally stretches out the cycle instead of firing
    // page switches on a fixed wall-clock schedule that outpaces what the link
    // can drain (which builds an ever-growing backlog of stale frames).
    public async Task WaitForIdleAsync(int maxWaitMs = 20_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (_renderInFlight && sw.ElapsedMilliseconds < maxWaitMs)
            await Task.Delay(100);
    }

    public async Task RenderAndSendAsync()
    {
        do
        {
            _renderQueued = false;

            using var bmp = BitmapFrame.CreateCanvas();
            using var g   = Graphics.FromImage(bmp);
            g.Clear(Color.Black);
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

            foreach (var widget in _widgets)
            {
                var clip = g.Clip;
                g.SetClip(widget.Bounds);
                widget.Render(g);
                g.Clip = clip;
            }

            byte[] frame = BitmapFrame.Pack(bmp);
            await _sendLock.WaitAsync();
            try
            {
                // One immediate retry on failure — cheap insurance against a
                // transient GATT write hiccup, without adding latency when the
                // send simply succeeds (the common case).
                if (!await _ble.SendBitmapAsync(frame))
                {
                    await Task.Delay(300);
                    await _ble.SendBitmapAsync(frame);
                }
            }
            finally { _sendLock.Release(); }
        } while (_renderQueued);

        _renderInFlight = false;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        StopAll();
        foreach (var w in _widgets) { w.Invalidated -= OnInvalidated; w.Dispose(); }
        _widgets.Clear();
    }
}
