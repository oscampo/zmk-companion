using ZmkCompanion.Core;

namespace ZmkCompanion.Features;

// Sends a clock-sync message once at start, then every minute.
sealed class ClockFeature : IDisposable
{
    private readonly BleService _ble;
    private System.Windows.Forms.Timer? _timer;

    public ClockFeature(BleService ble) => _ble = ble;

    public void Start()
    {
        Stop();
        _ = SyncAsync();

        _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _timer.Tick += async (_, _) => await SyncAsync();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    private async Task SyncAsync() =>
        await _ble.SendAsync(Protocol.BuildClock());

    public void Dispose() => Stop();
}
