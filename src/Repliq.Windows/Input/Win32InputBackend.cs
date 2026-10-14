using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Windows.UIAutomation;

namespace Repliq.Windows.Input;

public sealed class Win32InputBackend : IAutomationBackend
{
    private readonly IUiaAutomationAdapter _uiaAdapter;
    private readonly HashSet<ushort> _heldKeys = new();
    private readonly HashSet<uint> _heldMouseButtons = new();
    private readonly object _lock = new();

    public Win32InputBackend(IUiaAutomationAdapter? uiaAdapter = null)
    {
        _uiaAdapter = uiaAdapter ?? new UiaAutomationAdapter();
    }

    public async Task<ActionExecutionResult> ExecuteActionAsync(WorkflowAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Target window activation if specified
        if (action.Target is not null)
        {
            var activated = ActivateTargetWindow(action.Target);
            if (!activated && (action.Verification?.VerifyTargetExists ?? true))
            {
                return ActionExecutionResult.Failure(action.Id, action.Type,
                    $"Could not activate target window '{action.Target.WindowTitle ?? action.Target.ProcessName}'.",
                    "TARGET_ACTIVATION_FAILED", VerificationStatus.Failed);
            }
        }

        switch (ActionTypes.Normalize(action.Type))
        {
            case ActionTypes.Wait:
                int dur = 250;
                if (action.Parameters.TryGetValue("durationMs", out var durObj) &&
                    int.TryParse(durObj?.ToString(), out var d))
                {
                    dur = Math.Clamp(d, 0, 60000);
                }
                await Task.Delay(dur, cancellationToken);
                return ActionExecutionResult.Success(action.Id, action.Type, $"Waited {dur}ms.");

            case ActionTypes.Click:
                return await ExecuteClickAsync(action, cancellationToken);

            case ActionTypes.TypeText:
                return await ExecuteTypeTextAsync(action, cancellationToken);

            case ActionTypes.KeyPress:
                return ExecuteKeyPress(action);

            case ActionTypes.Scroll:
                return ExecuteScroll(action);

            case ActionTypes.FocusWindow:
                return ExecuteFocus(action);

            case ActionTypes.VerifyElement:
                return await ExecuteVerifyElementAsync(action, cancellationToken);

            case ActionTypes.Checkpoint:
                return ActionExecutionResult.Success(action.Id, action.Type, "Checkpoint reached.");

            default:
                return ActionExecutionResult.Failure(action.Id, action.Type,
                    $"Unsupported action type '{action.Type}'.", "UNSUPPORTED_ACTION");
        }
    }

    private async Task<ActionExecutionResult> ExecuteClickAsync(WorkflowAction action, CancellationToken cancellationToken)
    {
        int x = 0, y = 0;
        string button = "left";
        if (action.Parameters.TryGetValue("button", out var btnObj))
        {
            button = btnObj?.ToString()?.ToLowerInvariant() ?? "left";
        }

        if (action.Target?.SelectorType == "uia")
        {
            // First try invoking via pattern for buttons/items
            if (button == "left" && await _uiaAdapter.TryInvokeElementAsync(action.Target, 2000, cancellationToken))
            {
                return ActionExecutionResult.Success(action.Id, action.Type,
                    $"Invoked UIA element '{action.Target.AutomationId ?? action.Target.Name}' via pattern.",
                    VerificationStatus.Verified);
            }

            // Fallback: get clickable point of UIA element
            var pt = await _uiaAdapter.GetElementClickablePointAsync(action.Target, 2000, cancellationToken);
            if (pt is not null)
            {
                x = pt.Value.X;
                y = pt.Value.Y;
            }
            else
            {
                return ActionExecutionResult.Failure(action.Id, action.Type,
                    $"Could not determine clickable point for UIA target '{action.Target.AutomationId ?? action.Target.Name}'.",
                    "UIA_ELEMENT_NOT_CLICKABLE", VerificationStatus.Failed);
            }
        }
        else if (action.Target?.SelectorType == "coordinate")
        {
            x = action.Target.X ?? 0;
            y = action.Target.Y ?? 0;
        }
        else if (action.Parameters.TryGetValue("x", out var xObj) && action.Parameters.TryGetValue("y", out var yObj))
        {
            int.TryParse(xObj?.ToString(), out x);
            int.TryParse(yObj?.ToString(), out y);
        }

        // Move cursor
        SetCursorPos(x, y);
        Thread.Sleep(20);

        uint downFlag = button == "right" ? MOUSEEVENTF_RIGHTDOWN : (button == "middle" ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_LEFTDOWN);
        uint upFlag = button == "right" ? MOUSEEVENTF_RIGHTUP : (button == "middle" ? MOUSEEVENTF_MIDDLEUP : MOUSEEVENTF_LEFTUP);

        lock (_lock) _heldMouseButtons.Add(downFlag);
        mouse_event(downFlag, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(30);

        mouse_event(upFlag, 0, 0, 0, UIntPtr.Zero);
        lock (_lock) _heldMouseButtons.Remove(downFlag);

        if (button == "double")
        {
            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        return ActionExecutionResult.Success(action.Id, action.Type, $"Clicked {button} at ({x}, {y}).");
    }

    private async Task<ActionExecutionResult> ExecuteTypeTextAsync(WorkflowAction action, CancellationToken cancellationToken)
    {
        string? text = null;
        if (action.Parameters.TryGetValue("text", out var textObj))
            text = textObj?.ToString();

        if (action.Target?.SelectorType == "uia" && !string.IsNullOrEmpty(text))
        {
            if (await _uiaAdapter.TrySetElementValueAsync(action.Target, text, 2000, cancellationToken))
            {
                return ActionExecutionResult.Success(action.Id, action.Type,
                    $"Set UIA value for '{action.Target.AutomationId ?? action.Target.Name}' successfully.",
                    VerificationStatus.Verified);
            }
        }

        if (string.IsNullOrEmpty(text))
            return ActionExecutionResult.Success(action.Id, action.Type, "Empty text typed.");

        // Normalize newlines to avoid double-enter on Windows
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

        for (int i = 0; i < normalized.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char c = normalized[i];
            if (c == '\n')
            {
                // Send VK_RETURN
                keybd_event(0x0D, 0, 0, UIntPtr.Zero);
                Thread.Sleep(10);
                keybd_event(0x0D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            else
            {
                SendUnicodeChar(c);
            }
            Thread.Sleep(10);
        }

        return ActionExecutionResult.Success(action.Id, action.Type, $"Typed {text.Length} characters.");
    }

    private ActionExecutionResult ExecuteKeyPress(WorkflowAction action)
    {
        string? key = null;
        if (action.Parameters.TryGetValue("key", out var kObj))
            key = kObj?.ToString();

        if (string.IsNullOrWhiteSpace(key))
            return ActionExecutionResult.Failure(action.Id, action.Type, "No key specified.", "MISSING_KEY");

        // Parse modifiers from parameters (string like "Ctrl+Shift", list, or JsonElement)
        var modifierVks = new List<ushort>();
        if (action.Parameters.TryGetValue("modifiers", out var modObj) && modObj is not null)
        {
            ParseModifierList(modObj, modifierVks);
        }

        // Also check if key string itself is compound, e.g. "Ctrl+C", "Ctrl+Shift+S", "Alt+F4", "Ctrl++"
        if (key.Contains('+') && key.Length > 1 && key != "+")
        {
            string keyPart;
            string modPart;
            if (key.EndsWith("++"))
            {
                keyPart = "+";
                modPart = key[..^2];
            }
            else
            {
                int lastPlus = key.LastIndexOf('+');
                if (lastPlus > 0 && lastPlus < key.Length - 1)
                {
                    modPart = key[..lastPlus];
                    keyPart = key[(lastPlus + 1)..];
                }
                else
                {
                    modPart = "";
                    keyPart = key;
                }
            }

            if (!string.IsNullOrEmpty(modPart))
            {
                var modTokens = modPart.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var token in modTokens)
                {
                    var mVk = MapModifierToVk(token);
                    if (mVk != 0 && !modifierVks.Contains(mVk))
                        modifierVks.Add(mVk);
                }
                key = keyPart.Trim();
            }
        }

        ushort vk = MapKeyToVk(key);
        if (vk == 0)
            return ActionExecutionResult.Failure(action.Id, action.Type, $"Unknown key '{key}'.", "UNKNOWN_KEY");

        try
        {
            // Press modifiers down
            foreach (var modVk in modifierVks)
            {
                lock (_lock) _heldKeys.Add(modVk);
                keybd_event((byte)modVk, 0, 0, UIntPtr.Zero);
            }

            if (modifierVks.Count > 0)
                Thread.Sleep(20);

            // Press and release primary key
            lock (_lock) _heldKeys.Add(vk);
            keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30);
            keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            lock (_lock) _heldKeys.Remove(vk);

            if (modifierVks.Count > 0)
                Thread.Sleep(20);
        }
        finally
        {
            // Release modifiers in reverse order
            for (int i = modifierVks.Count - 1; i >= 0; i--)
            {
                var modVk = modifierVks[i];
                keybd_event((byte)modVk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                lock (_lock) _heldKeys.Remove(modVk);
            }
        }

        var modDesc = modifierVks.Count > 0 ? string.Join("+", modifierVks.Select(VkToModifierName)) + "+" : "";
        return ActionExecutionResult.Success(action.Id, action.Type, $"Pressed key '{modDesc}{key}'.");
    }

    public static ushort MapModifierToVk(string modifier)
    {
        return modifier.Trim().ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" or "LCONTROL" or "RCONTROL" => 0x11, // VK_CONTROL
            "SHIFT" or "LSHIFT" or "RSHIFT" => 0x10,                 // VK_SHIFT
            "ALT" or "MENU" or "LMENU" or "RMENU" => 0x12,           // VK_MENU
            "WIN" or "WINDOWS" or "LWIN" or "RWIN" or "META" or "SUPER" => 0x5B, // VK_LWIN
            _ => 0
        };
    }

    internal static string VkToModifierName(ushort vk) => vk switch
    {
        0x11 => "Ctrl",
        0x10 => "Shift",
        0x12 => "Alt",
        0x5B or 0x5C => "Win",
        _ => "Mod"
    };

    private static void ParseModifierList(object modObj, List<ushort> targetList)
    {
        if (modObj is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in je.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        var mVk = MapModifierToVk(s);
                        if (mVk != 0 && !targetList.Contains(mVk)) targetList.Add(mVk);
                    }
                }
                return;
            }
            modObj = je.GetString() ?? "";
        }

        if (modObj is IEnumerable<string> list)
        {
            foreach (var s in list)
            {
                var mVk = MapModifierToVk(s);
                if (mVk != 0 && !targetList.Contains(mVk)) targetList.Add(mVk);
            }
            return;
        }

        var str = modObj.ToString();
        if (string.IsNullOrWhiteSpace(str)) return;

        var tokens = str.Split(new[] { '+', ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            var mVk = MapModifierToVk(token);
            if (mVk != 0 && !targetList.Contains(mVk)) targetList.Add(mVk);
        }
    }

    private ActionExecutionResult ExecuteScroll(WorkflowAction action)
    {
        int deltaY = 0;
        if (action.Parameters.TryGetValue("deltaY", out var dObj) && int.TryParse(dObj?.ToString(), out var dy))
            deltaY = dy;

        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)deltaY, UIntPtr.Zero);
        return ActionExecutionResult.Success(action.Id, action.Type, $"Scrolled delta {deltaY}.");
    }

    private ActionExecutionResult ExecuteFocus(WorkflowAction action)
    {
        if (action.Target is not null)
        {
            var activated = ActivateTargetWindow(action.Target);
            if (activated)
                return ActionExecutionResult.Success(action.Id, action.Type, $"Focused window '{action.Target.WindowTitle ?? action.Target.ProcessName}'.");
        }

        return ActionExecutionResult.Failure(action.Id, action.Type, "Could not focus requested window.", "FOCUS_FAILED");
    }

    private async Task<ActionExecutionResult> ExecuteVerifyElementAsync(WorkflowAction action, CancellationToken cancellationToken)
    {
        if (action.Target is null)
            return ActionExecutionResult.Success(action.Id, action.Type, "Empty target verification passed.");

        bool verified = await VerifyTargetAsync(action.Target, cancellationToken);
        if (verified)
        {
            return ActionExecutionResult.Success(action.Id, action.Type,
                $"Verified target element '{action.Target.AutomationId ?? action.Target.Name ?? action.Target.WindowTitle ?? action.Target.ProcessName}'.",
                VerificationStatus.Verified);
        }

        return ActionExecutionResult.Failure(action.Id, action.Type,
            $"Target element verification failed for '{action.Target.AutomationId ?? action.Target.Name ?? action.Target.WindowTitle ?? action.Target.ProcessName}'.",
            "VERIFICATION_FAILED", VerificationStatus.Failed);
    }

    public async Task<bool> VerifyTargetAsync(TargetSelector target, CancellationToken cancellationToken)
    {
        if (target is null)
            return true;

        if (target.SelectorType == "uia")
        {
            return await _uiaAdapter.VerifyElementAsync(target, 2000, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(target.ProcessName))
        {
            var procName = Path.GetFileNameWithoutExtension(target.ProcessName);
            var procs = Process.GetProcessesByName(procName);
            if (procs.Length == 0)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(target.WindowTitle))
        {
            var hWnd = FindWindowByTitleSubstring(target.WindowTitle);
            if (hWnd == IntPtr.Zero)
            {
                if (!string.IsNullOrWhiteSpace(target.ProcessName))
                {
                    var procHwnd = FindWindowByProcessName(target.ProcessName);
                    if (procHwnd == IntPtr.Zero)
                        return false;
                }
                else
                {
                    return false;
                }
            }
        }

        return true;
    }

    public Task ReleaseInputStateAsync()
    {
        lock (_lock)
        {
            // Release any held mouse buttons
            foreach (var downFlag in _heldMouseButtons)
            {
                uint upFlag = downFlag switch
                {
                    MOUSEEVENTF_RIGHTDOWN => MOUSEEVENTF_RIGHTUP,
                    MOUSEEVENTF_MIDDLEDOWN => MOUSEEVENTF_MIDDLEUP,
                    _ => MOUSEEVENTF_LEFTUP
                };
                mouse_event(upFlag, 0, 0, 0, UIntPtr.Zero);
            }
            _heldMouseButtons.Clear();

            // Release any held keys
            foreach (var vk in _heldKeys)
            {
                keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            _heldKeys.Clear();
        }
        return Task.CompletedTask;
    }

    private bool ActivateTargetWindow(TargetSelector target)
    {
        IntPtr hWnd = IntPtr.Zero;

        if (!string.IsNullOrWhiteSpace(target.WindowTitle))
        {
            hWnd = FindWindowByTitleSubstring(target.WindowTitle);
        }

        if (hWnd == IntPtr.Zero && !string.IsNullOrWhiteSpace(target.ProcessName))
        {
            hWnd = FindWindowByProcessName(target.ProcessName);
        }

        if (hWnd != IntPtr.Zero)
        {
            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);

            SetForegroundWindow(hWnd);
            return true;
        }

        return false;
    }

    private static IntPtr FindWindowByProcessName(string processName)
    {
        var procName = Path.GetFileNameWithoutExtension(processName);
        var procs = Process.GetProcessesByName(procName);
        foreach (var p in procs)
        {
            if (p.MainWindowHandle != IntPtr.Zero)
                return p.MainWindowHandle;
        }

        var pids = new HashSet<uint>(procs.Select(p => (uint)p.Id));
        if (pids.Count == 0) return IntPtr.Zero;

        IntPtr foundHwnd = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            if (IsWindowVisible(hWnd))
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pids.Contains(pid))
                {
                    var sb = new StringBuilder(256);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    if (sb.Length > 0)
                    {
                        foundHwnd = hWnd;
                        return false;
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        return foundHwnd;
    }

    private static IntPtr FindWindowByTitleSubstring(string titleSubstring)
    {
        IntPtr foundHwnd = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            if (IsWindowVisible(hWnd))
            {
                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, sb.Capacity);
                var title = sb.ToString();
                if (title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
                {
                    foundHwnd = hWnd;
                    return false; // Stop enumeration
                }
            }
            return true;
        }, IntPtr.Zero);

        return foundHwnd;
    }

    private static void SendUnicodeChar(char c)
    {
        INPUT[] inputs = new INPUT[2];

        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wScan = c;
        inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wScan = c;
        inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

        SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    public static ushort MapKeyToVk(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        var trimmed = key.Trim();

        ushort vk = trimmed.ToUpperInvariant() switch
        {
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESCAPE" or "ESC" => 0x1B,
            "SPACE" or "SPACEBAR" => 0x20,
            "BACKSPACE" or "BACK" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" or "PRIOR" => 0x21,
            "PAGEDOWN" or "PGDN" or "NEXT" => 0x22,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "CAPSLOCK" or "CAPITAL" or "CAPS" => 0x14,
            "PRINTSCREEN" or "SNAPSHOT" or "PRTSC" => 0x2C,
            "SCROLL" or "SCROLLLOCK" => 0x91,
            "PAUSE" => 0x13,
            "NUMLOCK" => 0x90,
            "CTRL" or "CONTROL" or "LCONTROL" or "RCONTROL" => 0x11,
            "SHIFT" or "LSHIFT" or "RSHIFT" => 0x10,
            "ALT" or "MENU" or "LMENU" or "RMENU" => 0x12,
            "WIN" or "WINDOWS" or "LWIN" => 0x5B,
            "RWIN" => 0x5C,
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
            // OEM & Symbol keys
            "PLUS" or "OEMPLUS" or "+" => 0xBB,
            "MINUS" or "OEMMINUS" or "-" => 0xBD,
            "MULTIPLY" or "*" => 0x6A,
            "ADD" => 0x6B,
            "SUBTRACT" => 0x6D,
            "DECIMAL" => 0x6E,
            "DIVIDE" => 0x6F,
            "COMMA" or "OEMCOMMA" or "," => 0xBC,
            "PERIOD" or "DOT" or "OEMPERIOD" or "." => 0xBE,
            "SEMICOLON" or "OEM1" or "OEMSEMICOLON" or ";" => 0xBA,
            "SLASH" or "OEM2" or "OEMQUESTION" or "/" or "?" => 0xBF,
            "TILDE" or "BACKQUOTE" or "GRAVE" or "OEM3" or "OEMTILDE" or "`" or "~" => 0xC0,
            "OPENBRACKET" or "LEFTBRACKET" or "OEM4" or "OEMOPENBRACKETS" or "[" or "{" => 0xDB,
            "BACKSLASH" or "PIPE" or "OEM5" or "OEMPIPE" or "\\" or "|" => 0xDC,
            "CLOSEBRACKET" or "RIGHTBRACKET" or "OEM6" or "OEMCLOSEBRACKETS" or "]" or "}" => 0xDD,
            "QUOTE" or "APOSTROPHE" or "OEM7" or "OEMQUOTES" or "'" or "\"" => 0xDE,
            // Numpad digits
            "NUMPAD0" => 0x60,
            "NUMPAD1" => 0x61,
            "NUMPAD2" => 0x62,
            "NUMPAD3" => 0x63,
            "NUMPAD4" => 0x64,
            "NUMPAD5" => 0x65,
            "NUMPAD6" => 0x66,
            "NUMPAD7" => 0x67,
            "NUMPAD8" => 0x68,
            "NUMPAD9" => 0x69,
            _ => 0
        };

        if (vk != 0) return vk;

        // Try System.Windows.Forms.Keys enum (handles "D0"-"D9", "A"-"Z", "OemQuestion", etc.)
        if (Enum.TryParse<System.Windows.Forms.Keys>(trimmed, true, out var formsKey))
        {
            var code = (ushort)((int)formsKey & 0xFFFF);
            if (code != 0) return code;
        }

        // Single character: use VkKeyScan or direct uppercase ascii
        if (trimmed.Length == 1)
        {
            try
            {
                short scanned = VkKeyScan(trimmed[0]);
                if (scanned != -1)
                {
                    return (ushort)(scanned & 0xFF);
                }
            }
            catch
            {
                // Fallback if P/Invoke fails
            }
            return (ushort)char.ToUpperInvariant(trimmed[0]);
        }

        return 0;
    }

    #region Win32 P/Invoke
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const int INPUT_KEYBOARD = 1;
    private const int INPUT_MOUSE = 0;
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public int type;
        public InputUnion u;
    }
    #endregion
}
