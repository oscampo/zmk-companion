namespace ZmkCompanion.Core;

sealed class ClockConfig
{
    public bool Use24h      { get; set; } = false;  // false = follow system (Protocol.Detect12h)
    public bool ShowAmPm    { get; set; } = true;
    public bool ShowDate    { get; set; } = true;
    public bool ShowSeconds { get; set; } = false;
}

sealed class BatteryConfig
{
    public bool ShowIcon    { get; set; } = true;
    public bool ShowPercent { get; set; } = true;
}

sealed class ConnectionConfig
{
    public bool ShowIcon    { get; set; } = true;
    public bool ShowType    { get; set; } = false;  // "USB" / "BLE" label
    public bool ShowProfile { get; set; } = true;   // BLE profile number 1-5
}
