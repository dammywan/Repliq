using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Repliq.Core.Recording;

namespace Repliq.Windows.Recording;

public sealed class Win32RecorderBackend : IRecorderBackend, IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MOUSEWHEEL = 0x020A;

    private const uint EM_GETPASSWORDCHAR = 0x00D2;
    private const int GWL_STYLE = -16;
    private const int ES_PASSWORD = 0x0020;

    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyboardHook = IntPtr.Zero;

    private readonly LowLevelProc _mouseProc;
    private readonly LowLevelProc _keyboardProc;
    private readonly object _hookLock = new();
    private bool _isCapturing = false;

    public event Action<RawRecordedEvent>? RawEventCaptured;

    public Win32RecorderBackend()
    {
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;
    }

    public Task StartCaptureAsync()
    {
        lock (_hookLock)
        {
            if (_isCapturing) return Task.CompletedTask;

            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var hModule = GetModuleHandle(curModule?.ModuleName);

            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hModule, 0);
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, hModule, 0);
            _isCapturing = true;
        }

        return Task.CompletedTask;
    }

    public Task StopCaptureAsync()
    {
        lock (_hookLock)
        {
            if (!_isCapturing) return Task.CompletedTask;

            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }

            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }

            _isCapturing = false;
        }

        return Task.CompletedTask;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isCapturing)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_MOUSEWHEEL)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                string button = msg switch
                {
                    WM_LBUTTONDOWN => "left",
                    WM_RBUTTONDOWN => "right",
                    WM_MBUTTONDOWN => "middle",
                    _ => "none"
                };

                int deltaY = 0;
                if (msg == WM_MOUSEWHEEL)
                {
                    deltaY = (short)((hookStruct.mouseData >> 16) & 0xFFFF);
                }

                var (procName, winTitle, isSensitive) = GetForegroundWindowInfo();

                var rawEvent = new RawRecordedEvent
                {
                    EventType = msg == WM_MOUSEWHEEL ? "scroll" : "click",
                    X = hookStruct.pt.x,
                    Y = hookStruct.pt.y,
                    Button = button,
                    DeltaY = deltaY,
                    ProcessName = procName,
                    WindowTitle = winTitle,
                    IsSensitiveField = isSensitive,
                    TimestampUtc = DateTime.UtcNow
                };

                Task.Run(() => RawEventCaptured?.Invoke(rawEvent));
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isCapturing)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var vk = (System.Windows.Forms.Keys)hookStruct.vkCode;

                var (procName, winTitle, isSensitive) = GetForegroundWindowInfo();

                var modifiers = new List<string>();
                if ((GetKeyState(0x10) & 0x8000) != 0) modifiers.Add("Shift"); // VK_SHIFT
                if ((GetKeyState(0x11) & 0x8000) != 0) modifiers.Add("Ctrl");  // VK_CONTROL
                if ((GetKeyState(0x12) & 0x8000) != 0) modifiers.Add("Alt");   // VK_MENU

                var rawEvent = new RawRecordedEvent
                {
                    EventType = "key",
                    Key = vk.ToString(),
                    Modifiers = modifiers.Count > 0 ? string.Join("+", modifiers) : null,
                    ProcessName = procName,
                    WindowTitle = winTitle,
                    IsSensitiveField = isSensitive,
                    TimestampUtc = DateTime.UtcNow
                };

                Task.Run(() => RawEventCaptured?.Invoke(rawEvent));
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static (string? procName, string? winTitle, bool isSensitive) GetForegroundWindowInfo()
    {
        var hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return (null, null, false);

        var titleSb = new StringBuilder(256);
        GetWindowText(hWnd, titleSb, titleSb.Capacity);
        var title = titleSb.ToString();

        GetWindowThreadProcessId(hWnd, out uint pid);
        string? procName = null;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            procName = proc.ProcessName + ".exe";
        }
        catch { }

        // Sensitive field detection
        bool isSensitive = false;
        var classSb = new StringBuilder(128);
        GetClassName(hWnd, classSb, classSb.Capacity);
        var className = classSb.ToString();

        if (className.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Credentials", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("User Account Control", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Windows Security", StringComparison.OrdinalIgnoreCase))
        {
            isSensitive = true;
        }

        // Check focused control style
        var focusHwnd = GetFocus();
        if (focusHwnd != IntPtr.Zero)
        {
            int style = GetWindowLong(focusHwnd, GWL_STYLE);
            if ((style & ES_PASSWORD) != 0)
            {
                isSensitive = true;
            }

            var pwdChar = SendMessage(focusHwnd, EM_GETPASSWORDCHAR, IntPtr.Zero, IntPtr.Zero);
            if (pwdChar != IntPtr.Zero)
            {
                isSensitive = true;
            }
        }

        return (procName, title, isSensitive);
    }

    public void Dispose()
    {
        StopCaptureAsync().GetAwaiter().GetResult();
    }

    #region Win32 P/Invoke
    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    #endregion
}
