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

    public DisplayCompositor(BleService ble) => _ble = ble;

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

        await _ble.SendBitmapAsync(frame);

        _resumeTimer = new System.Windows.Forms.Timer { Interval = (int)duration.TotalMilliseconds };
        _resumeTimer.Tick += (_, _) =>
        {
            _resumeTimer!.Stop(); _resumeTimer.Dispose(); _resumeTimer = null;
            StartAll();
        };
        _resumeTimer.Start();
    }

    // ── Render ────────────────────────────────────────────────────────────────

    private void OnInvalidated() => _ = RenderAndSendAsync();

    public async Task RenderAndSendAsync()
    {
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

        await _ble.SendBitmapAsync(BitmapFrame.Pack(bmp));
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        StopAll();
        foreach (var w in _widgets) { w.Invalidated -= OnInvalidated; w.Dispose(); }
        _widgets.Clear();
    }
}
