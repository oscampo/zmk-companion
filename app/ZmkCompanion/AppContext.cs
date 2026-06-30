using System.Collections.Concurrent;
using ZmkCompanion.Core;
using ZmkCompanion.Features;
using ZmkCompanion.UI;

namespace ZmkCompanion;

sealed class ZmkAppContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly BleService  _ble;
    private readonly TrayIcon    _tray;
    private readonly ClockFeature _clock;
    private readonly PipeServer  _pipe;

    private readonly CancellationTokenSource _cts = new();

    // BLE writes are STA-affine; pipe server runs on thread pool.
    // Queue text here and drain on the UI thread via a WinForms Timer.
    private readonly ConcurrentQueue<string> _bleQueue = new();
    private System.Windows.Forms.Timer? _bleTimer;

    public ZmkAppContext()
    {
        _settings = AppSettings.Load();
        _ble      = new BleService();
        _tray     = new TrayIcon(_ble, _settings);
        _clock    = new ClockFeature(_ble);
        _pipe     = new PipeServer();

        _ble.Connected      += OnConnected;
        _ble.Disconnected   += OnDisconnected;
        _tray.ExitRequested += OnExit;
        _clock.SendFailed   += msg => _tray.ShowError("Display", msg);
        _clock.SendDiag     += msg => _tray.ShowInfo("Display diag", msg);

        // Defer startup until Application.Run() installs WindowsFormsSynchronizationContext.
        Application.Idle += OnFirstIdle;
    }

    private void OnFirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnFirstIdle;
        _ble.SetUiContext(SynchronizationContext.Current!);

        // Drain the BLE queue on the UI thread via WinForms Timer.
        // The message loop guarantees STA affinity for GattCharacteristic writes.
        _bleTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _bleTimer.Tick += async (_, _) =>
        {
            while (_bleQueue.TryDequeue(out string? text))
            {
                try
                {
                    _clock.PauseFor(TimeSpan.FromSeconds(30));
                    using var bmp = BitmapTextRenderer.Render(text!);
                    await _ble.SendBitmapAsync(BitmapFrame.Pack(bmp));
                }
                catch { }
            }
        };
        _bleTimer.Start();

        // Pipe server receives text on the thread pool; enqueue and respond OK
        // immediately. The Timer drains the queue on the UI (STA) thread.
        _pipe.Start(text =>
        {
            _bleQueue.Enqueue(text);
            return Task.FromResult(true);
        });

        _ = ConnectLoopAsync(_cts.Token);
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
        _clock.Start();
    }

    private void OnDisconnected()
    {
        _clock.Stop();
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
            _bleTimer?.Stop();
            _bleTimer?.Dispose();
            _pipe.Dispose();
            _clock.Dispose();
            _tray.Dispose();
            _ble.Dispose();
        }
        base.Dispose(disposing);
    }
}
