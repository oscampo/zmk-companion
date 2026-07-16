# Bienvenido a ZMK Companion

Esta app vive en la **bandeja del sistema** (system tray), junto al reloj de Windows. No hay una ventana principal: haz clic derecho en su ícono para acceder a todo.

## Qué puede hacer

- Reloj, clima (varias ciudades), zonas horarias y marcadores deportivos en el display de tu teclado
- Temporizador Pomodoro configurable, con íconos personalizables por fase
- Tokens personalizados que scripts externos actualizan con `zkc --set`
- Editor visual de páginas para el display (Canvas)
- CLI (`zkc.exe`) para enviar texto o comandos propios, incluyendo tuberías como `python reloj.py | zkc -w`

## Para empezar

1. Conecta tu teclado por Bluetooth, la app se conecta sola cuando lo detecta
2. Clic derecho en el ícono de la bandeja, **Canvas**, para diseñar qué se muestra en el display
3. Clic derecho, **Pomodoro**, para configurar el temporizador

## Más información

Guía completa de uso (editor, tokens, CLI, resolución de problemas):

https://github.com/oscampo/zmk-companion/blob/main/docs/user_guide.md

¿Tu teclado todavía no tiene el firmware necesario? Eso se configura aparte, en el repositorio del proyecto:

https://github.com/oscampo/zmk-companion
