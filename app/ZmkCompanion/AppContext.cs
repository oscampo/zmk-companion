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

    // Cancellation for the background connect loop
    private readonly CancellationTokenSource _cts = new();

    // BLE writes are STA-affine; pipe server runs on thread pool.
    // Queue text here; drain on the UI thread via a WinForms Timer.
    // Pipe responds OK immediately — no TCS cross-thread signaling needed.
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

        // Defer startup until Application.Run() installs WindowsFormsSynchronizationContext.
        // This ensures BleService events fire on the UI thread and PipeServer awaits resume
        // on the STA thread — preventing COM/WinRT marshaling deadlocks.
        Application.Idle += OnFirstIdle;
    }

    private void OnFirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnFirstIdle;
        _ble.SetUiContext(SynchronizationContext.Current!);

        // Called on the UI thread by TerminalDialog before each send.
        _tray.OnSend = () => _clock.PauseFor(TimeSpan.FromSeconds(30));

        // Drain the BLE queue on the UI thread via WinForms Timer.
        // The message loop guarantees STA affinity for GattCharacteristic writes.
        _bleTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _bleTimer.Tick += async (_, _) =>
        {
            while (_bleQueue.TryDequeue(out string? text))
            {
                TrayLog($"[TRAY] Timer: sending '{text}'");
                try
                {
                    _clock.PauseFor(TimeSpan.FromSeconds(30));
                    bool ok = await _ble.SendAsync(text!);
                    TrayLog($"[TRAY] Timer: SendAsync={ok}");
                }
                catch (Exception ex) { TrayLog($"[TRAY] Timer: ex={ex.Message}"); }
            }
        };
        _bleTimer.Start();
        TrayLog("[TRAY] BLE timer started.");

        // Pipe server receives text on the thread pool. Enqueue it and respond
        // OK immediately — no waiting for BLE completion. This prevents the
        // pipe from blocking if a GATT write takes longer than expected.
        _pipe.Start(text =>
        {
            TrayLog($"[TRAY] Pipe: enqueuing '{text}'");
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
            // Keyboard not advertising yet — retry in 15 s
            await Task.Delay(15_000, ct).ContinueWith(_ => { }); // swallow cancel
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
        // Keyboard switched BT profile — wait for it to re-advertise
        _ = Task.Delay(10_000).ContinueWith(async _ =>
        {
            if (!_ble.IsConnected && !_cts.IsCancellationRequested)
                await _ble.ScanAndConnectAsync(_cts.Token);
        });
    }

    private static void TrayLog(string msg)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "zkc-tray.log"), line + Environment.NewLine); } catch { }
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
