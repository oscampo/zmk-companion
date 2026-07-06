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

    // Glyph style for digit characters in {time}/{date}/{date.day}/{conn.profile}:
    // "text" | "box" | "box_outline" | "box_multiple" | "box_multiple_outline"
    // | "plain" | "circle" | "circle_outline"  (plain/circle: only digits 1-9)
    public string NumericStyle { get; set; } = "text";

    // Glyph style for letter characters in {date}/{date.month}:
    // "text" | "plain" | "box" | "box_outline" | "circle" | "circle_outline"
    public string AlphaStyle { get; set; } = "text";

    // Battery icon style for {battery.icon}:
    // "nf" = FA 5-tier (default), "md_level" = MD 10-step, "md_threshold" = MD low/med/high
    public string BatteryGlyphStyle { get; set; } = "nf";

    // Glyphs shown for {conn.icon}: one when BLE-connected, one when USB-connected.
    // Default: FA bluetooth and FA USB (both BMP, U+F293 / U+F287)
    public string ConnBleGlyph { get; set; } = "";
    public string ConnUsbGlyph { get; set; } = "";

    // Extra pixels inserted between each character element (negative = tighten).
    public float LetterSpacing { get; set; } = 0f;
}

// Pomodoro widget: timer config + display template for the canvas LabelWidget.
sealed class PomodoroWidgetConfig
{
    public int    WorkMin      { get; set; } = 25;
    public int    BreakMin     { get; set; } = 5;
    public int    Cycles       { get; set; } = 4;
    public int    LongBreakMin { get; set; } = 15;
    // Template rendered by LabelWidget using {pomodoro.*} bindings.
    public string Template     { get; set; } = "{pomodoro.time}\n{pomodoro.icon} {pomodoro.bar}";
    public float  Size         { get; set; } = 20f;
    public bool   Bold         { get; set; } = false;
    public bool   UseNerdFont  { get; set; } = true;
    public string Align        { get; set; } = "center";
}

// Profile bar widget: 5 slots showing BLE profiles 1-5 with three visual states.
// Connected  = the currently active BLE profile.
// Assigned   = paired (bonded) but not currently connected.
// Free       = slot has no paired device.
sealed class ProfileBarConfig
{
    public float Scale { get; set; } = 1.0f;

    // Glyph style for the CONNECTED (currently active) profile.
    // "gdi" = classic GDI+ inverted box; otherwise any NerdFont.NumericGlyph style.
    public string ConnectedStyle { get; set; } = "box";

    // Glyph style for ASSIGNED (paired but not connected) profiles.
    public string AssignedStyle { get; set; } = "plain";

    // Glyph style for FREE (unpaired) profile slots.
    public string FreeStyle { get; set; } = "box_outline";

    // Extra pixels between profile glyphs (negative = tighten).
    public float LetterSpacing { get; set; } = 0f;
}
