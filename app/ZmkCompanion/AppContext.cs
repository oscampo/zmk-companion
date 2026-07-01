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
    private readonly LiveState         _liveState = new();

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

        _ble.Connected              += OnConnected;
        _ble.Disconnected           += OnDisconnected;
        _ble.BatteryLevelChanged    += OnBatteryLevelChanged;
        _ble.StatusChanged          += OnStatusChanged;
        _tray.ExitRequested         += OnExit;
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

    // ── BLE status events ─────────────────────────────────────────────────────

    // Both handlers are raised on the UI thread by BleService via _uiContext.Post.

    private void OnBatteryLevelChanged(byte level)
    {
        _liveState.UpdateBattery(level, false);
        // Legacy rigid widgets still wired for backwards compat.
        foreach (var w in _compositor.Widgets.OfType<BatteryWidget>())
            w.Update(level, false);
    }

    private void OnStatusChanged(byte status, byte bonds)
    {
        bool usb      = (status & 0x01) != 0;
        int  profile  = (status >> 1) & 0x07;
        int  bondMask = bonds & 0x1F;  // bits 0-4 = profiles 1-5 bonded
        _liveState.UpdateConnection(usb, profile, bondMask);
        // Legacy rigid widgets still wired for backwards compat.
        foreach (var w in _compositor.Widgets.OfType<ConnectionWidget>())
            w.Update(usb, profile);
    }

    // ── Canvas editor ─────────────────────────────────────────────────────────

    private void OnCanvasEditor()
    {
        var form = new CanvasEditorForm(_settings, _liveState, ApplyCanvas);
        form.Show();
    }

    internal void ApplyCanvas(List<WidgetPlacement> canvas)
    {
        _settings.Canvas = canvas;
        _settings.Save();
        _compositor.Rebuild(canvas.Select(CreateWidget));
        if (_ble.IsConnected) _compositor.StartAll();
    }

    private IWidget CreateWidget(WidgetPlacement p)
    {
        var bounds = p.ToRectangle();
        return p.Type switch
        {
            "label"      => new LabelWidget(_liveState)      { Bounds = bounds, Config = p.GetConfig<LabelConfig>() },
            "profilebar" => new ProfileBarWidget(_liveState) { Bounds = bounds, Config = p.GetConfig<ProfileBarConfig>() },
            // Legacy rigid widgets — still supported for saved configs.
            "battery"    => new BatteryWidget    { Bounds = bounds, Config = p.GetConfig<BatteryConfig>() },
            "connection" => new ConnectionWidget { Bounds = bounds, Config = p.GetConfig<ConnectionConfig>() },
            _            => new ClockWidget      { Bounds = bounds, Config = p.GetConfig<ClockConfig>() },
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
