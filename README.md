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
