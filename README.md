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
  to implement to receive any of this. It's a proper west module (see
  `zephyr/module.yml`), any ZMK `zmk-config` can pull it in directly via
  `west.yml`, no forking or copying files. Requires a custom firmware
  build either way, this is not something stock ZMK supports out of the box.

Setting up a keyboard and this app from zero? Start with
**[`docs/getting_started.md`](docs/getting_started.md)**. For everything
else (the Canvas editor, tokens, the CLI) see
**[`docs/user_guide.md`](docs/user_guide.md)**.

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
enabled, this app can't do anything with stock ZMK firmware. What to do
depends on your board:

- **You have an `eyelash_corne` (or any ZMK board with a `nice_view`
  display) and no existing ZMK config**: fork
  [`zmk-companion-template`](https://github.com/oscampo/zmk-companion-template),
  push, download the `.uf2` GitHub Actions builds for you, flash it. No
  local toolchain needed. Full steps:
  **[`docs/getting_started.md`](docs/getting_started.md)**.
- **You already have your own ZMK config repo, any board with a
  `nice_view` display**: add `oscampo/zmk-companion` as a west module to
  your existing `west.yml`, no fork needed. Exact manifest snippet:
  **[`docs/getting_started.md`](docs/getting_started.md#1-firmware-one-time)**.
- **Your board has no `nice_view` display**: no path today, the display
  code is tied to that display's resolution.

The rest of this section is technical background, not something you need to
read to get set up.

<details>
<summary>Protocol details</summary>

This app talks to the firmware over a BLE GATT service. Two protocol
versions exist:

- [`docs/cell_grid_protocol.md`](docs/cell_grid_protocol.md): current
  protocol (cell-grid + bitmap), what the Canvas editor and `zkc.exe`
  actually use, and what both firmware paths above already implement.
- [`docs/protocol.md`](docs/protocol.md): older plain-text-only
  characteristic, still supported for backward compatibility but not what
  new firmware should target.

</details>

## Repository structure

```
zmk-companion/
├── app/
│   ├── ZmkCompanion/        # Windows tray app (WinForms, .NET 8)
│   ├── ZmkCompanionCli/     # zkc.exe — talks to the tray app over a named pipe
│   └── ZmkCompanion.Tests/
├── installer/               # Inno Setup script, builds the .exe installer
├── firmware/                # West module: the display service (zephyr/module.yml at repo root)
├── docs/
│   ├── getting_started.md     # First-time setup, firmware + app, start here
│   ├── user_guide.md          # Canvas editor, tokens, the CLI, troubleshooting
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
