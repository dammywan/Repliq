using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Repliq.Core.Models;
using Repliq.Core.Playback;

namespace Repliq.Windows.Input;

public sealed class Win32InputBackend : IAutomationBackend
{
    private readonly HashSet<ushort> _heldKeys = new();
    private readonly HashSet<uint> _heldMouseButtons = new();
    private readonly object _lock = new();

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

        switch (action.Type.ToLowerInvariant())
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
                return ExecuteClick(action);

            case ActionTypes.TypeText:
                return ExecuteTypeText(action);

            case ActionTypes.KeyPress:
                return ExecuteKeyPress(action);

            case ActionTypes.Scroll:
                return ExecuteScroll(action);

            case ActionTypes.FocusWindow:
                return ExecuteFocus(action);

            case ActionTypes.VerifyElement:
            case ActionTypes.Checkpoint:
                return ActionExecutionResult.Success(action.Id, action.Type, $"Checkpoint / verified step completed.");

            default:
                return ActionExecutionResult.Failure(action.Id, action.Type,
                    $"Unsupported action type '{action.Type}'.", "UNSUPPORTED_ACTION");
        }
    }

    private ActionExecutionResult ExecuteClick(WorkflowAction action)
    {
        int x = 0, y = 0;
        if (action.Target?.SelectorType == "coordinate")
        {
            x = action.Target.X ?? 0;
            y = action.Target.Y ?? 0;
        }
        else if (action.Parameters.TryGetValue("x", out var xObj) && action.Parameters.TryGetValue("y", out var yObj))
        {
            int.TryParse(xObj?.ToString(), out x);
            int.TryParse(yObj?.ToString(), out y);
        }

        string button = "left";
        if (action.Parameters.TryGetValue("button", out var btnObj))
        {
            button = btnObj?.ToString()?.ToLowerInvariant() ?? "left";
        }

        // Move cursor
        SetCursorPos(x, y);
        Thread.Sleep(20);

        uint downFlag = button == "right" ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
        uint upFlag = button == "right" ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;

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

    private ActionExecutionResult ExecuteTypeText(WorkflowAction action)
    {
        string? text = null;
        if (action.Parameters.TryGetValue("text", out var textObj))
            text = textObj?.ToString();

        if (string.IsNullOrEmpty(text))
            return ActionExecutionResult.Success(action.Id, action.Type, "Empty text typed.");

        foreach (char c in text)
        {
            SendUnicodeChar(c);
            Thread.Sleep(10);
        }

        return ActionExecutionResult.Success(action.Id, action.Type, $"Typed {text.Length} characters.");
    }

    private ActionExecutionResult ExecuteKeyPress(WorkflowAction action)
    {
        string? key = null;
        if (action.Parameters.TryGetValue("key", out var kObj))
            key = kObj?.ToString();

        if (string.IsNullOrEmpty(key))
            return ActionExecutionResult.Failure(action.Id, action.Type, "No key specified.", "MISSING_KEY");

        ushort vk = MapKeyToVk(key);
        if (vk == 0)
            return ActionExecutionResult.Failure(action.Id, action.Type, $"Unknown key '{key}'.", "UNKNOWN_KEY");

        lock (_lock) _heldKeys.Add(vk);
        keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
        Thread.Sleep(30);
        keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        lock (_lock) _heldKeys.Remove(vk);

        return ActionExecutionResult.Success(action.Id, action.Type, $"Pressed key '{key}'.");
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

    public Task<bool> VerifyTargetAsync(TargetSelector target, CancellationToken cancellationToken)
    {
        if (target is null)
            return Task.FromResult(true);

        if (!string.IsNullOrWhiteSpace(target.ProcessName))
        {
            var procName = Path.GetFileNameWithoutExtension(target.ProcessName);
            var procs = Process.GetProcessesByName(procName);
            if (procs.Length == 0)
                return Task.FromResult(false);
        }

        if (!string.IsNullOrWhiteSpace(target.WindowTitle))
        {
            var hWnd = FindWindowByTitleSubstring(target.WindowTitle);
            if (hWnd == IntPtr.Zero)
                return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public Task ReleaseInputStateAsync()
    {
        lock (_lock)
        {
            // Release any held mouse buttons
            foreach (var downFlag in _heldMouseButtons)
            {
                uint upFlag = downFlag == MOUSEEVENTF_RIGHTDOWN ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;
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
            var procName = Path.GetFileNameWithoutExtension(target.ProcessName);
            var procs = Process.GetProcessesByName(procName);
            if (procs.Length > 0)
            {
                hWnd = procs[0].MainWindowHandle;
            }
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

    private static ushort MapKeyToVk(string key)
    {
        return key.ToUpperInvariant() switch
        {
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESCAPE" or "ESC" => 0x1B,
            "SPACE" => 0x20,
            "BACKSPACE" or "BACK" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
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
            _ => (ushort)(key.Length == 1 ? char.ToUpperInvariant(key[0]) : 0)
        };
    }

    #region Win32 P/Invoke
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const int INPUT_KEYBOARD = 1;
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }
    #endregion
}
