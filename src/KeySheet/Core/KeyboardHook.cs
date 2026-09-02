using System.Runtime.InteropServices;

namespace KeySheet.Core;

/// <summary>
/// 全局低级键盘钩子（WH_KEYBOARD_LL）。仅监听，绝不吞键（永远 CallNextHookEx）。
/// 必须在带消息循环的线程（WPF UI 线程）上安装。
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private readonly Dictionary<int, bool> _down = new(); // vk -> 是否按下（含自动重复）

    /// <summary>任意键按下或抬起（含重复），参数为虚拟键码。</summary>
    public event Action<int>? KeyDown;
    public event Action<int>? KeyUp;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public bool Install()
    {
        if (_hook != IntPtr.Zero) return true;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _proc,
            NativeMethods.GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    /// <summary>某键当前是否处于按下状态（含按住后的自动重复）。</summary>
    public bool IsDown(int vk) => _down.TryGetValue(vk, out bool d) && d;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            int vk = GetVk(lParam);
            if (vk is > 0 and < 256)
            {
                if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                {
                    // 按住自动重复会连续发 WM_KEYDOWN，仅在首次按下时抛事件
                    if (!_down.ContainsKey(vk)) KeyDown?.Invoke(vk);
                    _down[vk] = true;
                }
                else if (msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                {
                    if (_down.TryGetValue(vk, out bool was) && was) KeyUp?.Invoke(vk);
                    _down[vk] = false;
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static int GetVk(IntPtr lParam)
    {
        // KBDLLHOOKSTRUCT 第一个字段 vkCode
        return Marshal.ReadInt32(lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
