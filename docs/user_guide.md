# ZMK Companion — User Guide

This covers the Windows tray app (`ZmkCompanion.exe`) and its CLI
(`zkc.exe`). If you're setting up the firmware side of a keyboard from
scratch, see [Firmware](#firmware) near the end, that part is separate from
everything else here.

## Install

1. Download the latest installer from the
   [Releases page](https://github.com/oscampo/zmk-companion/releases).
2. Run it. It installs per-user (no admin prompt) to
   `%LOCALAPPDATA%\ZmkCompanion`, sets up both `ZmkCompanion.exe` and
   `zkc.exe`, and launches the tray app automatically (and on every login
   after that).
3. Requires Windows 10 build 19041+ and the
   [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
   The installer checks for this and tells you if it's missing.

You'll know it's running when a small colored circle appears in your system
tray: **orange** while searching for your keyboard, **green** once
connected.

## First run

The first time you install (or update to a new version), a welcome screen
appears automatically with a short tour. Check "Don't show again" to skip it
next time, it comes back on its own the next time the app updates to a new
version, so you don't miss what changed. You can always reopen it from the
tray menu: right-click the icon → **Help…**.

## The tray menu

There's no main window, everything lives behind a right-click on the tray
icon:

| Item | What it does |
|---|---|
| **Canvas…** | Opens the page editor (see below). Only enabled once connected to your keyboard. |
| **Custom tokens…** | Declare `{custom.NAME}` names/categories ahead of time so they show up in the Canvas editor's token picker. Not required, `zkc --set` works without declaring anything first. |
| **Pomodoro — Start/Stop** | Only enabled once a page uses a `{pomodoro.*}` token. |
| **Configure Pomodoro…** | Durations, cycle count, and per-phase icons. |
| **Reconnect / Disconnect** | Manual BLE connection control. |
| **Idioma / Language** | Switches the whole app's UI between Spanish and English immediately. |
| **Debug Log** | Opens the plain-text log file used for troubleshooting. |
| **Help…** | Reopens the welcome screen. |
| **Acerca de… / About…** | Version number. |
| **Salir / Exit** | Quits the tray app (and disconnects). |

## The Canvas editor

This is where you design what actually shows on your keyboard's display.
Open it from the tray menu (**Canvas…**).

### Pages

A display is a list of **pages**, each with a name and a duration in
seconds. If "Cycle pages" is checked, the app rotates through all of them
automatically; otherwise it stays on whichever page you last selected.

### Rows

Each page is a stack of **rows**, top to bottom. A row has:

- **Tier**: the cell size for that row, in pixels. Bigger tiers show fewer
  characters per row but larger text/icons. There are rectangular tiers
  (`small_impar`, `large_par`, etc.) and square tiers (`*_sq_*`, better for
  icon-heavy rows), plus `icon_half`, a split tier: two consecutive rows
  set to `icon_half` (one "Top half", one "Bottom half") together display a
  single full-size 22×22 icon. The "+ Icon pair" button sets this up for
  you.
- **Template**: the row's content. Plain text, or `{tokens}` that expand to
  live data (see below). Use the "Insert" dropdown to browse available
  tokens by category, or the "NF…" button to insert a literal Nerd Font
  icon glyph via the glyph picker.
- **Align**: left / center / right, within the row's available columns.
- **Bold**, **AA** (anti-aliased vs. crisp 1-bit rendering).
- **Num** / **Alpha**: render digits or letters as styled glyphs (boxed,
  circled, etc.) instead of the tier's default rendering, useful for things
  like `{conn.profilebar}`.
- **Font**: which embedded FiraCode Nerd Font variant this row renders
  with, **Mono**, **Regular**, or **Propo**. Mono forces every glyph
  (including icons) into a single monospace advance width, which shrinks
  icon-heavy glyphs and box/circle glyphs to fit; Regular and Propo let
  each glyph keep its natural size. If icons in a row look undersized or a
  row of box/circle glyphs (like `{conn.profilebar}`) looks inconsistent,
  try switching that row's font. Inserting `{conn.profilebar}` switches its
  row to Propo automatically, since that's what that token needs.

The right-hand preview updates live as you edit, at 3x scale, with a grid
overlay showing cell and tier boundaries.

### Available tokens (by category)

Browse the full, current list from the editor itself (the Insert dropdown),
it stays in sync with the app automatically. Broad categories:

- **Time**: `{time}`, `{date}`, 12h/24h auto-detected from Windows' locale
  setting, plus foreign time zones via the Time Zone tab (`{time:ID}`).
- **Weather**: `{weather}`, `{weather.temp}`, `{weather.icon}`, etc., for up
  to 4 cities (Weather tab), using `:CityName` to target a specific one
  beyond the first/default. Icons switch between day/night variants
  automatically based on that city's own local time, not yours.
- **Battery / Connection**: `{battery.percent}`, `{conn.profilebar}` (BLE
  profile status, 5 slots), and similar.
- **Sports**: live scores, last/next game, league name, for any league
  configured in the Sports tab (NFL, NBA, NHL, and a long list of soccer
  leagues, see the "Edit leagues…" picker).
- **Pomodoro**: `{pomodoro.time}`, `{pomodoro.bar}` (progress bar),
  `{pomodoro.icon}` (current phase icon), `{pomodoro.cycle}`.
- **Custom**: `{custom.NAME}`, pushed from external scripts via
  `zkc --set NAME value` (see [CLI](#cli-zkcexe)).
- **CLI text**: `{ext.text}` / `{ext.text.N}`, whatever `zkc "..."` or
  `zkc --watch` last sent, either as a full-screen bitmap override or
  routed into a specific row, depending on which token the active page
  uses.

### Data source tabs (bottom-left)

- **Library**: save/load/delete whole page sets as named presets.
- **Time Zone**: pick additional IANA time zones to make available as
  `{time:ID}`/`{date:ID}` tokens.
- **Weather**: add up to 4 cities, and choose °C/°F.
- **Sports**: pick which leagues to track and, per league, an optional team
  filter.
- **CLI**: see [below](#cli-zkcexe), this is also where you launch `zkc.exe`
  from the app itself.

Click **Apply** to save and push your changes live; **Close** discards
anything not applied.

## Pomodoro

Configure from the tray menu (**Configure Pomodoro…**): work/break/long
break durations, number of cycles, and three presets (Classic/Short/Long).
Each phase (work, break, long break) has its own icon, click its button to
pick one from the same glyph picker used in the Canvas editor. Start/stop
from the tray once a page on your display uses a `{pomodoro.*}` token.

## CLI (`zkc.exe`)

`zkc.exe` is a separate, dependency-free executable that talks to the
already-running tray app over a local named pipe, it does not touch BLE
directly. The tray app must be running for it to work.

```
zkc "text"              Send text to the display (persists until the next update)
zkc ""                  Clear the text and restore the Canvas page
zkc --watch             Read lines from stdin, send each one live
zkc -w                  Alias for --watch
zkc --set NAME "val"    Set a named {custom.NAME} token
zkc --set NAME --watch  Read lines from stdin, updating {custom.NAME} live
zkc --help              Show this help
```

Examples:

```bash
zkc "Hello world"
zkc "Line1\nLine2\nLine3"
echo "score: 3-1" | zkc --watch
python clock.py | zkc --watch
zkc "Battery: \{battery.percent\}"
zkc --set cpu_temp "45C"
sensors.sh | zkc --set cpu_temp --watch
```

Notes:

- Use `\n` inside quoted strings for multi-line text; `--watch` also treats
  a bare `\r` as a line separator, so scripts that overwrite a terminal line
  in place work without changes.
- Escaped tokens like `\{battery.percent\}` resolve to their live value
  before display; unescaped `{like this}` shows as literal text. An unknown
  token is left as `{key}` unresolved, a typo shows up visibly instead of
  silently vanishing.
- `{custom.NAME}` works from `zkc --set` immediately, declaring it from the
  tray's **Custom tokens…** menu is only so it shows up in the editor's
  token picker, not required for it to work.
- `NAME` may only use `a-z`, `0-9`, `_`.

Whether a page shows `zkc` text as a full-screen override or routes it into
a specific row depends entirely on which token (`{ext.text}` vs.
`{ext.text.N}`) that page's template uses, see the tokens list above.

- **Changing text while pages are cycling**: `{ext.text.N}` picks up whatever
  you last sent the next time its page comes around in the cycle, even if a
  different page was on screen when you ran `zkc`. `{ext.text}` (bare, the
  full-screen override) is different: it only takes effect if its page is the
  one on screen at that exact moment, a different active page drops it. If
  you're not sure which page is active and you're using bare `{ext.text}`,
  either run the command again once it's that page's turn, or send it twice a
  few seconds apart so one of the two lands during its window. None of this
  applies to a continuous stream (`zkc --watch` fed by a script that updates
  every second or so, a clock, for example): a miss just means the next
  update a moment later corrects it.

The Canvas editor's **CLI** tab also has a command field and a "Lanzar
zkc"/"Launch zkc" button: type a full command line there (it can be
anything a shell would run, including a pipeline like
`python clock.py | zkc -w`, not just `zkc` arguments) and it launches in a
terminal exactly as typed. Leave it blank and the button opens a terminal
with `zkc -h` instead. Whatever you last typed there is remembered across
sessions.

### Running your scripts automatically

`{ext.text}`/`{ext.text.N}`/`{custom.NAME}` have no data source of their
own, unlike weather or the clock, nothing shows until whatever script feeds
them has run at least once. The **"Inicio automático" / "Auto-start"** tab
next to CLI lets you register named commands (a daily-phrase sender, a
sensor script piped into `zkc --set`, anything).

Enabled entries run on their own every time `ZmkCompanion.exe` itself
starts, not just at Windows login, closing and reopening the app (after an
update, say) re-runs them too, so a script's effect doesn't sit stale until
your next login. Each entry runs as its own independent process (one
script hanging doesn't block the others). Use "Ejecutar ahora" / "Run now"
to trigger all enabled entries immediately, without restarting the app,
useful right after adding or editing one.

## Troubleshooting

- **"tray app not running"** when running `zkc`: launch `ZmkCompanion.exe`
  first (Start menu, or it should already be running after install/login).
- **Won't connect**: check the tray icon is orange (searching) vs. red/gone
  (not running). Try **Reconnect** from the tray menu.
- **Debug log**: tray menu → **Debug Log**, opens
  `%APPDATA%\ZmkCompanion\debug.log` in your default text editor.
- **Settings file**: `%APPDATA%\ZmkCompanion\settings.json`, if the app
  won't start, corrupting or deleting this (back it up first) resets it to
  defaults.

## Firmware

None of this works against stock ZMK firmware. Your keyboard needs a custom
build exposing the BLE GATT display service this app talks to:

- [`docs/cell_grid_protocol.md`](cell_grid_protocol.md), the current
  protocol (cell-grid rows + full-page bitmap), what the Canvas editor and
  `zkc.exe` actually use.
- [`docs/protocol.md`](protocol.md), the older plain-text-only
  characteristic, still supported for backward compatibility.
- `firmware/custom_status_screen.c`, reference firmware source.

**If you have an "eyelash_corne" board** (the reference keyboard this was
built for): fork
[`zmk-companion-template`](https://github.com/oscampo/zmk-companion-template),
push, and GitHub Actions builds a ready-to-flash `.uf2` with the display
already enabled, no local toolchain needed. Full walkthrough, including
customizing your own keymap, in
[`getting_started.md`](getting_started.md).

**Any other ZMK board with a `nice_view` display**: the display code itself
has no eyelash_corne-specific dependencies (checked: no board-specific
device tree references, just generic LVGL/ZMK APIs, and the display
resolution matches the `nice_view` panel, not any particular PCB), and
[`zmk-companion-template`](https://github.com/oscampo/zmk-companion-template)
is already structured as a west module. In principle you can add it as a
module in your own ZMK config's `west.yml` (no fork, no copying files) and
build your own board with `-DCONFIG_KBD_BLE_DISPLAY=y`, the same flag the
reference board's CI build already uses. **This has not been verified on
any board other than eyelash_corne** — if you try it, please open an issue
either way so this stops being a guess.

**Any other keyboard, no `nice_view` display**: no path today, the code is
tied to that display's resolution.
