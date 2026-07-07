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
}
