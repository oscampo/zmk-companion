# Getting Started

For someone setting up ZMK Companion for the first time on an `eyelash_corne`
(or compatible `nice_view`) split keyboard, from zero.

## 1. Firmware (one time)

The BLE display feature (`custom_status_screen.c`) lives in its own west
module, [`oscampo/zmk-companion`](https://github.com/oscampo/zmk-companion)
(`firmware/`), enabled with `CONFIG_ZMK_COMPANION_DISPLAY=y`. Pick the path
that matches where you're starting from:

### (a) Starting from zero

1. Fork [`oscampo/zmk-companion-template`](https://github.com/oscampo/zmk-companion-template)
   to your own GitHub account. This is a neutral starting keymap
   (plain QWERTY + number/nav layers, no one else's personal macros or
   combos) with the module already added to `config/west.yml` and the flag
   already enabled in `build.yaml`.
2. In your fork, open the **Actions** tab and enable workflows if GitHub
   asks you to.
3. Wait for the "Build ZMK firmware" workflow to finish (a few minutes,
   runs automatically on the fork).
4. Open the finished run, download the `eyelash_corne_left` and
   `eyelash_corne_right` artifacts.
5. Put each half of the keyboard into bootloader mode and drag the matching
   `.uf2` onto it (`_left` for the left/central half, `_right` for the
   right/peripheral half).

   **Unverified**: this repo doesn't document the exact bootloader-entry
   procedure for this specific board (which button, how many taps) anywhere
   yet. If you don't already know it, check the keyboard's physical
   documentation or ask in
   [zmk-companion-template's issues](https://github.com/oscampo/zmk-companion-template/issues)
   before assuming a generic "double-tap reset" works here.

### (b) You already have your own `zmk-config`

No need to fork anything, add the module directly:

1. In your `config/west.yml`, add a remote and project pointing at
   `oscampo/zmk-companion`:

   ```yaml
   manifest:
     remotes:
       - name: oscampo
         url-base: https://github.com/oscampo
     projects:
       - name: zmk-companion
         remote: oscampo
         revision: main
   ```

2. Enable the flag for your central-half board (the display only runs on the
   split's central half), either in that board's `.conf` file
   (`CONFIG_ZMK_COMPANION_DISPLAY=y`) or as a `cmake-args:
   -DCONFIG_ZMK_COMPANION_DISPLAY=y` entry in your `build.yaml`, matching
   whichever pattern your `zmk-config` already uses for other options.
3. Push, let your existing build workflow run, flash as usual.

   **Unverified**: only tried so far on the `eyelash_corne` board this
   template targets. The display code itself has no board-specific
   dependencies (just the standard `nice_view` shield and a split board), if
   you try it elsewhere, please open an issue either way, working or not.

## 2. Customize your keymap (whenever you want)

This step is for path (a) above (the `zmk-companion-template` fork). If you
went with path (b) and your own `zmk-config`, you already have your own
keymap workflow, skip to step 3.

The template's keymap is intentionally minimal, you'll want to make it your
own. Easiest way:
[nickcoutsos.github.io/keymap-editor](https://nickcoutsos.github.io/keymap-editor/),
pointed at **your fork** (not the template). Every change there commits
directly to `config/eyelash_corne.keymap` and triggers a new automatic
build. Whenever you want to update your keyboard with a new build, repeat
step 1(a).4-5 above (download the new `.uf2` artifacts, flash both halves).

## 3. Install ZMK Companion

1. Download the installer from the
   [zmk-companion Releases page](https://github.com/oscampo/zmk-companion/releases).
2. Run it, no admin rights needed.
3. It launches into the system tray and starts automatically on login from
   then on.

## 4. Connect

1. On the keyboard, switch the BLE profile you use for this PC (`BT_SEL`,
   see your keymap for which layer/keys those are bound to).
2. Pair the keyboard over Bluetooth normally from Windows.
3. The app connects on its own, the tray icon turns green once it does.

## 5. Design your display

Right-click the tray icon → **Canvas**. From there you build what actually
shows on the keyboard's screen. Full reference for every token, tab, and
the `zkc.exe` CLI: [`user_guide.md`](user_guide.md).
