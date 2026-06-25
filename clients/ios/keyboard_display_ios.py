"""
keyboard_display_ios.py — Pythonista (iPad/iPhone)
Envía texto al display del teclado Corne via BLE usando CoreBluetooth.

Flujo de uso:
  1. Presiona BT_SEL 1 en el teclado (desconecta de iPad, empieza a anunciar)
  2. Abre esta app — el scan encuentra el teclado automáticamente
  3. Usa los modos: Reloj, Clima, Pomodoro, NFL, texto libre
  4. Presiona BT_SEL 0 en el teclado para volver al iPad
"""

import cb
import ui
import threading
import time
import json
import os
import urllib.request
import urllib.parse
import unicodedata
from datetime import timezone, datetime

# ── Constantes BLE ────────────────────────────────────────────────────────────
SERVICE_UUID   = '00001523-1212-EFDE-1523-785FEABCD123'
CHAR_UUID      = '00001524-1212-EFDE-1523-785FEABCD123'
KEYBOARD_NAMES = ['corne', 'zmk', 'eyelash']

_DOCS          = os.path.join(os.path.expanduser('~'), 'Documents')
_UUID_FILE     = os.path.join(_DOCS, 'keyboard_uuid.txt')
_PREFS_FILE    = os.path.join(_DOCS, 'keyboard_display_prefs.json')

# ── Persistencia ──────────────────────────────────────────────────────────────
def _save_uuid(uid):
    try:
        with open(_UUID_FILE, 'w') as f: f.write(uid.strip())
    except Exception: pass

def _load_uuid():
    try:
        if os.path.exists(_UUID_FILE):
            v = open(_UUID_FILE).read().strip()
            if v: return v
    except Exception: pass
    return None

def _save_prefs(prefs):
    try:
        with open(_PREFS_FILE, 'w') as f: json.dump(prefs, f)
    except Exception: pass

def _load_prefs():
    try:
        if os.path.exists(_PREFS_FILE):
            return json.load(open(_PREFS_FILE))
    except Exception: pass
    return {}

# ── Fuente / sanitización ─────────────────────────────────────────────────────
_FONT_RANGES = [
    (0x20,   0x7F),
    (0xA1,   0xFF),
    (0xE0A0, 0xE0D4),
    (0xE200, 0xE2A9),
    (0xE300, 0xE3FF),
    (0xEE00, 0xEE0F),
    (0xF000, 0xF2E0),
]
_EXTRA_MAP = str.maketrans({'ß':'ss','æ':'ae','Æ':'AE','œ':'oe','Œ':'OE'})

def to_display(text):
    text = text.translate(_EXTRA_MAP)
    result = []
    for ch in text:
        cp = ord(ch)
        if any(lo <= cp <= hi for lo, hi in _FONT_RANGES) or ch in '\n\r\x01':
            result.append(ch)
        else:
            nfd = unicodedata.normalize('NFD', ch)
            base = nfd[0]
            if any(lo <= ord(base) <= hi for lo, hi in _FONT_RANGES):
                result.append(base)
    return ''.join(result)

# ── Reloj ─────────────────────────────────────────────────────────────────────
def _clock_cmd():
    local_now  = datetime.now().astimezone()
    offset_s   = int(local_now.utcoffset().total_seconds())
    local_unix = int(datetime.now(timezone.utc).timestamp()) + offset_s
    return f'T:{local_unix}:A'  # :A = 12h con a/p; :H = 24h

# ── Pomodoro ──────────────────────────────────────────────────────────────────
_PB_FIRST       = ''
_PB_MID         = ''
_PB_LAST        = ''
_PB_EMPTY       = ''
_PB_FIRST_EMPTY = ''
_PB_END_EMPTY   = ''
_ICON_WORK  = ''  # nf-fa-gavel
_ICON_BREAK = ''  # nf-fa-coffee
_ICON_LONG  = ''  # nf-fa-hourglass

POMODORO_PRESETS = {
    'classic': (25,  5, 4, 15),
    'short':   (15,  3, 4, 10),
    'long':    (50, 10, 3, 20),
}

def _parse_pomodoro(spec):
    """Parse 'classic'/'short'/'long' or '25,5,4,15' → (work, brk, cycles, long_brk)."""
    if spec in POMODORO_PRESETS:
        return POMODORO_PRESETS[spec]
    parts = [int(x.strip()) for x in spec.split(',')]
    if len(parts) == 3:
        parts.append(0)
    if len(parts) != 4:
        raise ValueError(f'Formato inválido: {spec!r}  (usa trabajo,descanso,ciclos[,descanso_largo])')
    return tuple(parts)

def _pomo_bar(done, total):
    segs = []
    for i in range(total):
        is_last = i == total - 1
        if i >= done:
            segs.append(_PB_FIRST_EMPTY if i == 0 else (_PB_END_EMPTY if is_last else _PB_EMPTY))
        elif i == 0:
            segs.append(_PB_FIRST)
        elif is_last:
            segs.append(_PB_LAST)
        else:
            segs.append(_PB_MID)
    return ''.join(segs)

def _fmt_time(seconds):
    m, s = divmod(seconds, 60)
    return f'{m:02d}:{s:02d}'

# ── Clima ─────────────────────────────────────────────────────────────────────
_WMO_CONDITIONS = [
    (range(0,   1),  '', 'Sunny'),
    (range(1,   2),  '', 'PCloudy'),
    (range(2,   3),  '', 'Cloudy'),
    (range(3,   4),  '', 'Overcast'),
    (range(45,  49), '', 'Fog'),
    (range(51,  68), '', 'Rain'),
    (range(71,  78), '', 'Snow'),
    (range(80,  83), '', 'Showers'),
    (range(85,  87), '', 'Snowshwrs'),
    (range(95,  96), '', 'Storm'),
    (range(96, 100), '', 'HvyStorm'),
]

def _wmo_icon(code):
    for r, icon, _ in _WMO_CONDITIONS:
        if code in r: return icon
    return ''

def _wmo_label(code):
    for r, _, label in _WMO_CONDITIONS:
        if code in r: return label
    return f'WMO{code}'

def fetch_weather(city=''):
    def _get(url):
        with urllib.request.urlopen(url, timeout=10) as r:
            return json.loads(r.read().decode('utf-8'))
    if city:
        geo = _get('https://geocoding-api.open-meteo.com/v1/search'
                   f'?name={urllib.parse.quote(city)}&count=1&language=en&format=json')
        results = geo.get('results')
        if not results:
            raise ValueError(f'Ciudad no encontrada: {city!r}')
        r0 = results[0]
        lat, lon, name = r0['latitude'], r0['longitude'], r0.get('name', city)
    else:
        loc = _get('https://ipapi.co/json/')
        lat, lon, name = loc['latitude'], loc['longitude'], loc.get('city', '?')
    wx = _get(f'https://api.open-meteo.com/v1/forecast'
              f'?latitude={lat}&longitude={lon}'
              f'&current_weather=true&temperature_unit=celsius')
    cw = wx['current_weather']
    return {'city': name, 'temp_c': cw['temperature'], 'wmo': int(cw['weathercode'])}

# ── NFL ───────────────────────────────────────────────────────────────────────
_NFL_TROPHY = ''  # nf-fa-trophy
_NFL_BOLT   = ''  # nf-fa-bolt
_NFL_URL    = 'https://site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard?limit=100'

def _espn_get(url):
    with urllib.request.urlopen(url, timeout=10) as r:
        return json.loads(r.read().decode('utf-8'))

def _week_url(season, seasontype, week):
    return f'{_NFL_URL}&season={season}&seasontype={seasontype}&week={week}'

def _parse_games(data, team_filter=''):
    week_num = data.get('week', {}).get('number', '')
    games = []
    for event in data.get('events', []):
        comp        = event.get('competitions', [{}])[0]
        competitors = comp.get('competitors', [])
        if len(competitors) < 2: continue
        teams = {}
        for c in competitors:
            side = c.get('homeAway', 'home')
            teams[side] = {'abbr': c.get('team',{}).get('abbreviation','???').upper(),
                           'score': c.get('score','')}
        away, home = teams.get('away',{}), teams.get('home',{})
        st = comp.get('status',{}).get('type',{})
        games.append({
            'away': away.get('abbr','???'), 'home': home.get('abbr','???'),
            'away_score': away.get('score',''), 'home_score': home.get('score',''),
            'status_detail': st.get('shortDetail',''),
            'status_state':  st.get('state','pre'),
            'week': f'Wk{week_num}' if week_num else '',
        })
    if team_filter:
        tf = team_filter.upper()
        games = [g for g in games if tf in (g['away'], g['home'])]
    return games

def fetch_nfl_results(team_filter=''):
    """Última semana con partidos finalizados."""
    try:
        data       = _espn_get(_NFL_URL)
        season     = data.get('season',{}).get('year',2024) or 2024
        week_num   = data.get('week',{}).get('number',18)   or 18
        seasontype = data.get('season',{}).get('type',2)    or 2
        for step in range(25):
            if step == 0:
                games = _parse_games(data, team_filter)
            else:
                wk, st, yr = week_num - step, seasontype, season
                if wk < 1: yr -= 1; st = 3; wk = 5
                games = _parse_games(_espn_get(_week_url(yr, st, wk)), team_filter)
            final = [g for g in games if g['status_state'] == 'post']
            if final: return final
    except Exception as e:
        print(f'NFL error: {e}')
    return []

def fetch_nfl_schedule(team_filter=''):
    """Próxima semana con partidos programados."""
    try:
        data       = _espn_get(_NFL_URL)
        season     = data.get('season',{}).get('year',2025) or 2025
        week_num   = data.get('week',{}).get('number',1)    or 1
        seasontype = data.get('season',{}).get('type',2)    or 2
        for step in range(10):
            if step == 0:
                games = _parse_games(data, team_filter)
            else:
                games = _parse_games(
                    _espn_get(_week_url(season, seasontype, week_num + step)),
                    team_filter)
            pre = [g for g in games if g['status_state'] == 'pre']
            if pre: return pre
    except Exception as e:
        print(f'NFL schedule error: {e}')
    return []

def fetch_nfl_live(team_filter=''):
    """Partidos en curso."""
    try:
        games = _parse_games(_espn_get(_NFL_URL), team_filter)
        live  = [g for g in games if g['status_state'] == 'in']
        return live or fetch_nfl_results(team_filter)
    except Exception as e:
        print(f'NFL live error: {e}')
    return []

def format_nfl_game(game, live=False):
    away, home, state = game['away'], game['home'], game['status_state']
    if state == 'post':
        return f"{away}  {home}\n{game['away_score']}  {game['home_score']}\n{_NFL_TROPHY} Final"
    elif state == 'in':
        return f"{_NFL_BOLT}{away}-{home}\n{game['status_detail'][:8]}\n {game['away_score']}-{game['home_score']}"
    else:
        parts  = game['status_detail'].split(' - ', 1)
        date_s = parts[0].strip()
        time_s = ''
        if len(parts) > 1:
            t = parts[1].strip().split()
            if len(t) >= 2: time_s = f'{t[0]}{t[1][0].lower()}'
        return f'{away}  {home}\n{date_s}\n{time_s}'

# ── Escritura BLE via ctypes (evita bug PY_SSIZE_T_CLEAN de Pythonista) ──────
def _ble_write_ctypes(peripheral, characteristic, data: bytes):
    import ctypes
    libobjc = ctypes.cdll.LoadLibrary('libobjc.dylib')
    libobjc.objc_msgSend.restype     = ctypes.c_void_p
    libobjc.sel_registerName.restype = ctypes.c_void_p
    libobjc.sel_registerName.argtypes = [ctypes.c_char_p]
    libobjc.objc_getClass.restype    = ctypes.c_void_p
    libobjc.objc_getClass.argtypes   = [ctypes.c_char_p]
    PSIZE = 8

    def _ptr(py_obj):
        base = id(py_obj)
        for off in (2, 3, 4, 5):
            v = ctypes.c_void_p.from_address(base + off * PSIZE).value
            if v and 0x100000000 < v < 0x7fffffffffff:
                return v
        return None

    p_ptr  = _ptr(peripheral)
    ch_ptr = _ptr(characteristic)
    if not p_ptr or not ch_ptr:
        raise RuntimeError(f'No ObjC ptr (p={p_ptr}, ch={ch_ptr})')

    NSData  = libobjc.objc_getClass(b'NSData')
    sel_d   = libobjc.sel_registerName(b'dataWithBytes:length:')
    libobjc.objc_msgSend.argtypes = [ctypes.c_void_p, ctypes.c_void_p,
                                      ctypes.c_char_p, ctypes.c_ulong]
    ns_data = libobjc.objc_msgSend(NSData, sel_d, data, ctypes.c_ulong(len(data)))

    sel_w = libobjc.sel_registerName(b'writeValue:forCharacteristic:type:')
    libobjc.objc_msgSend.restype  = None
    libobjc.objc_msgSend.argtypes = [ctypes.c_void_p, ctypes.c_void_p,
                                      ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ulong]
    libobjc.objc_msgSend(p_ptr, sel_w, ns_data, ch_ptr, ctypes.c_ulong(1))

# ── Delegate CoreBluetooth ────────────────────────────────────────────────────
class KeyboardDelegate:
    def __init__(self, app):
        self.app        = app
        self.peripheral = None
        self.char       = None
        self._ready     = threading.Event()

    def did_update_state(self, *args): pass

    def did_discover_peripheral(self, p, *args):
        name = p.name or '(sin nombre)'
        print(f'BLE: {name}  |  {p.uuid}')
        self.app._set_status(f'Visto: {name}')
        if any(k in name.lower() for k in KEYBOARD_NAMES):
            self.peripheral = p
            _save_uuid(str(p.uuid))
            cb.stop_scan()
            self.app._set_status(f'Conectando a {name}…')
            cb.connect_peripheral(p)

    def did_connect_peripheral(self, p, *args):
        self.app._set_status(f'Conectado: {p.name} ✓')
        self.app._set_connected(True)
        p.discover_services()

    def did_fail_to_connect_peripheral(self, p, *args):
        self.app._set_status('Error al conectar — pulsa Reconectar')
        self.peripheral = None

    def did_disconnect_peripheral(self, p, *args):
        self.app._set_status('Desconectado — pulsa BT_SEL 0 en el teclado')
        self.app._set_connected(False)
        self.peripheral = None
        self.char       = None
        self._ready.clear()

    def did_discover_services(self, p, *args):
        for svc in p.services:
            if SERVICE_UUID in svc.uuid.upper():
                p.discover_characteristics(svc)

    def did_discover_characteristics(self, svc, *args):
        for char in svc.characteristics:
            if CHAR_UUID in char.uuid.upper():
                self.char = char
                self._ready.set()
                # auto-enviar reloj al conectar
                threading.Thread(target=lambda: self.write(_clock_cmd()), daemon=True).start()
                break

    def write(self, text):
        if not self._ready.wait(timeout=12):
            self.app._set_status('Error: teclado no listo')
            return False
        if not self.peripheral or not self.char:
            return False
        raw = to_display(text).encode('utf-8')[:64]
        _ble_write_ctypes(self.peripheral, self.char, raw)
        return True

# ── Delegate para tecla Return en campos de texto ─────────────────────────────
class _ReturnDelegate:
    def __init__(self, action):
        self._action = action
    def textfield_should_return(self, tf):
        tf.end_editing()
        self._action(None)
        return True

# ── Interfaz de usuario ───────────────────────────────────────────────────────
class KeyboardApp(ui.View):

    BG     = '#111827'
    BG_BTN = '#1f2937'
    BLUE   = '#1d4ed8'
    GREEN  = '#065f46'
    RED    = '#7f1d1d'
    ORANGE = '#7c2d12'
    GRAY   = '#374151'
    PURPLE = '#4c1d95'
    TEAL   = '#134e4a'

    def __init__(self):
        self.delegate   = KeyboardDelegate(self)
        self._stop_evt  = threading.Event()
        self._bg_thread = None
        self._prefs     = _load_prefs()
        self._build_ui()
        ui.delay(self._start_ble, 0.3)

    def _start_ble(self):
        self._set_status('Escaneando… (presiona BT_SEL 1 en el teclado)')
        cb.set_central_delegate(self.delegate)
        ui.delay(cb.scan_for_peripherals, 0.5)

    # ── Construcción UI ───────────────────────────────────────────────────────
    def _build_ui(self):
        self.background_color = self.BG
        self.name = 'Corne Display'

        W, PAD, GAP, BH = 390, 12, 8, 48

        def lbl(text, x, y, w, h, color='#9ca3af', size=13, bold=False):
            l = ui.Label(frame=(x, y, w, h))
            l.text       = text
            l.text_color = color
            l.font       = (('<system-bold>' if bold else '<system>'), size)
            l.number_of_lines = 0
            self.add_subview(l)
            return l

        def tf(placeholder, x, y, w, h, text='', on_return=None):
            t = ui.TextField(frame=(x, y, w, h))
            t.placeholder      = placeholder
            t.text             = text
            t.background_color = 'white'
            t.text_color       = 'black'
            t.tint_color       = '#1d4ed8'
            t.corner_radius    = 8
            t.font             = ('<system>', 14)
            if on_return:
                t.delegate = _ReturnDelegate(on_return)
            self.add_subview(t)
            return t

        def btn(title, action, x, y, w, color=None, size=14):
            b = ui.Button(frame=(x, y, w, BH))
            b.title            = title
            b.background_color = color or self.BG_BTN
            b.tint_color       = '#f9fafb'
            b.corner_radius    = 8
            b.font             = ('<system-bold>', size)
            b.action           = action
            self.add_subview(b)
            return b

        bw2 = (W - PAD*2 - GAP) // 2
        bw3 = (W - PAD*2 - GAP*2) // 3

        # ── Status bar ────────────────────────────────────────────────────────
        self._dot = ui.Label(frame=(PAD, PAD, 14, 14))
        self._dot.text       = '●'
        self._dot.font       = ('<system>', 12)
        self._dot.text_color = '#ef4444'
        self.add_subview(self._dot)

        self._lbl_status = lbl('Iniciando…', PAD+18, PAD, W-PAD-18, 16,
                               color='#9ca3af', size=12)
        self._lbl_mode   = lbl('', PAD, PAD+18, W-PAD*2, 20,
                               color='#f3f4f6', size=14, bold=True)
        y = PAD + 42

        # ── Sección: Reloj / Texto libre ──────────────────────────────────────
        lbl('─── Reloj & Texto ───', PAD, y, W-PAD*2, 18, color='#4b5563', size=11)
        y += 20
        btn('Reloj', self._do_clock, PAD, y, bw2, self.BLUE)
        btn('Limpiar', self._do_clear, PAD+bw2+GAP, y, bw2, self.GRAY)
        y += BH + GAP
        self._tf_text = tf('Texto libre para el display…', PAD, y, W-PAD*2-BH-GAP, BH,
                           on_return=self._do_send_text)
        btn('↑', self._do_send_text, W-PAD-BH, y, BH, self.BLUE)
        y += BH + GAP

        # ── Sección: Clima ────────────────────────────────────────────────────
        lbl('─── Clima ───', PAD, y, W-PAD*2, 18, color='#4b5563', size=11)
        y += 20
        saved_city = self._prefs.get('city', '')
        self._tf_city = tf('Ciudad (vacío = auto por IP)', PAD, y, W-PAD*2-BH-GAP, BH,
                           text=saved_city, on_return=self._do_weather)
        btn('↑', self._do_weather, W-PAD-BH, y, BH, self.GREEN)
        y += BH + GAP

        # ── Sección: Pomodoro ─────────────────────────────────────────────────
        lbl('─── Pomodoro ───', PAD, y, W-PAD*2, 18, color='#4b5563', size=11)
        y += 20
        btn('25/5', self._do_pomo_classic, PAD,           y, bw3, self.ORANGE)
        btn('15/3', self._do_pomo_short,   PAD+bw3+GAP,   y, bw3, self.ORANGE)
        btn('50/10',self._do_pomo_long,    PAD+bw3*2+GAP*2,y,bw3, self.ORANGE)
        y += BH + GAP
        self._tf_pomo = tf('Personalizado: trabajo,descanso,ciclos[,descanso_largo]',
                           PAD, y, W-PAD*2-BH-GAP, BH, on_return=self._do_pomo_custom)
        btn('▶', self._do_pomo_custom, W-PAD-BH, y, BH, self.ORANGE)
        y += BH + GAP

        # ── Sección: NFL ──────────────────────────────────────────────────────
        lbl('─── NFL ───', PAD, y, W-PAD*2, 18, color='#4b5563', size=11)
        y += 20
        saved_team = self._prefs.get('nfl_team', '')
        self._tf_team = tf('Equipo (ej: KC, GB — vacío = todos)', PAD, y, W-PAD*2, BH,
                           text=saved_team, on_return=self._do_nfl_last)
        y += BH + GAP
        btn('Últimos', self._do_nfl_last,     PAD,           y, bw3, self.TEAL)
        btn('Próximos',self._do_nfl_next,     PAD+bw3+GAP,   y, bw3, self.TEAL)
        btn('En Vivo', self._do_nfl_live,     PAD+bw3*2+GAP*2,y,bw3, '#14532d')
        y += BH + GAP

        # ── Fila: Detener / Reconectar ────────────────────────────────────────
        btn('⏹ Detener', self._do_stop,      PAD,       y, bw2, self.RED)
        btn('↺ Reconectar', self._do_reconnect, PAD+bw2+GAP, y, bw2, self.GRAY)
        y += BH + GAP

        # ── Hint ─────────────────────────────────────────────────────────────
        lbl('BT_SEL 1 → conectar esta app  ·  BT_SEL 0 → volver al iPad',
            PAD, y, W-PAD*2, 28, color='#4b5563', size=11)
        y += 32

        self.frame = (0, 0, W, y + PAD)

    # ── Helpers UI ────────────────────────────────────────────────────────────
    def _set_status(self, text):   self._lbl_status.text = text
    def _set_mode(self, text):     self._lbl_mode.text   = text
    def _set_connected(self, ok):
        self._dot.text_color = '#22c55e' if ok else '#ef4444'

    def _stop_bg(self):
        self._stop_evt.set()
        if self._bg_thread and self._bg_thread.is_alive():
            self._bg_thread.join(timeout=3)
        self._stop_evt.clear()

    def _run_bg(self, fn):
        self._stop_bg()
        self._bg_thread = threading.Thread(target=fn, daemon=True)
        self._bg_thread.start()

    def _write(self, text):
        return self.delegate.write(text)

    # ── Acciones ──────────────────────────────────────────────────────────────
    def _do_clock(self, sender):
        def _go():
            self._set_mode('Reloj')
            self._write(_clock_cmd())
        self._run_bg(_go)

    def _do_clear(self, sender):
        def _go():
            self._set_mode('')
            self._write('')
        self._run_bg(_go)

    def _do_stop(self, sender):
        self._stop_bg()
        def _go():
            self._write(_clock_cmd())
            self._set_mode('Reloj')
        threading.Thread(target=_go, daemon=True).start()

    def _do_send_text(self, sender):
        text = (self._tf_text.text or '').strip()
        if not text: return
        def _go():
            self._set_mode(f'Texto: {text[:20]}')
            self._write(text)
        self._run_bg(_go)

    def _do_weather(self, sender):
        city = (self._tf_city.text or '').strip()
        if city:
            self._prefs['city'] = city
            _save_prefs(self._prefs)
        def _go():
            self._set_mode('Obteniendo clima…')
            try:
                data  = fetch_weather(city)
                temp  = f"{data['temp_c']:.0f}°C"
                icon  = _wmo_icon(data['wmo'])
                label = _wmo_label(data['wmo'])
                name  = data['city'][:12]
                self._write(f'{name}\n{temp}\n{label}\x01{icon}')
                self._set_mode(f'Clima: {name} {temp}')
                time.sleep(5)
                self._write(_clock_cmd())
                self._set_mode('Reloj')
            except Exception as e:
                self._set_mode(f'Error: {e}')
        self._run_bg(_go)

    def _run_pomo(self, spec):
        try:
            work, brk, cycles, long_brk = _parse_pomodoro(spec)
        except ValueError as e:
            self._set_mode(str(e))
            return
        def _go():
            for cycle in range(1, cycles + 1):
                bar = _pomo_bar(cycle - 1, cycles)
                for remaining in range(work * 60, -1, -1):
                    if self._stop_evt.is_set(): return
                    self._write(f'{_fmt_time(remaining)}\n {bar}\x01{_ICON_WORK}')
                    self._set_mode(f'Trabajo {cycle}/{cycles}  {_fmt_time(remaining)}')
                    if remaining: time.sleep(1)
                if self._stop_evt.is_set(): return
                is_last  = cycle == cycles
                brk_min  = long_brk if (is_last and long_brk) else brk
                brk_icon = _ICON_LONG if (is_last and long_brk) else _ICON_BREAK
                bar2 = _pomo_bar(cycle, cycles)
                for remaining in range(brk_min * 60, -1, -1):
                    if self._stop_evt.is_set(): return
                    self._write(f'{_fmt_time(remaining)}\n {bar2}\x01{brk_icon}')
                    self._set_mode(f'Descanso {cycle}/{cycles}  {_fmt_time(remaining)}')
                    if remaining: time.sleep(1)
            self._write(_clock_cmd())
            self._set_mode('Pomodoro completado ✓')
        self._run_bg(_go)

    def _do_pomo_classic(self, sender): self._run_pomo('classic')
    def _do_pomo_short(self, sender):   self._run_pomo('short')
    def _do_pomo_long(self, sender):    self._run_pomo('long')
    def _do_pomo_custom(self, sender):
        spec = (self._tf_pomo.text or '').strip()
        if spec: self._run_pomo(spec)

    def _team(self):
        t = (self._tf_team.text or '').strip()
        if t:
            self._prefs['nfl_team'] = t
            _save_prefs(self._prefs)
        return t

    def _run_nfl(self, fetch_fn, label, live=False):
        team = self._team()
        CYCLE, REFRESH = 5, 30
        def _go():
            self._set_mode(f'NFL {label}…')
            last_fetch, games = 0.0, []
            while True:
                now = time.monotonic()
                if not games or (live and now - last_fetch >= REFRESH):
                    games = fetch_fn(team)
                    last_fetch = time.monotonic()
                if not games:
                    self._set_mode(f'NFL {label}: sin partidos')
                    return
                wk = games[0].get('week','')
                self._set_mode(f'NFL {label} {wk} ({len(games)})')
                for game in games:
                    if self._stop_evt.is_set(): return
                    self._write(format_nfl_game(game, live=live))
                    time.sleep(CYCLE)
                    if live: break
                if not live: break
            self._write(_clock_cmd())
            self._set_mode('NFL completado')
        self._run_bg(_go)

    def _do_nfl_last(self, sender): self._run_nfl(fetch_nfl_results,  'Últimos')
    def _do_nfl_next(self, sender): self._run_nfl(fetch_nfl_schedule, 'Próximos')
    def _do_nfl_live(self, sender): self._run_nfl(fetch_nfl_live,     'En Vivo', live=True)

    def _do_reconnect(self, sender):
        self.delegate.peripheral = None
        self.delegate.char       = None
        self.delegate._ready.clear()
        self._set_connected(False)
        self._set_status('Escaneando… (presiona BT_SEL 1 en el teclado)')
        try: cb.stop_scan()
        except Exception: pass
        cb.scan_for_peripherals()

    def _try_retrieve(self): pass  # kept for did_update_state compat

# ── Lanzar ────────────────────────────────────────────────────────────────────
if __name__ == '__main__':
    app = KeyboardApp()
    app.present('sheet')
