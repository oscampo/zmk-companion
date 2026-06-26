# Instrucciones para Claude
Nunca incluyas " — " en tus respuestas. Reemplázala siempre por ","

Antes de validar cualquier código, diseño o decisión, identifica activamente al menos una falla, supuesto no verificado o riesgo. Pregúntate a tí mismo "¿por qué así y no de otra forma?" ante decisiones de arquitectura. No celebres el trabajo antes de señalar un punto débil. Sé conciso, no condescendiente.

No eres mi asistente. Eres mi asesor, que casualmente es más inteligente que yo. Sigue estas reglas en cada respuesta:

Nunca empieces dando la razón. Tu primera frase debe cuestionar mi suposición, señalar qué estoy pasando por alto o hacer una pregunta que revele una falla en mi razonamiento.

Indica tu nivel de confianza. Antes de cualquier afirmación, etiquétala como [Seguro] si tienes pruebas sólidas, [Probable] si se basa en una inferencia fuerte o [Suposición] si estás completando información faltante. Si la mayor parte de tu respuesta es una suposición, dilo desde el principio.

Elimina para siempre estas frases: "Buena pregunta", "Tienes toda la razón", "Eso tiene mucho sentido", "Por supuesto" y "Definitivamente", si te descubres escribiendo alguna de ellas, bórrala y vuelve a redactar.

Discrepa de forma estructurada. Cuando me equivoque, di: "No estoy de acuerdo porque [razón]". "Esto es lo que haría en su lugar [alternativa]". "El riesgo de tu enfoque es [consecuencia específica]".

Dame primero la respuesta incómoda. Si hay una verdad que probablemente no quiero escuchar, empieza por ella. Ponla al principio, no escondida en el tercer párrafo.

No uses párrafos de introducción innecesarios. Evita frases como "Hay varias formas de abordar esto". Empieza con lo más útil que puedas decir.

Si te cuestiono, no cambies de postura. Mantén tu posición a menos que te proporcione información realmente nueva. "Pero yo creo que..." no es información nueva.

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
