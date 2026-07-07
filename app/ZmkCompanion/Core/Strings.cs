namespace ZmkCompanion.Core;

enum AppLanguage { Es, En }

// Minimal i18n scaffold — proof of the switching mechanism (AppSettings.Language
// persistence + tray "Idioma" submenu + live rebuild via LanguageChanged) before
// migrating the full ~7-class UI string set over to this pattern.
static class Strings
{
    public static AppLanguage Current { get; private set; } = AppLanguage.Es;

    public static event Action? LanguageChanged;

    public static void SetLanguage(AppLanguage lang)
    {
        if (Current == lang) return;
        Current = lang;
        LanguageChanged?.Invoke();
    }

    public static string Reconnect    => Current == AppLanguage.Es ? "Reconectar"  : "Reconnect";
    public static string Disconnect   => Current == AppLanguage.Es ? "Desconectar" : "Disconnect";
    public static string LanguageMenu => Current == AppLanguage.Es ? "Idioma"      : "Language";

    // ── Shared buttons ─────────────────────────────────────────────────────────
    public static string Ok     => Current == AppLanguage.Es ? "Aceptar"  : "OK";
    public static string Cancel => Current == AppLanguage.Es ? "Cancelar" : "Cancel";
    public static string Add    => Current == AppLanguage.Es ? "Agregar"  : "Add";
    public static string Remove => Current == AppLanguage.Es ? "Eliminar" : "Remove";
    public static string Close  => Current == AppLanguage.Es ? "Cerrar"   : "Close";

    // ── PomodoroConfigDialog ────────────────────────────────────────────────────
    public static string PomodoroConfigTitle => Current == AppLanguage.Es ? "Configurar Pomodoro" : "Configure Pomodoro";
    public static string PomodoroWorkLabel    => Current == AppLanguage.Es ? "Trabajo (min):"       : "Work (min):";
    public static string PomodoroBreakLabel   => Current == AppLanguage.Es ? "Pausa corta (min):"   : "Short break (min):";
    public static string PomodoroCyclesLabel  => Current == AppLanguage.Es ? "Ciclos:"              : "Cycles:";
    public static string PomodoroLongLabel    => Current == AppLanguage.Es ? "Pausa larga (min):"   : "Long break (min):";
    public static string PomodoroPresetsLabel => Current == AppLanguage.Es ? "Presets:"             : "Presets:";
    public static string PomodoroPresetClassic => Current == AppLanguage.Es ? "Clásico" : "Classic";
    public static string PomodoroPresetShort   => Current == AppLanguage.Es ? "Corto"   : "Short";
    public static string PomodoroPresetLong    => Current == AppLanguage.Es ? "Largo"   : "Long";

    // ── CustomTokensForm ─────────────────────────────────────────────────────────
    public static string CustomTokensTitle => Current == AppLanguage.Es ? "Tokens personalizados" : "Custom tokens";
    public static string CustomTokensHelp => Current == AppLanguage.Es
        ? "Declara nombres aquí para que aparezcan en el selector de\ntokens del editor. El valor real solo llega con\n\"zkc --set NOMBRE valor\" desde un script."
        : "Declare names here so they show up in the editor's token\npicker. The actual value only ever arrives via\n\"zkc --set NAME value\" from a script.";
    public static string NameLabel        => Current == AppLanguage.Es ? "Nombre:"   : "Name:";
    public static string CategoryLabel    => Current == AppLanguage.Es ? "Categoría:" : "Category:";
    public static string StaleAfterLabel  => Current == AppLanguage.Es
        ? "Avisar si no se actualiza en (segundos, 0 = nunca):"
        : "Warn if not updated within (seconds, 0 = never):";
    public static string AddToken   => Current == AppLanguage.Es ? "+ Agregar" : "+ Add";
    public static string InvalidNameTitle => Current == AppLanguage.Es ? "Nombre inválido" : "Invalid name";
    public static string InvalidNameBody  => Current == AppLanguage.Es
        ? "El nombre solo puede usar minúsculas, dígitos y guion bajo (a-z, 0-9, _)."
        : "The name can only use lowercase letters, digits, and underscore (a-z, 0-9, _).";
    public static string DuplicateNameTitle => Current == AppLanguage.Es ? "Nombre duplicado" : "Duplicate name";
    public static string DuplicateNameBody(string name) => Current == AppLanguage.Es
        ? $"Ya existe un token llamado \"{name}\"."
        : $"A token named \"{name}\" already exists.";
    public static string StaleSuffix(int seconds) => Current == AppLanguage.Es
        ? $"  desactualizado>{seconds}s"
        : $"  stale>{seconds}s";

    // ── LeaguePickerDialog ───────────────────────────────────────────────────────
    public static string LeaguePickerTitle => Current == AppLanguage.Es
        ? "ZMK Companion - Configurar Ligas" : "ZMK Companion - Configure Leagues";
    public static string SportLabel        => Current == AppLanguage.Es ? "Deporte:" : "Sport:";
    public static string SearchLeaguesLabel => Current == AppLanguage.Es ? "Buscar ligas:" : "Search leagues:";
    public static string SearchLeaguesPlaceholder => Current == AppLanguage.Es ? "Escribe para filtrar…" : "Type to filter…";
    public static string AvailableLeaguesLabel => Current == AppLanguage.Es
        ? "Disponibles  (doble clic o Enter para agregar):" : "Available  (double-click or Enter to add):";
    public static string SelectedLeaguesLabel => Current == AppLanguage.Es
        ? "Seleccionadas  (clic en la ficha para quitar):" : "Selected  (click chip to remove):";
    public static string Loading => Current == AppLanguage.Es ? "Cargando…" : "Loading…";
    public static string LoadingPercent(int v) => Current == AppLanguage.Es ? $"Cargando… {v}%" : $"Loading… {v}%";
    public static string LeaguesAvailable(int count) => Current == AppLanguage.Es
        ? $"{count} ligas disponibles" : $"{count} leagues available";
    public static string[] SportNames => Current == AppLanguage.Es
        ? ["Fútbol Americano", "Fútbol", "Baloncesto", "Hockey"]
        : ["Football", "Soccer", "Basketball", "Hockey"];

    // ── GlyphPickerDialog ────────────────────────────────────────────────────────
    public static string GlyphPickerTitle => Current == AppLanguage.Es
        ? "Selector de glifos , clic para elegir" : "Glyph Picker — click to select";
    public static string SearchLabel      => Current == AppLanguage.Es ? "Buscar:"    : "Search:";
    public static string SearchGlyphPlaceholder => Current == AppLanguage.Es ? "nombre o hex…" : "name or hex…";
    public static string AllIconsCategory => Current == AppLanguage.Es ? "Todos los íconos" : "All icons";
    public static string OtherBmpCategory => Current == AppLanguage.Es ? "BMP otros"        : "BMP other";
    public static string GlyphsLoaded(int count) => Current == AppLanguage.Es
        ? $"{count} glifos cargados" : $"{count} glyphs loaded";
    public static string GlyphsCount(int count) => Current == AppLanguage.Es
        ? $"{count} glifos" : $"{count} glyphs";

    // ── TrayIcon ─────────────────────────────────────────────────────────────────
    public static string Searching       => Current == AppLanguage.Es ? "ZMK Companion — buscando…" : "ZMK Companion — searching…";
    public static string NotConnected    => Current == AppLanguage.Es ? "  No conectado" : "  Not connected";
    public static string ConnectedTo(string device) => $"ZMK Companion — {device}";
    public static string ConnectedBalloon(string device) => Current == AppLanguage.Es
        ? $"Conectado a {device}" : $"Connected to {device}";
    public static string DisconnectedTray => Current == AppLanguage.Es ? "ZMK Companion — desconectado" : "ZMK Companion — disconnected";
    public static string KeyboardDisconnected => Current == AppLanguage.Es ? "Teclado desconectado" : "Keyboard disconnected";
    public static string CanvasMenu       => "Canvas…";
    public static string CustomTokensMenu => Current == AppLanguage.Es ? "Tokens personalizados…" : "Custom tokens…";
    public static string PomodoroStop     => Current == AppLanguage.Es ? "Pomodoro — Detener" : "Pomodoro — Stop";
    public static string PomodoroStart    => Current == AppLanguage.Es ? "Pomodoro — Iniciar" : "Pomodoro — Start";
    public static string PomodoroConfigMenu => Current == AppLanguage.Es ? "Configurar Pomodoro…" : "Configure Pomodoro…";
    public static string DebugLogMenu     => "Debug Log";
    public static string AboutMenu        => Current == AppLanguage.Es ? "Acerca de…" : "About…";
    public static string ExitMenu         => Current == AppLanguage.Es ? "Salir" : "Exit";
    public static string NoDebugLogYet    => Current == AppLanguage.Es ? "Aún no hay log de debug." : "There's no debug log yet.";
    public static string CouldNotOpenLog(string message, string path) => Current == AppLanguage.Es
        ? $"No se pudo abrir el log: {message}\n\nRuta: {path}"
        : $"Could not open the log: {message}\n\nPath: {path}";
    public static string AboutTitle => Current == AppLanguage.Es ? "Acerca de ZMK Companion" : "About ZMK Companion";
    public static string AboutBody(int major, int minor, int build) => Current == AppLanguage.Es
        ? $"ZMK Companion  v{major}.{minor}.{build}\n\n" +
          "Muestra información personalizada en la pantalla OLED\n" +
          "de tu teclado ZMK vía BLE.\n\n" +
          "github.com/oscampo/zmk-companion"
        : $"ZMK Companion  v{major}.{minor}.{build}\n\n" +
          "Shows custom information on your ZMK keyboard's\n" +
          "OLED screen over BLE.\n\n" +
          "github.com/oscampo/zmk-companion";
}
