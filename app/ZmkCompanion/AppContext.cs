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
    // Queue items here and drain on the UI thread via a WinForms Timer.
    private readonly ConcurrentQueue<(string text, TaskCompletionSource<bool> tcs)> _bleQueue = new();
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

        // Drain the BLE queue on the UI thread via WinForms Timer so the
        // message loop guarantees STA affinity — uiCtx.Post is unreliable in
        // tray-only apps (no visible Form to pump messages through Post).
        _bleTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _bleTimer.Tick += async (_, _) =>
        {
            while (_bleQueue.TryDequeue(out var item))
            {
                try
                {
                    TrayLog($"[TRAY] Timer: processing '{item.text}'");
                    _clock.PauseFor(TimeSpan.FromSeconds(30));
                    bool ok = await _ble.SendAsync(item.text);
                    TrayLog($"[TRAY] Timer: SendAsync returned {ok}");
                    item.tcs.SetResult(ok);
                }
                catch (Exception ex) { item.tcs.SetException(ex); }
            }
        };
        _bleTimer.Start();

        // Pipe server receives text on the thread pool — enqueue and return TCS.
        // The timer above drains the queue on the UI (STA) thread.
        _pipe.Start(text =>
        {
            TrayLog($"[TRAY] Enqueuing '{text}'");
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _bleQueue.Enqueue((text, tcs));
            return tcs.Task;
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
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "zkc-debug.log"), line + Environment.NewLine); } catch { }
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
