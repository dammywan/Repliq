using System.Runtime.InteropServices;

namespace Repliq.Windows.Hotkeys;

public sealed class HotkeyRegistrationResult
{
    public bool Success { get; set; }
    public string ShortcutText { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public interface IHotkeyManager : IDisposable
{
    HotkeyRegistrationResult RegisterShortcut(string actionName, string shortcutText, Action callback);
    void UnregisterShortcut(string actionName);
    void UnregisterAll();
    IReadOnlyDictionary<string, string> RegisteredShortcuts { get; }
}

public sealed class GlobalHotkeyManager : Form, IHotkeyManager
{
    private const int WM_HOTKEY = 0x0312;
    private int _currentId = 1000;
    private readonly Dictionary<int, (string actionName, Action callback)> _hotkeysById = new();
    private readonly Dictionary<string, (int id, string text)> _hotkeysByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncLock = new();

    public IReadOnlyDictionary<string, string> RegisteredShortcuts
    {
        get
        {
            lock (_syncLock)
            {
                return _hotkeysByName.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.text);
            }
        }
    }

    public GlobalHotkeyManager()
    {
        // Invisible message window
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        Opacity = 0;
        Size = new Size(0, 0);

        // Force handle creation
        var _ = Handle;
    }

    public HotkeyRegistrationResult RegisterShortcut(string actionName, string shortcutText, Action callback)
    {
        if (string.IsNullOrWhiteSpace(shortcutText))
            return new HotkeyRegistrationResult { Success = false, ActionName = actionName, ErrorMessage = "Shortcut cannot be empty." };

        UnregisterShortcut(actionName);

        if (!ParseShortcut(shortcutText, out var fsModifiers, out var vk))
        {
            return new HotkeyRegistrationResult
            {
                Success = false,
                ActionName = actionName,
                ShortcutText = shortcutText,
                ErrorMessage = $"Invalid shortcut syntax: '{shortcutText}'."
            };
        }

        int id = Interlocked.Increment(ref _currentId);
        bool success = RegisterHotKey(Handle, id, fsModifiers, vk);

        if (!success)
        {
            int err = Marshal.GetLastWin32Error();
            return new HotkeyRegistrationResult
            {
                Success = false,
                ActionName = actionName,
                ShortcutText = shortcutText,
                ErrorMessage = $"Shortcut '{shortcutText}' is already registered or reserved by another application (Win32 Error {err})."
            };
        }

        lock (_syncLock)
        {
            _hotkeysById[id] = (actionName, callback);
            _hotkeysByName[actionName] = (id, shortcutText);
        }

        return new HotkeyRegistrationResult
        {
            Success = true,
            ActionName = actionName,
            ShortcutText = shortcutText
        };
    }

    public void UnregisterShortcut(string actionName)
    {
        lock (_syncLock)
        {
            if (_hotkeysByName.TryGetValue(actionName, out var entry))
            {
                UnregisterHotKey(Handle, entry.id);
                _hotkeysById.Remove(entry.id);
                _hotkeysByName.Remove(actionName);
            }
        }
    }

    public void UnregisterAll()
    {
        lock (_syncLock)
        {
            foreach (var entry in _hotkeysByName.Values)
            {
                UnregisterHotKey(Handle, entry.id);
            }
            _hotkeysById.Clear();
            _hotkeysByName.Clear();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            Action? callback = null;

            lock (_syncLock)
            {
                if (_hotkeysById.TryGetValue(id, out var entry))
                {
                    callback = entry.callback;
                }
            }

            if (callback is not null)
            {
                Task.Run(() =>
                {
                    try { callback.Invoke(); } catch { }
                });
            }
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnregisterAll();
        }
        base.Dispose(disposing);
    }

    private static bool ParseShortcut(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var mod = parts[i].ToLowerInvariant();
            switch (mod)
            {
                case "ctrl" or "control":
                    modifiers |= MOD_CONTROL;
                    break;
                case "shift":
                    modifiers |= MOD_SHIFT;
                    break;
                case "alt":
                    modifiers |= MOD_ALT;
                    break;
                case "win" or "windows":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    return false;
            }
        }

        var key = parts[^1].ToUpperInvariant();
        vk = key switch
        {
            "SPACE" => 0x20,
            "ESC" or "ESCAPE" => 0x1B,
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "BACKSPACE" or "BACK" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "PAUSE" => 0x13,
            "PLUS" or "ADD" => 0x6B,
            "MINUS" or "SUBTRACT" => 0x6D,
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            _ => (uint)(key.Length == 1 ? char.ToUpperInvariant(key[0]) : 0)
        };

        return vk != 0;
    }

    #region Win32 P/Invoke
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    #endregion
}
