using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace ZmkCompanion.Core;

// Manages BLE connection to the ZMK keyboard using WinRT APIs.
// Works with already-paired devices (HID profile) — no re-pairing needed.
sealed class BleService : IDisposable
{
    public static readonly Guid ServiceUuid      = new("00001523-1212-efde-1523-785feabcd123");
    public static readonly Guid CharUuid         = new("00001524-1212-efde-1523-785feabcd123");
    public static readonly Guid BitmapCharUuid   = new("00001525-1212-efde-1523-785feabcd123");
    public static readonly Guid StatusCharUuid   = new("00001526-1212-efde-1523-785feabcd123");
    public static readonly Guid CellGridCharUuid = new("00001527-1212-efde-1523-785feabcd123");

    private static readonly string[] KeyboardNames = ["zmk", "corne", "eyelash"];

    // Raised on the UI thread via SynchronizationContext.
    public event Action<string>? Connected;
    public event Action?         Disconnected;
    // Battery level 0-100 from BAS 0x180F/0x2A19.
    public event Action<byte>?   BatteryLevelChanged;
    // Status bytes from 0x1526:
    //   byte 0 — bit 0 = USB-HID active, bits[3:1] = BLE profile (0-4)
    //   byte 1 — bits[4:0] = bond mask (bit i = profile i+1 has a paired device); 0xFF if absent (old firmware)
    public event Action<byte, byte>?   StatusChanged;

    public bool IsConnected     => _device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
    public bool HasBitmapChar   => _bitmapCharacteristic is not null;
    public bool HasCellGridChar => _cellGridCharacteristic is not null;
    public string? DeviceName { get; private set; }

    // Connection-stability diagnostics: a link that drops and reconnects
    // repeatedly freezes the display for the whole gap (rendering stops on
    // disconnect, and reconnect requires a fresh scan + full GATT discovery),
    // which can look exactly like clock drift when sampled at low frequency.
    public int       DisconnectCount { get; private set; }
    public DateTime? LastDisconnectAt { get; private set; }
    public TimeSpan? LastDowntime     { get; private set; }

    private BluetoothLEDevice? _device;
    private GattSession?        _session;
    private GattCharacteristic? _characteristic;
    private GattCharacteristic? _bitmapCharacteristic;
    private GattCharacteristic? _cellGridCharacteristic;
    private GattCharacteristic? _batteryCharacteristic;
    private GattCharacteristic? _statusCharacteristic;
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

        // Open a GattSession so we can read MaxPduSize (the negotiated ATT MTU).
        // MaintainConnection keeps the link alive between writes.
        _session = await GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId).AsTask(ct);
        _session.MaintainConnection = true;

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

        // 0x1527 — cell-grid display characteristic (newest firmware); optional.
        var cellResult = await service
            .GetCharacteristicsForUuidAsync(CellGridCharUuid, BluetoothCacheMode.Uncached).AsTask(ct);
        if (cellResult.Status == GattCommunicationStatus.Success && cellResult.Characteristics.Count > 0)
            _cellGridCharacteristic = cellResult.Characteristics[0];

        // Need at least one usable characteristic; otherwise it's the wrong device.
        if (_characteristic is null && _bitmapCharacteristic is null)
        {
            DisposeDevice();
            return false;
        }

        // 0x1526 — status: byte0 = USB+profile, byte1 (optional) = bond mask.
        var statusResult = await service
            .GetCharacteristicsForUuidAsync(StatusCharUuid, BluetoothCacheMode.Uncached).AsTask(ct);
        if (statusResult.Status == GattCommunicationStatus.Success && statusResult.Characteristics.Count > 0)
        {
            _statusCharacteristic = statusResult.Characteristics[0];
            try
            {
                var read = await _statusCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                if (read.Status == GattCommunicationStatus.Success && read.Value.Length >= 1)
                {
                    var r = DataReader.FromBuffer(read.Value);
                    byte s = r.ReadByte();
                    byte m = r.UnconsumedBufferLength >= 1 ? r.ReadByte() : (byte)0xFF;
                    _uiContext.Post(_ => StatusChanged?.Invoke(s, m), null);
                }
            }
            catch { }
            try
            {
                var notify = await _statusCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
                if (notify == GattCommunicationStatus.Success)
                    _statusCharacteristic.ValueChanged += OnStatusValueChanged;
            }
            catch { }
        }

        // BAS 0x180F / 0x2A19 — standard battery level with notify.
        var basSvcResult = await _device
            .GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached).AsTask(ct);
        if (basSvcResult.Status == GattCommunicationStatus.Success && basSvcResult.Services.Count > 0)
        {
            var basCharResult = await basSvcResult.Services[0]
                .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached)
                .AsTask(ct);
            if (basCharResult.Status == GattCommunicationStatus.Success && basCharResult.Characteristics.Count > 0)
            {
                _batteryCharacteristic = basCharResult.Characteristics[0];
                await SubscribeAndReadByteAsync(_batteryCharacteristic,
                    OnBatteryValueChanged,
                    b => _uiContext.Post(_ => BatteryLevelChanged?.Invoke(b), null));
            }
        }

        DeviceName = deviceInfo.Name;

        if (LastDisconnectAt is { } since) LastDowntime = DateTime.Now - since;
        _uiContext.Post(_ => Connected?.Invoke(DeviceName!), null);
        return true;
    }

    // Reads the initial byte value and subscribes to notifications.
    // onInitial fires once with the current value; handler fires on each notification.
    private static async Task SubscribeAndReadByteAsync(
        GattCharacteristic ch,
        TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler,
        Action<byte> onInitial)
    {
        try
        {
            var read = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
            if (read.Status == GattCommunicationStatus.Success && read.Value.Length > 0)
                onInitial(DataReader.FromBuffer(read.Value).ReadByte());
        }
        catch { }

        try
        {
            var notify = await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (notify == GattCommunicationStatus.Success)
                ch.ValueChanged += handler;
        }
        catch { }
    }

    private void OnBatteryValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (args.CharacteristicValue.Length < 1) return;
        byte level = DataReader.FromBuffer(args.CharacteristicValue).ReadByte();
        _uiContext.Post(_ => BatteryLevelChanged?.Invoke(level), null);
    }

    private void OnStatusValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (args.CharacteristicValue.Length < 1) return;
        var  r      = DataReader.FromBuffer(args.CharacteristicValue);
        byte status = r.ReadByte();
        byte bonds  = r.UnconsumedBufferLength >= 1 ? r.ReadByte() : (byte)0xFF;
        _uiContext.Post(_ => StatusChanged?.Invoke(status, bonds), null);
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

    // Diagnostics set on every SendBitmapAsync call.
    public string? LastBitmapError  { get; private set; }
    public int     LastChunkCount   { get; private set; }
    public int     LastMtu          { get; private set; }
    public long     LastSendMs      { get; private set; }
    public bool     LastWithResponse { get; private set; }

    // Sends a 1,440-byte bitmap frame via characteristic 0x1525 in 240-byte chunks.
    // Header per chunk: [2B offset LE][2B total LE][data].
    // preferSpeed=true uses WriteWithoutResponse (fire-and-forget, ~30-50ms total)
    // for latency-sensitive streaming; false (default) uses WriteWithResponse for
    // reliable delivery where a dropped chunk would be worse than extra latency.
    // Must be called from the UI (STA) thread.
    public async Task<bool> SendBitmapAsync(byte[] frame, bool preferSpeed = false)
    {
        var ch = _bitmapCharacteristic;
        if (ch is null) { LastBitmapError = "char is null"; return false; }

        var props = ch.CharacteristicProperties;
        GattWriteOption writeOpt;
        if (preferSpeed && props.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
            writeOpt = GattWriteOption.WriteWithoutResponse;
        else if (props.HasFlag(GattCharacteristicProperties.Write))
            writeOpt = GattWriteOption.WriteWithResponse;
        else if (props.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
            writeOpt = GattWriteOption.WriteWithoutResponse;
        else
        {
            LastBitmapError = $"props={props} — no write permission on 0x1525";
            return false;
        }

        // MaxPduSize = negotiated ATT MTU. Max write payload = MTU - 3 (ATT header).
        // Minus our 4-byte chunk header leaves the usable data bytes per packet.
        // Falls back to 16 bytes (fits in the 23-byte default MTU) if session is null.
        int mtu       = _session?.MaxPduSize ?? 23;
        int chunkData = Math.Max(1, mtu - 3 - 4);
        ushort total  = (ushort)frame.Length;
        LastMtu          = mtu;
        LastChunkCount   = (int)Math.Ceiling((double)frame.Length / chunkData);
        LastWithResponse = writeOpt == GattWriteOption.WriteWithResponse;

        var sw = System.Diagnostics.Stopwatch.StartNew();
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
                    LastBitmapError = $"chunk@{offset}/{chunkData}B mtu={mtu} status={result.Status} proto={result.ProtocolError} opt={writeOpt} props={props}";
                    LastSendMs = sw.ElapsedMilliseconds;
                    return false;
                }
            }
            catch (Exception ex)
            {
                LastBitmapError = $"chunk@{offset} ex={ex.Message}";
                LastSendMs = sw.ElapsedMilliseconds;
                return false;
            }
        }
        LastSendMs      = sw.ElapsedMilliseconds;
        LastBitmapError = null;
        return true;
    }

    // ── Cell-grid protocol (0x1527, docs/cell_grid_protocol.md) ──────────────

    public string? LastCellGridError { get; private set; }

    // Sends one protocol message (LAYOUT/CELL/CLEAR). Always WriteWithResponse:
    // every message is small enough for a single acked write at MTU 65, and the
    // whole point of the protocol is that delivery failures are visible.
    // Must be called from the UI (STA) thread.
    public async Task<bool> SendCellGridAsync(byte[] message)
    {
        var ch = _cellGridCharacteristic;
        if (ch is null) { LastCellGridError = "0x1527 not found — firmware too old?"; return false; }

        var dw = new DataWriter();
        dw.WriteBytes(message);
        try
        {
            var result = await ch.WriteValueWithResultAsync(
                dw.DetachBuffer(), GattWriteOption.WriteWithResponse);
            if (result.Status != GattCommunicationStatus.Success)
            {
                LastCellGridError = $"msg 0x{message[0]:X2} len={message.Length} status={result.Status} proto={result.ProtocolError}";
                return false;
            }
        }
        catch (Exception ex)
        {
            LastCellGridError = $"msg 0x{message[0]:X2} ex={ex.Message}";
            return false;
        }
        LastCellGridError = null;
        return true;
    }

    // ── Connection events ─────────────────────────────────────────────────────

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            _characteristic = null;
            DeviceName = null;
            DisconnectCount++;
            LastDisconnectAt = DateTime.Now;
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
        _session?.Dispose();
        _session                = null;
        _characteristic         = null;
        _bitmapCharacteristic   = null;
        _cellGridCharacteristic = null;

        if (_batteryCharacteristic is not null)
        {
            _batteryCharacteristic.ValueChanged -= OnBatteryValueChanged;
            _batteryCharacteristic = null;
        }
        if (_statusCharacteristic is not null)
        {
            _statusCharacteristic.ValueChanged -= OnStatusValueChanged;
            _statusCharacteristic = null;
        }

        DeviceName = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeDevice();
    }
}
