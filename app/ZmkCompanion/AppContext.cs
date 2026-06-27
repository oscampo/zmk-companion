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
        _pipe     = new PipeServer(_ble);

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
        // Runs on UI thread — safe to call WinForms timer directly.
        void pauseClock() => _clock.PauseFor(TimeSpan.FromSeconds(30));
        _tray.OnSend = pauseClock;
        // Pipe server runs on thread pool — must post to UI thread.
        var uiCtx = SynchronizationContext.Current!;
        _pipe.Start(() => uiCtx.Post(_ => pauseClock(), null));
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
