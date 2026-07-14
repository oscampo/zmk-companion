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
    public static string PomodoroIconsLabel    => Current == AppLanguage.Es ? "Íconos:" : "Icons:";

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
    // ── CellGridEditorForm ───────────────────────────────────────────────────────
    public static string EditorTitle       => Current == AppLanguage.Es ? "ZMK Companion — Editor de pantalla" : "ZMK Companion — Display Editor";
    public static string PreviewGroupTitle => Current == AppLanguage.Es ? "Vista previa  (3×)" : "Preview  (3×)";
    public static string PagesGroupTitle   => Current == AppLanguage.Es ? "Páginas" : "Pages";
    public static string CyclePagesCheck   => Current == AppLanguage.Es ? "Ciclar páginas" : "Cycle pages";
    public static string DurationLabel     => Current == AppLanguage.Es ? "Dur.:" : "Dur.:";
    public static string RowsGroupTitle    => Current == AppLanguage.Es ? "Filas" : "Rows";
    public static string AddRowButton      => Current == AppLanguage.Es ? "+ Agregar fila" : "+ Add row";
    public static string DeleteButton      => Current == AppLanguage.Es ? "Eliminar" : "Delete";
    public static string AddIconPairButton => Current == AppLanguage.Es ? "+ Par íconos" : "+ Icon pair";
    public static string AddTextBlockButton => Current == AppLanguage.Es ? "+ Texto" : "+ Text";
    public static string TierLabel         => Current == AppLanguage.Es ? "Tier:" : "Tier:";
    public static string HalfLabel         => Current == AppLanguage.Es ? "Mitad:" : "Half:";
    public static string[] SplitOptions    => Current == AppLanguage.Es
        ? ["Normal", "↑ Mitad superior", "↓ Mitad inferior"]
        : ["Normal", "↑ Top half", "↓ Bottom half"];
    public static string TemplateLabel     => Current == AppLanguage.Es ? "Template:" : "Template:";
    public static string AlignLabel        => Current == AppLanguage.Es ? "Align:" : "Align:";
    public static string AlignLeft         => Current == AppLanguage.Es ? "Izq"    : "Left";
    public static string AlignCenter       => Current == AppLanguage.Es ? "Centro" : "Center";
    public static string AlignRight        => Current == AppLanguage.Es ? "Der"    : "Right";
    public static string BoldCheck         => Current == AppLanguage.Es ? "Negrita" : "Bold";
    public static string NumericLabel      => Current == AppLanguage.Es ? "Núm:" : "Num:";
    public static string AlphaLabel        => Current == AppLanguage.Es ? "Alfa:" : "Alpha:";
    public static string FontVariantLabel  => Current == AppLanguage.Es ? "Fuente:" : "Font:";
    public static string InsertLabel       => Current == AppLanguage.Es ? "Insertar:" : "Insert:";
    public static string InsertButton      => Current == AppLanguage.Es ? "↵ Insertar" : "↵ Insert";
    public static string SportsTab         => Current == AppLanguage.Es ? "Deportes" : "Sports";
    public static string WeatherTab        => Current == AppLanguage.Es ? "Clima" : "Weather";
    public static string LibraryTab        => Current == AppLanguage.Es ? "Biblioteca" : "Library";
    public static string LeaguesTeamsLabel => Current == AppLanguage.Es ? "Ligas y equipos:" : "Leagues and teams:";
    public static string EditLeaguesButton => Current == AppLanguage.Es ? "Editar ligas…" : "Edit leagues…";
    public static string CityLabel         => Current == AppLanguage.Es ? "Ciudad:" : "City:";
    public static string TemperatureLabel  => Current == AppLanguage.Es ? "Temperatura:" : "Temperature:";
    public static string CopyButton        => Current == AppLanguage.Es ? "Copiar" : "Copy";
    public static string LaunchCliButton   => Current == AppLanguage.Es ? "Lanzar zkc" : "Launch zkc";
    public static string CliCommandLabel   => Current == AppLanguage.Es ? "Comando (opcional):" : "Command (optional):";
    public static string CliHint => Current == AppLanguage.Es
        ? "Vacío: abre \"zkc -h\". Con texto: corre ese comando tal cual\n(admite tuberías, ej. python reloj.py | zkc -w)."
        : "Blank: opens \"zkc -h\". With text: runs that command verbatim\n(pipes allowed, e.g. python clock.py | zkc -w).";
    public static string SaveButton        => Current == AppLanguage.Es ? "Guardar" : "Save";
    public static string LoadButton        => Current == AppLanguage.Es ? "Cargar" : "Load";
    public static string ApplyButton       => Current == AppLanguage.Es ? "Aplicar" : "Apply";

    public static string AutoStartTab           => Current == AppLanguage.Es ? "Inicio automático" : "Auto-start";
    public static string AutoStartCommandLabel  => Current == AppLanguage.Es ? "Comando:" : "Command:";
    public static string AutoStartAddButton     => Current == AppLanguage.Es ? "Agregar/actualizar" : "Add/update";
    public static string AutoStartRemoveButton  => Current == AppLanguage.Es ? "Quitar" : "Remove";
    public static string AutoStartNewButton     => Current == AppLanguage.Es ? "Nuevo" : "New";
    public static string AutoStartRunNowButton  => Current == AppLanguage.Es ? "Ejecutar ahora" : "Run now";
    public static string AutoStartHint => Current == AppLanguage.Es
        ? "Las entradas activas corren solas cada vez que abres ZmkCompanion."
        : "Enabled entries run on their own every time ZmkCompanion opens.";
    public static string AutoStartNameRequired => Current == AppLanguage.Es
        ? "Ponle un nombre a la entrada." : "Give the entry a name.";
    public static string DefaultPageName(int n) => Current == AppLanguage.Es ? $"Página {n}" : $"Page {n}";
    public static string TeamPlaceholder   => Current == AppLanguage.Es ? "equipo" : "team";
    public static string QueryingWeather   => Current == AppLanguage.Es ? "consultando…" : "querying…";
    public static string WeatherHttpError(int code) => Current == AppLanguage.Es
        ? $"HTTP {code} — red/proxy" : $"HTTP {code} — network/proxy";
    public static string WeatherCityLimitTitle => Current == AppLanguage.Es ? "Límite de ciudades" : "City limit";
    public static string WeatherCityLimitBody(int max) => Current == AppLanguage.Es
        ? $"No puedes agregar más de {max} ciudades de clima."
        : $"You can't add more than {max} weather cities.";
    public static string WeatherAutoDetectHint => Current == AppLanguage.Es
        ? "Sin ciudades definidas: se detecta automáticamente por IP."
        : "No cities set: auto-detected via IP.";
    public static string WeatherSuffixHint => Current == AppLanguage.Es
        ? "Tip: usa \":Ciudad\" en un token de Clima, ej. {weather.temp:Madrid}."
        : "Tip: use \":City\" on a Weather token, e.g. {weather.temp:Madrid}.";
    public static string TextBlockDialogTitle => Current == AppLanguage.Es ? "Bloque de texto" : "Text block";
    public static string NumberOfLinesLabel   => Current == AppLanguage.Es ? "Número de líneas:" : "Number of lines:";
    public static string NotEnoughSpaceIconPair(int needed, int free) => Current == AppLanguage.Es
        ? $"No hay espacio suficiente para un par de íconos ({needed}px necesarios, {free}px libres)."
        : $"Not enough space for an icon pair ({needed}px needed, {free}px free).";
    public static string NotEnoughSpace(int needed, int free) => Current == AppLanguage.Es
        ? $"No hay espacio suficiente ({needed}px necesarios, {free}px libres)."
        : $"Not enough space ({needed}px needed, {free}px free).";
    public static string ZkcNotFound(string path) => Current == AppLanguage.Es
        ? $"No se encontró zkc.exe en:\n{path}" : $"zkc.exe not found at:\n{path}";
    public static string CouldNotOpenTerminal(string message) => Current == AppLanguage.Es
        ? $"No se pudo abrir la terminal: {message}" : $"Could not open the terminal: {message}";
    public static string EnterConfigName => Current == AppLanguage.Es
        ? "Ingresa un nombre para la configuración." : "Enter a name for the configuration.";
    public static string ErrorLoadingConfig(string message) => Current == AppLanguage.Es
        ? $"Error al cargar la configuración: {message}" : $"Error loading configuration: {message}";
    public static string ConfirmDeleteLibraryItem(string name) => Current == AppLanguage.Es
        ? $"¿Eliminar '{name}' de la biblioteca?" : $"Delete '{name}' from the library?";
    public static string PageExceedsHeight(string pageName, int total, int max) => Current == AppLanguage.Es
        ? $"La página '{pageName}' excede la altura del display ({total}px > {max}px).\nElimina o reduce filas antes de aplicar."
        : $"Page '{pageName}' exceeds the display height ({total}px > {max}px).\nRemove or shrink rows before applying.";

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
    public static string HelpMenu         => Current == AppLanguage.Es ? "Ayuda…" : "Help…";
    public static string AboutMenu        => Current == AppLanguage.Es ? "Acerca de…" : "About…";
    public static string ExitMenu         => Current == AppLanguage.Es ? "Salir" : "Exit";
    public static string NoDebugLogYet    => Current == AppLanguage.Es ? "Aún no hay log de debug." : "There's no debug log yet.";
    public static string CouldNotOpenLog(string message, string path) => Current == AppLanguage.Es
        ? $"No se pudo abrir el log: {message}\n\nRuta: {path}"
        : $"Could not open the log: {message}\n\nPath: {path}";
    public static string WelcomeTitle => Current == AppLanguage.Es ? "Bienvenido a ZMK Companion" : "Welcome to ZMK Companion";
    public static string DontShowAgainCheck => Current == AppLanguage.Es ? "No volver a mostrar" : "Don't show again";
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

    // ── AppContext balloons/messages ─────────────────────────────────────────────
    public static string StaleTokenTitle => Current == AppLanguage.Es ? "Token desactualizado" : "Stale token";
    public static string StaleTokenBody(string name, string age) => Current == AppLanguage.Es
        ? $"{{custom.{name}}} no se actualiza hace {age}."
        : $"{{custom.{name}}} hasn't updated in {age}.";
    public static string PomodoroCompletedBalloon => Current == AppLanguage.Es
        ? "¡Sesión Pomodoro completada!" : "Pomodoro session complete!";
    public static string PomodoroPhaseWork      => Current == AppLanguage.Es ? "Trabajo"      : "Work";
    public static string PomodoroPhaseBreak     => Current == AppLanguage.Es ? "Pausa"        : "Break";
    public static string PomodoroPhaseLongBreak => Current == AppLanguage.Es ? "Pausa Larga"  : "Long Break";
    public static string AlreadySearchingBalloon => Current == AppLanguage.Es
        ? "Ya se está buscando el teclado…" : "Already searching for the keyboard…";
    public static string SearchingBalloon => Current == AppLanguage.Es
        ? "Buscando teclado…" : "Searching for keyboard…";

    // ── TimeZonePickerDialog ─────────────────────────────────────────────────────
    public static string TimeZonePickerTitle => Current == AppLanguage.Es
        ? "ZMK Companion - Configurar zonas horarias" : "ZMK Companion - Configure Time Zones";
    public static string SearchTimeZonesLabel => Current == AppLanguage.Es ? "Buscar ciudad:" : "Search city:";
    public static string SearchTimeZonesPlaceholder => Current == AppLanguage.Es ? "Escribe para filtrar…" : "Type to filter…";
    public static string AvailableTimeZonesLabel => Current == AppLanguage.Es
        ? "Disponibles  (doble clic o Enter para agregar):" : "Available  (double-click or Enter to add):";
    public static string CustomTimeZoneIdLabel => Current == AppLanguage.Es
        ? "¿No está en la lista? Escribe un ID IANA:" : "Not in the list? Type an IANA id:";
    public static string SelectedTimeZonesLabel => Current == AppLanguage.Es
        ? "Seleccionadas  (clic en la ficha para quitar):" : "Selected  (click chip to remove):";
    public static string InvalidTimeZoneTitle => Current == AppLanguage.Es ? "Zona horaria inválida" : "Invalid time zone";
    public static string InvalidTimeZoneBody(string id) => Current == AppLanguage.Es
        ? $"\"{id}\" no es un ID de zona horaria IANA reconocido (ej: America/New_York)."
        : $"\"{id}\" isn't a recognized IANA time zone id (e.g. America/New_York).";

    // ── CellGridEditorForm: Time Zone tab ────────────────────────────────────────
    public static string TimeZoneTab           => Current == AppLanguage.Es ? "Zona Horaria" : "Time Zone";
    public static string TimeZonesLabel        => Current == AppLanguage.Es ? "Zonas horarias:" : "Time zones:";
    public static string EditTimeZonesButton   => Current == AppLanguage.Es ? "Editar zonas…" : "Edit zones…";
    public static string TimeZoneSuffixHint => Current == AppLanguage.Es
        ? "Tip: agrega \":ID\" a un token de Hora/Fecha para otra ciudad,\nej. {time:America/Bogota}. Usa el ID completo de arriba, no el código corto."
        : "Tip: append \":ID\" to a Time/Date token for another city,\ne.g. {time:America/Bogota}. Use the full id above, not the short code.";
}
