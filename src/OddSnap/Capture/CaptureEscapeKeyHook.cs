using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using OddSnap.Native;

namespace OddSnap.Capture;

internal sealed class CaptureEscapeKeyHook : IDisposable
{
    private readonly Control _target;
    private readonly Action _onEscape;
    private readonly Action? _onSave;
    private readonly Func<bool>? _canHandleEnter;
    private readonly User32.LowLevelKeyboardProc _proc;
    private IntPtr _hook;
    private int _posted;
    private int _savePosted;
    private int _disposed;

    private CaptureEscapeKeyHook(Control target, Action onEscape, Action? onSave, Func<bool>? canHandleEnter)
    {
        _target = target;
        _onEscape = onEscape;
        _onSave = onSave;
        _canHandleEnter = canHandleEnter;
        _proc = HookProc;
    }

    public static CaptureEscapeKeyHook? Install(
        Control target,
        Action onEscape,
        Action? onSave = null,
        Func<bool>? canHandleEnter = null)
    {
        if (target.IsDisposed || !target.IsHandleCreated)
            return null;

        var hook = new CaptureEscapeKeyHook(target, onEscape, onSave, canHandleEnter);
        hook.Install();
        return hook._hook == IntPtr.Zero ? null : hook;
    }

    private void Install()
    {
        IntPtr moduleHandle = IntPtr.Zero;
        try
        {
            string? moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
            if (!string.IsNullOrWhiteSpace(moduleName))
                moduleHandle = Kernel32.GetModuleHandle(moduleName);
        }
        catch
        {
            moduleHandle = IntPtr.Zero;
        }

        _hook = User32.SetWindowsHookEx(User32.WH_KEYBOARD_LL, _proc, moduleHandle, 0);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return User32.CallNextHookEx(_hook, nCode, wParam, lParam);

        if (nCode >= 0 && (wParam == User32.WM_KEYDOWN || wParam == User32.WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == User32.VK_ESCAPE)
            {
                return PostEscape()
                    ? 1
                    : User32.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            bool enterSave = vkCode == 0x0D && (_canHandleEnter?.Invoke() ?? false);
            bool ctrlSave = vkCode == 0x53 && (User32.GetAsyncKeyState(User32.VK_CONTROL) & 0x8000) != 0;
            if (_onSave is not null && (enterSave || ctrlSave))
            {
                return PostSave()
                    ? 1
                    : User32.CallNextHookEx(_hook, nCode, wParam, lParam);
            }
        }

        return User32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool PostSave()
    {
        if (!CanPostToTarget() || _onSave is null)
            return false;

        if (Interlocked.Exchange(ref _savePosted, 1) == 1)
            return true;

        try
        {
            _target.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (CanPostToTarget())
                        _onSave();
                }
                finally
                {
                    Volatile.Write(ref _savePosted, 0);
                }
            }));
            return true;
        }
        catch
        {
            Volatile.Write(ref _savePosted, 0);
            return false;
        }
    }

    private bool PostEscape()
    {
        if (!CanPostToTarget())
            return false;

        if (Interlocked.Exchange(ref _posted, 1) == 1)
            return true;

        try
        {
            _target.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (CanPostToTarget())
                        _onEscape();
                }
                finally
                {
                    Volatile.Write(ref _posted, 0);
                }
            }));
            return true;
        }
        catch
        {
            Volatile.Write(ref _posted, 0);
            return false;
        }
    }

    private bool CanPostToTarget() =>
        Volatile.Read(ref _disposed) == 0 &&
        _target.IsHandleCreated &&
        !_target.IsDisposed &&
        !_target.Disposing;

    public void Dispose()
    {
        Volatile.Write(ref _disposed, 1);
        var hook = Interlocked.Exchange(ref _hook, IntPtr.Zero);
        if (hook != IntPtr.Zero)
            User32.UnhookWindowsHookEx(hook);
    }
}
