# ZMK Companion

A Windows tray app (plus a small companion CLI) that streams live data,
custom text, and full page layouts to a ZMK keyboard's onboard display over
BLE, and the firmware-side reference for keyboards that want to receive it.

## What is this?

zmk-companion has three parts:

- **`ZmkCompanion.exe`** (`app/ZmkCompanion`): the Windows tray app. It has no
  main window, everything happens from its system tray icon. It connects to
  your keyboard over Bluetooth, and lets you design what shows on its display
  using a visual page editor (the "Canvas"): clock, weather (multiple
  cities), time zones, sports scores, a configurable Pomodoro timer, and
  custom text/tokens.
- **`zkc.exe`** (`app/ZmkCompanionCli`): a small standalone CLI that talks to
  the running tray app (over a local named pipe, not directly to BLE) so
  scripts can push their own text or named values to the display. See
  [`docs/user_guide.md`](docs/user_guide.md#cli-zkcexe) for the full command
  reference.
- **Firmware side** (`firmware/`, [`docs/cell_grid_protocol.md`](docs/cell_grid_protocol.md)):
  the BLE GATT service and message protocol a ZMK keyboard's firmware needs
  to implement to receive any of this. Requires a custom firmware build,
  this is not something stock ZMK supports out of the box.

For a full walkthrough (installing, first run, the Canvas editor, tokens,
the CLI) see **[`docs/user_guide.md`](docs/user_guide.md)**.

## Install

Grab the latest installer from the
[Releases page](https://github.com/oscampo/zmk-companion/releases) and run
it, no admin rights needed (it installs per-user). It sets up both
`ZmkCompanion.exe` and `zkc.exe`, launches the tray app on login, and shows
a one-time welcome screen with a quick tour.

Requires Windows 10 2004+ (build 19041) or later, and the
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
(the installer will tell you if it's missing).

## Firmware requirement

Your ZMK keyboard needs a custom build with the display GATT service
enabled, this app can't do anything with stock ZMK firmware. See
[`docs/cell_grid_protocol.md`](docs/cell_grid_protocol.md) for the current
protocol (cell-grid + bitmap, what the Canvas editor and `zkc.exe` actually
use) and [`docs/protocol.md`](docs/protocol.md) for the older plain-text-only
characteristic, still supported for backward compatibility but not what new
firmware should target.

If you're setting up your own keyboard's firmware for the first time, budget
real time for this part, it's the least beginner-friendly step in the whole
setup and currently requires following the ZMK firmware build process by
hand. See [`docs/user_guide.md`](docs/user_guide.md#firmware) for what's
involved.

## Repository structure

```
zmk-companion/
├── app/
│   ├── ZmkCompanion/        # Windows tray app (WinForms, .NET 8)
│   ├── ZmkCompanionCli/     # zkc.exe — talks to the tray app over a named pipe
│   └── ZmkCompanion.Tests/
├── installer/               # Inno Setup script, builds the .exe installer
├── firmware/                # Reference firmware source for the display service
├── docs/
│   ├── cell_grid_protocol.md  # Current BLE protocol (cell-grid + bitmap)
│   └── protocol.md            # Legacy plain-text-only characteristic
└── clients/                 # Older cross-platform Python clients (see below)
```

### About `clients/`

`clients/cli/keyboard_display.py` and `clients/ios/keyboard_display_ios.py`
predate `ZmkCompanion.exe`/`zkc.exe` and only speak the legacy plain-text
characteristic (`docs/protocol.md`), not the current cell-grid/bitmap
protocol the Canvas editor uses. They still work for basic text, but they're
not being actively developed, and macOS/Linux/iOS users should not expect
feature parity with the Windows app. Treat them as a starting point if you
want to build a client for a non-Windows platform, not as a maintained
product.

## Contributing

Public repo, contributions welcome. Fork, branch, open a PR against `main`.
CI (`.github/workflows/build.yml`) builds and tests on every PR. Releases
are cut from `vX.Y.Z` git tags by a maintainer after merging, see the
workflow file if you're curious how that's wired up.

## License

MIT
