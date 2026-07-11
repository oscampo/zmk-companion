using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZmkCompanion.Core;

// System-wide hotkeys for jumping straight to a Canvas page on demand, even
// while some other app has focus (the user consults page 3 mid-work, then
// page 1, without waiting for the auto-cycle to get there). Bound to
// F13-F21 (VK 0x7C-0x84), bare, no modifiers deliberately: almost no
// physical keyboard has F13+ keys and almost no software binds them, so
// registering these is very unlikely to steal a shortcut the user already
// relies on elsewhere, unlike Ctrl+Shift+<digit> which plenty of apps
// already use. The intended source is a ZMK macro (&kp F13, &kp F14, ...)
// on the keyboard's own spare layer, see zmk-companion-template's keymap
// and docs/user_guide.md for the assignment.
sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint VkF13    = 0x7C; // F13..F24 are contiguous VK codes
    public const int MaxPages   = 9;    // F13..F21

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

    // Friendly label for logging/UI, "F13".."F21", matching page N -> F(12+N).
    public static string LabelFor(int pageNumber1Based) => $"F{12 + pageNumber1Based}";

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
            uint vk = VkF13 + (uint)i;
            if (RegisterHotKey(_window.Handle, id, 0 /* no modifiers */, vk))
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
