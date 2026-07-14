using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZmkCompanion.Core;

// System-wide hotkeys for jumping straight to a Canvas page on demand, even
// while some other app has focus (the user consults page 3 mid-work, then
// page 1, without waiting for the auto-cycle to get there). Bound to
// Ctrl+Alt+Shift+Win+<1-9>, a 4-modifier "Hyper key" combo: normal
// keyboard/modifier HID usages, not the F13-F24 range this project tried
// first, that range depends on ZMK/Zephyr's HID report descriptor covering
// extended keycodes and turned out NOT to reach Windows at all on a real
// build (confirmed independently via keycode.info, unrelated to this app's
// code), even though the keymap compiled and flashed cleanly. Four
// modifiers together is rare enough in everyday shortcuts to stay a safe
// choice without needing an exotic keycode range. The intended source is a
// ZMK macro nesting all four modifiers around &kp N1..N9 on the keyboard's
// own spare layer, see zmk-companion-template's keymap and
// docs/user_guide.md for the assignment.
sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    // MOD_ALT | MOD_CONTROL | MOD_SHIFT | MOD_WIN | MOD_NOREPEAT (the last
    // one is Vista+, stops a held key from re-firing WM_HOTKEY repeatedly).
    private const uint HyperMods = 0x0001 | 0x0002 | 0x0004 | 0x0008 | 0x4000;
    private const uint Vk1       = 0x31; // '1'..'9' are their own VK codes
    public const int MaxPages    = 9;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Hidden message-only window (HWND_MESSAGE parent): RegisterHotKey needs
    // an HWND to deliver WM_HOTKEY to, and this app has no window that's
    // always alive (tray-only), so this exists purely to receive that one
    // message, never shown, never part of the visible UI.
    private sealed class MessageWindow : NativeWindow
    {
        public event Action<int>? HotkeyPressed;

        public MessageWindow() =>
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) /* HWND_MESSAGE */ });

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
                HotkeyPressed?.Invoke(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }

    private readonly MessageWindow _window = new();
    private readonly HashSet<int>  _registered = new();

    // 0-based page index, already translated from the 1-based hotkey id.
    public event Action<int>? PageRequested;

    public HotkeyManager() => _window.HotkeyPressed += id => PageRequested?.Invoke(id - 1);

    // Friendly label for logging/UI, "Ctrl+Alt+Shift+Win+1".."+9".
    public static string LabelFor(int pageNumber1Based) => $"Ctrl+Alt+Shift+Win+{pageNumber1Based}";

    // Re-registers for the current page count, unregistering everything
    // first so a shrunk page list (edited in the Canvas editor) doesn't
    // leave orphaned hotkeys pointing at pages that no longer exist. Returns
    // the 1-based page numbers whose hotkey failed to register (already
    // claimed by another app), the caller decides how to surface that,
    // never silently swallowed.
    public List<int> RegisterAll(int pageCount)
    {
        UnregisterAll();
        var failed = new List<int>();
        int n = Math.Min(pageCount, MaxPages);
        for (int i = 0; i < n; i++)
        {
            int id = i + 1;
            uint vk = Vk1 + (uint)i;
            if (RegisterHotKey(_window.Handle, id, HyperMods, vk))
                _registered.Add(id);
            else
                failed.Add(id);
        }
        return failed;
    }

    public void UnregisterAll()
    {
        foreach (int id in _registered) UnregisterHotKey(_window.Handle, id);
        _registered.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.DestroyHandle();
    }
}
