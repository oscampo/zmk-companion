namespace ZmkCompanion.Core;

sealed class ClockConfig
{
    public bool  Use24h   { get; set; } = false;  // false = follow system (Protocol.Detect12h)
    public bool  ShowAmPm { get; set; } = true;
    public bool  ShowDate { get; set; } = true;
    public float Scale    { get; set; } = 1.0f;   // multiplies time/AM-PM/date font sizes
}

sealed class BatteryConfig
{
    public bool  ShowIcon    { get; set; } = true;
    public bool  ShowPercent { get; set; } = true;
    public float Scale       { get; set; } = 1.0f;  // multiplies icon/label font sizes
}

sealed class ConnectionConfig
{
    public bool  ShowIcon    { get; set; } = true;
    public bool  ShowType    { get; set; } = false;  // "USB" / "BLE" label
    public bool  ShowProfile { get; set; } = true;   // BLE profile number 1-5
    public float Scale       { get; set; } = 1.0f;   // multiplies icon/label font sizes
}

// Generic label widget: template string with {binding} tokens + font/alignment controls.
sealed class LabelConfig
{
    public string Template    { get; set; } = "";
    public float  Size        { get; set; } = 16f;
    public bool   Bold        { get; set; } = false;
    public bool   UseNerdFont { get; set; } = true;   // Fira Code NF (supports glyphs + ASCII)
    public bool   Use24h      { get; set; } = false;  // force 24h for {time} binding
    public string Align       { get; set; } = "center"; // "left" | "center" | "right"
}

// Profile bar widget: 5 numbered boxes, active BLE profile inverted.
sealed class ProfileBarConfig
{
    public float Scale { get; set; } = 1.0f;
}
