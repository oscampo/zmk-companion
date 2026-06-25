# ZMK Companion BLE Protocol

## GATT Service

| Field | Value |
|---|---|
| Service UUID | `00001523-1212-EFDE-1523-785FEABCD123` |
| Characteristic UUID | `00001524-1212-EFDE-1523-785FEABCD123` |
| Characteristic properties | WRITE, WRITE_WITHOUT_RESPONSE |
| Max payload | 64 bytes (UTF-8) |

## Connection Flow (BT_SEL Profile Workflow)

- **Profile 0**: Host computer/iPad HID (never disconnect)
- **Profile 1**: Companion app connection

1. Press `BT_SEL 1` → keyboard advertises on profile 1
2. Companion app scans and connects
3. Send data to the display characteristic
4. Press `BT_SEL 0` → keyboard reconnects to host

## Message Format

### Clock Sync
```
T:<local_unix>:A    # 12-hour clock (hh:mm a/p)
T:<local_unix>:H    # 24-hour clock (hh:mm)
```
- `<local_unix>` = UTC timestamp + local offset in seconds
- Firmware computes `local_unix % 86400` for seconds-since-midnight
- Sending this sets the keyboard to clock mode

### Text Display
```
<text>              # Plain text, up to 3 lines separated by \n
<text>\x01<icon>    # Text with Nerd Font icon
```
- `\x01` separates main text from icon glyph
- Sending empty string clears display (returns to clock if synced)

### Weather (right-side display)
```
W:<city>\n<temp>\n<label>\x01<icon>
W:                  # clears weather
```

## Supported Font Ranges

| Range | Description |
|---|---|
| U+0020–U+007F | Basic Latin |
| U+00A1–U+00FF | Latin-1 Supplement |
| U+E0A0–U+E0D4 | Powerline |
| U+E200–U+E2A9 | Font Awesome Extension |
| U+E300–U+E3FF | Weather icons |
| U+EE00–U+EE0F | FiraCode progress bar |
| U+F000–U+F2E0 | Font Awesome |

## Key Nerd Font Codepoints

| Codepoint | Description |
|---|---|
| U+EE00–U+EE05 | FiraCode progress bar |
| U+E30D | Weather: sunny |
| U+E309 | Weather: rain |
| U+F091 | Trophy (NFL final) |
| U+F0E7 | Bolt (NFL live) |
| U+F0E3 | Gavel (Pomodoro work) |
| U+F0F4 | Coffee (Pomodoro break) |
| U+F254 | Hourglass (Pomodoro long break) |
