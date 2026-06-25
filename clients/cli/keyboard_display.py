#!/usr/bin/env python3
"""
keyboard_display.py — Send any text to the ZMK Corne keyboard display via BLE.

Requirements:
    pip install bleak

Usage examples:
    # Send a simple message
    python keyboard_display.py "Hola mundo"

    # Clear the display
    python keyboard_display.py --clear

    # Scan for keyboards without sending anything
    python keyboard_display.py --scan

    # Specify keyboard address directly (skip scan)
    python keyboard_display.py --address AA:BB:CC:DD:EE:FF "texto"

    # Watch mode: read lines from stdin and send each one
    echo "ARG 1 - 0 FRA" | python keyboard_display.py --watch

    # Pipe from any command
    curl -s 'https://api.example.com/score' | python keyboard_display.py --watch

    # Sync clock (sent automatically on every connection; also use to reset display to clock)
    python keyboard_display.py --clock

    # Pomodoro timer (preset)
    python keyboard_display.py --pomodoro classic   # (25,5)x4 + 15 min long break
    python keyboard_display.py --pomodoro short     # (15,3)x4 + 10 min long break
    python keyboard_display.py --pomodoro long      # (50,10)x3 + 20 min long break

    # Pomodoro timer (custom: work,break,cycles[,long_break])
    python keyboard_display.py --pomodoro 25,5,4,15
    python keyboard_display.py --pomodoro 30,10,3

    # NFL scores (current week)
    python keyboard_display.py --nfl              # resultados última semana jugada
    python keyboard_display.py --nfl KC           # solo Chiefs, última semana
    python keyboard_display.py --nfl --next       # programación próxima semana
    python keyboard_display.py --nfl KC --next    # próximo partido de KC
    python keyboard_display.py --nfl KC --live    # marcadores en vivo, actualiza cada 30s
"""

import asyncio
import argparse
import sys
import time
import json
import urllib.request
import urllib.parse
import unicodedata
from datetime import timezone, datetime
from bleak import BleakClient, BleakScanner
from bleak.exc import BleakError


# Characters the firmware fonts support natively (Basic Latin + Latin-1 Supplement
# + Nerd Font icon ranges included in mono_16/mono_8).
_FONT_RANGES = [
    (0x20,   0x7F),   # Basic Latin
    (0xA1,   0xFF),   # Latin-1 Supplement
    (0xE0A0, 0xE0D4), # Powerline
    (0xE200, 0xE2A9), # FA Extension
    (0xE300, 0xE3FF), # Weather icons
    (0xEE00, 0xEE0F), # FiraCode progress bar
    (0xF000, 0xF2E0), # Font Awesome
]

# Multi-char mappings for chars that don't decompose to Latin-1 cleanly.
_EXTRA_MAP = str.maketrans({
    "ß": "ss",
    "æ": "ae", "Æ": "AE",
    "œ": "oe", "Œ": "OE",
})


def to_display(text: str) -> str:
    """
    Prepare text for the keyboard display.
    Characters in Basic Latin (0x20-0x7F) and Latin-1 Supplement (0xA1-0xFF)
    pass through unchanged — the firmware font (JetBrains Mono) covers them.
    Anything else is stripped via NFD diacritic removal or the fallback map.
    """
    text = text.translate(_EXTRA_MAP)
    result = []
    for ch in text:
        cp = ord(ch)
        if any(lo <= cp <= hi for lo, hi in _FONT_RANGES) or ch in "\n\r\x01":
            result.append(ch)
        else:
            # Try NFD decomposition: strip combining marks, keep base if in range
            nfd = unicodedata.normalize("NFD", ch)
            base = nfd[0]
            if any(lo <= ord(base) <= hi for lo, hi in _FONT_RANGES):
                result.append(base)
            # else: drop the character
    return "".join(result)


def _detect_time_format() -> tuple[int, bool]:
    """
    Returns (local_offset_seconds, use_12h).
    local_offset_seconds: seconds to add to UTC to get local time.
    use_12h: True if the system uses 12-hour clock.
    """
    local_now = datetime.now().astimezone()
    offset_s = int(local_now.utcoffset().total_seconds())

    use_12h = False
    try:
        import platform
        if platform.system() == "Windows":
            import winreg  # type: ignore
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                                 r"Control Panel\International")
            itime = winreg.QueryValueEx(key, "iTime")[0]
            winreg.CloseKey(key)
            use_12h = (str(itime) == "0")
        else:
            # Check if system locale formats 13:00 with AM/PM
            test = datetime(2000, 1, 1, 13, 0).strftime("%X")
            use_12h = "PM" in test.upper() or "AM" in test.upper()
    except Exception:
        pass  # default 24h

    return offset_s, use_12h

# Must match config/ble_display_service.c
SERVICE_UUID = "00001523-1212-efde-1523-785feabcd123"
CHAR_UUID    = "00001524-1212-efde-1523-785feabcd123"

# Keywords used to identify the keyboard in BLE scan results
KEYBOARD_NAMES = ["zmk", "corne", "eyelash"]

# Pomodoro presets: (work_min, break_min, cycles, long_break_min)
POMODORO_PRESETS = {
    "classic": (25,  5, 4, 15),
    "short":   (15,  3, 4, 10),
    "long":    (50, 10, 3, 20),
}


def parse_pomodoro(value: str) -> tuple[int, int, int, int]:
    """
    Parse --pomodoro value.  Accepts:
      classic / short / long          → preset
      work,break,cycles               → custom, no long break
      work,break,cycles,long_break    → custom with long break
    Returns (work_min, break_min, cycles, long_break_min).
    """
    v = value.strip().lower()
    if v in POMODORO_PRESETS:
        return POMODORO_PRESETS[v]
    parts = v.split(",")
    if len(parts) not in (3, 4):
        raise argparse.ArgumentTypeError(
            f"Formato inválido: {value!r}\n"
            "Usa un preset (classic/short/long) o work,break,cycles[,long_break]"
        )
    try:
        nums = [int(p) for p in parts]
    except ValueError:
        raise argparse.ArgumentTypeError(f"Los valores deben ser enteros: {value!r}")
    if len(nums) == 3:
        nums.append(0)  # no long break
    return tuple(nums)


def _fmt_time(seconds: int) -> str:
    m, s = divmod(seconds, 60)
    return f"{m:02d}:{s:02d}"


# FiraCode Nerd Font progress bar glyphs (U+EE01-EE05)
_PB_FIRST = ""   # U+EE03 left-end filled
_PB_MID   = ""   # U+EE04 middle filled
_PB_LAST  = ""   # U+EE05 right-end filled
_PB_EMPTY = ""   # U+EE01 empty segment

# Pomodoro mode icons (Font Awesome, already in font)
_ICON_WORK  = ""   # nf-fa-gavel (hammer/mallet)
_ICON_BREAK = ""   # nf-fa-coffee
_ICON_LONG  = ""   # nf-fa-hourglass


_PB_FIRST_EMPTY = "\uee00"  # U+EE00 left-bracket empty (first empty slot)
_PB_END_EMPTY   = "\uee02"  # U+EE02 right-bracket empty (last empty slot)

def _pomo_bar(done: int, total: int) -> str:
    """FiraCode progress bar using positional glyphs: first/mid/last filled, empty."""
    segs = []
    for i in range(total):
        is_last = (i == total - 1)
        if i >= done:
            segs.append(_PB_FIRST_EMPTY if i == 0 else (_PB_END_EMPTY if is_last else _PB_EMPTY))
        elif i == 0:
            segs.append(_PB_FIRST)
        elif is_last:
            segs.append(_PB_LAST)
        else:
            segs.append(_PB_MID)
    return "".join(segs)


async def run_pomodoro(address: str, work: int, brk: int, cycles: int,
                       long_brk: int, paired_windows: bool, debug: bool) -> None:
    """Run a full pomodoro session, updating the keyboard display each second."""

    async def show(msg: str) -> None:
        await send_text(address, msg, paired_windows=paired_windows, debug=False)

    async def countdown(icon: str, minutes: int, cycle: int) -> None:
        total = minutes * 60
        bar = _pomo_bar(cycle - 1, cycles)
        for remaining in range(total, -1, -1):
            line = f"{_fmt_time(remaining)}\n {bar}\x01{icon}"
            await show(line)
            print(f"\r  {icon} {_fmt_time(remaining)}  {cycle}/{cycles}   ",
                  end="", flush=True)
            if remaining:
                await asyncio.sleep(1)
        print()

    print(f"\nPomodoro: {work}min trabajo / {brk}min descanso / {cycles} ciclos"
          + (f" / {long_brk}min pausa larga" if long_brk else ""))
    print("Ctrl-C para cancelar.\n")

    try:
        for cycle in range(1, cycles + 1):
            # Work
            print(f"[Ciclo {cycle}/{cycles}] TRABAJO")
            await countdown(_ICON_WORK, work, cycle)

            # Break
            is_last = cycle == cycles
            if is_last and long_brk:
                print(f"[Ciclo {cycle}/{cycles}] PAUSA LARGA")
                await countdown(_ICON_LONG, long_brk, cycle)
            else:
                print(f"[Ciclo {cycle}/{cycles}] DESCANSO")
                await countdown(_ICON_BREAK, brk, cycle)

        await show(f"Listo!\n{_pomo_bar(cycles, cycles)}\x01{_ICON_WORK}")
        print("\n✓ Sesión completada.")

    except KeyboardInterrupt:
        await show("")
        print("\nPomodoro cancelado.")


NFL_SCOREBOARD_URL = "https://site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard?limit=100"


def _espn_get(url: str) -> dict:
    with urllib.request.urlopen(url, timeout=10) as r:
        return json.loads(r.read().decode("utf-8"))


def _parse_games(data: dict, team_filter: str = "") -> list[dict]:
    """Parse ESPN scoreboard JSON into game dicts."""
    week_num = data.get("week", {}).get("number", "")
    games = []
    for event in data.get("events", []):
        comp = event.get("competitions", [{}])[0]
        competitors = comp.get("competitors", [])
        if len(competitors) < 2:
            continue
        teams = {}
        for c in competitors:
            side = c.get("homeAway", "home")
            teams[side] = {
                "abbr":  c.get("team", {}).get("abbreviation", "???").upper(),
                "score": c.get("score", ""),
            }
        away = teams.get("away", {})
        home = teams.get("home", {})
        status_type = comp.get("status", {}).get("type", {})
        games.append({
            "away":          away.get("abbr", "???"),
            "home":          home.get("abbr", "???"),
            "away_score":    away.get("score", ""),
            "home_score":    home.get("score", ""),
            "status_detail": status_type.get("shortDetail", ""),
            "status_state":  status_type.get("state", "pre"),
            "week":          f"Wk{week_num}" if week_num else "",
        })
    if team_filter:
        tf = team_filter.upper()
        games = [g for g in games if tf in (g["away"], g["home"])]
    return games


def _week_url(season: int, seasontype: int, week: int) -> str:
    return f"{NFL_SCOREBOARD_URL}&season={season}&seasontype={seasontype}&week={week}"


def fetch_nfl_results(team_filter: str = "") -> list[dict]:
    """
    Last completed week: walks backwards from the current ESPN week until it
    finds games in 'post' (Final) state.  Returns only those Final games.
    """
    try:
        data = _espn_get(NFL_SCOREBOARD_URL)
    except Exception as e:
        print(f"NFL API error: {e}")
        return []

    season     = data.get("season", {}).get("year", 2024) or 2024
    week_num   = data.get("week",   {}).get("number", 18) or 18
    seasontype = data.get("season", {}).get("type", 2)    or 2

    for step in range(25):
        try:
            if step == 0:
                games = _parse_games(data, team_filter)
            else:
                wk, st, yr = week_num - step, seasontype, season
                if wk < 1:          # roll into postseason of previous year
                    yr -= 1; st = 3; wk = 5
                games = _parse_games(_espn_get(_week_url(yr, st, wk)), team_filter)
            final = [g for g in games if g["status_state"] == "post"]
            if final:
                return final
        except Exception:
            continue
    return []


def fetch_nfl_schedule(team_filter: str = "") -> list[dict]:
    """
    Next upcoming week: walks forward from the current ESPN week until it
    finds games in 'pre' (scheduled) state.
    """
    try:
        data = _espn_get(NFL_SCOREBOARD_URL)
    except Exception as e:
        print(f"NFL API error: {e}")
        return []

    season     = data.get("season", {}).get("year", 2025) or 2025
    week_num   = data.get("week",   {}).get("number", 1)  or 1
    seasontype = data.get("season", {}).get("type", 2)    or 2

    for step in range(10):
        try:
            if step == 0:
                games = _parse_games(data, team_filter)
            else:
                games = _parse_games(
                    _espn_get(_week_url(season, seasontype, week_num + step)),
                    team_filter,
                )
            pre = [g for g in games if g["status_state"] == "pre"]
            if pre:
                return pre
        except Exception:
            continue
    return []


_NFL_TROPHY  = ""  # nf-fa-trophy
_NFL_BOLT    = ""  # nf-fa-bolt  (live)

def format_nfl_game(game: dict, live: bool = False) -> str:
    """
    Final:
      GB  MIN
      29  13
       Final

    Live (--live):
      ⚡GB-MIN
      Q3 2:41
       9-13

    Pre-game:
      GB  MIN
      9/13
      4:25p
    """
    away  = game["away"]
    home  = game["home"]
    state = game["status_state"]

    if state == "post" and game["away_score"] is not None:
        # Final: teams / scores / trophy+Final
        teams  = f"{away}  {home}"
        scores = f"{game['away_score']}  {game['home_score']}"
        return f"{teams}\n{scores}\n{_NFL_TROPHY} Final"

    elif state == "in":
        # Live: bolt+matchup / quarter+clock / score
        detail = game["status_detail"]  # e.g. "Q3 2:41"
        score  = f"{game['away_score']}-{game['home_score']}"
        return f"{_NFL_BOLT}{away}-{home}\n{detail[:8]}\n {score}"

    else:
        # Pre-game
        detail = game["status_detail"]  # "9/13 - 4:25 PM ET"
        parts  = detail.split(" - ", 1)
        date_s = parts[0].strip() if parts else detail[:5]
        time_s = ""
        if len(parts) > 1:
            t = parts[1].strip().split()
            if len(t) >= 2:
                time_s = f"{t[0]}{t[1][0].lower()}"
        teams = f"{away}  {home}"
        return f"{teams}\n{date_s}\n{time_s}"


async def run_nfl(address: str, team_filter: str, next_week: bool,
                  live: bool, paired_windows: bool, debug: bool) -> None:
    """
    Cycle through NFL games on the keyboard display (5s each).
    next_week=True  → scheduled games for the next upcoming week
    next_week=False → final results from the most recent played week
    live=True       → re-fetch from ESPN every 30s (useful during game day)
    """
    CYCLE_INTERVAL = 5
    LIVE_REFRESH   = 30

    async def show(text: str) -> None:
        await send_text(address, text, paired_windows=paired_windows, debug=debug)

    mode = "próxima semana" if next_week else "última semana jugada"
    tf   = f" — equipo: {team_filter}" if team_filter else ""
    print(f"NFL {mode}{tf}{'  (live)' if live else ''}\nCtrl-C para salir.\n")

    fetch = fetch_nfl_schedule if next_week else fetch_nfl_results

    try:
        last_fetch = 0.0
        games: list[dict] = []

        while True:
            now = time.monotonic()

            if not games or (live and now - last_fetch >= LIVE_REFRESH):
                print("Consultando ESPN API...")
                games = fetch(team_filter)
                last_fetch = time.monotonic()

                if not games:
                    msg = f"No se encontraron partidos para '{team_filter}'." if team_filter \
                          else "No se encontraron partidos."
                    print(msg)
                    await show("")
                    return

                wk = games[0].get("week", "")
                print(f"  {len(games)} partido(s) — {wk}\n")

            for game in games:
                text = format_nfl_game(game, live=live)
                print(f"  {text!r}")
                await show(text)
                await asyncio.sleep(CYCLE_INTERVAL)
                if live:
                    break  # re-check refresh timer

            if not live:
                break  # one full cycle through the week, then done

    except KeyboardInterrupt:
        print("\nNFL mode stopped.")
    finally:
        await show("")


# WMO weather code ranges → short display label
# (range, icon U+E3xx NF weather, label)
_WMO_CONDITIONS = [
    (range(0,   1),  "", "Sunny"),
    (range(1,   2),  "", "PCloudy"),
    (range(2,   3),  "", "Cloudy"),
    (range(3,   4),  "", "Overcast"),
    (range(45,  49), "", "Fog"),
    (range(51,  68), "", "Rain"),
    (range(71,  78), "", "Snow"),
    (range(80,  83), "", "Showers"),
    (range(85,  87), "", "Snowshwrs"),
    (range(95,  96), "", "Storm"),
    (range(96, 100), "", "HvyStorm"),
]


def _wmo_icon(code: int) -> str:
    for r, icon, _ in _WMO_CONDITIONS:
        if code in r:
            return icon
    return ""


def _wmo_label(code: int) -> str:
    for r, _, label in _WMO_CONDITIONS:
        if code in r:
            return label
    return f"WMO{code}"


def _detect_imperial() -> bool:
    try:
        import platform
        if platform.system() == "Windows":
            import winreg  # type: ignore
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                                 r"Control Panel\International")
            imeasure = winreg.QueryValueEx(key, "iMeasure")[0]
            winreg.CloseKey(key)
            return str(imeasure) == "1"
    except Exception:
        pass
    return False


def fetch_weather(city: str = "") -> dict:
    """
    Returns {"city": str, "temp_c": float, "temp_f": float, "wmo": int}.
    If city is empty, auto-detects location via ipapi.co.
    """
    def _get(url: str) -> dict:
        with urllib.request.urlopen(url, timeout=10) as r:
            return json.loads(r.read().decode("utf-8"))

    if city:
        geo = _get(
            "https://geocoding-api.open-meteo.com/v1/search"
            f"?name={urllib.parse.quote(city)}&count=1&language=en&format=json"
        )
        results = geo.get("results")
        if not results:
            raise ValueError(f"City not found: {city!r}")
        r0 = results[0]
        lat, lon, name = r0["latitude"], r0["longitude"], r0.get("name", city)
    else:
        loc = _get("https://ipapi.co/json/")
        lat = loc["latitude"]
        lon = loc["longitude"]
        name = loc.get("city", "?")

    wx = _get(
        f"https://api.open-meteo.com/v1/forecast"
        f"?latitude={lat}&longitude={lon}"
        f"&current_weather=true&temperature_unit=celsius"
    )
    cw = wx["current_weather"]
    temp_c = cw["temperature"]
    return {
        "city":   name,
        "temp_c": temp_c,
        "temp_f": temp_c * 9 / 5 + 32,
        "wmo":    int(cw["weathercode"]),
    }


async def run_weather(address: str, city: str, paired_windows: bool,
                      debug: bool) -> None:
    imperial = _detect_imperial()
    print("Obteniendo clima...", end=" ", flush=True)
    try:
        data = fetch_weather(city)
    except Exception as e:
        print(f"Error: {e}")
        return

    if imperial:
        temp_str = f"{data['temp_f']:.0f}°F"
    else:
        temp_str = f"{data['temp_c']:.0f}°C"

    icon  = _wmo_icon(data['wmo'])
    label = _wmo_label(data['wmo'])
    city  = data['city'][:12]
    print(f"{city}: {icon} {temp_str}, {label}")

    display = f"{city}\n{temp_str}\n{label}\x01{icon}"
    await send_text(address, display, paired_windows=paired_windows, debug=debug)

    print("Mostrando 10s... ", end="", flush=True)
    await asyncio.sleep(10)
    print("listo.")


def _mac_to_int(mac: str) -> int:
    return int(mac.replace(":", ""), 16)


def _run_ps(script: str, debug: bool = False):
    """Write a PowerShell script to a temp .ps1 file and run it."""
    import subprocess, tempfile, os
    with tempfile.NamedTemporaryFile(mode='w', suffix='.ps1', delete=False,
                                     encoding='utf-8') as f:
        f.write(script)
        ps_file = f.name
    try:
        r = subprocess.run(
            ["powershell", "-NoProfile", "-NonInteractive",
             "-ExecutionPolicy", "Bypass", "-File", ps_file],
            capture_output=True, text=True, timeout=20,
        )
    finally:
        os.unlink(ps_file)
    if r.stderr.strip() and debug:
        print(f"[debug] PowerShell stderr: {r.stderr.strip()[:300]}")
    return r.stdout.strip().splitlines()


def _get_paired_macs_pnp(debug: bool = False) -> list[tuple[str, str]]:
    """Find paired BLE devices via Get-PnpDevice; returns [(name, mac), ...]."""
    ps = r"""
try {
    $devs = Get-PnpDevice | Where-Object { $_.InstanceId -match 'BTHLE\\DEV_' }
    foreach ($d in $devs) {
        if ($d.InstanceId -match 'DEV_([0-9A-Fa-f]{12})') {
            $hex = $Matches[1]
            $mac = ($hex -split '(?<=\G.{2})(?=.)') -join ':'
            Write-Output "DEVICE|$($d.FriendlyName)|$mac"
        }
    }
} catch {
    Write-Output "ERROR|$_"
}
"""
    results = []
    for line in _run_ps(ps, debug=debug):
        if line.startswith("DEVICE|"):
            parts = line.split("|", 2)
            if len(parts) == 3:
                _, name, mac = parts
                name, mac = name.strip(), mac.strip().upper()
                if debug:
                    print(f"[debug] PnP paired: {name!r}  {mac}")
                results.append((name, mac))
    return results


async def send_text_winrt(mac: str, text: str, debug: bool = False) -> bool:
    """
    Send text directly via WinRT GATT — used on Windows when the keyboard is
    already connected as HID and BleakClient can't scan-and-connect to it.
    """
    try:
        from winrt.windows.devices.bluetooth import BluetoothLEDevice  # type: ignore
        from winrt.windows.devices.bluetooth.genericattributeprofile import (  # type: ignore
            GattCommunicationStatus,
        )
        from winrt.windows.storage.streams import DataWriter  # type: ignore
        import uuid as _uuid

        addr = _mac_to_int(mac)
        dev = await BluetoothLEDevice.from_bluetooth_address_async(addr)
        if not dev:
            if debug:
                print("[debug] WinRT: from_bluetooth_address_async returned None")
            return False

        if debug:
            print(f"[debug] WinRT device: {dev.name}  id={dev.device_id}")

        svc_uuid = _uuid.UUID(SERVICE_UUID)
        svc_result = await dev.get_gatt_services_for_uuid_async(svc_uuid)
        if svc_result.status != GattCommunicationStatus.SUCCESS or not svc_result.services:
            print(f"BLE error: custom GATT service not found (status={svc_result.status})")
            return False

        char_uuid = _uuid.UUID(CHAR_UUID)
        char_result = await svc_result.services[0].get_characteristics_for_uuid_async(char_uuid)
        if char_result.status != GattCommunicationStatus.SUCCESS or not char_result.characteristics:
            print(f"BLE error: characteristic not found (status={char_result.status})")
            return False

        data = text.encode("utf-8")[:64]
        writer = DataWriter()
        writer.write_bytes(data)
        buf = writer.detach_buffer()

        status = await char_result.characteristics[0].write_value_with_result_async(buf)
        if status.status == GattCommunicationStatus.SUCCESS:
            if debug:
                display = text if len(text) <= 40 else text[:37] + "..."
                print(f"[debug] ✓ Enviado: {display!r}")
            return True
        else:
            if debug:
                print(f"[debug] BLE write failed (status={status.status})")
            return False

    except Exception as e:
        if debug:
            print(f"[debug] WinRT send failed: {e}")
        return False


async def find_keyboard(timeout: float = 6.0, debug: bool = False):
    """Scan for keyboards by name, with fallback to Windows paired-device lookup."""
    print(f"Buscando teclado ({timeout}s)...")

    all_devices = await BleakScanner.discover(timeout=timeout)
    if debug and all_devices:
        for d in all_devices:
            print(f"[debug] BLE scan: {d.name!r}  {d.address}")
    by_name = [
        d for d in all_devices
        if d.name and any(k in d.name.lower() for k in KEYBOARD_NAMES)
    ]
    if by_name:
        return by_name

    import platform
    if platform.system() == "Windows":
        print("Buscando dispositivos BLE emparejados (Windows)...")
        paired = _get_paired_macs_pnp(debug=debug)
        keyboards = [(n, m) for n, m in paired if any(k in n.lower() for k in KEYBOARD_NAMES)]
        if keyboards:
            class _Dev:
                def __init__(self, name, mac):
                    self.name = name
                    self.address = mac
                    self._is_paired_windows = True
            return [_Dev(n, m) for n, m in keyboards]

    return []


async def sync_clock(address: str, paired_windows: bool, debug: bool) -> None:
    """
    Sync local time to the keyboard.
    Sends T:<local_unix>:A (12h) or T:<local_unix>:H (24h).
    The firmware does local_unix % 86400 to get seconds-since-midnight in local time.
    """
    offset_s, use_12h = _detect_time_format()
    utc_now = int(datetime.now(timezone.utc).timestamp())
    local_unix = utc_now + offset_s
    mode = "A" if use_12h else "H"
    cmd = f"T:{local_unix}:{mode}"
    ok = await send_text(address, cmd, paired_windows=paired_windows, debug=debug)
    if ok and debug:
        fmt = "12h" if use_12h else "24h"
        tz_h = offset_s // 3600
        print(f"[debug] Hora sincronizada: UTC{tz_h:+d} modo {fmt}")


async def send_text(address: str, text: str, paired_windows: bool = False,
                    debug: bool = False) -> bool:
    """Connect to the keyboard and write text to the display characteristic."""
    text = to_display(text)
    if paired_windows:
        return await send_text_winrt(address, text, debug=debug)

    print(f"Conectando a {address}...")
    try:
        async with BleakClient(address, timeout=10.0) as client:
            if not client.is_connected:
                print("Error: no se pudo conectar.")
                return False
            data = text.encode("utf-8")[:64]
            await client.write_gatt_char(CHAR_UUID, data, response=False)
            display = text if len(text) <= 40 else text[:37] + "..."
            print(f"✓ Enviado: {display!r}")
            return True
    except BleakError as e:
        print(f"BLE error: {e}")
        return False


async def main():
    parser = argparse.ArgumentParser(
        description="Envía texto al display del teclado Corne via BLE.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "text", nargs="?",
        help="Texto a mostrar en el display del teclado (máx. 64 caracteres).",
    )
    parser.add_argument(
        "--scan", action="store_true",
        help="Escanear y listar teclados disponibles, luego salir.",
    )
    parser.add_argument(
        "--clear", action="store_true",
        help="Limpiar el texto del display.",
    )
    parser.add_argument(
        "--watch", action="store_true",
        help="Modo continuo: lee líneas desde stdin y envía cada una.",
    )
    parser.add_argument(
        "--address", metavar="ADDR",
        help="Dirección BLE del teclado (omite el escaneo).",
    )
    parser.add_argument(
        "--debug", action="store_true",
        help="Mostrar información de depuración BLE.",
    )
    parser.add_argument(
        "--pomodoro", metavar="PRESET_O_CONFIG",
        help=(
            "Iniciar temporizador pomodoro. "
            "Presets: classic (25,5,4,15) | short (15,3,4,10) | long (50,10,3,20). "
            "Custom: trabajo,descanso,ciclos[,pausa_larga]  ej: 25,5,4,15"
        ),
    )
    parser.add_argument(
        "--nfl", nargs="?", const="", metavar="TEAM",
        help=(
            "Resultados de la última semana jugada (todos o filtrado por equipo). "
            "Ej: --nfl  |  --nfl KC"
        ),
    )
    parser.add_argument(
        "--next", action="store_true",
        help="Con --nfl: muestra la programación de la próxima semana en vez de resultados.",
    )
    parser.add_argument(
        "--live", action="store_true",
        help="Con --nfl: actualiza los marcadores desde ESPN cada 30 segundos.",
    )
    parser.add_argument(
        "--clock", action="store_true",
        help="Sincronizar hora y cambiar el display a modo reloj.",
    )
    parser.add_argument(
        "--weather", nargs="?", const="", metavar="CITY",
        help="Mostrar el clima actual (ciudad opcional; auto-detecta por IP si se omite).",
    )
    args = parser.parse_args()

    # ── Resolve keyboard address ──────────────────────────────────────────────
    address = args.address
    paired_windows = False

    if not address:
        devices = await find_keyboard(debug=args.debug)
        if not devices:
            print(
                "No se encontró ningún teclado.\n"
                "Verifica que:\n"
                "  • El teclado esté encendido y el firmware nuevo esté flasheado\n"
                "  • BLE esté habilitado en el teclado\n"
                "  • Estés dentro del rango Bluetooth\n"
                "\nPrueba con --debug para ver todos los dispositivos encontrados."
            )
            sys.exit(1)

        if len(devices) == 1:
            address = devices[0].address
            paired_windows = getattr(devices[0], "_is_paired_windows", False)
            print(f"Teclado encontrado: {devices[0].name} ({address})")
        else:
            print("Múltiples teclados encontrados:")
            for i, d in enumerate(devices):
                print(f"  [{i}] {d.name} — {d.address}")
            idx = int(input("Selecciona [0]: ") or 0)
            address = devices[idx].address
            paired_windows = getattr(devices[idx], "_is_paired_windows", False)

    if args.scan:
        return

    # ── Sync clock on every connection (enables clock mode as default) ────────
    if not args.clear:  # --clear explicitly clears; all other modes sync first
        await sync_clock(address, paired_windows=paired_windows, debug=args.debug)

    # ── Dispatch ──────────────────────────────────────────────────────────────
    if args.clock:
        print("✓ Reloj sincronizado. El display mostrará la hora.")

    elif args.nfl is not None:
        await run_nfl(address, team_filter=args.nfl.strip(),
                      next_week=args.next, live=args.live,
                      paired_windows=paired_windows, debug=args.debug)
        # Restore clock after nfl mode
        await sync_clock(address, paired_windows=paired_windows, debug=args.debug)

    elif args.weather is not None:
        await run_weather(address, city=args.weather,
                          paired_windows=paired_windows, debug=args.debug)

    elif args.pomodoro is not None:
        try:
            work, brk, cycles, long_brk = parse_pomodoro(args.pomodoro)
        except argparse.ArgumentTypeError as e:
            print(f"Error: {e}")
            sys.exit(1)
        await run_pomodoro(address, work, brk, cycles, long_brk,
                           paired_windows, args.debug)
        # Restore clock after pomodoro
        await sync_clock(address, paired_windows=paired_windows, debug=args.debug)

    elif args.clear:
        await send_text(address, "", paired_windows=paired_windows, debug=args.debug)

    elif args.watch:
        print("Modo watch activo. Envía líneas (Ctrl-C para salir)...")
        try:
            for line in sys.stdin:
                text = line.rstrip("\n")
                await send_text(address, text, paired_windows=paired_windows,
                                debug=args.debug)
        except KeyboardInterrupt:
            print("\nSaliendo.")

    elif args.text is not None:
        await send_text(address, args.text, paired_windows=paired_windows,
                        debug=args.debug)

    elif not args.clock and args.weather is None:
        parser.print_help()


if __name__ == "__main__":
    asyncio.run(main())
