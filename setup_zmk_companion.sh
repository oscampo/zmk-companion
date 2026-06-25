#!/usr/bin/env bash
# Run this from an empty directory or inside your cloned zmk-companion repo
# Usage: bash setup_zmk_companion.sh

set -e
echo "Creating zmk-companion structure..."

mkdir -p clients/cli clients/ios docs

# ── README ────────────────────────────────────────────────────────────────────
cat > README.md << 'EOF'
# zmk-companion

Companion apps and firmware reference for ZMK keyboards with custom display support.

## What is this?

zmk-companion lets you send real-time data from your devices to your ZMK keyboard display via BLE:

- **Clock** — syncs local time, shows hh:mm with 12h/24h auto-detection
- **Weather** — current conditions via Open-Meteo (no API key needed)
- **Pomodoro** — countdown timer with progress bar (classic/short/long/custom)
- **NFL** — last results, upcoming schedule, live scores (via ESPN API)
- **Text** — any custom text with optional Nerd Font icon

## Repository Structure

```
zmk-companion/
├── clients/
│   ├── cli/
│   │   └── keyboard_display.py     # Windows/Mac/Linux CLI (Python + bleak)
│   └── ios/
│       └── keyboard_display_ios.py # iPad/iPhone (Pythonista app)
└── docs/
    └── protocol.md                 # BLE GATT protocol specification
```

## Quick Start

### CLI (Windows/Mac/Linux)

```bash
pip install bleak
python clients/cli/keyboard_display.py --clock
python clients/cli/keyboard_display.py --weather
python clients/cli/keyboard_display.py --nfl KC --live
python clients/cli/keyboard_display.py --pomodoro classic
```

### iOS (Pythonista)

1. Install [Pythonista](https://omz-software.com/pythonista/) on your iPad/iPhone
2. Copy `clients/ios/keyboard_display_ios.py` into Pythonista
3. Run it — follow the on-screen instructions

**Connection flow:**
1. Press `BT_SEL 1` on your keyboard (disconnects from iPad, starts advertising on profile 1)
2. Open the Pythonista app — it auto-connects
3. Use the UI to control the display
4. Press `BT_SEL 0` to return the keyboard to your iPad

## Firmware Requirement

Your ZMK keyboard must have the custom GATT display service enabled. See [`docs/protocol.md`](docs/protocol.md) for the full specification and UUIDs.

## License

MIT
EOF

# ── Protocol doc ──────────────────────────────────────────────────────────────
cat > docs/protocol.md << 'EOF'
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
EOF

echo "✓ Created README.md and docs/protocol.md"
echo ""
echo "⚠  For the Python client files, copy from your zmk-new_corne repo:"
echo "   cp ../zmk-new_corne/scripts/keyboard_display.py     clients/cli/"
echo "   cp ../zmk-new_corne/scripts/keyboard_display_ios.py clients/ios/"
echo ""
echo "Then:"
echo "   git add ."
echo "   git commit -m 'feat: initial project structure with clients and protocol docs'"
echo "   git push"
