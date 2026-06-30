using System.Collections.Concurrent;
using ZmkCompanion.Core;
using ZmkCompanion.Features.Widgets;
using ZmkCompanion.UI;

namespace ZmkCompanion;

sealed class ZmkAppContext : ApplicationContext
{
    private readonly AppSettings       _settings;
    private readonly BleService        _ble;
    private readonly TrayIcon          _tray;
    private readonly DisplayCompositor _compositor;
    private readonly PipeServer        _pipe;

    private readonly CancellationTokenSource _cts = new();

    // Pipe callbacks run on the thread pool; compositor/WinForms timers need STA.
    private readonly ConcurrentQueue<string> _textQueue = new();
    private System.Windows.Forms.Timer? _drainTimer;

    public ZmkAppContext()
    {
        _settings = AppSettings.Load();
        _ble      = new BleService();
        _tray     = new TrayIcon(_ble, _settings);

        _compositor = new DisplayCompositor(_ble);
        foreach (var p in _settings.Canvas)
            _compositor.Add(CreateWidget(p));

        _pipe = new PipeServer();

        _ble.Connected             += OnConnected;
        _ble.Disconnected          += OnDisconnected;
        _tray.ExitRequested        += OnExit;
        _tray.CanvasEditorRequested += OnCanvasEditor;

        Application.Idle += OnFirstIdle;
    }

    private void OnFirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnFirstIdle;
        _ble.SetUiContext(SynchronizationContext.Current!);

        _drainTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _drainTimer.Tick += async (_, _) =>
        {
            while (_textQueue.TryDequeue(out string? text))
            {
                try
                {
                    using var bmp = BitmapTextRenderer.Render(text!);
                    byte[] frame  = BitmapFrame.Pack(bmp);
                    await _compositor.ShowTemporaryAsync(frame, TimeSpan.FromSeconds(30));
                }
                catch { }
            }
        };
        _drainTimer.Start();

        _pipe.Start(text =>
        {
            _textQueue.Enqueue(text);
            return Task.FromResult(true);
        });

        _ = ConnectLoopAsync(_cts.Token);
    }

    // ── Canvas editor ─────────────────────────────────────────────────────────

    private void OnCanvasEditor()
    {
        var form = new CanvasEditorForm(_settings, ApplyCanvas);
        form.Show();
    }

    internal void ApplyCanvas(List<WidgetPlacement> canvas)
    {
        _settings.Canvas = canvas;
        _settings.Save();
        _compositor.Rebuild(canvas.Select(CreateWidget));
        if (_ble.IsConnected) _compositor.StartAll();
    }

    private static IWidget CreateWidget(WidgetPlacement p)
    {
        var bounds = p.ToRectangle();
        return p.Type switch
        {
            "battery"    => new BatteryWidget    { Bounds = bounds },
            "connection" => new ConnectionWidget { Bounds = bounds },
            _            => new ClockWidget      { Bounds = bounds },
        };
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
        _tray.SetConnected(deviceName);
        _compositor.StartAll();
    }

    private void OnDisconnected()
    {
        _compositor.StopAll();
        _tray.SetDisconnected();
        _ = Task.Delay(10_000).ContinueWith(async _ =>
        {
            if (!_ble.IsConnected && !_cts.IsCancellationRequested)
                await _ble.ScanAndConnectAsync(_cts.Token);
        });
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
            _pipe.Dispose();
            _compositor.Dispose();
            _tray.Dispose();
            _ble.Dispose();
        }
        base.Dispose(disposing);
    }
}
