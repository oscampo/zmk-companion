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
        var uiCtx = SynchronizationContext.Current!;

        // Called on the UI thread by TerminalDialog before each send.
        void pauseClock() => _clock.PauseFor(TimeSpan.FromSeconds(30));
        _tray.OnSend = pauseClock;

        // Pipe server receives text on the thread pool. Dispatch everything —
        // clock pause AND the BLE write — to the UI (STA) thread via TCS so
        // GattCharacteristic is never touched from an MTA thread.
        _pipe.Start(async text =>
        {
            TrayLog($"[TRAY] sendText callback: posting to UI thread for '{text}'");
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            uiCtx.Post(async _ =>
            {
                TrayLog("[TRAY] UI thread: pausing clock...");
                try
                {
                    _clock.PauseFor(TimeSpan.FromSeconds(30));
                    TrayLog("[TRAY] UI thread: calling BleService.SendAsync...");
                    bool ok = await _ble.SendAsync(text);
                    TrayLog($"[TRAY] UI thread: SendAsync returned {ok}. Setting TCS.");
                    tcs.SetResult(ok);
                }
                catch (Exception ex)
                {
                    TrayLog($"[TRAY] UI thread: exception: {ex.Message}");
                    tcs.SetException(ex);
                }
            }, null);
            TrayLog("[TRAY] sendText callback: awaiting TCS...");
            bool result = await tcs.Task;
            TrayLog($"[TRAY] sendText callback: TCS completed with {result}");
            return result;
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
            _pipe.Dispose();
            _clock.Dispose();
            _tray.Dispose();
            _ble.Dispose();
        }
        base.Dispose(disposing);
    }
}
