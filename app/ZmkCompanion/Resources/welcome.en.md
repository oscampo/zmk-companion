# Welcome to ZMK Companion

This app lives in the **system tray**, next to the Windows clock. There's no main window: right-click its icon to reach everything.

## What it can do

- Clock, weather (multiple cities), time zones, and sports scores on your keyboard's display
- Configurable Pomodoro timer, with per-phase icon pickers
- Custom tokens that external scripts update via `zkc --set`
- Visual page editor for the display (Canvas)
- CLI (`zkc.exe`) for sending text or your own commands, including pipes like `python clock.py | zkc -w`

## Getting started

1. Connect your keyboard over Bluetooth, the app connects on its own once it's detected
2. Right-click the tray icon, **Canvas**, to design what shows on the display
3. Right-click, **Pomodoro**, to configure the timer

## Learn more

Full user guide (editor, tokens, CLI, troubleshooting):

https://github.com/oscampo/zmk-companion/blob/main/docs/user_guide.md

Keyboard doesn't have the required firmware yet? That's a separate setup step, covered in the project's repository:

https://github.com/oscampo/zmk-companion
