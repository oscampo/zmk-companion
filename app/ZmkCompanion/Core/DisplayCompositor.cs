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

    // Coalesces bursts of Invalidated (e.g. StartAll firing one per widget)
    // into a single re-render+send instead of queuing one per event.
    private bool _renderPending;

    private void OnInvalidated()
    {
        if (_renderPending) return;
        _renderPending = true;
        _ = RenderAndSendAsync();
    }

    public async Task RenderAndSendAsync()
    {
        _renderPending = false;

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
        try   { await _ble.SendBitmapAsync(frame); }
        finally { _sendLock.Release(); }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        StopAll();
        foreach (var w in _widgets) { w.Invalidated -= OnInvalidated; w.Dispose(); }
        _widgets.Clear();
    }
}
