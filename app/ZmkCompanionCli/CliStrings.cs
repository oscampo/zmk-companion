using System.Text.Json;

namespace ZmkCompanionCli;

// zkc.exe is a standalone process with no reference to ZmkCompanion.csproj
// (which pulls in WinForms/WinRT) — deliberately, to keep the CLI dependency-
// free. To still honor the tray app's "Idioma" setting, this reads the
// "Language" field straight out of the shared settings.json instead of
// sharing Core.Strings across an assembly reference.
internal static class CliStrings
{
    private static readonly bool _isEnglish = ReadIsEnglish();

    private static bool ReadIsEnglish()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ZmkCompanion", "settings.json");
            if (!File.Exists(path)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("Language", out var lang) &&
                   lang.GetString() == "en";
        }
        catch { return false; } // corrupt/missing settings.json → default es
    }

    public static string TrayNotRunning => _isEnglish
        ? "zkc: tray app not running — launch ZmkCompanion from the Start menu first."
        : "zkc: la app de bandeja no está corriendo — inicia ZmkCompanion desde el menú de inicio primero.";

    public static string Sent => _isEnglish ? "Sent." : "Enviado.";

    public static string NoResponse => _isEnglish
        ? "no response from tray app (pipe closed unexpectedly)"
        : "sin respuesta de la app de bandeja (el pipe se cerró inesperadamente)";

    public static string SendFailed => _isEnglish ? "send failed" : "falló el envío";
    public static string SetFailed  => _isEnglish ? "set failed"  : "falló el set";
    public static string WatchRejected => _isEnglish ? "watch rejected" : "watch rechazado";

    public static string Help => _isEnglish ? """
        zkc — ZMK Keyboard Companion CLI

        Usage:
          zkc "text"           Send text to the keyboard display (persists until next update)
          zkc ""               Clear the text display and restore the canvas page
          zkc --watch          Read lines from stdin and send each one live
          zkc -w               Alias for --watch
          zkc --set NAME "val" Set a named {custom.NAME} token to a value
          zkc --set NAME --watch
                               Read lines from stdin, updating {custom.NAME} live
          zkc --help           Show this help

        Examples:
          zkc "Hello world"
          zkc "Line1\nLine2\nLine3"
          echo "score: 3-1" | zkc --watch
          python clock.py | zkc --watch
          zkc "Battery: \{battery.percent\}"
          zkc --set cpu_temp "45C"
          sensors.sh | zkc --set cpu_temp --watch

        Notes:
          Use \n in quoted strings for multi-line text.
          --watch accepts both \n and \r as line separators, so scripts that
          use carriage-return to overwrite a terminal line work out of the box.
          Escaped tokens like \{battery.percent\} or \{weather.temp\} are resolved
          to their current live value before display; unescaped {like this} is
          shown as literal text. An unknown token is left as "{key}" unresolved,
          as a visible sign of a typo rather than being silently dropped.
          {custom.NAME} works from `zkc --set` right away; declaring it from
          the tray's "Custom tokens…" menu (name + category) is only so it
          shows up in the editor's token picker, not required to work.
          NAME may only use a-z, 0-9, _.
          The ZMK Companion tray app must be running.
        """
        : """
        zkc — CLI de ZMK Keyboard Companion

        Uso:
          zkc "texto"           Envía texto a la pantalla del teclado (persiste hasta la próxima actualización)
          zkc ""                Limpia el texto y restaura la página del canvas
          zkc --watch           Lee líneas de stdin y envía cada una en vivo
          zkc -w                Alias de --watch
          zkc --set NOMBRE "val" Fija el token {custom.NOMBRE} a un valor
          zkc --set NOMBRE --watch
                               Lee líneas de stdin, actualizando {custom.NOMBRE} en vivo
          zkc --help           Muestra esta ayuda

        Ejemplos:
          zkc "Hola mundo"
          zkc "Linea1\nLinea2\nLinea3"
          echo "marcador: 3-1" | zkc --watch
          python reloj.py | zkc --watch
          zkc "Bateria: \{battery.percent\}"
          zkc --set cpu_temp "45C"
          sensors.sh | zkc --set cpu_temp --watch

        Notas:
          Usa \n en strings entre comillas para texto multilínea.
          --watch acepta tanto \n como \r como separadores de línea, así que
          scripts que usan retorno de carro para sobrescribir una línea de
          terminal funcionan sin cambios.
          Los tokens escapados como \{battery.percent\} o \{weather.temp\} se
          resuelven a su valor en vivo antes de mostrarse; {así sin escapar}
          se muestra como texto literal. Un token desconocido queda como
          "{key}" sin resolver, como señal visible de un error de tipeo en
          vez de desaparecer silenciosamente.
          {custom.NOMBRE} funciona desde `zkc --set` de inmediato; declararlo
          desde el menú "Tokens personalizados…" de la bandeja (nombre +
          categoría) solo sirve para que aparezca en el selector de tokens
          del editor, no es requisito para que funcione.
          NOMBRE solo puede usar a-z, 0-9, _.
          La app de bandeja de ZMK Companion debe estar corriendo.
        """;
}
