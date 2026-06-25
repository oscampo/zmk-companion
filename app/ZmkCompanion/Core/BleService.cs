using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace ZmkCompanion.Core;

// Manages BLE connection to the ZMK keyboard using WinRT APIs.
// Works with already-paired devices (HID profile) — no re-pairing needed.
sealed class BleService : IDisposable
{
    public static readonly Guid ServiceUuid  = new("00001523-1212-efde-1523-785feabcd123");
    public static readonly Guid CharUuid     = new("00001524-1212-efde-1523-785feabcd123");

    private static readonly string[] KeyboardNames = ["zmk", "corne", "eyelash"];

    // Raised on the UI thread via SynchronizationContext.
    public event Action<string>? Connected;
    public event Action? Disconnected;

    public bool IsConnected => _device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
    public string? DeviceName { get; private set; }

    private BluetoothLEDevice? _device;
    private GattCharacteristic? _characteristic;
    private readonly SynchronizationContext _uiContext = SynchronizationContext.Current
        ?? new SynchronizationContext();
    private bool _disposed;

    // ── Discovery ─────────────────────────────────────────────────────────────

    // Finds paired BLE keyboards by name, connects, and caches the characteristic.
    // Returns true if a keyboard was found and reachable.
    public async Task<bool> ScanAndConnectAsync(CancellationToken ct = default)
    {
        _characteristic = null;

        var deviceInfo = await FindKeyboardAsync(ct);
        if (deviceInfo is null)
            return false;

        return await ConnectToDeviceAsync(deviceInfo, ct);
    }

    private static async Task<DeviceInformation?> FindKeyboardAsync(CancellationToken ct)
    {
        // Query paired BLE devices — keyboard is already bonded via HID profile.
        string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(isPaired: true);
        var devices = await DeviceInformation.FindAllAsync(selector).AsTask(ct);

        foreach (var d in devices)
        {
            string name = d.Name.ToLowerInvariant();
            if (KeyboardNames.Any(k => name.Contains(k)))
                return d;
        }
        return null;
    }

    private async Task<bool> ConnectToDeviceAsync(DeviceInformation deviceInfo, CancellationToken ct)
    {
        DisposeDevice();

        _device = await BluetoothLEDevice.FromIdAsync(deviceInfo.Id).AsTask(ct);
        if (_device is null)
            return false;

        _device.ConnectionStatusChanged += OnConnectionStatusChanged;

        // Discover the custom GATT service.
        var svcResult = await _device.GetGattServicesForUuidAsync(ServiceUuid).AsTask(ct);
        if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
        {
            DisposeDevice();
            return false;
        }

        var service = svcResult.Services[0];
        var charResult = await service.GetCharacteristicsForUuidAsync(CharUuid).AsTask(ct);
        if (charResult.Status != GattCommunicationStatus.Success || charResult.Characteristics.Count == 0)
        {
            DisposeDevice();
            return false;
        }

        _characteristic = charResult.Characteristics[0];
        DeviceName = deviceInfo.Name;

        _uiContext.Post(_ => Connected?.Invoke(DeviceName!), null);
        return true;
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    // Returns true if the write succeeded.
    public async Task<bool> SendAsync(string message)
    {
        if (_characteristic is null)
            return false;

        byte[] data = TextConverter.ToBytes(message);

        var writer = new DataWriter();
        writer.WriteBytes(data);
        IBuffer buffer = writer.DetachBuffer();

        try
        {
            var result = await _characteristic.WriteValueWithResultAsync(buffer);
            return result.Status == GattCommunicationStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    // ── Connection events ─────────────────────────────────────────────────────

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            _characteristic = null;
            DeviceName = null;
            _uiContext.Post(_ => Disconnected?.Invoke(), null);
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void DisposeDevice()
    {
        if (_device is not null)
        {
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _device.Dispose();
            _device = null;
        }
        _characteristic = null;
        DeviceName = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeDevice();
    }
}
