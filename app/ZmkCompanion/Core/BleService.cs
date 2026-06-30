using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace ZmkCompanion.Core;

// Manages BLE connection to the ZMK keyboard using WinRT APIs.
// Works with already-paired devices (HID profile) — no re-pairing needed.
sealed class BleService : IDisposable
{
    public static readonly Guid ServiceUuid     = new("00001523-1212-efde-1523-785feabcd123");
    public static readonly Guid CharUuid        = new("00001524-1212-efde-1523-785feabcd123");
    public static readonly Guid BitmapCharUuid  = new("00001525-1212-efde-1523-785feabcd123");

    private static readonly string[] KeyboardNames = ["zmk", "corne", "eyelash"];

    // Raised on the UI thread via SynchronizationContext.
    public event Action<string>? Connected;
    public event Action? Disconnected;

    public bool IsConnected   => _device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
    public bool HasBitmapChar => _bitmapCharacteristic is not null;
    public string? DeviceName { get; private set; }

    private BluetoothLEDevice? _device;
    private GattCharacteristic? _characteristic;
    private GattCharacteristic? _bitmapCharacteristic;
    private SynchronizationContext _uiContext = SynchronizationContext.Current
        ?? new SynchronizationContext();
    private bool _disposed;

    // Called once after Application.Run() installs the real WindowsFormsSynchronizationContext.
    internal void SetUiContext(SynchronizationContext ctx) => _uiContext = ctx;

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
        string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
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

        // Discover the custom GATT service — bypass Windows GATT cache so firmware
        // updates (new/removed characteristics) are picked up without re-pairing.
        var svcResult = await _device
            .GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached).AsTask(ct);
        if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
        {
            DisposeDevice();
            return false;
        }

        var service = svcResult.Services[0];

        // 0x1524 — legacy text characteristic (older firmware); optional.
        var charResult = await service
            .GetCharacteristicsForUuidAsync(CharUuid, BluetoothCacheMode.Uncached).AsTask(ct);
        if (charResult.Status == GattCommunicationStatus.Success && charResult.Characteristics.Count > 0)
            _characteristic = charResult.Characteristics[0];

        // 0x1525 — bitmap display characteristic (current firmware); optional.
        var bmpResult = await service
            .GetCharacteristicsForUuidAsync(BitmapCharUuid, BluetoothCacheMode.Uncached).AsTask(ct);
        if (bmpResult.Status == GattCommunicationStatus.Success && bmpResult.Characteristics.Count > 0)
            _bitmapCharacteristic = bmpResult.Characteristics[0];

        // Need at least one usable characteristic; otherwise it's the wrong device.
        if (_characteristic is null && _bitmapCharacteristic is null)
        {
            DisposeDevice();
            return false;
        }

        DeviceName = deviceInfo.Name;

        _uiContext.Post(_ => Connected?.Invoke(DeviceName!), null);
        return true;
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    // Sends a legacy text message via characteristic 0x1524 (older firmware only).
    // Must be called from the UI (STA) thread.
    public async Task<bool> SendAsync(string message)
    {
        var ch = _characteristic;
        if (ch is null) return false;

        byte[] data = TextConverter.ToBytes(message);
        var dw = new DataWriter();
        dw.WriteBytes(data);
        try
        {
            var result = await ch.WriteValueWithResultAsync(dw.DetachBuffer());
            return result.Status == GattCommunicationStatus.Success;
        }
        catch { return false; }
    }

    // Diagnostic string set on every SendBitmapAsync call; null on success.
    public string? LastBitmapError { get; private set; }

    // Sends a 1,440-byte bitmap frame via characteristic 0x1525 in 240-byte chunks.
    // Header per chunk: [2B offset LE][2B total LE][data].
    // Must be called from the UI (STA) thread.
    public async Task<bool> SendBitmapAsync(byte[] frame)
    {
        var ch = _bitmapCharacteristic;
        if (ch is null) { LastBitmapError = "char is null"; return false; }

        // Choose write type based on what the characteristic actually declares.
        var props = ch.CharacteristicProperties;
        GattWriteOption writeOpt;
        if (props.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
            writeOpt = GattWriteOption.WriteWithoutResponse;
        else if (props.HasFlag(GattCharacteristicProperties.Write))
            writeOpt = GattWriteOption.WriteWithResponse;
        else
        {
            LastBitmapError = $"props={props} — no write permission on 0x1525";
            return false;
        }

        const int chunkData = 240;
        ushort total = (ushort)frame.Length;

        for (int offset = 0; offset < frame.Length; offset += chunkData)
        {
            int len = Math.Min(chunkData, frame.Length - offset);
            var dw = new DataWriter { ByteOrder = ByteOrder.LittleEndian };
            dw.WriteUInt16((ushort)offset);
            dw.WriteUInt16(total);
            dw.WriteBytes(frame[offset..(offset + len)]);
            try
            {
                var result = await ch.WriteValueWithResultAsync(dw.DetachBuffer(), writeOpt);
                if (result.Status != GattCommunicationStatus.Success)
                {
                    LastBitmapError = $"chunk@{offset} status={result.Status} proto={result.ProtocolError} opt={writeOpt} props={props}";
                    return false;
                }
            }
            catch (Exception ex) { LastBitmapError = $"chunk@{offset} ex={ex.Message}"; return false; }
        }
        LastBitmapError = null;
        return true;
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
        _characteristic       = null;
        _bitmapCharacteristic = null;
        DeviceName            = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeDevice();
    }
}
